namespace HUnityAutoTranslator.Core.Configuration;

public static class RuntimeConfigLimits
{
    public const int MinConcurrentRequests = 1;
    public const int DefaultMaxConcurrentRequests = 4;
    public const int MaxOnlineConcurrentRequests = 100;
    public const int MinRequestsPerMinute = 1;
    public const int MaxRequestsPerMinute = 15000;
    public const int MaxLlamaCppParallelSlots = 16;
    public const int MinLlamaCppBatchSize = 128;
    public const int MaxLlamaCppBatchSize = 8192;
    public const int MinLlamaCppUBatchSize = 64;
    public const int MaxLlamaCppUBatchSize = 4096;

    public static int ClampOnlineConcurrentRequests(int value)
    {
        return Clamp(value, MinConcurrentRequests, MaxOnlineConcurrentRequests);
    }

    public static int ClampRequestsPerMinute(int value)
    {
        return Clamp(value, MinRequestsPerMinute, MaxRequestsPerMinute);
    }

    public static int ClampLlamaCppParallelSlots(int value)
    {
        return Clamp(value, MinConcurrentRequests, MaxLlamaCppParallelSlots);
    }

    public static int ClampLlamaCppBatchSize(int value)
    {
        return Clamp(value, MinLlamaCppBatchSize, MaxLlamaCppBatchSize);
    }

    public static int ClampLlamaCppUBatchSize(int value, int batchSize)
    {
        return Math.Min(batchSize, Clamp(value, MinLlamaCppUBatchSize, MaxLlamaCppUBatchSize));
    }

    public static string NormalizeLlamaCppFlashAttentionMode(string? value)
    {
        if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
        {
            return "on";
        }

        if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
        {
            return "off";
        }

        return "auto";
    }

    public static int GetEffectiveMaxConcurrentRequests(RuntimeConfig config)
    {
        return config.Provider.Kind == ProviderKind.LlamaCpp
            ? ClampLlamaCppParallelSlots(config.LlamaCpp.ParallelSlots)
            : ClampOnlineConcurrentRequests(config.MaxConcurrentRequests);
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }
}
