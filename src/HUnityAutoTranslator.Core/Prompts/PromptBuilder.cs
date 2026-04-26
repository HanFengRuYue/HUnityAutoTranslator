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
        if (!string.IsNullOrWhiteSpace(options.CustomPrompt))
        {
            var customPrompt = ApplyPromptVariables(options.CustomPrompt.Trim(), targetLanguageName, style);
            return options.HasGlossaryTerms
                ? customPrompt + "\n" + BuildGlossarySystemPolicy()
                : customPrompt;
        }

        var custom = string.IsNullOrWhiteSpace(options.CustomInstruction)
            ? string.Empty
            : "\nAdditional style requirement: " + options.CustomInstruction.Trim();

        var glossary = options.HasGlossaryTerms ? "\n" + BuildGlossarySystemPolicy() : string.Empty;
        return $"""
You are a game localization translation engine.
Target language: {targetLanguageName}.
Detect the source language automatically. Translate it into the target language.
Output only the translated text. Do not explain, greet, add quotes, add Markdown, or add prefixes such as "Translation:".
Do not add indexes, item numbers, source labels, list markers, or any copied batch labels to the translation.
Preserve placeholders, control characters, line breaks, Unity rich text tags, and TextMeshPro tags exactly.
Use natural game localization. Keep menu and button text short; keep dialogue consistent with character voice.
{style}{custom}{glossary}
""";
    }

    public static string BuildDefaultSystemPrompt(string targetLanguage, TranslationStyle style, string? customInstruction = null)
    {
        return BuildSystemPrompt(new PromptOptions(targetLanguage, style, customInstruction));
    }

    public static string BuildSingleUserPrompt(string protectedText)
    {
        return "Translate the following text. Return only the translation:\n" + protectedText;
    }

    public static string BuildBatchUserPrompt(
        IReadOnlyList<string> protectedTexts,
        IReadOnlyList<TranslationContextExample>? contextExamples = null,
        IReadOnlyList<GlossaryPromptTerm>? glossaryTerms = null)
    {
        var json = JsonConvert.SerializeObject(protectedTexts, Formatting.None);
        var instruction = "Translate each string in the JSON array below. Return only a JSON string array with the same length and order. Do not return object keys, indexes, item numbers, labels, or list markers.";
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
            sections.Add("Mandatory glossary terms are provided below. For the input item at text_index, if that source term appears in the item, use the target term exactly and do not replace it with a synonym. Do not translate the glossary table and do not add unused glossary terms to items where the source term does not appear.\n" + glossaryJson);
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
            sections.Add("Translation context examples are provided only as reference for terminology, tone, and nearby dialogue. Do not translate the examples and do not include them in the output. These examples must not override mandatory glossary terms.\n" + examplesJson);
        }

        return (sections.Count == 0 ? string.Empty : string.Join("\n", sections) + "\n")
            + instruction
            + "\n"
            + json;
    }

    public static string BuildRepairPrompt(
        string sourceText,
        string invalidTranslation,
        string reason,
        IReadOnlyList<GlossaryPromptTerm>? glossaryTerms = null)
    {
        var glossary = glossaryTerms == null || glossaryTerms.Count == 0
            ? string.Empty
            : "\nRequired glossary terms:\n" + JsonConvert.SerializeObject(
                glossaryTerms.Select(term => new
                {
                    source = term.SourceTerm,
                    target = term.TargetTerm,
                    note = term.Note
                }),
                Formatting.None);
        return $"""
The previous translation result was invalid. Reason: {reason}
Translate again. Output only the repaired translation, with no explanation.
Source text: {sourceText}
Invalid translation: {invalidTranslation}
{glossary}
""";
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

    private static string ApplyPromptVariables(string prompt, string targetLanguageName, string styleInstruction)
    {
        return prompt
            .Replace("{TargetLanguage}", targetLanguageName, StringComparison.Ordinal)
            .Replace("{StyleInstruction}", styleInstruction, StringComparison.Ordinal);
    }

    private static string BuildGlossarySystemPolicy()
    {
        return "Mandatory glossary policy: When a source string contains a glossary source term supplied in the user message, use the glossary target term exactly. Glossary terms outrank style guidance and translation context examples. Do not translate the glossary table itself, do not invent glossary terms, and do not add unused terms to unrelated strings.";
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
