using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Glossary;

public static class GlossaryMatcher
{
    public static IReadOnlyList<GlossaryPromptTerm> MatchTerms(
        IReadOnlyList<string> sourceTexts,
        IReadOnlyList<GlossaryTerm> terms,
        int maxTerms,
        int maxCharacters)
    {
        if (sourceTexts.Count == 0 || terms.Count == 0 || maxTerms <= 0 || maxCharacters <= 0)
        {
            return Array.Empty<GlossaryPromptTerm>();
        }

        var result = new List<GlossaryPromptTerm>();
        var usedCharacters = 0;
        var orderedTerms = terms
            .Where(term => term.Enabled && !string.IsNullOrWhiteSpace(term.SourceTerm) && !string.IsNullOrWhiteSpace(term.TargetTerm))
            .OrderByDescending(term => term.SourceTerm.Length)
            .ThenBy(term => term.SourceTerm, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var textIndex = 0; textIndex < sourceTexts.Count; textIndex++)
        {
            var sourceText = RichTextGuard.GetVisibleText(sourceTexts[textIndex] ?? string.Empty);
            var occupied = new List<Range>();
            foreach (var term in orderedTerms)
            {
                if (result.Count >= maxTerms)
                {
                    return result;
                }

                var index = sourceText.IndexOf(term.SourceTerm, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    continue;
                }

                var range = new Range(index, index + term.SourceTerm.Length);
                if (occupied.Any(existing => existing.Overlaps(range)))
                {
                    continue;
                }

                var nextCharacters = usedCharacters + term.SourceTerm.Length + term.TargetTerm.Length;
                if (nextCharacters > maxCharacters)
                {
                    continue;
                }

                result.Add(new GlossaryPromptTerm(textIndex, term.SourceTerm, term.TargetTerm, term.Note));
                occupied.Add(range);
                usedCharacters = nextCharacters;
            }
        }

        return result;
    }

    private sealed record Range(int Start, int End)
    {
        public bool Overlaps(Range other)
        {
            return Start < other.End && other.Start < End;
        }
    }
}
