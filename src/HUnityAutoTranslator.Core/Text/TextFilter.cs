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

    public static bool IsAlreadyTargetLanguageSource(string? value, string? targetLanguage)
    {
        if (!IsSimplifiedChineseTarget(targetLanguage))
        {
            return false;
        }

        var normalized = TextNormalizer.NormalizeForCache(RichTextGuard.GetVisibleText(value ?? string.Empty));
        return normalized.Length > 0 &&
            ContainsCjk(normalized) &&
            !ContainsKanaOrHangul(normalized) &&
            !ContainsTranslatableLatinToken(normalized);
    }

    private static bool IsSimplifiedChineseTarget(string? targetLanguage)
    {
        var normalized = (targetLanguage ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "zh" or "zh-hans" or "zh-cn" or "zh-sg" or "simplified chinese";
    }

    private static bool ContainsTranslatableLatinToken(string value)
    {
        var token = string.Empty;
        foreach (var character in value)
        {
            if (IsAsciiLetterOrDigit(character))
            {
                token += character;
                continue;
            }

            if (TokenRequiresTranslation(token))
            {
                return true;
            }

            token = string.Empty;
        }

        return TokenRequiresTranslation(token);
    }

    private static bool TokenRequiresTranslation(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (IsShortTechnicalToken(token))
        {
            return false;
        }

        return token.Any(IsAsciiLetter);
    }

    private static bool IsShortTechnicalToken(string token)
    {
        var compact = token.Trim();
        if (compact.Length <= 4 && compact.All(character => char.IsUpper(character) || char.IsDigit(character)))
        {
            return true;
        }

        return compact.ToLowerInvariant() is "fps" or "hz" or "ui" or "tmp" or "ugui" or "imgui" or "ms" or "cpu" or "gpu" or "ram" or "vr";
    }

    private static bool IsAsciiLetterOrDigit(char character)
    {
        return IsAsciiLetter(character) || char.IsDigit(character);
    }

    private static bool IsAsciiLetter(char character)
    {
        return (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z');
    }

    private static bool ContainsCjk(string value)
    {
        return value.Any(character =>
            (character >= '\u3400' && character <= '\u4DBF') ||
            (character >= '\u4E00' && character <= '\u9FFF') ||
            (character >= '\uF900' && character <= '\uFAFF'));
    }

    private static bool ContainsKanaOrHangul(string value)
    {
        return value.Any(character =>
            (character >= '\u3040' && character <= '\u30FF') ||
            (character >= '\u31F0' && character <= '\u31FF') ||
            (character >= '\u1100' && character <= '\u11FF') ||
            (character >= '\u3130' && character <= '\u318F') ||
            (character >= '\uAC00' && character <= '\uD7AF'));
    }

    private static bool IsNumericSpecification(string value)
    {
        return ResolutionPattern.IsMatch(value) ||
            RatePattern.IsMatch(value) ||
            MultiplierPattern.IsMatch(value);
    }
}
