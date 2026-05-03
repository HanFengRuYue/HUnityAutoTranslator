using HUnityAutoTranslator.Core.Prompts;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Queueing;

public sealed class TranslationJobQueue
{
    private const int MaxRelatedBatchItems = 16;

    private readonly object _gate = new();
    private readonly Dictionary<string, TranslationJob> _pendingBySource = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TranslationJob> _deferredBySource = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inFlightSources = new(StringComparer.Ordinal);
    private long _sequence;
    private readonly Dictionary<string, long> _sequences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _deferredSequences = new(StringComparer.Ordinal);

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

    public int DeferredCount
    {
        get
        {
            lock (_gate)
            {
                return _deferredBySource.Count;
            }
        }
    }

    public int InFlightCount
    {
        get
        {
            lock (_gate)
            {
                return _inFlightSources.Count;
            }
        }
    }

    public bool Enqueue(TranslationJob job)
    {
        var key = CreateQueueKey(job);
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        lock (_gate)
        {
            if (_inFlightSources.Contains(key))
            {
                return false;
            }

            if (_deferredBySource.TryGetValue(key, out var deferred))
            {
                if (job.Priority > deferred.Priority)
                {
                    _deferredBySource[key] = job;
                }

                return false;
            }

            if (_pendingBySource.TryGetValue(key, out var existing))
            {
                if (job.Priority > existing.Priority)
                {
                    _pendingBySource[key] = job;
                }

                return false;
            }

            _pendingBySource[key] = job;
            _sequences[key] = _sequence++;
            return true;
        }
    }

    public bool EnqueueDeferred(TranslationJob job)
    {
        var key = CreateQueueKey(job);
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        lock (_gate)
        {
            if (_pendingBySource.ContainsKey(key))
            {
                return false;
            }

            if (_deferredBySource.TryGetValue(key, out var existing))
            {
                if (job.Priority > existing.Priority)
                {
                    _deferredBySource[key] = job;
                }

                return false;
            }

            _deferredBySource[key] = job;
            _deferredSequences[key] = _sequence++;
            return true;
        }
    }

    public int PromoteDeferred()
    {
        lock (_gate)
        {
            var promoted = 0;
            var removeDeferred = new List<string>();

            foreach (var item in _deferredBySource
                .OrderBy(item => _deferredSequences[item.Key]))
            {
                if (_inFlightSources.Contains(item.Key))
                {
                    continue;
                }

                if (!_pendingBySource.ContainsKey(item.Key))
                {
                    _pendingBySource[item.Key] = item.Value;
                    _sequences[item.Key] = _sequence++;
                    promoted++;
                }

                removeDeferred.Add(item.Key);
            }

            foreach (var key in removeDeferred)
            {
                _deferredBySource.Remove(key);
                _deferredSequences.Remove(key);
            }

            return promoted;
        }
    }

    public bool TryDequeueBatch(int maxItems, int maxCharacters, out IReadOnlyList<TranslationJob> batch)
    {
        lock (_gate)
        {
            var selected = new List<(string Key, TranslationJob Job)>();
            var characters = 0;
            string? relatedGroupKey = null;
            var relatedBatchExpansion = false;
            var effectiveMaxItems = Math.Max(1, maxItems);

            foreach (var item in _pendingBySource
                .OrderByDescending(item => item.Value.Priority)
                .ThenBy(item => _sequences[item.Key]))
            {
                if (selected.Count == 0)
                {
                    relatedGroupKey = CreateRelatedBatchGroupKey(item.Value);
                    relatedBatchExpansion = !string.IsNullOrEmpty(relatedGroupKey);
                    if (relatedBatchExpansion)
                    {
                        effectiveMaxItems = Math.Max(effectiveMaxItems, MaxRelatedBatchItems);
                    }
                }
                else if (relatedBatchExpansion &&
                    !string.Equals(relatedGroupKey, CreateRelatedBatchGroupKey(item.Value), StringComparison.Ordinal))
                {
                    continue;
                }

                if (selected.Count >= effectiveMaxItems)
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
                _inFlightSources.Remove(CreateQueueKey(job));
            }
        }
    }

    private static string CreateQueueKey(TranslationJob job)
    {
        var source = TextNormalizer.NormalizeForCache(job.SourceText);
        if (source.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "\u001f",
            source,
            job.Context.SceneName ?? string.Empty,
            job.Context.ComponentHierarchy ?? string.Empty);
    }

    private static string CreateRelatedBatchGroupKey(TranslationJob job)
    {
        var settingGroup = PromptItemClassifier.GetSettingGroupHierarchy(job.Context.ComponentHierarchy);
        if (string.IsNullOrWhiteSpace(settingGroup))
        {
            return string.Empty;
        }

        if (!PromptItemClassifier.IsToggleStateText(job.SourceText))
        {
            return string.Empty;
        }

        var source = TextNormalizer.NormalizeForCache(job.SourceText);
        if (source.Length == 0 || source.Length > 32)
        {
            return string.Empty;
        }

        return string.Join(
            "\u001f",
            job.Context.SceneName ?? string.Empty,
            settingGroup);
    }
}
