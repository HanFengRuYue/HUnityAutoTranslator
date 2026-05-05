using System.Text;

namespace HUnityAutoTranslator.Core.Caching;

internal static class CsvTranslationCacheFormat
{
    private static readonly string[] Header =
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
        "updated_utc",
        "provider_base_url",
        "provider_endpoint",
        "prompt_policy_version"
    };

    public static string Write(IEnumerable<TranslationCacheEntry> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", Header));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                Escape(row.SourceText),
                Escape(row.TranslatedText),
                Escape(row.TargetLanguage),
                Escape(row.SceneName),
                Escape(row.ComponentHierarchy),
                Escape(row.ComponentType),
                Escape(row.ReplacementFont),
                Escape(row.ProviderKind),
                Escape(row.ProviderModel),
                Escape(row.CreatedUtc.ToString("O")),
                Escape(row.UpdatedUtc.ToString("O")),
                Escape(row.ProviderBaseUrl),
                Escape(row.ProviderEndpoint),
                Escape(row.PromptPolicyVersion)
            }));
        }

        return builder.ToString();
    }

    public static IReadOnlyList<TranslationCacheEntry> Read(string content)
    {
        var lines = content
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return Array.Empty<TranslationCacheEntry>();
        }

        var rows = new List<TranslationCacheEntry>();
        var indexes = BuildHeaderIndexes(ParseLine(lines[0]));
        foreach (var line in lines.Skip(1))
        {
            var values = ParseLine(line);
            rows.Add(new TranslationCacheEntry(
                Required(values, indexes, "source_text"),
                Required(values, indexes, "target_language"),
                Optional(values, indexes, "provider_kind") ?? string.Empty,
                Optional(values, indexes, "provider_base_url") ?? string.Empty,
                Optional(values, indexes, "provider_endpoint") ?? string.Empty,
                Optional(values, indexes, "provider_model") ?? string.Empty,
                Required(values, indexes, "prompt_policy_version"),
                Optional(values, indexes, "translated_text"),
                Optional(values, indexes, "scene_name"),
                Optional(values, indexes, "component_hierarchy"),
                Optional(values, indexes, "component_type"),
                Optional(values, indexes, "replacement_font"),
                ParseDate(ValueOrEmpty(values, indexes, "created_utc")),
                ParseDate(ValueOrEmpty(values, indexes, "updated_utc"))));
        }

        return rows;
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static List<string> ParseLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    builder.Append(ch);
                }
            }
            else if (ch == ',')
            {
                values.Add(builder.ToString());
                builder.Clear();
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else
            {
                builder.Append(ch);
            }
        }

        values.Add(builder.ToString());
        return values;
    }

    private static Dictionary<string, int> BuildHeaderIndexes(IReadOnlyList<string> header)
    {
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim();
            if (name.Length > 0 && !indexes.ContainsKey(name))
            {
                indexes.Add(name, i);
            }
        }

        return indexes;
    }

    private static string Required(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> indexes, string name)
    {
        var value = ValueOrEmpty(values, indexes, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException($"Missing required CSV column value: {name}");
        }

        return value;
    }

    private static string? Optional(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> indexes, string name)
    {
        return EmptyToNull(ValueOrEmpty(values, indexes, name));
    }

    private static string ValueOrEmpty(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> indexes, string name)
    {
        if (!indexes.TryGetValue(name, out var index))
        {
            return string.Empty;
        }

        return index < values.Count ? values[index] : string.Empty;
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static DateTimeOffset ParseDate(string value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UtcNow;
    }
}
