namespace HUnityAutoTranslator.Core.Glossary;

public sealed record GlossaryQuery(
    string? Search,
    string SortColumn,
    bool SortDescending,
    int Offset,
    int Limit,
    IReadOnlyList<GlossaryColumnFilter>? ColumnFilters = null);

public sealed record GlossaryTermPage(int TotalCount, IReadOnlyList<GlossaryTerm> Items);

public sealed record GlossaryColumnFilter(
    string Column,
    IReadOnlyList<string?> Values);

public sealed record GlossaryFilterOptionsQuery(
    string Column,
    string? Search,
    IReadOnlyList<GlossaryColumnFilter>? ColumnFilters = null,
    string? OptionSearch = null,
    int Limit = 100);

public sealed record GlossaryFilterOption(
    string? Value,
    int Count);

public sealed record GlossaryFilterOptionPage(
    string Column,
    IReadOnlyList<GlossaryFilterOption> Items);

public static class GlossaryColumns
{
    public const string EmptyValueMarker = "__HUNITY_EMPTY__";

    private static readonly HashSet<string> FilterableColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "enabled",
        "source_term",
        "target_term",
        "target_language",
        "note",
        "source",
        "usage_count",
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

    public static string? ValueFor(GlossaryTerm term, string column)
    {
        return NormalizeColumn(column) switch
        {
            "enabled" => term.Enabled ? "true" : "false",
            "source_term" => term.SourceTerm,
            "target_term" => term.TargetTerm,
            "target_language" => term.TargetLanguage,
            "note" => term.Note,
            "source" => term.Source.ToString(),
            "usage_count" => term.UsageCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "created_utc" => term.CreatedUtc.ToString("O"),
            "updated_utc" => term.UpdatedUtc.ToString("O"),
            _ => null
        };
    }

    public static IReadOnlyList<GlossaryColumnFilter> NormalizeFilters(
        IReadOnlyList<GlossaryColumnFilter>? filters,
        string? excludedColumn = null)
    {
        var excluded = NormalizeColumn(excludedColumn);
        if (filters == null || filters.Count == 0)
        {
            return Array.Empty<GlossaryColumnFilter>();
        }

        return filters
            .Select(filter => new GlossaryColumnFilter(
                NormalizeColumn(filter.Column),
                filter.Values.Select(NormalizeFilterValue).Distinct().ToArray()))
            .Where(filter => filter.Column.Length > 0
                && filter.Values.Count > 0
                && !string.Equals(filter.Column, excluded, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static bool MatchesFilters(GlossaryTerm term, IReadOnlyList<GlossaryColumnFilter>? filters)
    {
        foreach (var filter in NormalizeFilters(filters))
        {
            var rowValue = NormalizeOptionValue(ValueFor(term, filter.Column));
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
