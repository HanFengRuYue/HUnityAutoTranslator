using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Control;

public sealed record TextureImageProviderProfileState(
    string Id,
    string Name,
    bool Enabled,
    int Priority,
    string BaseUrl,
    string EditEndpoint,
    string VisionEndpoint,
    string ImageModel,
    string VisionModel,
    string Quality,
    int TimeoutSeconds,
    int MaxConcurrentRequests,
    bool EnableVisionConfirmation,
    bool ApiKeyConfigured,
    string? ApiKeyPreview);

public sealed record TextureImageProviderRuntimeProfile(
    string Id,
    string Name,
    TextureImageTranslationConfig Config,
    string ApiKey);
