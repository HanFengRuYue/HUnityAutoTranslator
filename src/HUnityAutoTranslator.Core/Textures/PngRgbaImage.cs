using System.IO.Compression;

namespace HUnityAutoTranslator.Core.Textures;

public sealed record PngRgbaImage(int Width, int Height, byte[] Rgba)
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public byte[] Encode()
    {
        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width));
        }

        if (Rgba.Length != Width * Height * 4)
        {
            throw new ArgumentException("RGBA data length does not match image dimensions.", nameof(Rgba));
        }

        using var stream = new MemoryStream();
        stream.Write(Signature, 0, Signature.Length);
        WriteChunk(stream, "IHDR", BuildHeader());
        WriteChunk(stream, "IDAT", BuildImageData());
        WriteChunk(stream, "IEND", Array.Empty<byte>());
        return stream.ToArray();
    }

    public static PngRgbaImage Decode(byte[] pngBytes)
    {
        if (!PngTextureInfo.TryReadDimensions(pngBytes, out var width, out var height))
        {
            throw new InvalidDataException("Invalid PNG image.");
        }

        var idat = new List<byte>();
        var bitDepth = -1;
        var colorType = -1;
        var offset = Signature.Length;
        while (offset + 8 <= pngBytes.Length)
        {
            var length = ReadBigEndianInt32(pngBytes, offset);
            var type = System.Text.Encoding.ASCII.GetString(pngBytes, offset + 4, 4);
            offset += 8;
            if (offset + length + 4 > pngBytes.Length)
            {
                throw new InvalidDataException("PNG chunk length is invalid.");
            }

            if (type == "IHDR")
            {
                bitDepth = pngBytes[offset + 8];
                colorType = pngBytes[offset + 9];
            }
            else if (type == "IDAT")
            {
                idat.AddRange(pngBytes.Skip(offset).Take(length));
            }
            else if (type == "IEND")
            {
                break;
            }

            offset += length + 4;
        }

        if (bitDepth != 8 || colorType is not 2 and not 6)
        {
            throw new NotSupportedException("Only 8-bit RGB/RGBA PNG images are supported.");
        }

        var raw = InflateZlib(idat.ToArray());
        var bytesPerPixel = colorType == 6 ? 4 : 3;
        var sourceStride = width * bytesPerPixel;
        var targetStride = width * 4;
        var rgba = new byte[width * height * 4];
        var source = 0;
        var previous = new byte[sourceStride];
        var current = new byte[sourceStride];
        for (var y = 0; y < height; y++)
        {
            if (source >= raw.Length)
            {
                throw new InvalidDataException("PNG image data is truncated.");
            }

            var filter = raw[source++];
            if (source + sourceStride > raw.Length)
            {
                throw new InvalidDataException("PNG image data is truncated.");
            }

            Array.Copy(raw, source, current, 0, sourceStride);
            source += sourceStride;
            UnfilterScanline(current, previous, filter, bytesPerPixel);
            if (colorType == 6)
            {
                Array.Copy(current, 0, rgba, y * targetStride, targetStride);
            }
            else
            {
                CopyRgbToRgba(current, rgba, y * targetStride);
            }

            (previous, current) = (current, previous);
            Array.Clear(current, 0, current.Length);
        }

        return new PngRgbaImage(width, height, rgba);
    }

    private static void CopyRgbToRgba(byte[] rgb, byte[] rgba, int targetOffset)
    {
        for (int source = 0, target = targetOffset; source < rgb.Length; source += 3, target += 4)
        {
            rgba[target] = rgb[source];
            rgba[target + 1] = rgb[source + 1];
            rgba[target + 2] = rgb[source + 2];
            rgba[target + 3] = 255;
        }
    }

    private byte[] BuildHeader()
    {
        var data = new byte[13];
        WriteBigEndian(data, 0, Width);
        WriteBigEndian(data, 4, Height);
        data[8] = 8;
        data[9] = 6;
        return data;
    }

    private byte[] BuildImageData()
    {
        var stride = Width * 4;
        var raw = new byte[(stride + 1) * Height];
        for (var y = 0; y < Height; y++)
        {
            var rowStart = y * (stride + 1);
            raw[rowStart] = 0;
            Array.Copy(Rgba, y * stride, raw, rowStart + 1, stride);
        }

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

    private static byte[] InflateZlib(byte[] bytes)
    {
        var offset = bytes.Length > 2 && bytes[0] == 0x78 ? 2 : 0;
        var length = offset == 2 && bytes.Length > 6 ? bytes.Length - 6 : bytes.Length - offset;
        using var input = new MemoryStream(bytes, offset, length);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    private static void UnfilterScanline(byte[] current, byte[] previous, int filter, int bytesPerPixel)
    {
        switch (filter)
        {
            case 0:
                return;
            case 1:
                for (var i = bytesPerPixel; i < current.Length; i++)
                {
                    current[i] = unchecked((byte)(current[i] + current[i - bytesPerPixel]));
                }

                return;
            case 2:
                for (var i = 0; i < current.Length; i++)
                {
                    current[i] = unchecked((byte)(current[i] + previous[i]));
                }

                return;
            case 3:
                for (var i = 0; i < current.Length; i++)
                {
                    var left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                    var up = previous[i];
                    current[i] = unchecked((byte)(current[i] + ((left + up) / 2)));
                }

                return;
            case 4:
                for (var i = 0; i < current.Length; i++)
                {
                    var left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                    var up = previous[i];
                    var upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                    current[i] = unchecked((byte)(current[i] + Paeth(left, up, upLeft)));
                }

                return;
            default:
                throw new NotSupportedException($"PNG filter {filter} is not supported.");
        }
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
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
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

    private static int ReadBigEndianInt32(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24) |
            (bytes[offset + 1] << 16) |
            (bytes[offset + 2] << 8) |
            bytes[offset + 3];
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
