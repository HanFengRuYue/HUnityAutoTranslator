namespace HUnityAutoTranslator.Core.Prompts;

public sealed record PromptOptions(
    string TargetLanguage,
    TranslationStyle Style,
    string? CustomPrompt = null,
    bool HasGlossaryTerms = false);
