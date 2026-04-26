using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Glossary;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Prompts;
using HUnityAutoTranslator.Core.Providers;
using HUnityAutoTranslator.Core.Text;
using System.Diagnostics;

namespace HUnityAutoTranslator.Core.Queueing;

public sealed class TranslationWorkerPool
{
    private static readonly TimeSpan IdleWorkerPollInterval = TimeSpan.FromMilliseconds(40);

    private readonly TranslationJobQueue _queue;
    private readonly ResultDispatcher _dispatcher;
    private readonly ITranslationProvider _provider;
    private readonly ProviderRateLimiter _limiter;
    private readonly RuntimeConfig _config;
    private readonly ITranslationCache? _cache;
    private readonly ControlPanelMetrics? _metrics;
    private readonly IGlossaryStore? _glossary;

    public TranslationWorkerPool(
        TranslationJobQueue queue,
        ResultDispatcher dispatcher,
        ITranslationProvider provider,
        ProviderRateLimiter limiter,
        RuntimeConfig config,
        ITranslationCache? cache = null,
        ControlPanelMetrics? metrics = null,
        IGlossaryStore? glossary = null)
    {
        _queue = queue;
        _dispatcher = dispatcher;
        _provider = provider;
        _limiter = limiter;
        _config = config;
        _cache = cache;
        _metrics = metrics;
        _glossary = glossary;
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
                if (_queue.InFlightCount == 0)
                {
                    return;
                }

                await Task.Delay(IdleWorkerPollInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var completedCount = 0;
            foreach (var _ in batch)
            {
                _metrics?.RecordTranslationStarted();
            }

            try
            {
                await _limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
                var targetLanguage = ResolveTargetLanguage(batch);
                var contextExamples = BuildContextExamples(batch, targetLanguage);
                var glossaryTerms = BuildGlossaryTerms(batch, targetLanguage);
                var protectedTexts = batch.Select(job => PlaceholderProtector.Protect(job.SourceText)).ToArray();
                var request = new TranslationRequest(
                    protectedTexts.Select(text => text.Text).ToArray(),
                    targetLanguage,
                    PromptBuilder.BuildSystemPrompt(new PromptOptions(
                        targetLanguage,
                        _config.Style,
                        _config.CustomInstruction,
                        _config.CustomPrompt,
                        glossaryTerms.Count > 0)),
                    PromptBuilder.BuildBatchUserPrompt(protectedTexts.Select(text => text.Text).ToArray(), contextExamples, glossaryTerms));
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
                        restoredTexts = await RepairGlossaryFailuresAsync(
                            batch,
                            protectedTexts,
                            restoredTexts,
                            glossaryTerms,
                            targetLanguage,
                            cancellationToken).ConfigureAwait(false);
                        var glossaryValidation = GlossaryOutputValidator.ValidateBatch(
                            batch.Select(job => job.SourceText).ToArray(),
                            restoredTexts,
                            glossaryTerms);
                        if (glossaryValidation.IsValid &&
                            TranslationOutputValidator.ValidateBatch(batch.Select(job => job.SourceText).ToArray(), restoredTexts).IsValid)
                        {
                            completedCount = PublishResults(batch, restoredTexts, response.TotalTokens, stopwatch.Elapsed);
                        }
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
                ResolveTargetLanguage(jobs[i]),
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
                ResolveTargetLanguage(jobs[i]),
                _config.Provider.Kind.ToString(),
                _config.Provider.Model,
                jobs[i].Context.ComponentHierarchy ?? jobs[i].Context.SceneName ?? jobs[i].Context.ComponentType,
                DateTimeOffset.UtcNow),
                itemTokens,
                itemElapsed);
        }

        return count;
    }

    private async Task<IReadOnlyList<string>> RepairGlossaryFailuresAsync(
        IReadOnlyList<TranslationJob> jobs,
        IReadOnlyList<ProtectedText> protectedTexts,
        IReadOnlyList<string> translatedTexts,
        IReadOnlyList<GlossaryPromptTerm> glossaryTerms,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (glossaryTerms.Count == 0)
        {
            return translatedTexts;
        }

        var repaired = translatedTexts.ToArray();
        var count = Math.Min(jobs.Count, repaired.Length);
        for (var i = 0; i < count; i++)
        {
            var terms = glossaryTerms.Where(term => term.TextIndex == i).ToArray();
            if (terms.Length == 0)
            {
                continue;
            }

            var validation = GlossaryOutputValidator.ValidateSingle(jobs[i].SourceText, repaired[i], terms);
            if (validation.IsValid)
            {
                continue;
            }

            var request = new TranslationRequest(
                new[] { protectedTexts[i].Text },
                targetLanguage,
                PromptBuilder.BuildSystemPrompt(new PromptOptions(
                    targetLanguage,
                    _config.Style,
                    _config.CustomInstruction,
                    _config.CustomPrompt,
                    HasGlossaryTerms: true)),
                "The previous translation missed required glossary terms.\n" +
                PromptBuilder.BuildRepairPrompt(jobs[i].SourceText, repaired[i], validation.Reason, terms));
            var response = await _provider.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.Succeeded || response.TranslatedTexts.Count == 0)
            {
                continue;
            }

            repaired[i] = protectedTexts[i].Restore(response.TranslatedTexts[0]);
        }

        return repaired;
    }

    private IReadOnlyList<TranslationContextExample> BuildContextExamples(IReadOnlyList<TranslationJob> jobs, string targetLanguage)
    {
        if (!_config.EnableTranslationContext || _cache == null || jobs.Count == 0)
        {
            return Array.Empty<TranslationContextExample>();
        }

        var selected = new List<TranslationContextExample>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var usedCharacters = 0;
        foreach (var job in jobs)
        {
            var examples = _cache.GetTranslationContextExamples(
                job.SourceText,
                ResolveTargetLanguage(job),
                job.Context,
                _config.TranslationContextMaxExamples,
                _config.TranslationContextMaxCharacters);
            foreach (var example in examples)
            {
                if (selected.Count >= _config.TranslationContextMaxExamples)
                {
                    return selected;
                }

                var key = example.SourceText + "\u001f" + example.TranslatedText;
                if (!seen.Add(key))
                {
                    continue;
                }

                var nextCharacters = usedCharacters + example.SourceText.Length + example.TranslatedText.Length;
                if (nextCharacters > _config.TranslationContextMaxCharacters)
                {
                    continue;
                }

                selected.Add(example);
                usedCharacters = nextCharacters;
            }
        }

        return selected;
    }

    private IReadOnlyList<GlossaryPromptTerm> BuildGlossaryTerms(IReadOnlyList<TranslationJob> jobs, string targetLanguage)
    {
        if (!_config.EnableGlossary || _glossary == null || jobs.Count == 0)
        {
            return Array.Empty<GlossaryPromptTerm>();
        }

        return GlossaryMatcher.MatchTerms(
            jobs.Select(job => job.SourceText).ToArray(),
            _glossary.GetEnabledTerms(targetLanguage),
            Math.Min(100, Math.Max(0, _config.GlossaryMaxTerms)),
            Math.Min(8000, Math.Max(0, _config.GlossaryMaxCharacters)));
    }

    private string ResolveTargetLanguage(TranslationJob job)
    {
        return string.IsNullOrWhiteSpace(job.TargetLanguage)
            ? _config.TargetLanguage
            : job.TargetLanguage;
    }

    private string ResolveTargetLanguage(IReadOnlyList<TranslationJob> jobs)
    {
        return jobs.Count == 0 ? _config.TargetLanguage : ResolveTargetLanguage(jobs[0]);
    }
}
