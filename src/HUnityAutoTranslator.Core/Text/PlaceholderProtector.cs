using System.Text.RegularExpressions;

namespace HUnityAutoTranslator.Core.Text;

public static class PlaceholderProtector
{
    private static readonly Regex PlaceholderRegex = new(
        @"\\r\\n|\\n|\\t|\{[A-Za-z0-9_]+\}|%[-+#0 ]?(?:\d+|\*)?(?:\.\d+)?[A-Za-z]",
        RegexOptions.Compiled);

    public static ProtectedText Protect(string value)
    {
        var tokens = new Dictionary<string, string>();
        var index = 0;
        var text = PlaceholderRegex.Replace(value, match =>
        {
            var token = $"__HUT_TOKEN_{index++}__";
            tokens[token] = match.Value;
            return token;
        });

        return new ProtectedText(text, tokens);
    }

    public static IReadOnlyList<string> ExtractPlaceholders(string value)
    {
        var matches = PlaceholderRegex.Matches(value);
        if (matches.Count == 0)
        {
            return Array.Empty<string>();
        }

        var placeholders = new string[matches.Count];
        for (var i = 0; i < matches.Count; i++)
        {
            placeholders[i] = matches[i].Value;
        }

        return placeholders;
    }
}
