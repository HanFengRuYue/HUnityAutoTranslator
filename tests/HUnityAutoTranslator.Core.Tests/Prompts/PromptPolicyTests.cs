using FluentAssertions;
using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Tests.Prompts;

public sealed class PromptPolicyTests
{
    [Fact]
    public void BuildSystemPrompt_contains_hard_rules_and_style()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new PromptOptions("zh-Hans", TranslationStyle.Localized, "Keep character voice"));

        prompt.Should().Contain("You are a game localization translation engine.");
        prompt.Should().Contain("Target language: Simplified Chinese.");
        prompt.Should().Contain("Detect the source language automatically.");
        prompt.Should().Contain("Output only the translated text.");
        prompt.Should().Contain("Do not add indexes");
        prompt.Should().Contain("Style: Natural localization is allowed");
        prompt.Should().Contain("Additional style requirement: Keep character voice");
        prompt.Should().NotContain("zh-Hans");
        prompt.Should().NotContain("目标语言");
    }

    [Fact]
    public void BuildSystemPrompt_uses_full_custom_prompt_when_configured()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new PromptOptions(
            TargetLanguage: "ja",
            Style: TranslationStyle.UiConcise,
            CustomInstruction: null,
            CustomPrompt: "Output {TargetLanguage}. Style={StyleInstruction}"));

        prompt.Should().Be("Output Japanese. Style=Style: Keep UI, menu, and button text short and clear.");
    }

    [Fact]
    public void BuildBatchUserPrompt_uses_json_input_without_numeric_labels()
    {
        var prompt = PromptBuilder.BuildBatchUserPrompt(new[] { "CONTINUE", "Off" });

        prompt.Should().Contain("Return only a JSON string array");
        prompt.Should().Contain("""["CONTINUE","Off"]""");
        prompt.Should().NotContain("0:");
        prompt.Should().NotContain("1:");
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
