using HUnityAutoTranslator.Core.Caching;

namespace HUnityAutoTranslator.Core.Control;

public sealed record TranslationHighlightRequest(
    string? SourceText,
    string? SceneName,
    string? ComponentHierarchy,
    string? ComponentType)
{
    public static TranslationHighlightRequest FromEntry(TranslationCacheEntry entry)
    {
        return new TranslationHighlightRequest(
            entry.SourceText,
            entry.SceneName,
            entry.ComponentHierarchy,
            entry.ComponentType);
    }
}
