namespace HUnityAutoTranslator.Core.Configuration;

public sealed record LlamaCppConfig(
    string? ModelPath,
    int ContextSize,
    int GpuLayers,
    int ParallelSlots)
{
    public static LlamaCppConfig Default()
    {
        return new LlamaCppConfig(
            ModelPath: null,
            ContextSize: 4096,
            GpuLayers: 999,
            ParallelSlots: 1);
    }
}
