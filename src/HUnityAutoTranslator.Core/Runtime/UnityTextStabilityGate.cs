namespace HUnityAutoTranslator.Core.Runtime;

internal sealed class UnityTextStabilityGate
{
    private readonly Dictionary<StableTextKey, StableTextEntry> _entries = new();
    private readonly double _stableSeconds;
    private readonly double _typewriterStableSeconds;
    private readonly double _entryTtlSeconds;
    private double _nextPruneSeconds;

    public UnityTextStabilityGate(
        double stableSeconds = 0.25,
        double typewriterStableSeconds = 1.0,
        double entryTtlSeconds = 300)
    {
        _stableSeconds = Math.Max(0.05, stableSeconds);
        _typewriterStableSeconds = Math.Max(_stableSeconds, typewriterStableSeconds);
        _entryTtlSeconds = Math.Max(_typewriterStableSeconds, entryTtlSeconds);
    }

    public bool ShouldProcess(StableTextContext context, string? sourceText, double nowSeconds)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return false;
        }

        var key = StableTextKey.Create(context);
        if (!_entries.TryGetValue(key, out var entry))
        {
            _entries[key] = new StableTextEntry(sourceText, nowSeconds);
            TrimIfNeeded(nowSeconds);
            return false;
        }

        entry.LastSeenSeconds = nowSeconds;
        TrimIfNeeded(nowSeconds);

        if (!string.Equals(entry.CurrentText, sourceText, StringComparison.Ordinal))
        {
            var isPrefixGrowth = IsPrefixGrowth(entry.CurrentText, sourceText);
            entry.CurrentText = sourceText;
            entry.CurrentTextReleased = false;
            entry.IsTypewriterText = isPrefixGrowth;
            entry.LastChangedSeconds = nowSeconds;
            return false;
        }

        if (entry.CurrentTextReleased)
        {
            return false;
        }

        var requiredStableSeconds = entry.IsTypewriterText ? _typewriterStableSeconds : _stableSeconds;
        if (nowSeconds - entry.LastChangedSeconds < requiredStableSeconds)
        {
            return false;
        }

        entry.CurrentTextReleased = true;
        return true;
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

        public double LastChangedSeconds { get; set; }

        public double LastSeenSeconds { get; set; }
    }
}

internal sealed record StableTextContext(
    string TargetId,
    string TargetLanguage,
    string PromptPolicyVersion,
    string? SceneName,
    string? ComponentHierarchy,
    string? ComponentType);
