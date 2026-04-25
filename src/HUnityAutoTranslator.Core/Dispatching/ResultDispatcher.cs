namespace HUnityAutoTranslator.Core.Dispatching;

public sealed class ResultDispatcher
{
    private readonly object _gate = new();
    private readonly List<(TranslationResult Result, long Sequence)> _pending = new();
    private long _sequence;

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
            _pending.Add((result, _sequence++));
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
}
