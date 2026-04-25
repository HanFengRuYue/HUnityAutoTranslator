using BepInEx;
using BepInEx.Unity.Mono;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Plugin;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BaseUnityPlugin
{
    private ControlPanelService? _controlPanel;
    private LocalHttpServer? _httpServer;

    private void Awake()
    {
        try
        {
            _controlPanel = ControlPanelService.CreateDefault();
            _httpServer = new LocalHttpServer(_controlPanel, Logger);
            _httpServer.Start("127.0.0.1", 0);
            Logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME} loaded. Control panel: {_httpServer.Url}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Startup failed, plugin will stay inactive: {ex}");
        }
    }

    private void OnDestroy()
    {
        _httpServer?.Dispose();
    }
}
