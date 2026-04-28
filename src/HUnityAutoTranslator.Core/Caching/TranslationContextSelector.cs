using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Caching;

internal static class TranslationContextSelector
{
    public static IReadOnlyList<TranslationContextExample> Select(
        IEnumerable<TranslationCacheEntry> rows,
        string currentSourceText,
        string targetLanguage,
        TranslationCacheContext? context,
        int maxExamples,
        int maxCharacters)
    {
        if (maxExamples <= 0 || maxCharacters <= 0)
        {
            return Array.Empty<TranslationContextExample>();
        }

        context ??= TranslationCacheContext.Empty;
        var sceneName = TranslationCacheLookupKey.NormalizeContextPart(context.SceneName);
        if (sceneName.Length == 0)
        {
            return Array.Empty<TranslationContextExample>();
        }

        var componentHierarchy = TranslationCacheLookupKey.NormalizeContextPart(context.ComponentHierarchy);
        var currentSourceKey = TextNormalizer.NormalizeForCache(currentSourceText);
        var translatedRows = rows
            .Where(row => row.TranslatedText != null
                && string.Equals(row.TargetLanguage, targetLanguage, StringComparison.Ordinal)
                && string.Equals(TranslationCacheLookupKey.NormalizeContextPart(row.SceneName), sceneName, StringComparison.Ordinal)
                && !string.Equals(TextNormalizer.NormalizeForCache(row.SourceText), currentSourceKey, StringComparison.Ordinal))
            .ToArray();

        var selected = new List<TranslationContextExample>();
        var seenSources = new HashSet<string>(StringComparer.Ordinal);
        var usedCharacters = 0;

        if (componentHierarchy.Length > 0)
        {
            AddRows(translatedRows
                .Where(row => string.Equals(
                    TranslationCacheLookupKey.NormalizeContextPart(row.ComponentHierarchy),
                    componentHierarchy,
                    StringComparison.Ordinal))
                .OrderByDescending(row => row.UpdatedUtc));
        }

        var parentHierarchy = GetParentHierarchy(componentHierarchy);
        if (parentHierarchy.Length > 0)
        {
            AddRows(translatedRows
                .Where(row => IsSameParentSibling(row.ComponentHierarchy, componentHierarchy, parentHierarchy))
                .OrderByDescending(row => row.UpdatedUtc));
        }

        AddRows(translatedRows
            .OrderByDescending(row => row.UpdatedUtc));

        return selected;

        void AddRows(IEnumerable<TranslationCacheEntry> candidates)
        {
            foreach (var row in candidates)
            {
                if (selected.Count >= maxExamples)
                {
                    return;
                }

                var translatedText = row.TranslatedText;
                if (string.IsNullOrWhiteSpace(row.SourceText) || string.IsNullOrWhiteSpace(translatedText))
                {
                    continue;
                }

                var sourceKey = TextNormalizer.NormalizeForCache(row.SourceText);
                if (sourceKey.Length == 0 || !seenSources.Add(sourceKey))
                {
                    continue;
                }

                var nextCharacters = usedCharacters + row.SourceText.Length + translatedText.Length;
                if (nextCharacters > maxCharacters)
                {
                    continue;
                }

                selected.Add(new TranslationContextExample(row.SourceText, translatedText));
                usedCharacters = nextCharacters;
            }
        }
    }

    private static bool IsSameParentSibling(string? candidateHierarchy, string currentHierarchy, string currentParent)
    {
        var normalized = TranslationCacheLookupKey.NormalizeContextPart(candidateHierarchy);
        if (normalized.Length == 0 ||
            string.Equals(normalized, currentHierarchy, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(GetParentHierarchy(normalized), currentParent, StringComparison.Ordinal);
    }

    private static string GetParentHierarchy(string componentHierarchy)
    {
        var index = componentHierarchy.LastIndexOf('/');
        return index <= 0 ? string.Empty : componentHierarchy[..index];
    }
}

