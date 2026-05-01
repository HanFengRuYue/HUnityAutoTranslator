using FluentAssertions;
using HUnityAutoTranslator.Core.Textures;

namespace HUnityAutoTranslator.Core.Tests.Textures;

public sealed class TextureTextDetectorTests
{
    [Fact]
    public void Analyze_keeps_poster_or_art_text_as_manual_review_candidate()
    {
        var item = CatalogItem("poster001", "MainMenu_Poster_Title", width: 256, height: 128, componentType: "UnityEngine.UI.RawImage");
        var png = PosterLikePng(256, 128);

        var analysis = TextureTextDetector.Analyze(item, png, DateTimeOffset.Parse("2026-05-01T10:00:00Z"));

        analysis.Status.Should().Be(TextureTextStatus.NeedsManualReview);
        analysis.NeedsManualReview.Should().BeTrue();
        analysis.Confidence.Should().BeGreaterThan(0.5);
        analysis.Reason.Should().Contain("poster");
    }

    [Fact]
    public void Analyze_filters_obvious_flat_texture_as_likely_no_text()
    {
        var item = CatalogItem("flat001", "Wall_Diffuse", width: 128, height: 128, componentType: "UnityEngine.Renderer");
        var png = SolidPng(128, 128, r: 72, g: 84, b: 92, a: 255);

        var analysis = TextureTextDetector.Analyze(item, png, DateTimeOffset.Parse("2026-05-01T10:00:00Z"));

        analysis.Status.Should().Be(TextureTextStatus.LikelyNoText);
        analysis.NeedsManualReview.Should().BeFalse();
        analysis.Confidence.Should().BeLessThan(0.35);
    }

    [Fact]
    public void Analyze_filters_named_normal_map_even_when_it_has_many_edges()
    {
        var item = CatalogItem("normal001", "Poster_Normal_NRM", width: 128, height: 128, componentType: "UnityEngine.Renderer");
        var png = CheckerPng(128, 128);

        var analysis = TextureTextDetector.Analyze(item, png, DateTimeOffset.Parse("2026-05-01T10:00:00Z"));

        analysis.Status.Should().Be(TextureTextStatus.LikelyNoText);
        analysis.Reason.Should().Contain("normal");
    }

    private static TextureCatalogItem CatalogItem(string hash, string name, int width, int height, string componentType)
    {
        return new TextureCatalogItem(
            hash,
            name,
            width,
            height,
            "RGBA32",
            TextureArchiveNaming.BuildTextureEntryName(hash, name),
            ReferenceCount: 1,
            new[] { new TextureReferenceInfo("target", "Scene", "Canvas/Poster", componentType) },
            HasOverride: false,
            OverrideUpdatedUtc: null);
    }

    private static byte[] PosterLikePng(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                SetPixel(rgba, width, x, y, 28, 32, 44, 255);
            }
        }

        DrawBlockText(rgba, width, 28, 34, 7, 34, 230, 220, 180);
        return new PngRgbaImage(width, height, rgba).Encode();
    }

    private static byte[] SolidPng(int width, int height, byte r, byte g, byte b, byte a)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                SetPixel(rgba, width, x, y, r, g, b, a);
            }
        }

        return new PngRgbaImage(width, height, rgba).Encode();
    }

    private static byte[] CheckerPng(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = ((x / 4) + (y / 4)) % 2 == 0 ? (byte)80 : (byte)180;
                SetPixel(rgba, width, x, y, value, value, 230, 255);
            }
        }

        return new PngRgbaImage(width, height, rgba).Encode();
    }

    private static void DrawBlockText(byte[] rgba, int width, int left, int top, int glyphWidth, int glyphHeight, byte r, byte g, byte b)
    {
        for (var glyph = 0; glyph < 6; glyph++)
        {
            var x0 = left + glyph * (glyphWidth + 8);
            for (var x = x0; x < x0 + glyphWidth; x++)
            {
                for (var y = top; y < top + glyphHeight; y++)
                {
                    if (x == x0 || x == x0 + glyphWidth - 1 || y == top || y == top + glyphHeight / 2 || y == top + glyphHeight - 1)
                    {
                        SetPixel(rgba, width, x, y, r, g, b, 255);
                    }
                }
            }
        }
    }

    private static void SetPixel(byte[] rgba, int width, int x, int y, byte r, byte g, byte b, byte a)
    {
        var index = (y * width + x) * 4;
        rgba[index] = r;
        rgba[index + 1] = g;
        rgba[index + 2] = b;
        rgba[index + 3] = a;
    }
}
