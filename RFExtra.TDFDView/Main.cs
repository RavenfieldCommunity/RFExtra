using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
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
    private void Awake()
    {
        instance = this;
        spectatorOrthographizationKeybind = Config.Bind("Config",
            "Orthographic Spectator Keybind",
            new KeyboardShortcut(KeyCode.F7),
            "`w` and `s` to adjust camera depth, `a/d/q/e` to move, `L` to fix camera rotation, `scroll` to adjust move speed, `mouse middle` to enable smooth movement");
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
                SpectatorCamera.instance.camera.orthographic =
                    !SpectatorCamera.instance.camera.orthographic;
            SpectatorCamera.instance.camera.orthographicSize = (int)(SpectatorCamera.instance.camera.fieldOfView * spectatorOrthographicMultiplier.Value);
            Patch
        }
    }
}

[HarmonyPatch]
public static class Patch
{
    public static GameObject SpectatorCamera_cameraParent;
    public static Traverse SpectatorCamera_velocity;

    [HarmonyPatch(typeof(SpectatorCamera), "Start")]
    [HarmonyPostfix]
    public static void SpectatorCamera_Start(SpectatorCamera __instance)
    {
        var traverse = Traverse.Create(__instance);
        SpectatorCamera_cameraParent = traverse.Field("cameraParent").GetValue<GameObject>();
        SpectatorCamera_velocity = traverse.Field("velocity");
        if (GameManager.instance.gameModeParameters.playerTeam == -1)
            traverse.Field("fullLock").SetValue(true);
    }
}
