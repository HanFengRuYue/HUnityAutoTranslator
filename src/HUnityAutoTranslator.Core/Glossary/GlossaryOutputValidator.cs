using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Glossary;

public static class GlossaryOutputValidator
{
    public static ValidationResult ValidateSingle(
        string sourceText,
        string translatedText,
        IReadOnlyList<GlossaryPromptTerm> terms)
    {
        foreach (var term in terms)
        {
            if (sourceText.IndexOf(term.SourceTerm, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (translatedText.IndexOf(term.TargetTerm, StringComparison.Ordinal) < 0)
            {
                return ValidationResult.Invalid($"术语不一致: {term.SourceTerm} 必须译为 {term.TargetTerm}");
            }
        }

        return ValidationResult.Valid();
    }

    public static ValidationResult ValidateBatch(
        IReadOnlyList<string> sourceTexts,
        IReadOnlyList<string> translatedTexts,
        IReadOnlyList<GlossaryPromptTerm> terms)
    {
        if (sourceTexts.Count != translatedTexts.Count)
        {
            return ValidationResult.Invalid("批量翻译数量不匹配");
        }

        for (var i = 0; i < sourceTexts.Count; i++)
        {
            var itemTerms = terms.Where(term => term.TextIndex == i).ToArray();
            var result = ValidateSingle(sourceTexts[i], translatedTexts[i], itemTerms);
            if (!result.IsValid)
            {
                return ValidationResult.Invalid($"第 {i} 条无效: {result.Reason}");
            }
        }

        return ValidationResult.Valid();
    }
}
