using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Control;

public sealed class ControlPanelMetrics
{
    public const int MaxTrackedCapturedKeys = 4096;

    private readonly object _gate = new();
    private readonly Queue<RecentTranslationPreview> _recentTranslations = new();
    private readonly HashSet<TranslationCacheKey> _capturedKeys = new();
    private readonly Queue<TranslationCacheKey> _capturedKeyOrder = new();
    private long _capturedTextCount;
    private long _queuedTextCount;
    private long _inFlightTranslationCount;
    private long _completedTranslationCount;
    private long _totalTokenCount;
    private long _timedTranslationCount;
    private long _totalTranslationMilliseconds;
    private long _translatedCharacterCount;
    private long _textChangeHookEventCount;
    private long _textChangeHookQueuedCount;
    private long _textChangeHookMergedCount;
    private long _textChangeHookDroppedCount;
    private long _textChangeRawPrefilteredCount;
    private long _textChangeQueueProcessedCount;
    private long _textChangeQueueMilliseconds;
    private long _textTargetMetadataBuildCount;
    private long _cacheLookupCount;
    private long _globalTextScanRequestCount;
    private long _globalTextScanCount;
    private long _globalTextScanTargetCount;
    private long _globalTextScanMilliseconds;
    private long _rememberedReapplyCheckCount;
    private long _rememberedReapplyAppliedCount;
    private long _fontApplicationCount;
    private long _fontApplicationSkippedCount;
    private long _layoutApplicationCount;
    private long _layoutApplicationSkippedCount;
    private long _tmpMeshForceUpdateCount;
    private ProviderActivityPreview? _activeTranslationProvider;

    public void RecordCaptured()
    {
        Interlocked.Increment(ref _capturedTextCount);
    }

    public void RecordCaptured(TranslationCacheKey key)
    {
        lock (_gate)
        {
            if (_capturedKeys.Add(key))
            {
                _capturedKeyOrder.Enqueue(key);
                TrimCapturedKeyTracker();
                Interlocked.Increment(ref _capturedTextCount);
            }
        }
    }

    public void RecordQueued()
    {
        Interlocked.Increment(ref _queuedTextCount);
    }

    public void RecordTextChangeHookEvent()
    {
        Interlocked.Increment(ref _textChangeHookEventCount);
    }

    public void RecordTextChangeHookQueued()
    {
        Interlocked.Increment(ref _textChangeHookQueuedCount);
    }

    public void RecordTextChangeHookMerged()
    {
        Interlocked.Increment(ref _textChangeHookMergedCount);
    }

    public void RecordTextChangeHookDropped()
    {
        Interlocked.Increment(ref _textChangeHookDroppedCount);
    }

    public void RecordTextChangeRawPrefiltered()
    {
        Interlocked.Increment(ref _textChangeRawPrefilteredCount);
    }

    public void RecordTextChangeQueueDrain(TimeSpan elapsed, int processedItems)
    {
        Interlocked.Add(ref _textChangeQueueProcessedCount, Math.Max(0, processedItems));
        Interlocked.Add(ref _textChangeQueueMilliseconds, Math.Max(0, (long)Math.Round(elapsed.TotalMilliseconds)));
    }

    public void RecordTextTargetMetadataBuild()
    {
        Interlocked.Increment(ref _textTargetMetadataBuildCount);
    }

    public void RecordCacheLookup()
    {
        Interlocked.Increment(ref _cacheLookupCount);
    }

    public void RecordGlobalTextScanRequest()
    {
        Interlocked.Increment(ref _globalTextScanRequestCount);
    }

    public void RecordGlobalTextScan(TimeSpan elapsed, int processedTargets)
    {
        Interlocked.Increment(ref _globalTextScanCount);
        Interlocked.Add(ref _globalTextScanTargetCount, Math.Max(0, processedTargets));
        Interlocked.Add(ref _globalTextScanMilliseconds, Math.Max(0, (long)Math.Round(elapsed.TotalMilliseconds)));
    }

    public void RecordRememberedReapply(int checkedCount, int appliedCount)
    {
        Interlocked.Add(ref _rememberedReapplyCheckCount, Math.Max(0, checkedCount));
        Interlocked.Add(ref _rememberedReapplyAppliedCount, Math.Max(0, appliedCount));
    }

    public void RecordFontApplication()
    {
        Interlocked.Increment(ref _fontApplicationCount);
    }

    public void RecordFontApplicationSkipped()
    {
        Interlocked.Increment(ref _fontApplicationSkippedCount);
    }

    public void RecordLayoutApplication()
    {
        Interlocked.Increment(ref _layoutApplicationCount);
    }

    public void RecordLayoutApplicationSkipped()
    {
        Interlocked.Increment(ref _layoutApplicationSkippedCount);
    }

    public void RecordTmpMeshForceUpdate()
    {
        Interlocked.Increment(ref _tmpMeshForceUpdateCount);
    }

    public void RecordTranslationStarted()
    {
        Interlocked.Increment(ref _inFlightTranslationCount);
    }

    public void RecordProviderAttempt(ProviderRuntimeProfile profile)
    {
        lock (_gate)
        {
            _activeTranslationProvider = new ProviderActivityPreview(
                profile.Id,
                profile.Name,
                profile.Profile.Kind,
                profile.Profile.Model,
                DateTimeOffset.UtcNow);
        }
    }

    public void RecordTranslationCompleted(RecentTranslationPreview preview, int totalTokens = 0, TimeSpan? elapsed = null)
    {
        Interlocked.Increment(ref _completedTranslationCount);
        if (totalTokens > 0)
        {
            Interlocked.Add(ref _totalTokenCount, totalTokens);
        }

        if (elapsed.HasValue && elapsed.Value > TimeSpan.Zero)
        {
            Interlocked.Increment(ref _timedTranslationCount);
            Interlocked.Add(ref _totalTranslationMilliseconds, Math.Max(1, (long)Math.Round(elapsed.Value.TotalMilliseconds)));
            Interlocked.Add(ref _translatedCharacterCount, preview.SourceText.Length);
        }

        lock (_gate)
        {
            _recentTranslations.Enqueue(preview);
            while (_recentTranslations.Count > 12)
            {
                _recentTranslations.Dequeue();
            }
        }
    }

    public void RecordTranslationFinishedWithoutResult()
    {
        RecordTranslationRequestFinished();
    }

    public void RecordTranslationRequestFinished()
    {
        DecrementInFlight();
        if (Interlocked.Read(ref _inFlightTranslationCount) == 0)
        {
            lock (_gate)
            {
                _activeTranslationProvider = null;
            }
        }
    }

    public ControlPanelMetricsSnapshot Snapshot()
    {
        lock (_gate)
        {
            var timedTranslationCount = Interlocked.Read(ref _timedTranslationCount);
            var totalTranslationMilliseconds = Interlocked.Read(ref _totalTranslationMilliseconds);
            var translatedCharacterCount = Interlocked.Read(ref _translatedCharacterCount);
            var inFlightTranslationCount = Interlocked.Read(ref _inFlightTranslationCount);
            return new ControlPanelMetricsSnapshot(
                CapturedTextCount: Interlocked.Read(ref _capturedTextCount),
                QueuedTextCount: Interlocked.Read(ref _queuedTextCount),
                InFlightTranslationCount: inFlightTranslationCount,
                CompletedTranslationCount: Interlocked.Read(ref _completedTranslationCount),
                TotalTokenCount: Interlocked.Read(ref _totalTokenCount),
                AverageTranslationMilliseconds: timedTranslationCount == 0 ? 0 : (double)totalTranslationMilliseconds / timedTranslationCount,
                AverageCharactersPerSecond: totalTranslationMilliseconds == 0 ? 0 : translatedCharacterCount / (totalTranslationMilliseconds / 1000.0),
                RecentTranslations: _recentTranslations.Reverse().ToArray(),
                ActiveTranslationProvider: inFlightTranslationCount > 0 ? _activeTranslationProvider : null,
                CapturedKeyTrackerCount: _capturedKeys.Count,
                TextChangeHookEventCount: Interlocked.Read(ref _textChangeHookEventCount),
                TextChangeHookQueuedCount: Interlocked.Read(ref _textChangeHookQueuedCount),
                TextChangeHookMergedCount: Interlocked.Read(ref _textChangeHookMergedCount),
                TextChangeHookDroppedCount: Interlocked.Read(ref _textChangeHookDroppedCount),
                TextChangeRawPrefilteredCount: Interlocked.Read(ref _textChangeRawPrefilteredCount),
                TextChangeQueueProcessedCount: Interlocked.Read(ref _textChangeQueueProcessedCount),
                TextChangeQueueMilliseconds: Interlocked.Read(ref _textChangeQueueMilliseconds),
                TextTargetMetadataBuildCount: Interlocked.Read(ref _textTargetMetadataBuildCount),
                CacheLookupCount: Interlocked.Read(ref _cacheLookupCount),
                GlobalTextScanRequestCount: Interlocked.Read(ref _globalTextScanRequestCount),
                GlobalTextScanCount: Interlocked.Read(ref _globalTextScanCount),
                GlobalTextScanTargetCount: Interlocked.Read(ref _globalTextScanTargetCount),
                GlobalTextScanMilliseconds: Interlocked.Read(ref _globalTextScanMilliseconds),
                RememberedReapplyCheckCount: Interlocked.Read(ref _rememberedReapplyCheckCount),
                RememberedReapplyAppliedCount: Interlocked.Read(ref _rememberedReapplyAppliedCount),
                FontApplicationCount: Interlocked.Read(ref _fontApplicationCount),
                FontApplicationSkippedCount: Interlocked.Read(ref _fontApplicationSkippedCount),
                LayoutApplicationCount: Interlocked.Read(ref _layoutApplicationCount),
                LayoutApplicationSkippedCount: Interlocked.Read(ref _layoutApplicationSkippedCount),
                TmpMeshForceUpdateCount: Interlocked.Read(ref _tmpMeshForceUpdateCount));
        }
    }

    private void TrimCapturedKeyTracker()
    {
        while (_capturedKeys.Count > MaxTrackedCapturedKeys && _capturedKeyOrder.Count > 0)
        {
            _capturedKeys.Remove(_capturedKeyOrder.Dequeue());
        }
    }

    private void DecrementInFlight()
    {
        var current = Interlocked.Decrement(ref _inFlightTranslationCount);
        if (current < 0)
        {
            Interlocked.Exchange(ref _inFlightTranslationCount, 0);
        }
    }
}

public sealed record ControlPanelMetricsSnapshot(
    long CapturedTextCount,
    long QueuedTextCount,
    long InFlightTranslationCount,
    long CompletedTranslationCount,
    long TotalTokenCount,
    double AverageTranslationMilliseconds,
    double AverageCharactersPerSecond,
    IReadOnlyList<RecentTranslationPreview> RecentTranslations,
    ProviderActivityPreview? ActiveTranslationProvider = null,
    int CapturedKeyTrackerCount = 0,
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
    long TmpMeshForceUpdateCount = 0);

public sealed record ProviderActivityPreview(
    string Id,
    string Name,
    ProviderKind Kind,
    string Model,
    DateTimeOffset StartedUtc);
