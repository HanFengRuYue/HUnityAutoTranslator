using System.IO.Compression;
using Newtonsoft.Json;

namespace HUnityAutoTranslator.Core.Textures;

public sealed class TextureOverrideStore
{
    private const string ManifestEntryName = "manifest.json";
    private const string IndexFileName = "overrides.json";
    private const long MaxTextureEntryBytes = 64L * 1024L * 1024L;

    private readonly string _directory;
    private readonly string _indexPath;

    public TextureOverrideStore(string directory)
    {
        _directory = directory;
        _indexPath = Path.Combine(_directory, IndexFileName);
        Directory.CreateDirectory(_directory);
    }

    public int OverrideCount => LoadIndex().Records.Count;

    public TextureOverrideIndex LoadIndex()
    {
        if (!File.Exists(_indexPath))
        {
            return TextureOverrideIndex.Empty;
        }

        try
        {
            var persisted = JsonConvert.DeserializeObject<PersistedTextureOverrideIndex>(File.ReadAllText(_indexPath));
            if (persisted?.Records == null || persisted.Records.Length == 0)
            {
                return TextureOverrideIndex.Empty;
            }

            var records = persisted.Records
                .Where(record => !string.IsNullOrWhiteSpace(record.SourceHash) && !string.IsNullOrWhiteSpace(record.FileName))
                .Select(record => new TextureOverrideRecord(
                    record.SourceHash,
                    TextureArchiveNaming.BuildOverrideFileName(record.SourceHash),
                    record.Width,
                    record.Height,
                    record.UpdatedUtc)
                {
                    FilePath = Path.Combine(_directory, TextureArchiveNaming.BuildOverrideFileName(record.SourceHash))
                })
                .ToArray();
            return new TextureOverrideIndex(records);
        }
        catch
        {
            return TextureOverrideIndex.Empty;
        }
    }

    public bool TryGetOverride(string sourceHash, out TextureOverrideRecord? record)
    {
        record = LoadIndex().Records.FirstOrDefault(item =>
            string.Equals(item.SourceHash, sourceHash, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(item.FilePath));
        return record != null;
    }

    public bool TryReadOverrideBytes(string sourceHash, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (!TryGetOverride(sourceHash, out var record) || record == null)
        {
            return false;
        }

        bytes = File.ReadAllBytes(record.FilePath);
        return true;
    }

    public byte[] ExportArchive(
        IReadOnlyList<TextureCatalogItem> catalog,
        Func<TextureCatalogItem, byte[]> pngProvider,
        string? gameTitle)
    {
        var manifestItems = catalog
            .OrderBy(item => item.TextureName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceHash, StringComparer.OrdinalIgnoreCase)
            .Select(TextureManifestItem.FromCatalogItem)
            .ToArray();
        var manifest = new TextureExportManifest(
            TextureExportManifest.CurrentFormatVersion,
            string.IsNullOrWhiteSpace(gameTitle) ? null : gameTitle,
            DateTimeOffset.UtcNow,
            manifestItems);

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry(ManifestEntryName);
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write(TextureManifestSerializer.Serialize(manifest));
            }

            foreach (var item in catalog)
            {
                var entryName = TextureArchiveNaming.IsSafeArchivePath(item.FileName)
                    ? item.FileName
                    : TextureArchiveNaming.BuildTextureEntryName(item.SourceHash, item.TextureName);
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                var bytes = pngProvider(item);
                using var entryStream = entry.Open();
                entryStream.Write(bytes, 0, bytes.Length);
            }
        }

        return stream.ToArray();
    }

    public void ExportArchive(
        Stream output,
        IReadOnlyList<TextureCatalogItem> catalog,
        Action<TextureCatalogItem, Stream> pngWriter,
        string? gameTitle)
    {
        var manifestItems = catalog
            .OrderBy(item => item.TextureName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceHash, StringComparer.OrdinalIgnoreCase)
            .Select(TextureManifestItem.FromCatalogItem)
            .ToArray();
        var manifest = new TextureExportManifest(
            TextureExportManifest.CurrentFormatVersion,
            string.IsNullOrWhiteSpace(gameTitle) ? null : gameTitle,
            DateTimeOffset.UtcNow,
            manifestItems);

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var manifestEntry = archive.CreateEntry(ManifestEntryName);
        using (var writer = new StreamWriter(manifestEntry.Open()))
        {
            writer.Write(TextureManifestSerializer.Serialize(manifest));
        }

        foreach (var item in catalog)
        {
            var entryName = TextureArchiveNaming.IsSafeArchivePath(item.FileName)
                ? item.FileName
                : TextureArchiveNaming.BuildTextureEntryName(item.SourceHash, item.TextureName);
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            pngWriter(item, entryStream);
        }
    }

    public TextureImportResult ImportArchive(byte[] archiveBytes, IReadOnlyList<TextureCatalogItem> currentCatalog)
    {
        using var stream = new MemoryStream(archiveBytes);
        return ImportArchive(stream, currentCatalog);
    }

    public TextureImportResult ImportArchive(Stream archiveStream, IReadOnlyList<TextureCatalogItem> currentCatalog)
    {
        if (!archiveStream.CanSeek)
        {
            return ImportArchiveFromNonSeekableStream(archiveStream, currentCatalog);
        }

        var errors = new List<string>();
        var imported = 0;
        var catalogByHash = currentCatalog
            .GroupBy(item => item.SourceHash, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var records = LoadIndex().Records
            .ToDictionary(record => record.SourceHash, StringComparer.OrdinalIgnoreCase);

        try
        {
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
            var manifest = ReadManifest(archive);
            if (manifest == null)
            {
                return new TextureImportResult(0, 0, new[] { "缺少贴图清单 manifest.json。" });
            }

            if (manifest.FormatVersion != TextureExportManifest.CurrentFormatVersion)
            {
                return new TextureImportResult(0, 0, new[] { "贴图清单版本不兼容。" });
            }

            if (manifest.Textures == null)
            {
                return new TextureImportResult(0, 0, new[] { "贴图清单缺少贴图列表。" });
            }

            foreach (var item in manifest.Textures)
            {
                if (!TextureArchiveNaming.IsSafeArchivePath(item.FileName))
                {
                    errors.Add($"贴图路径不安全：{item.FileName}");
                    continue;
                }

                if (!catalogByHash.TryGetValue(item.SourceHash, out var current))
                {
                    errors.Add($"当前场景未找到贴图 {item.SourceHash}，已跳过。");
                    continue;
                }

                var entry = archive.GetEntry(item.FileName);
                if (entry == null)
                {
                    errors.Add($"压缩包中缺少贴图文件：{item.FileName}");
                    continue;
                }

                if (entry.Length > MaxTextureEntryBytes)
                {
                    errors.Add($"贴图文件过大：{item.FileName}");
                    continue;
                }

                var bytes = ReadEntryBytes(entry);
                if (!PngTextureInfo.TryReadDimensions(bytes, out var width, out var height))
                {
                    errors.Add($"贴图文件不是有效 PNG：{item.FileName}");
                    continue;
                }

                if (width != current.Width || height != current.Height)
                {
                    errors.Add($"贴图 {item.TextureName} 尺寸不匹配：导入 {width}x{height}，原始 {current.Width}x{current.Height}。");
                    continue;
                }

                var fileName = TextureArchiveNaming.BuildOverrideFileName(item.SourceHash);
                var filePath = Path.Combine(_directory, fileName);
                File.WriteAllBytes(filePath, bytes);
                records[item.SourceHash] = new TextureOverrideRecord(
                    item.SourceHash,
                    fileName,
                    width,
                    height,
                    DateTimeOffset.UtcNow)
                {
                    FilePath = filePath
                };
                imported++;
            }

            SaveIndex(records.Values);
            return new TextureImportResult(imported, 0, errors);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            return new TextureImportResult(0, 0, new[] { $"贴图导入失败：{ex.Message}" });
        }
    }

    private TextureImportResult ImportArchiveFromNonSeekableStream(Stream archiveStream, IReadOnlyList<TextureCatalogItem> currentCatalog)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"hunity-texture-import-{Guid.NewGuid():N}.zip");
        try
        {
            using (var temp = File.Open(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                archiveStream.CopyTo(temp);
                temp.Position = 0;
                return ImportArchive(temp, currentCatalog);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new TextureImportResult(0, 0, new[] { "贴图导入失败：" + ex.Message });
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }
        }
    }

    public TextureImportResult SaveOverride(TextureCatalogItem item, byte[] pngBytes)
    {
        if (string.IsNullOrWhiteSpace(item.SourceHash))
        {
            return new TextureImportResult(0, 0, new[] { "贴图来源哈希为空。" });
        }

        if (pngBytes.Length > MaxTextureEntryBytes)
        {
            return new TextureImportResult(0, 0, new[] { $"贴图文件过大：{item.TextureName}" });
        }

        if (!PngTextureInfo.TryReadDimensions(pngBytes, out var width, out var height))
        {
            return new TextureImportResult(0, 0, new[] { $"贴图文件不是有效 PNG：{item.TextureName}" });
        }

        if (width != item.Width || height != item.Height)
        {
            return new TextureImportResult(0, 0, new[] { $"贴图 {item.TextureName} 尺寸不匹配：生成 {width}x{height}，原始 {item.Width}x{item.Height}。" });
        }

        var records = LoadIndex().Records
            .ToDictionary(record => record.SourceHash, StringComparer.OrdinalIgnoreCase);
        var fileName = TextureArchiveNaming.BuildOverrideFileName(item.SourceHash);
        var filePath = Path.Combine(_directory, fileName);
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(filePath, pngBytes);
        records[item.SourceHash] = new TextureOverrideRecord(
            item.SourceHash,
            fileName,
            width,
            height,
            DateTimeOffset.UtcNow)
        {
            FilePath = filePath
        };
        SaveIndex(records.Values);
        return new TextureImportResult(1, 0, Array.Empty<string>());
    }

    public TextureOverrideClearResult ClearOverrides()
    {
        var deleted = 0;
        var errors = new List<string>();
        foreach (var record in LoadIndex().Records)
        {
            try
            {
                if (File.Exists(record.FilePath))
                {
                    File.Delete(record.FilePath);
                    deleted++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"删除覆盖贴图失败：{record.FileName}，{ex.Message}");
            }
        }

        try
        {
            if (File.Exists(_indexPath))
            {
                File.Delete(_indexPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"删除覆盖清单失败：{ex.Message}");
        }

        return new TextureOverrideClearResult(deleted, 0, errors);
    }

    private TextureExportManifest? ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry(ManifestEntryName);
        if (entry == null || entry.Length > MaxTextureEntryBytes)
        {
            return null;
        }

        using var reader = new StreamReader(entry.Open());
        return TextureManifestSerializer.Deserialize(reader.ReadToEnd());
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var output = new MemoryStream();
        using var input = entry.Open();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxTextureEntryBytes)
            {
                throw new InvalidDataException("Texture entry is too large.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private void SaveIndex(IEnumerable<TextureOverrideRecord> records)
    {
        Directory.CreateDirectory(_directory);
        var persisted = new PersistedTextureOverrideIndex(records
            .OrderBy(record => record.SourceHash, StringComparer.OrdinalIgnoreCase)
            .Select(record => new PersistedTextureOverrideRecord(
                record.SourceHash,
                TextureArchiveNaming.BuildOverrideFileName(record.SourceHash),
                record.Width,
                record.Height,
                record.UpdatedUtc))
            .ToArray());
        File.WriteAllText(_indexPath, JsonConvert.SerializeObject(persisted, Formatting.Indented));
    }

    private sealed record PersistedTextureOverrideIndex(PersistedTextureOverrideRecord[] Records);

    private sealed record PersistedTextureOverrideRecord(
        string SourceHash,
        string FileName,
        int Width,
        int Height,
        DateTimeOffset UpdatedUtc);
}
