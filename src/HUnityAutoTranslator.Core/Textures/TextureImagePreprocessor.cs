namespace HUnityAutoTranslator.Core.Textures;

public static class TextureImagePreprocessor
{
    private const int EditCanvasSize = 1024;

    public static PreparedTextureImage PrepareForEdit(byte[] originalPng)
    {
        var original = PngRgbaImage.Decode(originalPng);
        var prepared = ResizeNearest(original, EditCanvasSize, EditCanvasSize);
        return new PreparedTextureImage(
            prepared.Encode(),
            $"{EditCanvasSize}x{EditCanvasSize}",
            original.Width,
            original.Height,
            ExtractAlpha(original.Rgba));
    }

    public static byte[] RestoreGeneratedImage(PreparedTextureImage prepared, byte[] generatedPng)
    {
        var generated = PngRgbaImage.Decode(generatedPng);
        var restored = ResizeNearest(generated, prepared.OriginalWidth, prepared.OriginalHeight);
        for (var i = 0; i < prepared.OriginalAlpha.Length; i++)
        {
            restored.Rgba[(i * 4) + 3] = prepared.OriginalAlpha[i];
        }

        return restored.Encode();
    }

    private static PngRgbaImage ResizeNearest(PngRgbaImage source, int targetWidth, int targetHeight)
    {
        var rgba = new byte[targetWidth * targetHeight * 4];
        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = Math.Min(source.Height - 1, y * source.Height / targetHeight);
            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = Math.Min(source.Width - 1, x * source.Width / targetWidth);
                var sourceIndex = ((sourceY * source.Width) + sourceX) * 4;
                var targetIndex = ((y * targetWidth) + x) * 4;
                rgba[targetIndex] = source.Rgba[sourceIndex];
                rgba[targetIndex + 1] = source.Rgba[sourceIndex + 1];
                rgba[targetIndex + 2] = source.Rgba[sourceIndex + 2];
                rgba[targetIndex + 3] = source.Rgba[sourceIndex + 3];
            }
        }

        return new PngRgbaImage(targetWidth, targetHeight, rgba);
    }

    private static byte[] ExtractAlpha(byte[] rgba)
    {
        var alpha = new byte[rgba.Length / 4];
        for (var i = 0; i < alpha.Length; i++)
        {
            alpha[i] = rgba[(i * 4) + 3];
        }

        return alpha;
    }
}

public sealed record PreparedTextureImage(
    byte[] PngBytes,
    string RequestSize,
    int OriginalWidth,
    int OriginalHeight,
    byte[] OriginalAlpha);
