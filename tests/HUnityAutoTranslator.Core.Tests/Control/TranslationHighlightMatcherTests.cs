using FluentAssertions;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class TranslationHighlightMatcherTests
{
    [Fact]
    public void Find_best_match_prefers_visible_alive_target_with_matching_scene_hierarchy_and_component()
    {
        var request = new TranslationHighlightRequest(
            SourceText: "Start",
            SceneName: "MainMenu",
            ComponentHierarchy: "Canvas/Menu/StartButton/Text",
            ComponentType: "Text");

        var targets = new[]
        {
            new TranslationHighlightTarget("wrong-scene", "Hud", "Canvas/Menu/StartButton/Text", "UnityEngine.UI.Text", IsAlive: true, IsVisible: true),
            new TranslationHighlightTarget("dead", "MainMenu", "Canvas/Menu/StartButton/Text", "UnityEngine.UI.Text", IsAlive: false, IsVisible: true),
            new TranslationHighlightTarget("invisible", "MainMenu", "Canvas/Menu/StartButton/Text", "UnityEngine.UI.Text", IsAlive: true, IsVisible: false),
            new TranslationHighlightTarget("visible", "MainMenu", "Canvas/Menu/StartButton/Text", "UnityEngine.UI.Text", IsAlive: true, IsVisible: true)
        };

        var match = TranslationHighlightMatcher.FindBestMatch(request, targets);

        match.Should().NotBeNull();
        match!.TargetId.Should().Be("visible");
    }

    [Theory]
    [InlineData(null, "UnityEngine.UI.Text")]
    [InlineData("", "UnityEngine.UI.Text")]
    [InlineData("Canvas/Menu/Text", "IMGUI")]
    public void Is_supported_rejects_requests_without_locatable_unity_text_targets(string? hierarchy, string? componentType)
    {
        var request = new TranslationHighlightRequest(
            SourceText: "Start",
            SceneName: "MainMenu",
            ComponentHierarchy: hierarchy,
            ComponentType: componentType);

        TranslationHighlightMatcher.IsSupported(request).Should().BeFalse();
        TranslationHighlightMatcher.FindBestMatch(request, Array.Empty<TranslationHighlightTarget>()).Should().BeNull();
    }
}
