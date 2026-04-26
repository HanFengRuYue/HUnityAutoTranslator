namespace HUnityAutoTranslator.Core.Caching;

public sealed record TranslationCacheColumnFilter(
    string Column,
    IReadOnlyList<string?> Values);

public sealed record TranslationCacheFilterOptionsQuery(
    string Column,
    string? Search,
    IReadOnlyList<TranslationCacheColumnFilter>? ColumnFilters = null,
    string? OptionSearch = null,
    int Limit = 100);

public sealed record TranslationCacheFilterOption(
    string? Value,
    int Count);

public sealed record TranslationCacheFilterOptionPage(
    string Column,
    IReadOnlyList<TranslationCacheFilterOption> Items);

public static class TranslationCacheColumns
{
    public const string EmptyValueMarker = "__HUNITY_EMPTY__";

    private static readonly HashSet<string> FilterableColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "source_text",
        "translated_text",
        "target_language",
        "scene_name",
        "component_hierarchy",
        "component_type",
        "replacement_font",
        "provider_kind",
        "provider_model",
        "created_utc",
        "updated_utc"
    };

    public static bool IsFilterable(string? column)
    {
        return !string.IsNullOrWhiteSpace(column) && FilterableColumns.Contains(column);
    }

    public static string NormalizeColumn(string? column)
    {
        return IsFilterable(column) ? column!.Trim().ToLowerInvariant() : string.Empty;
    }

    public static string? NormalizeFilterValue(string? value)
    {
        return string.Equals(value, EmptyValueMarker, StringComparison.Ordinal)
            ? null
            : value;
    }

    public static string? NormalizeOptionValue(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static string? ValueFor(TranslationCacheEntry row, string column)
    {
        return NormalizeColumn(column) switch
        {
            "source_text" => row.SourceText,
            "translated_text" => row.TranslatedText,
            "target_language" => row.TargetLanguage,
            "scene_name" => row.SceneName,
            "component_hierarchy" => row.ComponentHierarchy,
            "component_type" => row.ComponentType,
            "replacement_font" => row.ReplacementFont,
            "provider_kind" => row.ProviderKind,
            "provider_model" => row.ProviderModel,
            "created_utc" => row.CreatedUtc.ToString("O"),
            "updated_utc" => row.UpdatedUtc.ToString("O"),
            _ => null
        };
    }

    public static IReadOnlyList<TranslationCacheColumnFilter> NormalizeFilters(
        IReadOnlyList<TranslationCacheColumnFilter>? filters,
        string? excludedColumn = null)
    {
        var excluded = NormalizeColumn(excludedColumn);
        if (filters == null || filters.Count == 0)
        {
            return Array.Empty<TranslationCacheColumnFilter>();
        }

        return filters
            .Select(filter => new TranslationCacheColumnFilter(
                NormalizeColumn(filter.Column),
                filter.Values.Select(NormalizeFilterValue).Distinct().ToArray()))
            .Where(filter => filter.Column.Length > 0
                && filter.Values.Count > 0
                && !string.Equals(filter.Column, excluded, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static bool MatchesFilters(TranslationCacheEntry row, IReadOnlyList<TranslationCacheColumnFilter>? filters)
    {
        foreach (var filter in NormalizeFilters(filters))
        {
            var rowValue = NormalizeOptionValue(ValueFor(row, filter.Column));
            var matched = filter.Values.Any(value =>
                string.IsNullOrEmpty(value)
                    ? string.IsNullOrEmpty(rowValue)
                    : string.Equals(rowValue, value, StringComparison.Ordinal));
            if (!matched)
            {
                return false;
            }
        }

        return true;
    }
}
