using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Prompts;
using HUnityAutoTranslator.Core.Providers;
using HUnityAutoTranslator.Core.Text;
using System.Diagnostics;

namespace HUnityAutoTranslator.Core.Queueing;

public sealed class TranslationWorkerPool
{
    private readonly TranslationJobQueue _queue;
    private readonly ResultDispatcher _dispatcher;
    private readonly ITranslationProvider _provider;
    private readonly ProviderRateLimiter _limiter;
    private readonly RuntimeConfig _config;
    private readonly ITranslationCache? _cache;
    private readonly ControlPanelMetrics? _metrics;

    public TranslationWorkerPool(
        TranslationJobQueue queue,
        ResultDispatcher dispatcher,
        ITranslationProvider provider,
        ProviderRateLimiter limiter,
        RuntimeConfig config,
        ITranslationCache? cache = null,
        ControlPanelMetrics? metrics = null)
    {
        _queue = queue;
        _dispatcher = dispatcher;
        _provider = provider;
        _limiter = limiter;
        _config = config;
        _cache = cache;
        _metrics = metrics;
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

            var completedCount = 0;
            foreach (var _ in batch)
            {
                _metrics?.RecordTranslationStarted();
            }

            try
            {
                await _limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
                var protectedTexts = batch.Select(job => PlaceholderProtector.Protect(job.SourceText)).ToArray();
                var request = new TranslationRequest(
                    protectedTexts.Select(text => text.Text).ToArray(),
                    _config.TargetLanguage,
                    PromptBuilder.BuildSystemPrompt(new PromptOptions(_config.TargetLanguage, _config.Style, _config.CustomInstruction, _config.CustomPrompt)),
                    PromptBuilder.BuildBatchUserPrompt(protectedTexts.Select(text => text.Text).ToArray()));
                var stopwatch = Stopwatch.StartNew();
                var response = await _provider.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                if (response.Succeeded)
                {
                    var restoredTexts = RestoreTokens(protectedTexts, response.TranslatedTexts);
                    var validation = TranslationOutputValidator.ValidateBatch(
                        batch.Select(job => job.SourceText).ToArray(),
                        restoredTexts);
                    if (validation.IsValid)
                    {
                        completedCount = PublishResults(batch, restoredTexts, response.TotalTokens, stopwatch.Elapsed);
                    }
                }
            }
            finally
            {
                for (var i = completedCount; i < batch.Count; i++)
                {
                    _metrics?.RecordTranslationFinishedWithoutResult();
                }

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

    private int PublishResults(IReadOnlyList<TranslationJob> jobs, IReadOnlyList<string> translatedTexts, int totalTokens, TimeSpan elapsed)
    {
        var count = Math.Min(jobs.Count, translatedTexts.Count);
        for (var i = 0; i < count; i++)
        {
            var cacheKey = TranslationCacheKey.Create(
                jobs[i].SourceText,
                _config.TargetLanguage,
                _config.Provider,
                TextPipeline.PromptPolicyVersion);
            _cache?.Set(cacheKey, translatedTexts[i], jobs[i].Context);
            if (jobs[i].PublishResult)
            {
                _dispatcher.Publish(new TranslationResult(
                    jobs[i].Id,
                    jobs[i].SourceText,
                    translatedTexts[i],
                    (int)jobs[i].Priority));
            }

            var itemTokens = totalTokens <= 0 ? 0 : totalTokens / count + (i == 0 ? totalTokens % count : 0);
            var itemElapsed = elapsed <= TimeSpan.Zero ? (TimeSpan?)null : TimeSpan.FromTicks(Math.Max(1, elapsed.Ticks / count));
            _metrics?.RecordTranslationCompleted(new RecentTranslationPreview(
                jobs[i].SourceText,
                translatedTexts[i],
                _config.TargetLanguage,
                _config.Provider.Kind.ToString(),
                _config.Provider.Model,
                jobs[i].Context.ComponentHierarchy ?? jobs[i].Context.SceneName ?? jobs[i].Context.ComponentType,
                DateTimeOffset.UtcNow),
                itemTokens,
                itemElapsed);
        }

        return count;
    }
}
