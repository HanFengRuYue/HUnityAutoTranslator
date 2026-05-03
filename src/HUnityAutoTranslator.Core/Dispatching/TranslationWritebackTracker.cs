namespace HUnityAutoTranslator.Core.Dispatching;

public sealed class TranslationWritebackTracker
{
    private readonly Dictionary<string, RememberedTranslation> _remembered = new(StringComparer.Ordinal);

    public void Remember(string targetId, string sourceText, string translatedText, string? previousTranslatedText = null)
    {
        if (string.IsNullOrEmpty(targetId)
            || string.IsNullOrEmpty(sourceText)
            || string.IsNullOrEmpty(translatedText))
        {
            return;
        }

        _remembered[targetId] = new RememberedTranslation(
            sourceText,
            translatedText,
            string.IsNullOrEmpty(previousTranslatedText) ? null : previousTranslatedText);
    }

    public void Forget(string targetId)
    {
        _remembered.Remove(targetId);
    }

    public bool IsRememberedTranslation(string targetId, string? currentText)
    {
        return currentText != null
            && _remembered.TryGetValue(targetId, out var remembered)
            && string.Equals(currentText, remembered.TranslatedText, StringComparison.Ordinal);
    }

    public bool IsRememberedSourceText(string targetId, string? currentText)
    {
        return currentText != null
            && _remembered.TryGetValue(targetId, out var remembered)
            && string.Equals(currentText, remembered.SourceText, StringComparison.Ordinal);
    }

    public bool TryGetRememberedSourceText(string targetId, string? currentText, out string sourceText)
    {
        if (currentText != null &&
            _remembered.TryGetValue(targetId, out var remembered) &&
            string.Equals(currentText, remembered.TranslatedText, StringComparison.Ordinal))
        {
            sourceText = remembered.SourceText;
            return true;
        }

        sourceText = string.Empty;
        return false;
    }

    public bool TryRememberForCurrentText(
        string targetId,
        string? currentText,
        string sourceText,
        string translatedText,
        string? previousTranslatedText = null)
    {
        if (currentText == null ||
            !CanBindToCurrentText(currentText, sourceText, translatedText, previousTranslatedText))
        {
            return false;
        }

        Remember(targetId, sourceText, translatedText, previousTranslatedText);
        return true;
    }

    public bool TryRestoreSourceText(
        string targetId,
        string? currentText,
        string sourceText,
        string? previousTranslatedText,
        out string replacement)
    {
        if (string.IsNullOrEmpty(targetId) ||
            string.IsNullOrEmpty(sourceText) ||
            currentText == null ||
            !CanRestoreSourceText(targetId, currentText, sourceText, previousTranslatedText))
        {
            replacement = string.Empty;
            return false;
        }

        Forget(targetId);
        replacement = sourceText;
        return true;
    }

    public bool TryGetReplacement(string targetId, string? currentText, out string replacement)
    {
        return TryGetDisplayText(targetId, currentText, useTranslatedText: true, out replacement);
    }

    public bool TryGetDisplayText(string targetId, string? currentText, bool useTranslatedText, out string replacement)
    {
        if (!_remembered.TryGetValue(targetId, out var remembered) || currentText == null)
        {
            replacement = string.Empty;
            return false;
        }

        if (useTranslatedText && string.Equals(currentText, remembered.TranslatedText, StringComparison.Ordinal))
        {
            replacement = string.Empty;
            return false;
        }

        if (!useTranslatedText && string.Equals(currentText, remembered.SourceText, StringComparison.Ordinal))
        {
            replacement = string.Empty;
            return false;
        }

        if (useTranslatedText &&
            (string.Equals(currentText, remembered.SourceText, StringComparison.Ordinal) ||
             string.Equals(currentText, remembered.PreviousTranslatedText, StringComparison.Ordinal)))
        {
            replacement = remembered.TranslatedText;
            return true;
        }

        if (!useTranslatedText &&
            (string.Equals(currentText, remembered.TranslatedText, StringComparison.Ordinal) ||
             string.Equals(currentText, remembered.PreviousTranslatedText, StringComparison.Ordinal)))
        {
            replacement = remembered.SourceText;
            return true;
        }

        replacement = string.Empty;
        return false;
    }

    private static bool CanBindToCurrentText(
        string currentText,
        string sourceText,
        string translatedText,
        string? previousTranslatedText)
    {
        return string.Equals(currentText, sourceText, StringComparison.Ordinal) ||
            string.Equals(currentText, translatedText, StringComparison.Ordinal) ||
            string.Equals(currentText, previousTranslatedText, StringComparison.Ordinal);
    }

    private bool CanRestoreSourceText(
        string targetId,
        string currentText,
        string sourceText,
        string? previousTranslatedText)
    {
        if (string.Equals(currentText, sourceText, StringComparison.Ordinal) ||
            string.Equals(currentText, previousTranslatedText, StringComparison.Ordinal))
        {
            return true;
        }

        return _remembered.TryGetValue(targetId, out var remembered) &&
            (string.Equals(currentText, remembered.SourceText, StringComparison.Ordinal) ||
             string.Equals(currentText, remembered.TranslatedText, StringComparison.Ordinal) ||
             string.Equals(currentText, remembered.PreviousTranslatedText, StringComparison.Ordinal));
    }

    private sealed record RememberedTranslation(
        string SourceText,
        string TranslatedText,
        string? PreviousTranslatedText);
}
