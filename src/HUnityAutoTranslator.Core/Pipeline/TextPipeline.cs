using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Glossary;
using HUnityAutoTranslator.Core.Prompts;
using HUnityAutoTranslator.Core.Queueing;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Pipeline;

public sealed class TextPipeline
{
    public const string PromptPolicyVersion = "prompt-v5";

    private readonly ITranslationCache _cache;
    private readonly TranslationJobQueue _queue;
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly ControlPanelMetrics? _metrics;
    private readonly IGlossaryStore? _glossary;

    public TextPipeline(
        ITranslationCache cache,
        TranslationJobQueue queue,
        RuntimeConfig config,
        ControlPanelMetrics? metrics = null,
        IGlossaryStore? glossary = null)
        : this(cache, queue, () => config, metrics, glossary)
    {
    }

    public TextPipeline(
        ITranslationCache cache,
        TranslationJobQueue queue,
        Func<RuntimeConfig> configProvider,
        ControlPanelMetrics? metrics = null,
        IGlossaryStore? glossary = null)
    {
        _cache = cache;
        _queue = queue;
        _configProvider = configProvider;
        _metrics = metrics;
        _glossary = glossary;
    }

    public PipelineDecision Process(CapturedText capturedText)
    {
        var config = _configProvider();
        if (!config.Enabled ||
            capturedText.SourceText.Length > config.MaxSourceTextLength ||
            (config.IgnoreInvisibleText && !capturedText.IsVisible) ||
            !TextFilter.ShouldTranslate(capturedText.SourceText))
        {
            return PipelineDecision.Ignored();
        }

        var key = TranslationCacheKey.Create(capturedText.SourceText, config.TargetLanguage, config.Provider, GetPromptPolicyVersion(config));
        _metrics?.RecordCaptured(key);
        if (config.EnableCacheLookup &&
            _cache.TryGet(key, capturedText.Context, out var translatedText))
        {
            var cachedTranslationValidation = ValidateCachedTranslation(capturedText, translatedText, config);
            if (CachedTranslationSatisfiesGlossary(capturedText.SourceText, translatedText, config) &&
                cachedTranslationValidation.IsValid)
            {
                return PipelineDecision.UseCachedTranslation(translatedText);
            }

            if (!cachedTranslationValidation.IsValid)
            {
                MarkCachedTranslationPending(key, capturedText.Context);
            }
        }

        if (config.EnableCacheLookup &&
            TranslationCacheReuse.TryGetReusableTranslation(
                _cache,
                key,
                capturedText.Context,
                config,
                _glossary,
                out var reusableTranslatedText))
        {
            _cache.Set(key, reusableTranslatedText, capturedText.Context);
            return PipelineDecision.UseCachedTranslation(reusableTranslatedText);
        }

        var enqueued = _queue.Enqueue(TranslationJob.Create(
            capturedText.TargetId,
            capturedText.SourceText,
            capturedText.IsVisible ? TranslationPriority.VisibleUi : TranslationPriority.Normal,
            capturedText.Context,
            publishResult: capturedText.PublishResult));
        if (enqueued)
        {
            _cache.RecordCaptured(key, capturedText.Context);
            _metrics?.RecordQueued();
        }

        return PipelineDecision.Queued();
    }

    private bool CachedTranslationSatisfiesGlossary(string sourceText, string translatedText, RuntimeConfig config)
    {
        if (!config.EnableGlossary || _glossary == null)
        {
            return true;
        }

        var matches = GlossaryMatcher.MatchTerms(
            new[] { sourceText },
            _glossary.GetEnabledTerms(config.TargetLanguage),
            config.GlossaryMaxTerms,
            config.GlossaryMaxCharacters);
        return matches.Count == 0 ||
            GlossaryOutputValidator.ValidateSingle(sourceText, translatedText, matches).IsValid;
    }

    public static string GetPromptPolicyVersion(RuntimeConfig config)
    {
        var hash = config.PromptTemplates.GetTemplateHash();
        return string.IsNullOrWhiteSpace(hash)
            ? PromptPolicyVersion
            : PromptPolicyVersion + "-" + hash;
    }

    private static ValidationResult ValidateCachedTranslation(CapturedText capturedText, string translatedText, RuntimeConfig config)
    {
        var outputValidation = TranslationOutputValidator.ValidateSingle(
            capturedText.SourceText,
            translatedText,
            requireSameRichTextTags: true);
        if (!outputValidation.IsValid)
        {
            return outputValidation;
        }

        var context = new PromptItemContext(
            0,
            capturedText.Context.SceneName,
            capturedText.Context.ComponentHierarchy,
            capturedText.Context.ComponentType);
        return TranslationQualityValidator.ValidateBatch(
            new[] { capturedText.SourceText },
            new[] { translatedText },
            new[] { context },
            config.TargetLanguage,
            config.GameTitle,
            config.TranslationQuality);
    }

    private void MarkCachedTranslationPending(TranslationCacheKey key, TranslationCacheContext context)
    {
        var now = DateTimeOffset.UtcNow;
        _cache.Update(new TranslationCacheEntry(
            key.SourceText,
            key.TargetLanguage,
            ProviderKind: string.Empty,
            ProviderBaseUrl: string.Empty,
            ProviderEndpoint: string.Empty,
            ProviderModel: string.Empty,
            key.PromptPolicyVersion,
            TranslatedText: null,
            context.SceneName,
            context.ComponentHierarchy,
            context.ComponentType,
            ReplacementFont: null,
            CreatedUtc: now,
            UpdatedUtc: now));
    }
}
