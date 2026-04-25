namespace HUnityAutoTranslator.Core.Configuration;

public sealed record ProviderProfile(
    ProviderKind Kind,
    string BaseUrl,
    string Endpoint,
    string Model,
    bool ApiKeyConfigured)
{
    public static ProviderProfile DefaultOpenAi()
    {
        return new ProviderProfile(ProviderKind.OpenAI, "https://api.openai.com", "/v1/responses", "gpt-5.5", false);
    }

    public static ProviderProfile DefaultDeepSeek()
    {
        return new ProviderProfile(ProviderKind.DeepSeek, "https://api.deepseek.com", "/chat/completions", "deepseek-v4-flash", false);
    }
}
