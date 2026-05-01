using System.Text.RegularExpressions;

namespace HUnityAutoTranslator.Core.Text;

public static class PreservableTextClassifier
{
    private static readonly Regex VersionPattern = new(
        @"^(?:v(?:ersion)?\s*)?\d+(?:[._-]\d+){1,5}(?:[-+][A-Za-z0-9._-]+)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UrlPattern = new(
        @"^(?:[a-z][a-z0-9+.-]*://|www\.)\S+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EmailPattern = new(
        @"^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HandlePattern = new(
        @"^@[A-Za-z0-9_.-]{2,}$",
        RegexOptions.Compiled);

    private static readonly Regex AuthorCreditPattern = new(
        @"^(?:by|author|created\s+by|made\s+by)\s+.+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HeartCreditPattern = new(
        @"^[A-Za-z0-9_.-]{3,}\s*(?:<3|\u2665|\u2764\ufe0f?)$",
        RegexOptions.Compiled);

    private static readonly Regex FileNamePattern = new(
        @"^[^\s\\/]+(?:\.[A-Za-z0-9_-]{1,12}){1,3}$",
        RegexOptions.Compiled);

    private static readonly Regex RootedPathPattern = new(
        @"^(?:[A-Za-z]:[\\/]|\\\\|/).+$",
        RegexOptions.Compiled);

    private static readonly Regex RelativePathPattern = new(
        @"^(?:[A-Za-z0-9_.-]+[\\/])+[A-Za-z0-9_.-]+$",
        RegexOptions.Compiled);

    public static bool ShouldSkipTranslation(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return false;
        }

        return IsPureVersion(normalized) ||
            IsUrl(normalized) ||
            IsEmail(normalized) ||
            IsPath(normalized) ||
            IsHandle(normalized) ||
            IsAuthorCredit(normalized) ||
            IsFileName(normalized);
    }

    public static bool CanRemainUntranslated(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (ShouldSkipTranslation(normalized) || IsTechnicalIdentifier(normalized))
        {
            return true;
        }

        var parts = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 && parts.All(part =>
            ShouldSkipTranslation(part) ||
            IsTechnicalIdentifier(part) ||
            string.Equals(part, "by", StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string? value)
    {
        return RichTextGuard.GetVisibleText(value ?? string.Empty).Trim();
    }

    private static bool IsPureVersion(string value)
    {
        return VersionPattern.IsMatch(value);
    }

    private static bool IsUrl(string value)
    {
        return UrlPattern.IsMatch(value);
    }

    private static bool IsEmail(string value)
    {
        return EmailPattern.IsMatch(value);
    }

    private static bool IsHandle(string value)
    {
        return HandlePattern.IsMatch(value);
    }

    private static bool IsAuthorCredit(string value)
    {
        return AuthorCreditPattern.IsMatch(value) || HeartCreditPattern.IsMatch(value);
    }

    private static bool IsPath(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal) &&
            !value.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        if (RootedPathPattern.IsMatch(value))
        {
            return true;
        }

        if (value.Any(char.IsWhiteSpace) || !RelativePathPattern.IsMatch(value))
        {
            return false;
        }

        var separatorCount = value.Count(character => character is '\\' or '/');
        return separatorCount >= 2 || IsFileName(Path.GetFileName(value));
    }

    private static bool IsFileName(string value)
    {
        if (!FileNamePattern.IsMatch(value))
        {
            return false;
        }

        var extension = Path.GetExtension(value);
        return extension.Length is >= 2 and <= 12;
    }

    private static bool IsTechnicalIdentifier(string value)
    {
        if (value.Any(char.IsWhiteSpace) || !value.Any(IsLatinLetter))
        {
            return false;
        }

        return value.Contains('.', StringComparison.Ordinal) ||
            value.Contains('_', StringComparison.Ordinal) ||
            value.Contains("::", StringComparison.Ordinal) ||
            value.Contains('#', StringComparison.Ordinal);
    }

    private static bool IsLatinLetter(char character)
    {
        return (character >= 'A' && character <= 'Z') ||
            (character >= 'a' && character <= 'z');
    }
}
