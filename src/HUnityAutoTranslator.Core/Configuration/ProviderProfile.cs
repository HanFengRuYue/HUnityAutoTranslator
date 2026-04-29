namespace HUnityAutoTranslator.Core.Configuration;

public sealed record ProviderProfile(
    ProviderKind Kind,
    string BaseUrl,
    string Endpoint,
    string Model,
    bool ApiKeyConfigured,
    string? OpenAICompatibleCustomHeaders = null,
    string? OpenAICompatibleExtraBodyJson = null)
{
    public static ProviderProfile DefaultOpenAi()
    {
        return new ProviderProfile(ProviderKind.OpenAI, "https://api.openai.com", "/v1/responses", "gpt-5.5", false);
    }

    public static ProviderProfile DefaultDeepSeek()
    {
        return new ProviderProfile(ProviderKind.DeepSeek, "https://api.deepseek.com", "/chat/completions", "deepseek-v4-flash", false);
    }

    public static ProviderProfile DefaultLlamaCpp()
    {
        return new ProviderProfile(ProviderKind.LlamaCpp, "http://127.0.0.1:0", "/v1/chat/completions", "local-model", true);
    }
}
