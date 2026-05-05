namespace HUnityAutoTranslator.Core.Runtime;

public static class TextLayoutCompensation
{
    public const float DefaultFitRatio = 0.92f;
    public const float DefaultMinimumScale = 0.75f;

    private const float MultilineHeightRatio = 1.35f;
    private const float MinimumLineMetric = 0.001f;
    private const float MaximumLineSpacingMultiplier = 3f;
    private const float MinimumFontSizeDelta = 0.25f;

    public static bool TryCalculateUguiLineSpacing(
        float originalLineSpacing,
        float originalFontLineHeight,
        float currentFontLineHeight,
        out float adjustedLineSpacing)
    {
        adjustedLineSpacing = originalLineSpacing;
        if (!IsPositiveFinite(originalLineSpacing) ||
            !IsPositiveFinite(originalFontLineHeight) ||
            !IsPositiveFinite(currentFontLineHeight))
        {
            return false;
        }

        var scaled = originalLineSpacing * (originalFontLineHeight / currentFontLineHeight);
        if (!IsPositiveFinite(scaled))
        {
            return false;
        }

        var maximum = Math.Max(originalLineSpacing, originalLineSpacing * MaximumLineSpacingMultiplier);
        adjustedLineSpacing = Math.Min(Math.Max(originalLineSpacing, scaled), maximum);
        return true;
    }

    public static bool TryCalculateTmpLineSpacing(
        float originalLineSpacing,
        float originalFontLineHeight,
        float currentFontLineHeight,
        out float adjustedLineSpacing)
    {
        adjustedLineSpacing = originalLineSpacing;
        if (!IsFinite(originalLineSpacing) ||
            !IsPositiveFinite(originalFontLineHeight) ||
            !IsPositiveFinite(currentFontLineHeight) ||
            currentFontLineHeight >= originalFontLineHeight)
        {
            return false;
        }

        var extraSpacing = originalFontLineHeight - currentFontLineHeight;
        var maximum = originalLineSpacing + originalFontLineHeight * 2f;
        adjustedLineSpacing = Math.Min(originalLineSpacing + extraSpacing, maximum);
        return IsFinite(adjustedLineSpacing);
    }

    public static bool TryCalculatePreferredHeightLineSpacing(
        float originalLineSpacing,
        float originalPreferredHeight,
        float currentPreferredHeight,
        int estimatedLineCount,
        out float adjustedLineSpacing)
    {
        adjustedLineSpacing = originalLineSpacing;
        if (!IsFinite(originalLineSpacing) ||
            !IsPositiveFinite(originalPreferredHeight) ||
            !IsPositiveFinite(currentPreferredHeight) ||
            currentPreferredHeight >= originalPreferredHeight ||
            estimatedLineCount < 2)
        {
            return false;
        }

        var extraPerGap = (originalPreferredHeight - currentPreferredHeight) / (estimatedLineCount - 1);
        if (!IsPositiveFinite(extraPerGap))
        {
            return false;
        }

        adjustedLineSpacing = originalLineSpacing + extraPerGap;
        return IsFinite(adjustedLineSpacing);
    }

    public static bool IsLikelyMultiline(string? text, float fontSize, float? preferredHeight, float? renderedHeight)
    {
        if (!string.IsNullOrEmpty(text) && (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0))
        {
            return true;
        }

        if (!IsPositiveFinite(fontSize))
        {
            return false;
        }

        return IsMultilineHeight(fontSize, preferredHeight) || IsMultilineHeight(fontSize, renderedHeight);
    }

    public static int EstimateLineCount(string? text, float fontSize, float? measuredHeight)
    {
        if (!string.IsNullOrEmpty(text))
        {
            var explicitLines = 1;
            foreach (var character in text)
            {
                if (character == '\n')
                {
                    explicitLines++;
                }
            }

            if (explicitLines > 1)
            {
                return explicitLines;
            }
        }

        if (!IsPositiveFinite(fontSize) || !measuredHeight.HasValue || !IsPositiveFinite(measuredHeight.Value))
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Ceiling(measuredHeight.Value / Math.Max(1f, fontSize * 1.1f)));
    }

    public static bool TryCalculateHeightFitFontSize(
        float currentFontSize,
        float originalFontSize,
        float preferredHeight,
        float rectHeight,
        float fitRatio,
        float minimumScale,
        out float adjustedFontSize)
    {
        adjustedFontSize = currentFontSize;
        if (!IsPositiveFinite(currentFontSize) ||
            !IsPositiveFinite(originalFontSize) ||
            !IsPositiveFinite(preferredHeight) ||
            !IsPositiveFinite(rectHeight) ||
            !IsPositiveFinite(fitRatio) ||
            !IsPositiveFinite(minimumScale) ||
            preferredHeight <= rectHeight)
        {
            return false;
        }

        var fitScale = (rectHeight * fitRatio) / preferredHeight;
        if (!IsPositiveFinite(fitScale) || fitScale >= 1f)
        {
            return false;
        }

        var minimumSize = Math.Max(1f, originalFontSize * minimumScale);
        adjustedFontSize = Math.Max(minimumSize, currentFontSize * fitScale);
        return currentFontSize - adjustedFontSize >= MinimumFontSizeDelta;
    }

    private static bool IsMultilineHeight(float fontSize, float? height)
    {
        return height.HasValue &&
            IsPositiveFinite(height.Value) &&
            height.Value > fontSize * MultilineHeightRatio;
    }

    private static bool IsPositiveFinite(float value)
    {
        return value > MinimumLineMetric && IsFinite(value);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
