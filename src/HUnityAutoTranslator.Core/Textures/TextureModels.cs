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
    DateTimeOffset? OverrideUpdatedUtc);

public sealed record TextureCatalogPage(
    DateTimeOffset? ScannedUtc,
    int TextureCount,
    int ReferenceCount,
    int OverrideCount,
    IReadOnlyList<TextureCatalogItem> Items,
    IReadOnlyList<string> Errors);

public sealed record TextureScanResult(
    DateTimeOffset ScannedUtc,
    int TextureCount,
    int ReferenceCount,
    int OverrideCount,
    IReadOnlyList<string> Errors);

public sealed record TextureImportResult(
    int ImportedCount,
    int AppliedCount,
    IReadOnlyList<string> Errors);

public sealed record TextureOverrideClearResult(
    int DeletedCount,
    int RestoredCount,
    IReadOnlyList<string> Errors);

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
