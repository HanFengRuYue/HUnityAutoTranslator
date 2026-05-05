using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Prompts;
using HUnityAutoTranslator.Core.Providers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Glossary;

public static class GlossaryExtractionService
{
    private const int SourceRowLimit = 40;

    public static async Task<GlossaryExtractionResult> ExtractOnceAsync(
        ITranslationCache cache,
        IGlossaryStore glossary,
        ITranslationProvider provider,
        RuntimeConfig config,
        CancellationToken cancellationToken)
    {
        if (!config.EnableAutoTermExtraction)
        {
            return new GlossaryExtractionResult(0, 0, 0);
        }

        var rows = cache.Query(new TranslationCacheQuery(null, "updated_utc", true, 0, SourceRowLimit))
            .Items
            .Where(row => !string.IsNullOrWhiteSpace(row.TranslatedText)
                && string.Equals(row.TargetLanguage, config.TargetLanguage, StringComparison.Ordinal))
            .Take(SourceRowLimit)
            .ToArray();
        if (rows.Length == 0)
        {
            return new GlossaryExtractionResult(0, 0, 0);
        }

        var request = new TranslationRequest(
            Array.Empty<string>(),
            config.TargetLanguage,
            PromptBuilder.BuildGlossaryExtractionSystemPrompt(config.PromptTemplates),
            PromptBuilder.BuildGlossaryExtractionUserPrompt(rows, config.PromptTemplates));
        var response = await provider.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.Succeeded || response.TranslatedTexts.Count == 0)
        {
            return new GlossaryExtractionResult(0, 0, rows.Length);
        }

        var candidates = ParseCandidates(response.TranslatedTexts[0]);
        var imported = 0;
        var skipped = 0;
        foreach (var candidate in candidates)
        {
            if (!IsValidCandidate(candidate, rows))
            {
                skipped++;
                continue;
            }

            var result = glossary.UpsertAutomatic(GlossaryTerm.CreateAutomatic(
                candidate.Source,
                candidate.Target,
                config.TargetLanguage,
                candidate.Note));
            if (result is GlossaryUpsertResult.Created or GlossaryUpsertResult.Updated)
            {
                imported++;
            }
            else
            {
                skipped++;
            }
        }

        return new GlossaryExtractionResult(imported, skipped, rows.Length);
    }

    private static IReadOnlyList<Candidate> ParseCandidates(string text)
    {
        try
        {
            var array = JArray.Parse(text.Trim());
            return array
                .OfType<JObject>()
                .Select(item => new Candidate(
                    item.Value<string>("source") ?? string.Empty,
                    item.Value<string>("target") ?? string.Empty,
                    item.Value<string>("note")))
                .ToArray();
        }
        catch
        {
            return Array.Empty<Candidate>();
        }
    }

    private static bool IsValidCandidate(Candidate candidate, IReadOnlyList<TranslationCacheEntry> rows)
    {
        var source = candidate.Source.Trim();
        var target = candidate.Target.Trim();
        if (source.Length < 2 || target.Length == 0 || !HasSemanticCharacter(source) || !HasSemanticCharacter(target))
        {
            return false;
        }

        if (LooksUnsafe(source) || LooksUnsafe(target))
        {
            return false;
        }

        return rows.Any(row =>
            row.SourceText.IndexOf(source, StringComparison.OrdinalIgnoreCase) >= 0 &&
            (row.TranslatedText ?? string.Empty).IndexOf(target, StringComparison.Ordinal) >= 0);
    }

    private static bool HasSemanticCharacter(string value)
    {
        return value.Any(char.IsLetterOrDigit);
    }

    private static bool LooksUnsafe(string value)
    {
        return value.IndexOf("__HUT_TOKEN_", StringComparison.Ordinal) >= 0
            || value.IndexOf('<') >= 0
            || value.IndexOf('>') >= 0
            || value.IndexOf('{') >= 0
            || value.IndexOf('}') >= 0
            || value.Length > 80;
    }

    private sealed record Candidate(string Source, string Target, string? Note);
}

public sealed record GlossaryExtractionResult(
    int ImportedCount,
    int SkippedCount,
    int SourcePairCount);
