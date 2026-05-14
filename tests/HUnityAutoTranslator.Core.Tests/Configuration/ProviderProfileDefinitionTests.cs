using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Tests.Configuration;

public sealed class ProviderProfileDefinitionTests
{
    [Fact]
    public void CreateDefault_does_not_assign_a_preset_id()
    {
        ProviderProfileDefinition.CreateDefault(null, ProviderKind.OpenAICompatible, priority: 0)
            .PresetId.Should().BeNull();
    }

    [Fact]
    public void Normalize_keeps_a_known_preset_id_and_canonicalizes_casing_and_whitespace()
    {
        var definition = ProviderProfileDefinition.CreateDefault("硅基流动", ProviderKind.OpenAICompatible, priority: 0)
            with { PresetId = "  SiliconFlow  " };

        definition.Normalize().PresetId.Should().Be("siliconflow");
    }

    [Fact]
    public void Normalize_drops_an_unknown_preset_id()
    {
        var definition = ProviderProfileDefinition.CreateDefault("自定义", ProviderKind.OpenAICompatible, priority: 0)
            with { PresetId = "not-a-real-preset" };

        definition.Normalize().PresetId.Should().BeNull();
    }
}
