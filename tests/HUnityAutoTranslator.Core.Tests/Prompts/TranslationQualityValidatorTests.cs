using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Prompts;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Tests.Prompts;

public sealed class TranslationQualityValidatorTests
{
    [Theory]
    [InlineData("v1.3.0")]
    [InlineData("v81")]
    [InlineData("V2")]
    [InlineData("version 81")]
    [InlineData("1.3.0")]
    [InlineData("2026.04.07")]
    [InlineData("by esreverreverse69")]
    [InlineData("esreverreverse69 <3")]
    [InlineData("@esreverreverse69")]
    [InlineData("https://example.com/mod")]
    [InlineData("support@example.com")]
    [InlineData(@"C:\Windows\Fonts\simhei.ttf")]
    [InlineData("HUnityAutoTranslator.Plugin.dll")]
    public void Text_filter_skips_identifiers_that_should_remain_untranslated(string sourceText)
    {
        TextFilter.ShouldTranslate(sourceText).Should().BeFalse();
    }

    [Theory]
    [InlineData("v1.3.0")]
    [InlineData("v81")]
    [InlineData("esreverreverse69 <3")]
    [InlineData("by esreverreverse69")]
    [InlineData("https://example.com/mod")]
    [InlineData(@"C:\Windows\Fonts\simhei.ttf")]
    [InlineData("esreverreverse69_MI.Cheats")]
    [InlineData("esreverreverse69_MI.Cheats v1.3.0")]
    public void Quality_validator_allows_preserved_identifiers_to_remain_unchanged(string sourceText)
    {
        var result = TranslationQualityValidator.ValidateBatch(
            new[] { sourceText },
            new[] { sourceText },
            itemContexts: null,
            targetLanguage: "zh-Hans",
            gameTitle: null);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Start Game")]
    [InlineData("Enable All")]
    public void Quality_validator_still_rejects_ordinary_english_ui_text_left_untranslated(string sourceText)
    {
        var result = TranslationQualityValidator.ValidateBatch(
            new[] { sourceText },
            new[] { sourceText },
            new[] { new PromptItemContext(0, "title_01", "Canvas/Menu/Button", "IMGUI") },
            "zh-Hans",
            gameTitle: null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be("ordinary English UI text was left untranslated");
    }

    [Fact]
    public void Quality_validator_rejects_translatable_source_text_left_unchanged_for_simplified_chinese()
    {
        var result = TranslationQualityValidator.ValidateBatch(
            new[] { "\u753b\u8cea\u30ec\u30d9\u30eb" },
            new[] { "\u753b\u8cea\u30ec\u30d9\u30eb" },
            new[] { new PromptItemContext(0, "Top Vertical", "Canvas/Settings/Graphic Level/Text", "TMPro.TMP_Text") },
            "zh-Hans",
            gameTitle: null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be("translatable source text was left untranslated");
    }

    [Theory]
    [InlineData("\u3052\u30fc\u3080\u305b\u3063\u3066\u3044", "\u30b2\u30fc\u30e0\u8a2d\u5b9a")]
    [InlineData("\u3050\u3089\u3075\u3043\u3063\u304f", "\u30b0\u30e9\u30d5\u30a3\u30c3\u30af")]
    public void Quality_validator_rejects_kana_left_in_simplified_chinese_translation(string sourceText, string translatedText)
    {
        var result = TranslationQualityValidator.ValidateBatch(
            new[] { sourceText },
            new[] { translatedText },
            new[] { new PromptItemContext(0, "Top Vertical", "Canvas/Settings/Visuals/Text", "TMPro.TMP_Text") },
            "zh-Hans",
            gameTitle: null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be("translation still contains source-language kana or Hangul text");
    }

    [Fact]
    public void Quality_validator_allows_ui_marker_symbol_when_translation_is_simplified_chinese()
    {
        var result = TranslationQualityValidator.ValidateBatch(
            new[] { "\u30fb\u30b7\u30fc\u30f3 Chapter1\u3092\u30af\u30ea\u30a2\r\n" },
            new[] { "\u30fb\u901a\u5173\u7b2c1\u7ae0\r\n" },
            new[] { new PromptItemContext(0, "Top Vertical", "Canvas/Story/Condition", "TMPro.TMP_Text") },
            "zh-Hans",
            gameTitle: null);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Quality_validator_rejects_untranslated_source_language_game_title()
    {
        const string gameTitle = "\u6c60\u888b\u30bb\u30af\u30b5\u30ed\u30a4\u30c9\u5973\u5b66\u5712";
        var result = TranslationQualityValidator.ValidateBatch(
            new[] { gameTitle },
            new[] { gameTitle },
            new[] { new PromptItemContext(0, "Top Vertical", "Canvas/Splash/Start Text", "TMPro.TMP_Text") },
            "zh-Hans",
            gameTitle);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be("translatable source text was left untranslated");
    }

    [Fact]
    public void Quality_validator_allows_simplified_chinese_source_with_short_technical_tokens()
    {
        var result = TranslationQualityValidator.ValidateBatch(
            new[] { "\u753b\u8d28 FPS" },
            new[] { "\u753b\u8d28 FPS" },
            new[] { new PromptItemContext(0, "Settings", "Canvas/Settings/Graphics/FpsCounter", "UnityEngine.UI.Text") },
            "zh-Hans",
            gameTitle: null,
            qualityConfig: TranslationQualityConfig.Default());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Quality_validator_still_rejects_simplified_chinese_source_with_translatable_latin_words()
    {
        var result = TranslationQualityValidator.ValidateBatch(
            new[] { "\u5f00\u59cb Game" },
            new[] { "\u5f00\u59cb Game" },
            new[] { new PromptItemContext(0, "MainMenu", "Canvas/Menu/StartButton", "UnityEngine.UI.Text") },
            "zh-Hans",
            gameTitle: null,
            qualityConfig: TranslationQualityConfig.Default());

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be("ordinary English UI text was left untranslated");
    }

    [Fact]
    public void Quality_validator_respects_individual_rule_switches()
    {
        var contexts = new[]
        {
            new PromptItemContext(0, "Main Menu", "Menu/Camera/World Canvas/Panels/Main/Title", "TMPro.TextMeshProUGUI"),
            new PromptItemContext(1, "Main Menu", "Menu/Camera/Canvas/Settings Menu/Main/Title", "TMPro.TextMeshProUGUI"),
            new PromptItemContext(2, "Main Menu", "Menu/Camera/Canvas/Main/Menu/Start", "UnityEngine.UI.Text"),
            new PromptItemContext(3, "Main Menu", "Menu/Camera/Canvas/Settings Menu/Gameplay Panel/Textures/Text", "TMPro.TextMeshProUGUI"),
            new PromptItemContext(4, "Main Menu", "Menu/Camera/Canvas/Settings Menu/Gameplay Panel/Post Processing/Text", "TMPro.TextMeshProUGUI"),
            new PromptItemContext(5, "Settings", "Canvas/Options/OptionAlpha", "UnityEngine.UI.Text"),
            new PromptItemContext(6, "Settings", "Canvas/Options/OptionBeta", "UnityEngine.UI.Text")
        };
        var config = TranslationQualityConfig.Default() with
        {
            Mode = "custom",
            RejectGeneratedOuterSymbols = false,
            RejectUntranslatedLatinUiText = false,
            RejectShortSettingValue = false,
            RejectLiteralStateTranslation = false,
            RejectSameParentOptionCollision = false
        };

        var failures = TranslationQualityValidator.FindFailures(
            new[] { "The Glitched Attraction", "Settings", "Start Game", "Ultra", "Activated", "Option Alpha", "Option Beta" },
            new[] { "\u6545\u969c\u5438\u5f15\u529b", "\"\u8bbe\u7f6e\"", "Start Game", "\u8d85", "\u5df2\u6fc0\u6d3b", "\u76f8\u540c\u9009\u9879", "\u76f8\u540c\u9009\u9879" },
            contexts,
            "zh-Hans",
            "The Glitched Attraction",
            config);

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Quality_validator_returns_no_failures_when_quality_check_is_disabled()
    {
        var result = TranslationQualityValidator.ValidateBatch(
            new[] { "Ultra" },
            new[] { "\u8d85" },
            new[] { new PromptItemContext(0, "Settings", "Canvas/Settings/Graphics/Quality", "UnityEngine.UI.Text") },
            "zh-Hans",
            gameTitle: null,
            qualityConfig: TranslationQualityConfig.Default() with { Enabled = false });

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("On", "\u5f00")]
    [InlineData("Off", "\u5173")]
    public void Quality_validator_rejects_single_character_switch_state_translations(string sourceText, string translatedText)
    {
        var result = TranslationQualityValidator.ValidateBatch(
            new[] { sourceText },
            new[] { translatedText },
            new[] { new PromptItemContext(
                0,
                "Top Vertical",
                "Canvas - Main Menu/Main Content/Settings (1)/Content/Panel Content/Panels/General/Content/List/Layout Group/Auto Message Lady/Switch/Label On",
                "TMPro.TMP_Text") },
            "zh-Hans",
            gameTitle: "\u6c60\u888b\u30bb\u30af\u30b5\u30ed\u30a4\u30c9\u5973\u5b66\u5712");

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be("switch state translation is too short or inconsistent");
    }

    [Fact]
    public void Quality_validator_rejects_same_source_switch_state_variants_in_same_setting_group()
    {
        var failures = TranslationQualityValidator.FindFailures(
            new[] { "On", "On", "Off", "Off" },
            new[] { "\u5f00\u542f", "\u5f00", "\u5173\u95ed", "\u5173" },
            new[]
            {
                new PromptItemContext(0, "Top Vertical", "Canvas - Main Menu/Main Content/Settings (1)/Content/Panel Content/Panels/General/Content/List/Layout Group/Auto Message Lady/Switch/Label On", "TMPro.TMP_Text"),
                new PromptItemContext(1, "Top Vertical", "Canvas - Main Menu/Main Content/Settings (1)/Content/Panel Content/Panels/General/Content/List/Layout Group/Particle Effect Enable/Switch/Label On", "TMPro.TMP_Text"),
                new PromptItemContext(2, "Top Vertical", "Canvas - Main Menu/Main Content/Settings (1)/Content/Panel Content/Panels/General/Content/List/Layout Group/Auto Message Lady/Switch/Label Off", "TMPro.TMP_Text"),
                new PromptItemContext(3, "Top Vertical", "Canvas - Main Menu/Main Content/Settings (1)/Content/Panel Content/Panels/General/Content/List/Layout Group/Particle Effect Enable/Switch/Label Off", "TMPro.TMP_Text")
            },
            "zh-Hans",
            "\u6c60\u888b\u30bb\u30af\u30b5\u30ed\u30a4\u30c9\u5973\u5b66\u5712");

        failures.Should().Contain(failure =>
            failure.TextIndex == 1 &&
            failure.Reason == "same source switch states under the same setting group produced different translations");
        failures.Should().Contain(failure =>
            failure.TextIndex == 3 &&
            failure.Reason == "same source switch states under the same setting group produced different translations");
    }
}
