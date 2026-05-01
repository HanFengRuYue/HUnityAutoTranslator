using System.IO.Compression;
using System.Text;

namespace HUnityAutoTranslator.Core.Tests.Textures;

internal static class FilteredPngBuilder
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static byte[] RgbaPng(int width, int height, byte[] rgba, byte filter)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException("RGBA data length does not match image dimensions.", nameof(rgba));
        }

        var raw = BuildFilteredRows(width, height, rgba, filter);
        using var stream = new MemoryStream();
        stream.Write(Signature, 0, Signature.Length);
        WriteChunk(stream, "IHDR", BuildHeader(width, height));
        WriteChunk(stream, "IDAT", DeflateZlib(raw));
        WriteChunk(stream, "IEND", Array.Empty<byte>());
        return stream.ToArray();
    }

    public static byte[] RgbPng(int width, int height, byte[] rgb, byte filter)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (rgb.Length != width * height * 3)
        {
            throw new ArgumentException("RGB data length does not match image dimensions.", nameof(rgb));
        }

        var raw = BuildFilteredRows(width, height, rgb, filter, bytesPerPixel: 3);
        using var stream = new MemoryStream();
        stream.Write(Signature, 0, Signature.Length);
        WriteChunk(stream, "IHDR", BuildHeader(width, height, colorType: 2));
        WriteChunk(stream, "IDAT", DeflateZlib(raw));
        WriteChunk(stream, "IEND", Array.Empty<byte>());
        return stream.ToArray();
    }

    private static byte[] BuildFilteredRows(int width, int height, byte[] rgba, byte filter)
    {
        return BuildFilteredRows(width, height, rgba, filter, bytesPerPixel: 4);
    }

    private static byte[] BuildFilteredRows(int width, int height, byte[] pixels, byte filter, int bytesPerPixel)
    {
        var stride = width * bytesPerPixel;
        var raw = new byte[(stride + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var sourceRow = y * stride;
            var rawRow = y * (stride + 1);
            raw[rawRow] = filter;
            for (var x = 0; x < stride; x++)
            {
                var left = x >= bytesPerPixel ? pixels[sourceRow + x - bytesPerPixel] : 0;
                var up = y > 0 ? pixels[sourceRow + x - stride] : 0;
                var upLeft = x >= bytesPerPixel && y > 0 ? pixels[sourceRow + x - stride - bytesPerPixel] : 0;
                var predictor = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => throw new ArgumentOutOfRangeException(nameof(filter))
                };
                raw[rawRow + 1 + x] = unchecked((byte)(pixels[sourceRow + x] - predictor));
            }
        }

        return raw;
    }

    private static byte[] BuildHeader(int width, int height, byte colorType = 6)
    {
        var data = new byte[13];
        WriteBigEndian(data, 0, width);
        WriteBigEndian(data, 4, height);
        data[8] = 8;
        data[9] = colorType;
        return data;
    }

    private static byte[] DeflateZlib(byte[] raw)
    {
        using var deflated = new MemoryStream();
        deflated.WriteByte(0x78);
        deflated.WriteByte(0x9c);
        using (var compression = new DeflateStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
        {
            compression.Write(raw, 0, raw.Length);
        }

        WriteBigEndian(deflated, Adler32(raw));
        return deflated.ToArray();
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

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        WriteBigEndian(stream, data.Length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes, 0, typeBytes.Length);
        stream.Write(data, 0, data.Length);

        var crcBytes = new byte[typeBytes.Length + data.Length];
        Array.Copy(typeBytes, crcBytes, typeBytes.Length);
        Array.Copy(data, 0, crcBytes, typeBytes.Length, data.Length);
        WriteBigEndian(stream, Crc32(crcBytes));
    }

    private static void WriteBigEndian(Stream stream, int value)
    {
        stream.WriteByte((byte)((value >> 24) & 0xff));
        stream.WriteByte((byte)((value >> 16) & 0xff));
        stream.WriteByte((byte)((value >> 8) & 0xff));
        stream.WriteByte((byte)(value & 0xff));
    }

    private static void WriteBigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)((value >> 24) & 0xff);
        bytes[offset + 1] = (byte)((value >> 16) & 0xff);
        bytes[offset + 2] = (byte)((value >> 8) & 0xff);
        bytes[offset + 3] = (byte)(value & 0xff);
    }

    private static int Adler32(byte[] bytes)
    {
        const int modulus = 65521;
        var a = 1;
        var b = 0;
        foreach (var value in bytes)
        {
            a = (a + value) % modulus;
            b = (b + a) % modulus;
        }

        return (b << 16) | a;
    }

    private static int Crc32(byte[] bytes)
    {
        uint crc = 0xffffffff;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 1 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
            }
        }

        return unchecked((int)(crc ^ 0xffffffff));
    }
}
