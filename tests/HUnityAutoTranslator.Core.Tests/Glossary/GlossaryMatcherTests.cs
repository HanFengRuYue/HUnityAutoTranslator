using FluentAssertions;
using HUnityAutoTranslator.Core.Glossary;

namespace HUnityAutoTranslator.Core.Tests.Glossary;

public sealed class GlossaryMatcherTests
{
    [Fact]
    public void MatchTerms_uses_case_insensitive_longest_match_and_ignores_disabled_terms()
    {
        var terms = new[]
        {
            GlossaryTerm.CreateManual("Security", "安全", "zh-Hans", null),
            GlossaryTerm.CreateManual("Security Breach", "安全漏洞", "zh-Hans", null),
            GlossaryTerm.CreateManual("Freddy", "弗雷迪", "zh-Hans", null) with { Enabled = false }
        };

        var matches = GlossaryMatcher.MatchTerms(
            new[] { "security breach starts now. Freddy is nearby." },
            terms,
            maxTerms: 10,
            maxCharacters: 200);

        matches.Should().ContainSingle();
        matches[0].TextIndex.Should().Be(0);
        matches[0].SourceTerm.Should().Be("Security Breach");
        matches[0].TargetTerm.Should().Be("安全漏洞");
    }

    [Fact]
    public void MatchTerms_respects_term_and_character_limits()
    {
        var terms = new[]
        {
            GlossaryTerm.CreateManual("Alpha", "甲", "zh-Hans", null),
            GlossaryTerm.CreateManual("Beta", "乙", "zh-Hans", null),
            GlossaryTerm.CreateManual("Gamma", "丙", "zh-Hans", null)
        };

        var matches = GlossaryMatcher.MatchTerms(
            new[] { "Alpha Beta Gamma" },
            terms,
            maxTerms: 2,
            maxCharacters: 20);

        matches.Should().HaveCount(2);
        matches.Sum(match => match.SourceTerm.Length + match.TargetTerm.Length).Should().BeLessThanOrEqualTo(20);
    }
}
