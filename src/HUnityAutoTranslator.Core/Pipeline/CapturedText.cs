using HUnityAutoTranslator.Core.Caching;

namespace HUnityAutoTranslator.Core.Pipeline;

public sealed class CapturedText
{
    public CapturedText(string targetId, string sourceText, bool isVisible)
        : this(targetId, sourceText, isVisible, TranslationCacheContext.Empty)
    {
    }

    public CapturedText(
        string targetId,
        string sourceText,
        bool isVisible,
        TranslationCacheContext context,
        bool publishResult = true,
        bool allowInvisiblePrefetch = false)
    {
        TargetId = targetId;
        SourceText = sourceText;
        IsVisible = isVisible;
        Context = context;
        PublishResult = publishResult;
        AllowInvisiblePrefetch = allowInvisiblePrefetch;
    }

    public string TargetId { get; }

    public string SourceText { get; }

    public bool IsVisible { get; }

    public TranslationCacheContext Context { get; }

    public bool PublishResult { get; }

    // Set by the inactive-UI prefetch scanner so a hidden component bypasses the
    // IgnoreInvisibleText gate and is queued at the lowest priority.
    public bool AllowInvisiblePrefetch { get; }
}
