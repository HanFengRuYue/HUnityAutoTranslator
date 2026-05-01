namespace HUnityAutoTranslator.Core.Control;

public sealed record TextureImageProviderProfileUpdateRequest(
    string? Id = null,
    string? Name = null,
    bool? Enabled = null,
    int? Priority = null,
    string? BaseUrl = null,
    string? EditEndpoint = null,
    string? VisionEndpoint = null,
    string? ImageModel = null,
    string? VisionModel = null,
    string? Quality = null,
    int? TimeoutSeconds = null,
    int? MaxConcurrentRequests = null,
    bool? EnableVisionConfirmation = null,
    string? ApiKey = null,
    bool? ClearApiKey = null);
