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
Accessibility and technical settings must stay distinct; do not collapse different options into the same wording.
Accessibility options must use established, specific accessibility terminology rather than broad labels such as generic color blindness.
For Simplified Chinese, do not leave ordinary English UI text untranslated unless it is a brand name, acronym, placeholder, or technical token that should stay unchanged.
For Simplified Chinese, output Simplified Chinese text; do not leave Japanese kana, Korean Hangul, or source-language rewrites in the translation unless they are required proper nouns.
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
        CurrentItemContextSection: "Current UI text context is provided below. Use text_index to match each input item. Use scene, setting group hierarchy, option container hierarchy, component hierarchy, parent hierarchy, and sibling labels to disambiguate short UI words such as On, Off, Ultra, Back, Start, and Continue. Use the same translation for the same short state text inside the same scene and setting group. For Simplified Chinese switch states, prefer \u5f00\u542f/\u5173\u95ed instead of one-character \u5f00/\u5173. Do not translate this context table and do not include it in the output.\n{ItemContextsJson}",
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
For Simplified Chinese, repair the output into Simplified Chinese; do not rewrite hiragana as katakana, leave Japanese kana, leave Korean Hangul, or return the source text unchanged.
Do not add outer quotes, brackets, book-title marks, corner brackets, or emphasis symbols unless the source text already has matching outer symbols.
Preserve source UI marker symbols such as leading >, >>, -, *, bullet, or arrow prefixes and matching suffix markers exactly.
Source text: {SourceText}
Invalid translation: {InvalidTranslation}
Repair context:
{RepairContextJson}
""",
        GlossaryExtractionSystemPrompt: """
You extract reusable game-localization glossary terms from source/translation pairs.
The provided pairs come from the same UI component or the same dialogue context; use that shared context to spot recurring terms.
Return ONLY a JSON array. Each item must have "source", "target", and "note".

INCLUDE only stable, reusable terms: proper nouns (characters, bosses, places), item names, skill/ability names, faction names, and recurring world terminology.

EXCLUDE, even if they appear frequently:
- interjections, greetings, fillers, yes/no words, acknowledgements (e.g. Japanese はい いいえ うん そう, English yes no ok back)
- pronouns, particles, conjunctions, generic everyday verbs and adjectives
- whole sentences or sentence fragments
- placeholders, rich-text or markup tags, control tokens, pure punctuation or symbols
- any word whose natural translation legitimately changes with dialogue context

Rule of thumb: if the same source word could be naturally translated several different ways depending on the scene, it is NOT a glossary term. Prefer precision over recall: when in doubt, leave it out.

The "note" field must be exactly one of these fixed categories, copied verbatim:
角色名, Boss·敌人名, 地名, 物品名, 技能名, 阵营·组织名, UI文本, 世界观术语, 其他
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

        return requiredPlaceholders.All(placeholder => normalized.IndexOf(placeholder, StringComparison.Ordinal) >= 0)
            ? normalized
            : null;
    }

    private static string NormalizeText(string? value)
    {
        return (value ?? string.Empty).Replace("\r\n", "\n")
            .Replace("\r", "\n")
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
