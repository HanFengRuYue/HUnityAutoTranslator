using FluentAssertions;
using HUnityAutoTranslator.Core.Textures;

namespace HUnityAutoTranslator.Core.Tests.Textures;

public sealed class PngRgbaImageTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Decode_supports_standard_average_and_paeth_png_filters(int filter)
    {
        var rgba = new byte[]
        {
            10, 20, 30, 255,
            70, 80, 90, 200,
            120, 130, 140, 160,
            23, 45, 67, 220,
            89, 101, 123, 180,
            144, 155, 166, 140
        };
        var png = FilteredPngBuilder.RgbaPng(3, 2, rgba, (byte)filter);

        var decoded = PngRgbaImage.Decode(png);

        decoded.Width.Should().Be(3);
        decoded.Height.Should().Be(2);
        decoded.Rgba.Should().Equal(rgba);
    }

    [Fact]
    public void Decode_converts_standard_rgb_png_to_opaque_rgba()
    {
        var rgb = new byte[]
        {
            10, 20, 30,
            70, 80, 90,
            120, 130, 140,
            23, 45, 67,
            89, 101, 123,
            144, 155, 166
        };
        var png = FilteredPngBuilder.RgbPng(3, 2, rgb, 4);

        var decoded = PngRgbaImage.Decode(png);

        decoded.Width.Should().Be(3);
        decoded.Height.Should().Be(2);
        decoded.Rgba.Should().Equal(new byte[]
        {
            10, 20, 30, 255,
            70, 80, 90, 255,
            120, 130, 140, 255,
            23, 45, 67, 255,
            89, 101, 123, 255,
            144, 155, 166, 255
        });
    }
}
