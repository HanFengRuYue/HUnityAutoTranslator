using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Queueing;

public sealed class QualityRetryResumeSuppressions
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _suppressedByKey = new(StringComparer.Ordinal);

    public void Suppress(TranslationJob job, DateTimeOffset suppressedUtc)
    {
        Suppress(job.SourceText, job.Context, suppressedUtc);
    }

    public void Suppress(string sourceText, TranslationCacheContext? context, DateTimeOffset suppressedUtc)
    {
        var key = CreateKey(sourceText, context);
        if (key.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            _suppressedByKey[key] = suppressedUtc;
        }
    }

    public bool ShouldSkip(TranslationCacheEntry row)
    {
        var key = CreateKey(
            row.SourceText,
            new TranslationCacheContext(row.SceneName, row.ComponentHierarchy, row.ComponentType));
        if (key.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_suppressedByKey.TryGetValue(key, out var suppressedUtc))
            {
                return false;
            }

            if (row.UpdatedUtc <= suppressedUtc)
            {
                return true;
            }

            _suppressedByKey.Remove(key);
            return false;
        }
    }

    private static string CreateKey(string sourceText, TranslationCacheContext? context)
    {
        var source = TextNormalizer.NormalizeForCache(sourceText);
        if (source.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "\u001f",
            source,
            context?.SceneName ?? string.Empty,
            context?.ComponentHierarchy ?? string.Empty);
    }
}
