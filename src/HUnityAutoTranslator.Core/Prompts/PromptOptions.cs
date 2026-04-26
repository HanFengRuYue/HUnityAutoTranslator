namespace HUnityAutoTranslator.Core.Prompts;

public sealed record PromptOptions(
    string TargetLanguage,
    TranslationStyle Style,
    string? CustomInstruction,
    string? CustomPrompt = null);
