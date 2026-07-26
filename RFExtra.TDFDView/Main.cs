using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Ravenfield.Trigger;
using UnityEngine;
using UnityEngine.PlayerLoop;

namespace RFExtra.TDFDView;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class TDFDView : BaseUnityPlugin
{
    public static TDFDView instance;
    public Harmony harmonyInstance;
    public ConfigEntry<KeyboardShortcut> spectatorOrthographizationKeybind;
    public ConfigEntry<float> spectatorOrthographicMultiplier;
    private float _previousCameraFOV = 60;
    private void Awake()
    {
        instance = this;
        spectatorOrthographizationKeybind = Config.Bind("Config",
            "Orthographic Spectator Keybind",
            new KeyboardShortcut(KeyCode.F9),
            "`q/e` to adjust camera depth, `w/a/s/d` to move, `L` to fix camera rotation, `scroll` to adjust move speed, `mouse middle` to enable smooth movement");
        spectatorOrthographicMultiplier = Config.Bind("Config",
            "Orthographic Spectator Multiplier",
            1f, "The speed of changing orthographic camera fov speed, using `ctrl` + `scroll`");
        harmonyInstance = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmonyInstance.PatchAll(typeof(Patch));
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
                    SpectatorCamera.instance.camera.fieldOfView = _previousCameraFOV;
            }
            if (SpectatorCamera.instance.camera.orthographic)
                SpectatorCamera.instance.camera.orthographicSize = 
                    (int)(SpectatorCamera.instance.camera.fieldOfView 
                    * spectatorOrthographicMultiplier.Value);
            //Traverse.Create(SpectatorCamera.instance).Field("velocity").SetValue(Vector3.zero);
            //Patch.SpectatorCamera_velocity.SetValue(Vector3.zero);
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
    public static void SteelInput_GetAxis(ref SteelInput.KeyBinds input)
    {
        if (SpectatorCamera.instance == null
            || !SpectatorCamera.instance.gameObject.activeSelf
            || !SpectatorCamera.instance.camera.orthographic)
            return;
        switch (input)
        {
            case SteelInput.KeyBinds.Vertical:
                input = SteelInput.KeyBinds.Lean;
                break;
            case SteelInput.KeyBinds.Lean:
                input = SteelInput.KeyBinds.Vertical;
                break;
        }
    }
}
