using FluentAssertions;
using HUnityAutoTranslator.Core.Glossary;

namespace HUnityAutoTranslator.Core.Tests.Glossary;

public sealed class GlossaryTermCategoryTests
{
    [Theory]
    [InlineData("Boss name", "Boss·敌人名")]
    [InlineData("BOSS名", "Boss·敌人名")]
    [InlineData("Boss名", "Boss·敌人名")]
    [InlineData("Boss encounter name", "Boss·敌人名")]
    [InlineData("Boss名 '艾蕾诺尔' 为专有名词", "Boss·敌人名")]
    [InlineData("character name", "角色名")]
    [InlineData("npc", "角色名")]
    [InlineData("地点", "地名")]
    [InlineData("weapon", "物品名")]
    [InlineData("skill", "技能名")]
    [InlineData("guild", "阵营·组织名")]
    [InlineData("UI文本", "UI文本")]
    [InlineData("世界观术语", "世界观术语")]
    public void Normalizes_messy_notes_to_canonical_categories(string raw, string expected)
    {
        GlossaryTermCategory.Normalize(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("something totally unrelated")]
    public void Falls_back_to_other_for_unknown_notes(string? raw)
    {
        GlossaryTermCategory.Normalize(raw).Should().Be(GlossaryTermCategory.Other);
    }

    [Fact]
    public void All_exposes_the_nine_canonical_categories()
    {
        GlossaryTermCategory.All.Should().HaveCount(9);
        GlossaryTermCategory.All.Should().OnlyHaveUniqueItems();
        GlossaryTermCategory.All.Should().OnlyContain(category => GlossaryTermCategory.IsCanonical(category));
    }
}
