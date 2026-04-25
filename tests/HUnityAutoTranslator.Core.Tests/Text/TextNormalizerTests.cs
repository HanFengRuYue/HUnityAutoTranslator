using FluentAssertions;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Tests.Text;

public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("  Start\\r\\nGame  ", "Start\nGame")]
    [InlineData("Start\u00A0Game", "Start Game")]
    [InlineData("\tLevel   Up\t", "Level Up")]
    public void NormalizeForCache_collapses_nonsemantic_whitespace(string input, string expected)
    {
        TextNormalizer.NormalizeForCache(input).Should().Be(expected);
    }
}
