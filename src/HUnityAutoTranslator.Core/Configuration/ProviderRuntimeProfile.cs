namespace HUnityAutoTranslator.Core.Configuration;

public sealed record ProviderRuntimeProfile(
    string Id,
    string Name,
    ProviderProfile Profile,
    string? ApiKey,
    int MaxConcurrentRequests,
    int RequestsPerMinute,
    int RequestTimeoutSeconds,
    string ReasoningEffort,
    string OutputVerbosity,
    string DeepSeekThinkingMode,
    double? Temperature,
    LlamaCppConfig? LlamaCpp = null,
    string? PresetId = null)
{
    public static ProviderRuntimeProfile Create(ProviderProfileDefinition definition)
    {
        var normalized = definition.Normalize();
        return new ProviderRuntimeProfile(
            normalized.Id,
            normalized.Name,
            normalized.ToProviderProfile(),
            normalized.ApiKey,
            normalized.MaxConcurrentRequests,
            normalized.RequestsPerMinute,
            normalized.RequestTimeoutSeconds,
            normalized.ReasoningEffort,
            normalized.OutputVerbosity,
            normalized.DeepSeekThinkingMode,
            normalized.Temperature,
            normalized.LlamaCpp,
            normalized.PresetId);
    }

    public RuntimeConfig ApplyTo(RuntimeConfig config)
    {
        return config with
        {
            Provider = Profile,
            MaxConcurrentRequests = MaxConcurrentRequests,
            RequestsPerMinute = RequestsPerMinute,
            RequestTimeoutSeconds = RequestTimeoutSeconds,
            ReasoningEffort = ReasoningEffort,
            OutputVerbosity = OutputVerbosity,
            DeepSeekThinkingMode = DeepSeekThinkingMode,
            OpenAICompatibleCustomHeaders = Profile.OpenAICompatibleCustomHeaders,
            OpenAICompatibleExtraBodyJson = Profile.OpenAICompatibleExtraBodyJson,
            Temperature = Temperature,
            LlamaCpp = LlamaCpp ?? config.LlamaCpp
        };
    }
}
