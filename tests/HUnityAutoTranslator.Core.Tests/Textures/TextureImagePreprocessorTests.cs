using FluentAssertions;
using HUnityAutoTranslator.Core.Textures;

namespace HUnityAutoTranslator.Core.Tests.Textures;

public sealed class TextureImagePreprocessorTests
{
    [Fact]
    public void PrepareForEdit_upscales_small_texture_to_valid_image_size_and_restores_original_dimensions()
    {
        var original = new PngRgbaImage(64, 32, CreateSolidRgba(64, 32, 20, 30, 40, 255)).Encode();

        var prepared = TextureImagePreprocessor.PrepareForEdit(original);

        prepared.RequestSize.Should().Be("1024x1024");
        PngTextureInfo.TryReadDimensions(prepared.PngBytes, out var preparedWidth, out var preparedHeight).Should().BeTrue();
        preparedWidth.Should().Be(1024);
        preparedHeight.Should().Be(1024);

        var generated = new PngRgbaImage(1024, 1024, CreateSolidRgba(1024, 1024, 180, 90, 40, 255)).Encode();
        var restored = TextureImagePreprocessor.RestoreGeneratedImage(prepared, generated);

        PngTextureInfo.TryReadDimensions(restored, out var width, out var height).Should().BeTrue();
        width.Should().Be(64);
        height.Should().Be(32);
    }

    [Fact]
    public void RestoreGeneratedImage_reapplies_original_alpha_mask()
    {
        var rgba = CreateSolidRgba(32, 32, 20, 30, 40, 255);
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                rgba[(y * 32 + x) * 4 + 3] = 0;
            }
        }

        var original = new PngRgbaImage(32, 32, rgba).Encode();
        var prepared = TextureImagePreprocessor.PrepareForEdit(original);
        var generated = new PngRgbaImage(1024, 1024, CreateSolidRgba(1024, 1024, 220, 220, 220, 255)).Encode();

        var restored = TextureImagePreprocessor.RestoreGeneratedImage(prepared, generated);

        var decoded = PngRgbaImage.Decode(restored);
        decoded.Rgba[(8 * 32 + 8) * 4 + 3].Should().Be(0);
        decoded.Rgba[(8 * 32 + 24) * 4 + 3].Should().Be(255);
    }

    [Fact]
    public void PrepareForEdit_and_restore_accept_paeth_filtered_png_images()
    {
        var originalRgba = CreateGradientRgba(4, 3);
        var original = FilteredPngBuilder.RgbaPng(4, 3, originalRgba, 4);

        var prepared = TextureImagePreprocessor.PrepareForEdit(original);

        prepared.OriginalWidth.Should().Be(4);
        prepared.OriginalHeight.Should().Be(3);

        var generatedRgba = CreateSolidRgba(1024, 1024, 220, 180, 90, 255);
        var generated = FilteredPngBuilder.RgbaPng(1024, 1024, generatedRgba, 4);
        var restored = TextureImagePreprocessor.RestoreGeneratedImage(prepared, generated);

        PngTextureInfo.TryReadDimensions(restored, out var width, out var height).Should().BeTrue();
        width.Should().Be(4);
        height.Should().Be(3);
        var decoded = PngRgbaImage.Decode(restored);
        decoded.Rgba[0].Should().Be(220);
        decoded.Rgba[1].Should().Be(180);
        decoded.Rgba[2].Should().Be(90);
        decoded.Rgba[3].Should().Be(originalRgba[3]);
    }

    [Fact]
    public void PrepareForEdit_accepts_rgb_png_source_images()
    {
        var originalRgb = CreateGradientRgb(4, 3);
        var original = FilteredPngBuilder.RgbPng(4, 3, originalRgb, 4);

        var prepared = TextureImagePreprocessor.PrepareForEdit(original);
        var generated = new PngRgbaImage(1024, 1024, CreateSolidRgba(1024, 1024, 120, 140, 160, 255)).Encode();
        var restored = TextureImagePreprocessor.RestoreGeneratedImage(prepared, generated);

        var decoded = PngRgbaImage.Decode(restored);
        decoded.Width.Should().Be(4);
        decoded.Height.Should().Be(3);
        decoded.Rgba[0].Should().Be(120);
        decoded.Rgba[1].Should().Be(140);
        decoded.Rgba[2].Should().Be(160);
        decoded.Rgba[3].Should().Be(255);
    }

    private static byte[] CreateSolidRgba(int width, int height, byte r, byte g, byte b, byte a)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = r;
            rgba[i + 1] = g;
            rgba[i + 2] = b;
            rgba[i + 3] = a;
        }

        return rgba;
    }

    private static byte[] CreateGradientRgb(int width, int height)
    {
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = ((y * width) + x) * 3;
                rgb[index] = (byte)(20 + x * 30 + y * 7);
                rgb[index + 1] = (byte)(40 + x * 11 + y * 23);
                rgb[index + 2] = (byte)(60 + x * 17 + y * 13);
            }
        }

        return rgb;
    }

    private static byte[] CreateGradientRgba(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = ((y * width) + x) * 4;
                rgba[index] = (byte)(20 + x * 30 + y * 7);
                rgba[index + 1] = (byte)(40 + x * 11 + y * 23);
                rgba[index + 2] = (byte)(60 + x * 17 + y * 13);
                rgba[index + 3] = (byte)(180 + x * 5 + y * 9);
            }
        }

        return rgba;
    }
}
