using System.Text.RegularExpressions;

namespace HUnityAutoTranslator.Core.Control;

public enum SelfCheckSeverity
{
    Ok = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Skipped = 4
}

public enum SelfCheckRunState
{
    NotStarted = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

public sealed record SelfCheckItem(
    string Id,
    string Category,
    string Name,
    SelfCheckSeverity Severity,
    string Summary,
    string Evidence,
    string Recommendation,
    double DurationMilliseconds)
{
    private static readonly Regex SecretPattern = new(
        @"(?i)\b(sk-[A-Za-z0-9_\-]{6,}|[A-Za-z0-9_\-]{16,}\.[A-Za-z0-9_\-]{6,}\.[A-Za-z0-9_\-]{6,})",
        RegexOptions.Compiled);

    public static SelfCheckItem Ok(
        string id,
        string category,
        string name,
        string evidence,
        string recommendation,
        double durationMilliseconds)
    {
        return Create(id, category, name, SelfCheckSeverity.Ok, "正常", evidence, recommendation, durationMilliseconds);
    }

    public static SelfCheckItem Info(
        string id,
        string category,
        string name,
        string summary,
        string evidence,
        string recommendation,
        double durationMilliseconds)
    {
        return Create(id, category, name, SelfCheckSeverity.Info, summary, evidence, recommendation, durationMilliseconds);
    }

    public static SelfCheckItem Warning(
        string id,
        string category,
        string name,
        string evidence,
        string recommendation,
        double durationMilliseconds)
    {
        return Create(id, category, name, SelfCheckSeverity.Warning, "需要注意", evidence, recommendation, durationMilliseconds);
    }

    public static SelfCheckItem Error(
        string id,
        string category,
        string name,
        string evidence,
        string recommendation,
        double durationMilliseconds)
    {
        return Create(id, category, name, SelfCheckSeverity.Error, "异常", evidence, recommendation, durationMilliseconds);
    }

    public static SelfCheckItem Skipped(
        string id,
        string category,
        string name,
        string evidence,
        string recommendation,
        double durationMilliseconds)
    {
        return Create(id, category, name, SelfCheckSeverity.Skipped, "已跳过", evidence, recommendation, durationMilliseconds);
    }

    public static SelfCheckItem Create(
        string id,
        string category,
        string name,
        SelfCheckSeverity severity,
        string summary,
        string evidence,
        string recommendation,
        double durationMilliseconds)
    {
        return new SelfCheckItem(
            Normalize(id, "unknown"),
            Normalize(category, "未分类"),
            Normalize(name, "未命名检查"),
            severity,
            Normalize(summary, "无结果"),
            Sanitize(Normalize(evidence, "无证据")),
            Sanitize(Normalize(recommendation, "无需处理。")),
            Math.Max(0, durationMilliseconds));
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string Sanitize(string value)
    {
        return SecretPattern.Replace(value, "***");
    }
}

public sealed record SelfCheckReport(
    SelfCheckRunState State,
    SelfCheckSeverity Severity,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? CompletedUtc,
    double DurationMilliseconds,
    int ItemCount,
    int OkCount,
    int InfoCount,
    int WarningCount,
    int ErrorCount,
    int SkippedCount,
    IReadOnlyList<SelfCheckItem> Items,
    string Message)
{
    public static SelfCheckReport NotStarted()
    {
        return new SelfCheckReport(
            SelfCheckRunState.NotStarted,
            SelfCheckSeverity.Info,
            StartedUtc: null,
            CompletedUtc: null,
            DurationMilliseconds: 0,
            ItemCount: 0,
            OkCount: 0,
            InfoCount: 0,
            WarningCount: 0,
            ErrorCount: 0,
            SkippedCount: 0,
            Items: Array.Empty<SelfCheckItem>(),
            Message: "尚未运行本地自检。");
    }

    public static SelfCheckReport Running(DateTimeOffset startedUtc)
    {
        return new SelfCheckReport(
            SelfCheckRunState.Running,
            SelfCheckSeverity.Info,
            startedUtc,
            CompletedUtc: null,
            DurationMilliseconds: 0,
            ItemCount: 0,
            OkCount: 0,
            InfoCount: 0,
            WarningCount: 0,
            ErrorCount: 0,
            SkippedCount: 0,
            Items: Array.Empty<SelfCheckItem>(),
            Message: "本地自检正在运行。");
    }

    public static SelfCheckReport Completed(
        DateTimeOffset startedUtc,
        DateTimeOffset completedUtc,
        IReadOnlyList<SelfCheckItem> items)
    {
        var normalized = items.ToArray();
        var errorCount = normalized.Count(item => item.Severity == SelfCheckSeverity.Error);
        var warningCount = normalized.Count(item => item.Severity == SelfCheckSeverity.Warning);
        var skippedCount = normalized.Count(item => item.Severity == SelfCheckSeverity.Skipped);
        var infoCount = normalized.Count(item => item.Severity == SelfCheckSeverity.Info);
        var okCount = normalized.Count(item => item.Severity == SelfCheckSeverity.Ok);
        var severity = errorCount > 0
            ? SelfCheckSeverity.Error
            : warningCount > 0
                ? SelfCheckSeverity.Warning
                : SelfCheckSeverity.Ok;
        var message = errorCount > 0
            ? $"本地自检发现 {errorCount} 个异常。"
            : warningCount > 0
                ? $"本地自检发现 {warningCount} 个需要注意的项目。"
                : "本地自检未发现阻断问题。";

        return new SelfCheckReport(
            SelfCheckRunState.Completed,
            severity,
            startedUtc,
            completedUtc,
            Math.Max(0, (completedUtc - startedUtc).TotalMilliseconds),
            normalized.Length,
            okCount,
            infoCount,
            warningCount,
            errorCount,
            skippedCount,
            normalized,
            message);
    }

    public static SelfCheckReport Failed(
        DateTimeOffset startedUtc,
        DateTimeOffset completedUtc,
        string message,
        IReadOnlyList<SelfCheckItem> items)
    {
        var failure = SelfCheckItem.Error(
            "self-check.failure",
            "本地自检",
            "自检流程",
            message,
            "查看 BepInEx 日志中该异常的完整堆栈。",
            0);
        var normalized = items.Concat(new[] { failure }).ToArray();
        return Completed(startedUtc, completedUtc, normalized) with
        {
            State = SelfCheckRunState.Failed,
            Message = "本地自检流程异常中止。"
        };
    }
}
