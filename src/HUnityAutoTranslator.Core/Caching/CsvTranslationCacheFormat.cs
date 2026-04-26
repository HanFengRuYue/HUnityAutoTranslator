using System.Text;

namespace HUnityAutoTranslator.Core.Caching;

internal static class CsvTranslationCacheFormat
{
    private static readonly string[] Header =
    {
        "source_text",
        "target_language",
        "provider_kind",
        "provider_base_url",
        "provider_endpoint",
        "provider_model",
        "prompt_policy_version",
        "translated_text",
        "scene_name",
        "component_hierarchy",
        "component_type",
        "replacement_font",
        "created_utc",
        "updated_utc"
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
                Escape(row.TargetLanguage),
                Escape(row.ProviderKind),
                Escape(row.ProviderBaseUrl),
                Escape(row.ProviderEndpoint),
                Escape(row.ProviderModel),
                Escape(row.PromptPolicyVersion),
                Escape(row.TranslatedText),
                Escape(row.SceneName),
                Escape(row.ComponentHierarchy),
                Escape(row.ComponentType),
                Escape(row.ReplacementFont),
                Escape(row.CreatedUtc.ToString("O")),
                Escape(row.UpdatedUtc.ToString("O"))
            }));
        }

        return builder.ToString();
    }

    public static IReadOnlyList<TranslationCacheEntry> Read(string content)
    {
        var lines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return Array.Empty<TranslationCacheEntry>();
        }

        var rows = new List<TranslationCacheEntry>();
        foreach (var line in lines.Skip(1))
        {
            var values = ParseLine(line);
            if (values.Count < Header.Length - 1)
            {
                throw new FormatException("CSV row has too few columns.");
            }

            var hasReplacementFont = values.Count >= Header.Length;
            var createdUtcIndex = hasReplacementFont ? 12 : 11;
            var updatedUtcIndex = hasReplacementFont ? 13 : 12;
            rows.Add(new TranslationCacheEntry(
                values[0],
                values[1],
                values[2],
                values[3],
                values[4],
                values[5],
                values[6],
                EmptyToNull(values[7]),
                EmptyToNull(values[8]),
                EmptyToNull(values[9]),
                EmptyToNull(values[10]),
                hasReplacementFont ? EmptyToNull(values[11]) : null,
                ParseDate(values[createdUtcIndex]),
                ParseDate(values[updatedUtcIndex])));
        }

        return rows;
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
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

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static DateTimeOffset ParseDate(string value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UtcNow;
    }
}
