namespace HUnityAutoTranslator.Core.Configuration;

public static class RuntimeConfigLimits
{
    public const int MinConcurrentRequests = 1;
    public const int DefaultMaxConcurrentRequests = 4;
    public const int MaxOnlineConcurrentRequests = 100;
    public const int MinRequestsPerMinute = 1;
    public const int MaxRequestsPerMinute = 600;
    public const int MaxLlamaCppParallelSlots = 16;

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
