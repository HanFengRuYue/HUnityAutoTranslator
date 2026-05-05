using System.Net.Http;
using HUnityAutoTranslator.Core.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Providers;

public static class OpenAICompatibleRequestOptions
{
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Content-Type"
    };

    public static string? NormalizeCustomHeaders(string? value, string? fallback = null)
    {
        if (value == null)
        {
            return fallback;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var headers = new List<string>();
        foreach (var rawLine in SplitLines(value))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                return fallback;
            }

            var name = line.Substring(0, separator).Trim();
            var headerValue = line.Substring(separator + 1).Trim();
            if (!IsHeaderNameAllowed(name))
            {
                return fallback;
            }

            if (ReservedHeaders.Contains(name))
            {
                continue;
            }

            headers.Add($"{name}: {headerValue}");
        }

        return headers.Count == 0 ? null : string.Join("\n", headers);
    }

    public static string? NormalizeExtraBodyJson(string? value, string? fallback = null)
    {
        if (value == null)
        {
            return fallback;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JToken.Parse(value) is JObject body
                ? JsonConvert.SerializeObject(body, Formatting.None)
                : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public static void ApplyCustomHeaders(HttpRequestMessage request, ProviderProfile profile)
    {
        if (profile.Kind != ProviderKind.OpenAICompatible)
        {
            return;
        }

        foreach (var header in ParseHeaders(profile.OpenAICompatibleCustomHeaders))
        {
            request.Headers.TryAddWithoutValidation(header.Name, header.Value);
        }
    }

    public static void ApplyExtraBody(JObject body, ProviderProfile profile)
    {
        if (profile.Kind != ProviderKind.OpenAICompatible)
        {
            return;
        }

        var normalized = NormalizeExtraBodyJson(profile.OpenAICompatibleExtraBodyJson);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var extra = JObject.Parse(normalized);
        foreach (var property in extra.Properties())
        {
            if (!body.ContainsKey(property.Name))
            {
                body[property.Name] = property.Value.DeepClone();
            }
        }
    }

    private static IEnumerable<(string Name, string Value)> ParseHeaders(string? value)
    {
        var normalized = NormalizeCustomHeaders(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            yield break;
        }

        foreach (var line in SplitLines(normalized))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            yield return (line.Substring(0, separator).Trim(), line.Substring(separator + 1).Trim());
        }
    }

    private static IEnumerable<string> SplitLines(string value)
    {
        return value.Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split(new[] { '\n' });
    }

    private static bool IsHeaderNameAllowed(string name)
    {
        return name.Length > 0 &&
            name.IndexOfAny(new[] { '\r', '\n', ':' }) < 0 &&
            name.All(c => c > 32 && c < 127);
    }
}
