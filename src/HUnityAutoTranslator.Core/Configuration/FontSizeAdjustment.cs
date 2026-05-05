namespace HUnityAutoTranslator.Core.Configuration;

public static class FontSizeAdjustment
{
    public const double MinimumValue = -99;
    public const double MaximumValue = 300;

    public static bool IsEnabled(FontSizeAdjustmentMode mode, double value)
    {
        return mode != FontSizeAdjustmentMode.Disabled && Math.Abs(value) > 0.001;
    }

    public static double ClampValue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Min(MaximumValue, Math.Max(MinimumValue, value));
    }

    public static float Calculate(float originalSize, FontSizeAdjustmentMode mode, double value)
    {
        if (originalSize <= 0 || !IsEnabled(mode, value))
        {
            return originalSize;
        }

        var adjusted = mode switch
        {
            FontSizeAdjustmentMode.Points => originalSize + value,
            FontSizeAdjustmentMode.Percent => originalSize * (1 + value / 100d),
            _ => originalSize
        };

        return (float)Math.Max(1d, adjusted);
    }
}
