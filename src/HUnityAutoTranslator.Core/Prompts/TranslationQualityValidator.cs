using System.Globalization;

namespace HUnityAutoTranslator.Core.Prompts;

public static class TranslationQualityValidator
{
    public static ValidationResult ValidateBatch(
        IReadOnlyList<string> sourceTexts,
        IReadOnlyList<string> translatedTexts,
        IReadOnlyList<PromptItemContext>? itemContexts,
        string targetLanguage,
        string? gameTitle)
    {
        var failures = FindFailures(sourceTexts, translatedTexts, itemContexts, targetLanguage, gameTitle);
        return failures.Count == 0
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(failures[0].Reason);
    }

    public static IReadOnlyList<TranslationQualityFailure> FindFailures(
        IReadOnlyList<string> sourceTexts,
        IReadOnlyList<string> translatedTexts,
        IReadOnlyList<PromptItemContext>? itemContexts,
        string targetLanguage,
        string? gameTitle)
    {
        var failures = new List<TranslationQualityFailure>();
        if (sourceTexts.Count != translatedTexts.Count)
        {
            failures.Add(new TranslationQualityFailure(0, "translation quality check could not match source and result counts"));
            return failures;
        }

        var contextByIndex = BuildContextLookup(itemContexts);
        var isSimplifiedChinese = PromptItemClassifier.IsSimplifiedChineseTarget(targetLanguage);
        for (var i = 0; i < sourceTexts.Count; i++)
        {
            contextByIndex.TryGetValue(i, out var context);
            var hints = PromptItemClassifier.BuildHints(sourceTexts[i], context, gameTitle);
            var failure = ValidateSingle(i, sourceTexts[i], translatedTexts[i], hints, isSimplifiedChinese, gameTitle);
            if (failure != null)
            {
                failures.Add(failure);
            }
        }

        AddSameParentCollisions(sourceTexts, translatedTexts, contextByIndex, gameTitle, failures);
        return failures;
    }

    private static TranslationQualityFailure? ValidateSingle(
        int index,
        string sourceText,
        string translatedText,
        IReadOnlyList<string> hints,
        bool isSimplifiedChinese,
        string? gameTitle)
    {
        if (PromptItemClassifier.ContainsGameTitle(sourceText, gameTitle) &&
            !PromptItemClassifier.ContainsGameTitle(translatedText, gameTitle))
        {
            return new TranslationQualityFailure(index, "game title must be preserved exactly when it appears in the source text");
        }

        if (!isSimplifiedChinese)
        {
            return null;
        }

        if (IsSuspiciousUntranslatedEnglish(sourceText, translatedText, hints))
        {
            return new TranslationQualityFailure(index, "ordinary English UI text was left untranslated");
        }

        if (IsSuspiciousAccessibilityTranslation(sourceText, translatedText, hints))
        {
            return new TranslationQualityFailure(index, "accessibility option translation is too broad or not specific enough");
        }

        if (IsSuspiciousShortSettingValue(sourceText, translatedText, hints))
        {
            return new TranslationQualityFailure(index, "settings value translation is too short or incomplete");
        }

        if (IsLiteralStateTranslation(sourceText, translatedText, hints))
        {
            return new TranslationQualityFailure(index, "state text is too literal for a game UI setting");
        }

        return null;
    }

    private static void AddSameParentCollisions(
        IReadOnlyList<string> sourceTexts,
        IReadOnlyList<string> translatedTexts,
        IReadOnlyDictionary<int, PromptItemContext> contextByIndex,
        string? gameTitle,
        List<TranslationQualityFailure> failures)
    {
        var failedIndexes = failures.Select(failure => failure.TextIndex).ToHashSet();
        for (var i = 0; i < sourceTexts.Count; i++)
        {
            if (!contextByIndex.TryGetValue(i, out var leftContext))
            {
                continue;
            }

            var leftParent = PromptItemClassifier.GetParentHierarchy(leftContext.ComponentHierarchy);
            if (leftParent == null)
            {
                continue;
            }

            for (var j = i + 1; j < sourceTexts.Count; j++)
            {
                if (failedIndexes.Contains(j) ||
                    !contextByIndex.TryGetValue(j, out var rightContext) ||
                    !string.Equals(leftParent, PromptItemClassifier.GetParentHierarchy(rightContext.ComponentHierarchy), StringComparison.Ordinal))
                {
                    continue;
                }

                if (IsSameMeaningSource(sourceTexts[i], sourceTexts[j]) ||
                    !IsSameMeaningTranslation(translatedTexts[i], translatedTexts[j]))
                {
                    continue;
                }

                var leftHints = PromptItemClassifier.BuildHints(sourceTexts[i], leftContext, gameTitle);
                var rightHints = PromptItemClassifier.BuildHints(sourceTexts[j], rightContext, gameTitle);
                if (!IsOptionLike(leftHints) || !IsOptionLike(rightHints))
                {
                    continue;
                }

                failures.Add(new TranslationQualityFailure(
                    j,
                    "different option texts under the same parent produced the same translation"));
                failedIndexes.Add(j);
            }
        }
    }

    private static bool IsSuspiciousUntranslatedEnglish(
        string sourceText,
        string translatedText,
        IReadOnlyList<string> hints)
    {
        var normalizedSource = PromptItemClassifier.NormalizeForMatch(sourceText);
        var normalizedTranslation = PromptItemClassifier.NormalizeForMatch(translatedText);
        if (!string.Equals(normalizedSource, normalizedTranslation, StringComparison.OrdinalIgnoreCase) ||
            !ContainsLatinLetter(normalizedSource) ||
            hints.Contains("game_title"))
        {
            return false;
        }

        if (hints.Contains("accessibility_option") || hints.Contains("menu_action") || hints.Contains("settings_value"))
        {
            return true;
        }

        return !IsAllowedUntranslatedToken(normalizedSource);
    }

    private static bool IsSuspiciousShortSettingValue(
        string sourceText,
        string translatedText,
        IReadOnlyList<string> hints)
    {
        if (!hints.Contains("settings_value") && !hints.Contains("accessibility_option"))
        {
            return false;
        }

        var source = PromptItemClassifier.NormalizeForMatch(sourceText).ToLowerInvariant();
        if (source is "low" or "high")
        {
            return false;
        }

        var compactTranslation = new string((translatedText ?? string.Empty)
            .Where(character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character) && !char.IsSymbol(character))
            .ToArray());
        return source.Length >= 4 && CountTextElements(compactTranslation) <= 1;
    }

    private static bool IsSuspiciousAccessibilityTranslation(
        string sourceText,
        string translatedText,
        IReadOnlyList<string> hints)
    {
        if (!hints.Contains("accessibility_option"))
        {
            return false;
        }

        var source = PromptItemClassifier.NormalizeForMatch(sourceText).ToLowerInvariant();
        if (!source.Contains("protanopia", StringComparison.Ordinal) &&
            !source.Contains("deuteranopia", StringComparison.Ordinal) &&
            !source.Contains("tritanopia", StringComparison.Ordinal))
        {
            return false;
        }

        var compactTranslation = new string((translatedText ?? string.Empty)
            .Where(character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character) && !char.IsSymbol(character))
            .ToArray());
        return !ContainsAny(compactTranslation, "\u8272\u76f2", "\u8272\u89c9") ||
            ContainsAny(
                compactTranslation,
                "\u5168\u8272\u76f2",
                "\u539f\u8272\u76f2",
                "\u7ea2\u7eff\u8272\u76f2",
                "\u4e8c\u8272\u89c6");
    }

    private static bool IsLiteralStateTranslation(
        string sourceText,
        string translatedText,
        IReadOnlyList<string> hints)
    {
        if (!hints.Contains("toggle_state") && !hints.Contains("settings_value"))
        {
            return false;
        }

        var source = PromptItemClassifier.NormalizeForMatch(sourceText).ToLowerInvariant();
        return source is "activated" or "active" or "enabled" &&
            (translatedText ?? string.Empty).Contains("\u6fc0\u6d3b", StringComparison.Ordinal);
    }

    private static bool IsSameMeaningSource(string left, string right)
    {
        return string.Equals(
            PromptItemClassifier.NormalizeForMatch(left),
            PromptItemClassifier.NormalizeForMatch(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameMeaningTranslation(string left, string right)
    {
        return string.Equals(
            PromptItemClassifier.NormalizeForMatch(left),
            PromptItemClassifier.NormalizeForMatch(right),
            StringComparison.Ordinal);
    }

    private static bool IsOptionLike(IReadOnlyList<string> hints)
    {
        return hints.Contains("accessibility_option") ||
            hints.Contains("settings_value") ||
            hints.Contains("toggle_state");
    }

    private static bool IsAllowedUntranslatedToken(string source)
    {
        var compact = source.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);
        return compact.Length <= 4 &&
            compact.All(character => char.IsUpper(character) || char.IsDigit(character));
    }

    private static bool ContainsLatinLetter(string value)
    {
        return value.Any(character => (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z'));
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.IndexOf(needle, StringComparison.Ordinal) >= 0);
    }

    private static int CountTextElements(string value)
    {
        return new StringInfo(value ?? string.Empty).LengthInTextElements;
    }

    private static IReadOnlyDictionary<int, PromptItemContext> BuildContextLookup(IReadOnlyList<PromptItemContext>? itemContexts)
    {
        return itemContexts == null
            ? new Dictionary<int, PromptItemContext>()
            : itemContexts.GroupBy(context => context.TextIndex).ToDictionary(group => group.Key, group => group.First());
    }
}
