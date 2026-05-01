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
    public void ShouldTranslate_allows_sentence_with_slash_pronoun()
    {
        const string value = "Please email support mentioning the author and his/her work used in this game.";

        TextFilter.ShouldTranslate(value).Should().BeTrue();
    }

    [Theory]
    [InlineData("C:\\Games\\The Glitched Attraction\\BepInEx\\config\\settings.cfg")]
    [InlineData("Assets/Textures/disclaimer.png")]
    [InlineData("https://example.com/path/file.png")]
    [InlineData("support@example.com")]
    public void ShouldTranslate_rejects_preservable_technical_text(string value)
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

    [Fact]
    public void RichTextGuard_allows_rebuilt_per_character_tmp_tags_with_different_text_length()
    {
        var source = "<rotate=90>A</rotate><rotate=90>B</rotate><rotate=90><voffset=0.5em>,</voffset></rotate><rotate=90>C</rotate>";
        var translated = "<rotate=90>X</rotate><rotate=90><voffset=0.5em>,</voffset></rotate><rotate=90>Y</rotate>";

        RichTextGuard.HasSameTags(source, translated).Should().BeTrue();
        translated.Should().NotContain("<rotate=90></rotate>");
    }

    [Fact]
    public void RichTextGuard_keeps_ordinary_rich_text_strict()
    {
        RichTextGuard.HasSameTags("<color=red>Start</color>", "<color=red>Start").Should().BeFalse();
    }
}
