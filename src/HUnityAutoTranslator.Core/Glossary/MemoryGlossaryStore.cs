using System.Collections.Concurrent;

namespace HUnityAutoTranslator.Core.Glossary;

public sealed class MemoryGlossaryStore : IGlossaryStore
{
    private readonly ConcurrentDictionary<string, GlossaryTerm> _terms = new(StringComparer.Ordinal);

    public int Count => _terms.Count;

    public GlossaryTermPage Query(GlossaryQuery query)
    {
        var rows = _terms.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            rows = rows.Where(term =>
                Contains(term.SourceTerm, search) ||
                Contains(term.TargetTerm, search) ||
                Contains(term.TargetLanguage, search) ||
                Contains(term.Note, search) ||
                Contains(term.Source.ToString(), search));
        }

        rows = Sort(rows, query.SortColumn, query.SortDescending);
        var total = rows.Count();
        var items = rows
            .Skip(Math.Max(0, query.Offset))
            .Take(Math.Min(500, Math.Max(1, query.Limit)))
            .ToArray();
        return new GlossaryTermPage(total, items);
    }

    public IReadOnlyList<GlossaryTerm> GetEnabledTerms(string targetLanguage)
    {
        return _terms.Values
            .Where(term => term.Enabled && string.Equals(term.TargetLanguage, targetLanguage, StringComparison.Ordinal))
            .OrderByDescending(term => term.SourceTerm.Length)
            .ThenBy(term => term.SourceTerm, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public GlossaryTerm UpsertManual(GlossaryTerm term)
    {
        var now = DateTimeOffset.UtcNow;
        var normalized = term.NormalizeForStorage() with
        {
            Source = GlossaryTermSource.Manual,
            Enabled = term.Enabled,
            UpdatedUtc = now
        };
        var key = Key(normalized);
        return _terms.AddOrUpdate(
            key,
            _ => normalized with
            {
                CreatedUtc = normalized.CreatedUtc == default ? now : normalized.CreatedUtc
            },
            (_, existing) => normalized with
            {
                CreatedUtc = existing.CreatedUtc == default ? now : existing.CreatedUtc,
                UsageCount = Math.Max(existing.UsageCount, normalized.UsageCount)
            });
    }

    public GlossaryUpsertResult UpsertAutomatic(GlossaryTerm term)
    {
        var normalized = term.NormalizeForStorage() with
        {
            Source = GlossaryTermSource.Automatic,
            Enabled = true,
            UsageCount = Math.Max(1, term.UsageCount)
        };
        if (!GlossaryTerm.IsValid(normalized))
        {
            return GlossaryUpsertResult.SkippedInvalid;
        }

        var now = DateTimeOffset.UtcNow;
        var key = Key(normalized);
        while (true)
        {
            if (!_terms.TryGetValue(key, out var existing))
            {
                var created = normalized with { CreatedUtc = now, UpdatedUtc = now };
                if (_terms.TryAdd(key, created))
                {
                    return GlossaryUpsertResult.Created;
                }

                continue;
            }

            if (existing.Source == GlossaryTermSource.Manual)
            {
                return GlossaryUpsertResult.SkippedManualConflict;
            }

            if (!string.Equals(existing.TargetTerm, normalized.TargetTerm, StringComparison.Ordinal))
            {
                return GlossaryUpsertResult.SkippedAutomaticConflict;
            }

            var updated = existing with
            {
                SourceTerm = normalized.SourceTerm,
                TargetTerm = normalized.TargetTerm,
                Note = normalized.Note ?? existing.Note,
                Enabled = true,
                UsageCount = existing.UsageCount + 1,
                UpdatedUtc = now
            };
            if (_terms.TryUpdate(key, updated, existing))
            {
                return GlossaryUpsertResult.Updated;
            }
        }
    }

    public void Delete(GlossaryTerm term)
    {
        _terms.TryRemove(Key(term.NormalizeForStorage()), out _);
    }

    private static string Key(GlossaryTerm term)
    {
        return term.TargetLanguage + "\u001f" + term.NormalizedSourceTerm;
    }

    private static bool Contains(string? value, string search)
    {
        return value?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static IEnumerable<GlossaryTerm> Sort(IEnumerable<GlossaryTerm> rows, string column, bool descending)
    {
        Func<GlossaryTerm, object?> selector = column.ToLowerInvariant() switch
        {
            "source_term" => row => row.SourceTerm,
            "target_term" => row => row.TargetTerm,
            "target_language" => row => row.TargetLanguage,
            "source" => row => row.Source,
            "usage_count" => row => row.UsageCount,
            "created_utc" => row => row.CreatedUtc,
            _ => row => row.UpdatedUtc
        };
        return descending ? rows.OrderByDescending(selector) : rows.OrderBy(selector);
    }
}
