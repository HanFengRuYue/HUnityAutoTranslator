using System.IO.Compression;
using FluentAssertions;
using HUnityAutoTranslator.Core.Textures;

namespace HUnityAutoTranslator.Core.Tests.Textures;

public sealed class TextureOverrideStoreTests
{
    [Fact]
    public void ExportArchive_writes_manifest_and_texture_png_entries()
    {
        using var temp = new TemporaryDirectory();
        var store = new TextureOverrideStore(Path.Combine(temp.Path, "texture-overrides"));
        var png = PngBytes(width: 1, height: 1);
        var item = CatalogItem("abc123", "Main Menu Logo", 1, 1);

        var archive = store.ExportArchive(
            new TextureCatalogItem[] { item },
            _ => png,
            "Test Game");

        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        var manifestEntry = zip.GetEntry("manifest.json");
        manifestEntry.Should().NotBeNull();
        zip.GetEntry("textures/abc123-main-menu-logo.png").Should().NotBeNull();
        ReadEntryText(manifestEntry!).Should().Contain("\"GameTitle\":\"Test Game\"");
        ReadEntryText(manifestEntry!).Should().Contain("\"SourceHash\":\"abc123\"");
    }

    [Fact]
    public void ImportArchive_persists_matching_png_and_updates_override_index()
    {
        using var temp = new TemporaryDirectory();
        var store = new TextureOverrideStore(Path.Combine(temp.Path, "texture-overrides"));
        var catalog = new[] { CatalogItem("abc123", "Logo", 1, 1) };
        var archive = BuildArchive(
            new TextureExportManifest(
                TextureExportManifest.CurrentFormatVersion,
                "Test Game",
                DateTimeOffset.UtcNow,
                new[]
                {
                    TextureManifestItem.FromCatalogItem(catalog[0])
                }),
            ("textures/abc123-logo.png", PngBytes(1, 1)));

        var result = store.ImportArchive(archive, catalog);

        result.ImportedCount.Should().Be(1);
        result.Errors.Should().BeEmpty();
        store.TryGetOverride("abc123", out var record).Should().BeTrue();
        record!.SourceHash.Should().Be("abc123");
        File.Exists(record.FilePath).Should().BeTrue();
        store.LoadIndex().Records.Should().ContainSingle(item => item.SourceHash == "abc123");
    }

    [Fact]
    public void ImportArchive_rejects_unsafe_manifest_paths()
    {
        using var temp = new TemporaryDirectory();
        var store = new TextureOverrideStore(Path.Combine(temp.Path, "texture-overrides"));
        var catalog = new[] { CatalogItem("abc123", "Logo", 1, 1) };
        var archive = BuildArchive(
            new TextureExportManifest(
                TextureExportManifest.CurrentFormatVersion,
                "Test Game",
                DateTimeOffset.UtcNow,
                new[]
                {
                    new TextureManifestItem(
                        "abc123",
                        "../bad.png",
                        "Logo",
                        1,
                        1,
                        "RGBA32",
                        1,
                        Array.Empty<TextureReferenceInfo>())
                }),
            ("../bad.png", PngBytes(1, 1)));

        var result = store.ImportArchive(archive, catalog);

        result.ImportedCount.Should().Be(0);
        result.Errors.Should().Contain(error => error.Contains("不安全", StringComparison.Ordinal));
        store.TryGetOverride("abc123", out _).Should().BeFalse();
        File.Exists(Path.Combine(temp.Path, "bad.png")).Should().BeFalse();
    }

    [Fact]
    public void ImportArchive_rejects_png_dimensions_that_do_not_match_catalog()
    {
        using var temp = new TemporaryDirectory();
        var store = new TextureOverrideStore(Path.Combine(temp.Path, "texture-overrides"));
        var catalog = new[] { CatalogItem("abc123", "Logo", 1, 1) };
        var archive = BuildArchive(
            new TextureExportManifest(
                TextureExportManifest.CurrentFormatVersion,
                "Test Game",
                DateTimeOffset.UtcNow,
                new[]
                {
                    TextureManifestItem.FromCatalogItem(catalog[0])
                }),
            ("textures/abc123-logo.png", PngBytes(2, 1)));

        var result = store.ImportArchive(archive, catalog);

        result.ImportedCount.Should().Be(0);
        result.Errors.Should().Contain(error => error.Contains("尺寸", StringComparison.Ordinal));
        store.TryGetOverride("abc123", out _).Should().BeFalse();
    }

    [Fact]
    public void ClearOverrides_removes_png_files_and_manifest()
    {
        using var temp = new TemporaryDirectory();
        var store = new TextureOverrideStore(Path.Combine(temp.Path, "texture-overrides"));
        var catalog = new[] { CatalogItem("abc123", "Logo", 1, 1) };
        store.ImportArchive(
            BuildArchive(
                new TextureExportManifest(
                    TextureExportManifest.CurrentFormatVersion,
                    "Test Game",
                    DateTimeOffset.UtcNow,
                    new[] { TextureManifestItem.FromCatalogItem(catalog[0]) }),
                ("textures/abc123-logo.png", PngBytes(1, 1))),
            catalog);

        var result = store.ClearOverrides();

        result.DeletedCount.Should().Be(1);
        store.LoadIndex().Records.Should().BeEmpty();
        Directory.EnumerateFiles(Path.Combine(temp.Path, "texture-overrides"), "*.png").Should().BeEmpty();
    }

    private static TextureCatalogItem CatalogItem(string hash, string name, int width, int height)
    {
        return new TextureCatalogItem(
            hash,
            name,
            width,
            height,
            "RGBA32",
            TextureArchiveNaming.BuildTextureEntryName(hash, name),
            ReferenceCount: 1,
            new[]
            {
                new TextureReferenceInfo("42", "MainMenu", "Canvas/Logo", "UnityEngine.UI.Image")
            },
            HasOverride: false,
            OverrideUpdatedUtc: null);
    }

    private static byte[] BuildArchive(TextureExportManifest manifest, params (string Name, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = zip.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write(TextureManifestSerializer.Serialize(manifest));
            }

            foreach (var entry in entries)
            {
                var zipEntry = zip.CreateEntry(entry.Name);
                using var entryStream = zipEntry.Open();
                entryStream.Write(entry.Bytes, 0, entry.Bytes.Length);
            }
        }

        return stream.ToArray();
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static byte[] PngBytes(int width, int height)
    {
        var bytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;
    }

    private static void WriteBigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)((value >> 24) & 0xff);
        bytes[offset + 1] = (byte)((value >> 16) & 0xff);
        bytes[offset + 2] = (byte)((value >> 8) & 0xff);
        bytes[offset + 3] = (byte)(value & 0xff);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hunity-textures-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
