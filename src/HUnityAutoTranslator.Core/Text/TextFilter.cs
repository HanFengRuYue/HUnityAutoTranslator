using System.Globalization;
using System.Text.RegularExpressions;

namespace HUnityAutoTranslator.Core.Text;

public static class TextFilter
{
    private static readonly Regex ResolutionPattern = new(@"^\d{2,5}\s*(?:x|X|\u00d7|\*)\s*\d{2,5}$", RegexOptions.Compiled);
    private static readonly Regex RatePattern = new(@"^\d+(?:\.\d+)?\s*(?:fps|hz)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MultiplierPattern = new(@"^\d+(?:\.\d+)?\s*x$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool ShouldTranslate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = TextNormalizer.NormalizeForCache(value);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (IsNumericSpecification(normalized))
        {
            return false;
        }

        if (PreservableTextClassifier.ShouldSkipTranslation(normalized))
        {
            return false;
        }

        var hasLetter = false;
        var hasNonSymbol = false;
        var nonSymbolCount = 0;

        foreach (var ch in normalized)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            var category = char.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.DecimalDigitNumber)
            {
                nonSymbolCount++;
                hasNonSymbol = true;
                continue;
            }

            if (char.IsLetter(ch))
            {
                nonSymbolCount++;
                hasLetter = true;
                hasNonSymbol = true;
                continue;
            }

            if (category is not UnicodeCategory.OtherPunctuation
                and not UnicodeCategory.MathSymbol
                and not UnicodeCategory.CurrencySymbol
                and not UnicodeCategory.ModifierSymbol
                and not UnicodeCategory.OtherSymbol
                and not UnicodeCategory.DashPunctuation
                and not UnicodeCategory.OpenPunctuation
                and not UnicodeCategory.ClosePunctuation
                and not UnicodeCategory.InitialQuotePunctuation
                and not UnicodeCategory.FinalQuotePunctuation
                and not UnicodeCategory.ConnectorPunctuation)
            {
                nonSymbolCount++;
                hasNonSymbol = true;
            }
        }

        return hasLetter && hasNonSymbol && nonSymbolCount >= 2;
    }

    private static bool IsNumericSpecification(string value)
    {
        return ResolutionPattern.IsMatch(value) ||
            RatePattern.IsMatch(value) ||
            MultiplierPattern.IsMatch(value);
    }
}
