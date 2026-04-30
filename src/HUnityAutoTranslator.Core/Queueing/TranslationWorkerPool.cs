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
using System.Text;
using System.Text.RegularExpressions;

namespace HUnityAutoTranslator.Core.Queueing;

public sealed class TranslationWorkerPool
{
    private static readonly TimeSpan IdleWorkerPollInterval = TimeSpan.FromMilliseconds(40);
    private const int MaxBatchItems = 4;
    private const int MaxQualityRetryCount = 3;

    private readonly TranslationJobQueue _queue;
    private readonly ResultDispatcher _dispatcher;
    private readonly ITranslationProvider _provider;
    private readonly ProviderRateLimiter _limiter;
    private readonly RuntimeConfig _config;
    private readonly ITranslationCache? _cache;
    private readonly ControlPanelMetrics? _metrics;
    private readonly IGlossaryStore? _glossary;
    private readonly Action<string>? _failureReporter;
    private readonly Action<TranslationJob>? _qualityRetryLimitReporter;
    private readonly Action<TranslationRequestDebugSnapshot>? _debugReporter;

    public TranslationWorkerPool(
        TranslationJobQueue queue,
        ResultDispatcher dispatcher,
        ITranslationProvider provider,
        ProviderRateLimiter limiter,
        RuntimeConfig config,
        ITranslationCache? cache = null,
        ControlPanelMetrics? metrics = null,
        IGlossaryStore? glossary = null,
        Action<string>? failureReporter = null,
        Action<TranslationJob>? qualityRetryLimitReporter = null,
        Action<TranslationRequestDebugSnapshot>? debugReporter = null)
    {
        _queue = queue;
        _dispatcher = dispatcher;
        _provider = provider;
        _limiter = limiter;
        _config = config;
        _cache = cache;
        _metrics = metrics;
        _glossary = glossary;
        _failureReporter = failureReporter;
        _qualityRetryLimitReporter = qualityRetryLimitReporter;
        _debugReporter = debugReporter;
    }

    public async Task RunUntilIdleAsync(CancellationToken cancellationToken)
    {
        var workerCount = _config.EffectiveMaxConcurrentRequests;
        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => RunWorkerAsync(cancellationToken))
            .ToArray();

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_queue.TryDequeueBatch(ResolveBatchItemLimit(), _config.MaxBatchCharacters, out var batch))
            {
                if (_queue.InFlightCount == 0)
                {
                    return;
                }

                await Task.Delay(IdleWorkerPollInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            IReadOnlyList<TranslationJob> retryJobs = Array.Empty<TranslationJob>();
            _metrics?.RecordTranslationStarted();
            try
            {
                await _limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
                var targetLanguage = ResolveTargetLanguage(batch);
                var contextExamples = BuildContextExamples(batch, targetLanguage);
                var glossaryTerms = BuildGlossaryTerms(batch, targetLanguage);
                var richTextPreparations = batch.Select(job => RichTextTranslationPreprocessor.Prepare(job.SourceText)).ToArray();
                var protectedTexts = richTextPreparations.Select(item => PlaceholderProtector.Protect(item.ProviderText)).ToArray();
                var itemContexts = BuildItemContexts(batch);
                var request = new TranslationRequest(
                    protectedTexts.Select(text => text.Text).ToArray(),
                    targetLanguage,
                    PromptBuilder.BuildSystemPrompt(new PromptOptions(
                        targetLanguage,
                        _config.Style,
                        _config.CustomPrompt,
                        glossaryTerms.Count > 0,
                        _config.GameTitle,
                        _config.PromptTemplates)),
                    PromptBuilder.BuildBatchUserPrompt(
                        protectedTexts.Select(text => text.Text).ToArray(),
                        contextExamples,
                        glossaryTerms,
                        itemContexts,
                        _config.GameTitle,
                        _config.PromptTemplates));
                ReportDebugSnapshot(
                    "translate",
                    batch,
                    itemContexts,
                    contextExamples.Count,
                    glossaryTerms.Count,
                    repairReason: null);
                var stopwatch = Stopwatch.StartNew();
                var response = await _provider.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                if (response.Succeeded)
                {
                    retryJobs = await HandleSuccessfulResponseAsync(
                        batch,
                        richTextPreparations,
                        protectedTexts,
                        response,
                        itemContexts,
                        glossaryTerms,
                        targetLanguage,
                        stopwatch.Elapsed,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    ReportFailure(batch, $"翻译服务请求失败：{response.ErrorMessage ?? "未知错误"}");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ReportFailure(batch, $"翻译请求处理异常：{ex.Message}");
            }
            finally
            {
                _metrics?.RecordTranslationRequestFinished();
                _queue.MarkCompleted(batch);
                foreach (var retryJob in retryJobs)
                {
                    _queue.EnqueueDeferred(retryJob);
                }
            }
        }
    }

    private static IReadOnlyList<string> RestoreTokens(
        IReadOnlyList<TranslationJob> jobs,
        IReadOnlyList<RichTextTranslationPreparation> richTextPreparations,
        IReadOnlyList<ProtectedText> protectedTexts,
        IReadOnlyList<string> translatedTexts)
    {
        var count = Math.Min(protectedTexts.Count, translatedTexts.Count);
        var restored = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            restored.Add(RestoreTokens(jobs[i], richTextPreparations[i], protectedTexts[i], translatedTexts[i]));
        }

        return restored;
    }

    private static string RestoreTokens(
        TranslationJob job,
        RichTextTranslationPreparation richTextPreparation,
        ProtectedText protectedText,
        string translatedText)
    {
        var restored = protectedText.Restore(translatedText);
        restored = richTextPreparation.RebuildTranslation(restored);
        return NormalizeEscapedControlCharacters(job.SourceText, restored);
    }

    private static string NormalizeEscapedControlCharacters(string sourceText, string translatedText)
    {
        var normalized = translatedText;
        var sourceNewLine = GetSourceNewLine(sourceText);
        if (sourceNewLine != null)
        {
            normalized = normalized
                .Replace("\\r\\n", sourceNewLine, StringComparison.Ordinal)
                .Replace("\\n", sourceNewLine, StringComparison.Ordinal)
                .Replace("\\r", sourceNewLine, StringComparison.Ordinal);
        }

        if (sourceText.Contains('\t'))
        {
            normalized = normalized.Replace("\\t", "\t", StringComparison.Ordinal);
        }

        return normalized;
    }

    private static string? GetSourceNewLine(string sourceText)
    {
        if (sourceText.Contains("\r\n", StringComparison.Ordinal))
        {
            return "\r\n";
        }

        if (sourceText.Contains('\n'))
        {
            return "\n";
        }

        return sourceText.Contains('\r') ? "\r" : null;
    }

    private async Task<IReadOnlyList<TranslationJob>> HandleSuccessfulResponseAsync(
        IReadOnlyList<TranslationJob> jobs,
        IReadOnlyList<RichTextTranslationPreparation> richTextPreparations,
        IReadOnlyList<ProtectedText> protectedTexts,
        TranslationResponse response,
        IReadOnlyList<PromptItemContext> itemContexts,
        IReadOnlyList<GlossaryPromptTerm> glossaryTerms,
        string targetLanguage,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        var translatedTexts = NormalizeProviderResponseShape(jobs, response.TranslatedTexts);
        if (translatedTexts.Count != jobs.Count)
        {
            ReportFailure(jobs, $"翻译结果未通过格式检查：批量翻译数量不匹配");
            return Array.Empty<TranslationJob>();
        }

        var restoredTexts = RestoreTokens(jobs, richTextPreparations, protectedTexts, translatedTexts);
        restoredTexts = await RepairGlossaryFailuresAsync(
            jobs,
            richTextPreparations,
            protectedTexts,
            restoredTexts,
            glossaryTerms,
            targetLanguage,
            cancellationToken).ConfigureAwait(false);
        restoredTexts = await RepairQualityFailuresAsync(
            jobs,
            richTextPreparations,
            protectedTexts,
            restoredTexts,
            itemContexts,
            targetLanguage,
            cancellationToken).ConfigureAwait(false);

        var failures = ValidateFinalTranslations(jobs, restoredTexts, itemContexts, glossaryTerms, targetLanguage);
        var publishJobs = new List<TranslationJob>();
        var publishTexts = new List<string>();
        var retryJobs = new List<TranslationJob>();
        for (var i = 0; i < jobs.Count; i++)
        {
            if (failures.TryGetValue(i, out var failure))
            {
                if (failure.IsQualityFailure && jobs[i].QualityRetryCount < MaxQualityRetryCount)
                {
                    var retryJob = jobs[i] with { QualityRetryCount = jobs[i].QualityRetryCount + 1 };
                    retryJobs.Add(retryJob);
                    ReportFinalFailure(
                        jobs[i],
                        failure,
                        $"重试：{retryJob.QualityRetryCount}/{MaxQualityRetryCount}，已加入等待翻译队列。");
                }
                else
                {
                    if (failure.IsQualityFailure)
                    {
                        _qualityRetryLimitReporter?.Invoke(jobs[i]);
                    }

                    var retryStatus = failure.IsQualityFailure
                        ? $"重试：{jobs[i].QualityRetryCount}/{MaxQualityRetryCount}，已达上限，保留为待翻译。"
                        : null;
                    ReportFinalFailure(jobs[i], failure, retryStatus);
                }

                continue;
            }

            publishJobs.Add(jobs[i]);
            publishTexts.Add(restoredTexts[i]);
        }

        if (publishJobs.Count > 0)
        {
            PublishResults(publishJobs, publishTexts, response, elapsed);
        }

        return retryJobs;
    }

    private Dictionary<int, TranslationFinalFailure> ValidateFinalTranslations(
        IReadOnlyList<TranslationJob> jobs,
        IReadOnlyList<string> translatedTexts,
        IReadOnlyList<PromptItemContext> itemContexts,
        IReadOnlyList<GlossaryPromptTerm> glossaryTerms,
        string targetLanguage)
    {
        var failures = new Dictionary<int, TranslationFinalFailure>();
        var contextByIndex = itemContexts.GroupBy(context => context.TextIndex)
            .ToDictionary(group => group.Key, group => group.First());
        for (var i = 0; i < jobs.Count; i++)
        {
            var outputValidation = TranslationOutputValidator.ValidateSingle(
                jobs[i].SourceText,
                translatedTexts[i],
                requireSameRichTextTags: true);
            if (!outputValidation.IsValid)
            {
                failures[i] = CreateFinalFailure(
                    jobs,
                    translatedTexts,
                    contextByIndex,
                    i,
                    $"翻译结果未通过格式检查：{outputValidation.Reason}");
                continue;
            }

            var terms = glossaryTerms.Where(term => term.TextIndex == i).ToArray();
            var glossaryValidation = GlossaryOutputValidator.ValidateSingle(jobs[i].SourceText, translatedTexts[i], terms);
            if (!glossaryValidation.IsValid)
            {
                failures[i] = CreateFinalFailure(
                    jobs,
                    translatedTexts,
                    contextByIndex,
                    i,
                    $"翻译结果未通过术语检查：{glossaryValidation.Reason}");
            }
        }

        foreach (var failure in TranslationQualityValidator.FindFailures(
            jobs.Select(job => job.SourceText).ToArray(),
            translatedTexts,
            itemContexts,
            targetLanguage,
            _config.GameTitle))
        {
            failures.TryAdd(
                failure.TextIndex,
                CreateFinalFailure(
                    jobs,
                    translatedTexts,
                    contextByIndex,
                    failure.TextIndex,
                    "translation quality check failed: " + failure.Reason));
        }

        return failures;
    }

    private TranslationFinalFailure CreateFinalFailure(
        IReadOnlyList<TranslationJob> jobs,
        IReadOnlyList<string> translatedTexts,
        IReadOnlyDictionary<int, PromptItemContext> contextByIndex,
        int index,
        string reason)
    {
        var context = ResolveItemContext(jobs[index], index, contextByIndex);
        return new TranslationFinalFailure(
            reason,
            translatedTexts[index],
            context,
            PromptItemClassifier.BuildHints(jobs[index].SourceText, context, _config.GameTitle));
    }

    private void ReportFailure(IReadOnlyList<TranslationJob> jobs, string reason)
    {
        if (_failureReporter == null)
        {
            return;
        }

        _failureReporter($"{reason}。源文本预览：{BuildSourcePreview(jobs)}");
    }

    private void ReportFinalFailure(TranslationJob job, TranslationFinalFailure failure, string? retryStatus)
    {
        if (_failureReporter == null)
        {
            return;
        }

        var retryText = string.IsNullOrWhiteSpace(retryStatus) ? string.Empty : "。" + retryStatus;
        _failureReporter(
            $"质量失败：{LocalizeFailureReason(failure.Reason)}。" +
            $"原文：{BuildTextPreview(job.SourceText)}。" +
            $"译文：{BuildTextPreview(failure.CandidateTranslation)}" +
            retryText);
    }

    private void ReportDebugSnapshot(
        string phase,
        IReadOnlyList<TranslationJob> jobs,
        IReadOnlyList<PromptItemContext> itemContexts,
        int contextExampleCount,
        int glossaryTermCount,
        string? repairReason,
        IReadOnlyList<string>? candidateTranslations = null,
        IReadOnlyDictionary<int, string>? qualityFailureReasons = null)
    {
        if (_debugReporter == null)
        {
            return;
        }

        var contextByIndex = itemContexts.GroupBy(context => context.TextIndex)
            .ToDictionary(group => group.Key, group => group.First());
        var items = new List<TranslationRequestDebugItem>(jobs.Count);
        for (var i = 0; i < jobs.Count; i++)
        {
            var effectiveContext = ResolveItemContext(jobs[i], i, contextByIndex);
            string? qualityFailureReason = null;
            qualityFailureReasons?.TryGetValue(i, out qualityFailureReason);
            items.Add(new TranslationRequestDebugItem(
                i,
                jobs[i].SourceText,
                effectiveContext.SceneName,
                effectiveContext.ComponentHierarchy,
                effectiveContext.ComponentType,
                PromptItemClassifier.BuildHints(jobs[i].SourceText, effectiveContext, _config.GameTitle),
                candidateTranslations != null && i < candidateTranslations.Count ? candidateTranslations[i] : null,
                qualityFailureReason,
                jobs[i].QualityRetryCount));
        }

        _debugReporter(new TranslationRequestDebugSnapshot(
            phase,
            TextPipeline.GetPromptPolicyVersion(_config),
            ResolveTargetLanguage(jobs),
            _config.GameTitle,
            _config.Provider.Kind.ToString(),
            _config.Provider.Model,
            jobs.Count,
            contextExampleCount,
            glossaryTermCount,
            itemContexts.Count > 0,
            QualityRulesEnabled: true,
            repairReason,
            items));
    }

    private static PromptItemContext ResolveItemContext(
        TranslationJob job,
        int index,
        IReadOnlyDictionary<int, PromptItemContext> contextByIndex)
    {
        return contextByIndex.TryGetValue(index, out var context)
            ? context
            : new PromptItemContext(
                index,
                job.Context.SceneName,
                job.Context.ComponentHierarchy,
                job.Context.ComponentType);
    }

    private static string BuildSourcePreview(IReadOnlyList<TranslationJob> jobs)
    {
        if (jobs.Count == 0)
        {
            return "<空>";
        }

        var source = jobs[0].SourceText
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        return source.Length <= 160 ? source : source[..160] + "...";
    }

    private static string BuildTextPreview(string? value)
    {
        var preview = (value ?? string.Empty)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        return preview.Length <= 160 ? preview : preview[..160] + "...";
    }

    private static string LocalizeFailureReason(string reason)
    {
        const string qualityPrefix = "translation quality check failed: ";
        var normalized = reason.StartsWith(qualityPrefix, StringComparison.OrdinalIgnoreCase)
            ? reason[qualityPrefix.Length..]
            : reason;
        return normalized switch
        {
            "game title must be preserved exactly when it appears in the source text" => "游戏标题未按原文保留",
            "translation added outer symbols that are not present in the source text" => "译文添加了原文没有的外层符号",
            "ordinary English UI text was left untranslated" => "普通英文 UI 文本未翻译",
            "settings value translation is too short or incomplete" => "设置值译文过短或不完整",
            "state text is too literal for a game UI setting" => "状态文本直译不适合游戏 UI",
            "different option texts under the same parent produced the same translation" => "同一父级下的不同选项被翻成了相同文本",
            "translation quality check could not match source and result counts" => "源文本和译文数量不一致",
            _ => reason
        };
    }

    private sealed record TranslationFinalFailure(
        string Reason,
        string CandidateTranslation,
        PromptItemContext Context,
        IReadOnlyList<string> Hints)
    {
        public bool IsQualityFailure =>
            Reason.StartsWith("translation quality check failed:", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> NormalizeProviderResponseShape(
        IReadOnlyList<TranslationJob> jobs,
        IReadOnlyList<string> translatedTexts)
    {
        if (jobs.Count == translatedTexts.Count ||
            jobs.Count != 1 ||
            translatedTexts.Count <= 1)
        {
            return translatedTexts;
        }

        return new[]
        {
            ReassembleSplitSingleItemResponse(jobs[0].SourceText, translatedTexts)
        };
    }

    private static string ReassembleSplitSingleItemResponse(
        string sourceText,
        IReadOnlyList<string> translatedTexts)
    {
        var segments = translatedTexts
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text.Trim())
            .ToArray();
        if (segments.Length == 0)
        {
            return string.Empty;
        }

        var lineReassembled = TryReassembleUsingSourceLineBreaks(sourceText, segments);
        if (lineReassembled != null)
        {
            return lineReassembled;
        }

        return string.Join(FindDominantSourceSeparator(sourceText), segments);
    }

    private static string? TryReassembleUsingSourceLineBreaks(
        string sourceText,
        IReadOnlyList<string> translatedSegments)
    {
        var parts = Regex.Matches(sourceText, @"[^\r\n]+|(?:\r\n|\r|\n)+")
            .Cast<Match>()
            .Select(match => match.Value)
            .ToArray();
        var sourceTextPartCount = parts.Count(part => !IsLineBreakRun(part));
        if (sourceTextPartCount != translatedSegments.Count)
        {
            return null;
        }

        var builder = new StringBuilder(sourceText.Length);
        var translatedIndex = 0;
        foreach (var part in parts)
        {
            if (IsLineBreakRun(part))
            {
                builder.Append(part);
                continue;
            }

            builder.Append(translatedSegments[translatedIndex++]);
        }

        return builder.ToString();
    }

    private static string FindDominantSourceSeparator(string sourceText)
    {
        var paragraphBreak = Regex.Match(sourceText, @"(?:\r\n|\r|\n){2,}");
        if (paragraphBreak.Success)
        {
            return paragraphBreak.Value;
        }

        var lineBreak = Regex.Match(sourceText, @"\r\n|\r|\n");
        return lineBreak.Success ? lineBreak.Value : "\n";
    }

    private static bool IsLineBreakRun(string value)
    {
        return value.All(character => character is '\r' or '\n');
    }

    private int PublishResults(
        IReadOnlyList<TranslationJob> jobs,
        IReadOnlyList<string> translatedTexts,
        TranslationResponse response,
        TimeSpan elapsed)
    {
        var provider = response.Provider ?? _config.Provider;
        var totalTokens = response.TotalTokens;
        var count = Math.Min(jobs.Count, translatedTexts.Count);
        for (var i = 0; i < count; i++)
        {
            var cacheKey = TranslationCacheKey.Create(
                jobs[i].SourceText,
                ResolveTargetLanguage(jobs[i]),
                provider,
                TextPipeline.GetPromptPolicyVersion(_config));
            _cache?.Set(cacheKey, translatedTexts[i], jobs[i].Context);
            if (jobs[i].PublishResult)
            {
                var resultUpdatedUtc = DateTimeOffset.UtcNow;
                _dispatcher.Publish(new TranslationResult(
                    jobs[i].Id,
                    jobs[i].SourceText,
                    translatedTexts[i],
                    (int)jobs[i].Priority,
                    sceneName: jobs[i].Context.SceneName,
                    componentHierarchy: jobs[i].Context.ComponentHierarchy,
                    componentType: jobs[i].Context.ComponentType,
                    updatedUtc: resultUpdatedUtc));
            }

            var itemTokens = totalTokens <= 0 ? 0 : totalTokens / count + (i == 0 ? totalTokens % count : 0);
            var itemElapsed = elapsed <= TimeSpan.Zero ? (TimeSpan?)null : TimeSpan.FromTicks(Math.Max(1, elapsed.Ticks / count));
            _metrics?.RecordTranslationCompleted(new RecentTranslationPreview(
                jobs[i].SourceText,
                translatedTexts[i],
                ResolveTargetLanguage(jobs[i]),
                provider.Kind.ToString(),
                provider.Model,
                jobs[i].Context.ComponentHierarchy ?? jobs[i].Context.SceneName ?? jobs[i].Context.ComponentType,
                DateTimeOffset.UtcNow,
                response.ProviderProfileId,
                response.ProviderProfileName,
                response.ProviderProfileId == null ? null : provider.Kind.ToString()),
                itemTokens,
                itemElapsed);
        }

        return count;
    }

    private async Task<IReadOnlyList<string>> RepairGlossaryFailuresAsync(
        IReadOnlyList<TranslationJob> jobs,
        IReadOnlyList<RichTextTranslationPreparation> richTextPreparations,
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
                    _config.CustomPrompt,
                    HasGlossaryTerms: true,
                    GameTitle: _config.GameTitle,
                    Templates: _config.PromptTemplates)),
                "The previous translation missed required glossary terms.\n" +
                PromptBuilder.BuildRepairPrompt(
                    richTextPreparations[i].ProviderText,
                    RichTextGuard.GetVisibleText(repaired[i]),
                    validation.Reason,
                    terms,
                    _config.PromptTemplates));
            var response = await _provider.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.Succeeded || response.TranslatedTexts.Count == 0)
            {
                continue;
            }

            repaired[i] = RestoreTokens(jobs[i], richTextPreparations[i], protectedTexts[i], response.TranslatedTexts[0]);
        }

        return repaired;
    }

    private async Task<IReadOnlyList<string>> RepairQualityFailuresAsync(
        IReadOnlyList<TranslationJob> jobs,
        IReadOnlyList<RichTextTranslationPreparation> richTextPreparations,
        IReadOnlyList<ProtectedText> protectedTexts,
        IReadOnlyList<string> translatedTexts,
        IReadOnlyList<PromptItemContext> itemContexts,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var failures = TranslationQualityValidator.FindFailures(
            jobs.Select(job => job.SourceText).ToArray(),
            translatedTexts,
            itemContexts,
            targetLanguage,
            _config.GameTitle);
        if (failures.Count == 0)
        {
            return translatedTexts;
        }

        var repaired = translatedTexts.ToArray();
        var contextByIndex = itemContexts.GroupBy(context => context.TextIndex)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var failure in failures.GroupBy(item => item.TextIndex).Select(group => group.First()))
        {
            if (failure.TextIndex < 0 || failure.TextIndex >= jobs.Count || failure.TextIndex >= protectedTexts.Count)
            {
                continue;
            }

            contextByIndex.TryGetValue(failure.TextIndex, out var context);
            var sameParentSourceTexts = BuildSameParentSourceTexts(jobs, itemContexts, failure.TextIndex);
            ReportDebugSnapshot(
                "quality-repair",
                new[] { jobs[failure.TextIndex] },
                context == null
                    ? Array.Empty<PromptItemContext>()
                    : new[] { new PromptItemContext(0, context.SceneName, context.ComponentHierarchy, context.ComponentType) },
                contextExampleCount: 0,
                glossaryTermCount: 0,
                LocalizeFailureReason(failure.Reason),
                new[] { repaired[failure.TextIndex] },
                new Dictionary<int, string> { [0] = LocalizeFailureReason(failure.Reason) });
            var request = new TranslationRequest(
                new[] { protectedTexts[failure.TextIndex].Text },
                targetLanguage,
                PromptBuilder.BuildSystemPrompt(new PromptOptions(
                    targetLanguage,
                    _config.Style,
                    _config.CustomPrompt,
                    HasGlossaryTerms: false,
                    GameTitle: _config.GameTitle,
                    Templates: _config.PromptTemplates)),
                PromptBuilder.BuildQualityRepairPrompt(
                    richTextPreparations[failure.TextIndex].ProviderText,
                    RichTextGuard.GetVisibleText(repaired[failure.TextIndex]),
                    failure.Reason,
                    context,
                    sameParentSourceTexts,
                    _config.GameTitle,
                    _config.PromptTemplates));
            var response = await _provider.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.Succeeded || response.TranslatedTexts.Count == 0)
            {
                continue;
            }

            repaired[failure.TextIndex] = RestoreTokens(
                jobs[failure.TextIndex],
                richTextPreparations[failure.TextIndex],
                protectedTexts[failure.TextIndex],
                response.TranslatedTexts[0]);
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

    private static IReadOnlyList<string> BuildSameParentSourceTexts(
        IReadOnlyList<TranslationJob> jobs,
        IReadOnlyList<PromptItemContext> itemContexts,
        int textIndex)
    {
        var contextByIndex = itemContexts.GroupBy(context => context.TextIndex)
            .ToDictionary(group => group.Key, group => group.First());
        if (!contextByIndex.TryGetValue(textIndex, out var currentContext))
        {
            return Array.Empty<string>();
        }

        var currentParent = PromptItemClassifier.GetParentHierarchy(currentContext.ComponentHierarchy);
        if (currentParent == null)
        {
            return Array.Empty<string>();
        }

        var sources = new List<string>();
        for (var i = 0; i < jobs.Count; i++)
        {
            if (!contextByIndex.TryGetValue(i, out var context) ||
                !string.Equals(currentParent, PromptItemClassifier.GetParentHierarchy(context.ComponentHierarchy), StringComparison.Ordinal))
            {
                continue;
            }

            sources.Add(RichTextGuard.GetVisibleText(jobs[i].SourceText));
        }

        return sources;
    }

    private static IReadOnlyList<PromptItemContext> BuildItemContexts(IReadOnlyList<TranslationJob> jobs)
    {
        var contexts = new List<PromptItemContext>(jobs.Count);
        for (var i = 0; i < jobs.Count; i++)
        {
            var context = jobs[i].Context;
            if (string.IsNullOrWhiteSpace(context.SceneName) &&
                string.IsNullOrWhiteSpace(context.ComponentHierarchy) &&
                string.IsNullOrWhiteSpace(context.ComponentType))
            {
                continue;
            }

            contexts.Add(new PromptItemContext(
                i,
                context.SceneName,
                context.ComponentHierarchy,
                context.ComponentType));
        }

        return contexts;
    }

    private int ResolveBatchItemLimit()
    {
        var pendingCount = Math.Max(0, _queue.PendingCount);
        var workerCount = Math.Max(1, _config.EffectiveMaxConcurrentRequests);
        if (pendingCount <= workerCount)
        {
            return 1;
        }

        var balancedBatchSize = (int)Math.Ceiling((double)pendingCount / workerCount);
        return Math.Min(MaxBatchItems, Math.Max(1, balancedBatchSize));
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
