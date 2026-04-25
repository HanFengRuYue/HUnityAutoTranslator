namespace HUnityAutoTranslator.Core.Dispatching;

public sealed class TranslationResult
{
    public TranslationResult(string targetId, string sourceText, string translatedText, int priority)
    {
        TargetId = targetId;
        SourceText = sourceText;
        TranslatedText = translatedText;
        Priority = priority;
    }

    public string TargetId { get; }

    public string SourceText { get; }

    public string TranslatedText { get; }

    public int Priority { get; }
}
