using System.Globalization;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Glossary;
using Newtonsoft.Json;

namespace HUnityAutoTranslator.Core.Prompts;

public static class PromptBuilder
{
    public static string BuildSystemPrompt(PromptOptions options)
    {
        var style = BuildStyleInstruction(options.Style);
        var targetLanguageName = ResolveTargetLanguageName(options.TargetLanguage);
        var gameContext = BuildGameContext(options.GameTitle);
        var templates = (options.Templates ?? PromptTemplateConfig.Empty).NormalizeAgainstDefaults();
        var glossaryPolicy = options.HasGlossaryTerms
            ? "\n" + ApplyTemplate(templates.Resolve(item => item.GlossarySystemPolicy), new Dictionary<string, string>())
            : string.Empty;
        var template = !string.IsNullOrWhiteSpace(templates.SystemPrompt)
            ? templates.SystemPrompt!
            : !string.IsNullOrWhiteSpace(options.CustomPrompt)
                ? options.CustomPrompt!.Trim()
                : templates.Resolve(item => item.SystemPrompt);
        var values = new Dictionary<string, string>
        {
            ["TargetLanguage"] = targetLanguageName,
            ["StyleInstruction"] = style,
            ["GameTitle"] = NormalizeSingleLine(options.GameTitle),
            ["GameContext"] = gameContext,
            ["GlossarySystemPolicy"] = glossaryPolicy
        };
        var prompt = ApplyTemplate(template, values);
        if (options.HasGlossaryTerms && !prompt.Contains(glossaryPolicy.Trim(), StringComparison.Ordinal))
        {
            prompt += glossaryPolicy;
        }

        return prompt;
    }

    public static string BuildDefaultSystemPrompt(string targetLanguage, TranslationStyle style, string? gameTitle = null)
    {
        return BuildSystemPrompt(new PromptOptions(targetLanguage, style, GameTitle: gameTitle));
    }

    public static string BuildSingleUserPrompt(string protectedText)
    {
        return "Translate the following text. Return only the translation:\n" + protectedText;
    }

    public static string BuildBatchUserPrompt(
        IReadOnlyList<string> protectedTexts,
        IReadOnlyList<TranslationContextExample>? contextExamples = null,
        IReadOnlyList<GlossaryPromptTerm>? glossaryTerms = null,
        IReadOnlyList<PromptItemContext>? itemContexts = null,
        string? gameTitle = null,
        PromptTemplateConfig? templates = null)
    {
        var promptTemplates = (templates ?? PromptTemplateConfig.Empty).NormalizeAgainstDefaults();
        var json = JsonConvert.SerializeObject(protectedTexts, Formatting.None);
        var sections = new List<string>();
        if (glossaryTerms != null && glossaryTerms.Count > 0)
        {
            var glossaryJson = JsonConvert.SerializeObject(
                glossaryTerms.Select(term => new
                {
                    text_index = term.TextIndex,
                    source = term.SourceTerm,
                    target = term.TargetTerm,
                    note = term.Note
                }),
                Formatting.None);
            sections.Add(ApplyTemplate(
                promptTemplates.Resolve(item => item.GlossaryTermsSection),
                new Dictionary<string, string> { ["GlossaryTermsJson"] = glossaryJson }));
        }

        if (itemContexts != null && itemContexts.Count > 0)
        {
            var contextJson = JsonConvert.SerializeObject(
                itemContexts.Select(item => new
                {
                    text_index = item.TextIndex,
                    scene = NullIfWhiteSpace(item.SceneName),
                    component_hierarchy = NullIfWhiteSpace(item.ComponentHierarchy),
                    parent_hierarchy = GetParentHierarchy(item.ComponentHierarchy),
                    component_type = NullIfWhiteSpace(item.ComponentType)
                }),
                Formatting.None);
            sections.Add(ApplyTemplate(
                promptTemplates.Resolve(item => item.CurrentItemContextSection),
                new Dictionary<string, string> { ["ItemContextsJson"] = contextJson }));
        }

        var hintRows = BuildItemHintRows(protectedTexts, itemContexts, gameTitle);
        if (hintRows.Count > 0)
        {
            var hintsJson = JsonConvert.SerializeObject(hintRows, Formatting.None);
            sections.Add(ApplyTemplate(
                promptTemplates.Resolve(item => item.ItemHintsSection),
                new Dictionary<string, string> { ["ItemHintsJson"] = hintsJson }));
        }

        if (contextExamples != null && contextExamples.Count > 0)
        {
            var examplesJson = JsonConvert.SerializeObject(
                contextExamples.Select(example => new
                {
                    source = example.SourceText,
                    translation = example.TranslatedText
                }),
                Formatting.None);
            sections.Add(ApplyTemplate(
                promptTemplates.Resolve(item => item.ContextExamplesSection),
                new Dictionary<string, string> { ["ContextExamplesJson"] = examplesJson }));
        }

        return ApplyTemplate(
            promptTemplates.Resolve(item => item.BatchUserPrompt),
            new Dictionary<string, string>
            {
                ["PromptSections"] = sections.Count == 0 ? string.Empty : string.Join("\n", sections) + "\n",
                ["InputJson"] = json
            });
    }

    public static string BuildRepairPrompt(
        string sourceText,
        string invalidTranslation,
        string reason,
        IReadOnlyList<GlossaryPromptTerm>? glossaryTerms = null,
        PromptTemplateConfig? templates = null)
    {
        var promptTemplates = (templates ?? PromptTemplateConfig.Empty).NormalizeAgainstDefaults();
        var glossaryJson = glossaryTerms == null || glossaryTerms.Count == 0
            ? "[]"
            : JsonConvert.SerializeObject(
                glossaryTerms.Select(term => new
                {
                    source = term.SourceTerm,
                    target = term.TargetTerm,
                    note = term.Note
                }),
                Formatting.None);
        return ApplyTemplate(
            promptTemplates.Resolve(item => item.GlossaryRepairPrompt),
            new Dictionary<string, string>
            {
                ["SourceText"] = sourceText,
                ["InvalidTranslation"] = invalidTranslation,
                ["FailureReason"] = reason,
                ["RequiredGlossaryTermsJson"] = glossaryJson,
                ["RequiredGlossaryTermsBlock"] = glossaryTerms == null || glossaryTerms.Count == 0
                    ? string.Empty
                    : "\nRequired glossary terms:\n" + glossaryJson
            });
    }

    public static string BuildQualityRepairPrompt(
        string sourceText,
        string invalidTranslation,
        string reason,
        PromptItemContext? itemContext,
        IReadOnlyList<string>? sameParentSourceTexts,
        string? gameTitle,
        PromptTemplateConfig? templates = null)
    {
        var promptTemplates = (templates ?? PromptTemplateConfig.Empty).NormalizeAgainstDefaults();
        var hints = PromptItemClassifier.BuildHints(sourceText, itemContext, gameTitle);
        var context = itemContext == null
            ? null
            : new
            {
                text_index = itemContext.TextIndex,
                scene = NullIfWhiteSpace(itemContext.SceneName),
                component_hierarchy = NullIfWhiteSpace(itemContext.ComponentHierarchy),
                parent_hierarchy = PromptItemClassifier.GetParentHierarchy(itemContext.ComponentHierarchy),
                component_type = NullIfWhiteSpace(itemContext.ComponentType),
                hints
            };
        var repairContext = JsonConvert.SerializeObject(
            new
            {
                game_title = NormalizeSingleLine(gameTitle),
                source = sourceText,
                invalid_translation = invalidTranslation,
                failure_reason = reason,
                item_context = context,
                same_parent_source_texts = sameParentSourceTexts ?? Array.Empty<string>()
            },
            Formatting.None);
        return ApplyTemplate(
            promptTemplates.Resolve(item => item.QualityRepairPrompt),
            new Dictionary<string, string>
            {
                ["SourceText"] = sourceText,
                ["InvalidTranslation"] = invalidTranslation,
                ["FailureReason"] = reason,
                ["RepairContextJson"] = repairContext,
                ["GameTitle"] = NormalizeSingleLine(gameTitle)
            });
    }

    private static IReadOnlyList<object> BuildItemHintRows(
        IReadOnlyList<string> protectedTexts,
        IReadOnlyList<PromptItemContext>? itemContexts,
        string? gameTitle)
    {
        var contextByIndex = itemContexts == null
            ? new Dictionary<int, PromptItemContext>()
            : itemContexts.GroupBy(context => context.TextIndex).ToDictionary(group => group.Key, group => group.First());
        var rows = new List<object>();
        for (var i = 0; i < protectedTexts.Count; i++)
        {
            contextByIndex.TryGetValue(i, out var context);
            var hints = PromptItemClassifier.BuildHints(protectedTexts[i], context, gameTitle);
            if (hints.Count == 0)
            {
                continue;
            }

            rows.Add(new
            {
                text_index = i,
                hints
            });
        }

        return rows;
    }

    public static string BuildGlossaryExtractionSystemPrompt(PromptTemplateConfig? templates = null)
    {
        var promptTemplates = (templates ?? PromptTemplateConfig.Empty).NormalizeAgainstDefaults();
        return promptTemplates.Resolve(item => item.GlossaryExtractionSystemPrompt);
    }

    public static string BuildGlossaryExtractionUserPrompt(
        IReadOnlyList<TranslationCacheEntry> rows,
        PromptTemplateConfig? templates = null)
    {
        var promptTemplates = (templates ?? PromptTemplateConfig.Empty).NormalizeAgainstDefaults();
        var payload = rows.Select(row => new
        {
            source = row.SourceText,
            translation = row.TranslatedText
        });
        return ApplyTemplate(
            promptTemplates.Resolve(item => item.GlossaryExtractionUserPrompt),
            new Dictionary<string, string>
            {
                ["RowsJson"] = JsonConvert.SerializeObject(payload, Formatting.None)
            });
    }

    private static string BuildStyleInstruction(TranslationStyle style)
    {
        return style switch
        {
            TranslationStyle.Faithful => "Style: Preserve the original meaning faithfully and avoid adding or removing information.",
            TranslationStyle.Natural => "Style: Use natural fluent language and avoid machine-translation phrasing.",
            TranslationStyle.Localized => "Style: Natural localization is allowed when it fits the game context and character voice.",
            TranslationStyle.UiConcise => "Style: Keep UI, menu, and button text short and clear.",
            _ => "Style: Be natural and accurate."
        };
    }

    private static string ApplyTemplate(string template, IReadOnlyDictionary<string, string> values)
    {
        var result = template;
        foreach (var pair in values)
        {
            result = result.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
        }

        return result.Trim();
    }

    private static string BuildGameContext(string? gameTitle)
    {
        var normalized = NormalizeSingleLine(gameTitle);
        return normalized.Length == 0
            ? string.Empty
            : $"Game title: {normalized}. Use this game's context for character names, locations, menus, short UI labels, and horror-game text.";
    }

    private static string NormalizeSingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? GetParentHierarchy(string? componentHierarchy)
    {
        var normalized = NullIfWhiteSpace(componentHierarchy);
        if (normalized == null)
        {
            return null;
        }

        var index = normalized.LastIndexOf('/');
        return index <= 0 ? null : normalized[..index];
    }

    private static string ResolveTargetLanguageName(string targetLanguage)
    {
        var normalized = targetLanguage.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "the target language";
        }

        return normalized.ToLowerInvariant() switch
        {
            "zh-hans" or "zh-cn" or "zh-sg" => "Simplified Chinese",
            "zh-hant" or "zh-tw" or "zh-hk" or "zh-mo" => "Traditional Chinese",
            "zh" => "Chinese",
            "ja" or "ja-jp" => "Japanese",
            "ko" or "ko-kr" => "Korean",
            "en" or "en-us" or "en-gb" => "English",
            "fr" or "fr-fr" => "French",
            "de" or "de-de" => "German",
            "es" or "es-es" => "Spanish",
            "pt" or "pt-pt" => "Portuguese",
            "pt-br" => "Brazilian Portuguese",
            "ru" or "ru-ru" => "Russian",
            "it" or "it-it" => "Italian",
            "th" or "th-th" => "Thai",
            "vi" or "vi-vn" => "Vietnamese",
            "id" or "id-id" => "Indonesian",
            "tr" or "tr-tr" => "Turkish",
            "ar" => "Arabic",
            _ => TryGetCultureEnglishName(normalized)
        };
    }

    private static string TryGetCultureEnglishName(string value)
    {
        try
        {
            return CultureInfo.GetCultureInfo(value).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return value;
        }
    }
}
