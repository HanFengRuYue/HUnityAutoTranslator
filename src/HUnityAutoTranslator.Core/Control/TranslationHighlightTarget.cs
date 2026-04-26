namespace HUnityAutoTranslator.Core.Control;

public sealed record TranslationHighlightTarget(
    string TargetId,
    string? SceneName,
    string? ComponentHierarchy,
    string? ComponentType,
    bool IsAlive,
    bool IsVisible);
