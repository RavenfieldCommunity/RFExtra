using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using System.Linq;
using UnityEngine.XR;
using BepInEx.Logging;
using System.Threading.Tasks;

namespace RFExtra.ConfigCloudBackup;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("com.bepis.bepinex.configurationmanager", BepInDependency.DependencyFlags.HardDependency)]
public class ConfigCloudBackup : BaseUnityPlugin
{
    public static ConfigCloudBackup instance;
    internal static ManualLogSource logger;
    public Harmony harmonyInstance;
    private string _uiOutputMessage;
    private bool _isActionConfirmed = false;


    public readonly string HASH_GAME_CONFIG_PATH
        = Path.Combine(Application.platform == RuntimePlatform.WindowsPlayer ?
                Application.persistentDataPath.Replace("/", "\\") : Application.persistentDataPath
            , "GameConfigurations");
    public readonly string HASH_GAME_CONFIG_BACKUP_PATH
        = Path.Combine(Application.platform == RuntimePlatform.WindowsPlayer ?
                Application.persistentDataPath.Replace("/", "\\") : Application.persistentDataPath
            , "RFExtra", "GameConfigurationBackups");
    public readonly string HASH_CLOUD_CONFIG_LIST_FILENAME = "rfextra_cloudconfiglist.txt";
    public const string HASH_TIME_FORMAT = "yyyy-MM-dd-HH-mm-ss";
    public const string HASH_LOCAL = "LOCAL";
    public const string HASH_CLOUD = "CLOUD";
    public const string HASH_OUTPUT_NEEDCOMFIRM = "Action needs comfirm";
    public ConfigEntry<string> lastBackupTime;
    public ConfigEntry<bool> enableAutoUpload;
    public ConfigEntry<bool> backupBeforeImportantActions;
    public ConfigEntry<bool> enableAutoBackup;
    private void Awake()
    {
        instance = this;
        logger = Logger;
        enableAutoUpload = Config.Bind("Config"
            , "Enable Auto Upload", true
            , "Auto upload local files to Cloud when entering map");
        enableAutoBackup = Config.Bind("Config"
            , "Enable Auto Backup", false
            , "Auto backup local files to local backup directory weekly when launching game");
        backupBeforeImportantActions = Config.Bind("Config"
            , "Backup Before Important Actions", true
            , "Backup files to local backup directory before important actions which need confirm");
        lastBackupTime = Config.Bind("Cache"
            , "lastBackupTime", ""
            , new ConfigDescription(""
                , tags: new ConfigurationManagerAttributes() { IsAdvanced = true }));
        // config ui
        Config.Bind<bool>("UI", "UI", true,
            new ConfigDescription("", null, new ConfigurationManagerAttributes()
            {
                CustomDrawer = (obj) =>
                {
                    GUILayout.EndVertical();
                    GUILayout.Label("TOOL ONLY, ALWAYS BACKUP ON YOUR OWN OCCASIONALLY!!");
                    _isActionConfirmed = GUILayout.Toggle(_isActionConfirmed, "CONFIRM ACTION?");
                    GUILayout.TextArea(_uiOutputMessage);
                    // button
                    if (GUILayout.Button("Get cloud file list"))
                    {
                        var listFilePath = Path.Combine(Paths.GameRootPath
                            , HASH_CLOUD_CONFIG_LIST_FILENAME);
                        if (File.Exists(listFilePath))
                            File.Delete(listFilePath);
                        var writer = File.CreateText(listFilePath);
                        int realCount = 0;
                        for (int i = 0; i < SteamRemoteStorage.GetFileCount(); i++)
                        {
                            var configFilename = SteamRemoteStorage
                            .GetFileNameAndSize(i, out var pnFileSizeInBytes);
                            if (SteamRemoteStorage.FilePersisted(configFilename))
                            {
                                realCount++;
                                writer.WriteLine(configFilename + " ~"
                                    + (pnFileSizeInBytes / 1024).ToString("F2")
                                    + "KB");
                            }
                        }
                        writer.WriteLine("#####");
                        writer.WriteLine($"CLOUD CONFIG FILE LIST ({realCount} in total)");
                        writer.Close();
                        Process.Start(listFilePath);
                        OutputMessage("List is got");
                    }
                    else if (GUILayout.Button("Open backup directory"))
                    {
                        Process.Start(HASH_GAME_CONFIG_BACKUP_PATH);
                    }
                    else if (GUILayout.Button("Open log"))
                        Process.Start(Path.Combine(Paths.BepInExRootPath, "LogOutput.log"));
                    else if (GUILayout.Button("BACKUP: Local to Local"))
                        BackupLocal();
                    else if (GUILayout.Button("BACKUP: Cloud to Local"))
                        Task.Run(() =>
                        {
                            DownloadFromCloud(isBackup: true);
                        });
                    else if (GUILayout.Button("UPLOAD: Local to Cloud"))
                        Task.Run(() =>
                        {
                            if (!_isActionConfirmed)
                            {
                                OutputMessage(HASH_OUTPUT_NEEDCOMFIRM);
                                return;
                            }
                            _isActionConfirmed = false;
                            if (backupBeforeImportantActions.Value)
                                DownloadFromCloud(isBackup: true);
                            UploadToCloud();
                        });
                    else if (GUILayout.Button("DOWNLOAD: Cloud to Local"))
                    {
                        Task.Run(() =>
                        {
                            if (!_isActionConfirmed)
                            {
                                OutputMessage(HASH_OUTPUT_NEEDCOMFIRM);
                                return;
                            }
                            _isActionConfirmed = false;
                            if (backupBeforeImportantActions.Value)
                                BackupLocal();
                            DownloadFromCloud(isBackup: false);
                        });
                    }
                    else if (GUILayout.Button("REMOVE: Local exists but not on Cloud"))
                    {
                        Task.Run(() =>
                        {
                            if (!_isActionConfirmed)
                            {
                                OutputMessage(HASH_OUTPUT_NEEDCOMFIRM);
                                return;
                            }
                            _isActionConfirmed = false;
                            if (backupBeforeImportantActions.Value)
                                DownloadFromCloud(isBackup: true);
                            var configDirectory = new DirectoryInfo(HASH_GAME_CONFIG_PATH);
                            for (int i = 0; i < SteamRemoteStorage.GetFileCount(); i++)
                            {
                                var cloudFilename = SteamRemoteStorage.GetFileNameAndSize(i, out _);
                                var isFileExist = false;
                                foreach (var localConfigFile in configDirectory.GetFiles())
                                {
                                    if (SteamRemoteStorage.FilePersisted(cloudFilename)
                                        && localConfigFile.Name == cloudFilename)
                                    {
                                        isFileExist = true;
                                        break;
                                    }
                                }
                                if (!isFileExist)
                                {
                                    Logger.LogInfo("Remove: " + cloudFilename);
                                    try
                                    {
                                        SteamRemoteStorage.FileForget(cloudFilename);
                                    }
                                    catch (Exception exception)
                                    {
                                        Logger.LogError(exception);
                                    }
                                }
                            }
                            OutputMessage("Delete file finished");
                        });
                    }
                    GUILayout.BeginVertical();
                }
            }));
        SteamAPI.Init();
        if (!SteamManager.Initialized)
        {
            OutputMessage("Steam is not connected");
            return;
        }
        else
        {
            OutputMessage($"AppId: {SteamUtils.GetAppID()}");
            if (!SteamRemoteStorage.IsCloudEnabledForApp() || !SteamRemoteStorage.IsCloudEnabledForApp())
            {
                OutputMessage("Enable cloud storage for account or game on Steam!");
                SteamRemoteStorage.SetCloudEnabledForApp(true);
            }
            Directory.CreateDirectory(HASH_GAME_CONFIG_PATH);
            Directory.CreateDirectory(HASH_GAME_CONFIG_BACKUP_PATH);
        }
    }

    private void Start()
    {
        if (enableAutoBackup.Value
            && (lastBackupTime.Value == ""
            || DateTime.Now - DateTime.Parse(lastBackupTime.Value)
                > new TimeSpan(7, 0, 0, 0)))
        {
            Logger.LogInfo("Time for backing up local");
            BackupLocal();
            lastBackupTime.Value = DateTime.Now.ToString();
        }
        else
        {
            Logger.LogInfo("No startup backup");
        }
        harmonyInstance = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmonyInstance.PatchAll(typeof(Patch));
    }

    /// <summary>
    /// Show message to players on IMGUI and log it to log
    /// </summary>
    /// <param name="msg"></param>
    public void OutputMessage(string msg)
    {
        _uiOutputMessage = msg;
        Logger.LogInfo(msg);
    }

    public void BackupLocal()
    {
        var hasError = false;
        OutputMessage("Start backing up Local");
        var configDirectory = new DirectoryInfo(HASH_GAME_CONFIG_PATH);
        var timestampString = DateTime.Now
            .ToUniversalTime().ToString(HASH_TIME_FORMAT);
        Directory.CreateDirectory(Path.Combine(HASH_GAME_CONFIG_BACKUP_PATH
            , $"{HASH_LOCAL}_{timestampString}"));
        foreach (var configFile in configDirectory.GetFiles())
        {
            try
            {
                if (configFile.Extension == ".rgc"
                    || configFile.Extension == ".xml")
                    configFile.CopyTo(Path.Combine(HASH_GAME_CONFIG_BACKUP_PATH
                        , $"{HASH_LOCAL}_{timestampString}"
                        , configFile.Name));
            }
            catch (Exception exception)
            {
                hasError = true;
                Logger.LogError(exception);
            }
        }
        OutputMessage($"Local is backed up {(hasError ? "with error" : "")}");
    }

    public void DownloadFromCloud(bool isBackup)
    {
        OutputMessage($"Start {(isBackup
            ? "backing up" : "downloading from")} Cloud");
        var timestampString = DateTime.Now
            .ToUniversalTime().ToString(HASH_TIME_FORMAT);
        Directory.CreateDirectory(Path.Combine(HASH_GAME_CONFIG_BACKUP_PATH
            , $"{HASH_CLOUD}_{timestampString}"));
        for (int i = 0; i < SteamRemoteStorage.GetFileCount(); i++)
        {
            var filename = SteamRemoteStorage
                .GetFileNameAndSize(i, out var pnFileSizeInBytes);
            if (SteamRemoteStorage.FilePersisted(filename))
            {
                Logger.LogInfo("Read cloud: " + filename);
                byte[] pvBuffer = new byte[pnFileSizeInBytes];
                try
                {
                    SteamRemoteStorage.FileRead(filename, pvBuffer, pnFileSizeInBytes);
                    string targetPath;
                    if (isBackup)
                        targetPath = Path.Combine(HASH_GAME_CONFIG_BACKUP_PATH
                            , $"{HASH_CLOUD}_{timestampString}"
                            , filename);
                    else
                        targetPath = Path.Combine(HASH_GAME_CONFIG_PATH
                            , filename);
                    var file = File.Create(targetPath);
                    file.Write(pvBuffer, 0, pnFileSizeInBytes);
                    file.Close();
                }
                catch (Exception exception)
                {
                    Logger.LogError(exception);
                }
            }
            else
            {
                Logger.LogInfo("Read cancel cloud: " + filename);
            }
        }
        OutputMessage("Action reading from cloud finished");
    }

    public void UploadToCloud()
    {
        OutputMessage($"Start uploading");
        var configDirectory = new DirectoryInfo(HASH_GAME_CONFIG_PATH);
        var hasError = false;
        foreach (var file in configDirectory.GetFiles())
        {
            Logger.LogInfo("Upload file:" + file.Name);
            try
            {
                var fileData = new byte[file.Length];
                var fileStream = file.OpenRead();
                fileStream.Read(fileData, 0, (int)file.Length);
                SteamRemoteStorage.FileWrite(file.Name, fileData, (int)file.Length);
                fileStream.Dispose();
            }
            catch (Exception exception)
            {
                hasError = true;
                Logger.LogError(exception);
            }
        }
        OutputMessage("Upload finished "
            + (hasError ? "with error" : ""));
    }

    class ConfigurationManagerAttributes
    {
        public Action<object> CustomDrawer;

        public bool IsAdvanced = false;
    }

}

[HarmonyPatch]
public static class Patch
{
    [HarmonyPatch(typeof(InstantActionConfigMenu)
        , nameof(InstantActionConfigMenu.StartGame))]
    [HarmonyPostfix]
    public static void InstantActionConfigMenu_StartGame()
    {
        if (ConfigCloudBackup.instance.enableAutoUpload.Value)
        {
            try
            {
                ConfigCloudBackup.logger.LogInfo("Start uploading when start game");
            }
            catch (Exception exception)
            {
                ConfigCloudBackup.logger.LogError(exception);
            }
        }
    }
}