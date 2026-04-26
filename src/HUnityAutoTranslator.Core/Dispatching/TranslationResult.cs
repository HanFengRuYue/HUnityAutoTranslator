namespace HUnityAutoTranslator.Core.Dispatching;

public sealed class TranslationResult
{
    public TranslationResult(
        string targetId,
        string sourceText,
        string translatedText,
        int priority,
        string? previousTranslatedText = null,
        string? sceneName = null,
        string? componentHierarchy = null,
        string? componentType = null,
        DateTimeOffset? updatedUtc = null)
    {
        TargetId = targetId;
        SourceText = sourceText;
        TranslatedText = translatedText;
        Priority = priority;
        PreviousTranslatedText = string.IsNullOrEmpty(previousTranslatedText) ? null : previousTranslatedText;
        SceneName = sceneName;
        ComponentHierarchy = componentHierarchy;
        ComponentType = componentType;
        UpdatedUtc = updatedUtc ?? DateTimeOffset.UtcNow;
    }

    public string TargetId { get; }

    public string SourceText { get; }

    public string TranslatedText { get; }

    public int Priority { get; }

    public string? PreviousTranslatedText { get; }

    public string? SceneName { get; }

    public string? ComponentHierarchy { get; }

    public string? ComponentType { get; }

    public DateTimeOffset UpdatedUtc { get; }

    public bool HasComponentContext => !string.IsNullOrWhiteSpace(ComponentHierarchy);
}
