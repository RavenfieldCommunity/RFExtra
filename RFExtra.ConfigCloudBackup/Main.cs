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

namespace RFExtra.ConfigCloudBackup;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class ConfigCloudBackup : BaseUnityPlugin
{
    public static ConfigCloudBackup instance;
    public Harmony harmonyInstance;
    public string uiOutputMessage
    {
        get;
        set
        {
            field = value;
            Logger.LogInfo(value);
        }
    }
    private bool _acceptActionConfirm = false;
    private bool _isAcionRunning = false;
    /// <summary>
    /// Steam async action only
    /// </summary>
    private string _actionTimestampString;
    private int _totalConfigToBeProcessedCount = 0;
    private int _currentProcessedConfigCount = 0;

    public Callback<RemoteStorageFileReadAsyncComplete_t> callback_RemoteStorageFileReadAsyncComplete;
    public bool _isNext = false;

    public readonly string HASH_GAME_CONFIG_PATH = Application.persistentDataPath + "/GameConfigurations/";
    public readonly string HASH_GAME_CONFIG_BACKUP_PATH = Application.persistentDataPath + "/RFExtra/ConfigurationBacups/";
    public readonly string HASH_CLOUD_CONFIG_LIST_FILENAME = "rfextra_cloudconfiglist.txt";
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
                    GUILayout.TextArea("TOOL ONLY, ALWAYS BACKUP ON YOUR OWN OCCASIONALLY!!");
                    GUILayout.TextArea("Some actions need confirm and will auto backup configs locally first");
                    _acceptActionConfirm = GUILayout.Toggle(_acceptActionConfirm, "CONFIRM ACTION");
                    GUILayout.TextArea(uiOutputMessage);
                    // button
                    if (GUILayout.Button("Get cloud config list"))
                    {
                        var listFilePath = Paths.GameRootPath + "\\" + HASH_CLOUD_CONFIG_LIST_FILENAME;
                        if (File.Exists(listFilePath))
                            File.Delete(listFilePath);
                        var writer = File.CreateText(listFilePath);
                        writer.WriteLine($"CLOUD CONFIG FILE LIST ({SteamRemoteStorage.GetFileCount()} in total):");
                        for (int i = 0; i <= SteamRemoteStorage.GetFileCount(); i++)
                        {
                            foreach (var configFilename in SteamRemoteStorage.GetFileNameAndSize(i, out var pnFileSizeInBytes))
                            {
                                writer.WriteLine(configFilename + " "
                                    + (pnFileSizeInBytes / 1024).ToString("F2")
                                    + "KB");
                            }
                        }
                        uiOutputMessage = "List is got";
                    }
                    else if (GUILayout.Button("Open backup directory"))
                        Process.Start(HASH_GAME_CONFIG_BACKUP_PATH);
                    else if (GUILayout.Button("Open log"))
                    {
                        Process.Start(Paths.BepInExRootPath + "\\LogOutput.log");
                    }
                    else if (GUILayout.Button("LOCAL BACKUP: Local to Local"))
                    {
                        BackupLocalConfigToLocal();
                    }
                    else if (GUILayout.Button("LOCAL BACKUP: Cloud to Local") && !_isAcionRunning)
                    {

                    }
                    else if (GUILayout.Button("OVERWRITE: Local to Cloud") && !_isAcionRunning)
                    {
                        if (!_acceptActionConfirm)
                        {
                            uiOutputMessage = "Action needs to confirm";
                            return;
                        }
                        _isAcionRunning = true;
                        uiOutputMessage = "Start OVERWRITE: Local to Cloud";
                        _actionTimestampString = DateTime.Now
                            .ToUniversalTime().ToString("yyyy-MM-dd-HH-mm-ss");
                        _isNext = true;
                        var configDirectory = new DirectoryInfo(HASH_GAME_CONFIG_PATH);
                         _totalConfigToBeProcessedCount = configDirectory.GetFiles().Count();
                        BackupCloudConfigToLocal();
                    }
                    else if (GUILayout.Button("OVERWRITE: Cloud to Local") && !_isAcionRunning)
                    {

                    }
                    GUILayout.BeginVertical();
                }
            }));
        SteamAPI.Init();
        if (!SteamManager.Initialized)
        {
            uiOutputMessage = "Steam is not connected";
            return;
        }
        else
        {
            uiOutputMessage = $"AppId: {SteamUtils.GetAppID()}";
            callback_RemoteStorageFileReadAsyncComplete =
                Callback<RemoteStorageFileReadAsyncComplete_t>.Create(
                    new Callback<RemoteStorageFileReadAsyncComplete_t>.DispatchDelegate(
                        this.Event_RemoteStorageFileReadAsyncComplete));

            Directory.CreateDirectory(HASH_GAME_CONFIG_PATH);
            Directory.CreateDirectory(HASH_GAME_CONFIG_BACKUP_PATH);
        }
    }


    public void BackupCloudConfigToLocal()
    {
        uiOutputMessage = "Backuping Cloud";
        for (int i = 0; i <= SteamRemoteStorage.GetFileCount(); i++)
        {
            _totalConfigToBeProcessedCount = SteamRemoteStorage.GetFileCount();
            //var callRes = CallResult<RemoteStorageFileWriteAsyncComplete_t>
            //    .Create(this.Event_RemoteStorageFileReadAsyncComplete);
            SteamRemoteStorage.FileReadAsync(
                SteamRemoteStorage.GetFileNameAndSize(i, out var pnFileSizeInBytes)
                , 0, (uint)pnFileSizeInBytes);
        }
    }

    public void BackupLocalConfigToLocal()
    {
        var configDirectory = new DirectoryInfo(HASH_GAME_CONFIG_PATH);
        foreach (var configFile in configDirectory.GetFiles())
        {
            var timestampString = DateTime.Now
                .ToUniversalTime().ToString("yyyy-MM-dd-HH-mm-ss");
            configFile.CopyTo(HASH_GAME_CONFIG_BACKUP_PATH
                + $"\\LOCAL_{timestampString}\\");
            uiOutputMessage = "Local is backed up";
        }
    }

    /// <summary>
    /// For cloud back up to local
    /// </summary>
    public void Event_RemoteStorageFileReadAsyncComplete(RemoteStorageFileReadAsyncComplete_t pCallback)
    {
        if (pCallback.m_eResult != EResult.k_EResultOK)
        {
            // todo: process failure
            return;
        }
        byte[] pvBuffer = new byte[pCallback.m_cubRead];
        SteamRemoteStorage.FileReadAsyncComplete(pCallback.m_hFileReadAsync, pvBuffer, pCallback.m_cubRead);
        
        _currentProcessedConfigCount += 1;
        if (_currentProcessedConfigCount >= _totalConfigToBeProcessedCount)
            UploadLocalConfigToCloud();
    }

    public void UploadLocalConfigToCloud()
    {
        
    }

    class ConfigurationManagerAttributes
    {
        public Action<object> CustomDrawer;
    }
}

[HarmonyPatch]
public static class Patch
{

}
