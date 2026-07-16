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
    public ConfigEntry<KeyboardShortcut> openFolderKeybindConfig;
    public ConfigEntry<KeyboardShortcut> reloadModsKeybindConfig;
    public ConfigEntry<KeyboardShortcut> modListKeybindConfig;

    public Traverse configurationManagerTraverse;
    public ConfigEntry<KeyboardShortcut> configUIKeybindConfig;
    public bool allowReloadMods = false;
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
        openFolderKeybindConfig = Config.Bind("Config",
            "Open Mod Folder Keybind",
            new KeyboardShortcut(KeyCode.O, [KeyCode.LeftAlt]),
            "");
        reloadModsKeybindConfig = Config.Bind("Config",
            "Reload Mods Keybind",
            new KeyboardShortcut(KeyCode.N, [KeyCode.LeftAlt]),
            "");
        modListKeybindConfig = Config.Bind("Config",
            "Mod List Action Keybind",
            new KeyboardShortcut(KeyCode.M, [KeyCode.LeftAlt]),
            "");
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
        configurationManagerTraverse = Traverse.Create(FindAnyObjectByType<ConfigurationManager.ConfigurationManager>(FindObjectsInactive.Include));
        configUIKeybindConfig = configurationManagerTraverse.Field("_keybind").GetValue<ConfigEntry<KeyboardShortcut>>();
    }
    private void Update()
    {
        if (openFolderKeybindConfig.Value.IsDown())
        {
            logger.LogDebug("Pressed open folder");
            Process.Start(ModManager.instance.modStagingPathOverride);
        }
        else if (LobbySystem.instance.InLobby)
        {
            if (reloadModsKeybindConfig.Value.IsDown())
            {
                if (ModManager.instance.contentHasFinishedLoading)
                {
                    ChatManager.instance.PushLobbyChatMessage("Reload mods");
                    ModManager.instance.ReloadMods();
                }
                else
                    ChatManager.instance.PushLobbyChatMessage("Reloading mods is not allowed while loading mods");
            }
            else if (modListKeybindConfig.Value.IsDown())
            {
                ChatManager.instance.PushLobbyChatMessage("Open local mod list");
                var modlistFilePath = Paths.GameRootPath + "\\" + HASH_MODLIST_FILENAME;
                if (File.Exists(modlistFilePath))
                    File.Delete(modlistFilePath);
                var writer = File.CreateText(modlistFilePath);
                var directories = Directory.GetDirectories(ModManager.instance.modStagingPathOverride);
                writer.WriteLine($"LOCAL MOD LIST({directories.Length} in total):");
                foreach (var modDirectory in directories)
                {
                    var info = new DirectoryInfo(modDirectory);
                    writer.WriteLine(info.Name);
                }
                writer.Close();
                Process.Start(modlistFilePath);
            }
        }

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
                SteamMatchmaking.SetLobbyType(LobbySystem.instance.ActualLobbyID, ELobbyType.k_ELobbyTypeFriendsOnly);
                ChatManager.instance.PushLobbyChatMessage("Steam friends only");
            }
        }
        ChatManager.instance.PushLobbyChatMessage($"Your are using the LocalModLoader, check config for configs by pressing `{LocalModLoader.instance.configUIKeybindConfig.Value}`");
        if (LobbySystem.instance.IsLobbyOwner)
        {
            ChatManager.instance.PushLobbyChatMessage($"Press `{LocalModLoader.instance.modListKeybindConfig.Value}` export local mod list for the client's check");
            ChatManager.instance.PushLobbyChatMessage($"Press `{LocalModLoader.instance.reloadModsKeybindConfig.Value}` to reload mod");
        }
        else
        {
            ChatManager.instance.PushLobbyChatMessage($"Press `{LocalModLoader.instance.modListKeybindConfig.Value}` to check local mod list");
            ChatManager.instance.PushLobbyChatMessage($"Once you are sure you have installed all mods requested by thee host, press `{LocalModLoader.instance.reloadModsKeybindConfig.Value}` to load mods for game");
        }
        if (LobbySystem.instance.ModsToDownload.Count == 0)
            ModManager.instance.ReloadMods();
    }

    [HarmonyPatch(typeof(ModManager), nameof(ModManager.ReloadModContent))]
    [HarmonyPrefix]
    public static bool ModManager_ReloadModContent()
    {
        if (LobbySystem.instance != null && LobbySystem.instance.InLobby && !LocalModLoader.instance.allowReloadMods)
            return false;
        else
            return true;
    }
}
