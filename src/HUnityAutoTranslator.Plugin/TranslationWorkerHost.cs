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
using HUnityAutoTranslator.Core.Text;

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
    private readonly QualityRetryResumeSuppressions _qualityRetryResumeSuppressions = new();
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

                var promotedQualityRetries = _queue.PromoteDeferred();
                if (promotedQualityRetries > 0)
                {
                    _logger.LogInfo($"已将 {promotedQualityRetries} 条质量重试加入等待翻译队列。");
                }

                if (_queue.PendingCount == 0)
                {
                    var resumed = ResumePendingTranslations(config);
                    if (resumed > 0)
                    {
                        _logger.LogInfo($"已从缓存恢复 {resumed} 条待翻译文本。");
                    }
                }

                if (!await ProviderReadyAsync(config, cancellationToken).ConfigureAwait(false))
                {
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                config = _controlPanel.GetConfig();
                var provider = CreateProvider(config);
                if (_queue.PendingCount == 0)
                {
                    await TryExtractGlossaryAsync(provider, config, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                    continue;
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
                    ReportTranslationFailure,
                    ReportQualityRetryLimitReached,
                    snapshot => ReportTranslationDebugSnapshot(config, snapshot));

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
        if (_controlPanel.HasReadyProviderRuntimeProfile())
        {
            return new FailoverTranslationProvider(
                _controlPanel.GetReadyProviderRuntimeProfiles,
                CreateProviderAsync,
                (profile, error) =>
                {
                    if (profile.Profile.Kind == ProviderKind.LlamaCpp)
                    {
                        _logger.LogWarning(error);
                        return false;
                    }

                    var shouldFailOver = _controlPanel.RegisterProviderProfileFailure(profile, error);
                    if (shouldFailOver)
                    {
                        _logger.LogWarning($"服务商配置“{profile.Name}”连续失败，当前批次将切换到下一优先级配置。错误：{error}");
                    }

                    return shouldFailOver;
                },
                profile => _controlPanel.RegisterProviderProfileSuccess(profile),
                profile => _metrics.RecordProviderAttempt(profile));
        }

        return CreateLegacyProvider(config);
    }

    private ITranslationProvider CreateLegacyProvider(RuntimeConfig config)
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

    private ITranslationProvider CreateProvider(ProviderRuntimeProfile runtimeProfile)
    {
        return runtimeProfile.Profile.Kind == ProviderKind.OpenAI
            ? new OpenAiResponsesProvider(
                _httpClient,
                runtimeProfile.Profile,
                () => runtimeProfile.ApiKey,
                runtimeProfile.ReasoningEffort,
                runtimeProfile.OutputVerbosity,
                TimeSpan.FromSeconds(runtimeProfile.RequestTimeoutSeconds))
            : new ChatCompletionsProvider(
                _httpClient,
                runtimeProfile.Profile,
                () => runtimeProfile.ApiKey,
                runtimeProfile.ReasoningEffort,
                runtimeProfile.DeepSeekThinkingMode,
                runtimeProfile.Temperature,
                TimeSpan.FromSeconds(runtimeProfile.RequestTimeoutSeconds));
    }

    private async Task<ITranslationProvider> CreateProviderAsync(ProviderRuntimeProfile runtimeProfile, CancellationToken cancellationToken)
    {
        if (runtimeProfile.Profile.Kind != ProviderKind.LlamaCpp)
        {
            return CreateProvider(runtimeProfile);
        }

        if (_llamaCppServer == null)
        {
            return new FailureTranslationProvider(runtimeProfile.Profile, "llama.cpp 本地模型管理器不可用。");
        }

        var config = runtimeProfile.ApplyTo(_controlPanel.GetConfig());
        if (!await EnsureLlamaCppRuntimeReadyAsync(
                runtimeProfile,
                config,
                runtimeProfile.LlamaCpp?.AutoStartOnStartup == true,
                cancellationToken).ConfigureAwait(false))
        {
            var pendingStatus = _llamaCppServer.GetStatus(config);
            var message = string.IsNullOrWhiteSpace(pendingStatus.Message)
                ? "llama.cpp 本地模型未启动。请在控制面板手动启动。"
                : pendingStatus.Message;
            return new PendingTranslationProvider(runtimeProfile.Profile, message);
        }

        var status = _llamaCppServer.GetStatus(config);
        _controlPanel.SetLlamaCppStatus(status);
        if (!await _llamaCppServer.IsReadyAsync(config, cancellationToken).ConfigureAwait(false))
        {
            status = await _llamaCppServer.StartAsync(config, cancellationToken).ConfigureAwait(false);
            _controlPanel.SetLlamaCppStatus(status);
        }

        if (!await _llamaCppServer.IsReadyAsync(config, cancellationToken).ConfigureAwait(false))
        {
            var message = string.IsNullOrWhiteSpace(status.Message)
                ? "llama.cpp 本地模型未能启动。"
                : status.Message;
            _controlPanel.SetProviderStatus(new ProviderStatus("error", message, DateTimeOffset.UtcNow));
            return new FailureTranslationProvider(runtimeProfile.Profile, message);
        }

        status = _llamaCppServer.GetStatus(config);
        _controlPanel.SetLlamaCppStatus(status);
        _controlPanel.SetProviderStatus(new ProviderStatus("ok", "llama.cpp 本地模型运行中。", DateTimeOffset.UtcNow));
        var profile = runtimeProfile.Profile with
        {
            BaseUrl = $"http://127.0.0.1:{status.Port}",
            Endpoint = "/v1/chat/completions",
            ApiKeyConfigured = true
        };
        return new ChatCompletionsProvider(
            _httpClient,
            profile,
            () => runtimeProfile.ApiKey,
            runtimeProfile.ReasoningEffort,
            runtimeProfile.DeepSeekThinkingMode,
            runtimeProfile.Temperature,
            TimeSpan.FromSeconds(runtimeProfile.RequestTimeoutSeconds));
    }

    private void ReportTranslationDebugSnapshot(RuntimeConfig config, TranslationRequestDebugSnapshot snapshot)
    {
        if (!config.EnableTranslationDebugLogs)
        {
            return;
        }

        _logger.LogInfo("AI 翻译请求结构：" + snapshot.ToLogLine());
    }

    private void ReportTranslationFailure(string message)
    {
        _logger.LogWarning(message);
    }

    private void ReportQualityRetryLimitReached(TranslationJob job)
    {
        _qualityRetryResumeSuppressions.Suppress(job, DateTimeOffset.UtcNow);
    }

    private async Task<bool> ProviderProfilesReadyAsync(RuntimeConfig config, CancellationToken cancellationToken)
    {
        var profiles = _controlPanel.GetReadyProviderRuntimeProfiles();
        if (profiles.Count == 0)
        {
            return false;
        }

        var runtimeProfile = profiles[0];
        if (runtimeProfile.Profile.Kind != ProviderKind.LlamaCpp)
        {
            return true;
        }

        var localConfig = runtimeProfile.ApplyTo(config);
        return await EnsureLlamaCppRuntimeReadyAsync(
            runtimeProfile,
            localConfig,
            runtimeProfile.LlamaCpp?.AutoStartOnStartup == true,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> EnsureLlamaCppRuntimeReadyAsync(
        ProviderRuntimeProfile runtimeProfile,
        RuntimeConfig config,
        bool allowAutoStart,
        CancellationToken cancellationToken)
    {
        if (_llamaCppServer == null)
        {
            const string unavailableMessage = "llama.cpp 本地模型管理器不可用。";
            _controlPanel.SetLastError(unavailableMessage);
            _controlPanel.SetProviderStatus(new ProviderStatus("error", unavailableMessage, DateTimeOffset.UtcNow));
            return false;
        }

        var status = _llamaCppServer.GetStatus(config);
        _controlPanel.SetLlamaCppStatus(status);
        if (await _llamaCppServer.IsReadyAsync(config, cancellationToken).ConfigureAwait(false))
        {
            _controlPanel.SetLlamaCppStatus(_llamaCppServer.GetStatus(config));
            _controlPanel.SetProviderStatus(new ProviderStatus("ok", "llama.cpp 本地模型运行中。", DateTimeOffset.UtcNow));
            return true;
        }

        status = _llamaCppServer.GetStatus(config);
        _controlPanel.SetLlamaCppStatus(status);
        if (string.Equals(status.State, "starting", StringComparison.OrdinalIgnoreCase))
        {
            var startingMessage = string.IsNullOrWhiteSpace(status.Message)
                ? "llama.cpp 本地模型正在启动。"
                : status.Message;
            _controlPanel.SetProviderStatus(new ProviderStatus("warning", startingMessage, DateTimeOffset.UtcNow));
            return false;
        }

        if (allowAutoStart)
        {
            status = await _llamaCppServer.StartAsync(config, cancellationToken).ConfigureAwait(false);
            _controlPanel.SetLlamaCppStatus(status);
            if (await _llamaCppServer.IsReadyAsync(config, cancellationToken).ConfigureAwait(false))
            {
                _controlPanel.SetLlamaCppStatus(_llamaCppServer.GetStatus(config));
                _controlPanel.SetProviderStatus(new ProviderStatus("ok", "llama.cpp 本地模型运行中。", DateTimeOffset.UtcNow));
                return true;
            }
        }

        status = _llamaCppServer.GetStatus(config);
        _controlPanel.SetLlamaCppStatus(status);
        var message = string.IsNullOrWhiteSpace(status.Message) || string.Equals(status.State, "stopped", StringComparison.OrdinalIgnoreCase)
            ? "llama.cpp 本地模型未启动。请在控制面板手动启动。"
            : status.Message;
        var state = string.Equals(status.State, "error", StringComparison.OrdinalIgnoreCase) ? "error" : "warning";
        _controlPanel.SetLastError(message);
        _controlPanel.SetProviderStatus(new ProviderStatus(state, message, DateTimeOffset.UtcNow));
        return false;
    }

    private async Task<bool> ProviderReadyAsync(RuntimeConfig config, CancellationToken cancellationToken)
    {
        if (_controlPanel.HasReadyProviderRuntimeProfile())
        {
            return await ProviderProfilesReadyAsync(config, cancellationToken).ConfigureAwait(false);
        }

        if (config.Provider.Kind != ProviderKind.LlamaCpp)
        {
            return false;
        }

        if (_llamaCppServer == null)
        {
            const string message = "llama.cpp 本地模型管理器不可用。";
            _controlPanel.SetLastError(message);
            _controlPanel.SetProviderStatus(new ProviderStatus("error", message, DateTimeOffset.UtcNow));
            return false;
        }

        if (!await _llamaCppServer.IsReadyAsync(config, cancellationToken).ConfigureAwait(false))
        {
            _controlPanel.SetLlamaCppStatus(await _llamaCppServer.StartAsync(config, cancellationToken).ConfigureAwait(false));
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

    private sealed class FailureTranslationProvider : ITranslationProvider
    {
        private readonly ProviderProfile _profile;
        private readonly string _message;

        public FailureTranslationProvider(ProviderProfile profile, string message)
        {
            _profile = profile;
            _message = message;
        }

        public ProviderKind Kind => _profile.Kind;

        public Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(TranslationResponse.Failure(_message, _profile));
        }
    }

    private sealed class PendingTranslationProvider : ITranslationProvider
    {
        private readonly ProviderProfile _profile;
        private readonly string _message;

        public PendingTranslationProvider(ProviderProfile profile, string message)
        {
            _profile = profile;
            _message = message;
        }

        public ProviderKind Kind => _profile.Kind;

        public Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(TranslationResponse.Failure(_message, _profile));
        }
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
            TextPipeline.GetPromptPolicyVersion(config),
            PendingResumeBatchSize);

        var enqueued = 0;
        foreach (var row in pending)
        {
            var context = new TranslationCacheContext(row.SceneName, row.ComponentHierarchy, row.ComponentType);
            if (_qualityRetryResumeSuppressions.ShouldSkip(row))
            {
                continue;
            }

            var key = TranslationCacheKey.Create(
                row.SourceText,
                row.TargetLanguage,
                config.Provider,
                TextPipeline.GetPromptPolicyVersion(config));
            if (!TextFilter.ShouldTranslate(row.SourceText))
            {
                _cache.Update(row with
                {
                    ProviderKind = string.Empty,
                    ProviderBaseUrl = string.Empty,
                    ProviderEndpoint = string.Empty,
                    ProviderModel = string.Empty,
                    TranslatedText = row.SourceText,
                    UpdatedUtc = DateTimeOffset.UtcNow
                });
                continue;
            }

            if (config.EnableCacheLookup &&
                TranslationCacheReuse.TryGetReusableTranslation(_cache, key, context, config, _glossary, out var reusableTranslatedText))
            {
                _cache.Set(key, reusableTranslatedText, context);
                continue;
            }

            if (_queue.Enqueue(TranslationJob.Create(
                "pending:" + row.SourceText,
                row.SourceText,
                TranslationPriority.Normal,
                context,
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
