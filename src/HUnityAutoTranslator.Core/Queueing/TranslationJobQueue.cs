using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Queueing;

public sealed class TranslationJobQueue
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TranslationJob> _pendingBySource = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inFlightSources = new(StringComparer.Ordinal);
    private long _sequence;
    private readonly Dictionary<string, long> _sequences = new(StringComparer.Ordinal);

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pendingBySource.Count;
            }
        }
    }

    public void Enqueue(TranslationJob job)
    {
        var key = TextNormalizer.NormalizeForCache(job.SourceText);
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        lock (_gate)
        {
            if (_inFlightSources.Contains(key))
            {
                return;
            }

            if (_pendingBySource.TryGetValue(key, out var existing))
            {
                if (job.Priority > existing.Priority)
                {
                    _pendingBySource[key] = job;
                }

                return;
            }

            _pendingBySource[key] = job;
            _sequences[key] = _sequence++;
        }
    }

    public bool TryDequeueBatch(int maxItems, int maxCharacters, out IReadOnlyList<TranslationJob> batch)
    {
        lock (_gate)
        {
            var selected = new List<(string Key, TranslationJob Job)>();
            var characters = 0;

            foreach (var item in _pendingBySource
                .OrderByDescending(item => item.Value.Priority)
                .ThenBy(item => _sequences[item.Key]))
            {
                if (selected.Count >= maxItems)
                {
                    break;
                }

                var nextCharacters = characters + item.Value.SourceText.Length;
                if (selected.Count > 0 && nextCharacters > maxCharacters)
                {
                    break;
                }

                selected.Add((item.Key, item.Value));
                characters = nextCharacters;
            }

            foreach (var item in selected)
            {
                _pendingBySource.Remove(item.Key);
                _sequences.Remove(item.Key);
                _inFlightSources.Add(item.Key);
            }

            batch = selected.Select(item => item.Job).ToArray();
            return batch.Count > 0;
        }
    }

    public void MarkCompleted(IEnumerable<TranslationJob> jobs)
    {
        lock (_gate)
        {
            foreach (var job in jobs)
            {
                _inFlightSources.Remove(TextNormalizer.NormalizeForCache(job.SourceText));
            }
        }
    }
}
