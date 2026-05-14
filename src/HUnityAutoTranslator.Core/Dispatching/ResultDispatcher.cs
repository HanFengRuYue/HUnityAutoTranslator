namespace HUnityAutoTranslator.Core.Dispatching;

public sealed class ResultDispatcher
{
    private const int DefaultMaxPendingResults = 4096;

    private static readonly Comparison<(TranslationResult Result, long Sequence)> DrainOrder = CompareForDrain;
    private static readonly Comparison<(TranslationResult Result, long Sequence)> EvictionOrder = CompareForEviction;

    private readonly object _gate = new();
    private readonly List<(TranslationResult Result, long Sequence)> _pending = new();
    private readonly int _maxPendingResults;
    private long _sequence;

    public ResultDispatcher(int maxPendingResults = DefaultMaxPendingResults)
    {
        _maxPendingResults = Math.Max(1, maxPendingResults);
    }

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public void Publish(TranslationResult result)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(result.TargetId))
            {
                _pending.RemoveAll(item => string.Equals(item.Result.TargetId, result.TargetId, StringComparison.Ordinal));
            }

            _pending.Add((result, _sequence++));
            TrimPending();
        }
    }

    public IReadOnlyList<TranslationResult> Drain(int maxCount)
    {
        lock (_gate)
        {
            if (maxCount <= 0 || _pending.Count == 0)
            {
                return Array.Empty<TranslationResult>();
            }

            _pending.Sort(DrainOrder);
            var take = Math.Min(maxCount, _pending.Count);
            var drained = new TranslationResult[take];
            for (var i = 0; i < take; i++)
            {
                drained[i] = _pending[i].Result;
            }

            if (take == _pending.Count)
            {
                _pending.Clear();
            }
            else
            {
                _pending.RemoveRange(0, take);
            }

            return drained;
        }
    }

    private void TrimPending()
    {
        var excess = _pending.Count - _maxPendingResults;
        if (excess <= 0)
        {
            return;
        }

        _pending.Sort(EvictionOrder);
        _pending.RemoveRange(0, excess);
    }

    private static int CompareForDrain(
        (TranslationResult Result, long Sequence) left,
        (TranslationResult Result, long Sequence) right)
    {
        var byPriority = right.Result.Priority.CompareTo(left.Result.Priority);
        return byPriority != 0 ? byPriority : left.Sequence.CompareTo(right.Sequence);
    }

    private static int CompareForEviction(
        (TranslationResult Result, long Sequence) left,
        (TranslationResult Result, long Sequence) right)
    {
        var byPriority = left.Result.Priority.CompareTo(right.Result.Priority);
        return byPriority != 0 ? byPriority : left.Sequence.CompareTo(right.Sequence);
    }
}
