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
    private readonly Action<string?, string?> _automaticFontFallbackReporter;
    private readonly Dictionary<string, Font> _unityFonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _tmpFontAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _failedTmpFontAssetKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _warnedUnityFontFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _warnedTmpCandidateFailureSets = new(StringComparer.OrdinalIgnoreCase);
    private bool _warnedNoUnityFont;
    private bool _warnedTmpUnavailable;

    public UnityTextFontReplacementService(
        ITranslationCache cache,
        ManualLogSource logger,
        Func<RuntimeConfig> configProvider,
        Action<string?, string?> automaticFontFallbackReporter)
    {
        _cache = cache;
        _logger = logger;
        _configProvider = configProvider;
        _automaticFontFallbackReporter = automaticFontFallbackReporter;
    }

    public void InstallStartupFallbacks()
    {
        var config = _configProvider();
        ReportAutomaticFontFallbacks(config);
        if (!config.EnableFontReplacement || !config.ReplaceTmpFonts)
        {
            return;
        }

        var fontAsset = ResolveTmpFontAsset(config, key: null, context: null, out var resolved);
        if (fontAsset != null && resolved != null && AddTmpFallback(fontAsset))
        {
            _logger.LogInfo($"TMP fallback font installed: {resolved.Font.name}.");
        }
    }

    public void ApplyToUgui(UnityEngine.Object component, TranslationCacheKey key, TranslationCacheContext context)
    {
        var config = _configProvider();
        ReportAutomaticFontFallbacks(config);
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
        ReportAutomaticFontFallbacks(config);
        if (!config.EnableFontReplacement || !config.ReplaceTmpFonts)
        {
            return;
        }

        var resolved = ResolveFont(config, key, context);
        if (resolved == null)
        {
            return;
        }

        var fontAsset = ResolveTmpFontAsset(config, key, context, out _);
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
        ReportAutomaticFontFallbacks(config);
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

    private void ReportAutomaticFontFallbacks(RuntimeConfig config)
    {
        if (!config.EnableFontReplacement ||
            !config.AutoUseCjkFallbackFonts ||
            !string.IsNullOrWhiteSpace(config.ReplacementFontName) ||
            !string.IsNullOrWhiteSpace(config.ReplacementFontFile))
        {
            _automaticFontFallbackReporter(null, null);
            return;
        }

        _automaticFontFallbackReporter(
            ResolveFirstUsableAutomaticFontName(config.FontSamplingPointSize),
            ResolveFirstUsableAutomaticFontFile(config.FontSamplingPointSize));
    }

    private string? ResolveFirstUsableAutomaticFontName(int size)
    {
        foreach (var fontName in CandidateFontNames)
        {
            var candidate = FontCandidate.Create("auto-name", fontName, warnOnUnityFailure: false);
            if (candidate != null && ResolveExplicitFont(candidate, size) != null)
            {
                return fontName;
            }
        }

        return null;
    }

    private string? ResolveFirstUsableAutomaticFontFile(int size)
    {
        foreach (var fontFile in CandidateFontFiles)
        {
            if (!File.Exists(fontFile))
            {
                continue;
            }

            var candidate = FontCandidate.Create("auto-file", fontFile, warnOnUnityFailure: false);
            if (candidate != null && ResolveExplicitFont(candidate, size) != null)
            {
                return fontFile;
            }
        }

        return null;
    }

    private ResolvedFont? ResolveFont(RuntimeConfig config, TranslationCacheKey? key, TranslationCacheContext? context)
    {
        foreach (var candidate in EnumerateFontCandidates(config, key, context))
        {
            var resolved = ResolveExplicitFont(candidate, config.FontSamplingPointSize);
            if (resolved != null)
            {
                return resolved;
            }
        }

        WarnNoUnityFont();
        return null;
    }

    private object? ResolveTmpFontAsset(
        RuntimeConfig config,
        TranslationCacheKey? key,
        TranslationCacheContext? context,
        out ResolvedFont? resolvedFont)
    {
        resolvedFont = null;
        var fontAssetType = ResolveType(TmpFontAssetTypeNames);
        if (fontAssetType == null)
        {
            WarnTmpUnavailable();
            return null;
        }

        var attemptedCandidates = new List<string>();
        string? lastError = null;
        foreach (var candidate in EnumerateFontCandidates(config, key, context))
        {
            var resolved = ResolveExplicitFont(candidate, config.FontSamplingPointSize);
            if (resolved == null)
            {
                continue;
            }

            attemptedCandidates.Add(resolved.DisplayName);
            var cacheKey = $"{resolved.CacheKey}:tmp:{config.FontSamplingPointSize}";
            if (_tmpFontAssets.TryGetValue(cacheKey, out var cached))
            {
                resolvedFont = resolved;
                return cached;
            }

            if (_failedTmpFontAssetKeys.TryGetValue(cacheKey, out var cachedError))
            {
                lastError = cachedError;
                continue;
            }

            var fontAsset = CreateTmpFontAsset(fontAssetType, resolved.Font, config.FontSamplingPointSize, out var createError);
            if (fontAsset == null)
            {
                lastError = createError ?? "none";
                _failedTmpFontAssetKeys[cacheKey] = lastError;
                continue;
            }

            EnableDynamicAtlas(fontAsset);
            _tmpFontAssets[cacheKey] = fontAsset;
            resolvedFont = resolved;
            return fontAsset;
        }

        if (attemptedCandidates.Count == 0)
        {
            WarnNoUnityFont();
        }
        else
        {
            WarnTmpCandidatesFailed(attemptedCandidates, lastError);
        }

        return null;
    }

    private IEnumerable<FontCandidate> EnumerateFontCandidates(
        RuntimeConfig config,
        TranslationCacheKey? key,
        TranslationCacheContext? context)
    {
        if (key != null && context != null && _cache.TryGetReplacementFont(key, context, out var overrideFont))
        {
            var candidate = FontCandidate.Create("row", overrideFont, warnOnUnityFailure: true);
            if (candidate != null)
            {
                yield return candidate;
            }
        }

        var fontFileCandidate = FontCandidate.Create("file", config.ReplacementFontFile, warnOnUnityFailure: true);
        if (fontFileCandidate != null)
        {
            yield return fontFileCandidate;
        }

        var fontNameCandidate = FontCandidate.Create("name", config.ReplacementFontName, warnOnUnityFailure: true);
        if (fontNameCandidate != null)
        {
            yield return fontNameCandidate;
        }

        if (!config.AutoUseCjkFallbackFonts)
        {
            yield break;
        }

        foreach (var fontName in CandidateFontNames)
        {
            var candidate = FontCandidate.Create("auto-name", fontName, warnOnUnityFailure: false);
            if (candidate != null)
            {
                yield return candidate;
            }
        }

        foreach (var fontFile in CandidateFontFiles)
        {
            if (!File.Exists(fontFile))
            {
                continue;
            }

            var candidate = FontCandidate.Create("auto-file", fontFile, warnOnUnityFailure: false);
            if (candidate != null)
            {
                yield return candidate;
            }
        }
    }

    private ResolvedFont? ResolveExplicitFont(FontCandidate candidate, int size)
    {
        var cacheKey = $"{candidate.Source}:{candidate.Value}:{size}";
        if (_unityFonts.TryGetValue(cacheKey, out var cached))
        {
            return new ResolvedFont(cacheKey, candidate.DisplayName, cached);
        }

        var font = CreateUnityFont(candidate.Value, size);
        if (font == null)
        {
            if (candidate.WarnOnUnityFailure && _warnedUnityFontFailures.Add(cacheKey))
            {
                _logger.LogWarning($"Font replacement skipped because the font could not be created: {candidate.Value}");
            }

            return null;
        }

        _unityFonts[cacheKey] = font;
        return new ResolvedFont(cacheKey, candidate.DisplayName, font);
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

    private static object? CreateTmpFontAsset(Type fontAssetType, Font osFont, int samplingPointSize, out string? lastError)
    {
        lastError = null;
        var methods = fontAssetType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "CreateFontAsset")
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length > 0 &&
                    (parameters[0].ParameterType == typeof(Font) ||
                     string.Equals(parameters[0].ParameterType.FullName, typeof(Font).FullName, StringComparison.Ordinal));
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

    private void WarnTmpCandidatesFailed(IReadOnlyList<string> attemptedCandidates, string? lastError)
    {
        var warningKey = string.Join("|", attemptedCandidates) + "|" + (lastError ?? "none");
        if (!_warnedTmpCandidateFailureSets.Add(warningKey))
        {
            return;
        }

        _logger.LogWarning(
            "TMP fallback font asset could not be created from any candidate. " +
            $"Tried: {string.Join(", ", attemptedCandidates)}. Last error: {lastError ?? "none"}");
    }

    private sealed class FontCandidate
    {
        private FontCandidate(string source, string value, bool warnOnUnityFailure)
        {
            Source = source;
            Value = value;
            WarnOnUnityFailure = warnOnUnityFailure;
            DisplayName = $"{source}:{value}";
        }

        public string Source { get; }

        public string Value { get; }

        public bool WarnOnUnityFailure { get; }

        public string DisplayName { get; }

        public static FontCandidate? Create(string source, string? value, bool warnOnUnityFailure)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return new FontCandidate(source, value.Trim(), warnOnUnityFailure);
        }
    }

    private sealed class ResolvedFont
    {
        public ResolvedFont(string cacheKey, string displayName, Font font)
        {
            CacheKey = cacheKey;
            DisplayName = displayName;
            Font = font;
        }

        public string CacheKey { get; }

        public string DisplayName { get; }

        public Font Font { get; }
    }
}
