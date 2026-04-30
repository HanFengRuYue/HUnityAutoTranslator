namespace HUnityAutoTranslator.Core.Control;

public sealed record RecentTranslationPreview(
    string SourceText,
    string TranslatedText,
    string TargetLanguage,
    string Provider,
    string Model,
    string? Context,
    DateTimeOffset CompletedUtc,
    string? ProviderProfileId = null,
    string? ProviderProfileName = null,
    string? ProviderProfileKind = null);
