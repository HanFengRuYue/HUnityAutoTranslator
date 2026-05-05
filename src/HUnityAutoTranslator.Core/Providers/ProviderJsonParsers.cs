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

    public static IReadOnlyList<string> ParseAssistantTextAsList(string assistantText, int? expectedCount = null)
    {
        var trimmed = assistantText.Trim();
        if (TryParseJsonResponse(trimmed, expectedCount, out var jsonTexts))
        {
            return jsonTexts;
        }

        if (TryParseIndexedLines(trimmed, out var indexedTexts))
        {
            return indexedTexts;
        }

        return new[] { assistantText };
    }

    private static bool TryParseJsonResponse(string text, int? expectedCount, out IReadOnlyList<string> texts)
    {
        texts = Array.Empty<string>();
        if (text.Length == 0 ||
            (!text.StartsWith("[", StringComparison.Ordinal) &&
             !text.StartsWith("{", StringComparison.Ordinal) &&
             !text.StartsWith("\"", StringComparison.Ordinal)))
        {
            return false;
        }

        try
        {
            var token = JToken.Parse(text);
            if (token.Type == JTokenType.String && expectedCount == 1)
            {
                texts = new[] { token.Value<string>() ?? string.Empty };
                return true;
            }

            if (token is JArray array)
            {
                return TryParseStringArray(array, out texts) ||
                    TryParseIndexedObjectArray(array, expectedCount, out texts);
            }

            return token is JObject obj &&
                TryParseSingleIndexedObject(obj, expectedCount, out texts);
        }
        catch
        {
            texts = Array.Empty<string>();
            return false;
        }
    }

    private static bool TryParseStringArray(JArray array, out IReadOnlyList<string> texts)
    {
        var parsed = new string[array.Count];
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i].Type != JTokenType.String)
            {
                texts = Array.Empty<string>();
                return false;
            }

            parsed[i] = array[i].Value<string>() ?? string.Empty;
        }

        texts = parsed;
        return true;
    }

    private static bool TryParseSingleIndexedObject(
        JObject obj,
        int? expectedCount,
        out IReadOnlyList<string> texts)
    {
        texts = Array.Empty<string>();
        if (expectedCount.HasValue && expectedCount.Value != 1)
        {
            return false;
        }

        if (!TryGetIndexedObjectTranslation(obj, out var index, out var text) || index != 0)
        {
            return false;
        }

        texts = new[] { text };
        return true;
    }

    private static bool TryParseIndexedObjectArray(
        JArray array,
        int? expectedCount,
        out IReadOnlyList<string> texts)
    {
        texts = Array.Empty<string>();
        var expected = expectedCount ?? array.Count;
        if (expected < 0 || array.Count != expected)
        {
            return false;
        }

        var parsed = new string[expected];
        var seen = new bool[expected];
        foreach (var item in array)
        {
            if (item is not JObject obj ||
                !TryGetIndexedObjectTranslation(obj, out var index, out var text) ||
                index < 0 ||
                index >= expected ||
                seen[index])
            {
                texts = Array.Empty<string>();
                return false;
            }

            parsed[index] = text;
            seen[index] = true;
        }

        if (seen.Any(item => !item))
        {
            texts = Array.Empty<string>();
            return false;
        }

        texts = parsed;
        return true;
    }

    private static bool TryGetIndexedObjectTranslation(JObject obj, out int index, out string text)
    {
        index = -1;
        text = string.Empty;
        var indexToken = obj["text_index"];
        if (indexToken?.Type != JTokenType.Integer)
        {
            return false;
        }

        var textToken = obj["text"];
        if (textToken?.Type != JTokenType.String)
        {
            textToken = obj["translation"];
        }

        if (textToken?.Type != JTokenType.String)
        {
            return false;
        }

        index = indexToken.Value<int>();
        text = textToken.Value<string>() ?? string.Empty;
        return true;
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
            .Replace("\r\n", "\n")
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
