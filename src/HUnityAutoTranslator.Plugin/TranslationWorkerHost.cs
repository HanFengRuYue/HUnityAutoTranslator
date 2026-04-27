using System.Net.Http;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Glossary;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Providers;
using HUnityAutoTranslator.Core.Queueing;

namespace HUnityAutoTranslator.Plugin;

internal sealed class TranslationWorkerHost : IDisposable
{
    private const int PendingResumeBatchSize = 100;
    private static readonly TimeSpan PendingResumeInterval = TimeSpan.FromSeconds(5);

    private readonly ControlPanelService _controlPanel;
    private readonly TranslationJobQueue _queue;
    private readonly ResultDispatcher _dispatcher;
    private readonly ITranslationCache _cache;
    private readonly IGlossaryStore _glossary;
    private readonly ControlPanelMetrics _metrics;
    private readonly ManualLogSource _logger;
    private readonly LlamaCppServerManager? _llamaCppServer;
    private readonly HttpClient _httpClient = new();
    private CancellationTokenSource? _cts;
    private Task? _task;
    private DateTimeOffset _nextPendingResumeUtc;
    private DateTimeOffset _nextGlossaryExtractionUtc;

    public TranslationWorkerHost(
        ControlPanelService controlPanel,
        TranslationJobQueue queue,
        ResultDispatcher dispatcher,
        ITranslationCache cache,
        IGlossaryStore glossary,
        ControlPanelMetrics metrics,
        ManualLogSource logger,
        LlamaCppServerManager? llamaCppServer = null)
    {
        _controlPanel = controlPanel;
        _queue = queue;
        _dispatcher = dispatcher;
        _cache = cache;
        _glossary = glossary;
        _metrics = metrics;
        _logger = logger;
        _llamaCppServer = llamaCppServer;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _task = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var config = _controlPanel.GetConfig();
                if (!config.Enabled)
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!await ProviderReadyAsync(config, cancellationToken).ConfigureAwait(false))
                {
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var provider = CreateProvider(config);
                if (_queue.PendingCount == 0)
                {
                    var resumed = ResumePendingTranslations(config);
                    if (resumed > 0)
                    {
                        _logger.LogInfo($"已从缓存恢复 {resumed} 条待翻译文本。");
                    }
                    else
                    {
                        await TryExtractGlossaryAsync(provider, config, cancellationToken).ConfigureAwait(false);
                        await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                var pendingBefore = _queue.PendingCount;
                var pool = new TranslationWorkerPool(
                    _queue,
                    _dispatcher,
                    provider,
                    new ProviderRateLimiter(config.RequestsPerMinute),
                    config,
                    _cache,
                    _metrics,
                    _glossary,
                    message => _logger.LogWarning(message));

                await pool.RunUntilIdleAsync(cancellationToken).ConfigureAwait(false);
                await TryExtractGlossaryAsync(provider, config, cancellationToken).ConfigureAwait(false);
                _controlPanel.SetLastError(null);
                _logger.LogInfo($"本轮已处理 {pendingBefore} 条待翻译文本。待写回：{_dispatcher.PendingCount}，缓存条目：{_cache.Count}。");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _controlPanel.SetLastError(ex.Message);
                _logger.LogWarning($"翻译工作线程出错：{ex.Message}");
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private ITranslationProvider CreateProvider(RuntimeConfig config)
    {
        return config.Provider.Kind == ProviderKind.OpenAI
            ? new OpenAiResponsesProvider(
                _httpClient,
                config.Provider,
                _controlPanel.GetApiKey,
                config.ReasoningEffort,
                config.OutputVerbosity,
                TimeSpan.FromSeconds(config.RequestTimeoutSeconds))
            : new ChatCompletionsProvider(
                _httpClient,
                config.Provider,
                _controlPanel.GetApiKey,
                config.ReasoningEffort,
                config.DeepSeekThinkingMode,
                config.Temperature,
                TimeSpan.FromSeconds(config.RequestTimeoutSeconds));
    }

    private async Task<bool> ProviderReadyAsync(RuntimeConfig config, CancellationToken cancellationToken)
    {
        if (config.Provider.Kind != ProviderKind.LlamaCpp)
        {
            return config.Provider.ApiKeyConfigured;
        }

        if (_llamaCppServer == null)
        {
            const string message = "llama.cpp 本地模型管理器不可用。";
            _controlPanel.SetLastError(message);
            _controlPanel.SetProviderStatus(new ProviderStatus("error", message, DateTimeOffset.UtcNow));
            return false;
        }

        if (await _llamaCppServer.IsReadyAsync(config, cancellationToken).ConfigureAwait(false))
        {
            _controlPanel.SetProviderStatus(new ProviderStatus("ok", "llama.cpp 本地模型运行中。", DateTimeOffset.UtcNow));
            _controlPanel.SetLlamaCppStatus(_llamaCppServer.GetStatus(config));
            return true;
        }

        const string notStarted = "llama.cpp 本地模型未启动。请在控制面板手动启动。";
        _controlPanel.SetLastError(notStarted);
        _controlPanel.SetProviderStatus(new ProviderStatus("warning", notStarted, DateTimeOffset.UtcNow));
        _controlPanel.SetLlamaCppStatus(_llamaCppServer.GetStatus(config));
        return false;
    }

    private int ResumePendingTranslations(RuntimeConfig config)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < _nextPendingResumeUtc)
        {
            return 0;
        }

        _nextPendingResumeUtc = now + PendingResumeInterval;
        var pending = _cache.GetPendingTranslations(
            config.TargetLanguage,
            TextPipeline.PromptPolicyVersion,
            PendingResumeBatchSize);

        var enqueued = 0;
        foreach (var row in pending)
        {
            if (_queue.Enqueue(TranslationJob.Create(
                "pending:" + row.SourceText,
                row.SourceText,
                TranslationPriority.Normal,
                new TranslationCacheContext(row.SceneName, row.ComponentHierarchy, row.ComponentType),
                publishResult: false,
                targetLanguage: row.TargetLanguage)))
            {
                enqueued++;
            }
        }

        return enqueued;
    }

    private async Task TryExtractGlossaryAsync(
        ITranslationProvider provider,
        RuntimeConfig config,
        CancellationToken cancellationToken)
    {
        if (!config.EnableAutoTermExtraction || _queue.PendingCount > 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < _nextGlossaryExtractionUtc)
        {
            return;
        }

        _nextGlossaryExtractionUtc = now + TimeSpan.FromSeconds(30);
        var result = await GlossaryExtractionService.ExtractOnceAsync(
            _cache,
            _glossary,
            provider,
            config,
            cancellationToken).ConfigureAwait(false);
        if (result.ImportedCount > 0)
        {
            _logger.LogInfo($"术语自动提取完成：新增 {result.ImportedCount} 条，跳过 {result.SkippedCount} 条。");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _task?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        _httpClient.Dispose();
        _cts?.Dispose();
    }
}
