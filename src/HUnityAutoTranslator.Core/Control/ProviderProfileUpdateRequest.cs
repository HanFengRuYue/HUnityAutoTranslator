using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Control;

public sealed record ProviderProfileUpdateRequest(
    string? Id = null,
    string? Name = null,
    bool? Enabled = null,
    int? Priority = null,
    ProviderKind? Kind = null,
    string? BaseUrl = null,
    string? Endpoint = null,
    string? Model = null,
    string? ApiKey = null,
    bool? ClearApiKey = null,
    int? MaxConcurrentRequests = null,
    int? RequestsPerMinute = null,
    int? RequestTimeoutSeconds = null,
    string? ReasoningEffort = null,
    string? OutputVerbosity = null,
    string? DeepSeekThinkingMode = null,
    string? OpenAICompatibleCustomHeaders = null,
    string? OpenAICompatibleExtraBodyJson = null,
    LlamaCppConfig? LlamaCpp = null,
    double? Temperature = null,
    bool? ClearTemperature = null,
    string? PresetId = null,
    bool? ClearPresetId = null);
