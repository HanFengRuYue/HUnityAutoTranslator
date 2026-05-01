namespace HUnityAutoTranslator.Core.Textures;

public static class TextureTextDetector
{
    private static readonly string[] NoTextNameMarkers =
    {
        "normal",
        "_nrm",
        "nrm",
        "bump",
        "diffuse",
        "albedo"
    };

    private static readonly string[] TextCandidateNameMarkers =
    {
        "poster",
        "logo",
        "title",
        "menu",
        "button",
        "sign"
    };

    public static TextureTextAnalysis Analyze(TextureCatalogItem item, byte[] pngBytes, DateTimeOffset updatedUtc)
    {
        if (LooksLikeNoTextName(item.TextureName))
        {
            return new TextureTextAnalysis(
                item.SourceHash,
                TextureTextStatus.LikelyNoText,
                0.1,
                null,
                "normal or diffuse map name suggests no UI text",
                NeedsManualReview: false,
                UserReviewed: false,
                updatedUtc,
                LastError: null);
        }

        var score = EstimateContrastScore(pngBytes);
        if (LooksLikeTextCandidateName(item.TextureName))
        {
            score = Math.Max(score, 0.62);
        }

        if (score >= 0.5)
        {
            return new TextureTextAnalysis(
                item.SourceHash,
                TextureTextStatus.NeedsManualReview,
                score,
                null,
                "poster-like high contrast layout",
                NeedsManualReview: true,
                UserReviewed: false,
                updatedUtc,
                LastError: null);
        }

        return new TextureTextAnalysis(
            item.SourceHash,
            TextureTextStatus.LikelyNoText,
            score,
            null,
            "flat low-contrast texture",
            NeedsManualReview: false,
            UserReviewed: false,
            updatedUtc,
            LastError: null);
    }

    private static bool LooksLikeNoTextName(string name)
    {
        return NoTextNameMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeTextCandidateName(string name)
    {
        return TextCandidateNameMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static double EstimateContrastScore(byte[] pngBytes)
    {
        try
        {
            var image = PngRgbaImage.Decode(pngBytes);
            var min = 255;
            var max = 0;
            for (var i = 0; i < image.Rgba.Length; i += 4)
            {
                var luminance = (image.Rgba[i] * 299 + image.Rgba[i + 1] * 587 + image.Rgba[i + 2] * 114) / 1000;
                min = Math.Min(min, luminance);
                max = Math.Max(max, luminance);
            }

            return Math.Min(0.95, Math.Max(0, (max - min) / 255.0));
        }
        catch
        {
            return 0;
        }
    }
}
