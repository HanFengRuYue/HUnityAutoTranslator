namespace HUnityAutoTranslator.Core.Configuration;

public sealed record TextureImageProviderProfileDefinition(
    string Id,
    string Name,
    bool Enabled,
    int Priority,
    string BaseUrl,
    string EditEndpoint,
    string VisionEndpoint,
    string ImageModel,
    string VisionModel,
    string Quality,
    int TimeoutSeconds,
    int MaxConcurrentRequests,
    bool EnableVisionConfirmation,
    string? ApiKey)
{
    public static TextureImageProviderProfileDefinition CreateDefault(string? name, int priority)
    {
        var defaults = TextureImageTranslationConfig.Default();
        return new TextureImageProviderProfileDefinition(
            CreateId(),
            string.IsNullOrWhiteSpace(name) ? "贴图图片服务" : name.Trim(),
            Enabled: true,
            Priority: Math.Max(0, priority),
            defaults.BaseUrl,
            defaults.EditEndpoint,
            defaults.VisionEndpoint,
            defaults.ImageModel,
            defaults.VisionModel,
            defaults.Quality,
            defaults.TimeoutSeconds,
            defaults.MaxConcurrentRequests,
            defaults.EnableVisionConfirmation,
            ApiKey: null);
    }

    public TextureImageProviderProfileDefinition Normalize()
    {
        var defaults = TextureImageTranslationConfig.Default();
        return this with
        {
            Id = NormalizeId(Id),
            Name = string.IsNullOrWhiteSpace(Name) ? "贴图图片服务" : Name.Trim(),
            Priority = Math.Max(0, Priority),
            BaseUrl = NormalizeUrl(BaseUrl, defaults.BaseUrl),
            EditEndpoint = NormalizeEndpoint(EditEndpoint, defaults.EditEndpoint),
            VisionEndpoint = NormalizeEndpoint(VisionEndpoint, defaults.VisionEndpoint),
            ImageModel = SelectText(ImageModel, defaults.ImageModel),
            VisionModel = SelectText(VisionModel, defaults.VisionModel),
            Quality = SelectKnown(Quality, defaults.Quality, "low", "medium", "high", "auto"),
            TimeoutSeconds = Clamp(TimeoutSeconds, 30, 300),
            MaxConcurrentRequests = Clamp(MaxConcurrentRequests, 1, 4),
            ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim()
        };
    }

    public TextureImageTranslationConfig ToConfig()
    {
        var normalized = Normalize();
        return new TextureImageTranslationConfig(
            normalized.Enabled,
            normalized.BaseUrl,
            normalized.EditEndpoint,
            normalized.VisionEndpoint,
            normalized.ImageModel,
            normalized.VisionModel,
            normalized.Quality,
            normalized.TimeoutSeconds,
            normalized.MaxConcurrentRequests,
            normalized.EnableVisionConfirmation);
    }

    public static TextureImageProviderProfileDefinition FromLegacy(
        TextureImageTranslationConfig config,
        string? apiKey,
        int priority)
    {
        return new TextureImageProviderProfileDefinition(
            CreateId(),
            "迁移的贴图图片服务",
            config.Enabled,
            priority,
            config.BaseUrl,
            config.EditEndpoint,
            config.VisionEndpoint,
            config.ImageModel,
            config.VisionModel,
            config.Quality,
            config.TimeoutSeconds,
            config.MaxConcurrentRequests,
            config.EnableVisionConfirmation,
            string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim()).Normalize();
    }

    public static string CreateId()
    {
        return Guid.NewGuid().ToString("N");
    }

    public static string NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return CreateId();
        }

        var trimmed = id.Trim();
        return trimmed.All(character =>
            (character >= 'a' && character <= 'z') ||
            (character >= 'A' && character <= 'Z') ||
            (character >= '0' && character <= '9') ||
            character == '_' ||
            character == '-')
            ? trimmed
            : CreateId();
    }

    private static string NormalizeUrl(string? value, string fallback)
    {
        var text = SelectText(value, fallback).TrimEnd(new[] { '/' });
        return text.Length == 0 ? fallback : text;
    }

    private static string NormalizeEndpoint(string? value, string fallback)
    {
        var text = SelectText(value, fallback);
        return text.StartsWith("/", StringComparison.Ordinal) ? text : "/" + text;
    }

    private static string SelectText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string SelectKnown(string? value, string fallback, params string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        return allowed.FirstOrDefault(item => string.Equals(item, trimmed, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }
}
