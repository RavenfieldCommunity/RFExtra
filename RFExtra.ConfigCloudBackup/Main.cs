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
public class ConfigCloudBackup : BaseUnityPlugin
{
    public static ConfigCloudBackup instance;
    public Harmony harmonyInstance;
    private string _uiOutputMessage;
    private bool _isActionConfirmed = false;

    public readonly string HASH_GAME_CONFIG_PATH = Path.Combine(Path.GetDirectoryName(Application.persistentDataPath)
        , "GameConfigurations");
    public readonly string HASH_GAME_CONFIG_BACKUP_PATH = Path.Combine(Path.GetDirectoryName(Application.persistentDataPath)
        , "RFExtra", "GameConfigurationBackups");
    public readonly string HASH_CLOUD_CONFIG_LIST_FILENAME = "rfextra_cloudconfiglist.txt";
    public const string HASH_TIME_FORMAT = "yyyy-MM-dd-HH-mm-ss";
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
                    _isActionConfirmed = GUILayout.Toggle(_isActionConfirmed, "CONFIRM ACTION?");
                    GUILayout.TextArea(_uiOutputMessage);
                    // button
                    if (GUILayout.Button("Get cloud file list"))
                    {
                        var listFilePath = Paths.GameRootPath + "\\" + HASH_CLOUD_CONFIG_LIST_FILENAME;
                        if (File.Exists(listFilePath))
                            File.Delete(listFilePath);
                        var writer = File.CreateText(listFilePath);
                        writer.WriteLine($"CLOUD CONFIG FILE LIST ({SteamRemoteStorage.GetFileCount()} in total):");
                        for (int i = 0; i < SteamRemoteStorage.GetFileCount(); i++)
                        {
                            var configFilename = SteamRemoteStorage.GetFileNameAndSize(i, out var pnFileSizeInBytes);
                            writer.WriteLine(configFilename + " "
                                + (pnFileSizeInBytes / 1024).ToString("F2")
                                + "KB");
                        }
                        writer.Close();
                        Process.Start(listFilePath);
                        OutputMessage("List is got");
                    }
                    else if (GUILayout.Button("Open backup directory"))
                        Process.Start(HASH_GAME_CONFIG_BACKUP_PATH);
                    else if (GUILayout.Button("Open log"))
                        Process.Start(Path.Combine(Paths.BepInExRootPath, "LogOutput.log"));
                    else if (GUILayout.Button("BACKUP: Local"))
                        BackupLocal();
                    else if (GUILayout.Button("BACKUP: Cloud"))
                        BackupCloud();
                    else if (GUILayout.Button("UPLOAD: Local to Cloud"))
                    {
                        if (!_isActionConfirmed)
                        {
                            OutputMessage("Action needs to be confirmed");
                            return;
                        }
                        OutputMessage("Start upload Local to Cloud");
                        UploadLocalConfigToCloud();
                    }
                    else if (GUILayout.Button("REMOVE: Local exists but not on Cloud"))
                    { }
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
                    this.Event_RemoteStorageFileReadAsyncComplete));
            callResult_Write = CallResult<RemoteStorageFileWriteAsyncComplete_t>.Create(
                new CallResult<RemoteStorageFileWriteAsyncComplete_t>.APIDispatchDelegate(
                    this.Event_RemoteStorageFileWriteAsyncComplete));
            Directory.CreateDirectory(HASH_GAME_CONFIG_PATH);
            Directory.CreateDirectory(HASH_GAME_CONFIG_BACKUP_PATH);
        }
    }

    public void OutputMessage(string msg)
    {
        _uiOutputMessage = msg;
        Logger.LogInfo(msg);
    }

    public void BackupLocal()
    {
        var configDirectory = new DirectoryInfo(HASH_GAME_CONFIG_PATH);
        Logger.LogInfo("config path: " + path);
        var timestampString = DateTime.Now
            .ToUniversalTime().ToString(HASH_TIME_FORMAT);
        foreach (var configFile in configDirectory.GetFiles())
        {
            var path = Path.Combine(HASH_GAME_CONFIG_BACKUP_PATH
                    , $"LOCAL_{timestampString}"
                    , configFile.Name);
            Logger.LogInfo(path);
            //if (configFile.Extension == ".rgc" 
            //    || configFile.Extension == ".xml")
                configFile.CopyTo(path);
        }
        OutputMessage("Local is backed up");
    }

    public string _timestampString;
    public Dictionary<SteamAPICall_t, string> cloudFilenameIndex
        = new Dictionary<SteamAPICall_t, string>();
    public void BackupCloud()
    {
        cloudFilenameIndex.Clear();
        _timestampString = DateTime.Now
            .ToUniversalTime().ToString(HASH_TIME_FORMAT);
        for (int i = 0; i <= SteamRemoteStorage.GetFileCount(); i++)
        {
            var filename = SteamRemoteStorage.GetFileNameAndSize(i, out var pnFileSizeInBytes);
            var handle = SteamRemoteStorage.FileReadAsync(filename, 0, (uint)pnFileSizeInBytes);
            callResult_Read.Set(handle, null);
            Logger.LogInfo("Read cloud: " + filename);
            cloudFilenameIndex.Add(handle, filename);
        }
    }

    public void Event_RemoteStorageFileReadAsyncComplete(RemoteStorageFileReadAsyncComplete_t pCallback
        , bool bIOFailure)
    {
        Logger.LogInfo("OnReadComplete");
        if (pCallback.m_eResult != EResult.k_EResultOK)
        {
            Logger.LogError("Read cloud failed: " + (int)pCallback.m_eResult);
            return;
        }
        byte[] pvBuffer = new byte[pCallback.m_cubRead];
        SteamRemoteStorage.FileReadAsyncComplete(pCallback.m_hFileReadAsync, pvBuffer, pCallback.m_cubRead);
        File.Create(Path.Combine(HASH_GAME_CONFIG_BACKUP_PATH
            , $"CLOUD_{_timestampString}"
            , cloudFilenameIndex[pCallback.m_hFileReadAsync]))
            .Write(pvBuffer, 0, (int)pCallback.m_cubRead);
    }

    public void UploadLocalConfigToCloud()
    {
        // todo: handle failure
        var configDirectory = new DirectoryInfo(HASH_GAME_CONFIG_PATH);
        foreach (var configFile in configDirectory.GetFiles())
        {
            var fileData = new byte[configFile.Length];
            configFile.OpenRead().Read(fileData, 0, (int)configFile.Length);
            callResult_Write.Set(
                SteamRemoteStorage.FileWriteAsync(configFile.Name, fileData, (uint)configFile.Length)
                , null);
        }
    }

    public void Event_RemoteStorageFileWriteAsyncComplete(RemoteStorageFileWriteAsyncComplete_t pCallback
        , bool bIOFailure)
    {
        Logger.LogInfo("OnWriteComplete");
        if (pCallback.m_eResult != EResult.k_EResultOK)
        {
            Logger.LogError("Write cloud failed: " + (int)pCallback.m_eResult);
            return;
        }

        //byte[] pvBuffer = new byte[pCallback.m_cubRead];
        //SteamRemoteStorage.FileReadAsyncComplete(pCallback.m_hFileReadAsync, pvBuffer, pCallback.m_cubRead);
        //File.Create(Path.Combine(HASH_GAME_CONFIG_BACKUP_PATH
        //    , $"CLOUD_{_timestampString}"
        //    , cloudFilenameIndex[pCallback.m_hFileReadAsync]))
        ///    .Write(pvBuffer, 0, (int)pCallback.m_cubRead);
    }

    class ConfigurationManagerAttributes
    {
        public Action<object> CustomDrawer;
    }

}