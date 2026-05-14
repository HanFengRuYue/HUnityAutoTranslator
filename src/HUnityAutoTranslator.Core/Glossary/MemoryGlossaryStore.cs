using System.Collections.Concurrent;

namespace HUnityAutoTranslator.Core.Glossary;

public sealed class MemoryGlossaryStore : IGlossaryStore
{
    private readonly ConcurrentDictionary<string, GlossaryTerm> _terms = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _extractionWatermarks = new(StringComparer.Ordinal);

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

        rows = rows.Where(term => GlossaryColumns.MatchesFilters(term, query.ColumnFilters));
        rows = Sort(rows, query.SortColumn, query.SortDescending);
        var total = rows.Count();
        var items = rows
            .Skip(Math.Max(0, query.Offset))
            .Take(Math.Min(500, Math.Max(1, query.Limit)))
            .ToArray();
        return new GlossaryTermPage(total, items);
    }

    public GlossaryFilterOptionPage GetFilterOptions(GlossaryFilterOptionsQuery query)
    {
        var column = GlossaryColumns.NormalizeColumn(query.Column);
        if (column.Length == 0)
        {
            return new GlossaryFilterOptionPage(string.Empty, Array.Empty<GlossaryFilterOption>());
        }

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

        rows = rows.Where(term => GlossaryColumns.MatchesFilters(
            term,
            GlossaryColumns.NormalizeFilters(query.ColumnFilters, column)));

        if (!string.IsNullOrWhiteSpace(query.OptionSearch))
        {
            var optionSearch = query.OptionSearch.Trim();
            rows = rows.Where(term => Contains(GlossaryColumns.ValueFor(term, column), optionSearch));
        }

        var items = rows
            .GroupBy(term => GlossaryColumns.NormalizeOptionValue(GlossaryColumns.ValueFor(term, column)))
            .OrderBy(group => group.Key is not null)
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Min(500, Math.Max(1, query.Limit)))
            .Select(group => new GlossaryFilterOption(group.Key, group.Count()))
            .ToArray();

        return new GlossaryFilterOptionPage(column, items);
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
                // 同源多译铁证：该词上下文相关，两个译法都不可信。
                // 禁用已入库的旧自动术语（运行时自愈），新候选也不写入。
                var disabled = existing with
                {
                    Enabled = false,
                    Note = AppendNote(existing.Note, "[自动禁用：译法不一致]"),
                    UpdatedUtc = now
                };
                _terms.TryUpdate(key, disabled, existing);
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

    public string? GetExtractionWatermark(string targetLanguage)
    {
        return _extractionWatermarks.TryGetValue(targetLanguage ?? string.Empty, out var value) ? value : null;
    }

    public void SetExtractionWatermark(string targetLanguage, string watermark)
    {
        _extractionWatermarks[targetLanguage ?? string.Empty] = watermark ?? string.Empty;
    }

    public IReadOnlyList<GlossaryTerm> FindSuspiciousAutomaticTerms()
    {
        return _terms.Values
            .Where(term =>
                term.Enabled &&
                term.Source == GlossaryTermSource.Automatic &&
                SuspiciousGlossaryTermDetector.IsSuspicious(term.SourceTerm))
            .OrderByDescending(term => term.UpdatedUtc)
            .ToArray();
    }

    public int DisableTerms(IReadOnlyList<GlossaryTerm> terms)
    {
        if (terms == null || terms.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var affected = 0;
        foreach (var term in terms)
        {
            var key = Key(term.NormalizeForStorage());
            while (_terms.TryGetValue(key, out var existing) && existing.Enabled)
            {
                if (_terms.TryUpdate(key, existing with { Enabled = false, UpdatedUtc = now }, existing))
                {
                    affected++;
                    break;
                }
            }
        }

        return affected;
    }

    public int RenormalizeAutomaticTermNotes()
    {
        var changed = 0;
        foreach (var entry in _terms.ToArray())
        {
            var existing = entry.Value;
            if (existing.Source != GlossaryTermSource.Automatic)
            {
                continue;
            }

            var newNote = GlossaryTermCategory.Normalize(existing.Note);
            if (string.Equals(existing.Note, newNote, StringComparison.Ordinal))
            {
                continue;
            }

            if (_terms.TryUpdate(entry.Key, existing with { Note = newNote, UpdatedUtc = DateTimeOffset.UtcNow }, existing))
            {
                changed++;
            }
        }

        return changed;
    }

    private static string AppendNote(string? note, string suffix)
    {
        var trimmed = (note ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return suffix;
        }

        return trimmed.EndsWith(suffix, StringComparison.Ordinal) ? trimmed : trimmed + " " + suffix;
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
            "note" => row => row.Note,
            "enabled" => row => row.Enabled,
            "source" => row => row.Source,
            "usage_count" => row => row.UsageCount,
            "created_utc" => row => row.CreatedUtc,
            _ => row => row.UpdatedUtc
        };
        return descending ? rows.OrderByDescending(selector) : rows.OrderBy(selector);
    }
}
