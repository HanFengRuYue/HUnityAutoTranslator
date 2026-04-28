using FluentAssertions;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Tests.Text;

public sealed class TextSafetyTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("!!!")]
    [InlineData("1280 x 720")]
    [InlineData("1920\u00d71080")]
    [InlineData("60 FPS")]
    public void ShouldTranslate_rejects_nonsemantic_text(string value)
    {
        TextFilter.ShouldTranslate(value).Should().BeFalse();
    }

    [Fact]
    public void PlaceholderProtector_preserves_format_tokens()
    {
        var protectedText = PlaceholderProtector.Protect("Hello {playerName}, you have {0} coins and %s gems.");

        protectedText.Text.Should().Contain("__HUT_TOKEN_0__");
        protectedText.Text.Should().Contain("__HUT_TOKEN_1__");
        protectedText.Text.Should().Contain("__HUT_TOKEN_2__");
        protectedText.Restore("你好 __HUT_TOKEN_0__，你有 __HUT_TOKEN_1__ 枚金币和 __HUT_TOKEN_2__ 颗宝石。")
            .Should().Be("你好 {playerName}，你有 {0} 枚金币和 %s 颗宝石。");
    }

    [Fact]
    public void RichTextGuard_detects_broken_tags()
    {
        RichTextGuard.HasSameTags("<color=red>Start</color>", "<color=red>开始</color>").Should().BeTrue();
        RichTextGuard.HasSameTags("<color=red>Start</color>", "<color=red>开始").Should().BeFalse();
    }
}
