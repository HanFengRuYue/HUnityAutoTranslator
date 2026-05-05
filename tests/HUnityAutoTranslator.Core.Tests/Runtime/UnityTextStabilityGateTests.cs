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
    public void ShouldProcess_releases_default_typewriter_text_soon_after_it_stops_changing()
    {
        var gate = new UnityTextStabilityGate();
        var context = new StableTextContext(
            "subtitle",
            "zh-Hans",
            "prompt-v6",
            "Dialogue",
            "Canvas/Dialog/Text",
            "TMPro.TextMeshProUGUI");

        gate.ShouldProcess(context, "Do", nowSeconds: 0).Should().BeFalse();
        gate.ShouldProcess(context, "Do n", nowSeconds: 0.1).Should().BeFalse();
        gate.ShouldProcess(context, "Do not", nowSeconds: 0.2).Should().BeFalse();
        gate.ShouldProcess(context, "Do not", nowSeconds: 0.54).Should().BeFalse();

        gate.ShouldProcess(context, "Do not", nowSeconds: 0.55).Should().BeTrue();
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
    public void Fast_static_channel_releases_unchanged_text_before_normal_stability_window()
    {
        var gate = new UnityTextStabilityGate(
            stableSeconds: 0.25,
            typewriterStableSeconds: 1.0,
            fastStaticStableSeconds: 0.08,
            entryTtlSeconds: 60);
        var context = new StableTextContext(
            "button",
            "zh-Hans",
            "prompt-v7",
            "MainMenu",
            "Canvas/Start",
            "UnityEngine.UI.Text");

        gate.ShouldProcess(context, "Start Game", nowSeconds: 0, preferFastStaticRelease: true).Should().BeFalse();
        gate.ShouldProcess(context, "Start Game", nowSeconds: 0.07, preferFastStaticRelease: true).Should().BeFalse();

        gate.ShouldProcess(context, "Start Game", nowSeconds: 0.08, preferFastStaticRelease: true).Should().BeTrue();
    }

    [Fact]
    public void Fast_static_channel_switches_to_typewriter_window_for_prefix_growth()
    {
        var gate = new UnityTextStabilityGate(
            stableSeconds: 0.25,
            typewriterStableSeconds: 1.0,
            fastStaticStableSeconds: 0.08,
            entryTtlSeconds: 60);
        var context = new StableTextContext(
            "subtitle",
            "zh-Hans",
            "prompt-v7",
            "Dialogue",
            "Canvas/Dialog/Text",
            "TMPro.TextMeshProUGUI");

        gate.ShouldProcess(context, "He", nowSeconds: 0, preferFastStaticRelease: true).Should().BeFalse();
        gate.ShouldProcess(context, "Hello", nowSeconds: 0.05, preferFastStaticRelease: true).Should().BeFalse();
        gate.ShouldProcess(context, "Hello", nowSeconds: 0.9, preferFastStaticRelease: true).Should().BeFalse();

        gate.ShouldProcess(context, "Hello", nowSeconds: 1.05, preferFastStaticRelease: true).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_returns_cache_refresh_for_released_text_after_refresh_interval()
    {
        var gate = new UnityTextStabilityGate(
            stableSeconds: 0.25,
            typewriterStableSeconds: 1.0,
            entryTtlSeconds: 60,
            releasedTextRefreshSeconds: 2.0);
        var context = new StableTextContext(
            "slider-title",
            "zh-Hans",
            "prompt-v5",
            "PlayerSceneNight",
            "GUIRoot/GameSettingGUI/CuteSettingCanvasGUI/Window/InfoRoot/InfoAreaControl/Viewport/Content/Slider_Mouse Speed X Cute/LabelArea/TitleText",
            "UnityEngine.UI.Text");

        gate.Evaluate(context, "Camera Speed X", nowSeconds: 0).Should().Be(StableTextDecisionKind.Wait);
        gate.Evaluate(context, "Camera Speed X", nowSeconds: 0.25).Should().Be(StableTextDecisionKind.Process);
        gate.Evaluate(context, "Camera Speed X", nowSeconds: 1.5).Should().Be(StableTextDecisionKind.Wait);

        gate.Evaluate(context, "Camera Speed X", nowSeconds: 2.25).Should().Be(StableTextDecisionKind.RefreshCachedTranslation);
        gate.Evaluate(context, "Camera Speed X", nowSeconds: 2.5).Should().Be(StableTextDecisionKind.Wait);
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
