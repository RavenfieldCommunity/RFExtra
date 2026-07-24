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
    public AssetBundle assetBundle;
    public GameObject tdfdGameObject;
    public GameObject tdfdCameraGameObject;
    public ConfigEntry<KeyboardShortcut> spectatorOrthographizationKeybind;
    public ConfigEntry<float> spectatorOrthographicMultiplier;
    private void Awake()
    {
        var assembly = Assembly.GetExecutingAssembly();
        Task.Run(() =>
            {
                var assetLoader = AssetBundle.LoadFromStreamAsync(
                    assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()[0]));
                assetLoader.completed += (AsyncOperation asyncOperation) =>
                    {
                        assetBundle = assetLoader.assetBundle;
                        Logger.LogDebug(assetBundle.GetAllAssetNames()[0]);
                    };
            });
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
        }
    }

    public void Event_OnPlayerDied(Actor actor)
    {
        if (tdfdGameObject != null)
            tdfdCameraGameObject.transform.SetParent(null);
    }

    public void Event_OnPlayerSpawn()
    {
        if (tdfdGameObject == null)
        {
            tdfdGameObject = Instantiate(
                assetBundle.LoadAsset(assetBundle.GetAllAssetNames()[0]) as GameObject);
            tdfdCameraGameObject = Instantiate(
                tdfdGameObject.GetComponent<TDFDViewClient>().cameraGameObject);
        }
        tdfdCameraGameObject.transform.SetParent(
            FpsActorController.instance.actor.transform);
        tdfdCameraGameObject.transform.localPosition = Vector3.zero;
    }
}

public class TDFDViewClient : MonoBehaviour
{
    public RenderTexture renderTexture;
    public GameObject cameraGameObject;
}

[HarmonyPatch]
public static class Patch
{
    public static GameObject SpectatorCamera_cameraParent;

    [HarmonyPatch(typeof(FpsActorController), nameof(FpsActorController.SpawnAt))]
    [HarmonyPostfix]
    public static void FpsActorController_SpawnAt()
    {
        TDFDView.instance.Event_OnPlayerSpawn();
    }

    [HarmonyPatch(typeof(Actor), "Die")]
    [HarmonyPostfix]
    public static void Actor_Die(Actor __instance)
    {
        if (FpsActorController.instance != null
            && FpsActorController.instance.actor == __instance)
            TDFDView.instance.Event_OnPlayerDied(__instance);
    }

    [HarmonyPatch(typeof(SpectatorCamera), "Start")]
    [HarmonyPostfix]
    public static void SpectatorCamera_Start(SpectatorCamera __instance)
    {
        var traverse = Traverse.Create(__instance);
        SpectatorCamera_cameraParent = traverse.Field("cameraParent").GetValue<GameObject>();
        traverse.Field("fullLock").SetValue(true);
    }
}
