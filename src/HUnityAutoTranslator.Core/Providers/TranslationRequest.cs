namespace HUnityAutoTranslator.Core.Providers;

public sealed record TranslationRequest(
    IReadOnlyList<string> ProtectedTexts,
    string TargetLanguage,
    string SystemPrompt,
    string UserPrompt);
