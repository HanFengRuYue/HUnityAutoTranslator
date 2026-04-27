using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Glossary;
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
    public void BuildSystemPrompt_uses_full_custom_prompt_when_configured()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new PromptOptions(
            TargetLanguage: "ja",
            Style: TranslationStyle.UiConcise,
            CustomPrompt: "Output {TargetLanguage}. Style={StyleInstruction}"));

        prompt.Should().Be("Output Japanese. Style=Style: Keep UI, menu, and button text short and clear.");
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
