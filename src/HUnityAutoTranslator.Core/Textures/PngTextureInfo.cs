namespace HUnityAutoTranslator.Core.Textures;

public static class PngTextureInfo
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static bool TryReadDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 24 || !Signature.SequenceEqual(bytes.Take(Signature.Length)))
        {
            return false;
        }

        if (bytes[12] != (byte)'I' ||
            bytes[13] != (byte)'H' ||
            bytes[14] != (byte)'D' ||
            bytes[15] != (byte)'R')
        {
            return false;
        }

        width = ReadBigEndianInt32(bytes, 16);
        height = ReadBigEndianInt32(bytes, 20);
        return width > 0 && height > 0;
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24) |
            (bytes[offset + 1] << 16) |
            (bytes[offset + 2] << 8) |
            bytes[offset + 3];
    }
}
