using FluentAssertions;
using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Tests.Prompts;

public sealed class PromptPolicyTests
{
    [Fact]
    public void BuildSystemPrompt_contains_hard_rules_and_style()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new PromptOptions("zh-Hans", TranslationStyle.Localized, "保持角色口吻"));

        prompt.Should().Contain("只输出译文");
        prompt.Should().Contain("自动判断源语言");
        prompt.Should().Contain("不要解释");
        prompt.Should().Contain("不要改变占位符");
        prompt.Should().Contain("允许自然本地化");
        prompt.Should().Contain("保持角色口吻");
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
}
