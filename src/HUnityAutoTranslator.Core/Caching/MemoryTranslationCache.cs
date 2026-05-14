using System.Collections.Concurrent;
using HUnityAutoTranslator.Core.Configuration;
using Newtonsoft.Json;

namespace HUnityAutoTranslator.Core.Caching;

public sealed class MemoryTranslationCache : ITranslationCache
{
    private readonly ConcurrentDictionary<TranslationCacheLookupKey, TranslationCacheEntry> _items = new();

    public int Count => _items.Values.Count(item => item.TranslatedText != null);

    public bool TryGet(TranslationCacheKey key, TranslationCacheContext? context, out string translatedText)
    {
        if (_items.TryGetValue(TranslationCacheLookupKey.Create(key, context), out var entry) &&
            entry.TranslatedText != null &&
            string.Equals(entry.PromptPolicyVersion, key.PromptPolicyVersion, StringComparison.Ordinal))
        {
            translatedText = entry.TranslatedText;
            return true;
        }

        translatedText = string.Empty;
        return false;
    }

    public bool TryGetReplacementFont(TranslationCacheKey key, TranslationCacheContext context, out string replacementFont)
    {
        if (_items.TryGetValue(TranslationCacheLookupKey.Create(key, context), out var entry) &&
            entry.TranslatedText != null &&
            !string.IsNullOrWhiteSpace(entry.ReplacementFont) &&
            ContextMatches(entry, context))
        {
            replacementFont = entry.ReplacementFont!;
            return true;
        }

        replacementFont = string.Empty;
        return false;
    }

    public IReadOnlyList<TranslationCacheEntry> GetCompletedTranslationsBySource(
        TranslationCacheKey key,
        int limit)
    {
        var take = Math.Min(500, Math.Max(1, limit));
        return _items.Values
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.TranslatedText) &&
                string.Equals(row.SourceText, key.SourceText, StringComparison.Ordinal) &&
                string.Equals(row.TargetLanguage, key.TargetLanguage, StringComparison.Ordinal) &&
                string.Equals(row.PromptPolicyVersion, key.PromptPolicyVersion, StringComparison.Ordinal))
            .OrderByDescending(row => row.UpdatedUtc)
            .Take(take)
            .ToArray();
    }

    public IReadOnlyList<TranslationCacheEntry> GetCompletedContainingSource(
        string sourceSubstring,
        string targetLanguage,
        int limit)
    {
        var needle = sourceSubstring?.Trim() ?? string.Empty;
        if (needle.Length == 0)
        {
            return Array.Empty<TranslationCacheEntry>();
        }

        var take = Math.Min(1000, Math.Max(1, limit));
        return _items.Values
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.TranslatedText) &&
                string.Equals(row.TargetLanguage, targetLanguage, StringComparison.Ordinal) &&
                row.SourceText.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderByDescending(row => row.UpdatedUtc)
            .Take(take)
            .ToArray();
    }

    public IReadOnlyList<TranslationCacheEntry> GetCompletedSince(
        string targetLanguage,
        string? afterUpdatedUtc,
        int limit)
    {
        var take = Math.Min(2000, Math.Max(1, limit));
        var after = DateTimeOffset.TryParse(afterUpdatedUtc, out var parsed) ? parsed : DateTimeOffset.MinValue;
        return _items.Values
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.TranslatedText) &&
                string.Equals(row.TargetLanguage, targetLanguage, StringComparison.Ordinal) &&
                row.UpdatedUtc > after)
            .OrderBy(row => row.UpdatedUtc)
            .Take(take)
            .ToArray();
    }

    public IReadOnlyList<TranslationCacheEntry> GetCompletedInHierarchy(
        string targetLanguage,
        string sceneName,
        string componentHierarchyPrefix,
        int limit)
    {
        var prefix = componentHierarchyPrefix?.Trim() ?? string.Empty;
        if (prefix.Length == 0)
        {
            return Array.Empty<TranslationCacheEntry>();
        }

        var subtreePrefix = prefix + "/";
        var take = Math.Min(2000, Math.Max(1, limit));
        return _items.Values
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.TranslatedText) &&
                string.Equals(row.TargetLanguage, targetLanguage, StringComparison.Ordinal) &&
                string.Equals(row.SceneName, sceneName, StringComparison.Ordinal) &&
                (string.Equals(row.ComponentHierarchy, prefix, StringComparison.Ordinal) ||
                 (row.ComponentHierarchy ?? string.Empty).StartsWith(subtreePrefix, StringComparison.Ordinal)))
            .OrderByDescending(row => row.UpdatedUtc)
            .Take(take)
            .ToArray();
    }

    public void RecordCaptured(TranslationCacheKey key, TranslationCacheContext? context = null)
    {
        var now = DateTimeOffset.UtcNow;
        context ??= TranslationCacheContext.Empty;
        _items.AddOrUpdate(
            TranslationCacheLookupKey.Create(key, context),
            _ => ToPendingEntry(key, context, now, now),
            (_, existing) => existing.TranslatedText != null
                ? existing
                : existing with
                {
                    ProviderKind = string.Empty,
                    ProviderBaseUrl = string.Empty,
                    ProviderEndpoint = string.Empty,
                    ProviderModel = string.Empty,
                    PromptPolicyVersion = key.PromptPolicyVersion,
                    SceneName = context.SceneName,
                    ComponentHierarchy = context.ComponentHierarchy,
                    ComponentType = context.ComponentType,
                    UpdatedUtc = now
                });
    }

    public void Set(TranslationCacheKey key, string translatedText, TranslationCacheContext? context = null)
    {
        var now = DateTimeOffset.UtcNow;
        context ??= TranslationCacheContext.Empty;
        _items.AddOrUpdate(
            TranslationCacheLookupKey.Create(key, context),
            _ => ToEntry(key, translatedText, context, now, now),
            (_, existing) => existing with
            {
                ProviderKind = key.ProviderKind.ToString(),
                ProviderBaseUrl = key.ProviderBaseUrl,
                ProviderEndpoint = key.ProviderEndpoint,
                ProviderModel = key.ProviderModel,
                PromptPolicyVersion = key.PromptPolicyVersion,
                TranslatedText = translatedText,
                SceneName = context.SceneName,
                ComponentHierarchy = context.ComponentHierarchy,
                ComponentType = context.ComponentType,
                UpdatedUtc = now
            });
    }

    public IReadOnlyList<TranslationCacheEntry> GetPendingTranslations(
        string targetLanguage,
        string promptPolicyVersion,
        int limit)
    {
        var take = Math.Min(500, Math.Max(1, limit));
        return _items.Values
            .Where(row =>
                row.TranslatedText == null &&
                string.Equals(row.TargetLanguage, targetLanguage, StringComparison.Ordinal) &&
                string.Equals(row.PromptPolicyVersion, promptPolicyVersion, StringComparison.Ordinal))
            .OrderBy(row => row.CreatedUtc)
            .Take(take)
            .ToArray();
    }

    public IReadOnlyList<TranslationContextExample> GetTranslationContextExamples(
        string currentSourceText,
        string targetLanguage,
        TranslationCacheContext? context,
        int maxExamples,
        int maxCharacters)
    {
        return TranslationContextSelector.Select(
            _items.Values,
            currentSourceText,
            targetLanguage,
            context,
            Math.Min(20, Math.Max(0, maxExamples)),
            Math.Min(8000, Math.Max(0, maxCharacters)));
    }

    public TranslationCachePage Query(TranslationCacheQuery query)
    {
        var rows = _items.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            rows = rows.Where(row =>
                Contains(row.SourceText, search) ||
                Contains(row.TranslatedText, search) ||
                Contains(row.SceneName, search) ||
                Contains(row.ComponentHierarchy, search) ||
                Contains(row.ComponentType, search) ||
                Contains(row.ReplacementFont, search));
        }

        rows = rows.Where(row => TranslationCacheColumns.MatchesFilters(row, query.ColumnFilters));
        rows = Sort(rows, query.SortColumn, query.SortDescending);
        var total = rows.Count();
        var items = rows
            .Skip(Math.Max(0, query.Offset))
            .Take(Math.Min(500, Math.Max(1, query.Limit)))
            .ToArray();
        return new TranslationCachePage(total, items);
    }

    public TranslationCacheFilterOptionPage GetFilterOptions(TranslationCacheFilterOptionsQuery query)
    {
        var column = TranslationCacheColumns.NormalizeColumn(query.Column);
        if (column.Length == 0)
        {
            return new TranslationCacheFilterOptionPage(string.Empty, Array.Empty<TranslationCacheFilterOption>());
        }

        var rows = _items.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            rows = rows.Where(row =>
                Contains(row.SourceText, search) ||
                Contains(row.TranslatedText, search) ||
                Contains(row.SceneName, search) ||
                Contains(row.ComponentHierarchy, search) ||
                Contains(row.ComponentType, search) ||
                Contains(row.ReplacementFont, search));
        }

        rows = rows.Where(row => TranslationCacheColumns.MatchesFilters(
            row,
            TranslationCacheColumns.NormalizeFilters(query.ColumnFilters, column)));

        if (!string.IsNullOrWhiteSpace(query.OptionSearch))
        {
            var optionSearch = query.OptionSearch.Trim();
            rows = rows.Where(row => Contains(TranslationCacheColumns.ValueFor(row, column), optionSearch));
        }

        var limit = Math.Min(500, Math.Max(1, query.Limit));
        var items = rows
            .GroupBy(row => TranslationCacheColumns.NormalizeOptionValue(TranslationCacheColumns.ValueFor(row, column)))
            .Select(group => new TranslationCacheFilterOption(group.Key, group.Count()))
            .OrderBy(item => item.Value is null ? 0 : 1)
            .ThenBy(item => item.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();

        return new TranslationCacheFilterOptionPage(column, items);
    }

    public void Update(TranslationCacheEntry entry)
    {
        var key = TranslationCacheLookupKey.Create(entry);
        _items[key] = NormalizeEntry(entry) with { UpdatedUtc = entry.UpdatedUtc == default ? DateTimeOffset.UtcNow : entry.UpdatedUtc };
    }

    public void Delete(TranslationCacheEntry entry)
    {
        _items.TryRemove(TranslationCacheLookupKey.Create(entry), out _);
    }

    public string Export(string format)
    {
        var rows = Query(new TranslationCacheQuery(null, "updated_utc", true, 0, int.MaxValue)).Items;
        return string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            ? CsvTranslationCacheFormat.Write(rows)
            : JsonConvert.SerializeObject(rows, Formatting.Indented);
    }

    public TranslationCacheImportResult Import(string content, string format)
    {
        try
        {
            var rows = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
                ? CsvTranslationCacheFormat.Read(content)
                : JsonConvert.DeserializeObject<IReadOnlyList<TranslationCacheEntry>>(content) ?? Array.Empty<TranslationCacheEntry>();
            var imported = 0;
            foreach (var row in rows)
            {
                Update(NormalizeImported(row));
                imported++;
            }

            return new TranslationCacheImportResult(imported, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            return new TranslationCacheImportResult(0, new[] { ex.Message });
        }
    }

    private static TranslationCacheEntry ToEntry(
        TranslationCacheKey key,
        string? translatedText,
        TranslationCacheContext context,
        DateTimeOffset createdUtc,
        DateTimeOffset updatedUtc)
    {
        return new TranslationCacheEntry(
            key.SourceText,
            key.TargetLanguage,
            key.ProviderKind.ToString(),
            key.ProviderBaseUrl,
            key.ProviderEndpoint,
            key.ProviderModel,
            key.PromptPolicyVersion,
            translatedText,
            TranslationCacheLookupKey.NormalizeContextPart(context.SceneName),
            TranslationCacheLookupKey.NormalizeContextPart(context.ComponentHierarchy),
            context.ComponentType,
            ReplacementFont: null,
            createdUtc,
            updatedUtc);
    }

    private static TranslationCacheEntry ToPendingEntry(
        TranslationCacheKey key,
        TranslationCacheContext context,
        DateTimeOffset createdUtc,
        DateTimeOffset updatedUtc)
    {
        return new TranslationCacheEntry(
            key.SourceText,
            key.TargetLanguage,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            key.PromptPolicyVersion,
            TranslatedText: null,
            TranslationCacheLookupKey.NormalizeContextPart(context.SceneName),
            TranslationCacheLookupKey.NormalizeContextPart(context.ComponentHierarchy),
            context.ComponentType,
            ReplacementFont: null,
            createdUtc,
            updatedUtc);
    }

    private static TranslationCacheEntry NormalizeEntry(TranslationCacheEntry entry)
    {
        return entry with
        {
            TranslatedText = string.IsNullOrWhiteSpace(entry.TranslatedText) ? null : entry.TranslatedText,
            SceneName = TranslationCacheLookupKey.NormalizeContextPart(entry.SceneName),
            ComponentHierarchy = TranslationCacheLookupKey.NormalizeContextPart(entry.ComponentHierarchy)
        };
    }

    private static TranslationCacheEntry NormalizeImported(TranslationCacheEntry entry)
    {
        var now = DateTimeOffset.UtcNow;
        return entry with
        {
            CreatedUtc = entry.CreatedUtc == default ? now : entry.CreatedUtc,
            UpdatedUtc = entry.UpdatedUtc == default ? now : entry.UpdatedUtc
        };
    }

    private static bool ContextMatches(TranslationCacheEntry entry, TranslationCacheContext context)
    {
        return string.Equals(entry.SceneName, context.SceneName, StringComparison.Ordinal)
            && string.Equals(entry.ComponentHierarchy, context.ComponentHierarchy, StringComparison.Ordinal)
            && string.Equals(entry.ComponentType, context.ComponentType, StringComparison.Ordinal);
    }

    private static bool Contains(string? value, string search)
    {
        return value?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static IEnumerable<TranslationCacheEntry> Sort(IEnumerable<TranslationCacheEntry> rows, string column, bool descending)
    {
        Func<TranslationCacheEntry, object?> selector = column.ToLowerInvariant() switch
        {
            "source_text" => row => row.SourceText,
            "translated_text" => row => row.TranslatedText,
            "target_language" => row => row.TargetLanguage,
            "provider_kind" => row => row.ProviderKind,
            "provider_model" => row => row.ProviderModel,
            "scene_name" => row => row.SceneName,
            "component_hierarchy" => row => row.ComponentHierarchy,
            "component_type" => row => row.ComponentType,
            "replacement_font" => row => row.ReplacementFont,
            "created_utc" => row => row.CreatedUtc,
            _ => row => row.UpdatedUtc
        };
        return descending ? rows.OrderByDescending(selector) : rows.OrderBy(selector);
    }
}
