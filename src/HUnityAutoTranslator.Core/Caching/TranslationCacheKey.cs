using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Caching;

public sealed record TranslationCacheKey(
    string SourceText,
    string TargetLanguage,
    ProviderKind ProviderKind,
    string ProviderBaseUrl,
    string ProviderEndpoint,
    string ProviderModel,
    string PromptPolicyVersion)
{
    public static TranslationCacheKey Create(string sourceText, string targetLanguage, ProviderProfile provider, string promptPolicyVersion)
    {
        return new TranslationCacheKey(
            sourceText,
            targetLanguage.Trim(),
            provider.Kind,
            provider.BaseUrl.TrimEnd('/'),
            provider.Endpoint.Trim(),
            provider.Model.Trim(),
            promptPolicyVersion.Trim());
    }
}
