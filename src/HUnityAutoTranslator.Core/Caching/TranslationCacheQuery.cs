namespace HUnityAutoTranslator.Core.Caching;

public sealed record TranslationCacheQuery(
    string? Search,
    string SortColumn,
    bool SortDescending,
    int Offset,
    int Limit,
    IReadOnlyList<TranslationCacheColumnFilter>? ColumnFilters = null);
