using HUnityAutoTranslator.Core.Providers;

namespace HUnityAutoTranslator.Core.Configuration;

public sealed record ProviderProfileDefinition(
    string Id,
    string Name,
    bool Enabled,
    int Priority,
    ProviderKind Kind,
    string BaseUrl,
    string Endpoint,
    string Model,
    string? ApiKey,
    int MaxConcurrentRequests,
    int RequestsPerMinute,
    int RequestTimeoutSeconds,
    string ReasoningEffort,
    string OutputVerbosity,
    string DeepSeekThinkingMode,
    string? OpenAICompatibleCustomHeaders,
    string? OpenAICompatibleExtraBodyJson,
    double? Temperature,
    LlamaCppConfig? LlamaCpp = null)
{
    public static ProviderProfileDefinition CreateDefault(string? name, ProviderKind kind, int priority)
    {
        var profile = kind switch
        {
            ProviderKind.LlamaCpp => ProviderProfile.DefaultLlamaCpp(),
            ProviderKind.DeepSeek => ProviderProfile.DefaultDeepSeek(),
            ProviderKind.OpenAICompatible => new ProviderProfile(
                ProviderKind.OpenAICompatible,
                "http://127.0.0.1:8000",
                "/v1/chat/completions",
                "local-model",
                false),
            _ => ProviderProfile.DefaultOpenAi()
        };

        return new ProviderProfileDefinition(
            CreateId(),
            string.IsNullOrWhiteSpace(name) ? DefaultName(kind) : name.Trim(),
            Enabled: true,
            Priority: priority,
            Kind: NormalizeProfileKind(kind),
            BaseUrl: profile.BaseUrl,
            Endpoint: profile.Endpoint,
            Model: profile.Model,
            ApiKey: null,
            MaxConcurrentRequests: RuntimeConfigLimits.DefaultMaxConcurrentRequests,
            RequestsPerMinute: DefaultRequestsPerMinute(kind),
            RequestTimeoutSeconds: 30,
            ReasoningEffort: "none",
            OutputVerbosity: "low",
            DeepSeekThinkingMode: "disabled",
            OpenAICompatibleCustomHeaders: null,
            OpenAICompatibleExtraBodyJson: null,
            Temperature: null,
            LlamaCpp: kind == ProviderKind.LlamaCpp ? LlamaCppConfig.Default() : null);
    }

    public ProviderProfileDefinition Normalize()
    {
        var kind = NormalizeProfileKind(Kind);
        var defaults = CreateDefault(Name, kind, Priority);
        var customHeaders = OpenAICompatibleRequestOptions.NormalizeCustomHeaders(OpenAICompatibleCustomHeaders, null);
        var extraBodyJson = OpenAICompatibleRequestOptions.NormalizeExtraBodyJson(OpenAICompatibleExtraBodyJson, null);
        var llamaCpp = kind == ProviderKind.LlamaCpp
            ? NormalizeLlamaCppConfig(LlamaCpp)
            : null;

        return this with
        {
            Id = NormalizeId(Id),
            Name = string.IsNullOrWhiteSpace(Name) ? DefaultName(kind) : Name.Trim(),
            Priority = Math.Max(0, Priority),
            Kind = kind,
            BaseUrl = kind == ProviderKind.LlamaCpp
                ? ProviderProfile.DefaultLlamaCpp().BaseUrl
                : (string.IsNullOrWhiteSpace(BaseUrl) ? defaults.BaseUrl : BaseUrl.Trim()),
            Endpoint = kind == ProviderKind.LlamaCpp
                ? ProviderProfile.DefaultLlamaCpp().Endpoint
                : (string.IsNullOrWhiteSpace(Endpoint) ? defaults.Endpoint : Endpoint.Trim()),
            Model = kind == ProviderKind.LlamaCpp
                ? ProviderProfile.DefaultLlamaCpp().Model
                : (string.IsNullOrWhiteSpace(Model) ? defaults.Model : Model.Trim()),
            ApiKey = kind == ProviderKind.LlamaCpp || string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim(),
            MaxConcurrentRequests = kind == ProviderKind.LlamaCpp
                ? RuntimeConfigLimits.ClampLlamaCppParallelSlots(llamaCpp?.ParallelSlots ?? LlamaCppConfig.Default().ParallelSlots)
                : RuntimeConfigLimits.ClampOnlineConcurrentRequests(MaxConcurrentRequests),
            RequestsPerMinute = RuntimeConfigLimits.ClampRequestsPerMinute(RequestsPerMinute <= 0
                ? DefaultRequestsPerMinute(kind)
                : RequestsPerMinute),
            RequestTimeoutSeconds = Clamp(RequestTimeoutSeconds, 5, 180),
            ReasoningEffort = NormalizeReasoningEffort(kind, ReasoningEffort),
            OutputVerbosity = SelectKnown(OutputVerbosity, "low", "low", "medium", "high"),
            DeepSeekThinkingMode = SelectKnown(DeepSeekThinkingMode, "disabled", "enabled", "disabled"),
            OpenAICompatibleCustomHeaders = kind == ProviderKind.OpenAICompatible ? customHeaders : null,
            OpenAICompatibleExtraBodyJson = kind == ProviderKind.OpenAICompatible ? extraBodyJson : null,
            Temperature = kind == ProviderKind.LlamaCpp
                ? null
                : (Temperature.HasValue ? Clamp(Temperature.Value, 0.0, 2.0) : null),
            LlamaCpp = llamaCpp
        };
    }

    public ProviderProfile ToProviderProfile()
    {
        var normalized = Normalize();
        var ready = normalized.Kind == ProviderKind.OpenAICompatible ||
            normalized.Kind == ProviderKind.LlamaCpp ||
            !string.IsNullOrWhiteSpace(normalized.ApiKey);
        return new ProviderProfile(
            normalized.Kind,
            normalized.BaseUrl,
            normalized.Endpoint,
            normalized.Model,
            ready,
            normalized.Kind == ProviderKind.OpenAICompatible ? normalized.OpenAICompatibleCustomHeaders : null,
            normalized.Kind == ProviderKind.OpenAICompatible ? normalized.OpenAICompatibleExtraBodyJson : null);
    }

    public static string CreateId()
    {
        return Guid.NewGuid().ToString("N");
    }

    public static string NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return CreateId();
        }

        var trimmed = id.Trim();
        return trimmed.All(character =>
            (character >= 'a' && character <= 'z') ||
            (character >= 'A' && character <= 'Z') ||
            (character >= '0' && character <= '9') ||
            character == '_' ||
            character == '-')
            ? trimmed
            : CreateId();
    }

    public static bool IsOnlineKind(ProviderKind kind)
    {
        return kind is ProviderKind.OpenAI or ProviderKind.DeepSeek or ProviderKind.OpenAICompatible;
    }

    public static bool IsSupportedProfileKind(ProviderKind kind)
    {
        return IsOnlineKind(kind) || kind == ProviderKind.LlamaCpp;
    }

    private static ProviderKind NormalizeProfileKind(ProviderKind kind)
    {
        return IsSupportedProfileKind(kind) ? kind : ProviderKind.OpenAI;
    }

    private static string DefaultName(ProviderKind kind)
    {
        return kind switch
        {
            ProviderKind.LlamaCpp => "llama.cpp 本地模型",
            ProviderKind.DeepSeek => "DeepSeek",
            ProviderKind.OpenAICompatible => "OpenAI 兼容",
            _ => "OpenAI"
        };
    }

    public static int DefaultRequestsPerMinute(ProviderKind kind)
    {
        return NormalizeProfileKind(kind) switch
        {
            ProviderKind.OpenAI => 500,
            ProviderKind.DeepSeek => 15000,
            ProviderKind.OpenAICompatible => 15000,
            ProviderKind.LlamaCpp => 15000,
            _ => 500
        };
    }

    private static LlamaCppConfig NormalizeLlamaCppConfig(LlamaCppConfig? config)
    {
        var current = config ?? LlamaCppConfig.Default();
        var batchSize = RuntimeConfigLimits.ClampLlamaCppBatchSize(current.BatchSize);
        return new LlamaCppConfig(
            string.IsNullOrWhiteSpace(current.ModelPath) ? null : current.ModelPath.Trim(),
            Clamp(current.ContextSize, 512, 131072),
            Clamp(current.GpuLayers, 0, 999),
            RuntimeConfigLimits.ClampLlamaCppParallelSlots(current.ParallelSlots),
            batchSize,
            RuntimeConfigLimits.ClampLlamaCppUBatchSize(current.UBatchSize, batchSize),
            RuntimeConfigLimits.NormalizeLlamaCppFlashAttentionMode(current.FlashAttentionMode),
            current.AutoStartOnStartup,
            RuntimeConfigLimits.ClampLlamaCppCacheReuseTokens(current.CacheReuseTokens));
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static string SelectKnown(string? value, string fallback, params string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        return allowed.FirstOrDefault(item => string.Equals(item, trimmed, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    private static string NormalizeReasoningEffort(ProviderKind kind, string? value)
    {
        return NormalizeProfileKind(kind) switch
        {
            ProviderKind.DeepSeek => SelectKnown(value, "high", "high", "max"),
            ProviderKind.OpenAI => SelectKnown(value, "none", "none", "low", "medium", "high", "xhigh"),
            _ => SelectKnown(value, "none", "none", "low", "medium", "high", "xhigh", "max")
        };
    }
}
