using FluentAssertions;
using HUnityAutoTranslator.Core.Runtime;

namespace HUnityAutoTranslator.Core.Tests.Runtime;

public sealed class TextLayoutCompensationTests
{
    [Fact]
    public void Ugui_line_spacing_increases_to_preserve_original_absolute_line_height()
    {
        TextLayoutCompensation.TryCalculateUguiLineSpacing(
                originalLineSpacing: 1f,
                originalFontLineHeight: 40f,
                currentFontLineHeight: 30f,
                out var adjusted)
            .Should().BeTrue();

        adjusted.Should().BeApproximately(1.333f, 0.001f);
    }

    [Fact]
    public void Ugui_line_spacing_never_shrinks_below_original_spacing()
    {
        TextLayoutCompensation.TryCalculateUguiLineSpacing(
                originalLineSpacing: 1.1f,
                originalFontLineHeight: 30f,
                currentFontLineHeight: 40f,
                out var adjusted)
            .Should().BeTrue();

        adjusted.Should().Be(1.1f);
    }

    [Fact]
    public void Translated_layout_detection_treats_wrapped_or_explicit_multiline_text_as_multiline()
    {
        TextLayoutCompensation.IsLikelyMultiline("第一行\n第二行", 32f, preferredHeight: null, renderedHeight: null)
            .Should().BeTrue();

        TextLayoutCompensation.IsLikelyMultiline("自动换行的长文本", 32f, preferredHeight: 78f, renderedHeight: null)
            .Should().BeTrue();

        TextLayoutCompensation.IsLikelyMultiline("继续", 32f, preferredHeight: 34f, renderedHeight: null)
            .Should().BeFalse();
    }

    [Fact]
    public void Height_fit_font_size_shrink_respects_minimum_ratio()
    {
        TextLayoutCompensation.TryCalculateHeightFitFontSize(
                currentFontSize: 32f,
                originalFontSize: 32f,
                preferredHeight: 120f,
                rectHeight: 90f,
                fitRatio: 0.92f,
                minimumScale: 0.75f,
                out var adjusted)
            .Should().BeTrue();

        adjusted.Should().Be(24f);
    }
}
