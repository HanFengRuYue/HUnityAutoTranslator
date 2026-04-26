namespace HUnityAutoTranslator.Core.Dispatching;

public sealed class TranslationWritebackTracker
{
    private readonly Dictionary<string, RememberedTranslation> _remembered = new(StringComparer.Ordinal);

    public void Remember(string targetId, string sourceText, string translatedText)
    {
        if (string.IsNullOrEmpty(targetId)
            || string.IsNullOrEmpty(sourceText)
            || string.IsNullOrEmpty(translatedText))
        {
            return;
        }

        _remembered[targetId] = new RememberedTranslation(sourceText, translatedText);
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

    public bool TryGetReplacement(string targetId, string? currentText, out string replacement)
    {
        if (!_remembered.TryGetValue(targetId, out var remembered) || currentText == null)
        {
            replacement = string.Empty;
            return false;
        }

        if (string.Equals(currentText, remembered.TranslatedText, StringComparison.Ordinal))
        {
            replacement = string.Empty;
            return false;
        }

        if (string.Equals(currentText, remembered.SourceText, StringComparison.Ordinal))
        {
            replacement = remembered.TranslatedText;
            return true;
        }

        _remembered.Remove(targetId);
        replacement = string.Empty;
        return false;
    }

    private sealed record RememberedTranslation(string SourceText, string TranslatedText);
}
