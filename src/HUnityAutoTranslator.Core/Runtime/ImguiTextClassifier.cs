using System.Text.RegularExpressions;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Runtime;

internal static class ImguiTextClassifier
{
    private static readonly Regex LoadingProgressPattern = new(
        @"\bLoading\s*:\s*\d+\s*/\s*\d+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LatinWordPattern = new(
        "[A-Za-z]+",
        RegexOptions.Compiled);
    private static readonly Regex LeadingProgressPattern = new(
        @"^\s*\d+\s*/\s*\d+\b",
        RegexOptions.Compiled);
    private static readonly Regex ShowingRangePattern = new(
        @"\bShowing\s+\d+\s*-\s*\d+\s*/\s*\d+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PagePattern = new(
        @"(?:\bPage\b|页)\s*\d+\s*/\s*\d+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool ShouldSkipTranslation(string sourceText, string targetLanguage)
    {
        var normalized = TextNormalizer.NormalizeForCache(sourceText);
        if (normalized.Length == 0)
        {
            return true;
        }

        if (IsHighChurnProgressText(normalized))
        {
            return true;
        }

        return IsSimplifiedChineseTarget(targetLanguage) &&
            ContainsCjk(normalized) &&
            !ContainsTranslatableLatinWord(normalized);
    }

    public static bool ShouldPrepareFontForSkippedText(string sourceText, string targetLanguage)
    {
        var normalized = TextNormalizer.NormalizeForCache(sourceText);
        if (normalized.Length == 0 || IsHighChurnProgressText(normalized))
        {
            return false;
        }

        return IsSimplifiedChineseTarget(targetLanguage) &&
            ContainsCjk(normalized) &&
            !ContainsTranslatableLatinWord(normalized);
    }

    private static bool IsHighChurnProgressText(string value)
    {
        return LoadingProgressPattern.IsMatch(value) ||
            LeadingProgressPattern.IsMatch(value) ||
            ShowingRangePattern.IsMatch(value) ||
            PagePattern.IsMatch(value);
    }

    private static bool IsSimplifiedChineseTarget(string targetLanguage)
    {
        return targetLanguage.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
            targetLanguage.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase) ||
            targetLanguage.Equals("zh-CN", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsTranslatableLatinWord(string value)
    {
        foreach (Match match in LatinWordPattern.Matches(value))
        {
            var token = match.Value;
            if (token.Length <= 4 && token.All(IsAsciiUppercaseLetter))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsAsciiUppercaseLetter(char character)
    {
        return character >= 'A' && character <= 'Z';
    }

    private static bool ContainsCjk(string value)
    {
        return value.Any(character =>
            (character >= '\u3400' && character <= '\u4DBF') ||
            (character >= '\u4E00' && character <= '\u9FFF') ||
            (character >= '\uF900' && character <= '\uFAFF'));
    }
}
