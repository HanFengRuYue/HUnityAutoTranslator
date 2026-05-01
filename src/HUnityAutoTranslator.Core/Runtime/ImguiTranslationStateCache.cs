namespace HUnityAutoTranslator.Core.Runtime;

internal sealed class ImguiTranslationStateCache
{
    private readonly Dictionary<StateKey, StateEntry> _entries = new();
    private readonly int _maxEntries;
    private readonly int _maxNewItemsPerFrame;
    private readonly int _maxRefreshesPerFrame;
    private readonly double _pendingRefreshSeconds;
    private readonly double _entryTtlSeconds;
    private readonly double _minNewItemIntervalSeconds;
    private readonly double _minRefreshIntervalSeconds;
    private int _currentFrameId = int.MinValue;
    private int _newItemsThisFrame;
    private int _refreshesThisFrame;
    private long _sequence;
    private double _nextPruneSeconds;
    private double _nextNewItemSeconds;
    private double _nextRefreshSeconds;

    public ImguiTranslationStateCache(
        int maxEntries = 2048,
        int maxNewItemsPerFrame = 8,
        int maxRefreshesPerFrame = 4,
        double pendingRefreshSeconds = 1,
        double entryTtlSeconds = 300,
        double minNewItemIntervalSeconds = 0,
        double minRefreshIntervalSeconds = 0)
    {
        _maxEntries = Math.Max(16, maxEntries);
        _maxNewItemsPerFrame = Math.Max(1, maxNewItemsPerFrame);
        _maxRefreshesPerFrame = Math.Max(0, maxRefreshesPerFrame);
        _pendingRefreshSeconds = Math.Max(0.1, pendingRefreshSeconds);
        _entryTtlSeconds = Math.Max(5, entryTtlSeconds);
        _minNewItemIntervalSeconds = Math.Max(0, minNewItemIntervalSeconds);
        _minRefreshIntervalSeconds = Math.Max(0, minRefreshIntervalSeconds);
    }

    public ImguiTranslationStateResult Resolve(
        string sourceText,
        string targetLanguage,
        string promptPolicyVersion,
        string? sceneName,
        double nowSeconds,
        int frameId,
        Func<string?> tryGetCachedTranslation,
        Func<string?> processSourceText)
    {
        ResetFrame(frameId);

        var key = new StateKey(sourceText, targetLanguage, promptPolicyVersion, sceneName ?? string.Empty);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new StateEntry();
            _entries[key] = entry;
        }

        entry.LastSeenSeconds = nowSeconds;
        entry.LastSeenSequence = ++_sequence;
        TrimIfNeeded(nowSeconds);

        if (entry.TranslatedText != null)
        {
            return ImguiTranslationStateResult.Translated(entry.TranslatedText);
        }

        if (!entry.HasProcessedSource)
        {
            return ResolveNewSource(entry, sourceText, nowSeconds, tryGetCachedTranslation, processSourceText);
        }

        if (ShouldRefreshPending(entry, nowSeconds) && TrySpendRefreshBudget(nowSeconds))
        {
            entry.LastCacheLookupSeconds = nowSeconds;
            var translated = tryGetCachedTranslation();
            if (translated != null)
            {
                entry.TranslatedText = translated;
                return ImguiTranslationStateResult.Translated(translated);
            }
        }

        return ImguiTranslationStateResult.Pending(sourceText);
    }

    private ImguiTranslationStateResult ResolveNewSource(
        StateEntry entry,
        string sourceText,
        double nowSeconds,
        Func<string?> tryGetCachedTranslation,
        Func<string?> processSourceText)
    {
        if (!TrySpendNewItemBudget(nowSeconds))
        {
            return ImguiTranslationStateResult.Pending(sourceText);
        }

        entry.HasProcessedSource = true;
        entry.LastCacheLookupSeconds = nowSeconds;
        var cached = tryGetCachedTranslation();
        if (cached != null)
        {
            entry.TranslatedText = cached;
            return ImguiTranslationStateResult.Translated(cached);
        }

        var processed = processSourceText();
        if (processed != null)
        {
            entry.TranslatedText = processed;
            return ImguiTranslationStateResult.Translated(processed);
        }

        return ImguiTranslationStateResult.Pending(sourceText);
    }

    private bool ShouldRefreshPending(StateEntry entry, double nowSeconds)
    {
        return nowSeconds - entry.LastCacheLookupSeconds >= _pendingRefreshSeconds;
    }

    private bool TrySpendNewItemBudget(double nowSeconds)
    {
        if (_newItemsThisFrame >= _maxNewItemsPerFrame || nowSeconds < _nextNewItemSeconds)
        {
            return false;
        }

        _newItemsThisFrame++;
        _nextNewItemSeconds = nowSeconds + _minNewItemIntervalSeconds;
        return true;
    }

    private bool TrySpendRefreshBudget(double nowSeconds)
    {
        if (_refreshesThisFrame >= _maxRefreshesPerFrame || nowSeconds < _nextRefreshSeconds)
        {
            return false;
        }

        _refreshesThisFrame++;
        _nextRefreshSeconds = nowSeconds + _minRefreshIntervalSeconds;
        return true;
    }

    private void ResetFrame(int frameId)
    {
        if (frameId == _currentFrameId)
        {
            return;
        }

        _currentFrameId = frameId;
        _newItemsThisFrame = 0;
        _refreshesThisFrame = 0;
    }

    private void TrimIfNeeded(double nowSeconds)
    {
        if (_entries.Count <= _maxEntries && nowSeconds < _nextPruneSeconds)
        {
            return;
        }

        _nextPruneSeconds = nowSeconds + Math.Min(30, _entryTtlSeconds);
        var staleKeys = new List<StateKey>();
        foreach (var item in _entries)
        {
            if (nowSeconds - item.Value.LastSeenSeconds > _entryTtlSeconds)
            {
                staleKeys.Add(item.Key);
            }
        }

        foreach (var key in staleKeys)
        {
            _entries.Remove(key);
        }

        while (_entries.Count > _maxEntries)
        {
            var oldestKey = default(StateKey);
            var oldestSequence = long.MaxValue;
            var found = false;
            foreach (var item in _entries)
            {
                if (item.Value.LastSeenSequence >= oldestSequence)
                {
                    continue;
                }

                oldestKey = item.Key;
                oldestSequence = item.Value.LastSeenSequence;
                found = true;
            }

            if (!found)
            {
                return;
            }

            _entries.Remove(oldestKey);
        }
    }

    private readonly struct StateKey : IEquatable<StateKey>
    {
        private readonly string _sourceText;
        private readonly string _targetLanguage;
        private readonly string _promptPolicyVersion;
        private readonly string _sceneName;

        public StateKey(string sourceText, string targetLanguage, string promptPolicyVersion, string sceneName)
        {
            _sourceText = sourceText;
            _targetLanguage = targetLanguage;
            _promptPolicyVersion = promptPolicyVersion;
            _sceneName = sceneName;
        }

        public bool Equals(StateKey other)
        {
            return string.Equals(_sourceText, other._sourceText, StringComparison.Ordinal) &&
                string.Equals(_targetLanguage, other._targetLanguage, StringComparison.Ordinal) &&
                string.Equals(_promptPolicyVersion, other._promptPolicyVersion, StringComparison.Ordinal) &&
                string.Equals(_sceneName, other._sceneName, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is StateKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(_sourceText);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(_targetLanguage);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(_promptPolicyVersion);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(_sceneName);
                return hash;
            }
        }
    }

    private sealed class StateEntry
    {
        public bool HasProcessedSource { get; set; }

        public string? TranslatedText { get; set; }

        public double LastCacheLookupSeconds { get; set; } = double.NegativeInfinity;

        public double LastSeenSeconds { get; set; }

        public long LastSeenSequence { get; set; }
    }
}

internal sealed record ImguiTranslationStateResult(string DisplayText, bool IsTranslated)
{
    public static ImguiTranslationStateResult Pending(string sourceText) => new(sourceText, false);

    public static ImguiTranslationStateResult Translated(string translatedText) => new(translatedText, true);
}
