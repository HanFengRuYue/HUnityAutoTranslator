namespace HUnityAutoTranslator.Core.Dispatching;

public sealed class TranslationResult
{
    public TranslationResult(
        string targetId,
        string sourceText,
        string translatedText,
        int priority,
        string? previousTranslatedText = null)
    {
        TargetId = targetId;
        SourceText = sourceText;
        TranslatedText = translatedText;
        Priority = priority;
        PreviousTranslatedText = string.IsNullOrEmpty(previousTranslatedText) ? null : previousTranslatedText;
    }

    public string TargetId { get; }

    public string SourceText { get; }

    public string TranslatedText { get; }

    public int Priority { get; }

    public string? PreviousTranslatedText { get; }
}
