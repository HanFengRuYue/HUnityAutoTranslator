using System.Text.RegularExpressions;

namespace HUnityAutoTranslator.Core.Text;

public static class RichTextGuard
{
    private static readonly Regex TagRegex = new(@"<\s*(/?)\s*([A-Za-z][\w.-]*)(?:\s*=[^>]*)?\s*/?\s*>", RegexOptions.Compiled);

    public static bool HasSameTags(string source, string translated)
    {
        return ExtractTags(source).SequenceEqual(ExtractTags(translated), StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> ExtractTags(string value)
    {
        return TagRegex.Matches(value)
            .Select(match =>
            {
                var closePrefix = match.Groups[1].Value == "/" ? "/" : string.Empty;
                return closePrefix + match.Groups[2].Value.ToLowerInvariant();
            })
            .ToArray();
    }
}
