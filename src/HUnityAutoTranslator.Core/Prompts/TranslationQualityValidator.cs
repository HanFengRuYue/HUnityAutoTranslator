using System.Globalization;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Prompts;

public static class TranslationQualityValidator
{
    private enum OuterSymbolKind
    {
        Quote,
        Square,
        Round,
        BookTitle,
        Corner,
        Brace
    }

    private static readonly (string Open, string Close, OuterSymbolKind Kind)[] OuterSymbolPairs =
    {
        ("\"", "\"", OuterSymbolKind.Quote),
        ("'", "'", OuterSymbolKind.Quote),
        ("\u201c", "\u201d", OuterSymbolKind.Quote),
        ("\u2018", "\u2019", OuterSymbolKind.Quote),
        ("[", "]", OuterSymbolKind.Square),
        ("\uff3b", "\uff3d", OuterSymbolKind.Square),
        ("\u3010", "\u3011", OuterSymbolKind.Square),
        ("\u3014", "\u3015", OuterSymbolKind.Square),
        ("(", ")", OuterSymbolKind.Round),
        ("\uff08", "\uff09", OuterSymbolKind.Round),
        ("\u300a", "\u300b", OuterSymbolKind.BookTitle),
        ("<", ">", OuterSymbolKind.BookTitle),
        ("\u3008", "\u3009", OuterSymbolKind.BookTitle),
        ("\u300c", "\u300d", OuterSymbolKind.Corner),
        ("\u300e", "\u300f", OuterSymbolKind.Corner),
        ("{", "}", OuterSymbolKind.Brace)
    };

    public static ValidationResult ValidateBatch(
        IReadOnlyList<string> sourceTexts,
        IReadOnlyList<string> translatedTexts,
        IReadOnlyList<PromptItemContext>? itemContexts,
        string targetLanguage,
        string? gameTitle,
        TranslationQualityConfig? qualityConfig = null)
    {
        var failures = FindFailures(sourceTexts, translatedTexts, itemContexts, targetLanguage, gameTitle, qualityConfig);
        return failures.Count == 0
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(failures[0].Reason);
    }

    public static IReadOnlyList<TranslationQualityFailure> FindFailures(
        IReadOnlyList<string> sourceTexts,
        IReadOnlyList<string> translatedTexts,
        IReadOnlyList<PromptItemContext>? itemContexts,
        string targetLanguage,
        string? gameTitle,
        TranslationQualityConfig? qualityConfig = null)
    {
        var failures = new List<TranslationQualityFailure>();
        var config = (qualityConfig ?? TranslationQualityConfig.Default()).Normalize();
        if (!config.Enabled)
        {
            return failures;
        }

        if (sourceTexts.Count != translatedTexts.Count)
        {
            failures.Add(new TranslationQualityFailure(0, "translation quality check could not match source and result counts"));
            return failures;
        }

        var contextByIndex = BuildContextLookup(itemContexts);
        var isSimplifiedChinese = PromptItemClassifier.IsSimplifiedChineseTarget(targetLanguage);
        var visibleSourceTexts = sourceTexts.Select(RichTextGuard.GetVisibleText).ToArray();
        var visibleTranslatedTexts = translatedTexts.Select(RichTextGuard.GetVisibleText).ToArray();
        for (var i = 0; i < sourceTexts.Count; i++)
        {
            contextByIndex.TryGetValue(i, out var context);
            var hints = PromptItemClassifier.BuildHints(visibleSourceTexts[i], context, gameTitle);
            var failure = ValidateSingle(i, visibleSourceTexts[i], visibleTranslatedTexts[i], hints, isSimplifiedChinese, gameTitle, config);
            if (failure != null)
            {
                failures.Add(failure);
            }
        }

        AddSameParentCollisions(visibleSourceTexts, visibleTranslatedTexts, contextByIndex, isSimplifiedChinese, config, gameTitle, failures);
        return failures;
    }

    private static TranslationQualityFailure? ValidateSingle(
        int index,
        string sourceText,
        string translatedText,
        IReadOnlyList<string> hints,
        bool isSimplifiedChinese,
        string? gameTitle,
        TranslationQualityConfig config)
    {
        if (config.PreserveGameTitle &&
            PromptItemClassifier.ContainsGameTitle(sourceText, gameTitle) &&
            !PromptItemClassifier.ContainsGameTitle(translatedText, gameTitle))
        {
            return new TranslationQualityFailure(index, "game title must be preserved exactly when it appears in the source text");
        }

        if (config.RejectGeneratedOuterSymbols && HasGeneratedOuterSymbols(sourceText, translatedText))
        {
            return new TranslationQualityFailure(index, "translation added outer symbols that are not present in the source text");
        }

        if (config.RejectGeneratedOuterSymbols && UiMarkerSymbolGuard.HasChangedMarkers(sourceText, translatedText))
        {
            return new TranslationQualityFailure(index, "translation changed UI marker symbols that must be preserved");
        }

        if (!isSimplifiedChinese ||
            (config.AllowAlreadyTargetLanguageSource && IsAlreadySimplifiedChineseSource(sourceText)))
        {
            return null;
        }

        if (config.RejectUntranslatedLatinUiText && IsSuspiciousUntranslatedEnglish(sourceText, translatedText, hints))
        {
            return new TranslationQualityFailure(index, "ordinary English UI text was left untranslated");
        }

        if (config.RejectShortSettingValue && IsSuspiciousShortSettingValue(sourceText, translatedText, hints, config))
        {
            return new TranslationQualityFailure(index, "settings value translation is too short or incomplete");
        }

        if (config.RejectLiteralStateTranslation && IsLiteralStateTranslation(sourceText, translatedText, hints))
        {
            return new TranslationQualityFailure(index, "state text is too literal for a game UI setting");
        }

        return null;
    }

    private static void AddSameParentCollisions(
        IReadOnlyList<string> sourceTexts,
        IReadOnlyList<string> translatedTexts,
        IReadOnlyDictionary<int, PromptItemContext> contextByIndex,
        bool isSimplifiedChinese,
        TranslationQualityConfig config,
        string? gameTitle,
        List<TranslationQualityFailure> failures)
    {
        if (!config.RejectSameParentOptionCollision)
        {
            return;
        }

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

                if (isSimplifiedChinese &&
                    config.AllowAlreadyTargetLanguageSource &&
                    IsAlreadySimplifiedChineseSource(sourceTexts[i]) &&
                    IsAlreadySimplifiedChineseSource(sourceTexts[j]))
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

        if (PreservableTextClassifier.CanRemainUntranslated(normalizedSource))
        {
            return false;
        }

        if (IsAllowedUntranslatedToken(normalizedSource))
        {
            return false;
        }

        if (hints.Contains("accessibility_option") || hints.Contains("menu_action") || hints.Contains("settings_value"))
        {
            return true;
        }

        return true;
    }

    private static bool IsSuspiciousShortSettingValue(
        string sourceText,
        string translatedText,
        IReadOnlyList<string> hints,
        TranslationQualityConfig config)
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
        return source.Length >= config.ShortSettingValueMinSourceLength &&
            CountTextElements(compactTranslation) <= config.ShortSettingValueMaxTranslationTextElements;
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

    private static bool HasGeneratedOuterSymbols(string sourceText, string translatedText)
    {
        return TryGetOuterSymbolKind(translatedText, out var translatedKind) &&
            (!TryGetOuterSymbolKind(sourceText, out var sourceKind) || sourceKind != translatedKind);
    }

    private static bool TryGetOuterSymbolKind(string value, out OuterSymbolKind kind)
    {
        var trimmed = (value ?? string.Empty).Trim();
        foreach (var pair in OuterSymbolPairs)
        {
            if (trimmed.Length <= pair.Open.Length + pair.Close.Length ||
                !trimmed.StartsWith(pair.Open, StringComparison.Ordinal) ||
                !trimmed.EndsWith(pair.Close, StringComparison.Ordinal))
            {
                continue;
            }

            var innerLength = trimmed.Length - pair.Open.Length - pair.Close.Length;
            if (string.IsNullOrWhiteSpace(trimmed.Substring(pair.Open.Length, innerLength)))
            {
                continue;
            }

            kind = pair.Kind;
            return true;
        }

        kind = default;
        return false;
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

    private static bool IsAlreadySimplifiedChineseSource(string value)
    {
        return TextFilter.IsAlreadyTargetLanguageSource(value, "zh-Hans");
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
