using FluentAssertions;
using HUnityAutoTranslator.Core.Dispatching;

namespace HUnityAutoTranslator.Core.Tests.Dispatching;

public sealed class TranslationResultTests
{
    [Fact]
    public void Translation_result_carries_component_context_for_targeted_refresh()
    {
        var updatedUtc = DateTimeOffset.Parse("2026-04-26T12:00:00Z");

        var result = new TranslationResult(
            "target-1",
            "Show Helps",
            "显示帮助",
            priority: 110,
            previousTranslatedText: "旧帮助",
            sceneName: "Main Menu",
            componentHierarchy: "Menu/Camera/Canvas/Settings Menu/Gameplay Panel/Show Helps/Name",
            componentType: "TMPro.TextMeshProUGUI",
            updatedUtc: updatedUtc);

        result.SceneName.Should().Be("Main Menu");
        result.ComponentHierarchy.Should().Be("Menu/Camera/Canvas/Settings Menu/Gameplay Panel/Show Helps/Name");
        result.ComponentType.Should().Be("TMPro.TextMeshProUGUI");
        result.UpdatedUtc.Should().Be(updatedUtc);
        result.HasComponentContext.Should().BeTrue();
    }

    [Fact]
    public void Translation_result_without_component_hierarchy_does_not_have_component_context()
    {
        var result = new TranslationResult("target-1", "Start", "开始", priority: 100);

        result.HasComponentContext.Should().BeFalse();
    }
    [Fact]
    public void Translation_result_can_mark_source_restore_without_treating_source_as_translation()
    {
        var result = new TranslationResult(
            "target-1",
            "Start",
            "Start",
            priority: 110,
            previousTranslatedText: "Start ZH",
            restoreSourceText: true);

        result.RestoreSourceText.Should().BeTrue();
        result.PreviousTranslatedText.Should().Be("Start ZH");
    }
}
