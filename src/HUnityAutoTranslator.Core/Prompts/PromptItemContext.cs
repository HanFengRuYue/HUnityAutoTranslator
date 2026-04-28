namespace HUnityAutoTranslator.Core.Prompts;

public sealed record PromptItemContext(
    int TextIndex,
    string? SceneName,
    string? ComponentHierarchy,
    string? ComponentType);
