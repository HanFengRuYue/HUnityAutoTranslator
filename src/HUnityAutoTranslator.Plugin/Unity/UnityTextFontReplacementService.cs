using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Unity;

internal sealed class UnityTextFontReplacementService
{
    private static readonly string[] CandidateFontNames =
    {
        "Noto Sans SC",
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "SimSun",
        "SimHei",
        "DengXian",
        "Arial Unicode MS",
        "Noto Sans CJK SC"
    };

    private static readonly string[] CandidateFontFiles =
    {
        @"C:\Windows\Fonts\NotoSansSC-VF.ttf",
        @"C:\Windows\Fonts\msyh.ttc",
        @"C:\Windows\Fonts\simhei.ttf",
        @"C:\Windows\Fonts\simsun.ttc"
    };

    private static readonly string[] TmpFontAssetTypeNames =
    {
        "TMPro.TMP_FontAsset, Unity.TextMeshPro",
        "TMPro.TMP_FontAsset, Assembly-CSharp",
        "TMPro.TMP_FontAsset, Unity.TextMeshProModule"
    };

    private static readonly string[] TmpSettingsTypeNames =
    {
        "TMPro.TMP_Settings, Unity.TextMeshPro",
        "TMPro.TMP_Settings, Assembly-CSharp",
        "TMPro.TMP_Settings, Unity.TextMeshProModule"
    };

    private readonly ITranslationCache _cache;
    private readonly ManualLogSource _logger;
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly Dictionary<string, Font> _unityFonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _tmpFontAssets = new(StringComparer.OrdinalIgnoreCase);
    private bool _warnedNoUnityFont;
    private bool _warnedTmpUnavailable;

    public UnityTextFontReplacementService(
        ITranslationCache cache,
        ManualLogSource logger,
        Func<RuntimeConfig> configProvider)
    {
        _cache = cache;
        _logger = logger;
        _configProvider = configProvider;
    }

    public void InstallStartupFallbacks()
    {
        var config = _configProvider();
        if (!config.EnableFontReplacement || !config.ReplaceTmpFonts)
        {
            return;
        }

        var resolved = ResolveFont(config, key: null, context: null);
        if (resolved == null)
        {
            return;
        }

        var fontAsset = GetOrCreateTmpFontAsset(resolved, config.FontSamplingPointSize);
        if (fontAsset != null && AddTmpFallback(fontAsset))
        {
            _logger.LogInfo($"TMP fallback font installed: {resolved.Font.name}.");
        }
    }

    public void ApplyToUgui(UnityEngine.Object component, TranslationCacheKey key, TranslationCacheContext context)
    {
        var config = _configProvider();
        if (!config.EnableFontReplacement || !config.ReplaceUguiFonts)
        {
            return;
        }

        var resolved = ResolveFont(config, key, context);
        if (resolved?.Font == null)
        {
            return;
        }

        SetProperty(component, "font", resolved.Font);
    }

    public void ApplyToTmp(UnityEngine.Object component, TranslationCacheKey key, TranslationCacheContext context)
    {
        var config = _configProvider();
        if (!config.EnableFontReplacement || !config.ReplaceTmpFonts)
        {
            return;
        }

        var resolved = ResolveFont(config, key, context);
        if (resolved == null)
        {
            return;
        }

        var fontAsset = GetOrCreateTmpFontAsset(resolved, config.FontSamplingPointSize);
        if (fontAsset == null)
        {
            return;
        }

        SetProperty(component, "font", fontAsset);
        AddTmpFallback(fontAsset);
    }

    public void ApplyToImgui(TranslationCacheKey key, TranslationCacheContext context)
    {
        var config = _configProvider();
        if (!config.EnableFontReplacement || !config.ReplaceImguiFonts)
        {
            return;
        }

        var resolved = ResolveFont(config, key, context);
        if (resolved?.Font == null || GUI.skin == null)
        {
            return;
        }

        GUI.skin.font = resolved.Font;
    }

    private ResolvedFont? ResolveFont(RuntimeConfig config, TranslationCacheKey? key, TranslationCacheContext? context)
    {
        if (key != null && context != null && _cache.TryGetReplacementFont(key, context, out var overrideFont))
        {
            return ResolveExplicitFont("row", overrideFont, config.FontSamplingPointSize);
        }

        if (!string.IsNullOrWhiteSpace(config.ReplacementFontFile))
        {
            return ResolveExplicitFont("file", config.ReplacementFontFile, config.FontSamplingPointSize);
        }

        if (!string.IsNullOrWhiteSpace(config.ReplacementFontName))
        {
            return ResolveExplicitFont("name", config.ReplacementFontName, config.FontSamplingPointSize);
        }

        if (!config.AutoUseCjkFallbackFonts)
        {
            return null;
        }

        foreach (var fontName in CandidateFontNames)
        {
            var resolved = ResolveExplicitFont("auto-name", fontName, config.FontSamplingPointSize, warnOnFailure: false);
            if (resolved != null)
            {
                return resolved;
            }
        }

        foreach (var fontFile in CandidateFontFiles)
        {
            if (!File.Exists(fontFile))
            {
                continue;
            }

            var resolved = ResolveExplicitFont("auto-file", fontFile, config.FontSamplingPointSize, warnOnFailure: false);
            if (resolved != null)
            {
                return resolved;
            }
        }

        WarnNoUnityFont();
        return null;
    }

    private ResolvedFont? ResolveExplicitFont(string source, string value, int size, bool warnOnFailure = true)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        var cacheKey = $"{source}:{normalized}:{size}";
        if (_unityFonts.TryGetValue(cacheKey, out var cached))
        {
            return new ResolvedFont(cacheKey, cached);
        }

        var font = CreateUnityFont(normalized, size);
        if (font == null)
        {
            if (warnOnFailure)
            {
                _logger.LogWarning($"Font replacement skipped because the font could not be created: {normalized}");
            }

            return null;
        }

        _unityFonts[cacheKey] = font;
        return new ResolvedFont(cacheKey, font);
    }

    private static Font? CreateUnityFont(string value, int size)
    {
        if (File.Exists(value))
        {
            try
            {
                return new Font(value);
            }
            catch
            {
            }
        }

        try
        {
            var dynamicFont = Font.CreateDynamicFontFromOSFont(value, size);
            if (dynamicFont != null)
            {
                return dynamicFont;
            }
        }
        catch
        {
        }

        try
        {
            return new Font(value);
        }
        catch
        {
            return null;
        }
    }

    private object? GetOrCreateTmpFontAsset(ResolvedFont resolved, int samplingPointSize)
    {
        var cacheKey = $"{resolved.CacheKey}:tmp:{samplingPointSize}";
        if (_tmpFontAssets.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var fontAssetType = ResolveType(TmpFontAssetTypeNames);
        if (fontAssetType == null)
        {
            WarnTmpUnavailable();
            return null;
        }

        var fontAsset = CreateTmpFontAsset(fontAssetType, resolved.Font, samplingPointSize, out var lastError);
        if (fontAsset == null)
        {
            _logger.LogWarning($"TMP fallback font asset could not be created from {resolved.Font.name}. Last error: {lastError ?? "none"}");
            return null;
        }

        EnableDynamicAtlas(fontAsset);
        _tmpFontAssets[cacheKey] = fontAsset;
        return fontAsset;
    }

    private static object? CreateTmpFontAsset(Type fontAssetType, Font osFont, int samplingPointSize, out string? lastError)
    {
        lastError = null;
        var methods = fontAssetType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "CreateFontAsset")
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length > 0 && parameters[0].ParameterType == typeof(Font);
            })
            .OrderByDescending(method => method.GetParameters().Length);

        foreach (var method in methods)
        {
            try
            {
                return method.Invoke(null, BuildCreateFontAssetArguments(method, osFont, samplingPointSize));
            }
            catch (Exception ex)
            {
                lastError = ex.InnerException?.Message ?? ex.Message;
            }
        }

        return null;
    }

    private static object?[] BuildCreateFontAssetArguments(MethodInfo method, Font osFont, int samplingPointSize)
    {
        var parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (i == 0)
            {
                arguments[i] = osFont;
                continue;
            }

            if (parameter.HasDefaultValue)
            {
                arguments[i] = parameter.DefaultValue;
                continue;
            }

            arguments[i] = CreateFallbackArgument(parameter, samplingPointSize);
        }

        return arguments;
    }

    private static object? CreateFallbackArgument(ParameterInfo parameter, int samplingPointSize)
    {
        if (parameter.ParameterType == typeof(int))
        {
            return parameter.Name switch
            {
                "samplingPointSize" => samplingPointSize,
                "atlasPadding" => 9,
                "atlasWidth" => 4096,
                "atlasHeight" => 4096,
                _ => 0
            };
        }

        if (parameter.ParameterType == typeof(bool))
        {
            return true;
        }

        if (parameter.ParameterType.IsEnum)
        {
            var preferred = parameter.ParameterType.Name.Contains("AtlasPopulationMode", StringComparison.Ordinal)
                ? "Dynamic"
                : "SDFAA";
            return Enum.Parse(parameter.ParameterType, preferred);
        }

        return parameter.ParameterType.IsValueType
            ? Activator.CreateInstance(parameter.ParameterType)
            : null;
    }

    private bool AddTmpFallback(object fontAsset)
    {
        var settingsType = ResolveType(TmpSettingsTypeNames);
        var fallbackProperty = settingsType?.GetProperty("fallbackFontAssets", BindingFlags.Public | BindingFlags.Static);
        if (fallbackProperty?.GetValue(null, null) is not IList fallbacks)
        {
            WarnTmpUnavailable();
            return false;
        }

        if (fallbacks.Contains(fontAsset))
        {
            return false;
        }

        fallbacks.Add(fontAsset);
        return true;
    }

    private static Type? ResolveType(IEnumerable<string> typeNames)
    {
        foreach (var typeName in typeNames)
        {
            var type = Type.GetType(typeName);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private static void EnableDynamicAtlas(object fontAsset)
    {
        SetProperty(fontAsset, "atlasPopulationMode", "Dynamic");
        SetProperty(fontAsset, "isMultiAtlasTexturesEnabled", true);
    }

    private static void SetProperty(object instance, string propertyName, object value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null || !property.CanWrite)
        {
            return;
        }

        if (property.PropertyType.IsEnum && value is string enumName)
        {
            property.SetValue(instance, Enum.Parse(property.PropertyType, enumName), null);
            return;
        }

        if (!property.PropertyType.IsInstanceOfType(value))
        {
            return;
        }

        property.SetValue(instance, value, null);
    }

    private void WarnNoUnityFont()
    {
        if (_warnedNoUnityFont)
        {
            return;
        }

        _warnedNoUnityFont = true;
        _logger.LogWarning("Font replacement is enabled, but no usable CJK fallback font was found.");
    }

    private void WarnTmpUnavailable()
    {
        if (_warnedTmpUnavailable)
        {
            return;
        }

        _warnedTmpUnavailable = true;
        _logger.LogWarning("TMP font replacement skipped because TextMeshPro settings or font asset APIs were not available.");
    }

    private sealed class ResolvedFont
    {
        public ResolvedFont(string cacheKey, Font font)
        {
            CacheKey = cacheKey;
            Font = font;
        }

        public string CacheKey { get; }

        public Font Font { get; }
    }
}
