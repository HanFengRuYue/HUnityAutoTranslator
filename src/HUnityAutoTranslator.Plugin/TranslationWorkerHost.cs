using System.Net.Http;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Dispatching;
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
    private readonly ControlPanelMetrics _metrics;
    private readonly ManualLogSource _logger;
    private readonly HttpClient _httpClient = new();
    private CancellationTokenSource? _cts;
    private Task? _task;
    private DateTimeOffset _nextPendingResumeUtc;

    public TranslationWorkerHost(
        ControlPanelService controlPanel,
        TranslationJobQueue queue,
        ResultDispatcher dispatcher,
        ITranslationCache cache,
        ControlPanelMetrics metrics,
        ManualLogSource logger)
    {
        _controlPanel = controlPanel;
        _queue = queue;
        _dispatcher = dispatcher;
        _cache = cache;
        _metrics = metrics;
        _logger = logger;
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
                if (!config.Enabled || !config.Provider.ApiKeyConfigured)
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (_queue.PendingCount == 0)
                {
                    var resumed = ResumePendingTranslations(config);
                    if (resumed > 0)
                    {
                        _logger.LogInfo($"Resumed {resumed} pending translation(s) from the persistent cache.");
                    }
                    else
                    {
                        await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                var pendingBefore = _queue.PendingCount;
                var provider = CreateProvider(config);
                var pool = new TranslationWorkerPool(
                    _queue,
                    _dispatcher,
                    provider,
                    new ProviderRateLimiter(config.RequestsPerMinute),
                    config,
                    _cache,
                    _metrics);

                await pool.RunUntilIdleAsync(cancellationToken).ConfigureAwait(false);
                _controlPanel.SetLastError(null);
                _logger.LogInfo($"Translation worker processed {pendingBefore} queued text item(s). Writeback backlog: {_dispatcher.PendingCount}. Cache entries: {_cache.Count}.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _controlPanel.SetLastError(ex.Message);
                _logger.LogWarning($"Translation worker failed: {ex.Message}");
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
            config.Provider,
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
                publishResult: false)))
            {
                enqueued++;
            }
        }

        return enqueued;
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
