using System.Text.RegularExpressions;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Runtime;

internal static class ImguiTextClassifier
{
    private static readonly Regex LoadingProgressPattern = new(
        @"\bLoading\s*:\s*\d+\s*/\s*\d+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
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

        return TextFilter.IsAlreadyTargetLanguageSource(normalized, targetLanguage);
    }

    public static bool ShouldPrepareFontForSkippedText(string sourceText, string targetLanguage)
    {
        var normalized = TextNormalizer.NormalizeForCache(sourceText);
        if (normalized.Length == 0 || IsHighChurnProgressText(normalized))
        {
            return false;
        }

        return TextFilter.IsAlreadyTargetLanguageSource(normalized, targetLanguage);
    }

    private static bool IsHighChurnProgressText(string value)
    {
        return LoadingProgressPattern.IsMatch(value) ||
            LeadingProgressPattern.IsMatch(value) ||
            ShowingRangePattern.IsMatch(value) ||
            PagePattern.IsMatch(value);
    }
}
