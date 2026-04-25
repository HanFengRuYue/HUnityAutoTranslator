using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
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
        service.GetApiKey().Should().Be("secret-value");
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

    [Fact]
    public void UpdateConfig_changes_provider_profile_and_runtime_switches()
    {
        var service = ControlPanelService.CreateDefault();

        service.UpdateConfig(new UpdateConfigRequest(
            TargetLanguage: "ko",
            MaxConcurrentRequests: 32,
            RequestsPerMinute: 700,
            Enabled: false,
            ProviderKind: ProviderKind.OpenAICompatible,
            BaseUrl: "http://127.0.0.1:9000",
            Endpoint: "/v1/chat/completions",
            Model: "local-model",
            EnableUgui: false,
            EnableTmp: false,
            EnableImgui: true,
            MaxScanTargetsPerTick: 12,
            MaxWritebacksPerFrame: 64));

        var state = service.GetState(queueCount: 5, cacheCount: 9);
        var config = service.GetConfig();

        state.Enabled.Should().BeFalse();
        state.ProviderKind.Should().Be(ProviderKind.OpenAICompatible);
        state.BaseUrl.Should().Be("http://127.0.0.1:9000");
        state.Endpoint.Should().Be("/v1/chat/completions");
        state.Model.Should().Be("local-model");
        state.QueueCount.Should().Be(5);
        state.CacheCount.Should().Be(9);
        state.MaxConcurrentRequests.Should().Be(16);
        state.RequestsPerMinute.Should().Be(600);
        config.EnableUgui.Should().BeFalse();
        config.EnableTmp.Should().BeFalse();
        config.EnableImgui.Should().BeTrue();
        config.MaxScanTargetsPerTick.Should().Be(12);
        config.MaxWritebacksPerFrame.Should().Be(64);
    }
}
