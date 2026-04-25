using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
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
        var provider = new CapturingProvider(new[] { "你好 __HUT_TOKEN_0__" });
        var limiter = new ProviderRateLimiter(requestsPerMinute: 120);
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, limiter, config, cache);

        queue.Enqueue(TranslationJob.Create("ui-1", "Hello {playerName}", TranslationPriority.VisibleUi));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.LastRequest.Should().NotBeNull();
        provider.LastRequest!.SystemPrompt.Should().Contain("只输出译文");
        provider.LastRequest.UserPrompt.Should().Contain("__HUT_TOKEN_0__");

        var results = dispatcher.Drain(10);
        results.Should().ContainSingle();
        results[0].TranslatedText.Should().Be("你好 {playerName}");

        var key = TranslationCacheKey.Create(
            "Hello {playerName}",
            config.TargetLanguage,
            config.Provider,
            TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, out var cached).Should().BeTrue();
        cached.Should().Be("你好 {playerName}");
    }

    private sealed class DelayedProvider : ITranslationProvider
    {
        private int _current;
        private readonly TimeSpan _delay;
        public int MaxObservedConcurrency { get; private set; }
        public ProviderKind Kind => ProviderKind.OpenAI;

        public DelayedProvider(TimeSpan delay) => _delay = delay;

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
}
