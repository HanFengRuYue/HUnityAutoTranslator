using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Glossary;
using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Pipeline;

public static class TranslationCacheReuse
{
    private const int CandidateLimit = 50;

    public static bool TryGetReusableTranslation(
        ITranslationCache cache,
        TranslationCacheKey key,
        TranslationCacheContext context,
        RuntimeConfig config,
        IGlossaryStore? glossary,
        out string translatedText)
    {
        var candidates = cache.GetCompletedTranslationsBySource(key, CandidateLimit)
            .Where(candidate => CandidateIsUsable(key.SourceText, candidate.TranslatedText, context, config, glossary))
            .Select(candidate => new ScoredCandidate(candidate.TranslatedText!, Score(candidate, context)))
            .ToArray();
        if (candidates.Length == 0)
        {
            translatedText = string.Empty;
            return false;
        }

        var bestScore = candidates.Max(candidate => candidate.Score);
        var bestTranslations = candidates
            .Where(candidate => candidate.Score == bestScore)
            .Select(candidate => candidate.TranslatedText)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (bestTranslations.Length == 1)
        {
            translatedText = bestTranslations[0];
            return true;
        }

        translatedText = string.Empty;
        return false;
    }

    private static bool CandidateIsUsable(
        string sourceText,
        string? translatedText,
        TranslationCacheContext context,
        RuntimeConfig config,
        IGlossaryStore? glossary)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            return false;
        }

        var outputValidation = TranslationOutputValidator.ValidateSingle(
            sourceText,
            translatedText,
            requireSameRichTextTags: true);
        if (!outputValidation.IsValid)
        {
            return false;
        }

        if (config.EnableGlossary && glossary != null)
        {
            var terms = GlossaryMatcher.MatchTerms(
                new[] { sourceText },
                glossary.GetEnabledTerms(config.TargetLanguage),
                config.GlossaryMaxTerms,
                config.GlossaryMaxCharacters);
            if (terms.Count > 0 &&
                !GlossaryOutputValidator.ValidateSingle(sourceText, translatedText, terms).IsValid)
            {
                return false;
            }
        }

        var itemContext = new PromptItemContext(
            0,
            context.SceneName,
            context.ComponentHierarchy,
            context.ComponentType);
        return TranslationQualityValidator.ValidateBatch(
            new[] { sourceText },
            new[] { translatedText },
            new[] { itemContext },
            config.TargetLanguage,
            config.GameTitle).IsValid;
    }

    private static int Score(TranslationCacheEntry candidate, TranslationCacheContext context)
    {
        var score = 0;
        if (Same(candidate.SceneName, context.SceneName))
        {
            score += 100;
        }

        if (Same(candidate.ComponentHierarchy, context.ComponentHierarchy))
        {
            score += 80;
        }
        else if (Same(
            PromptItemClassifier.GetParentHierarchy(candidate.ComponentHierarchy),
            PromptItemClassifier.GetParentHierarchy(context.ComponentHierarchy)))
        {
            score += 40;
        }
        else if (Same(LeafName(candidate.ComponentHierarchy), LeafName(context.ComponentHierarchy)))
        {
            score += 20;
        }

        if (Same(candidate.ComponentType, context.ComponentType))
        {
            score += 5;
        }

        return score;
    }

    private static bool Same(string? left, string? right)
    {
        return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
    }

    private static string LeafName(string? hierarchy)
    {
        var value = hierarchy ?? string.Empty;
        var index = value.LastIndexOf('/');
        return index < 0 ? value : value[(index + 1)..];
    }

    private sealed record ScoredCandidate(string TranslatedText, int Score);
}
