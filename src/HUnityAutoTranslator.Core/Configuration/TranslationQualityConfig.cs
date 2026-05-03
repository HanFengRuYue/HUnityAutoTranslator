namespace HUnityAutoTranslator.Core.Configuration;

public sealed record TranslationQualityConfig(
    bool Enabled,
    string Mode,
    bool AllowAlreadyTargetLanguageSource,
    bool EnableRepair,
    int MaxRetryCount,
    bool RejectGeneratedOuterSymbols,
    bool RejectUntranslatedLatinUiText,
    bool RejectShortSettingValue,
    bool RejectLiteralStateTranslation,
    bool RejectSameParentOptionCollision,
    int ShortSettingValueMinSourceLength,
    int ShortSettingValueMaxTranslationTextElements)
{
    public static TranslationQualityConfig Default() => Balanced();

    public static TranslationQualityConfig Balanced() => new(
        Enabled: true,
        Mode: "balanced",
        AllowAlreadyTargetLanguageSource: true,
        EnableRepair: true,
        MaxRetryCount: 3,
        RejectGeneratedOuterSymbols: true,
        RejectUntranslatedLatinUiText: true,
        RejectShortSettingValue: true,
        RejectLiteralStateTranslation: true,
        RejectSameParentOptionCollision: true,
        ShortSettingValueMinSourceLength: 4,
        ShortSettingValueMaxTranslationTextElements: 1);

    public static TranslationQualityConfig Relaxed() => Balanced() with
    {
        Mode = "relaxed",
        MaxRetryCount = 1,
        RejectShortSettingValue = false,
        RejectLiteralStateTranslation = false,
        RejectSameParentOptionCollision = false
    };

    public static TranslationQualityConfig Strict() => Balanced() with
    {
        Mode = "strict",
        MaxRetryCount = 5,
        ShortSettingValueMinSourceLength = 3
    };

    public TranslationQualityConfig Normalize()
    {
        var mode = NormalizeMode(Mode);
        var preset = mode switch
        {
            "balanced" => Balanced(),
            "relaxed" => Relaxed(),
            "strict" => Strict(),
            _ => this with { Mode = "custom" }
        };

        if (mode != "custom")
        {
            return preset with { Enabled = Enabled };
        }

        return preset with
        {
            Mode = "custom",
            MaxRetryCount = Clamp(MaxRetryCount, 0, 10),
            ShortSettingValueMinSourceLength = Clamp(ShortSettingValueMinSourceLength, 1, 32),
            ShortSettingValueMaxTranslationTextElements = Clamp(ShortSettingValueMaxTranslationTextElements, 1, 8)
        };
    }

    private static string NormalizeMode(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "balanced" or "relaxed" or "strict" or "custom"
            ? normalized
            : "balanced";
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}
