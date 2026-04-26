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
        var config = _configProvider();
        if (!config.Enabled ||
            capturedText.SourceText.Length > config.MaxSourceTextLength ||
            (config.IgnoreInvisibleText && !capturedText.IsVisible) ||
            !TextFilter.ShouldTranslate(capturedText.SourceText))
        {
            return PipelineDecision.Ignored();
        }

        var key = TranslationCacheKey.Create(capturedText.SourceText, config.TargetLanguage, config.Provider, PromptPolicyVersion);
        _metrics?.RecordCaptured(key);
        _cache.RecordCaptured(key, capturedText.Context);
        if (config.EnableCacheLookup && _cache.TryGet(key, out var translatedText))
        {
            return PipelineDecision.UseCachedTranslation(translatedText);
        }

        var enqueued = _queue.Enqueue(TranslationJob.Create(
            capturedText.TargetId,
            capturedText.SourceText,
            capturedText.IsVisible ? TranslationPriority.VisibleUi : TranslationPriority.Normal,
            capturedText.Context));
        if (enqueued)
        {
            _metrics?.RecordQueued();
        }

        return PipelineDecision.Queued();
    }
}
