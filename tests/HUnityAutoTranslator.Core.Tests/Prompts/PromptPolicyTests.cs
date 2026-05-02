using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Glossary;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Tests.Prompts;

public sealed class PromptPolicyTests
{
    [Fact]
    public void BuildSystemPrompt_contains_hard_rules_and_style()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new PromptOptions("zh-Hans", TranslationStyle.Localized));

        prompt.Should().Contain("You are a game localization translation engine.");
        prompt.Should().Contain("Target language: Simplified Chinese.");
        prompt.Should().Contain("Detect the source language automatically.");
        prompt.Should().Contain("Output only the translated text.");
        prompt.Should().Contain("Do not add indexes");
        prompt.Should().Contain("Style: Natural localization is allowed");
        prompt.Should().NotContain("zh-Hans");
        prompt.Should().NotContain("目标语言");
    }

    [Fact]
    public void Prompt_policy_version_is_v5_after_per_character_rich_text_rebuild()
    {
        TextPipeline.PromptPolicyVersion.Should().Be("prompt-v5");
    }

    [Fact]
    public void BuildSystemPrompt_contains_quality_rules_for_short_ui_and_game_titles()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new PromptOptions(
            "zh-Hans",
            TranslationStyle.Localized,
            GameTitle: "The Glitched Attraction"));

        prompt.Should().Contain("For short UI text, infer the intended UI role before translating");
        prompt.Should().Contain("If the source text is or contains the game title, preserve the exact game title");
        prompt.Should().Contain("Accessibility and technical settings must stay distinct");
        prompt.Should().Contain("For Simplified Chinese, do not leave ordinary English UI text untranslated");
        prompt.Should().Contain("Preserve UI marker symbols");
    }

    [Fact]
    public void BuildSystemPrompt_includes_game_title_when_available()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new PromptOptions(
            "zh-Hans",
            TranslationStyle.Localized,
            GameTitle: "The Glitched Attraction"));

        prompt.Should().Contain("Game title: The Glitched Attraction.");
        prompt.Should().Contain("Use this game's context for character names, locations, menus, short UI labels, and horror-game text.");
    }

    [Fact]
    public void BuildSystemPrompt_omits_game_title_when_blank()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new PromptOptions(
            "zh-Hans",
            TranslationStyle.Localized,
            GameTitle: " "));

        prompt.Should().NotContain("Game title:");
    }

    [Fact]
    public void BuildSystemPrompt_uses_full_custom_prompt_when_configured()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new PromptOptions(
            TargetLanguage: "ja",
            Style: TranslationStyle.UiConcise,
            CustomPrompt: "Output {TargetLanguage}. Style={StyleInstruction}"));

        prompt.Should().Be("Output Japanese. Style=Style: Keep UI, menu, and button text short and clear.");
    }

    [Fact]
    public void BuildSystemPrompt_custom_prompt_can_use_game_title_variable()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new PromptOptions(
            TargetLanguage: "zh-Hans",
            Style: TranslationStyle.Localized,
            CustomPrompt: "Translate {GameTitle} into {TargetLanguage}. {GameContext}",
            GameTitle: "The Glitched Attraction"));

        prompt.Should().Contain("Translate The Glitched Attraction into Simplified Chinese.");
        prompt.Should().Contain("Game title: The Glitched Attraction.");
    }

    [Fact]
    public void BuildSystemPrompt_appends_mandatory_glossary_policy_to_custom_prompt()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new PromptOptions(
            TargetLanguage: "ja",
            Style: TranslationStyle.UiConcise,
            CustomPrompt: "Output {TargetLanguage}.",
            HasGlossaryTerms: true));

        prompt.Should().StartWith("Output Japanese.");
        prompt.Should().Contain("Mandatory glossary policy");
        prompt.Should().Contain("use the glossary target term exactly");
    }

    [Fact]
    public void BuildSystemPrompt_uses_custom_system_and_glossary_policy_templates()
    {
        var templates = new PromptTemplateConfig(
            SystemPrompt: "SYS {TargetLanguage} {GameTitle} {GameContext} {StyleInstruction} {GlossarySystemPolicy}",
            GlossarySystemPolicy: "GLOSSARY POLICY");

        var prompt = PromptBuilder.BuildSystemPrompt(new PromptOptions(
            TargetLanguage: "zh-Hans",
            Style: TranslationStyle.UiConcise,
            HasGlossaryTerms: true,
            GameTitle: "The Glitched Attraction",
            Templates: templates));

        prompt.Should().Contain("SYS Simplified Chinese The Glitched Attraction Game title: The Glitched Attraction.");
        prompt.Should().Contain("Style: Keep UI, menu, and button text short and clear.");
        prompt.Should().Contain("GLOSSARY POLICY");
    }

    [Fact]
    public void BuildBatchUserPrompt_uses_json_input_without_numeric_labels()
    {
        var prompt = PromptBuilder.BuildBatchUserPrompt(new[] { "CONTINUE", "Off" });

        prompt.Should().Contain("Return only a JSON string array");
        prompt.Should().Contain("Do not split one input string into multiple output items");
        prompt.Should().Contain("""["CONTINUE","Off"]""");
        prompt.Should().NotContain("0:");
        prompt.Should().NotContain("1:");
    }

    [Fact]
    public void BuildBatchUserPrompt_includes_translation_context_examples_without_changing_output_contract()
    {
        var prompt = PromptBuilder.BuildBatchUserPrompt(
            new[] { "Open the gate" },
            new[] { new TranslationContextExample("Take the key", "Take translated") });

        prompt.Should().Contain("Translation context examples");
        prompt.Should().Contain("Take the key");
        prompt.Should().Contain("Take translated");
        prompt.Should().Contain("Return only a JSON string array");
        prompt.Should().Contain("""["Open the gate"]""");
    }

    [Fact]
    public void BuildBatchUserPrompt_includes_current_item_context_with_text_indexes()
    {
        var prompt = PromptBuilder.BuildBatchUserPrompt(
            new[] { "Ultra", "On" },
            contextExamples: null,
            glossaryTerms: null,
            itemContexts: new[]
            {
                new PromptItemContext(0, "MainMenu", "Canvas/Settings/Textures/Quality", "TMPro.TextMeshProUGUI"),
                new PromptItemContext(1, "MainMenu", "Canvas/Settings/Toggles/FullScreen", "UnityEngine.UI.Text")
            },
            gameTitle: "The Glitched Attraction");

        prompt.Should().Contain("Current UI text context");
        prompt.Should().Contain("\"text_index\":0");
        prompt.Should().Contain("\"component_hierarchy\":\"Canvas/Settings/Textures/Quality\"");
        prompt.Should().Contain("\"text_index\":1");
        prompt.Should().Contain("\"scene\":\"MainMenu\"");
        prompt.Should().Contain("Item translation hints");
        prompt.Should().Contain("\"settings_value\"");
        prompt.Should().Contain("\"toggle_state\"");
        prompt.Should().Contain("""["Ultra","On"]""");
    }

    [Fact]
    public void BuildBatchUserPrompt_marks_game_title_and_accessibility_hints()
    {
        var prompt = PromptBuilder.BuildBatchUserPrompt(
            new[] { "The Glitched\nAttraction", "Tritanopia" },
            itemContexts: new[]
            {
                new PromptItemContext(0, "Main Menu", "Menu/Camera/World Canvas/Panels/Main/Title", "TMPro.TextMeshProUGUI"),
                new PromptItemContext(1, "SetSettings_FirstTime", "Canvas/Options/Tritanopia", "UnityEngine.UI.Text")
            },
            gameTitle: "The Glitched Attraction");

        prompt.Should().Contain("\"game_title\"");
        prompt.Should().Contain("\"accessibility_option\"");
        prompt.Should().Contain("\"title_text\"");
    }

    [Fact]
    public void BuildBatchUserPrompt_includes_glossary_terms_before_context_examples()
    {
        var prompt = PromptBuilder.BuildBatchUserPrompt(
            new[] { "Find Freddy" },
            new[] { new TranslationContextExample("Take the key", "Take translated") },
            new[] { new GlossaryPromptTerm(0, "Freddy", "弗雷迪", "角色名") });

        prompt.Should().Contain("Mandatory glossary terms");
        prompt.Should().Contain("Freddy");
        prompt.Should().Contain("弗雷迪");
        prompt.IndexOf("Mandatory glossary terms", StringComparison.Ordinal)
            .Should().BeLessThan(prompt.IndexOf("Translation context examples", StringComparison.Ordinal));
        prompt.Should().Contain("Return only a JSON string array");
        prompt.Should().Contain("""["Find Freddy"]""");
    }

    [Fact]
    public void BuildBatchUserPrompt_uses_custom_section_templates()
    {
        var templates = new PromptTemplateConfig(
            BatchUserPrompt: "SECTIONS\n{PromptSections}INPUT\n{InputJson}",
            GlossaryTermsSection: "TERMS {GlossaryTermsJson}",
            CurrentItemContextSection: "ITEMS {ItemContextsJson}",
            ItemHintsSection: "HINTS {ItemHintsJson}",
            ContextExamplesSection: "EXAMPLES {ContextExamplesJson}");

        var prompt = PromptBuilder.BuildBatchUserPrompt(
            new[] { "Ultra" },
            new[] { new TranslationContextExample("Textures", "纹理") },
            new[] { new GlossaryPromptTerm(0, "Ultra", "极高", "画质") },
            new[] { new PromptItemContext(0, "MainMenu", "Canvas/Settings/Textures/Quality", "TMPro.TextMeshProUGUI") },
            "The Glitched Attraction",
            templates);

        prompt.Should().StartWith("SECTIONS");
        prompt.Should().Contain("TERMS [{\"text_index\":0,\"source\":\"Ultra\",\"target\":\"极高\",\"note\":\"画质\"}]");
        prompt.Should().Contain("ITEMS [{\"text_index\":0,\"scene\":\"MainMenu\"");
        prompt.Should().Contain("HINTS [{\"text_index\":0");
        prompt.Should().Contain("EXAMPLES [{\"source\":\"Textures\",\"translation\":\"纹理\"}]");
        prompt.Should().Contain("INPUT\n[\"Ultra\"]");
    }

    [Fact]
    public void Repair_prompts_use_custom_templates()
    {
        var templates = new PromptTemplateConfig(
            GlossaryRepairPrompt: "GLOSSARY FIX {SourceText}|{InvalidTranslation}|{FailureReason}|{RequiredGlossaryTermsJson}",
            QualityRepairPrompt: "QUALITY FIX {SourceText}|{InvalidTranslation}|{FailureReason}|{RepairContextJson}");

        var glossaryPrompt = PromptBuilder.BuildRepairPrompt(
            "Find Freddy",
            "寻找弗雷德",
            "missing term",
            new[] { new GlossaryPromptTerm(0, "Freddy", "弗雷迪", "角色名") },
            templates);
        var qualityPrompt = PromptBuilder.BuildQualityRepairPrompt(
            "Ultra",
            "超",
            "too short",
            new PromptItemContext(0, "MainMenu", "Canvas/Settings/Textures/Quality", "TMPro.TextMeshProUGUI"),
            new[] { "Low", "High" },
            "The Glitched Attraction",
            templates);

        glossaryPrompt.Should().Be("GLOSSARY FIX Find Freddy|寻找弗雷德|missing term|[{\"source\":\"Freddy\",\"target\":\"弗雷迪\",\"note\":\"角色名\"}]");
        qualityPrompt.Should().StartWith("QUALITY FIX Ultra|超|too short|{");
        qualityPrompt.Should().Contain("\"same_parent_source_texts\":[\"Low\",\"High\"]");
    }

    [Fact]
    public void Default_prompt_templates_contain_required_placeholders()
    {
        var defaults = PromptTemplateConfig.Default;
        var requiredTemplates = new (string Name, string? Template, string[] Placeholders)[]
        {
            ("BatchUserPrompt", defaults.BatchUserPrompt, new[] { "{InputJson}" }),
            ("GlossaryTermsSection", defaults.GlossaryTermsSection, new[] { "{GlossaryTermsJson}" }),
            ("CurrentItemContextSection", defaults.CurrentItemContextSection, new[] { "{ItemContextsJson}" }),
            ("ItemHintsSection", defaults.ItemHintsSection, new[] { "{ItemHintsJson}" }),
            ("ContextExamplesSection", defaults.ContextExamplesSection, new[] { "{ContextExamplesJson}" }),
            ("GlossaryRepairPrompt", defaults.GlossaryRepairPrompt, new[] { "{SourceText}", "{InvalidTranslation}", "{FailureReason}" }),
            ("QualityRepairPrompt", defaults.QualityRepairPrompt, new[] { "{SourceText}", "{InvalidTranslation}", "{FailureReason}", "{RepairContextJson}" }),
            ("GlossaryExtractionUserPrompt", defaults.GlossaryExtractionUserPrompt, new[] { "{RowsJson}" })
        };

        foreach (var (name, template, placeholders) in requiredTemplates)
        {
            template.Should().NotBeNullOrWhiteSpace(name);
            foreach (var placeholder in placeholders)
            {
                template.Should().Contain(placeholder, name);
            }
        }
    }

    [Fact]
    public void Quality_validator_rejects_observed_bad_translations()
    {
        var contexts = new[]
        {
            new PromptItemContext(0, "Main Menu", "Menu/Camera/World Canvas/Panels/Main/Title", "TMPro.TextMeshProUGUI"),
            new PromptItemContext(1, "Main Menu", "Menu/Camera/Canvas/Settings Menu/Gameplay Panel/Textures/Text", "TMPro.TextMeshProUGUI"),
            new PromptItemContext(2, "Main Menu", "Menu/Camera/Canvas/Settings Menu/Gameplay Panel/Shadows/Text", "TMPro.TextMeshProUGUI"),
            new PromptItemContext(3, "Main Menu", "Menu/Camera/Canvas/Settings Menu/Gameplay Panel/Post Processing/Text", "TMPro.TextMeshProUGUI"),
            new PromptItemContext(4, "SetSettings_FirstTime", "Canvas/Options/Tritanopia", "UnityEngine.UI.Text")
        };

        var failures = TranslationQualityValidator.FindFailures(
            new[] { "The Glitched\nAttraction", "Ultra", "Good", "Activated", "Tritanopia" },
            new[] { "故障吸引力", "超", "好", "已激活", "Tritanopia" },
            contexts,
            "zh-Hans",
            "The Glitched Attraction");

        failures.Select(failure => failure.TextIndex).Should().BeEquivalentTo(new[] { 0, 1, 2, 3, 4 });
        failures.Should().Contain(failure => failure.Reason.Contains("game title", StringComparison.OrdinalIgnoreCase));
        failures.Should().Contain(failure => failure.Reason.Contains("too short", StringComparison.OrdinalIgnoreCase));
        failures.Should().Contain(failure => failure.Reason.Contains("untranslated", StringComparison.OrdinalIgnoreCase));
        failures.Should().Contain(failure => failure.Reason.Contains("state", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Quality_validator_allows_short_keyboard_tokens_to_remain_untranslated()
    {
        var failures = TranslationQualityValidator.FindFailures(
            new[] { "ESC", "FPS", "F1", "UI" },
            new[] { "ESC", "FPS", "F1", "UI" },
            new[]
            {
                new PromptItemContext(0, "Settings", "Canvas/Settings/Menu/Keyboard/Escape", "UnityEngine.UI.Text"),
                new PromptItemContext(1, "Settings", "Canvas/Settings/Graphics/FpsCounter", "UnityEngine.UI.Text"),
                new PromptItemContext(2, "Settings", "Canvas/Settings/Keyboard/FunctionKey", "UnityEngine.UI.Text"),
                new PromptItemContext(3, "Settings", "Canvas/Settings/Screen/UiScale", "UnityEngine.UI.Text")
            },
            "zh-Hans",
            "The Glitched Attraction");

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Quality_validator_still_rejects_untranslated_ordinary_or_mixed_ui_text()
    {
        var failures = TranslationQualityValidator.FindFailures(
            new[] { "SFX Volume", "H\u3048\u3075\u3047\u304f\u3068", "Level2" },
            new[] { "SFX Volume", "H\u3048\u3075\u3047\u304f\u3068", "Level2" },
            new[]
            {
                new PromptItemContext(0, "Settings", "Canvas/Settings/Audio/SFX Volume", "UnityEngine.UI.Text"),
                new PromptItemContext(1, "Settings", "Canvas/Settings/H Effect", "UnityEngine.UI.Text"),
                new PromptItemContext(2, "LevelSelect", "Canvas/Menu/Level2", "UnityEngine.UI.Text")
            },
            "zh-Hans",
            "The Glitched Attraction");

        failures.Select(failure => failure.TextIndex).Should().BeEquivalentTo(new[] { 0, 1, 2 });
        failures.Should().OnlyContain(failure => failure.Reason.Contains("untranslated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Quality_validator_rejects_same_group_option_collisions()
    {
        var failures = TranslationQualityValidator.FindFailures(
            new[] { "Option Alpha", "Option Beta" },
            new[] { "相同选项", "相同选项" },
            new[]
            {
                new PromptItemContext(0, "Settings", "Canvas/Options/OptionAlpha", "UnityEngine.UI.Text"),
                new PromptItemContext(1, "Settings", "Canvas/Options/OptionBeta", "UnityEngine.UI.Text")
            },
            "zh-Hans",
            "The Glitched Attraction");

        failures.Should().ContainSingle();
        failures[0].TextIndex.Should().Be(1);
        failures[0].Reason.Should().Contain("same parent");
    }

    [Fact]
    public void Quality_validator_does_not_special_case_color_vision_mode_translations()
    {
        var failures = TranslationQualityValidator.FindFailures(
            new[] { "Protanopia", "Deuteranopia", "Tritanopia" },
            new[] { "\u5168\u8272\u76f2\u6a21\u5f0f", "\u4e8c\u8272\u89c6", "\u539f\u8272\u76f2\u6a21\u5f0f" },
            new[]
            {
                new PromptItemContext(0, "SetSettings_FirstTime", "Canvas/Options/Protanopia", "UnityEngine.UI.Text"),
                new PromptItemContext(1, "SetSettings_FirstTime", "Canvas/Options/Deuteranopia", "UnityEngine.UI.Text"),
                new PromptItemContext(2, "SetSettings_FirstTime", "Canvas/Options/Tritanopia", "UnityEngine.UI.Text")
            },
            "zh-Hans",
            "The Glitched Attraction");

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Quality_validator_rejects_generated_outer_symbols()
    {
        var failures = TranslationQualityValidator.FindFailures(
            new[] { "Settings", "IMPORTANT" },
            new[] { "\u0022\u8bbe\u7f6e\u0022", "\u3010\u91cd\u8981\u63d0\u793a\u3011" },
            new[]
            {
                new PromptItemContext(0, "Main Menu", "Menu/Camera/Canvas/Settings Menu/Main/Title", "TMPro.TextMeshProUGUI"),
                new PromptItemContext(1, "Disclaimer", "Canvas/Text (Legacy)", "UnityEngine.UI.Text")
            },
            "zh-Hans",
            "The Glitched Attraction");

        failures.Select(failure => failure.TextIndex).Should().BeEquivalentTo(new[] { 0, 1 });
        failures.Should().OnlyContain(failure => failure.Reason.Contains("outer symbols", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Quality_validator_allows_localized_outer_symbols_when_source_has_wrapper()
    {
        var failures = TranslationQualityValidator.FindFailures(
            new[] { "[IMPORTANT]" },
            new[] { "\u3010\u91cd\u8981\u63d0\u793a\u3011" },
            new[]
            {
                new PromptItemContext(0, "Disclaimer", "Canvas/Text (Legacy)", "UnityEngine.UI.Text")
            },
            "zh-Hans",
            "The Glitched Attraction");

        failures.Should().BeEmpty();
    }

    [Theory]
    [InlineData("v81", "\u5bf9\u5e94 v81 \u7248\u672c\u53f7")]
    [InlineData("HUnityAutoTranslator.Plugin.dll", "\u63d2\u4ef6\u6587\u4ef6")]
    public void Validator_rejects_overtranslated_preservable_identifiers(string sourceText, string translatedText)
    {
        var result = TranslationOutputValidator.ValidateSingle(
            sourceText,
            translatedText,
            requireSameRichTextTags: true);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("must remain unchanged");
    }

    [Theory]
    [InlineData("v81")]
    [InlineData("HUnityAutoTranslator.Plugin.dll")]
    public void Validator_allows_preservable_identifiers_to_remain_unchanged(string sourceText)
    {
        var result = TranslationOutputValidator.ValidateSingle(
            sourceText,
            sourceText,
            requireSameRichTextTags: true);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Quality_validator_rejects_missing_ui_marker_symbols()
    {
        var failures = TranslationQualityValidator.FindFailures(
            new[] { "> Join a crew" },
            new[] { "\u52a0\u5165\u961f\u4f0d" },
            new[]
            {
                new PromptItemContext(0, "Lobby", "Canvas/Menu/JoinCrew", "UnityEngine.UI.Text")
            },
            "zh-Hans",
            "The Glitched Attraction");

        failures.Should().ContainSingle();
        failures[0].TextIndex.Should().Be(0);
        failures[0].Reason.Should().Contain("UI marker symbols");
    }

    [Fact]
    public void Quality_validator_allows_preserved_ui_marker_symbols()
    {
        var failures = TranslationQualityValidator.FindFailures(
            new[] { "> Join a crew" },
            new[] { "> \u52a0\u5165\u961f\u4f0d" },
            new[]
            {
                new PromptItemContext(0, "Lobby", "Canvas/Menu/JoinCrew", "UnityEngine.UI.Text")
            },
            "zh-Hans",
            "The Glitched Attraction");

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Glossary_validator_rejects_missing_required_target_term()
    {
        var result = GlossaryOutputValidator.ValidateSingle(
            "Find Freddy",
            "找到佛莱迪",
            new[] { new GlossaryPromptTerm(0, "Freddy", "弗雷迪", null) });

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("Freddy");
        result.Reason.Should().Contain("弗雷迪");
    }

    [Fact]
    public void Validator_rejects_explanatory_prefix_and_broken_placeholders()
    {
        var result = TranslationOutputValidator.ValidateSingle(
            "You have {0} coins.",
            "翻译如下：你有 0 枚金币。",
            requireSameRichTextTags: true);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("解释性前缀");
    }

    [Fact]
    public void Validator_rejects_numbered_prefix_that_came_from_batch_prompt()
    {
        var result = TranslationOutputValidator.ValidateSingle(
            "Off",
            "0: 关",
            requireSameRichTextTags: true);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("编号前缀");
    }
}
