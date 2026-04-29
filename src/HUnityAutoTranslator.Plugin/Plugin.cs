using BepInEx;

#if HUNITY_IL2CPP
using BepInEx.Unity.IL2CPP;
#else
using BepInEx.Unity.Mono;
#endif

namespace HUnityAutoTranslator.Plugin;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
#if HUNITY_IL2CPP
public sealed class Plugin : BasePlugin
{
    private PluginRuntime? _runtime;

    public override void Load()
    {
        _runtime = new PluginRuntime(Log, Path.GetDirectoryName(typeof(Plugin).Assembly.Location));
        _runtime.Start();
        Il2CppPluginLoop.Install(_runtime);
    }

    public override bool Unload()
    {
        if (_runtime != null)
        {
            Il2CppPluginLoop.Uninstall(_runtime);
            _runtime.Dispose();
            _runtime = null;
        }

        return true;
    }
}
#else
public sealed class Plugin : BaseUnityPlugin
{
    private PluginRuntime? _runtime;

    private void Awake()
    {
        _runtime = new PluginRuntime(Logger, Path.GetDirectoryName(typeof(Plugin).Assembly.Location));
        _runtime.Start();
    }

    private void OnDestroy()
    {
        _runtime?.Dispose();
        _runtime = null;
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
