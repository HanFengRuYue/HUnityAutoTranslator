namespace HUnityAutoTranslator.Core.Caching;

internal sealed record TranslationCacheLookupKey(
    string SourceText,
    string TargetLanguage,
    string SceneName,
    string ComponentHierarchy)
{
    public static TranslationCacheLookupKey Create(TranslationCacheKey key, TranslationCacheContext? context)
    {
        context ??= TranslationCacheContext.Empty;
        return new TranslationCacheLookupKey(
            key.SourceText,
            key.TargetLanguage,
            NormalizeContextPart(context.SceneName),
            NormalizeContextPart(context.ComponentHierarchy));
    }

    public static TranslationCacheLookupKey Create(TranslationCacheEntry entry)
    {
        return new TranslationCacheLookupKey(
            entry.SourceText,
            entry.TargetLanguage,
            NormalizeContextPart(entry.SceneName),
            NormalizeContextPart(entry.ComponentHierarchy));
    }

    public static string NormalizeContextPart(string? value)
    {
        return value ?? string.Empty;
    }
}
