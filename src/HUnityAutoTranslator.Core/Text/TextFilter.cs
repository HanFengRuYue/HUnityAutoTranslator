using System.Globalization;

namespace HUnityAutoTranslator.Core.Text;

public static class TextFilter
{
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
}
