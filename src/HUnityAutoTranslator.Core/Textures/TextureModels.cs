using Newtonsoft.Json;

namespace HUnityAutoTranslator.Core.Textures;

public sealed record TextureReferenceInfo(
    string TargetId,
    string? SceneName,
    string? ComponentHierarchy,
    string? ComponentType);

public sealed record TextureCatalogItem(
    string SourceHash,
    string TextureName,
    int Width,
    int Height,
    string Format,
    string FileName,
    int ReferenceCount,
    IReadOnlyList<TextureReferenceInfo> References,
    bool HasOverride,
    DateTimeOffset? OverrideUpdatedUtc)
{
    public TextureTextAnalysis? TextAnalysis { get; init; }
}

public sealed record TextureCatalogQuery(
    string? SceneName,
    int Offset,
    int Limit,
    string? TextStatus = null);

public sealed record TextureCatalogQueryResult(
    int TotalCount,
    int FilteredCount,
    int ReferenceCount,
    int Offset,
    int Limit,
    IReadOnlyList<string> Scenes,
    IReadOnlyList<TextureCatalogItem> Items);

public sealed record TextureCatalogScanStatus(
    bool IsScanning,
    string Message,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? CompletedUtc,
    int ProcessedTargets,
    int DiscoveredTextureCount,
    int DiscoveredReferenceCount)
{
    public static TextureCatalogScanStatus Idle(DateTimeOffset? completedUtc)
    {
        return new TextureCatalogScanStatus(
            false,
            "空闲",
            null,
            completedUtc,
            0,
            0,
            0);
    }
}

public sealed record TextureCatalogPage(
    DateTimeOffset? ScannedUtc,
    int TextureCount,
    int ReferenceCount,
    int OverrideCount,
    IReadOnlyList<TextureCatalogItem> Items,
    IReadOnlyList<string> Errors,
    int TotalCount,
    int FilteredCount,
    int Offset,
    int Limit,
    IReadOnlyList<string> Scenes,
    TextureCatalogScanStatus ScanStatus);

public sealed record TextureScanResult(
    DateTimeOffset ScannedUtc,
    int TextureCount,
    int ReferenceCount,
    int OverrideCount,
    IReadOnlyList<string> Errors,
    bool IsScanning,
    string Message);

public sealed record TextureImportResult(
    int ImportedCount,
    int AppliedCount,
    IReadOnlyList<string> Errors);

public sealed record TextureOverrideClearResult(
    int DeletedCount,
    int RestoredCount,
    IReadOnlyList<string> Errors);

public sealed record TextureTextDetectionResult(
    int RequestedCount,
    int UpdatedCount,
    IReadOnlyList<TextureTextAnalysis> Items,
    IReadOnlyList<string> Errors);

public sealed record TextureTextDetectionRequest(
    IReadOnlyList<string>? SourceHashes = null);

public sealed record TextureTextStatusUpdateRequest(
    IReadOnlyList<string> SourceHashes,
    string Status);

public sealed record TextureTextStatusUpdateResult(
    int UpdatedCount,
    IReadOnlyList<TextureTextAnalysis> Items,
    IReadOnlyList<string> Errors);

public sealed record TextureImageTranslateRequest(
    IReadOnlyList<string> SourceHashes,
    bool Force = false);

public sealed record TextureImageTranslateResult(
    int RequestedCount,
    int GeneratedCount,
    int AppliedCount,
    IReadOnlyList<TextureTextAnalysis> Items,
    IReadOnlyList<string> Errors);

public enum TextureTextStatus
{
    Unknown = 0,
    LikelyNoText = 1,
    Candidate = 2,
    ConfirmedText = 3,
    NeedsManualReview = 4,
    NoText = 5,
    Generated = 6,
    Failed = 7
}

public sealed record TextureTextAnalysis(
    string SourceHash,
    TextureTextStatus Status,
    double Confidence,
    string? DetectedText,
    string? Reason,
    bool NeedsManualReview,
    bool UserReviewed,
    DateTimeOffset UpdatedUtc,
    string? LastError)
{
    public static TextureTextAnalysis Unknown(string sourceHash)
    {
        return new TextureTextAnalysis(
            sourceHash,
            TextureTextStatus.Unknown,
            0,
            null,
            null,
            false,
            false,
            DateTimeOffset.UtcNow,
            null);
    }
}

public sealed record TextureOverrideIndex(IReadOnlyList<TextureOverrideRecord> Records)
{
    public static TextureOverrideIndex Empty { get; } = new(Array.Empty<TextureOverrideRecord>());
}

public sealed record TextureOverrideRecord(
    string SourceHash,
    string FileName,
    int Width,
    int Height,
    DateTimeOffset UpdatedUtc)
{
    [JsonIgnore]
    public string FilePath { get; init; } = string.Empty;
}

public sealed record TextureExportManifest(
    int FormatVersion,
    string? GameTitle,
    DateTimeOffset ExportedUtc,
    IReadOnlyList<TextureManifestItem> Textures)
{
    public const int CurrentFormatVersion = 1;
}

public sealed record TextureManifestItem(
    string SourceHash,
    string FileName,
    string TextureName,
    int Width,
    int Height,
    string Format,
    int ReferenceCount,
    IReadOnlyList<TextureReferenceInfo> References)
{
    public static TextureManifestItem FromCatalogItem(TextureCatalogItem item)
    {
        return new TextureManifestItem(
            item.SourceHash,
            item.FileName,
            item.TextureName,
            item.Width,
            item.Height,
            item.Format,
            item.ReferenceCount,
            item.References);
    }
}
