using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class ControlPanelMetricsTests
{
    [Fact]
    public void Captured_key_tracker_is_bounded_for_long_sessions()
    {
        var metrics = new ControlPanelMetrics();
        var config = RuntimeConfig.CreateDefault();

        for (var i = 0; i < ControlPanelMetrics.MaxTrackedCapturedKeys + 250; i++)
        {
            metrics.RecordCaptured(TranslationCacheKey.Create(
                $"source-{i}",
                config.TargetLanguage,
                config.Provider,
                "policy"));
        }

        var snapshot = metrics.Snapshot();

        snapshot.CapturedTextCount.Should().Be(ControlPanelMetrics.MaxTrackedCapturedKeys + 250);
        snapshot.CapturedKeyTrackerCount.Should().BeLessThanOrEqualTo(ControlPanelMetrics.MaxTrackedCapturedKeys);
    }
}
