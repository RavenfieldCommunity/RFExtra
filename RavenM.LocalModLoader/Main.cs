using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RavenM.DiscordGameSDK;
using ConfigurationManager;
using UnityEngine;
using System.Reflection;
using System;
using System.IO;
using System.Diagnostics;
using BepInEx.Logging;
using UnityEngine.SceneManagement;
using Steamworks;
using System.Collections.Generic;

namespace RavenM.LocalModLoader;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("RavenM", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.bepis.bepinex.configurationmanager", BepInDependency.DependencyFlags.HardDependency)]
public class LocalModLoader : BaseUnityPlugin
{
    public static LocalModLoader instance;
    public static ManualLogSource logger;
    public Harmony harmonyInstance;
    public ConfigEntry<string> remoteModDirectoryName;
    public ConfigEntry<bool> forceWarningOnLocalModLoader;
    public ConfigEntry<bool> forceHiddenLobby;
    public ConfigEntry<bool> showModfileList;
    public ConfigEntry<KeyboardShortcut> configUIKeybindConfig;
    public bool allowReloadMods = false;
    public Traverse _configurationManagerTraverse;
    public const string HASH_LOBBYDATA_MODSIZE_LOCALMODS = "LocalMods";
    public const string HASH_MODLIST_FILENAME = "ravenm_modlist.txt";
    private void Start()
    {
        instance = this;
        logger = Logger;
        remoteModDirectoryName = Config.Bind<string>("Config",
            "Remote Mod Directory Name",
            "RemoteMods",
            "The folder name to contain mods that using on RavenM lobby, appears on game root path and mod items should be put on each single sub folder");
        forceWarningOnLocalModLoader = Config.Bind<bool>("Config",
            "Force Warning On LocalModLoader",
            true,
            "Whether pop on a obvious warning when this plugin is enabled");
        forceHiddenLobby = Config.Bind<bool>("Config",
            "Force Hidden Lobby",
            true,
            "Whether hide the created lobby when LocalModLoader enabled");
        showModfileList = Config.Bind<bool>("Config",
            "Show Modfile List",
            true,
            "Whether show the list of mod file whlie showing modpack list");
        var textInstance = Traverse.Create(RavenM.Plugin.instance).Field("pluginNotificationText");
        if (forceWarningOnLocalModLoader.Value)
        {
            if (Environment.CommandLine.Contains(" -nolocalmodloader"))
            {
                textInstance.SetValue("LocalModLoader is disabled by the launch argument");
                this.enabled = false;
                return;
            }
            else
                textInstance.SetValue("LocalModLoader is enabled");
        }
        harmonyInstance = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmonyInstance.PatchAll(typeof(Patch));
        _configurationManagerTraverse = Traverse.Create(
            FindAnyObjectByType<ConfigurationManager.ConfigurationManager>(FindObjectsInactive.Include));
        configUIKeybindConfig = _configurationManagerTraverse.Field("_keybind")
            .GetValue<ConfigEntry<KeyboardShortcut>>();
        Config.Bind<bool>("UI", "UI", true,
            new ConfigDescription("", null, new ConfigurationManagerAttributes()
            {
                CustomDrawer = (obj) =>
                {
                    GUILayout.EndVertical();
                    if (GUILayout.Button("Open Mod Folder"))
                        Process.Start(ModManager.instance.modStagingPathOverride);
                    else if (GUILayout.Button("Open Mod List"))
                    {
                        ChatManager.instance.PushLobbyChatMessage("Open local mod list");
                        var modlistFilePath = Paths.GameRootPath + "\\" + HASH_MODLIST_FILENAME;
                        if (File.Exists(modlistFilePath))
                            File.Delete(modlistFilePath);
                        var writer = File.CreateText(modlistFilePath);
                        var directories = Directory.GetDirectories(ModManager.instance.modStagingPathOverride);
                        writer.WriteLine($"LOCAL MODPACK LIST({directories.Length} in total):");
                        List<string> modfileList = [];
                        foreach (var modpackDirectory in directories)
                        {
                            var packInfo = new DirectoryInfo(modpackDirectory);
                            writer.WriteLine(packInfo.Name);
                            if (showModfileList.Value)
                                foreach (var modfileInfo in packInfo.GetFiles())
                                {
                                    if (modfileInfo.Extension == "rfc" ||
                                        modfileInfo.Extension == "rfs" ||
                                        modfileInfo.Extension == "rfl" ||
                                        modfileInfo.Extension == "rfld")
                                        modfileList.Add(modfileInfo.Name);
                                }
                        }
                        if (showModfileList.Value)
                        {
                            modfileList.Sort();
                            writer.WriteLine($"######\nLOCAL MODFILE LIST({modfileList.Count} in total):");
                            foreach (var modfileName in modfileList)
                                writer.WriteLine(modfileName);
                        }
                        writer.Close();
                        Process.Start(modlistFilePath);
                    }
                    else if (GUILayout.Button("Reload Mods"))
                    {
                        if (ModManager.instance.contentHasFinishedLoading)
                        {
                            ChatManager.instance.PushLobbyChatMessage("Reload mods");
                            ModManager.instance.ReloadMods();
                        }
                        else
                            ChatManager.instance.PushLobbyChatMessage("Reloading mods is not allowed while loading mods");
                    }
                    GUILayout.BeginVertical();
                }
            }));
    }

    // the most foolish thingy i have ever done for ConfigurationManager
    class ConfigurationManagerAttributes
    {
        public Action<object> CustomDrawer;
    }
}

[HarmonyPatch]
public static class Patch
{
    [HarmonyPatch(typeof(NoCustommodsPatch), "Prefix")]
    [HarmonyPostfix]
    public static void NoCustommodsPatch_OnGameManagerStart()
    {
        ModManager.instance.modStagingPathOverride = Paths.GameRootPath +
          "\\" +
          LocalModLoader.instance.remoteModDirectoryName.Value;
        if (!Directory.Exists(ModManager.instance.modStagingPathOverride))
            Directory.CreateDirectory(ModManager.instance.modStagingPathOverride);
    }

    [HarmonyPatch(typeof(LobbySystem), "OnLobbyEnter")]
    [HarmonyPrefix]
    public static void LobbySystem_OnEnterLobby_Prefix()
    {
        LocalModLoader.instance.allowReloadMods = false;
    }

    [HarmonyPatch(typeof(LobbySystem), "OnLobbyEnter")]
    [HarmonyPostfix]
    public static void LobbySystem_OnEnterLobby_Postfix()
    {
        LocalModLoader.logger.LogDebug("OnEnterLobby Postfix");
        LocalModLoader.instance.allowReloadMods = true;
        if (LobbySystem.instance.InLobby && LobbySystem.instance.IsLobbyOwner)
        {
            LobbySystem.instance.SetLobbyDataDedup("modtotalsize", LocalModLoader.HASH_LOBBYDATA_MODSIZE_LOCALMODS);
            if (LocalModLoader.instance.forceHiddenLobby.Value)
            {
                SteamMatchmaking.SetLobbyType(
                    LobbySystem.instance.ActualLobbyID, ELobbyType.k_ELobbyTypeFriendsOnly);
                ChatManager.instance.PushLobbyChatMessage("Steam friends only");
            }
        }
        ChatManager.instance.PushLobbyChatMessage(
            $"Your are using the LocalModLoader, check more by pressing `{LocalModLoader.instance.configUIKeybindConfig.Value}`");
        if (LobbySystem.instance.ModsToDownload.Count == 0)
        {
            ChatManager.instance.PushLobbyChatMessage("Reload mods");
            ModManager.instance.ReloadMods();
        }
    }

    [HarmonyPatch(typeof(ModManager), nameof(ModManager.ReloadModContent))]
    [HarmonyPrefix]
    public static bool ModManager_ReloadModContent()
    {
        if (LobbySystem.instance != null &&
            LobbySystem.instance.InLobby &&
            !LocalModLoader.instance.allowReloadMods)
            return false;
        else
            return true;
    }
}
