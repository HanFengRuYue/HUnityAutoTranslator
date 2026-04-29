using FluentAssertions;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class BrandIconAssetTests
{
    [Fact]
    public void Generated_brand_icons_remove_edge_connected_white_background()
    {
        var brandingRoot = FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "public", "branding");

        var blueWhite = PngRgbaImage.Read(Path.Combine(brandingRoot, "hunity-icon-blue-white-512.png"));
        blueWhite.GetAlpha(0, 0).Should().Be(0);
        blueWhite.GetAlpha(20, 100).Should().Be(0);
        blueWhite.GetAlpha(255, 0).Should().Be(0);
        blueWhite.GetAlpha(60, 60).Should().BeGreaterThan(240);

        var whiteBlue = PngRgbaImage.Read(Path.Combine(brandingRoot, "hunity-icon-white-blue-512.png"));
        whiteBlue.GetAlpha(0, 0).Should().Be(0);
        whiteBlue.GetAlpha(20, 100).Should().Be(0);
        whiteBlue.GetAlpha(255, 0).Should().Be(0);
        whiteBlue.GetAlpha(60, 60).Should().BeGreaterThan(240);
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HUnityAutoTranslator.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("tests should run from inside the repository checkout");
        return Path.Combine(new[] { directory!.FullName }.Concat(relativeSegments).ToArray());
    }

    private sealed class PngRgbaImage
    {
        private readonly byte[] _pixels;

        private PngRgbaImage(int width, int height, byte[] pixels)
        {
            Width = width;
            Height = height;
            _pixels = pixels;
        }

        private int Width { get; }

        private int Height { get; }

        public static PngRgbaImage Read(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            var signature = reader.ReadBytes(8);
            signature.Should().Equal(137, 80, 78, 71, 13, 10, 26, 10);

            var width = 0;
            var height = 0;
            var idat = new MemoryStream();

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                var length = ReadBigEndianInt32(reader);
                var chunkType = new string(reader.ReadChars(4));
                var data = reader.ReadBytes(length);
                _ = reader.ReadBytes(4);

                if (chunkType == "IHDR")
                {
                    width = ReadBigEndianInt32(data, 0);
                    height = ReadBigEndianInt32(data, 4);
                    data[8].Should().Be(8, "brand icons should be 8-bit PNG files");
                    data[9].Should().Be(6, "brand icons should be RGBA PNG files");
                }
                else if (chunkType == "IDAT")
                {
                    idat.Write(data, 0, data.Length);
                }
                else if (chunkType == "IEND")
                {
                    break;
                }
            }

            width.Should().BeGreaterThan(0);
            height.Should().BeGreaterThan(0);
            return new PngRgbaImage(width, height, DecodeRgba(width, height, idat.ToArray()));
        }

        public byte GetAlpha(int x, int y)
        {
            x.Should().BeInRange(0, Width - 1);
            y.Should().BeInRange(0, Height - 1);
            return _pixels[((y * Width + x) * 4) + 3];
        }

        private static byte[] DecodeRgba(int width, int height, byte[] compressed)
        {
            using var compressedStream = new MemoryStream(compressed);
            using var zlib = new System.IO.Compression.ZLibStream(compressedStream, System.IO.Compression.CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            zlib.CopyTo(decompressed);

            var source = decompressed.ToArray();
            var rowLength = width * 4;
            var output = new byte[rowLength * height];
            var sourceOffset = 0;

            for (var y = 0; y < height; y++)
            {
                var filter = source[sourceOffset++];
                var rowStart = y * rowLength;
                Array.Copy(source, sourceOffset, output, rowStart, rowLength);
                sourceOffset += rowLength;

                for (var x = 0; x < rowLength; x++)
                {
                    var left = x >= 4 ? output[rowStart + x - 4] : 0;
                    var up = y > 0 ? output[rowStart + x - rowLength] : 0;
                    var upLeft = x >= 4 && y > 0 ? output[rowStart + x - rowLength - 4] : 0;
                    var predictor = filter switch
                    {
                        0 => 0,
                        1 => left,
                        2 => up,
                        3 => (left + up) / 2,
                        4 => Paeth(left, up, upLeft),
                        _ => throw new InvalidDataException($"Unsupported PNG filter type: {filter}")
                    };

                    output[rowStart + x] = unchecked((byte)(output[rowStart + x] + predictor));
                }
            }

            return output;
        }

        private static int Paeth(int left, int up, int upLeft)
        {
            var estimate = left + up - upLeft;
            var leftDistance = Math.Abs(estimate - left);
            var upDistance = Math.Abs(estimate - up);
            var upLeftDistance = Math.Abs(estimate - upLeft);
            if (leftDistance <= upDistance && leftDistance <= upLeftDistance)
            {
                return left;
            }

            return upDistance <= upLeftDistance ? up : upLeft;
        }

        private static int ReadBigEndianInt32(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(4);
            return ReadBigEndianInt32(bytes, 0);
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        }
    }
}
