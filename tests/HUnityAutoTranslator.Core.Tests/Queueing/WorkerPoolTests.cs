using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Providers;
using HUnityAutoTranslator.Core.Queueing;

namespace HUnityAutoTranslator.Core.Tests.Queueing;

public sealed class WorkerPoolTests
{
    [Fact]
    public async Task WorkerPool_runs_multiple_batches_concurrently()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var provider = new DelayedProvider(TimeSpan.FromMilliseconds(150));
        var limiter = new ProviderRateLimiter(requestsPerMinute: 120);
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, limiter, RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 3 });

        queue.Enqueue(TranslationJob.Create("a", "A", TranslationPriority.Normal));
        queue.Enqueue(TranslationJob.Create("b", "B", TranslationPriority.Normal));
        queue.Enqueue(TranslationJob.Create("c", "C", TranslationPriority.Normal));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        dispatcher.PendingCount.Should().Be(3);
        provider.MaxObservedConcurrency.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task WorkerPool_builds_guarded_prompt_restores_placeholders_and_caches_results()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new CapturingProvider(new[] { "Hello translated __HUT_TOKEN_0__" });
        var limiter = new ProviderRateLimiter(requestsPerMinute: 120);
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, limiter, config, cache);

        queue.Enqueue(TranslationJob.Create("ui-1", "Hello {playerName}", TranslationPriority.VisibleUi));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.LastRequest.Should().NotBeNull();
        provider.LastRequest!.SystemPrompt.Should().Contain("Simplified Chinese");
        provider.LastRequest.SystemPrompt.Should().NotContain(config.TargetLanguage);
        provider.LastRequest.UserPrompt.Should().Contain("__HUT_TOKEN_0__");

        var results = dispatcher.Drain(10);
        results.Should().ContainSingle();
        results[0].TranslatedText.Should().Be("Hello translated {playerName}");

        var key = TranslationCacheKey.Create(
            "Hello {playerName}",
            config.TargetLanguage,
            config.Provider,
            TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, out var cached).Should().BeTrue();
        cached.Should().Be("Hello translated {playerName}");
    }

    [Fact]
    public async Task WorkerPool_preserves_capture_context_when_caching_results()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new RecordingCache();
        var provider = new CapturingProvider(new[] { "Options translated" });
        var limiter = new ProviderRateLimiter(requestsPerMinute: 120);
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, limiter, config, cache);
        var context = new TranslationCacheContext("MainMenu", "Canvas/Menu/Options", "UnityEngine.UI.Text");

        queue.Enqueue(TranslationJob.Create("ui-1", "Options", TranslationPriority.VisibleUi, context));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        cache.LastKey.Should().NotBeNull();
        cache.LastKey!.SourceText.Should().Be("Options");
        cache.LastContext.Should().Be(context);
        cache.LastTranslatedText.Should().Be("Options translated");
    }

    [Fact]
    public async Task WorkerPool_records_recent_completed_translation()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var metrics = new ControlPanelMetrics();
        var config = RuntimeConfig.CreateDefault() with
        {
            Provider = ProviderProfile.DefaultOpenAi() with { ApiKeyConfigured = true }
        };
        var cache = new MemoryTranslationCache();
        queue.Enqueue(TranslationJob.Create("target", "Start Game", TranslationPriority.Normal, TranslationCacheContext.Empty));
        var provider = new CapturingProvider(new[] { "Start translated" });
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, new ProviderRateLimiter(600), config, cache, metrics);

        await pool.RunUntilIdleAsync(CancellationToken.None);

        var snapshot = metrics.Snapshot();
        snapshot.CompletedTranslationCount.Should().Be(1);
        snapshot.RecentTranslations.Should().ContainSingle(item => item.SourceText == "Start Game" && item.TranslatedText == "Start translated");
    }

    [Fact]
    public async Task WorkerPool_can_complete_resumed_pending_job_without_dispatching_writeback()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new CapturingProvider(new[] { "Continue translated" });
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, new ProviderRateLimiter(600), config, cache);

        queue.Enqueue(TranslationJob.Create(
            "pending:Continue",
            "Continue",
            TranslationPriority.Normal,
            new TranslationCacheContext("Menu", "Canvas/Continue", "Text"),
            publishResult: false));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        var key = TranslationCacheKey.Create("Continue", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, out var cached).Should().BeTrue();
        cached.Should().Be("Continue translated");
        dispatcher.PendingCount.Should().Be(0);
    }

    private sealed class DelayedProvider : ITranslationProvider
    {
        private int _current;
        private readonly TimeSpan _delay;

        public DelayedProvider(TimeSpan delay) => _delay = delay;

        public int MaxObservedConcurrency { get; private set; }

        public ProviderKind Kind => ProviderKind.OpenAI;

        public async Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _current);
            MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, current);
            await Task.Delay(_delay, cancellationToken);
            Interlocked.Decrement(ref _current);
            return TranslationResponse.Success(request.ProtectedTexts.Select(x => "T:" + x).ToArray());
        }
    }

    private sealed class CapturingProvider : ITranslationProvider
    {
        private readonly IReadOnlyList<string> _translations;

        public CapturingProvider(IReadOnlyList<string> translations) => _translations = translations;

        public TranslationRequest? LastRequest { get; private set; }

        public ProviderKind Kind => ProviderKind.OpenAI;

        public Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(TranslationResponse.Success(_translations));
        }
    }

    private sealed class RecordingCache : ITranslationCache
    {
        public TranslationCacheKey? LastKey { get; private set; }

        public TranslationCacheContext? LastContext { get; private set; }

        public string? LastTranslatedText { get; private set; }

        public int Count => LastKey == null ? 0 : 1;

        public bool TryGet(TranslationCacheKey key, out string translatedText)
        {
            translatedText = string.Empty;
            return false;
        }

        public void Set(TranslationCacheKey key, string translatedText, TranslationCacheContext? context = null)
        {
            LastKey = key;
            LastTranslatedText = translatedText;
            LastContext = context;
        }

        public TranslationCachePage Query(TranslationCacheQuery query)
        {
            return new TranslationCachePage(0, Array.Empty<TranslationCacheEntry>());
        }

        public void Update(TranslationCacheEntry entry)
        {
        }

        public void Delete(TranslationCacheEntry entry)
        {
        }

        public string Export(string format)
        {
            return string.Empty;
        }

        public TranslationCacheImportResult Import(string content, string format)
        {
            return new TranslationCacheImportResult(0, Array.Empty<string>());
        }

        public void RecordCaptured(TranslationCacheKey key, TranslationCacheContext? context = null)
        {
        }

        public IReadOnlyList<TranslationCacheEntry> GetPendingTranslations(
            string targetLanguage,
            ProviderProfile provider,
            string promptPolicyVersion,
            int limit)
        {
            return Array.Empty<TranslationCacheEntry>();
        }
    }
}
