using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace HUnityAutoTranslator.Core.Prompts;

public sealed record PromptTemplateConfig(
    string? SystemPrompt = null,
    string? GlossarySystemPolicy = null,
    string? BatchUserPrompt = null,
    string? GlossaryTermsSection = null,
    string? CurrentItemContextSection = null,
    string? ItemHintsSection = null,
    string? ContextExamplesSection = null,
    string? GlossaryRepairPrompt = null,
    string? QualityRepairPrompt = null,
    string? GlossaryExtractionSystemPrompt = null,
    string? GlossaryExtractionUserPrompt = null)
{
    public static PromptTemplateConfig Empty { get; } = new();

    public static PromptTemplateConfig Default { get; } = new(
        SystemPrompt: """
You are a game localization translation engine.
Target language: {TargetLanguage}.
{GameContext}
Treat character names, place names, menus, item labels, and short horror-game UI text according to the game's context.
For short UI text, infer the intended UI role before translating; avoid word-by-word fragments that are too short to be useful.
If the source text is or contains the game title, preserve the exact game title as a brand/title instead of translating it literally.
Accessibility and technical settings must stay distinct; do not collapse different options into the same wording.
Accessibility options must use established, specific accessibility terminology rather than broad labels such as generic color blindness.
For Simplified Chinese, do not leave ordinary English UI text untranslated unless it is a brand name, acronym, placeholder, or technical token that should stay unchanged.
For Simplified Chinese, output Simplified Chinese text; do not leave Japanese kana, Korean Hangul, or source-language rewrites in the translation unless they are part of a preserved game title or required proper noun.
Detect the source language automatically. Translate it into the target language.
Output only the translated text. Do not explain, greet, add quotes, add Markdown, or add prefixes such as "Translation:".
Do not add indexes, item numbers, source labels, list markers, or any copied batch labels to the translation.
Do not wrap short UI labels in quotes, brackets, book-title marks, corner brackets, or emphasis symbols unless the source text already has matching outer symbols.
Preserve UI marker symbols such as leading menu arrows, bullets, and decorative prefix/suffix markers exactly.
Preserve placeholders, control characters, line breaks, Unity rich text tags, and TextMeshPro tags exactly.
Use natural game localization. Keep menu and button text short; keep dialogue consistent with character voice.
{StyleInstruction}{GlossarySystemPolicy}
""",
        GlossarySystemPolicy: "Mandatory glossary policy: When a source string contains a glossary source term supplied in the user message, use the glossary target term exactly. Glossary terms outrank style guidance and translation context examples. Do not translate the glossary table itself, do not invent glossary terms, and do not add unused terms to unrelated strings.",
        BatchUserPrompt: "{PromptSections}Translate each string in the JSON array below. Return only a JSON string array with the same length and order. Do not split one input string into multiple output items, even when it contains line breaks. Do not return object keys, indexes, item numbers, labels, or list markers.\n{InputJson}",
        GlossaryTermsSection: "Mandatory glossary terms are provided below. For the input item at text_index, if that source term appears in the item, use the target term exactly and do not replace it with a synonym. Do not translate the glossary table and do not add unused glossary terms to items where the source term does not appear.\n{GlossaryTermsJson}",
        CurrentItemContextSection: "Current UI text context is provided below. Use text_index to match each input item. Use scene, component hierarchy, parent hierarchy, and sibling labels to disambiguate short UI words such as On, Off, Ultra, Back, Start, and Continue. Do not translate this context table and do not include it in the output.\n{ItemContextsJson}",
        ItemHintsSection: "Item translation hints are provided below. Treat hints as generic UI roles, not as a glossary. Use them to choose concise, natural, distinct game-localized wording.\n{ItemHintsJson}",
        ContextExamplesSection: "Translation context examples are provided only as reference for terminology, tone, and nearby dialogue. Do not translate the examples and do not include them in the output. These examples must not override mandatory glossary terms.\n{ContextExamplesJson}",
        GlossaryRepairPrompt: """
The previous translation result was invalid. Reason: {FailureReason}
Translate again. Output only the repaired translation, with no explanation.
Source text: {SourceText}
Invalid translation: {InvalidTranslation}
{RequiredGlossaryTermsBlock}
""",
        QualityRepairPrompt: """
The previous translation failed translation quality rules. Reason: {FailureReason}
Translate the source again for game UI localization. Output only the repaired translation, with no explanation.
Use the item context and same-parent source texts to keep short UI labels concise, natural, and distinct.
For Simplified Chinese, repair the output into Simplified Chinese; do not rewrite hiragana as katakana, leave Japanese kana, leave Korean Hangul, or return the source text unchanged unless it is only the preserved game title.
If the source text is or contains the game title, preserve the exact game title.
Do not add outer quotes, brackets, book-title marks, corner brackets, or emphasis symbols unless the source text already has matching outer symbols.
Preserve source UI marker symbols such as leading >, >>, -, *, bullet, or arrow prefixes and matching suffix markers exactly.
Source text: {SourceText}
Invalid translation: {InvalidTranslation}
Repair context:
{RepairContextJson}
""",
        GlossaryExtractionSystemPrompt: """
You extract game localization glossary terms from source and translated text pairs.
Return only a JSON array. Each item must contain source, target, and optional note.
Only include proper nouns, UI terms, item names, location names, skill names, or recurring world terms.
Do not include placeholders, rich text tags, pure symbols, whole sentences, or generic grammar words.
""",
        GlossaryExtractionUserPrompt: "{RowsJson}");

    public bool HasOverrides => Values().Any(value => !string.IsNullOrWhiteSpace(value));

    public PromptTemplateConfig NormalizeAgainstDefaults()
    {
        var defaults = Default;
        return new PromptTemplateConfig(
            Normalize(SystemPrompt, defaults.SystemPrompt),
            Normalize(GlossarySystemPolicy, defaults.GlossarySystemPolicy),
            Normalize(BatchUserPrompt, defaults.BatchUserPrompt, "{InputJson}"),
            Normalize(GlossaryTermsSection, defaults.GlossaryTermsSection, "{GlossaryTermsJson}"),
            Normalize(CurrentItemContextSection, defaults.CurrentItemContextSection, "{ItemContextsJson}"),
            Normalize(ItemHintsSection, defaults.ItemHintsSection, "{ItemHintsJson}"),
            Normalize(ContextExamplesSection, defaults.ContextExamplesSection, "{ContextExamplesJson}"),
            Normalize(GlossaryRepairPrompt, defaults.GlossaryRepairPrompt, "{SourceText}", "{InvalidTranslation}", "{FailureReason}"),
            Normalize(QualityRepairPrompt, defaults.QualityRepairPrompt, "{SourceText}", "{InvalidTranslation}", "{FailureReason}", "{RepairContextJson}"),
            Normalize(GlossaryExtractionSystemPrompt, defaults.GlossaryExtractionSystemPrompt),
            Normalize(GlossaryExtractionUserPrompt, defaults.GlossaryExtractionUserPrompt, "{RowsJson}"));
    }

    public string GetTemplateHash()
    {
        var normalized = NormalizeAgainstDefaults();
        if (!normalized.HasOverrides)
        {
            return string.Empty;
        }

        using var sha = SHA256.Create();
        var payload = JsonConvert.SerializeObject(normalized);
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return string.Concat(bytes.Select(item => item.ToString("x2"))).Substring(0, 12);
    }

    public string Resolve(Func<PromptTemplateConfig, string?> selector)
    {
        return Select(selector, this);
    }

    private static string Select(Func<PromptTemplateConfig, string?> selector, PromptTemplateConfig overrides)
    {
        var value = selector(overrides);
        return string.IsNullOrWhiteSpace(value)
            ? selector(Default) ?? string.Empty
            : value;
    }

    private static string? Normalize(string? value, string? defaultValue, params string[] requiredPlaceholders)
    {
        var normalized = NormalizeText(value);
        if (normalized.Length == 0 ||
            string.Equals(normalized, NormalizeText(defaultValue), StringComparison.Ordinal))
        {
            return null;
        }

        return requiredPlaceholders.All(placeholder => normalized.Contains(placeholder, StringComparison.Ordinal))
            ? normalized
            : null;
    }

    private static string NormalizeText(string? value)
    {
        return (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
    }

    private IEnumerable<string?> Values()
    {
        yield return SystemPrompt;
        yield return GlossarySystemPolicy;
        yield return BatchUserPrompt;
        yield return GlossaryTermsSection;
        yield return CurrentItemContextSection;
        yield return ItemHintsSection;
        yield return ContextExamplesSection;
        yield return GlossaryRepairPrompt;
        yield return QualityRepairPrompt;
        yield return GlossaryExtractionSystemPrompt;
        yield return GlossaryExtractionUserPrompt;
    }
}
