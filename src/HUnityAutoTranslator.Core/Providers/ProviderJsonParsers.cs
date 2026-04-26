using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Providers;

public static class ProviderJsonParsers
{
    private static readonly Regex IndexedLinePattern = new(@"^\s*(\d+)\s*[:：.]\s+(.+?)\s*$", RegexOptions.Compiled);

    public static string ParseOpenAiResponsesText(string json)
    {
        var root = JToken.Parse(json);
        var texts = new List<string>();
        CollectOpenAiOutputText(root, texts);
        return string.Join(string.Empty, texts);
    }

    public static string ParseChatCompletionsText(string json)
    {
        var root = JObject.Parse(json);
        return root["choices"]?.FirstOrDefault()?["message"]?["content"]?.Value<string>() ?? string.Empty;
    }

    public static IReadOnlyList<string> ParseAssistantTextAsList(string assistantText)
    {
        var trimmed = assistantText.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                return JArray.Parse(trimmed).Select(item => item.Value<string>() ?? string.Empty).ToArray();
            }
            catch
            {
                return new[] { assistantText };
            }
        }

        if (TryParseIndexedLines(trimmed, out var indexedTexts))
        {
            return indexedTexts;
        }

        return new[] { assistantText };
    }

    public static int ParseTotalTokens(string json)
    {
        var root = JObject.Parse(json);
        return root["usage"]?["total_tokens"]?.Value<int?>()
            ?? root["usage"]?["totalTokens"]?.Value<int?>()
            ?? 0;
    }

    private static void CollectOpenAiOutputText(JToken token, List<string> texts)
    {
        if (token is JObject obj)
        {
            if (string.Equals(obj["type"]?.Value<string>(), "output_text", StringComparison.OrdinalIgnoreCase))
            {
                var text = obj["text"]?.Value<string>();
                if (!string.IsNullOrEmpty(text))
                {
                    texts.Add(text);
                }
            }

            foreach (var child in obj.Properties().Select(property => property.Value))
            {
                CollectOpenAiOutputText(child, texts);
            }
        }
        else if (token is JArray array)
        {
            foreach (var child in array)
            {
                CollectOpenAiOutputText(child, texts);
            }
        }
    }

    private static bool TryParseIndexedLines(string text, out IReadOnlyList<string> indexedTexts)
    {
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        if (lines.Length == 0)
        {
            indexedTexts = Array.Empty<string>();
            return false;
        }

        var parsed = new string[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            var match = IndexedLinePattern.Match(lines[i]);
            if (!match.Success ||
                !int.TryParse(match.Groups[1].Value, out var index) ||
                index != i)
            {
                indexedTexts = Array.Empty<string>();
                return false;
            }

            parsed[i] = match.Groups[2].Value.Trim();
        }

        indexedTexts = parsed;
        return true;
    }
}
