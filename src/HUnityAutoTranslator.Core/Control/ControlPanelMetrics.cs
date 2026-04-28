using HUnityAutoTranslator.Core.Caching;

namespace HUnityAutoTranslator.Core.Control;

public sealed class ControlPanelMetrics
{
    private readonly object _gate = new();
    private readonly Queue<RecentTranslationPreview> _recentTranslations = new();
    private readonly HashSet<TranslationCacheKey> _capturedKeys = new();
    private long _capturedTextCount;
    private long _queuedTextCount;
    private long _inFlightTranslationCount;
    private long _completedTranslationCount;
    private long _totalTokenCount;
    private long _timedTranslationCount;
    private long _totalTranslationMilliseconds;
    private long _translatedCharacterCount;

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
    }

    public ControlPanelMetricsSnapshot Snapshot()
    {
        lock (_gate)
        {
            var timedTranslationCount = Interlocked.Read(ref _timedTranslationCount);
            var totalTranslationMilliseconds = Interlocked.Read(ref _totalTranslationMilliseconds);
            var translatedCharacterCount = Interlocked.Read(ref _translatedCharacterCount);
            return new ControlPanelMetricsSnapshot(
                CapturedTextCount: Interlocked.Read(ref _capturedTextCount),
                QueuedTextCount: Interlocked.Read(ref _queuedTextCount),
                InFlightTranslationCount: Interlocked.Read(ref _inFlightTranslationCount),
                CompletedTranslationCount: Interlocked.Read(ref _completedTranslationCount),
                TotalTokenCount: Interlocked.Read(ref _totalTokenCount),
                AverageTranslationMilliseconds: timedTranslationCount == 0 ? 0 : (double)totalTranslationMilliseconds / timedTranslationCount,
                AverageCharactersPerSecond: totalTranslationMilliseconds == 0 ? 0 : translatedCharacterCount / (totalTranslationMilliseconds / 1000.0),
                RecentTranslations: _recentTranslations.Reverse().ToArray());
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
    IReadOnlyList<RecentTranslationPreview> RecentTranslations);
