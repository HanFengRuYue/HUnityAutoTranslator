using System.IO.Compression;
using FluentAssertions;
using HUnityAutoTranslator.Core.Textures;

namespace HUnityAutoTranslator.Core.Tests.Textures;

public sealed class TextureCatalogStoreTests
{
    [Fact]
    public void Catalog_persists_sources_and_merges_same_hash_references_across_scenes()
    {
        using var temp = new TemporaryDirectory();
        var store = new TextureCatalogStore(Path.Combine(temp.Path, "texture-catalog"));
        var png = PngBytes(1, 1);

        store.Upsert(CatalogItem("abc123", "Logo", "MainMenu", "Canvas/Logo"), png);
        store.Upsert(CatalogItem("abc123", "Logo", "Game", "World/Logo"), png);
        store.Upsert(CatalogItem("abc123", "Logo", "MainMenu", "Canvas/Logo"), png);
        store.Save();

        var reloaded = new TextureCatalogStore(Path.Combine(temp.Path, "texture-catalog"));
        var page = reloaded.Query(new TextureCatalogQuery(null, 0, 10), TextureOverrideIndex.Empty);

        page.TotalCount.Should().Be(1);
        page.FilteredCount.Should().Be(1);
        page.ReferenceCount.Should().Be(2);
        page.Scenes.Should().Equal("Game", "MainMenu");
        page.Items.Should().ContainSingle();
        page.Items[0].References.Select(reference => reference.SceneName).Should().BeEquivalentTo("MainMenu", "Game");
        page.Items[0].ReferenceCount.Should().Be(2);
        reloaded.TryReadSourceBytes("abc123", out var persisted).Should().BeTrue();
        persisted.Should().Equal(png);
    }

    [Fact]
    public void Query_filters_by_scene_and_paginates_without_losing_total_counts()
    {
        using var temp = new TemporaryDirectory();
        var store = new TextureCatalogStore(Path.Combine(temp.Path, "texture-catalog"));
        var png = PngBytes(1, 1);

        store.Upsert(CatalogItem("hash-a", "A", "SceneA", "Canvas/A"), png);
        store.Upsert(CatalogItem("hash-b", "B", "SceneB", "Canvas/B"), png);
        store.Upsert(CatalogItem("hash-c", "C", "SceneB", "Canvas/C"), png);
        store.Save();

        var page = store.Query(new TextureCatalogQuery("SceneB", 1, 1), TextureOverrideIndex.Empty);

        page.TotalCount.Should().Be(3);
        page.FilteredCount.Should().Be(2);
        page.Offset.Should().Be(1);
        page.Limit.Should().Be(1);
        page.Items.Should().ContainSingle();
        page.Items[0].References.Should().OnlyContain(reference => reference.SceneName == "SceneB");
        page.Items[0].ReferenceCount.Should().Be(1);
    }

    [Fact]
    public void Query_marks_items_with_persisted_overrides()
    {
        using var temp = new TemporaryDirectory();
        var catalogStore = new TextureCatalogStore(Path.Combine(temp.Path, "texture-catalog"));
        catalogStore.Upsert(CatalogItem("abc123", "Logo", "MainMenu", "Canvas/Logo"), PngBytes(1, 1));
        catalogStore.Save();

        var overrideIndex = new TextureOverrideIndex(new[]
        {
            new TextureOverrideRecord("abc123", "abc123.png", 1, 1, DateTimeOffset.UtcNow)
        });

        var page = catalogStore.Query(new TextureCatalogQuery(null, 0, 20), overrideIndex);

        page.Items.Should().ContainSingle();
        page.Items[0].HasOverride.Should().BeTrue();
        page.Items[0].OverrideUpdatedUtc.Should().Be(overrideIndex.Records[0].UpdatedUtc);
    }

    [Fact]
    public void Source_image_reader_rejects_unsafe_or_unknown_hashes()
    {
        using var temp = new TemporaryDirectory();
        var store = new TextureCatalogStore(Path.Combine(temp.Path, "texture-catalog"));
        store.Upsert(CatalogItem("abc123", "Logo", "MainMenu", "Canvas/Logo"), PngBytes(1, 1));
        store.Save();

        store.TryReadSourceBytes("../abc123", out _).Should().BeFalse();
        store.TryReadSourceBytes("abc123/other", out _).Should().BeFalse();
        store.TryReadSourceBytes("missing", out _).Should().BeFalse();
        store.TryReadSourceBytes("abc123", out var bytes).Should().BeTrue();
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void ExportArchive_can_export_the_current_scene_filter_without_current_page_limits()
    {
        using var temp = new TemporaryDirectory();
        var catalogStore = new TextureCatalogStore(Path.Combine(temp.Path, "texture-catalog"));
        var overrideStore = new TextureOverrideStore(Path.Combine(temp.Path, "texture-overrides"));
        var png = PngBytes(1, 1);

        catalogStore.Upsert(CatalogItem("hash-a", "A", "SceneA", "Canvas/A"), png);
        catalogStore.Upsert(CatalogItem("hash-b", "B", "SceneB", "Canvas/B"), png);
        catalogStore.Upsert(CatalogItem("hash-c", "C", "SceneB", "Canvas/C"), png);
        catalogStore.Save();

        var filtered = catalogStore.Query(new TextureCatalogQuery("SceneB", 0, int.MaxValue), TextureOverrideIndex.Empty);
        var archive = overrideStore.ExportArchive(
            filtered.Items,
            item => catalogStore.TryReadSourceBytes(item.SourceHash, out var bytes) ? bytes : Array.Empty<byte>(),
            "Test Game");

        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        zip.GetEntry("textures/hasha-a.png").Should().BeNull();
        zip.GetEntry("textures/hashb-b.png").Should().NotBeNull();
        zip.GetEntry("textures/hashc-c.png").Should().NotBeNull();
    }

    private static TextureCatalogItem CatalogItem(string hash, string name, string scene, string hierarchy)
    {
        return new TextureCatalogItem(
            hash,
            name,
            Width: 1,
            Height: 1,
            Format: "RGBA32",
            FileName: TextureArchiveNaming.BuildTextureEntryName(hash, name),
            ReferenceCount: 1,
            new[] { new TextureReferenceInfo($"{scene}:{hierarchy}", scene, hierarchy, "UnityEngine.UI.Image") },
            HasOverride: false,
            OverrideUpdatedUtc: null);
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hunity-texture-catalog-{Guid.NewGuid():N}");
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
