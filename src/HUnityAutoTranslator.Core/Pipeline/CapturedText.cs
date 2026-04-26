using HUnityAutoTranslator.Core.Caching;

namespace HUnityAutoTranslator.Core.Pipeline;

public sealed class CapturedText
{
    public CapturedText(string targetId, string sourceText, bool isVisible)
        : this(targetId, sourceText, isVisible, TranslationCacheContext.Empty)
    {
    }

    public CapturedText(string targetId, string sourceText, bool isVisible, TranslationCacheContext context)
    {
        TargetId = targetId;
        SourceText = sourceText;
        IsVisible = isVisible;
        Context = context;
    }

    public string TargetId { get; }

    public string SourceText { get; }

    public bool IsVisible { get; }

    public TranslationCacheContext Context { get; }
}
