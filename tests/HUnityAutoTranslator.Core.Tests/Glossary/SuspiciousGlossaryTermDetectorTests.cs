using FluentAssertions;
using HUnityAutoTranslator.Core.Glossary;

namespace HUnityAutoTranslator.Core.Tests.Glossary;

public sealed class SuspiciousGlossaryTermDetectorTests
{
    [Theory]
    [InlineData("はい")]
    [InlineData("いいえ")]
    [InlineData("うん")]
    [InlineData("そう")]
    [InlineData("です")]
    [InlineData("もし")]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("ok")]
    [InlineData("back")]
    public void Flags_short_high_frequency_function_words(string source)
    {
        SuspiciousGlossaryTermDetector.IsSuspicious(source).Should().BeTrue();
    }

    [Theory]
    [InlineData("鉄の剣")]
    [InlineData("エクスカリバー")]
    [InlineData("ファイアボール")]
    [InlineData("艾蕾诺尔")]
    [InlineData("Pirate Cove")]
    public void Keeps_real_proper_nouns_and_named_terms(string source)
    {
        SuspiciousGlossaryTermDetector.IsSuspicious(source).Should().BeFalse();
    }

    [Fact]
    public void Pure_short_hiragana_is_a_function_word_but_kanji_or_katakana_is_not()
    {
        SuspiciousGlossaryTermDetector.LooksLikeFunctionWord("そう").Should().BeTrue();
        SuspiciousGlossaryTermDetector.LooksLikeFunctionWord("ファイア").Should().BeFalse();
        SuspiciousGlossaryTermDetector.LooksLikeFunctionWord("鉄剣").Should().BeFalse();
    }

    [Fact]
    public void Blank_source_is_treated_as_suspicious()
    {
        SuspiciousGlossaryTermDetector.IsSuspicious(null).Should().BeTrue();
        SuspiciousGlossaryTermDetector.IsSuspicious("   ").Should().BeTrue();
    }
}
