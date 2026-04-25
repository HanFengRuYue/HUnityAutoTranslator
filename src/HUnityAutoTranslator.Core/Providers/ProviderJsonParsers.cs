using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Providers;

public static class ProviderJsonParsers
{
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

        return new[] { assistantText };
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
}
