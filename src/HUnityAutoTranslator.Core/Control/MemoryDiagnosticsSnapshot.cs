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
    long TexturePngBytes,
    long TextChangeHookEventCount = 0,
    long TextChangeHookQueuedCount = 0,
    long TextChangeHookMergedCount = 0,
    long TextChangeHookDroppedCount = 0,
    long TextChangeRawPrefilteredCount = 0,
    long TextChangeQueueProcessedCount = 0,
    long TextChangeQueueMilliseconds = 0,
    long TextTargetMetadataBuildCount = 0,
    long CacheLookupCount = 0,
    long GlobalTextScanRequestCount = 0,
    long GlobalTextScanCount = 0,
    long GlobalTextScanTargetCount = 0,
    long GlobalTextScanMilliseconds = 0,
    long RememberedReapplyCheckCount = 0,
    long RememberedReapplyAppliedCount = 0,
    long FontApplicationCount = 0,
    long FontApplicationSkippedCount = 0,
    long LayoutApplicationCount = 0,
    long LayoutApplicationSkippedCount = 0,
    long TmpMeshForceUpdateCount = 0)
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
