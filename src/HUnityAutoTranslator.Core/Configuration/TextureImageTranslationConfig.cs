namespace HUnityAutoTranslator.Core.Configuration;

public sealed record TextureImageTranslationConfig(
    bool Enabled,
    string BaseUrl,
    string EditEndpoint,
    string VisionEndpoint,
    string ImageModel,
    string VisionModel,
    string Quality,
    int TimeoutSeconds,
    int MaxConcurrentRequests,
    bool EnableVisionConfirmation)
{
    public static TextureImageTranslationConfig Default()
    {
        return new TextureImageTranslationConfig(
            Enabled: false,
            BaseUrl: "https://api.openai.com",
            EditEndpoint: "/v1/images/edits",
            VisionEndpoint: "/v1/responses",
            ImageModel: "gpt-image-2",
            VisionModel: "gpt-5.4-mini",
            Quality: "medium",
            TimeoutSeconds: 180,
            MaxConcurrentRequests: 1,
            EnableVisionConfirmation: true);
    }
}
