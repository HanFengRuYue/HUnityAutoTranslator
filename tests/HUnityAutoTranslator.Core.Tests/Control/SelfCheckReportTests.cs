using FluentAssertions;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class SelfCheckReportTests
{
    [Fact]
    public void Completed_report_summarizes_item_severity_counts()
    {
        var started = DateTimeOffset.Parse("2026-05-02T10:00:00Z");
        var completed = started.AddMilliseconds(1250);
        var report = SelfCheckReport.Completed(
            started,
            completed,
            new[]
            {
                SelfCheckItem.Ok("runtime.http", "运行环境", "本机控制面板", "监听地址 http://127.0.0.1:48110/", "无需处理。", 12),
                SelfCheckItem.Warning("capture.tmp", "文本采集", "TextMeshPro", "未发现 TMPro.TMP_Text 类型。", "如果游戏使用 TMP 文本，请检查 TextMeshPro 程序集。", 4),
                SelfCheckItem.Error("storage.cache", "本地存储", "翻译缓存", "SQLite 打开失败。", "检查 BepInEx/config/HUnityAutoTranslator 写入权限。", 18),
                SelfCheckItem.Skipped("provider.external", "AI 服务", "外部连接测试", "按自检策略跳过外部 API 调用。", "需要验证额度或模型时请使用 AI 设置页的手动测试。", 1)
            });

        report.State.Should().Be(SelfCheckRunState.Completed);
        report.Severity.Should().Be(SelfCheckSeverity.Error);
        report.ItemCount.Should().Be(4);
        report.OkCount.Should().Be(1);
        report.WarningCount.Should().Be(1);
        report.ErrorCount.Should().Be(1);
        report.SkippedCount.Should().Be(1);
        report.DurationMilliseconds.Should().Be(1250);
    }

    [Fact]
    public void Report_summary_never_contains_secret_values_from_evidence()
    {
        var report = SelfCheckReport.Completed(
            DateTimeOffset.Parse("2026-05-02T10:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T10:00:01Z"),
            new[]
            {
                SelfCheckItem.Warning("provider.key", "AI 服务", "API Key", "API Key 已配置：sk-secret-123456", "无需联网验证。", 1)
            });

        report.Items[0].Evidence.Should().NotContain("sk-secret-123456");
        report.Items[0].Evidence.Should().Contain("***");
    }
}
