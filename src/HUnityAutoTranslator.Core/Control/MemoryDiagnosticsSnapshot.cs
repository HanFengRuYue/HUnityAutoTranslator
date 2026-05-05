namespace HUnityAutoTranslator.Core.Control;

public sealed record MemoryDiagnosticsSnapshot(
    long ManagedMemoryBytes,
    long UnityAllocatedMemoryBytes,
    long UnityReservedMemoryBytes,
    long UnityMonoHeapBytes,
    int QueueCount,
    int WritebackQueueCount,
    int CapturedKeyTrackerCount,
    int RegisteredTextTargetCount,
    int FontCacheCount,
    int TmpFontAssetCacheCount,
    int ImguiFontResolutionCacheCount,
    int TextureRecordCount,
    int ReplacementTextureCount,
    long TexturePngBytes)
{
    public static MemoryDiagnosticsSnapshot Empty { get; } = new(
        ManagedMemoryBytes: 0,
        UnityAllocatedMemoryBytes: 0,
        UnityReservedMemoryBytes: 0,
        UnityMonoHeapBytes: 0,
        QueueCount: 0,
        WritebackQueueCount: 0,
        CapturedKeyTrackerCount: 0,
        RegisteredTextTargetCount: 0,
        FontCacheCount: 0,
        TmpFontAssetCacheCount: 0,
        ImguiFontResolutionCacheCount: 0,
        TextureRecordCount: 0,
        ReplacementTextureCount: 0,
        TexturePngBytes: 0);

    public MemoryDiagnosticsSnapshot WithRuntimeCounts(
        int queueCount,
        int writebackQueueCount,
        int capturedKeyTrackerCount)
    {
        return this with
        {
            QueueCount = Math.Max(0, queueCount),
            WritebackQueueCount = Math.Max(0, writebackQueueCount),
            CapturedKeyTrackerCount = Math.Max(0, capturedKeyTrackerCount)
        };
    }
}
