using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using RavenM;
using System.Reflection;
using System;
using System.IO;
using System.Diagnostics;
using BepInEx.Logging;
using UnityEngine.SceneManagement;
using Steamworks;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace RavenM.IMECharFix;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("RavenM", BepInDependency.DependencyFlags.HardDependency)]
public class IMECharFix : BaseUnityPlugin
{
    public static IMECharFix instance;
    public static ManualLogSource logger;

    // var for outside ref 
    public Harmony harmonyInstance;
    public ConfigEntry<bool> selfEnabled;
    public ConfigEntry<bool> ctrlEnterToSend;
    public ConfigEntry<int> fieldPosY;
    public ConfigEntry<int> fieldPosX;
    public ConfigEntry<float> fieldScale;
    public ConfigEntry<KeyboardShortcut> globalChatKeybind;
    public ConfigEntry<KeyboardShortcut> teamChatKeybind;
    // runtime var and not for outside ref
    private bool _isInited = false;
    private bool _showField = false;
    private bool _isGlobalMessage = false;
    private bool _isCNPlayer = false;
    internal AssetBundle uiBundle;
    private AssetBundleCreateRequest _uiBundleLoader;
    internal GameObject uiObjectInstance;
    internal InputField uiInputField;
    internal Text uiIndicatorText;
    private readonly KeyboardShortcut escKeybind = new KeyboardShortcut(KeyCode.Escape);
    private readonly KeyboardShortcut enterKeybind = new KeyboardShortcut(KeyCode.Return);
    private readonly KeyboardShortcut leftCtrlEnterKeybind = new KeyboardShortcut(KeyCode.Return,[KeyCode.LeftControl]);
    private readonly KeyboardShortcut rightCtrlEnterKeybind = new KeyboardShortcut(KeyCode.Return,[KeyCode.RightControl]);
    private void Start()
    {
        instance = this;
        logger = Logger;
        _isCNPlayer = System.Threading.Thread.CurrentThread.CurrentCulture.Name == "zh-CN";

        // config
        selfEnabled = Config.Bind("Config",
            "Enabled",
            true,
            _isCNPlayer ? "是否启用插件" : "Plugin feature is enabled?");
        ctrlEnterToSend = Config.Bind("Config",
            "Ctrl and Enter to send message",
            false,
            _isCNPlayer ? "Ctrl + Enter 发送消息" : "");
        globalChatKeybind = Config.Bind("Config",
            "Global Chat Keybind",
            new KeyboardShortcut(KeyCode.Y),
            _isCNPlayer ? "全局消息按键" : "");
        teamChatKeybind = Config.Bind("Config",
            "Team Chat Keybind",
            new KeyboardShortcut(KeyCode.U),
            _isCNPlayer ? "队伍消息按键" : "");
        // res
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            Assembly.GetExecutingAssembly().GetManifestResourceNames()[0]);
        _uiBundleLoader = AssetBundle.LoadFromStreamAsync(stream);
        _uiBundleLoader.completed += (asyncOperation) =>
            {
                uiBundle = _uiBundleLoader.assetBundle;
                _isInited = true;
            };
        // patcher
        harmonyInstance = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmonyInstance.PatchAll(typeof(Patch));
    }

    private void Update()
    {
        if (_isInited
            && LobbySystem.instance != null
            && LobbySystem.instance.InLobby == true
            && selfEnabled.Value
            && uiInputField != null)
        {
            ChatManager.instance.TypeIntention = false;
            ChatManager.instance.JustFocused = false;

            if (teamChatKeybind.Value.IsDown()
                || globalChatKeybind.Value.IsDown()
                )
            {
                if (_showField)
                    Event.current.Use();
                IMECharFix.logger.LogDebug("Chat keybind pressed");
                _showField = true;
                _isGlobalMessage = globalChatKeybind.Value.IsDown();
            }

            if (uiInputField.gameObject.activeSelf != _showField)
                uiInputField.gameObject.SetActive(_showField);

            if (_showField)
            {
                uiInputField.Select();
                uiIndicatorText.text = _isGlobalMessage ?
                    (_isCNPlayer ? "全局" : "GLOBAL")
                    : (_isCNPlayer ? "队伍" : "TEAM");
            }

            if (escKeybind.IsDown())
                _showField = false;
            if (ctrlEnterToSend.Value ? leftCtrlEnterKeybind.IsDown() || rightCtrlEnterKeybind.IsDown()
                : enterKeybind.IsDown())
            {
                _showField = false;
                // from ravenm lol
                if (!string.IsNullOrEmpty(uiInputField.text))
                {
                    string textProcessed = uiInputField.text.Trim();
                    if ((textProcessed.StartsWith("/") | textProcessed.StartsWith("、")) ? true : false)
                    {
                        ChatManager.instance.ProcessCommand(
                            uiInputField.text.Replace("、", "/")
                            , ChatManager.instance.SteamId.m_SteamID, local: true);
                    }
                    else
                    {
                        if (!IngameNetManager.instance.IsClient)
                        {
                            ChatManager.instance.PushLobbyChatMessage(
                                uiInputField.text, ChatManager.instance.SteamUsername);
                            ChatManager.instance.SendLobbyChat(uiInputField.text);
                        }
                        else
                        {
                            ChatManager.instance.PushChatMessage(ActorManager.instance.player
                            , uiInputField.text
                            , _isGlobalMessage, GameManager.PlayerTeam());
                            using MemoryStream memoryStream = new MemoryStream();
                            ChatPacket value = new ChatPacket
                            {
                                Id = ActorManager.instance.player.GetComponent<GuidComponent>().guid,
                                Message = uiInputField.text,
                                TeamOnly = !_isGlobalMessage
                            };
                            using (ProtocolWriter protocolWriter = new ProtocolWriter(memoryStream))
                            {
                                protocolWriter.Write(value);
                            }

                            byte[] data = memoryStream.ToArray();
                            IngameNetManager.instance.SendPacketToServer(data, PacketType.Chat, 8);
                        }
                    }
                }
                uiInputField.text = "";
                LoadoutUi.Hide();
            }
        }
    }
}

[HarmonyPatch]
public static class Patch
{
    [HarmonyPatch(typeof(LobbySystem), "OnLobbyEnter")]
    [HarmonyPostfix]
    public static void LobbySystem_OnLobbyEnter()
    {
        try
        {
            if (IMECharFix.instance.uiObjectInstance == null)
            {
                IMECharFix.logger.LogDebug("Instantiate input field");
                IMECharFix.instance.uiObjectInstance = GameObject.Instantiate(
                    IMECharFix.instance.uiBundle.LoadAsset(
                        IMECharFix.instance.uiBundle.GetAllAssetNames()[0])) as GameObject;
                GameObject.DontDestroyOnLoad(IMECharFix.instance.uiObjectInstance);
                IMECharFix.instance.uiObjectInstance.layer = 10;
                IMECharFix.instance.uiInputField =
                    IMECharFix.instance.uiObjectInstance.GetComponentInChildren<InputField>();
                IMECharFix.instance.uiInputField.gameObject.SetActive(false);
                foreach (var text in IMECharFix.instance.uiInputField.gameObject.GetComponentsInChildren<Text>())
                {
                    if (text.gameObject.name.Contains("Indicator"))
                        IMECharFix.instance.uiIndicatorText = text;
                }
            }
        }
        catch (Exception exception)
        {
            IMECharFix.logger.LogError(exception);
        }
    }
}