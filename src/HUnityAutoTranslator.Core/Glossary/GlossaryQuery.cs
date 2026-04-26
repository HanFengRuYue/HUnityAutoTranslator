namespace HUnityAutoTranslator.Core.Glossary;

public sealed record GlossaryQuery(
    string? Search,
    string SortColumn,
    bool SortDescending,
    int Offset,
    int Limit);

public sealed record GlossaryTermPage(int TotalCount, IReadOnlyList<GlossaryTerm> Items);
