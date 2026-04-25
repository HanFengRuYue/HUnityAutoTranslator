using BepInEx;
using BepInEx.Unity.Mono;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Queueing;
using HUnityAutoTranslator.Plugin.Capture;
using HUnityAutoTranslator.Plugin.Unity;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BaseUnityPlugin
{
    private ControlPanelService? _controlPanel;
    private LocalHttpServer? _httpServer;
    private TranslationWorkerHost? _workerHost;
    private MemoryTranslationCache? _cache;
    private TranslationJobQueue? _queue;
    private ResultDispatcher? _dispatcher;
    private UnityMainThreadResultApplier? _resultApplier;
    private TextCaptureCoordinator? _captureCoordinator;
    private float _nextScanTime;

    private void Awake()
    {
        try
        {
            _controlPanel = ControlPanelService.CreateDefault();
            var config = _controlPanel.GetConfig();
            _cache = new MemoryTranslationCache();
            _queue = new TranslationJobQueue();
            _dispatcher = new ResultDispatcher();
            _resultApplier = new UnityMainThreadResultApplier();
            var pipeline = new TextPipeline(_cache, _queue, _controlPanel.GetConfig);
            _captureCoordinator = new TextCaptureCoordinator(new ITextCaptureModule[]
            {
                new UguiTextScanner(pipeline, _resultApplier, Logger, _controlPanel.GetConfig),
                new TmpTextScanner(pipeline, _resultApplier, Logger, _controlPanel.GetConfig),
                new ImguiHookInstaller(pipeline, _cache, Logger, _controlPanel.GetConfig)
            });
            _captureCoordinator.Start();
            _workerHost = new TranslationWorkerHost(_controlPanel, _queue, _dispatcher, _cache, Logger);
            _workerHost.Start();
            _httpServer = new LocalHttpServer(
                _controlPanel,
                () => _queue.PendingCount,
                () => _cache.Count,
                Logger);
            _httpServer.Start(config.HttpHost, config.HttpPort);
            Logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME} loaded. Control panel: {_httpServer.Url}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Startup failed, plugin will stay inactive: {ex}");
        }
    }

    private void OnDestroy()
    {
        _captureCoordinator?.Dispose();
        _workerHost?.Dispose();
        _httpServer?.Dispose();
    }

    private void Update()
    {
        if (_controlPanel == null)
        {
            return;
        }

        var config = _controlPanel.GetConfig();
        if (_dispatcher != null && _resultApplier != null)
        {
            var results = _dispatcher.Drain(config.MaxWritebacksPerFrame);
            _resultApplier.Apply(results);
        }

        if (_captureCoordinator != null && Time.unscaledTime >= _nextScanTime)
        {
            _captureCoordinator.Tick();
            _nextScanTime = Time.unscaledTime + (float)config.ScanInterval.TotalSeconds;
        }
    }
}
