using FluentAssertions;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class ControlPanelServiceTests
{
    [Fact]
    public void Snapshot_masks_api_key_and_reports_runtime_status()
    {
        var service = ControlPanelService.CreateDefault();
        service.SetApiKey("secret-value");

        var state = service.GetState();

        state.ApiKeyConfigured.Should().BeTrue();
        state.ApiKeyPreview.Should().BeNull();
        state.TargetLanguage.Should().Be("zh-Hans");
    }

    [Fact]
    public void UpdateConfig_changes_target_language_and_concurrency()
    {
        var service = ControlPanelService.CreateDefault();

        service.UpdateConfig(new UpdateConfigRequest(TargetLanguage: "ja", MaxConcurrentRequests: 8, RequestsPerMinute: 90));

        var state = service.GetState();
        state.TargetLanguage.Should().Be("ja");
        state.MaxConcurrentRequests.Should().Be(8);
        state.RequestsPerMinute.Should().Be(90);
    }
}
