using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Prompts;
using HUnityAutoTranslator.Core.Providers;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Queueing;

public sealed class TranslationWorkerPool
{
    private readonly TranslationJobQueue _queue;
    private readonly ResultDispatcher _dispatcher;
    private readonly ITranslationProvider _provider;
    private readonly ProviderRateLimiter _limiter;
    private readonly RuntimeConfig _config;
    private readonly ITranslationCache? _cache;

    public TranslationWorkerPool(
        TranslationJobQueue queue,
        ResultDispatcher dispatcher,
        ITranslationProvider provider,
        ProviderRateLimiter limiter,
        RuntimeConfig config,
        ITranslationCache? cache = null)
    {
        _queue = queue;
        _dispatcher = dispatcher;
        _provider = provider;
        _limiter = limiter;
        _config = config;
        _cache = cache;
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
                var protectedTexts = batch.Select(job => PlaceholderProtector.Protect(job.SourceText)).ToArray();
                var request = new TranslationRequest(
                    protectedTexts.Select(text => text.Text).ToArray(),
                    _config.TargetLanguage,
                    PromptBuilder.BuildSystemPrompt(new PromptOptions(_config.TargetLanguage, _config.Style, null)),
                    PromptBuilder.BuildBatchUserPrompt(protectedTexts.Select(text => text.Text).ToArray()));
                var response = await _provider.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.Succeeded)
                {
                    var restoredTexts = RestoreTokens(protectedTexts, response.TranslatedTexts);
                    var validation = TranslationOutputValidator.ValidateBatch(
                        batch.Select(job => job.SourceText).ToArray(),
                        restoredTexts);
                    if (validation.IsValid)
                    {
                        PublishResults(batch, restoredTexts);
                    }
                }
            }
            finally
            {
                _queue.MarkCompleted(batch);
            }
        }
    }

    private static IReadOnlyList<string> RestoreTokens(IReadOnlyList<ProtectedText> protectedTexts, IReadOnlyList<string> translatedTexts)
    {
        var count = Math.Min(protectedTexts.Count, translatedTexts.Count);
        var restored = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            restored.Add(protectedTexts[i].Restore(translatedTexts[i]));
        }

        return restored;
    }

    private void PublishResults(IReadOnlyList<TranslationJob> jobs, IReadOnlyList<string> translatedTexts)
    {
        var count = Math.Min(jobs.Count, translatedTexts.Count);
        for (var i = 0; i < count; i++)
        {
            var cacheKey = TranslationCacheKey.Create(
                jobs[i].SourceText,
                _config.TargetLanguage,
                _config.Provider,
                TextPipeline.PromptPolicyVersion);
            _cache?.Set(cacheKey, translatedTexts[i]);
            _dispatcher.Publish(new TranslationResult(
                jobs[i].Id,
                jobs[i].SourceText,
                translatedTexts[i],
                (int)jobs[i].Priority));
        }
    }
}
