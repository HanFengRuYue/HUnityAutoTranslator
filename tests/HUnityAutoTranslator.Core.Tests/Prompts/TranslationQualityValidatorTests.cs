using FluentAssertions;
using HUnityAutoTranslator.Core.Prompts;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Tests.Prompts;

public sealed class TranslationQualityValidatorTests
{
    [Theory]
    [InlineData("v1.3.0")]
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
}
