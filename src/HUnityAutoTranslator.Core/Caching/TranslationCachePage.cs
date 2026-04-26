namespace HUnityAutoTranslator.Core.Caching;

public sealed record TranslationCachePage(
    int TotalCount,
    IReadOnlyList<TranslationCacheEntry> Items);

public sealed record TranslationCacheImportResult(
    int ImportedCount,
    IReadOnlyList<string> Errors);
