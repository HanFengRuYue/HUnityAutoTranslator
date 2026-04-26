using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Queueing;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Pipeline;

public sealed class TextPipeline
{
    public const string PromptPolicyVersion = "prompt-v1";

    private readonly ITranslationCache _cache;
    private readonly TranslationJobQueue _queue;
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly ControlPanelMetrics? _metrics;

    public TextPipeline(ITranslationCache cache, TranslationJobQueue queue, RuntimeConfig config, ControlPanelMetrics? metrics = null)
        : this(cache, queue, () => config, metrics)
    {
    }

    public TextPipeline(ITranslationCache cache, TranslationJobQueue queue, Func<RuntimeConfig> configProvider, ControlPanelMetrics? metrics = null)
    {
        _cache = cache;
        _queue = queue;
        _configProvider = configProvider;
        _metrics = metrics;
    }

    public PipelineDecision Process(CapturedText capturedText)
    {
        _metrics?.RecordCaptured();
        var config = _configProvider();
        if (!config.Enabled ||
            capturedText.SourceText.Length > config.MaxSourceTextLength ||
            (config.IgnoreInvisibleText && !capturedText.IsVisible) ||
            !TextFilter.ShouldTranslate(capturedText.SourceText))
        {
            return PipelineDecision.Ignored();
        }

        var key = TranslationCacheKey.Create(capturedText.SourceText, config.TargetLanguage, config.Provider, PromptPolicyVersion);
        _cache.RecordCaptured(key, capturedText.Context);
        if (config.EnableCacheLookup && _cache.TryGet(key, out var translatedText))
        {
            _metrics?.RecordTranslationCompleted(new RecentTranslationPreview(
                capturedText.SourceText,
                translatedText,
                config.TargetLanguage,
                config.Provider.Kind.ToString(),
                config.Provider.Model,
                capturedText.Context.ComponentHierarchy ?? capturedText.Context.SceneName ?? capturedText.Context.ComponentType,
                DateTimeOffset.UtcNow));
            return PipelineDecision.UseCachedTranslation(translatedText);
        }

        _queue.Enqueue(TranslationJob.Create(
            capturedText.TargetId,
            capturedText.SourceText,
            capturedText.IsVisible ? TranslationPriority.VisibleUi : TranslationPriority.Normal,
            capturedText.Context));
        _metrics?.RecordQueued();

        return PipelineDecision.Queued();
    }
}
