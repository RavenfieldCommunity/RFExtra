using System;
using System.Diagnostics;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace RFExtra.TDFDView;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class TDFDView : BaseUnityPlugin
{
    public static TDFDView instance;
    public Harmony harmonyInstance;
    public ConfigEntry<KeyboardShortcut> spectatorOrthographizationKeybind;
    public ConfigEntry<float> spectatorOrthographicAdjustingSpeed;
    private float _previousCameraFOV = 60;
    private void Awake()
    {
        instance = this;
        spectatorOrthographizationKeybind = Config.Bind("Config",
            "Orthographic Spectator Keybind",
            new KeyboardShortcut(KeyCode.H, [KeyCode.LeftControl]),
            "`q/e` to adjust camera depth (suitable depth is good for camera rotating), `w/a/s/d` to move, `L` to fix camera rotation, `scroll` to adjust move speed, `mouse middle` to enable smooth movement");
        spectatorOrthographicAdjustingSpeed = Config.Bind("Config",
            "Orthographic Spectator Adjusting Speed",
            15f, "The speed of adjusting orthographic camera fov, using `ctrl` + `scroll`");
        harmonyInstance = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmonyInstance.PatchAll(typeof(Patch));
        // config ui
        Config.Bind<bool>("UI", "UI", true,
            new ConfigDescription("", null, new ConfigurationManagerAttributes()
            {
                CustomDrawer = (obj) =>
                {
                    GUILayout.EndVertical();
                    // button
                    if (GUILayout.Button("Reset Camera position") 
                        && SpectatorCamera.instance != null)
                        SpectatorCamera.instance.camera.gameObject.transform.position = 
                            Vector3.zero;
                    GUILayout.BeginVertical();
                }
            }));
    }

    // the most foolish thingy i have ever done for ConfigurationManager
    class ConfigurationManagerAttributes
    {
        public Action<object> CustomDrawer;
    }

    private void Update()
    {
        if (SpectatorCamera.instance != null)
        {
            if (spectatorOrthographizationKeybind.Value.IsDown())
            {
                SpectatorCamera.instance.camera.orthographic =
                    !SpectatorCamera.instance.camera.orthographic;
                if (SpectatorCamera.instance.camera.orthographic)
                {
                    _previousCameraFOV = SpectatorCamera.instance.camera.fieldOfView;
                }
                else
                {
                    var tempFov = SpectatorCamera.instance.camera.fieldOfView;
                    SpectatorCamera.instance.camera.fieldOfView = _previousCameraFOV;
                    _previousCameraFOV = tempFov;
                }
            }
            if (SpectatorCamera.instance.camera.orthographic)
                SpectatorCamera.instance.camera.orthographicSize =
                    (int)(SpectatorCamera.instance.camera.fieldOfView
                    * spectatorOrthographicAdjustingSpeed.Value);
        }
    }
}

[HarmonyPatch]
public static class Patch
{
    public static Traverse SpectatorCamera_velocity;

    [HarmonyPatch(typeof(SpectatorCamera), "Start")]
    [HarmonyPrefix]
    public static void SpectatorCamera_Start(SpectatorCamera __instance)
    {
        var traverse = Traverse.Create(__instance);
        SpectatorCamera_velocity = traverse.Field("velocity");
        if (GameManager.instance.gameModeParameters.playerTeam == -1)
            traverse.Field("fullLock").SetValue(true);
    }

    [HarmonyPatch(typeof(SteelInput), nameof(SteelInput.GetAxis))]
    [HarmonyPrefix]
    public static bool SteelInput_GetAxis(ref SteelInput.KeyBinds input, ref float __result)
    {
        if (SpectatorCamera.instance == null
            || !SpectatorCamera.instance.gameObject.activeSelf
            || !SpectatorCamera.instance.camera.orthographic)
            return true;
        switch (input)
        {
            case SteelInput.KeyBinds.Vertical:
                input = SteelInput.KeyBinds.Lean;
                break;
            case SteelInput.KeyBinds.Lean:
                //input = SteelInput.KeyBinds.Vertical;
                __result = -SteelInput.GetInput(SteelInput.KeyBinds.Vertical).GetValue();
                return false;
        }
        return true;
    }
}
