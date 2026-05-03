using FluentAssertions;
using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Tests.Prompts;

public sealed class PromptItemClassifierTests
{
    [Fact]
    public void Classifier_extracts_ikebukuro_switch_option_and_setting_group()
    {
        const string hierarchy = "Canvas - Main Menu/Main Content/Settings (1)/Content/Panel Content/Panels/General/Content/List/Layout Group/Auto Message Lady/Switch/Label On";

        PromptItemClassifier.GetOptionContainerHierarchy(hierarchy)
            .Should().Be("Canvas - Main Menu/Main Content/Settings (1)/Content/Panel Content/Panels/General/Content/List/Layout Group/Auto Message Lady");
        PromptItemClassifier.GetSettingGroupHierarchy(hierarchy)
            .Should().Be("Canvas - Main Menu/Main Content/Settings (1)/Content/Panel Content/Panels/General/Content/List/Layout Group");
    }

    [Fact]
    public void Classifier_extracts_settings_panel_option_and_setting_group()
    {
        const string hierarchy = "Menu/Camera/Canvas/Settings Menu/Screen Panel/Reticle/Text";

        PromptItemClassifier.GetOptionContainerHierarchy(hierarchy)
            .Should().Be("Menu/Camera/Canvas/Settings Menu/Screen Panel/Reticle");
        PromptItemClassifier.GetSettingGroupHierarchy(hierarchy)
            .Should().Be("Menu/Camera/Canvas/Settings Menu/Screen Panel");
    }
}
