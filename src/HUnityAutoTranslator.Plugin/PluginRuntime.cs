using BepInEx;
using BepInEx.Logging;
using System.Diagnostics;
using System.Reflection;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Glossary;
using HUnityAutoTranslator.Core.Http;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Queueing;
using HUnityAutoTranslator.Core.Runtime;
using HUnityAutoTranslator.Core.Textures;
using HUnityAutoTranslator.Plugin.Capture;
using HUnityAutoTranslator.Plugin.Hotkeys;
using HUnityAutoTranslator.Plugin.Http;
using HUnityAutoTranslator.Plugin.Unity;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin;

internal sealed class PluginRuntime : IDisposable
{
    private const float HighlighterSnapshotIntervalSeconds = 0.15f;
    private const float TextHookIdleGlobalScanSeconds = 20f;
    private const float TextHookDiscoveryScanIntervalSeconds = 3f;
    private const float GlobalTextScanDebounceSeconds = 0.75f;
    private const int TextHookGlobalScanTargetLimit = 128;
    private const int TextHookDiscoveryScanTargetLimit = 64;
    private const int TextHookQueueMaxItemsPerTick = 64;
    private const int TextHookQueueMaxMillisecondsPerTick = 2;
    private const float FastStaticTextRetrySeconds = 0.1f;

    private readonly ManualLogSource _logger;
    private readonly string? _pluginDirectory;
    private IHttpTransport? _httpTransport;
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
    private TextureOverrideStore? _textureOverrides;
    private TextureTextAnalysisStore? _textureTextAnalysis;
    private UnityTextureReplacementService? _textureReplacement;
    private RuntimeHotkeyController? _hotkeys;
    private LlamaCppServerManager? _llamaCppServer;
    private LlamaCppModelDownloadManager? _llamaCppModelDownloads;
    private SelfCheckService? _selfCheck;
    private UnityTextChangeHookInstaller? _textChangeHook;
    private UnityTextChangeQueue? _textChangeQueue;
    private UnityTextTargetRegistry? _textTargetRegistry;
    private UnityTextTargetProcessor? _textTargetProcessor;
    private float _nextScanTime;
    private float _nextReflectionScanTime;
    private float _requestedGlobalTextScanTime;
    private float _nextHighlighterSnapshotTime;
    private float _nextSkippedWritebackLogTime;
    private bool _globalTextScanRequested;
    private bool _openedControlPanel;
    private bool _hotkeyTickFailureLogged;
    private bool _pendingAutomaticSelfCheck;

    public PluginRuntime(ManualLogSource logger, string? pluginDirectory = null)
    {
        _logger = logger;
        _pluginDirectory = pluginDirectory;
    }

    private static string? GetBepInExVersion()
    {
        try
        {
            return typeof(Paths).Assembly.GetName().Version?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public void Start()
    {
        WindowsConsoleEncoding.ConfigureUtf8();

        try
        {
            var dataDirectory = Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME);
            var settingsPath = Path.Combine(Paths.ConfigPath, $"{MyPluginInfo.PLUGIN_GUID}.cfg");
            var providerProfilesPath = Path.Combine(dataDirectory, "providers");
            var textureImageProviderProfilesPath = Path.Combine(dataDirectory, "texture-image-providers");
            var cachePath = Path.Combine(dataDirectory, "translation-cache.sqlite");
            var glossaryPath = Path.Combine(dataDirectory, "translation-glossary.sqlite");
            var textureOverridesPath = Path.Combine(dataDirectory, "texture-overrides");
            var textureCatalogPath = Path.Combine(dataDirectory, "texture-catalog");
            _metrics = new ControlPanelMetrics();
            _controlPanel = ControlPanelService.CreateDefault(
                new CfgControlPanelSettingsStore(settingsPath),
                new EncryptedProviderProfileStore(providerProfilesPath),
                new EncryptedTextureImageProviderProfileStore(textureImageProviderProfilesPath),
                _metrics);
            _controlPanel.SetRuntimeVersions(MyPluginInfo.PLUGIN_VERSION, GetBepInExVersion());
            _controlPanel.SetAutomaticGameTitle(Application.productName);
            var config = _controlPanel.GetConfig();
            _cache = new SqliteTranslationCache(cachePath);
            _glossary = new SqliteGlossaryStore(glossaryPath);
            _queue = new TranslationJobQueue();
            _dispatcher = new ResultDispatcher();
            _resultApplier = new UnityMainThreadResultApplier(_controlPanel.GetConfig, message => _logger.LogInfo(message), _metrics);
            _highlighter = new UnityTextHighlighter(_resultApplier, _logger);
            _textureOverrides = new TextureOverrideStore(textureOverridesPath);
            _textureTextAnalysis = new TextureTextAnalysisStore(Path.Combine(textureCatalogPath, "text-analysis.json"));
            _textureReplacement = new UnityTextureReplacementService(
                _textureOverrides,
                new TextureCatalogStore(textureCatalogPath),
                _textureTextAnalysis,
                () => _controlPanel?.GetConfig().GameTitle ?? Application.productName,
                _logger);
            var pluginDirectory = _pluginDirectory ?? Path.GetDirectoryName(typeof(PluginRuntime).Assembly.Location) ?? Paths.PluginPath;
            _httpTransport = HttpTransportFactory.Create(_logger);
            _llamaCppServer = new LlamaCppServerManager(pluginDirectory, _logger, _httpTransport);
            _llamaCppModelDownloads = new LlamaCppModelDownloadManager(_httpTransport, Path.Combine(pluginDirectory, "models"));
            _fontReplacement = new UnityTextFontReplacementService(_cache, _logger, _controlPanel.GetConfig, _controlPanel.SetAutomaticFontFallbacks, _metrics);
            _resultApplier.SetFontReplacementService(_fontReplacement);
            _fontReplacement.InstallStartupFallbacks();
            var pipeline = new TextPipeline(_cache, _queue, _controlPanel.GetConfig, _metrics, _glossary);
            var textStabilityGate = new UnityTextStabilityGate();
            var textTargetRegistry = new UnityTextTargetRegistry(_metrics);
            _textTargetRegistry = textTargetRegistry;
            _textChangeQueue = new UnityTextChangeQueue(_metrics);
            _textTargetProcessor = new UnityTextTargetProcessor(
                pipeline,
                _resultApplier,
                _controlPanel.GetConfig,
                _fontReplacement,
                RunTextChangeSuppressed,
                textStabilityGate,
                textTargetRegistry,
                _metrics);
            _textChangeHook = new UnityTextChangeHookInstaller(
                pipeline,
                _resultApplier,
                _logger,
                _controlPanel.GetConfig,
                _fontReplacement,
                textStabilityGate,
                RequestGlobalTextScan,
                _metrics,
                _textChangeQueue);
            _captureCoordinator = new TextCaptureCoordinator(new ITextCaptureModule[]
            {
                new UguiTextScanner(pipeline, _resultApplier, _logger, _controlPanel.GetConfig, _fontReplacement, textStabilityGate, textTargetRegistry, _textChangeQueue),
                new TmpTextScanner(pipeline, _resultApplier, _logger, _controlPanel.GetConfig, _fontReplacement, textStabilityGate, textTargetRegistry, _textChangeQueue),
                new ImguiHookInstaller(pipeline, _cache, _logger, _controlPanel.GetConfig, _fontReplacement)
            });
            _textChangeHook?.Start();
            _captureCoordinator.Start();
            _workerHost = new TranslationWorkerHost(_controlPanel, _queue, _dispatcher, _cache, _glossary, _metrics, _logger, _httpTransport, _llamaCppServer);
            _workerHost.Start();
            _selfCheck = new SelfCheckService(
                _controlPanel,
                _cache,
                _glossary,
                _queue,
                _dispatcher,
                _resultApplier,
                _textureReplacement,
                _llamaCppServer,
                pluginDirectory,
                dataDirectory,
                () => _httpServer?.Url ?? string.Empty,
                BuildMemoryDiagnostics,
                _logger);
            _httpServer = new LocalHttpServer(
                _controlPanel,
                _cache,
                _glossary,
                _queue,
                _dispatcher,
                _highlighter,
                _textureReplacement,
                _llamaCppServer,
                _llamaCppModelDownloads,
                _selfCheck,
                BuildMemoryDiagnostics,
                dataDirectory,
                _httpTransport,
                _logger);
            _httpServer.Start(config.HttpHost, config.HttpPort);
            _pendingAutomaticSelfCheck = true;
            _hotkeys = new RuntimeHotkeyController(
                _httpServer,
                _captureCoordinator,
                _resultApplier,
                _fontReplacement,
                _logger,
                _textChangeHook == null ? null : _textChangeHook.RunSuppressed);
            StartLlamaCppIfConfigured();
            LogMemoryDiagnostics(BuildMemoryDiagnostics());
            _logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME} 已加载。控制面板：{_httpServer.Url}");
            OpenControlPanelIfConfigured();
            _logger.LogInfo($"设置文件：{settingsPath}");
            _logger.LogInfo($"服务商配置目录：{providerProfilesPath}");
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

    private MemoryDiagnosticsSnapshot BuildMemoryDiagnostics()
    {
        var text = _resultApplier?.GetMemoryDiagnostics();
        var font = _fontReplacement?.GetMemoryDiagnostics();
        var texture = _textureReplacement?.GetMemoryDiagnostics();
        var metrics = _metrics?.Snapshot();
        return new MemoryDiagnosticsSnapshot(
            ManagedMemoryBytes: GC.GetTotalMemory(forceFullCollection: false),
            UnityAllocatedMemoryBytes: TryReadUnityProfilerValue("GetTotalAllocatedMemoryLong"),
            UnityReservedMemoryBytes: TryReadUnityProfilerValue("GetTotalReservedMemoryLong"),
            UnityMonoHeapBytes: TryReadUnityProfilerValue("GetMonoHeapSizeLong"),
            QueueCount: _queue?.PendingCount ?? 0,
            WritebackQueueCount: _dispatcher?.PendingCount ?? 0,
            CapturedKeyTrackerCount: _metrics?.Snapshot().CapturedKeyTrackerCount ?? 0,
            RegisteredTextTargetCount: text?.RegisteredTextTargetCount ?? 0,
            FontCacheCount: font?.UnityFontCacheCount ?? 0,
            TmpFontAssetCacheCount: font?.TmpFontAssetCacheCount ?? 0,
            ImguiFontResolutionCacheCount: font?.ImguiFontResolutionCacheCount ?? 0,
            TextureRecordCount: texture?.TextureRecordCount ?? 0,
            ReplacementTextureCount: texture?.ReplacementTextureCount ?? 0,
            TexturePngBytes: texture?.RetainedSourcePngBytes ?? 0,
            TextChangeHookEventCount: metrics?.TextChangeHookEventCount ?? 0,
            TextChangeHookQueuedCount: metrics?.TextChangeHookQueuedCount ?? 0,
            TextChangeHookMergedCount: metrics?.TextChangeHookMergedCount ?? 0,
            TextChangeHookDroppedCount: metrics?.TextChangeHookDroppedCount ?? 0,
            TextChangeRawPrefilteredCount: metrics?.TextChangeRawPrefilteredCount ?? 0,
            TextChangeQueueProcessedCount: metrics?.TextChangeQueueProcessedCount ?? 0,
            TextChangeQueueMilliseconds: metrics?.TextChangeQueueMilliseconds ?? 0,
            TextTargetMetadataBuildCount: metrics?.TextTargetMetadataBuildCount ?? 0,
            CacheLookupCount: metrics?.CacheLookupCount ?? 0,
            GlobalTextScanRequestCount: metrics?.GlobalTextScanRequestCount ?? 0,
            GlobalTextScanCount: metrics?.GlobalTextScanCount ?? 0,
            GlobalTextScanTargetCount: metrics?.GlobalTextScanTargetCount ?? 0,
            GlobalTextScanMilliseconds: metrics?.GlobalTextScanMilliseconds ?? 0,
            RememberedReapplyCheckCount: metrics?.RememberedReapplyCheckCount ?? 0,
            RememberedReapplyAppliedCount: metrics?.RememberedReapplyAppliedCount ?? 0,
            FontApplicationCount: metrics?.FontApplicationCount ?? 0,
            FontApplicationSkippedCount: metrics?.FontApplicationSkippedCount ?? 0,
            LayoutApplicationCount: metrics?.LayoutApplicationCount ?? 0,
            LayoutApplicationSkippedCount: metrics?.LayoutApplicationSkippedCount ?? 0,
            TmpMeshForceUpdateCount: metrics?.TmpMeshForceUpdateCount ?? 0);
    }

    private void LogMemoryDiagnostics(MemoryDiagnosticsSnapshot diagnostics)
    {
        _logger.LogInfo(
            "插件内存快照：" +
            $"托管 {FormatBytes(diagnostics.ManagedMemoryBytes)}，" +
            $"Unity 已分配 {FormatBytes(diagnostics.UnityAllocatedMemoryBytes)}，" +
            $"队列 {diagnostics.QueueCount}，写回 {diagnostics.WritebackQueueCount}，" +
            $"文本目标 {diagnostics.RegisteredTextTargetCount}，" +
            $"字体缓存 {diagnostics.FontCacheCount}/{diagnostics.TmpFontAssetCacheCount}，" +
            $"Hook {diagnostics.TextChangeHookEventCount}/{diagnostics.TextChangeHookQueuedCount}/{diagnostics.TextChangeHookMergedCount}，" +
            $"脏队列 {diagnostics.TextChangeQueueProcessedCount} 项/{diagnostics.TextChangeQueueMilliseconds} ms，" +
            $"缓存查找 {diagnostics.CacheLookupCount}，" +
            $"IMGUI 字体解析 {diagnostics.ImguiFontResolutionCacheCount}，" +
            $"纹理记录 {diagnostics.TextureRecordCount}，替换纹理 {diagnostics.ReplacementTextureCount}。");
    }

    private static long TryReadUnityProfilerValue(string methodName)
    {
        try
        {
            var profilerType =
                Type.GetType("UnityEngine.Profiling.Profiler, UnityEngine.CoreModule") ??
                typeof(Application).Assembly.GetType("UnityEngine.Profiling.Profiler");
            var method = profilerType?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            var value = method?.Invoke(null, null);
            return value == null ? 0 : Convert.ToInt64(value);
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "未知";
        }

        return $"{bytes / 1024d / 1024d:0.0} MB";
    }

    public void Dispose()
    {
        _textChangeHook?.Dispose();
        _captureCoordinator?.Dispose();
        _workerHost?.Dispose();
        _llamaCppServer?.Dispose();
        _llamaCppModelDownloads?.Dispose();
        _fontReplacement?.Dispose();
        _textureReplacement?.Dispose();
        _httpServer?.Dispose();
        _httpTransport?.Dispose();
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
        _selfCheck?.Tick();
        StartPendingAutomaticSelfCheck();
        TryTickHotkeys(config);
        DrainTextChangeQueue();
        var scanned = false;
        if (_captureCoordinator != null && Time.unscaledTime >= _nextScanTime)
        {
            var textHooksEnabled = _textChangeHook?.IsEnabled == true;
            var requestedGlobalScanReady = _globalTextScanRequested && Time.unscaledTime >= _requestedGlobalTextScanTime;
            var staticDiscoveryScanReady = textHooksEnabled && Time.unscaledTime >= _nextReflectionScanTime;
            var hookIdleFallbackReady = textHooksEnabled &&
                _textChangeQueue?.HasRecentHookEvent(Time.unscaledTime, TextHookIdleGlobalScanSeconds) != true &&
                Time.unscaledTime >= _nextReflectionScanTime;
            var runReflectionScan = !textHooksEnabled || requestedGlobalScanReady || staticDiscoveryScanReady || hookIdleFallbackReady;
            var globalScanTargetLimit = requestedGlobalScanReady ? TextHookGlobalScanTargetLimit : TextHookDiscoveryScanTargetLimit;
            var stopwatch = Stopwatch.StartNew();
            var processedTargets = _captureCoordinator.Tick(
                skipGlobalObjectScanners: textHooksEnabled && !runReflectionScan,
                maxGlobalObjectScanTargets: textHooksEnabled ? globalScanTargetLimit : null);
            stopwatch.Stop();
            _nextScanTime = Time.unscaledTime + (float)config.ScanInterval.TotalSeconds;
            if (runReflectionScan)
            {
                if (requestedGlobalScanReady || !textHooksEnabled)
                {
                    _globalTextScanRequested = false;
                }

                _metrics?.RecordGlobalTextScan(stopwatch.Elapsed, processedTargets);
                _nextReflectionScanTime = Time.unscaledTime + (textHooksEnabled
                    ? TextHookDiscoveryScanIntervalSeconds
                    : Math.Max(1f, (float)config.ScanInterval.TotalSeconds));
            }

            scanned = true;
        }

        MaybeRefreshHighlighterSnapshot(scanned);
        if (_highlighter != null)
        {
            _highlighter.Tick();
        }

        _textureReplacement?.Tick();
    }

    private void DrainTextChangeQueue()
    {
        if (_textChangeQueue == null || _textTargetProcessor == null)
        {
            return;
        }

        _textChangeQueue.Drain(
            ProcessQueuedTextChange,
            TextHookQueueMaxItemsPerTick,
            TextHookQueueMaxMillisecondsPerTick);
    }

    private void ProcessQueuedTextChange(UnityTextChangeWorkItem item)
    {
        try
        {
            var result = UnityTextProcessResult.Ignored;
            RunTextChangeSuppressed(() => result = _textTargetProcessor?.Process(
                item.Component,
                item.TextProperty,
                item.TargetKind,
                item.ObservedText) ?? UnityTextProcessResult.Ignored);
            if (result == UnityTextProcessResult.WaitForStability)
            {
                _textChangeQueue?.RequeueForStability(item, Time.unscaledTime + FastStaticTextRetrySeconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"即时文本队列处理失败，将由低频扫描兜底：{ex.Message}");
        }
    }

    private void RequestGlobalTextScan()
    {
        var now = Time.unscaledTime;
        _globalTextScanRequested = true;
        _requestedGlobalTextScanTime = Math.Max(_requestedGlobalTextScanTime, now + GlobalTextScanDebounceSeconds);
        _nextScanTime = Math.Min(_nextScanTime, _requestedGlobalTextScanTime);
        _metrics?.RecordGlobalTextScanRequest();
        _textTargetRegistry?.InvalidateMetadata();
        _resultApplier?.MarkAllTargetsForReapply();
    }

    private void StartPendingAutomaticSelfCheck()
    {
        if (!_pendingAutomaticSelfCheck || _selfCheck == null)
        {
            return;
        }

        _pendingAutomaticSelfCheck = false;
        _selfCheck.StartAutomaticAsync();
    }

    private void TryTickHotkeys(RuntimeConfig config)
    {
        try
        {
            _hotkeys?.Tick(config);
        }
        catch (Exception ex)
        {
            if (_hotkeyTickFailureLogged)
            {
                return;
            }

            _hotkeyTickFailureLogged = true;
            _logger.LogWarning($"运行时热键轮询失败，已跳过本帧热键处理：{ex.Message}");
        }
    }

    private void MaybeRefreshHighlighterSnapshot(bool force)
    {
        if (_highlighter == null || _resultApplier == null)
        {
            return;
        }

        if (!force && Time.unscaledTime < _nextHighlighterSnapshotTime)
        {
            return;
        }

        var targets = _resultApplier.SnapshotTargets();
        _highlighter.RefreshTargetSnapshot(targets);
        _nextHighlighterSnapshotTime = Time.unscaledTime + HighlighterSnapshotIntervalSeconds;
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
            var applied = 0;
            RunTextChangeSuppressed(() => applied = _resultApplier.Apply(results));
            if (config.ReapplyRememberedTranslations)
            {
                RunTextChangeSuppressed(() => _resultApplier.ReapplyDirtyRemembered(config.MaxWritebacksPerFrame));
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

    private void RunTextChangeSuppressed(Action action)
    {
        if (_textChangeHook != null)
        {
            _textChangeHook.RunSuppressed(action);
            return;
        }

        action();
    }
}
