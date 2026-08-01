# RFExtra.ConfigCloudBackup

A plugin to backup local game configuration(`.rgc`) to Steam cloud.

# Installation 
- Install BepInEx
- Install [BepInEx.ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) (FOLLOWING plugin zip archive includes it)
- Download [the plugin](https://github.com/RavenfieldCommunity/RFExtra/releases/tag/SinglePackages) and put it to folder `BepInEx\plugins\`

# Usage
Launch the game, then you can press `F1` or `Left alt`+`S`(default) to edit the config and invoke other features.

**REMEMBER:** This is only a tool, always backing up files on your own regularly! 

Some actions need to toggle `COMFIRM ACTIONS?` to continue

Actions:

  - `Get cloud file list`

    Get the files  on the cloud in form of list

  - `Open backup directory`

    Open the local directory which contains backups from important actions

  - `Open log`

    Open log file for checking error and debug

  - `BACKUP`

    Backup local or cloud file to local backup directory, you can use this feature when you are editing your own game configurations

  - `UPLOAD`

    Upload local files to cloud and backup cloud files first

  - `DOWNLOAD`

    Download cloud files and overwrite local files(which will not delete local files not on cloud) and backup local files first
     
  - `REMOVE`

    Delete cloud files which not exist on players' config directory, as a way to delete the files on cloud, in order to reduce accidental actions by players

    The deleted files are still storaged by Steam client on local cache(`steam\userdata\*userid*\636480\remote`), but not reachable to players