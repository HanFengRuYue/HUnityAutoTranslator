using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Providers;

namespace HUnityAutoTranslator.Core.Queueing;

public sealed class TranslationWorkerPool
{
    private readonly TranslationJobQueue _queue;
    private readonly ResultDispatcher _dispatcher;
    private readonly ITranslationProvider _provider;
    private readonly ProviderRateLimiter _limiter;
    private readonly RuntimeConfig _config;

    public TranslationWorkerPool(
        TranslationJobQueue queue,
        ResultDispatcher dispatcher,
        ITranslationProvider provider,
        ProviderRateLimiter limiter,
        RuntimeConfig config)
    {
        _queue = queue;
        _dispatcher = dispatcher;
        _provider = provider;
        _limiter = limiter;
        _config = config;
    }

    public async Task RunUntilIdleAsync(CancellationToken cancellationToken)
    {
        var workerCount = Math.Max(1, _config.MaxConcurrentRequests);
        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => RunWorkerAsync(cancellationToken))
            .ToArray();

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_queue.TryDequeueBatch(1, _config.MaxBatchCharacters, out var batch))
            {
                return;
            }

            try
            {
                await _limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
                var request = new TranslationRequest(
                    batch.Select(job => job.SourceText).ToArray(),
                    _config.TargetLanguage,
                    string.Empty,
                    string.Empty);
                var response = await _provider.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.Succeeded)
                {
                    PublishResults(batch, response.TranslatedTexts);
                }
            }
            finally
            {
                _queue.MarkCompleted(batch);
            }
        }
    }

    private void PublishResults(IReadOnlyList<TranslationJob> jobs, IReadOnlyList<string> translatedTexts)
    {
        var count = Math.Min(jobs.Count, translatedTexts.Count);
        for (var i = 0; i < count; i++)
        {
            _dispatcher.Publish(new TranslationResult(
                jobs[i].Id,
                jobs[i].SourceText,
                translatedTexts[i],
                (int)jobs[i].Priority));
        }
    }
}
