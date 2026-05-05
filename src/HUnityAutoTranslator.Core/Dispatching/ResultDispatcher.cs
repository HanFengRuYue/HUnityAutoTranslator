namespace HUnityAutoTranslator.Core.Dispatching;

public sealed class ResultDispatcher
{
    private const int DefaultMaxPendingResults = 4096;

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
            var selected = _pending
                .OrderByDescending(item => item.Result.Priority)
                .ThenBy(item => item.Sequence)
                .Take(Math.Max(0, maxCount))
                .ToArray();

            foreach (var item in selected)
            {
                _pending.Remove(item);
            }

            return selected.Select(item => item.Result).ToArray();
        }
    }

    private void TrimPending()
    {
        while (_pending.Count > _maxPendingResults)
        {
            var lowest = _pending
                .OrderBy(item => item.Result.Priority)
                .ThenBy(item => item.Sequence)
                .First();
            _pending.Remove(lowest);
        }
    }
}
