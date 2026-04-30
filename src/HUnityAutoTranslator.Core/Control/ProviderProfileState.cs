using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Control;

public sealed record ProviderProfileState(
    string Id,
    string Name,
    bool Enabled,
    int Priority,
    ProviderKind Kind,
    string BaseUrl,
    string Endpoint,
    string Model,
    bool ApiKeyConfigured,
    string? ApiKeyPreview,
    int MaxConcurrentRequests,
    int RequestsPerMinute,
    int RequestTimeoutSeconds,
    string ReasoningEffort,
    string OutputVerbosity,
    string DeepSeekThinkingMode,
    string? OpenAICompatibleCustomHeaders,
    string? OpenAICompatibleExtraBodyJson,
    LlamaCppConfig? LlamaCpp,
    double? Temperature,
    bool IsActive,
    int ConsecutiveFailureCount,
    int CooldownRemainingSeconds,
    string? LastError);
