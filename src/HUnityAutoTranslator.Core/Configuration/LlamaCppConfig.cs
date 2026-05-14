namespace HUnityAutoTranslator.Core.Configuration;

public sealed record LlamaCppConfig(
    string? ModelPath,
    int ContextSize,
    int GpuLayers,
    int ParallelSlots,
    int BatchSize = 2048,
    int UBatchSize = 512,
    string FlashAttentionMode = "auto",
    bool AutoStartOnStartup = false,
    int CacheReuseTokens = 256)
{
    public static LlamaCppConfig Default()
    {
        return new LlamaCppConfig(
            ModelPath: null,
            ContextSize: 4096,
            GpuLayers: 999,
            ParallelSlots: 1,
            BatchSize: 2048,
            UBatchSize: 512,
            FlashAttentionMode: "auto",
            AutoStartOnStartup: false,
            CacheReuseTokens: 256);
    }
}
