namespace HUnityAutoTranslator.Core.Runtime;

internal enum StableTextDecisionKind
{
    Wait,
    Process,
    RefreshCachedTranslation
}

internal sealed class UnityTextStabilityGate
{
    private readonly Dictionary<StableTextKey, StableTextEntry> _entries = new();
    private readonly double _stableSeconds;
    private readonly double _typewriterStableSeconds;
    private readonly double _fastStaticStableSeconds;
    private readonly double _releasedTextRefreshSeconds;
    private readonly double _entryTtlSeconds;
    private double _nextPruneSeconds;

    public UnityTextStabilityGate(
        double stableSeconds = 0.25,
        double typewriterStableSeconds = 0.35,
        double releasedTextRefreshSeconds = 1.0,
        double entryTtlSeconds = 300,
        double fastStaticStableSeconds = 0.08)
    {
        _stableSeconds = Math.Max(0.05, stableSeconds);
        _typewriterStableSeconds = Math.Max(_stableSeconds, typewriterStableSeconds);
        _fastStaticStableSeconds = Math.Min(_stableSeconds, Math.Max(0.02, fastStaticStableSeconds));
        _releasedTextRefreshSeconds = Math.Max(_stableSeconds, releasedTextRefreshSeconds);
        _entryTtlSeconds = Math.Max(_typewriterStableSeconds, entryTtlSeconds);
    }

    public bool ShouldProcess(
        StableTextContext context,
        string? sourceText,
        double nowSeconds,
        bool preferFastStaticRelease = false)
    {
        return Evaluate(context, sourceText, nowSeconds, preferFastStaticRelease) == StableTextDecisionKind.Process;
    }

    public StableTextDecisionKind Evaluate(
        StableTextContext context,
        string? sourceText,
        double nowSeconds,
        bool preferFastStaticRelease = false)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return StableTextDecisionKind.Wait;
        }

        var key = StableTextKey.Create(context);
        if (!_entries.TryGetValue(key, out var entry))
        {
            _entries[key] = new StableTextEntry(sourceText, nowSeconds)
            {
                FastStaticEligible = preferFastStaticRelease
            };
            TrimIfNeeded(nowSeconds);
            return StableTextDecisionKind.Wait;
        }

        entry.LastSeenSeconds = nowSeconds;
        TrimIfNeeded(nowSeconds);

        if (!string.Equals(entry.CurrentText, sourceText, StringComparison.Ordinal))
        {
            var isPrefixGrowth = IsPrefixGrowth(entry.CurrentText, sourceText);
            entry.CurrentText = sourceText;
            entry.CurrentTextReleased = false;
            entry.IsTypewriterText = isPrefixGrowth;
            entry.FastStaticEligible = preferFastStaticRelease && !isPrefixGrowth;
            entry.LastChangedSeconds = nowSeconds;
            entry.LastRefreshSeconds = nowSeconds;
            return StableTextDecisionKind.Wait;
        }

        if (preferFastStaticRelease && !entry.IsTypewriterText)
        {
            entry.FastStaticEligible = true;
        }

        if (entry.CurrentTextReleased)
        {
            if (nowSeconds - entry.LastRefreshSeconds < _releasedTextRefreshSeconds)
            {
                return StableTextDecisionKind.Wait;
            }

            entry.LastRefreshSeconds = nowSeconds;
            return StableTextDecisionKind.RefreshCachedTranslation;
        }

        var requiredStableSeconds = entry.IsTypewriterText
            ? _typewriterStableSeconds
            : entry.FastStaticEligible
                ? _fastStaticStableSeconds
                : _stableSeconds;
        if (nowSeconds - entry.LastChangedSeconds < requiredStableSeconds)
        {
            return StableTextDecisionKind.Wait;
        }

        entry.CurrentTextReleased = true;
        entry.LastRefreshSeconds = nowSeconds;
        return StableTextDecisionKind.Process;
    }

    private void TrimIfNeeded(double nowSeconds)
    {
        if (nowSeconds < _nextPruneSeconds)
        {
            return;
        }

        _nextPruneSeconds = nowSeconds + Math.Min(30, _entryTtlSeconds);
        var staleKeys = _entries
            .Where(item => nowSeconds - item.Value.LastSeenSeconds > _entryTtlSeconds)
            .Select(item => item.Key)
            .ToArray();
        foreach (var key in staleKeys)
        {
            _entries.Remove(key);
        }
    }

    private static bool IsPrefixGrowth(string previousText, string currentText)
    {
        return currentText.Length > previousText.Length &&
            currentText.StartsWith(previousText, StringComparison.Ordinal);
    }

    private readonly struct StableTextKey : IEquatable<StableTextKey>
    {
        private StableTextKey(
            string targetId,
            string targetLanguage,
            string promptPolicyVersion,
            string sceneName,
            string componentHierarchy,
            string componentType)
        {
            TargetId = targetId;
            TargetLanguage = targetLanguage;
            PromptPolicyVersion = promptPolicyVersion;
            SceneName = sceneName;
            ComponentHierarchy = componentHierarchy;
            ComponentType = componentType;
        }

        private string TargetId { get; }

        private string TargetLanguage { get; }

        private string PromptPolicyVersion { get; }

        private string SceneName { get; }

        private string ComponentHierarchy { get; }

        private string ComponentType { get; }

        public static StableTextKey Create(StableTextContext context)
        {
            return new StableTextKey(
                Normalize(context.TargetId),
                Normalize(context.TargetLanguage),
                Normalize(context.PromptPolicyVersion),
                Normalize(context.SceneName),
                Normalize(context.ComponentHierarchy),
                Normalize(context.ComponentType));
        }

        public bool Equals(StableTextKey other)
        {
            return string.Equals(TargetId, other.TargetId, StringComparison.Ordinal) &&
                string.Equals(TargetLanguage, other.TargetLanguage, StringComparison.Ordinal) &&
                string.Equals(PromptPolicyVersion, other.PromptPolicyVersion, StringComparison.Ordinal) &&
                string.Equals(SceneName, other.SceneName, StringComparison.Ordinal) &&
                string.Equals(ComponentHierarchy, other.ComponentHierarchy, StringComparison.Ordinal) &&
                string.Equals(ComponentType, other.ComponentType, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is StableTextKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(TargetId);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(TargetLanguage);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(PromptPolicyVersion);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(SceneName);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(ComponentHierarchy);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(ComponentType);
                return hash;
            }
        }

        private static string Normalize(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }
    }

    private sealed class StableTextEntry
    {
        public StableTextEntry(string currentText, double nowSeconds)
        {
            CurrentText = currentText;
            LastChangedSeconds = nowSeconds;
            LastSeenSeconds = nowSeconds;
        }

        public string CurrentText { get; set; }

        public bool CurrentTextReleased { get; set; }

        public bool IsTypewriterText { get; set; }

        public bool FastStaticEligible { get; set; }

        public double LastChangedSeconds { get; set; }

        public double LastSeenSeconds { get; set; }

        public double LastRefreshSeconds { get; set; }
    }
}

internal sealed record StableTextContext(
    string TargetId,
    string TargetLanguage,
    string PromptPolicyVersion,
    string? SceneName,
    string? ComponentHierarchy,
    string? ComponentType);
