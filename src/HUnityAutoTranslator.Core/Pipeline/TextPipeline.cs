using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Queueing;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Pipeline;

public sealed class TextPipeline
{
    public const string PromptPolicyVersion = "prompt-v1";

    private readonly ITranslationCache _cache;
    private readonly TranslationJobQueue _queue;
    private readonly Func<RuntimeConfig> _configProvider;

    public TextPipeline(ITranslationCache cache, TranslationJobQueue queue, RuntimeConfig config)
        : this(cache, queue, () => config)
    {
    }

    public TextPipeline(ITranslationCache cache, TranslationJobQueue queue, Func<RuntimeConfig> configProvider)
    {
        _cache = cache;
        _queue = queue;
        _configProvider = configProvider;
    }

    public PipelineDecision Process(CapturedText capturedText)
    {
        var config = _configProvider();
        if (!config.Enabled || !TextFilter.ShouldTranslate(capturedText.SourceText))
        {
            return PipelineDecision.Ignored();
        }

        var normalized = TextNormalizer.NormalizeForCache(capturedText.SourceText);
        var key = TranslationCacheKey.Create(normalized, config.TargetLanguage, config.Provider, PromptPolicyVersion);
        if (_cache.TryGet(key, out var translatedText))
        {
            return PipelineDecision.UseCachedTranslation(translatedText);
        }

        _queue.Enqueue(TranslationJob.Create(
            capturedText.TargetId,
            normalized,
            capturedText.IsVisible ? TranslationPriority.VisibleUi : TranslationPriority.Normal));

        return PipelineDecision.Queued();
    }
}
