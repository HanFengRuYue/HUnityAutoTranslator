namespace HUnityAutoTranslator.Core.Runtime;

internal sealed class ImguiTranslationStateCache
{
    private readonly Dictionary<StateKey, StateEntry> _entries = new();
    private readonly int _maxEntries;
    private readonly double _pendingRefreshSeconds;
    private readonly double _entryTtlSeconds;
    private long _sequence;
    private double _nextPruneSeconds;

    public ImguiTranslationStateCache(
        int maxEntries = 2048,
        double pendingRefreshSeconds = 1,
        double entryTtlSeconds = 300)
    {
        _maxEntries = Math.Max(16, maxEntries);
        _pendingRefreshSeconds = Math.Max(0.1, pendingRefreshSeconds);
        _entryTtlSeconds = Math.Max(5, entryTtlSeconds);
    }

    public ImguiTranslationStateResult ResolveForDraw(
        string sourceText,
        string targetLanguage,
        string promptPolicyVersion,
        string? sceneName,
        double nowSeconds,
        int frameId)
    {
        var key = StateKey.Create(sourceText, targetLanguage, promptPolicyVersion, sceneName);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new StateEntry(++_sequence);
            _entries[key] = entry;
        }

        entry.LastSeenSeconds = nowSeconds;
        entry.LastSeenFrameId = frameId;
        entry.LastSeenSequence = ++_sequence;
        TrimIfNeeded(nowSeconds);

        return entry.TranslatedText != null
            ? ImguiTranslationStateResult.Translated(entry.TranslatedText)
            : ImguiTranslationStateResult.Pending(sourceText);
    }

    public IReadOnlyList<ImguiPendingText> TakePendingBatch(int maxCount, double nowSeconds)
    {
        if (maxCount <= 0)
        {
            return Array.Empty<ImguiPendingText>();
        }

        TrimIfNeeded(nowSeconds);
        var batch = new List<ImguiPendingText>(Math.Min(maxCount, _entries.Count));
        foreach (var item in _entries
            .OrderBy(item => item.Value.FirstSeenSequence)
            .ThenBy(item => item.Value.LastSeenSequence))
        {
            if (batch.Count >= maxCount)
            {
                break;
            }

            var entry = item.Value;
            if (entry.InBatch ||
                entry.Ignored ||
                entry.TranslatedText != null ||
                !ShouldProcess(entry, nowSeconds))
            {
                continue;
            }

            entry.InBatch = true;
            entry.LastBatchSeconds = nowSeconds;
            batch.Add(new ImguiPendingText(
                item.Key.SourceText,
                item.Key.TargetLanguage,
                item.Key.PromptPolicyVersion,
                item.Key.SceneName,
                ShouldProcessSource: !entry.HasProcessedSource));
        }

        return batch;
    }

    public void MarkCached(ImguiPendingText pendingText, string translatedText, double nowSeconds)
    {
        if (!TryGetEntry(pendingText, out var entry))
        {
            return;
        }

        entry.TranslatedText = translatedText;
        entry.HasProcessedSource = true;
        entry.LastCacheLookupSeconds = nowSeconds;
        entry.InBatch = false;
        entry.Ignored = false;
    }

    public void MarkQueued(ImguiPendingText pendingText, double nowSeconds)
    {
        if (!TryGetEntry(pendingText, out var entry))
        {
            return;
        }

        entry.HasProcessedSource = true;
        entry.LastCacheLookupSeconds = nowSeconds;
        entry.InBatch = false;
    }

    public void MarkCacheMiss(ImguiPendingText pendingText, double nowSeconds)
    {
        if (!TryGetEntry(pendingText, out var entry))
        {
            return;
        }

        entry.LastCacheLookupSeconds = nowSeconds;
        entry.InBatch = false;
    }

    public void MarkIgnored(ImguiPendingText pendingText, double nowSeconds)
    {
        if (!TryGetEntry(pendingText, out var entry))
        {
            return;
        }

        entry.Ignored = true;
        entry.HasProcessedSource = true;
        entry.LastCacheLookupSeconds = nowSeconds;
        entry.InBatch = false;
    }

    private bool TryGetEntry(ImguiPendingText pendingText, out StateEntry entry)
    {
        return _entries.TryGetValue(
            StateKey.Create(
                pendingText.SourceText,
                pendingText.TargetLanguage,
                pendingText.PromptPolicyVersion,
                pendingText.SceneName),
            out entry!);
    }

    private bool ShouldProcess(StateEntry entry, double nowSeconds)
    {
        if (!entry.HasProcessedSource)
        {
            return true;
        }

        return nowSeconds - entry.LastCacheLookupSeconds >= _pendingRefreshSeconds;
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
            if (!item.Value.InBatch && nowSeconds - item.Value.LastSeenSeconds > _entryTtlSeconds)
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
                if (item.Value.InBatch || item.Value.LastSeenSequence >= oldestSequence)
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
        public StateKey(string sourceText, string targetLanguage, string promptPolicyVersion, string sceneName)
        {
            SourceText = sourceText;
            TargetLanguage = targetLanguage;
            PromptPolicyVersion = promptPolicyVersion;
            SceneName = sceneName;
        }

        public string SourceText { get; }

        public string TargetLanguage { get; }

        public string PromptPolicyVersion { get; }

        public string SceneName { get; }

        public static StateKey Create(
            string sourceText,
            string targetLanguage,
            string promptPolicyVersion,
            string? sceneName)
        {
            return new StateKey(sourceText, targetLanguage, promptPolicyVersion, sceneName ?? string.Empty);
        }

        public bool Equals(StateKey other)
        {
            return string.Equals(SourceText, other.SourceText, StringComparison.Ordinal) &&
                string.Equals(TargetLanguage, other.TargetLanguage, StringComparison.Ordinal) &&
                string.Equals(PromptPolicyVersion, other.PromptPolicyVersion, StringComparison.Ordinal) &&
                string.Equals(SceneName, other.SceneName, StringComparison.Ordinal);
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
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(SourceText);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(TargetLanguage);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(PromptPolicyVersion);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(SceneName);
                return hash;
            }
        }
    }

    private sealed class StateEntry
    {
        public StateEntry(long firstSeenSequence)
        {
            FirstSeenSequence = firstSeenSequence;
        }

        public long FirstSeenSequence { get; }

        public bool HasProcessedSource { get; set; }

        public bool Ignored { get; set; }

        public bool InBatch { get; set; }

        public string? TranslatedText { get; set; }

        public double LastCacheLookupSeconds { get; set; } = double.NegativeInfinity;

        public double LastSeenSeconds { get; set; }

        public int LastSeenFrameId { get; set; }

        public double LastBatchSeconds { get; set; }

        public long LastSeenSequence { get; set; }
    }
}

internal sealed record ImguiPendingText(
    string SourceText,
    string TargetLanguage,
    string PromptPolicyVersion,
    string SceneName,
    bool ShouldProcessSource);

internal sealed record ImguiTranslationStateResult(string DisplayText, bool IsTranslated)
{
    public static ImguiTranslationStateResult Pending(string sourceText) => new(sourceText, false);

    public static ImguiTranslationStateResult Translated(string translatedText) => new(translatedText, true);
}
