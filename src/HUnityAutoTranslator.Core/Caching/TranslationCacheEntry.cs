namespace HUnityAutoTranslator.Core.Caching;

public sealed record TranslationCacheEntry(
    string SourceText,
    string TargetLanguage,
    string ProviderKind,
    string ProviderBaseUrl,
    string ProviderEndpoint,
    string ProviderModel,
    string PromptPolicyVersion,
    string? TranslatedText,
    string? SceneName,
    string? ComponentHierarchy,
    string? ComponentType,
    string? ReplacementFont,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);
