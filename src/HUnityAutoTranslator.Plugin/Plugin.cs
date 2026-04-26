using BepInEx;
using BepInEx.Unity.Mono;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Glossary;
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
    private ITranslationCache? _cache;
    private IGlossaryStore? _glossary;
    private TranslationJobQueue? _queue;
    private ResultDispatcher? _dispatcher;
    private UnityMainThreadResultApplier? _resultApplier;
    private TextCaptureCoordinator? _captureCoordinator;
    private ControlPanelMetrics? _metrics;
    private UnityTextFontReplacementService? _fontReplacement;
    private UnityTextHighlighter? _highlighter;
    private float _nextScanTime;
    private float _nextSkippedWritebackLogTime;
    private bool _openedControlPanel;

    private void Awake()
    {
        try
        {
            var dataDirectory = Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME);
            var settingsPath = Path.Combine(dataDirectory, "settings.json");
            var cachePath = Path.Combine(dataDirectory, "translation-cache.sqlite");
            var glossaryPath = Path.Combine(dataDirectory, "translation-glossary.sqlite");
            _metrics = new ControlPanelMetrics();
            _controlPanel = ControlPanelService.CreateDefault(new JsonControlPanelSettingsStore(settingsPath), _metrics);
            var config = _controlPanel.GetConfig();
            _cache = new SqliteTranslationCache(cachePath);
            _glossary = new SqliteGlossaryStore(glossaryPath);
            _queue = new TranslationJobQueue();
            _dispatcher = new ResultDispatcher();
            _resultApplier = new UnityMainThreadResultApplier();
            _highlighter = new UnityTextHighlighter(_resultApplier, Logger);
            _fontReplacement = new UnityTextFontReplacementService(_cache, Logger, _controlPanel.GetConfig);
            _fontReplacement.InstallStartupFallbacks();
            var pipeline = new TextPipeline(_cache, _queue, _controlPanel.GetConfig, _metrics, _glossary);
            _captureCoordinator = new TextCaptureCoordinator(new ITextCaptureModule[]
            {
                new UguiTextScanner(pipeline, _resultApplier, Logger, _controlPanel.GetConfig, _fontReplacement),
                new TmpTextScanner(pipeline, _resultApplier, Logger, _controlPanel.GetConfig, _fontReplacement),
                new ImguiHookInstaller(pipeline, _cache, Logger, _controlPanel.GetConfig, _fontReplacement)
            });
            _captureCoordinator.Start();
            _workerHost = new TranslationWorkerHost(_controlPanel, _queue, _dispatcher, _cache, _glossary, _metrics, Logger);
            _workerHost.Start();
            _httpServer = new LocalHttpServer(
                _controlPanel,
                _cache,
                _glossary,
                _queue,
                _dispatcher,
                _highlighter,
                Logger);
            _httpServer.Start(config.HttpHost, config.HttpPort);
            Logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME} loaded. Control panel: {_httpServer.Url}");
            OpenControlPanelIfConfigured();
            Logger.LogInfo($"Persistent settings: {settingsPath}");
            Logger.LogInfo($"Translation cache: {cachePath} ({_cache.Count} entries)");
            Logger.LogInfo($"Translation glossary: {glossaryPath} ({_glossary.Count} terms)");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Startup failed, plugin will stay inactive: {ex}");
        }
    }

    private void OpenControlPanelIfConfigured()
    {
        if (_openedControlPanel || _controlPanel == null || _httpServer == null)
        {
            return;
        }

        if (!_controlPanel.GetConfig().AutoOpenControlPanel)
        {
            Logger.LogInfo("Control panel auto-open is disabled by settings.");
            return;
        }

        _openedControlPanel = true;
        SystemBrowserLauncher.TryOpen(_httpServer.Url, Logger);
    }

    private void OnDestroy()
    {
        _captureCoordinator?.Dispose();
        _workerHost?.Dispose();
        _httpServer?.Dispose();
        (_cache as IDisposable)?.Dispose();
        (_glossary as IDisposable)?.Dispose();
    }

    private void Update()
    {
        if (_controlPanel == null)
        {
            return;
        }

        var config = _controlPanel.GetConfig();
        if (_captureCoordinator != null && Time.unscaledTime >= _nextScanTime)
        {
            _captureCoordinator.Tick();
            _nextScanTime = Time.unscaledTime + (float)config.ScanInterval.TotalSeconds;
        }

        if (_highlighter != null && _resultApplier != null)
        {
            _highlighter.RefreshTargetSnapshot(_resultApplier.SnapshotTargets());
            _highlighter.Tick();
        }
    }

    private void LateUpdate()
    {
        if (_controlPanel == null)
        {
            return;
        }

        var config = _controlPanel.GetConfig();
        if (_dispatcher != null && _resultApplier != null)
        {
            var results = _dispatcher.Drain(config.MaxWritebacksPerFrame);
            var applied = _resultApplier.Apply(results);
            _resultApplier.ReapplyRemembered(config.MaxWritebacksPerFrame);
            if (applied > 0)
            {
                Logger.LogInfo($"Applied {applied} translated text result(s).");
            }
            else if (results.Count > 0 && Time.unscaledTime >= _nextSkippedWritebackLogTime)
            {
                Logger.LogWarning($"Skipped {results.Count} translated text result(s) because targets were gone or changed before writeback.");
                _nextSkippedWritebackLogTime = Time.unscaledTime + 5f;
            }
        }
    }

    private void OnGUI()
    {
        _highlighter?.OnGUI();
    }
}
