using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Dispatching;
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
}
