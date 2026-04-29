#if HUNITY_IL2CPP
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin;

internal sealed class Il2CppPluginLoop : MonoBehaviour
{
    private static PluginRuntime? _runtime;
    private static bool _installed;

    public static void Install(PluginRuntime runtime)
    {
        _runtime = runtime;
        if (_installed)
        {
            return;
        }

        IL2CPPChainloader.AddUnityComponent(typeof(Il2CppPluginLoop));
        _installed = true;
    }

    public static void Uninstall(PluginRuntime runtime)
    {
        if (ReferenceEquals(_runtime, runtime))
        {
            _runtime = null;
        }
    }

    private void Update()
    {
        _runtime?.Tick();
    }

    private void LateUpdate()
    {
        _runtime?.LateTick();
    }

    private void OnGUI()
    {
        _runtime?.RenderGui();
    }
}
#endif
