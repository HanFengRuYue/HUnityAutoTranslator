using FluentAssertions;
using HUnityAutoTranslator.Core.Runtime;

namespace HUnityAutoTranslator.Core.Tests.Runtime;

public sealed class UnityTextStabilityGateTests
{
    [Fact]
    public void ShouldProcess_releases_only_final_typewriter_text_after_long_stability_window()
    {
        var gate = new UnityTextStabilityGate(
            stableSeconds: 0.25,
            typewriterStableSeconds: 1.0,
            entryTtlSeconds: 60);
        var context = new StableTextContext(
            "subtitle",
            "zh-Hans",
            "prompt-v5",
            "PlayerSceneNight",
            "GUIRoot/GameSettingGUI/MessageView/BG/Text",
            "UnityEngine.UI.Text");

        gate.ShouldProcess(context, "No", nowSeconds: 0).Should().BeFalse();
        gate.ShouldProcess(context, "No ", nowSeconds: 0.1).Should().BeFalse();
        gate.ShouldProcess(context, "No r", nowSeconds: 0.2).Should().BeFalse();
        gate.ShouldProcess(context, "No re", nowSeconds: 0.3).Should().BeFalse();
        gate.ShouldProcess(context, "No re", nowSeconds: 1.29).Should().BeFalse();

        gate.ShouldProcess(context, "No re", nowSeconds: 1.3).Should().BeTrue();
        gate.ShouldProcess(context, "No re", nowSeconds: 1.4).Should().BeFalse();
    }

    [Fact]
    public void ShouldProcess_releases_static_text_after_short_stability_window()
    {
        var gate = new UnityTextStabilityGate(
            stableSeconds: 0.25,
            typewriterStableSeconds: 1.0,
            entryTtlSeconds: 60);
        var context = new StableTextContext(
            "button",
            "zh-Hans",
            "prompt-v5",
            "MainMenu",
            "Canvas/Start",
            "UnityEngine.UI.Text");

        gate.ShouldProcess(context, "Start Game", nowSeconds: 0).Should().BeFalse();
        gate.ShouldProcess(context, "Start Game", nowSeconds: 0.24).Should().BeFalse();

        gate.ShouldProcess(context, "Start Game", nowSeconds: 0.25).Should().BeTrue();
        gate.ShouldProcess(context, "Start Game", nowSeconds: 0.5).Should().BeFalse();
    }

    [Fact]
    public void ShouldProcess_resets_when_same_component_switches_to_unrelated_text()
    {
        var gate = new UnityTextStabilityGate(
            stableSeconds: 0.25,
            typewriterStableSeconds: 1.0,
            entryTtlSeconds: 60);
        var context = new StableTextContext(
            "button",
            "zh-Hans",
            "prompt-v5",
            "MainMenu",
            "Canvas/Menu/Action",
            "UnityEngine.UI.Text");

        gate.ShouldProcess(context, "Start Game", nowSeconds: 0).Should().BeFalse();
        gate.ShouldProcess(context, "Start Game", nowSeconds: 0.25).Should().BeTrue();

        gate.ShouldProcess(context, "Options", nowSeconds: 0.3).Should().BeFalse();
        gate.ShouldProcess(context, "Options", nowSeconds: 0.54).Should().BeFalse();
        gate.ShouldProcess(context, "Options", nowSeconds: 0.55).Should().BeTrue();
    }
}
