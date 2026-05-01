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
        bool publishResult = true)
    {
        TargetId = targetId;
        SourceText = sourceText;
        IsVisible = isVisible;
        Context = context;
        PublishResult = publishResult;
    }

    public string TargetId { get; }

    public string SourceText { get; }

    public bool IsVisible { get; }

    public TranslationCacheContext Context { get; }

    public bool PublishResult { get; }
}
