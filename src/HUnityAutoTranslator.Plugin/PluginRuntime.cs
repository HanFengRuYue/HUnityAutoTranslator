using BepInEx;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Glossary;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Queueing;
using HUnityAutoTranslator.Plugin.Capture;
using HUnityAutoTranslator.Plugin.Hotkeys;
using HUnityAutoTranslator.Plugin.Unity;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin;

internal sealed class PluginRuntime : IDisposable
{
    private readonly ManualLogSource _logger;
    private readonly string? _pluginDirectory;
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
    private RuntimeHotkeyController? _hotkeys;
    private LlamaCppServerManager? _llamaCppServer;
    private LlamaCppModelDownloadManager? _llamaCppModelDownloads;
    private float _nextScanTime;
    private float _nextSkippedWritebackLogTime;
    private bool _openedControlPanel;

    public PluginRuntime(ManualLogSource logger, string? pluginDirectory = null)
    {
        _logger = logger;
        _pluginDirectory = pluginDirectory;
    }

    public void Start()
    {
        WindowsConsoleEncoding.ConfigureUtf8();

        try
        {
            var dataDirectory = Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME);
            var settingsPath = Path.Combine(Paths.ConfigPath, $"{MyPluginInfo.PLUGIN_GUID}.cfg");
            var cachePath = Path.Combine(dataDirectory, "translation-cache.sqlite");
            var glossaryPath = Path.Combine(dataDirectory, "translation-glossary.sqlite");
            _metrics = new ControlPanelMetrics();
            _controlPanel = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(settingsPath), _metrics);
            _controlPanel.SetAutomaticGameTitle(Application.productName);
            var config = _controlPanel.GetConfig();
            _cache = new SqliteTranslationCache(cachePath);
            _glossary = new SqliteGlossaryStore(glossaryPath);
            _queue = new TranslationJobQueue();
            _dispatcher = new ResultDispatcher();
            _resultApplier = new UnityMainThreadResultApplier(_controlPanel.GetConfig, message => _logger.LogInfo(message));
            _highlighter = new UnityTextHighlighter(_resultApplier, _logger);
            var pluginDirectory = _pluginDirectory ?? Path.GetDirectoryName(typeof(PluginRuntime).Assembly.Location) ?? Paths.PluginPath;
            _llamaCppServer = new LlamaCppServerManager(pluginDirectory, _logger);
            _llamaCppModelDownloads = new LlamaCppModelDownloadManager(Path.Combine(pluginDirectory, "models"));
            _fontReplacement = new UnityTextFontReplacementService(_cache, _logger, _controlPanel.GetConfig, _controlPanel.SetAutomaticFontFallbacks);
            _fontReplacement.InstallStartupFallbacks();
            var pipeline = new TextPipeline(_cache, _queue, _controlPanel.GetConfig, _metrics, _glossary);
            _captureCoordinator = new TextCaptureCoordinator(new ITextCaptureModule[]
            {
                new UguiTextScanner(pipeline, _resultApplier, _logger, _controlPanel.GetConfig, _fontReplacement),
                new TmpTextScanner(pipeline, _resultApplier, _logger, _controlPanel.GetConfig, _fontReplacement),
                new ImguiHookInstaller(pipeline, _cache, _logger, _controlPanel.GetConfig, _fontReplacement)
            });
            _captureCoordinator.Start();
            _workerHost = new TranslationWorkerHost(_controlPanel, _queue, _dispatcher, _cache, _glossary, _metrics, _logger, _llamaCppServer);
            _workerHost.Start();
            _httpServer = new LocalHttpServer(
                _controlPanel,
                _cache,
                _glossary,
                _queue,
                _dispatcher,
                _highlighter,
                _llamaCppServer,
                _llamaCppModelDownloads,
                _logger);
            _httpServer.Start(config.HttpHost, config.HttpPort);
            _hotkeys = new RuntimeHotkeyController(_httpServer, _captureCoordinator, _resultApplier, _fontReplacement, _logger);
            StartLlamaCppIfConfigured();
            _logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME} 已加载。控制面板：{_httpServer.Url}");
            OpenControlPanelIfConfigured();
            _logger.LogInfo($"设置文件：{settingsPath}");
            _logger.LogInfo($"翻译缓存：{cachePath}（{_cache.Count} 条）");
            _logger.LogInfo($"术语库：{glossaryPath}（{_glossary.Count} 条）");
        }
        catch (Exception ex)
        {
            _logger.LogError($"启动失败，插件将保持停用：{ex}");
        }
    }

    private void StartLlamaCppIfConfigured()
    {
        if (_controlPanel == null || _llamaCppServer == null)
        {
            return;
        }

        var config = _controlPanel.GetConfig();
        if (config.Provider.Kind != ProviderKind.LlamaCpp || !config.LlamaCpp.AutoStartOnStartup)
        {
            return;
        }

        _logger.LogInfo("检测到上次使用本地模型，正在后台自动启动 llama.cpp。");
        _ = Task.Run(async () =>
        {
            try
            {
                var status = await _llamaCppServer.StartAsync(_controlPanel.GetConfig(), CancellationToken.None).ConfigureAwait(false);
                _controlPanel.SetLlamaCppStatus(status);
                if (status.State == "error")
                {
                    _controlPanel.SetLastError(status.Message);
                    _controlPanel.SetProviderStatus(new ProviderStatus("error", status.Message, DateTimeOffset.UtcNow));
                    _logger.LogWarning($"自动启动 llama.cpp 本地模型失败：{status.Message}");
                    return;
                }

                _logger.LogInfo($"自动启动 llama.cpp 本地模型：{status.Message}");
            }
            catch (Exception ex)
            {
                var message = $"自动启动 llama.cpp 本地模型失败：{ex.Message}";
                _controlPanel.SetLastError(message);
                _controlPanel.SetProviderStatus(new ProviderStatus("error", message, DateTimeOffset.UtcNow));
                _logger.LogWarning(message);
            }
        });
    }

    private void OpenControlPanelIfConfigured()
    {
        if (_openedControlPanel || _controlPanel == null || _httpServer == null)
        {
            return;
        }

        if (!_controlPanel.GetConfig().AutoOpenControlPanel)
        {
            _logger.LogInfo("设置中已关闭控制面板自动打开。");
            return;
        }

        _openedControlPanel = true;
        SystemBrowserLauncher.TryOpen(_httpServer.Url, _logger);
    }

    public void Dispose()
    {
        _captureCoordinator?.Dispose();
        _workerHost?.Dispose();
        _llamaCppServer?.Dispose();
        _llamaCppModelDownloads?.Dispose();
        _httpServer?.Dispose();
        (_cache as IDisposable)?.Dispose();
        (_glossary as IDisposable)?.Dispose();
    }

    public void Tick()
    {
        if (_controlPanel == null)
        {
            return;
        }

        var config = _controlPanel.GetConfig();
        _hotkeys?.Tick(config);
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

    public void LateTick()
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
            if (config.ReapplyRememberedTranslations)
            {
                _resultApplier.ReapplyRemembered(int.MaxValue);
            }

            if (applied > 0)
            {
                _logger.LogInfo($"已写回 {applied} 条翻译文本。");
            }
            else if (results.Count > 0 && Time.unscaledTime >= _nextSkippedWritebackLogTime)
            {
                _logger.LogWarning($"跳过 {results.Count} 条翻译写回：目标文本已消失或已变化。");
                _nextSkippedWritebackLogTime = Time.unscaledTime + 5f;
            }
        }
    }

    public void RenderGui()
    {
        _highlighter?.OnGUI();
    }
}
