using FluentAssertions;
using HUnityAutoTranslator.Core.Runtime;

namespace HUnityAutoTranslator.Core.Tests.Runtime;

public sealed class ImguiTextClassifierTests
{
    [Theory]
    [InlineData("\u6e38\u620f\u8bbe\u7f6e")]
    [InlineData("\u542f\u7528\u9a6c\u8d5b\u514b\u906e\u6321")]
    [InlineData("\u590d\u5236")]
    [InlineData("\u9875 1/1")]
    [InlineData("\u6e05\u9664NPC\u7f13\u5b58")]
    [InlineData("NPC - \u751f\u6210 (9)")]
    [InlineData("\u6253\u5f00DLC\u6587\u4ef6\u5939")]
    [InlineData("\u91cd\u7f6e\u7a97\u53e3 (F6)")]
    public void ShouldSkipTranslation_skips_chinese_text_when_target_is_simplified_chinese(string sourceText)
    {
        ImguiTextClassifier.ShouldSkipTranslation(sourceText, "zh-Hans").Should().BeTrue();
    }

    [Theory]
    [InlineData("Loading: 1200/1697")]
    [InlineData("Loading: 1625/1697")]
    [InlineData("25/1697 Items (Loading 1675/1697)")]
    [InlineData("Showing 1-25 / 1697")]
    [InlineData("Page 1/4")]
    public void ShouldSkipTranslation_skips_high_churn_progress_text(string sourceText)
    {
        ImguiTextClassifier.ShouldSkipTranslation(sourceText, "zh-Hans").Should().BeTrue();
    }

    [Fact]
    public void ShouldPrepareFontForSkippedText_only_keeps_static_chinese_text()
    {
        ImguiTextClassifier.ShouldPrepareFontForSkippedText("\u6e38\u620f\u8bbe\u7f6e", "zh-Hans").Should().BeTrue();
        ImguiTextClassifier.ShouldPrepareFontForSkippedText("Loading: 1200/1697", "zh-Hans").Should().BeFalse();
        ImguiTextClassifier.ShouldPrepareFontForSkippedText("God Mode", "zh-Hans").Should().BeFalse();
    }

    [Theory]
    [InlineData("God Mode")]
    [InlineData("Enable cheats")]
    [InlineData("\u542f\u7528 God Mode")]
    [InlineData("Time speed (0=frozen, 1=normal, 10=fast):")]
    public void ShouldSkipTranslation_keeps_translatable_imgui_text(string sourceText)
    {
        ImguiTextClassifier.ShouldSkipTranslation(sourceText, "zh-Hans").Should().BeFalse();
    }

    [Fact]
    public void ShouldSkipTranslation_does_not_skip_chinese_text_for_non_chinese_targets()
    {
        ImguiTextClassifier.ShouldSkipTranslation("\u6e38\u620f\u8bbe\u7f6e", "en").Should().BeFalse();
    }
}
