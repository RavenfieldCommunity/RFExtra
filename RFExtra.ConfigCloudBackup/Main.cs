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

namespace RFExtra.ConfigCloudBackup;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("com.bepis.bepinex.configurationmanager", BepInDependency.DependencyFlags.HardDependency)]
public class ConfigCloudBackup : BaseUnityPlugin
{
    public static ConfigCloudBackup instance;
    public Harmony harmonyInstance;
    private string _uiOutputMessage;
    private bool _isActionConfirmed = false;
    private bool _hasCloudActionRunning = false;
    private int _totalFileCount = 0;
    private int _processedFileCount = 0;
    private bool _isReadORWriteOnceFinished = false;
    private int _errorFileCount = 0;
    private NextActionAfterRead _nextActionAfterRead = NextActionAfterRead.Backup;
    private bool _isReadyToUpload = false;

    public enum NextActionAfterRead
    {
        Download,
        Backup,
        Upload,
        Delete
    }

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
    private CallResult<RemoteStorageFileReadAsyncComplete_t> callResult_Read;
    private CallResult<RemoteStorageFileWriteAsyncComplete_t> callResult_Write;
    private void Awake()
    {
        instance = this;
        // config ui
        Config.Bind<bool>("UI", "UI", true,
            new ConfigDescription("", null, new ConfigurationManagerAttributes()
            {
                CustomDrawer = (obj) =>
                {
                    GUILayout.EndVertical();
                    GUILayout.Label("TOOL ONLY, ALWAYS BACKUP ON YOUR OWN OCCASIONALLY!!");
                    GUILayout.Label("Some actions need confirm and will auto backup configs locally first");
                    GUILayout.Label(_hasCloudActionRunning
                        ? "Cloud-related action is running"
                        : "Actions relate to Cloud can only run one during once time");
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
                        writer.WriteLine($"CLOUD CONFIG FILE LIST ({SteamRemoteStorage.GetFileCount()} in total):");
                        for (int i = 0; i < SteamRemoteStorage.GetFileCount(); i++)
                        {
                            var configFilename = SteamRemoteStorage
                            .GetFileNameAndSize(i, out var pnFileSizeInBytes);
                            if (SteamRemoteStorage.FilePersisted(configFilename))
                                writer.WriteLine(configFilename + " ~"
                                    + (pnFileSizeInBytes / 1024).ToString("F2")
                                    + "KB");
                        }
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
                    else if (GUILayout.Button("BACKUP: Cloud to Local")
                        && !_hasCloudActionRunning)
                        BackupCloud(NextActionAfterRead.Backup);
                    else if (GUILayout.Button("UPLOAD: Local to Cloud")
                        && !_hasCloudActionRunning)
                        UploadLocalConfigToCloud();
                    else if (GUILayout.Button("DOWNLOAD: Cloud to Local")
                        && !_hasCloudActionRunning)
                    {
                        if (!_isActionConfirmed)
                        {
                            OutputMessage("Action needs to be confirmed");
                            return;
                        }
                        _isActionConfirmed = false;
                        BackupLocal();
                        _nextActionAfterRead = NextActionAfterRead.Download;
                        Sub_ReadFileRelayStart();
                    }
                    else if (GUILayout.Button("REMOVE: Local exists but not on Cloud")
                        && !_hasCloudActionRunning)
                    {
                        BackupCloud(NextActionAfterRead.Delete);
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
            callResult_Read = CallResult<RemoteStorageFileReadAsyncComplete_t>.Create(
                new CallResult<RemoteStorageFileReadAsyncComplete_t>.APIDispatchDelegate(
                    this.Event_OnReadAsyncComplete));
            callResult_Write = CallResult<RemoteStorageFileWriteAsyncComplete_t>.Create(
                new CallResult<RemoteStorageFileWriteAsyncComplete_t>.APIDispatchDelegate(
                    this.Event_OnWriteComplete));
            Directory.CreateDirectory(HASH_GAME_CONFIG_PATH);
            Directory.CreateDirectory(HASH_GAME_CONFIG_BACKUP_PATH);
        }
    }

    private void Update()
    {
        if (_hasCloudActionRunning
            && !callResult_Read.IsActive()
            && _processedFileCount < _totalFileCount
            && _isReadORWriteOnceFinished)
        {
            Logger.LogInfo($"Continue invoke {(_isReadyToUpload
                ? "read" : "write")} index: " + _processedFileCount);
            if (!_isReadyToUpload)
            {
                Sub_ReadFileRelay(_processedFileCount);
            }
            else if (_isReadyToUpload && !callResult_Write.IsActive())
            {
                Sub_WriteFileRelay(_processedFileCount);
            }
            _isReadORWriteOnceFinished = false;
        }
    }
    public void OutputMessage(string msg)
    {
        _uiOutputMessage = msg;
        Logger.LogInfo(msg);
    }
    private void ResetState()
    {
        _isActionConfirmed = false;
        _nextActionAfterRead = NextActionAfterRead.Backup;
        _hasCloudActionRunning = false;
        _totalFileCount = 0;
        _processedFileCount = 0;
        _errorFileCount = 0;
        _currentCloudFilename = "";
        _isReadyToUpload = false;
    }

    public void BackupLocal()
    {
        var hasError = false;
        OutputMessage("Start backing up Local");
        var configDirectory = new DirectoryInfo(HASH_GAME_CONFIG_PATH);
        var timestampString = DateTime.Now
            .ToUniversalTime().ToString(HASH_TIME_FORMAT);
        Directory.CreateDirectory(Path.Combine(HASH_GAME_CONFIG_BACKUP_PATH
            , $"LOCAL_{timestampString}"));
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

    public string _timestampString;
    public string _currentCloudFilename = "";
    public void BackupCloud(NextActionAfterRead action)
    {
        if (SteamRemoteStorage.GetFileCount() == 0
            && action != NextActionAfterRead.Upload)
        {
            OutputMessage("No file is on cloud");
            return;
        }
        if ((action == NextActionAfterRead.Delete
            || action == NextActionAfterRead.Upload)
            && !_isActionConfirmed)
        {
            OutputMessage("Action needs to be confirmed");
            return;
        }
        ResetState();
        _nextActionAfterRead = action;
        Sub_ReadFileRelayStart();
    }

    private void Sub_ReadFileRelayStart()
    {
        _hasCloudActionRunning = true;
        _timestampString = DateTime.Now
            .ToUniversalTime().ToString(HASH_TIME_FORMAT);
        Directory.CreateDirectory(Path.Combine(HASH_GAME_CONFIG_BACKUP_PATH
            , $"{HASH_CLOUD}_{_timestampString}"));
        _totalFileCount = SteamRemoteStorage.GetFileCount();
        if (_processedFileCount < SteamRemoteStorage.GetFileCount())
        {
            Sub_ReadFileRelay(_processedFileCount);
        }
    }

    private void Sub_ReadFileRelay(int index)
    {
        var filename = SteamRemoteStorage.GetFileNameAndSize(index, out var pnFileSizeInBytes);
        var handle = SteamRemoteStorage.FileReadAsync(filename, 0, (uint)pnFileSizeInBytes);
        callResult_Read.Set(handle, null);
        Logger.LogInfo("Read cloud: " + filename);
        _currentCloudFilename = filename;
    }

    private void Sub_WriteFileRelay(int index)
    {
        var files = new DirectoryInfo(HASH_GAME_CONFIG_PATH).GetFiles();
        if (index > files.Count() - 1)
        {
            Logger.LogInfo("Outbound index during writing");
            return;
        }
        var file = files[index];
        var fileData = new byte[file.Length];
        var fileStream = file.OpenRead();
        fileStream.Read(fileData, 0, (int)file.Length);
        callResult_Write.Set(
            SteamRemoteStorage.FileWriteAsync(file.Name, fileData, (uint)file.Length)
            , null);
        fileStream.Dispose();
    }

    /// <summary>
    /// Callback to handle `BackupCloud()` and continue uploading to cloud 
    /// </summary>
    public void Event_OnReadAsyncComplete(RemoteStorageFileReadAsyncComplete_t pCallback
        , bool bIOFailure)
    {
        Logger.LogInfo("OnReadComplete");
        _processedFileCount++;
        _isReadORWriteOnceFinished = true;
        if (pCallback.m_eResult != EResult.k_EResultOK)
        {
            Logger.LogError("Read cloud failed: " + (int)pCallback.m_eResult);
            _errorFileCount++;
            return;
        }
        try
        {
            byte[] pvBuffer = new byte[pCallback.m_cubRead];
            SteamRemoteStorage.FileReadAsyncComplete(pCallback.m_hFileReadAsync
                , pvBuffer, pCallback.m_cubRead);
            string targetPath;
            if (_nextActionAfterRead == NextActionAfterRead.Download)
                targetPath = Path.Combine(HASH_GAME_CONFIG_PATH
                    , _currentCloudFilename);
            else
                targetPath = Path.Combine(HASH_GAME_CONFIG_BACKUP_PATH
                    , $"CLOUD_{_timestampString}"
                    , _currentCloudFilename);

            var file = File.Create(targetPath);
            file.Write(pvBuffer, 0, (int)pCallback.m_cubRead);
            file.Close();
        }
        catch (Exception exception)
        {
            Logger.LogError(exception);
            _errorFileCount++;
        }

        // next action
        if (_processedFileCount >= _totalFileCount)
        {
            OutputMessage("Read Cloud finished");
            DirectoryInfo configDirectory;
            if (_nextActionAfterRead == NextActionAfterRead.Delete)
            {
                configDirectory = new DirectoryInfo(HASH_GAME_CONFIG_PATH);
                for (int i = 0; i < SteamRemoteStorage.GetFileCount(); i++)
                {
                    var cloudFilename = SteamRemoteStorage.GetFileNameAndSize(i, out _);
                    var isFileExist = false;
                    foreach (var localConfigFile in configDirectory.GetFiles())
                    {
                        if (localConfigFile.Name == cloudFilename)
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
                ResetState();
                return;
            }
            else if (!(_nextActionAfterRead == NextActionAfterRead.Upload))
            {
                ResetState();
                return;
            }
            _isReadyToUpload = true;
            configDirectory = new DirectoryInfo(HASH_GAME_CONFIG_PATH);
            _totalFileCount = configDirectory.GetFiles().Count();
            _processedFileCount = 0;
            try
            {
                Sub_WriteFileRelay(0);
            }
            catch (Exception exception)
            {
                _errorFileCount++;
                Logger.LogError(exception);
            }
        }
    }

    public void UploadLocalConfigToCloud()
    {
        OutputMessage("Start upload Local to Cloud");
        BackupCloud(NextActionAfterRead.Upload);
    }

    public void Event_OnWriteComplete(RemoteStorageFileWriteAsyncComplete_t pCallback
        , bool bIOFailure)
    {
        Logger.LogInfo("OnWriteComplete");
        _processedFileCount++;
        _isReadORWriteOnceFinished = true;
        if (pCallback.m_eResult != EResult.k_EResultOK)
        {
            Logger.LogError("Write cloud failed: " + (int)pCallback.m_eResult);
            return;
        }
        if (_processedFileCount >= _totalFileCount)
        {
            OutputMessage("Upload to Cloud finished (RECOMMENDED to check file list and cloud storage occupied amount at Steam if action succeeds)" + (_errorFileCount > 0 ? "with error" : ""));
            ResetState();
        }
    }

    class ConfigurationManagerAttributes
    {
        public Action<object> CustomDrawer;
    }

}