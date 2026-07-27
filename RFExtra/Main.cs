using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace RFExtra;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("com.bepis.bepinex.configurationmanager", BepInDependency.DependencyFlags.SoftDependency)]
public class RFExtraStandard : BaseUnityPlugin
{
    public ConfigEntry<bool> noConfigUIHotkeyConfilct;
    private void Awake()
    {
        noConfigUIHotkeyConfilct = Config.Bind("Config",
            "No ConfigUI Hotkey Confilct",
            true,
            "Replace ConfigUI hotkey `F1` to another hotkey, player can also go to set the hotkey on own in the config file.");
        // get keybind
        var configurationManagerTraverse = Traverse.Create(
            FindAnyObjectByType<ConfigurationManager.ConfigurationManager>(FindObjectsInactive.Include));
        var configUIKeybindConfig = configurationManagerTraverse.Field("_keybind")
            .GetValue<ConfigEntry<KeyboardShortcut>>();
        if (noConfigUIHotkeyConfilct.Value
            && configUIKeybindConfig.Value.MainKey == KeyCode.F1
            && configUIKeybindConfig.Value.Modifiers.Count() == 0)
            configUIKeybindConfig.Value =
                new KeyboardShortcut(KeyCode.S, [KeyCode.LeftAlt]);
    }

    public class ConfigurationManagerAttributes
    {

    }
}