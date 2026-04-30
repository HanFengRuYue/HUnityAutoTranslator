using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Providers;

public sealed record TranslationResponse(
    bool Succeeded,
    IReadOnlyList<string> TranslatedTexts,
    string? ErrorMessage,
    int TotalTokens = 0,
    ProviderProfile? Provider = null,
    string? ProviderProfileId = null,
    string? ProviderProfileName = null)
{
    public static TranslationResponse Success(
        IReadOnlyList<string> translatedTexts,
        int totalTokens = 0,
        ProviderProfile? provider = null,
        string? providerProfileId = null,
        string? providerProfileName = null)
    {
        return new TranslationResponse(true, translatedTexts, null, totalTokens, provider, providerProfileId, providerProfileName);
    }

    public static TranslationResponse Failure(
        string errorMessage,
        ProviderProfile? provider = null,
        string? providerProfileId = null,
        string? providerProfileName = null)
    {
        return new TranslationResponse(false, Array.Empty<string>(), errorMessage, 0, provider, providerProfileId, providerProfileName);
    }
}
