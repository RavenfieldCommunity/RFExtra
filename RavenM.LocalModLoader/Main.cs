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

    // var for outside ref 
    public Harmony harmonyInstance;
    public ConfigEntry<string> remoteModDirectory;
    public ConfigEntry<bool> forceWarningOnLocalModLoader;
    public ConfigEntry<bool> forceFriendOnlyLobby;
    public ConfigEntry<bool> showModfileList;
    /// <summary>
    /// Hotkey of opening Configuration Manager UI
    /// </summary>
    public ConfigEntry<KeyboardShortcut> configUIKeybindConfig;
    /// <summary>
    /// Allow reloading mod now? when in lobby
    /// </summary>
    public bool allowReloadMods = false;
    // runtime var and not for outside ref
    public Traverse _configurationManagerTraverse;
    public const string HASH_LOBBYDATA_MODSIZE_LOCALMODS = "LocalMods";
    public const string HASH_MODLIST_FILENAME = "ravenm_modlist.txt";
    private void Start()
    {
        instance = this;
        logger = Logger;
        // config
        remoteModDirectory = Config.Bind<string>("Config",
            "Remote Mod Directory",
            "RemoteMods",
            "The folder to contain mods that using on RavenM lobby, appears on game root path and mod items should be put on each single sub folder."
                + " Default is the folder in the game path, if the directory is not a absolute path."
                + " Applied after relaunching game.");
        forceWarningOnLocalModLoader = Config.Bind<bool>("Config",
            "Force Warning On LocalModLoader",
            true,
            "Whether pop on a obvious warning when this plugin is enabled");
        forceFriendOnlyLobby = Config.Bind<bool>("Config",
            "Force Friend-only Lobby",
            false,
            "Whether hide the created lobby when LocalModLoader enabled");
        showModfileList = Config.Bind<bool>("Config",
            "Show Modfile List",
            true,
            "Whether show the list of mod file whlie showing modpack list");
        // setup notification
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
        // patcher
        harmonyInstance = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmonyInstance.PatchAll(typeof(Patch));
        // get keybind
        _configurationManagerTraverse = Traverse.Create(
            FindAnyObjectByType<ConfigurationManager.ConfigurationManager>(FindObjectsInactive.Include));
        configUIKeybindConfig = _configurationManagerTraverse.Field("_keybind")
            .GetValue<ConfigEntry<KeyboardShortcut>>();
        // config ui
        Config.Bind<bool>("UI", "UI", true,
            new ConfigDescription("", null, new ConfigurationManagerAttributes()
            {
                CustomDrawer = (obj) =>
                {
                    GUILayout.EndVertical();
                    // button
                    if (GUILayout.Button("Open Current Mod Folder"))
                        Process.Start(ModManager.instance.modStagingPathOverride);
                    // button
                    else if (GUILayout.Button("Open Configured Mod Folder"))
                        Process.Start(remoteModDirectory.Value);
                    // button
                    else if (GUILayout.Button("Disable No Content Mods mode"))
                    {
                        ModManager.instance.noContentMods = false;
                        ModManager.instance.noWorkshopMods = false;
                    }
                    // button
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
                                    if (modfileInfo.Extension == ".rfc" ||
                                        modfileInfo.Extension == ".rfs" ||
                                        modfileInfo.Extension == ".rfl" ||
                                        modfileInfo.Extension == ".rfld")
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
                    // button
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
        // have we once used goto jnp?
        // if yes then dont jnp back again to prevent jnp loop
        var hasGotoOnce = false;
        // process input dir
    pathProcesser:
        if (LocalModLoader.instance.remoteModDirectory.Value.Contains(":")
            || LocalModLoader.instance.remoteModDirectory.Value.Contains("\\")
            || LocalModLoader.instance.remoteModDirectory.Value.Contains("/"))
        {
            ModManager.instance.modStagingPathOverride = LocalModLoader.instance.remoteModDirectory.Value;
        }
        else
        {
            ModManager.instance.modStagingPathOverride = Paths.GameRootPath
                + "\\"
                + LocalModLoader.instance.remoteModDirectory.Value;
        }

        // create dir
        if (!Directory.Exists(ModManager.instance.modStagingPathOverride))
        {
            try
            {
                Directory.CreateDirectory(ModManager.instance.modStagingPathOverride);
            }
            catch (Exception exception)
            {
                LocalModLoader.logger.LogError(exception);
                Traverse.Create(RavenM.Plugin.instance).Field("pluginNotificationText")
                    .SetValue("Invaild remote mod path, resetted.");
                hasGotoOnce = true;
                LocalModLoader.instance.remoteModDirectory.Value =
                    LocalModLoader.instance.remoteModDirectory.DefaultValue as string;
                if (!hasGotoOnce)
                    goto pathProcesser;
            }
        }
    }

    [HarmonyPatch(typeof(LobbySystem), "OnLobbyEnter")]
    [HarmonyPrefix]
    public static void LobbySystem_OnEnterLobby_Prefix()
    {
        // prevent loading workshop-only mods
        LocalModLoader.instance.allowReloadMods = false;
    }

    [HarmonyPatch(typeof(LobbySystem), "OnLobbyEnter")]
    [HarmonyPostfix]
    public static void LobbySystem_OnEnterLobby_Postfix()
    {
        // notification
        LocalModLoader.logger.LogDebug("OnEnterLobby Postfix");
        LocalModLoader.instance.allowReloadMods = true;
        if (LobbySystem.instance.InLobby && LobbySystem.instance.IsLobbyOwner)
        {
            // lobby data 
            LobbySystem.instance.SetLobbyDataDedup("modtotalsize", LocalModLoader.HASH_LOBBYDATA_MODSIZE_LOCALMODS);
            if (LocalModLoader.instance.forceFriendOnlyLobby.Value)
            {
                SteamMatchmaking.SetLobbyType(
                    LobbySystem.instance.ActualLobbyID, ELobbyType.k_ELobbyTypeFriendsOnly);
                ChatManager.instance.PushLobbyChatMessage("Steam friends only");
            }
        }
        ChatManager.instance.PushLobbyChatMessage(
            $"Your are using the LocalModLoader, check more by pressing `{LocalModLoader.instance.configUIKeybindConfig.Value}`");
        // maunal reload
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
        // prevent reloading
        if (LobbySystem.instance != null &&
            LobbySystem.instance.InLobby &&
            !LocalModLoader.instance.allowReloadMods)
            return false;
        else
            return true;
    }
}
