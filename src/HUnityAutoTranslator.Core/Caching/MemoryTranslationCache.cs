using System.Collections.Concurrent;
using HUnityAutoTranslator.Core.Configuration;
using Newtonsoft.Json;

namespace HUnityAutoTranslator.Core.Caching;

public sealed class MemoryTranslationCache : ITranslationCache
{
    private readonly ConcurrentDictionary<TranslationCacheKey, TranslationCacheEntry> _items = new();

    public int Count => _items.Count;

    public bool TryGet(TranslationCacheKey key, out string translatedText)
    {
        if (_items.TryGetValue(key, out var entry) && entry.TranslatedText != null)
        {
            translatedText = entry.TranslatedText;
            return true;
        }

        translatedText = string.Empty;
        return false;
    }

    public void RecordCaptured(TranslationCacheKey key, TranslationCacheContext? context = null)
    {
        var now = DateTimeOffset.UtcNow;
        context ??= TranslationCacheContext.Empty;
        _items.AddOrUpdate(
            key,
            _ => ToEntry(key, translatedText: null, context, now, now),
            (_, existing) => existing.TranslatedText != null
                ? existing
                : existing with
                {
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
            key,
            _ => ToEntry(key, translatedText, context, now, now),
            (_, existing) => existing with
            {
                TranslatedText = translatedText,
                SceneName = context.SceneName,
                ComponentHierarchy = context.ComponentHierarchy,
                ComponentType = context.ComponentType,
                UpdatedUtc = now
            });
    }

    public IReadOnlyList<TranslationCacheEntry> GetPendingTranslations(
        string targetLanguage,
        ProviderProfile provider,
        string promptPolicyVersion,
        int limit)
    {
        var take = Math.Min(500, Math.Max(1, limit));
        return _items.Values
            .Where(row =>
                row.TranslatedText == null &&
                string.Equals(row.TargetLanguage, targetLanguage, StringComparison.Ordinal) &&
                string.Equals(row.ProviderKind, provider.Kind.ToString(), StringComparison.Ordinal) &&
                string.Equals(row.ProviderBaseUrl, provider.BaseUrl, StringComparison.Ordinal) &&
                string.Equals(row.ProviderEndpoint, provider.Endpoint, StringComparison.Ordinal) &&
                string.Equals(row.ProviderModel, provider.Model, StringComparison.Ordinal) &&
                string.Equals(row.PromptPolicyVersion, promptPolicyVersion, StringComparison.Ordinal))
            .OrderBy(row => row.CreatedUtc)
            .Take(take)
            .ToArray();
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
                Contains(row.ComponentType, search));
        }

        rows = Sort(rows, query.SortColumn, query.SortDescending);
        var total = rows.Count();
        var items = rows
            .Skip(Math.Max(0, query.Offset))
            .Take(Math.Min(500, Math.Max(1, query.Limit)))
            .ToArray();
        return new TranslationCachePage(total, items);
    }

    public void Update(TranslationCacheEntry entry)
    {
        var key = ToKey(entry);
        _items[key] = entry with { UpdatedUtc = entry.UpdatedUtc == default ? DateTimeOffset.UtcNow : entry.UpdatedUtc };
    }

    public void Delete(TranslationCacheEntry entry)
    {
        _items.TryRemove(ToKey(entry), out _);
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
            context.SceneName,
            context.ComponentHierarchy,
            context.ComponentType,
            createdUtc,
            updatedUtc);
    }

    private static TranslationCacheKey ToKey(TranslationCacheEntry entry)
    {
        var kind = Enum.TryParse<ProviderKind>(entry.ProviderKind, out var parsed) ? parsed : ProviderKind.OpenAICompatible;
        return new TranslationCacheKey(
            entry.SourceText,
            entry.TargetLanguage,
            kind,
            entry.ProviderBaseUrl,
            entry.ProviderEndpoint,
            entry.ProviderModel,
            entry.PromptPolicyVersion);
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
            "created_utc" => row => row.CreatedUtc,
            _ => row => row.UpdatedUtc
        };
        return descending ? rows.OrderByDescending(selector) : rows.OrderBy(selector);
    }
}
