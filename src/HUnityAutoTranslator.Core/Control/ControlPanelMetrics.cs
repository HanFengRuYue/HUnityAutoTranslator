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
                CapturedKeyTrackerCount: _capturedKeys.Count);
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
    int CapturedKeyTrackerCount = 0);

public sealed record ProviderActivityPreview(
    string Id,
    string Name,
    ProviderKind Kind,
    string Model,
    DateTimeOffset StartedUtc);
