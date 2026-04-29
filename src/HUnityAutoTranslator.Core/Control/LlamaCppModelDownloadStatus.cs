namespace HUnityAutoTranslator.Core.Control;

public sealed record LlamaCppModelDownloadStatus(
    string State,
    string? PresetId,
    string? PresetLabel,
    string? FileName,
    string? LocalPath,
    long DownloadedBytes,
    long TotalBytes,
    double ProgressPercent,
    string Message,
    string? Error,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? CompletedUtc)
{
    public static LlamaCppModelDownloadStatus Idle()
    {
        return new LlamaCppModelDownloadStatus(
            "idle",
            null,
            null,
            null,
            null,
            0,
            0,
            0,
            "尚未开始下载模型。",
            null,
            null,
            null);
    }
}
