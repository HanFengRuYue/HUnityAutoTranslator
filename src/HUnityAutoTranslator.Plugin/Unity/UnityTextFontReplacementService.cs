using System.Collections;
using System.Runtime.CompilerServices;
using System.Reflection;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Unity;

internal sealed class UnityTextFontReplacementService
{
    private const string PreferredAutomaticFontName = "Noto Sans SC";
    private const string PreferredAutomaticFontFile = @"C:\Windows\Fonts\NotoSansSC-VF.ttf";

    private static readonly string[] CandidateFontNames =
    {
        PreferredAutomaticFontName,
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
        PreferredAutomaticFontFile,
        @"C:\Windows\Fonts\simhei.ttf",
        @"C:\Windows\Fonts\Deng.ttf",
        @"C:\Windows\Fonts\simfang.ttf",
        @"C:\Windows\Fonts\simkai.ttf",
        @"C:\Windows\Fonts\simsunb.ttf",
        @"C:\Windows\Fonts\NotoSerifSC-VF.ttf"
    };

    private static readonly char[] FontProbeCharacters = { '测', '试', '汉', '語' };

    private static readonly Dictionary<string, string[]> VariableFontRegularFaces = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"C:\Windows\Fonts\NotoSansSC-VF.ttf"] = new[] { "Noto Sans SC Regular", "Noto Sans SC" },
        [@"C:\Windows\Fonts\NotoSerifSC-VF.ttf"] = new[] { "Noto Serif SC Regular", "Noto Serif SC" }
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

    private static readonly string[] TmpMaterialReferenceManagerTypeNames =
    {
        "TMPro.MaterialReferenceManager, Unity.TextMeshPro",
        "TMPro.MaterialReferenceManager, Assembly-CSharp",
        "TMPro.MaterialReferenceManager, Unity.TextMeshProModule"
    };

    private readonly ITranslationCache _cache;
    private readonly ManualLogSource _logger;
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly Action<string?, string?> _automaticFontFallbackReporter;
    private readonly Dictionary<string, Font> _unityFonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _tmpFontAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, UnityEngine.Object> _uguiFontTargets = new();
    private readonly Dictionary<int, object?> _uguiOriginalFonts = new();
    private readonly Dictionary<int, object?> _uguiReplacementFonts = new();
    private readonly Dictionary<int, UnityEngine.Object> _tmpFontTargets = new();
    private readonly Dictionary<int, object?> _tmpOriginalFonts = new();
    private readonly Dictionary<int, object?> _tmpReplacementFonts = new();
    private readonly Dictionary<string, string> _failedTmpFontAssetKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ResolvedFont?> _imguiFontResolutions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _imguiRequestedCharacters = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warnedUnityFontFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _warnedTmpCandidateFailureSets = new(StringComparer.OrdinalIgnoreCase);
    private Font? _originalImguiFont;
    private string? _automaticFontFallbackConfigKey;
    private string? _automaticFontFallbackName;
    private string? _automaticFontFallbackFile;
    private bool _capturedImguiFont;
    private bool _runtimeReplacementFontsEnabled = true;
    private bool _warnedNoUnityFont;
    private bool _warnedTmpUnavailable;
    private bool _warnedTmpFallbackListUnavailable;
    private bool _warnedTmpDirectAssignmentFailure;
    private bool _loggedTmpDirectAssignment;
    private bool _warnedTmpInstanceFallbackFailure;
    private bool _loggedTmpInstanceFallback;
    private bool _loggedUguiReplacement;

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
            _logger.LogInfo($"已安装 TMP 后备字体：{resolved.Font.name}。");
        }
    }

    public void ApplyToUgui(UnityEngine.Object component, TranslationCacheKey key, TranslationCacheContext context, string translatedText)
    {
        var config = _configProvider();
        ReportAutomaticFontFallbacks(config);
        if (!config.EnableFontReplacement || !config.ReplaceUguiFonts)
        {
            return;
        }

        if (!_runtimeReplacementFontsEnabled)
        {
            RestoreKnownFont(component, _uguiOriginalFonts);
            return;
        }

        if (OriginalUguiFontCanRenderText(component, translatedText))
        {
            RestoreUgui(component);
            return;
        }

        var samplingPointSize = ResolveComponentFontSamplingPointSize(component, config);
        var resolved = ResolveUnityTextFont(config, key, context, samplingPointSize);
        if (resolved?.Font == null)
        {
            return;
        }

        RememberFontTarget(component, _uguiFontTargets, _uguiOriginalFonts);
        _uguiReplacementFonts[component.GetInstanceID()] = resolved.Font;
        if (SetProperty(component, "font", resolved.Font))
        {
            LogUguiReplacement(resolved);
        }
    }

    public void RestoreUgui(UnityEngine.Object component)
    {
        RestoreKnownFont(component, _uguiOriginalFonts);
        ForgetFontTarget(component, _uguiFontTargets, _uguiOriginalFonts, _uguiReplacementFonts);
    }

    public void ApplyToTmp(UnityEngine.Object component, TranslationCacheKey key, TranslationCacheContext context, string translatedText)
    {
        var config = _configProvider();
        ReportAutomaticFontFallbacks(config);
        if (!config.EnableFontReplacement || !config.ReplaceTmpFonts)
        {
            return;
        }

        if (!_runtimeReplacementFontsEnabled)
        {
            RestoreKnownTmpFont(component);
            return;
        }

        if (OriginalTmpFontCanRenderText(component, translatedText))
        {
            RestoreTmp(component);
            return;
        }

        var samplingPointSize = ResolveComponentFontSamplingPointSize(component, config);
        var fontAsset = ResolveTmpFontAsset(config, key, context, samplingPointSize, out _);
        if (fontAsset == null)
        {
            return;
        }

        PopulateTmpFontAsset(fontAsset, translatedText);
        var componentFallbackInstalled = AddTmpFallbackToComponentFont(component, fontAsset);
        var globalFallbackInstalled = AddTmpFallback(fontAsset);
        if (componentFallbackInstalled)
        {
            LogTmpInstanceFallback(fontAsset);
        }
        else if (!globalFallbackInstalled)
        {
            WarnTmpInstanceFallbackFailed(component, fontAsset);
        }

        if (!componentFallbackInstalled && !globalFallbackInstalled)
        {
            RememberFontTarget(component, _tmpFontTargets, _tmpOriginalFonts);
            _tmpReplacementFonts[component.GetInstanceID()] = fontAsset;
            if (SetTmpFont(component, fontAsset))
            {
                LogTmpDirectAssignment(fontAsset);
            }
            else
            {
                WarnTmpDirectAssignmentFailed(component, fontAsset);
            }
        }
    }

    public void RestoreTmp(UnityEngine.Object component)
    {
        RestoreKnownTmpFont(component);
        ForgetFontTarget(component, _tmpFontTargets, _tmpOriginalFonts, _tmpReplacementFonts);
    }

    public ImguiFontScope? BeginImguiDrawFontScope(
        MethodBase originalMethod,
        object[] args,
        TranslationCacheKey key,
        TranslationCacheContext context,
        string displayedText)
    {
        var config = _configProvider();
        ReportAutomaticFontFallbacks(config);
        if (!config.EnableFontReplacement || !config.ReplaceImguiFonts)
        {
            RestoreImguiFont();
            return null;
        }

        if (!_runtimeReplacementFontsEnabled)
        {
            RestoreImguiFont();
            return null;
        }

        if (!TryGetImguiSkin(out var skin))
        {
            return null;
        }

        CaptureImguiSkinFont(skin);
        var drawStyle = ResolveImguiDrawStyle(originalMethod, args, skin);
        var fontSize = ResolveImguiFontPointSize(drawStyle.Style, skin, config);
        if (ImguiOriginalFontCanRenderText(drawStyle.Style, skin, displayedText, fontSize))
        {
            RestoreImguiFont();
            return null;
        }

        var resolved = ResolveCachedImguiFont(config, key, context, fontSize);
        if (resolved?.Font == null)
        {
            RestoreImguiFont();
            return null;
        }

        TryRequestCharactersOnce(resolved, displayedText, fontSize);
        return drawStyle.HasExplicitStyle && drawStyle.Style != null
            ? ImguiFontScope.ForStyle(drawStyle.Style, resolved.Font)
            : ImguiFontScope.ForSkin(skin, resolved.Font);
    }

    public void RestoreImgui()
    {
        RestoreImguiFont();
    }

    public bool TryResolveImguiFont(
        TranslationCacheKey key,
        TranslationCacheContext context,
        int fontSize,
        string sampleText,
        out Font font)
    {
        font = null!;
        var config = _configProvider();
        ReportAutomaticFontFallbacks(config);
        if (!config.EnableFontReplacement || !config.ReplaceImguiFonts || !_runtimeReplacementFontsEnabled)
        {
            return false;
        }

        var normalizedFontSize = Math.Max(1, fontSize);
        var resolved = ResolveCachedImguiFont(config, key, context, normalizedFontSize);
        if (resolved?.Font == null)
        {
            return false;
        }

        TryRequestCharactersOnce(resolved, sampleText, normalizedFontSize);
        font = resolved.Font;
        return true;
    }

    public int SetReplacementFontsEnabledForRuntime(bool enabled)
    {
        _runtimeReplacementFontsEnabled = enabled;
        return enabled ? ApplyReplacementFontTargets() : RestoreOriginalFontTargets();
    }

    private void RememberFontTarget(
        UnityEngine.Object component,
        Dictionary<int, UnityEngine.Object> targets,
        Dictionary<int, object?> originalFonts)
    {
        var id = component.GetInstanceID();
        targets[id] = component;
        if (!originalFonts.ContainsKey(id))
        {
            originalFonts[id] = GetProperty(component, "font");
        }
    }

    private void RestoreKnownFont(UnityEngine.Object component, Dictionary<int, object?> originalFonts)
    {
        if (originalFonts.TryGetValue(component.GetInstanceID(), out var originalFont))
        {
            SetProperty(component, "font", originalFont);
        }
    }

    private void RestoreKnownTmpFont(UnityEngine.Object component)
    {
        if (_tmpOriginalFonts.TryGetValue(component.GetInstanceID(), out var originalFont))
        {
            SetTmpFont(component, originalFont);
        }
    }

    private static void ForgetFontTarget(
        UnityEngine.Object component,
        Dictionary<int, UnityEngine.Object> targets,
        Dictionary<int, object?> originalFonts,
        Dictionary<int, object?> replacementFonts)
    {
        var id = component.GetInstanceID();
        targets.Remove(id);
        originalFonts.Remove(id);
        replacementFonts.Remove(id);
    }

    private int RestoreOriginalFontTargets()
    {
        return RestoreFontTargets(_uguiFontTargets, _uguiOriginalFonts, _uguiReplacementFonts) +
            RestoreTmpFontTargets(_tmpFontTargets, _tmpOriginalFonts, _tmpReplacementFonts) +
            RestoreImguiFont();
    }

    private int ApplyReplacementFontTargets()
    {
        return ApplyFontTargets(_uguiFontTargets, _uguiOriginalFonts, _uguiReplacementFonts) +
            ApplyTmpFontTargets(_tmpFontTargets, _tmpOriginalFonts, _tmpReplacementFonts) +
            ApplyImguiReplacementFont();
    }

    private static int RestoreFontTargets(
        Dictionary<int, UnityEngine.Object> targets,
        Dictionary<int, object?> originalFonts,
        Dictionary<int, object?> replacementFonts)
    {
        var changed = 0;
        foreach (var item in targets.ToArray())
        {
            if (item.Value == null)
            {
                targets.Remove(item.Key);
                originalFonts.Remove(item.Key);
                replacementFonts.Remove(item.Key);
                continue;
            }

            if (originalFonts.TryGetValue(item.Key, out var originalFont) && SetProperty(item.Value, "font", originalFont))
            {
                changed++;
            }
        }

        return changed;
    }

    private static int RestoreTmpFontTargets(
        Dictionary<int, UnityEngine.Object> targets,
        Dictionary<int, object?> originalFonts,
        Dictionary<int, object?> replacementFonts)
    {
        var changed = 0;
        foreach (var item in targets.ToArray())
        {
            if (item.Value == null)
            {
                targets.Remove(item.Key);
                originalFonts.Remove(item.Key);
                replacementFonts.Remove(item.Key);
                continue;
            }

            if (originalFonts.TryGetValue(item.Key, out var originalFont) && SetTmpFont(item.Value, originalFont))
            {
                changed++;
            }
        }

        return changed;
    }

    private static int ApplyTmpFontTargets(
        Dictionary<int, UnityEngine.Object> targets,
        Dictionary<int, object?> originalFonts,
        Dictionary<int, object?> replacementFonts)
    {
        var changed = 0;
        foreach (var item in targets.ToArray())
        {
            if (item.Value == null)
            {
                targets.Remove(item.Key);
                originalFonts.Remove(item.Key);
                replacementFonts.Remove(item.Key);
                continue;
            }

            if (replacementFonts.TryGetValue(item.Key, out var replacementFont) && SetTmpFont(item.Value, replacementFont))
            {
                changed++;
            }
        }

        return changed;
    }

    private static int ApplyFontTargets(
        Dictionary<int, UnityEngine.Object> targets,
        Dictionary<int, object?> originalFonts,
        Dictionary<int, object?> replacementFonts)
    {
        var changed = 0;
        foreach (var item in targets.ToArray())
        {
            if (item.Value == null)
            {
                targets.Remove(item.Key);
                originalFonts.Remove(item.Key);
                replacementFonts.Remove(item.Key);
                continue;
            }

            if (replacementFonts.TryGetValue(item.Key, out var replacementFont) &&
                SetProperty(item.Value, "font", replacementFont))
            {
                changed++;
            }
        }

        return changed;
    }

    private int RestoreImguiFont()
    {
        if (!_capturedImguiFont || !TryGetImguiSkin(out var skin))
        {
            return 0;
        }

        skin.font = _originalImguiFont;
        return 1;
    }

    private void CaptureImguiSkinFont(GUISkin skin)
    {
        _originalImguiFont = skin.font;
        _capturedImguiFont = true;
    }

    private int ApplyImguiReplacementFont()
    {
        return 0;
    }

    private static bool TryGetImguiSkin(out GUISkin skin)
    {
        skin = null!;
        try
        {
            skin = GUI.skin;
            return skin != null;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static int ResolveImguiFontPointSize(GUISkin skin, RuntimeConfig config)
    {
        var styleSize = Math.Max(
            Math.Max(skin.label?.fontSize ?? 0, skin.button?.fontSize ?? 0),
            Math.Max(skin.toggle?.fontSize ?? 0, skin.textField?.fontSize ?? 0));
        if (styleSize > 0)
        {
            return Math.Max(8, Math.Min(32, styleSize));
        }

        var skinFontSize = skin.font?.fontSize ?? 0;
        if (skinFontSize > 0)
        {
            return Math.Max(8, Math.Min(32, skinFontSize));
        }

        _ = config;
        return 16;
    }

    private static int ResolveImguiFontPointSize(GUIStyle? style, GUISkin skin, RuntimeConfig config)
    {
        if (style?.fontSize > 0)
        {
            return Math.Max(8, Math.Min(32, style.fontSize));
        }

        return ResolveImguiFontPointSize(skin, config);
    }

    private static ImguiDrawStyle ResolveImguiDrawStyle(MethodBase originalMethod, object[] args, GUISkin skin)
    {
        foreach (var argument in args)
        {
            if (argument is GUIStyle explicitStyle)
            {
                return new ImguiDrawStyle(explicitStyle, HasExplicitStyle: true);
            }
        }

        return new ImguiDrawStyle(ResolveImguiSkinStyle(originalMethod, skin), HasExplicitStyle: false);
    }

    private static GUIStyle? ResolveImguiSkinStyle(MethodBase originalMethod, GUISkin skin)
    {
        return originalMethod.Name switch
        {
            "Label" => skin.label,
            "Button" => skin.button,
            "Toggle" => skin.toggle,
            "TextField" => skin.textField,
            _ => null
        };
    }

    private static bool ImguiOriginalFontCanRenderText(GUIStyle? style, GUISkin skin, string displayedText, int fontSize)
    {
        var probeText = BuildFontProbeText(displayedText);
        if (probeText.Length == 0)
        {
            return true;
        }

        var fonts = new[] { style?.font, skin.font }
            .Where(font => font != null)
            .Distinct()
            .ToArray();
        if (fonts.Length == 0)
        {
            return true;
        }

        return fonts.Any(font => UnityFontCanRenderText(font, displayedText, fontSize));
    }

    private void ReportAutomaticFontFallbacks(RuntimeConfig config)
    {
        if (!config.EnableFontReplacement ||
            !config.AutoUseCjkFallbackFonts ||
            !string.IsNullOrWhiteSpace(config.ReplacementFontName) ||
            !string.IsNullOrWhiteSpace(config.ReplacementFontFile))
        {
            _automaticFontFallbackConfigKey = null;
            _automaticFontFallbackName = null;
            _automaticFontFallbackFile = null;
            _automaticFontFallbackReporter(null, null);
            return;
        }

        var cacheKey = $"auto:{config.FontSamplingPointSize}";
        if (!string.Equals(cacheKey, _automaticFontFallbackConfigKey, StringComparison.Ordinal))
        {
            _automaticFontFallbackConfigKey = cacheKey;
            var preferred = ResolvePreferredAutomaticFontPair(config.FontSamplingPointSize);
            _automaticFontFallbackName = preferred?.Name ?? ResolveFirstUsableAutomaticFontName(config.FontSamplingPointSize);
            _automaticFontFallbackFile = preferred?.File ?? ResolveFirstUsableAutomaticFontFile(config.FontSamplingPointSize);
        }

        _automaticFontFallbackReporter(_automaticFontFallbackName, _automaticFontFallbackFile);
    }

    private (string Name, string File)? ResolvePreferredAutomaticFontPair(int size)
    {
        if (!File.Exists(PreferredAutomaticFontFile))
        {
            return null;
        }

        var candidate = FontCandidate.Create("auto-file", PreferredAutomaticFontFile, warnOnUnityFailure: false);
        return candidate != null && ResolveExplicitFont(candidate, size) != null
            ? (PreferredAutomaticFontName, PreferredAutomaticFontFile)
            : null;
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

    private ResolvedFont? ResolveUnityTextFont(
        RuntimeConfig config,
        TranslationCacheKey? key,
        TranslationCacheContext? context,
        int? sizeOverride = null)
    {
        var pointSize = sizeOverride ?? config.FontSamplingPointSize;
        foreach (var candidate in EnumerateUnityFontCandidates(config, key, context))
        {
            var resolved = ResolveExplicitFont(candidate, pointSize);
            if (resolved != null)
            {
                return resolved;
            }
        }

        WarnNoUnityFont();
        return null;
    }

    private ResolvedFont? ResolveCachedImguiFont(
        RuntimeConfig config,
        TranslationCacheKey key,
        TranslationCacheContext context,
        int fontSize)
    {
        var cacheKey = string.Join(
            "\u001f",
            key.SourceText,
            key.TargetLanguage,
            key.PromptPolicyVersion,
            context.SceneName ?? string.Empty,
            context.ComponentType ?? string.Empty,
            config.ReplacementFontFile ?? string.Empty,
            config.ReplacementFontName ?? string.Empty,
            config.AutoUseCjkFallbackFonts.ToString(),
            fontSize.ToString());
        if (_imguiFontResolutions.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var resolved = ResolveUnityTextFont(config, key, context, fontSize);
        _imguiFontResolutions[cacheKey] = resolved;
        return resolved;
    }

    private static void TryRequestCharacters(Font font, string text, int fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            font.RequestCharactersInTexture(text, fontSize);
        }
        catch
        {
        }
    }

    private void TryRequestCharactersOnce(ResolvedFont resolved, string? text, int fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (_imguiRequestedCharacters.Count > 4096)
        {
            _imguiRequestedCharacters.Clear();
        }

        var requestKey = $"{resolved.CacheKey}\u001f{fontSize}\u001f{text}";
        if (_imguiRequestedCharacters.Add(requestKey))
        {
            TryRequestCharacters(resolved.Font, text, fontSize);
        }
    }

    private bool OriginalUguiFontCanRenderText(UnityEngine.Object component, string translatedText)
    {
        if (BuildFontProbeText(translatedText).Length == 0)
        {
            return true;
        }

        var originalFont = _uguiOriginalFonts.TryGetValue(component.GetInstanceID(), out var rememberedFont)
            ? rememberedFont
            : GetProperty(component, "font");
        return originalFont is Font font &&
            UnityFontCanRenderText(font, translatedText, ResolveComponentFontSamplingPointSize(component, _configProvider()));
    }

    private bool OriginalTmpFontCanRenderText(UnityEngine.Object component, string translatedText)
    {
        return TmpFontAssetCanRenderText(ResolveOriginalTmpFontAsset(component), translatedText);
    }

    private object? ResolveOriginalTmpFontAsset(UnityEngine.Object component)
    {
        if (_tmpOriginalFonts.TryGetValue(component.GetInstanceID(), out var rememberedFont))
        {
            return rememberedFont;
        }

        return GetCurrentTmpFontAsset(component);
    }

    private static object? GetCurrentTmpFontAsset(object component)
    {
        return GetProperty(component, "font") ??
            GetProperty(component, "fontAsset") ??
            GetField(component, "m_fontAsset") ??
            GetField(component, "m_currentFontAsset");
    }

    private static int ResolveComponentFontSamplingPointSize(UnityEngine.Object component, RuntimeConfig config)
    {
        var configuredSize = Math.Max(1, config.FontSamplingPointSize);
        var componentSize = ResolveComponentFontSize(component);
        return componentSize > 0
            ? Math.Max(configuredSize, (int)Math.Ceiling(componentSize))
            : configuredSize;
    }

    private static float ResolveComponentFontSize(UnityEngine.Object component)
    {
        var value = GetProperty(component, "fontSize") ?? GetField(component, "m_fontSize");
        return value switch
        {
            int intValue => intValue,
            float floatValue => floatValue,
            double doubleValue => (float)doubleValue,
            _ => 0
        };
    }

    private static bool UnityFontCanRenderText(Font? font, string text, int fontSize)
    {
        var probeText = BuildFontProbeText(text);
        if (probeText.Length == 0)
        {
            return true;
        }

        if (font == null)
        {
            return false;
        }

        TryRequestCharacters(font, probeText, Math.Max(1, fontSize));
        foreach (var character in probeText)
        {
            try
            {
                if (!font.HasCharacter(character))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildFontProbeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var characters = new List<char>();
        foreach (var character in text)
        {
            if (character <= 0x7f ||
                char.IsControl(character) ||
                char.IsWhiteSpace(character) ||
                char.IsSurrogate(character) ||
                characters.Contains(character))
            {
                continue;
            }

            characters.Add(character);
        }

        return characters.Count == 0 ? string.Empty : new string(characters.ToArray());
    }

    private static bool TmpFontAssetCanRenderText(object? fontAsset, string text, bool includeFallbacks = true)
    {
        var probeText = BuildFontProbeText(text);
        if (probeText.Length == 0)
        {
            return true;
        }

        if (fontAsset == null)
        {
            return false;
        }

        foreach (var character in probeText)
        {
            if (!TmpFontAssetCanRenderCharacter(fontAsset, character, includeFallbacks, new HashSet<int>()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TmpFontAssetCanRenderCharacter(
        object fontAsset,
        char character,
        bool includeFallbacks,
        HashSet<int> visited)
    {
        var assetId = fontAsset is UnityEngine.Object unityObject
            ? unityObject.GetInstanceID()
            : RuntimeHelpers.GetHashCode(fontAsset);
        if (!visited.Add(assetId))
        {
            return false;
        }

        if (TryTmpFontAssetHasCharacter(fontAsset, character, includeFallbacks, out var hasCharacter) && hasCharacter)
        {
            return true;
        }

        if (!includeFallbacks)
        {
            return false;
        }

        foreach (var fallback in EnumerateTmpFallbacks(fontAsset).Concat(EnumerateGlobalTmpFallbacks()))
        {
            if (fallback != null && TmpFontAssetCanRenderCharacter(fallback, character, includeFallbacks: true, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryTmpFontAssetHasCharacter(
        object fontAsset,
        char character,
        bool includeFallbacks,
        out bool hasCharacter)
    {
        hasCharacter = false;
        var methods = fontAsset
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "HasCharacter" && method.ReturnType == typeof(bool))
            .OrderByDescending(method => method.GetParameters().Length);
        foreach (var method in methods)
        {
            var arguments = BuildTmpHasCharacterArguments(method, character, includeFallbacks);
            if (arguments == null)
            {
                continue;
            }

            try
            {
                if (method.Invoke(fontAsset, arguments) is bool result)
                {
                    hasCharacter = result;
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private static object?[]? BuildTmpHasCharacterArguments(MethodInfo method, char character, bool includeFallbacks)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
        {
            return null;
        }

        var arguments = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (i == 0)
            {
                if (parameter.ParameterType == typeof(char))
                {
                    arguments[i] = character;
                }
                else if (parameter.ParameterType == typeof(int))
                {
                    arguments[i] = (int)character;
                }
                else if (parameter.ParameterType == typeof(uint))
                {
                    arguments[i] = (uint)character;
                }
                else if (parameter.ParameterType == typeof(string))
                {
                    arguments[i] = character.ToString();
                }
                else
                {
                    return null;
                }

                continue;
            }

            if (parameter.ParameterType == typeof(bool))
            {
                arguments[i] = includeFallbacks;
                continue;
            }

            if (parameter.HasDefaultValue)
            {
                arguments[i] = parameter.DefaultValue;
                continue;
            }

            arguments[i] = parameter.ParameterType.IsValueType
                ? Activator.CreateInstance(parameter.ParameterType)
                : null;
        }

        return arguments;
    }

    private static IEnumerable<object?> EnumerateTmpFallbacks(object fontAsset)
    {
        var fallbackTable =
            GetProperty(fontAsset, "fallbackFontAssetTable") ??
            GetField(fontAsset, "m_FallbackFontAssetTable");
        return EnumerateCollectionItems(fallbackTable);
    }

    private static IEnumerable<object?> EnumerateGlobalTmpFallbacks()
    {
        var settingsType = ResolveType(TmpSettingsTypeNames);
        var fallbackProperty = settingsType?.GetProperty("fallbackFontAssets", BindingFlags.Public | BindingFlags.Static);
        return EnumerateCollectionItems(fallbackProperty?.GetValue(null, null));
    }

    private static IEnumerable<object?> EnumerateCollectionItems(object? collection)
    {
        if (collection is not IEnumerable enumerable)
        {
            yield break;
        }

        foreach (var item in enumerable)
        {
            yield return item;
        }
    }

    private object? ResolveTmpFontAsset(
        RuntimeConfig config,
        TranslationCacheKey? key,
        TranslationCacheContext? context,
        out ResolvedFont? resolvedFont)
    {
        return ResolveTmpFontAsset(config, key, context, config.FontSamplingPointSize, out resolvedFont);
    }

    private object? ResolveTmpFontAsset(
        RuntimeConfig config,
        TranslationCacheKey? key,
        TranslationCacheContext? context,
        int samplingPointSize,
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
        foreach (var candidate in EnumerateTmpFontCandidates(config, key, context))
        {
            var resolved = ResolveExplicitFont(candidate, samplingPointSize);
            if (resolved == null)
            {
                continue;
            }

            attemptedCandidates.Add(resolved.DisplayName);
            var cacheKey = $"{resolved.CacheKey}:tmp:{samplingPointSize}";
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

            var fontAsset = CreateTmpFontAsset(fontAssetType, resolved.Font, samplingPointSize, out var createError);
            if (fontAsset == null)
            {
                lastError = createError ?? "none";
                _failedTmpFontAssetKeys[cacheKey] = lastError;
                continue;
            }

            EnableDynamicAtlas(fontAsset);
            RegisterTmpFontAsset(fontAsset);
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

    private IEnumerable<FontCandidate> EnumerateUnityFontCandidates(
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

    private IEnumerable<FontCandidate> EnumerateTmpFontCandidates(
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
        var cacheKey = $"{candidate.Source}:{candidate.Value}:{candidate.RegularFaceNamesKey}:{size}";
        if (_unityFonts.TryGetValue(cacheKey, out var cached))
        {
            return new ResolvedFont(cacheKey, candidate.DisplayName, cached);
        }

        var font = CreateUnityFont(candidate, size);
        if (font == null)
        {
            if (candidate.WarnOnUnityFailure && _warnedUnityFontFailures.Add(cacheKey))
            {
                _logger.LogWarning($"字体替换已跳过：无法创建字体 {candidate.Value}");
            }

            return null;
        }

        _unityFonts[cacheKey] = font;
        return new ResolvedFont(cacheKey, candidate.DisplayName, font);
    }

    private static Font? CreateUnityFont(FontCandidate candidate, int size)
    {
        foreach (var regularFaceName in candidate.RegularFaceNames)
        {
            var regularFont = CreateUnityFont(regularFaceName, size);
            if (regularFont != null && IsUsableReplacementFont(regularFont, size))
            {
                return regularFont;
            }
        }

        if (candidate.Source == "file" || candidate.Source == "auto-file")
        {
            return CreateUnityFontFromFile(candidate.Value);
        }

        if (candidate.Source == "name" || candidate.Source == "auto-name")
        {
            return CreateUnityFont(candidate.Value, size);
        }

        if (candidate.Source == "row")
        {
            var fontFromFile = CreateUnityFontFromFile(candidate.Value);
            return fontFromFile ?? CreateUnityFont(candidate.Value, size);
        }

        return File.Exists(candidate.Value)
            ? CreateUnityFontFromFile(candidate.Value)
            : CreateUnityFont(candidate.Value, size);
    }

    private static Font? CreateUnityFont(string value, int size)
    {
        if (!IsInstalledOsFontName(value))
        {
            return null;
        }

        try
        {
            return Font.CreateDynamicFontFromOSFont(value, size);
        }
        catch
        {
            return null;
        }
    }

    private static Font? CreateUnityFontFromFile(string value)
    {
        if (!File.Exists(value))
        {
            return null;
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

    private static bool IsInstalledOsFontName(string value)
    {
        try
        {
            return Font.GetOSInstalledFontNames()
                .Any(fontName => string.Equals(fontName, value, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUsableReplacementFont(Font font, int size)
    {
        try
        {
            font.RequestCharactersInTexture(new string(FontProbeCharacters), size);
        }
        catch
        {
        }

        foreach (var character in FontProbeCharacters)
        {
            try
            {
                if (font.HasCharacter(character))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    private static string[] ResolveVariableFontRegularFaceNames(string value)
    {
        return VariableFontRegularFaces.TryGetValue(NormalizeFontPath(value), out var regularFaceNames)
            ? regularFaceNames
            : Array.Empty<string>();
    }

    private static string NormalizeFontPath(string value)
    {
        try
        {
            return Path.GetFullPath(value);
        }
        catch
        {
            return value;
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
            WarnTmpFallbackListUnavailable();
            return false;
        }

        if (fallbacks.Contains(fontAsset))
        {
            return true;
        }

        fallbacks.Add(fontAsset);
        return true;
    }

    private static bool AddTmpFallbackToComponentFont(object component, object fontAsset)
    {
        var currentFontAsset = GetCurrentTmpFontAsset(component);
        if (currentFontAsset == null || ReferenceEquals(currentFontAsset, fontAsset))
        {
            return false;
        }

        return AddTmpFallbackToFontAsset(currentFontAsset, fontAsset);
    }

    private static bool AddTmpFallbackToFontAsset(object targetFontAsset, object fallbackFontAsset)
    {
        var fallbackTable = EnsureTmpFallbackTable(targetFontAsset);
        if (fallbackTable == null)
        {
            return false;
        }

        if (CollectionContains(fallbackTable, fallbackFontAsset))
        {
            return true;
        }

        return CollectionAdd(fallbackTable, fallbackFontAsset);
    }

    private static object? EnsureTmpFallbackTable(object targetFontAsset)
    {
        var fallbackTable =
            GetProperty(targetFontAsset, "fallbackFontAssetTable") ??
            GetField(targetFontAsset, "m_FallbackFontAssetTable");
        if (fallbackTable != null)
        {
            return fallbackTable;
        }

        var createdTable = CreateTmpFallbackTable(targetFontAsset);
        if (createdTable == null)
        {
            return null;
        }

        var propertyChanged = SetProperty(targetFontAsset, "fallbackFontAssetTable", createdTable);
        var fieldChanged = SetField(targetFontAsset, "m_FallbackFontAssetTable", createdTable);
        return propertyChanged || fieldChanged ? createdTable : null;
    }

    private static object? CreateTmpFallbackTable(object targetFontAsset)
    {
        var tableType =
            targetFontAsset.GetType().GetProperty("fallbackFontAssetTable", BindingFlags.Public | BindingFlags.Instance)?.PropertyType ??
            targetFontAsset.GetType().GetField("m_FallbackFontAssetTable", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.FieldType;
        if (tableType != null && !tableType.IsInterface && !tableType.IsAbstract)
        {
            try
            {
                return Activator.CreateInstance(tableType);
            }
            catch
            {
            }
        }

        try
        {
            return Activator.CreateInstance(typeof(List<>).MakeGenericType(targetFontAsset.GetType()));
        }
        catch
        {
            return null;
        }
    }

    private static Type? ResolveType(IEnumerable<string> typeNames)
    {
        var fullNames = new List<string>();
        foreach (var typeName in typeNames)
        {
            var type = Type.GetType(typeName);
            if (type != null)
            {
                return type;
            }

            var commaIndex = typeName.IndexOf(',');
            fullNames.Add(commaIndex >= 0 ? typeName[..commaIndex].Trim() : typeName.Trim());
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var fullName in fullNames)
            {
                var type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
                if (type != null)
                {
                    return type;
                }
            }
        }

        return null;
    }

    private static void EnableDynamicAtlas(object fontAsset)
    {
        SetProperty(fontAsset, "atlasPopulationMode", "Dynamic");
        SetProperty(fontAsset, "isMultiAtlasTexturesEnabled", true);
        InvokeMethodIfAvailable(fontAsset, "ReadFontAssetDefinition");
    }

    private static void RegisterTmpFontAsset(object fontAsset)
    {
        var managerType = ResolveType(TmpMaterialReferenceManagerTypeNames);
        if (managerType == null)
        {
            return;
        }

        var methods = managerType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "AddFontAsset" && method.GetParameters().Length == 1);
        foreach (var method in methods)
        {
            var parameter = method.GetParameters()[0];
            if (!IsCompatibleValue(parameter.ParameterType, fontAsset))
            {
                continue;
            }

            try
            {
                method.Invoke(null, new[] { fontAsset });
                return;
            }
            catch
            {
            }
        }
    }

    private static object? GetProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property != null && property.CanRead)
        {
            try
            {
                return property.GetValue(instance, null);
            }
            catch
            {
            }
        }

        var getter = instance
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == $"get_{propertyName}" && method.GetParameters().Length == 0);
        if (getter == null)
        {
            return null;
        }

        try
        {
            return getter.Invoke(instance, Array.Empty<object?>());
        }
        catch
        {
            return null;
        }
    }

    private static object? GetField(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            return null;
        }

        try
        {
            return field.GetValue(instance);
        }
        catch
        {
            return null;
        }
    }

    private static bool SetProperty(object instance, string propertyName, object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null || !property.CanWrite)
        {
            return InvokeSetter(instance, propertyName, value);
        }

        if (value == null)
        {
            if (property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) == null)
            {
                return false;
            }

            return TrySetPropertyValue(instance, property, null) || InvokeSetter(instance, propertyName, null);
        }

        if (property.PropertyType.IsEnum && value is string enumName)
        {
            var enumValue = Enum.Parse(property.PropertyType, enumName);
            return TrySetPropertyValue(instance, property, enumValue) || InvokeSetter(instance, propertyName, enumValue);
        }

        if (!IsCompatibleValue(property.PropertyType, value))
        {
            return InvokeSetter(instance, propertyName, value);
        }

        return TrySetPropertyValue(instance, property, value) || InvokeSetter(instance, propertyName, value);
    }

    private static bool SetField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            return false;
        }

        if (value == null)
        {
            if (field.FieldType.IsValueType && Nullable.GetUnderlyingType(field.FieldType) == null)
            {
                return false;
            }

            return TrySetFieldValue(instance, field, null);
        }

        if (field.FieldType.IsEnum && value is string enumName)
        {
            return TrySetFieldValue(instance, field, Enum.Parse(field.FieldType, enumName));
        }

        if (!IsCompatibleValue(field.FieldType, value))
        {
            return false;
        }

        return TrySetFieldValue(instance, field, value);
    }

    private static bool SetTmpFont(object component, object? fontAsset)
    {
        if (fontAsset != null)
        {
            PopulateTmpFontAsset(fontAsset, component);
        }

        var changed = SetProperty(component, "font", fontAsset);
        changed |= SetProperty(component, "fontAsset", fontAsset);
        changed |= SetField(component, "m_fontAsset", fontAsset);
        changed |= SetField(component, "m_currentFontAsset", fontAsset);

        if (fontAsset != null)
        {
            var material = GetProperty(fontAsset, "material") ?? GetProperty(fontAsset, "fontMaterial");
            if (material != null)
            {
                SetProperty(component, "fontMaterial", material);
                SetProperty(component, "fontSharedMaterial", material);
                SetProperty(component, "sharedMaterial", material);
                SetProperty(component, "material", material);
                SetField(component, "m_fontMaterial", material);
                SetField(component, "m_sharedMaterial", material);
                SetField(component, "m_currentMaterial", material);
                SetField(component, "m_material", material);
            }
        }

        MarkTmpTextDirty(component);
        return changed;
    }

    private static void MarkTmpTextDirty(object component)
    {
        SetProperty(component, "havePropertiesChanged", true);
        SetField(component, "m_havePropertiesChanged", true);
        SetField(component, "m_hasFontAssetChanged", true);
        InvokeMethodIfAvailable(component, "LoadFontAsset");
        InvokeMethodIfAvailable(component, "SetAllDirty");
        InvokeMethodIfAvailable(component, "SetVerticesDirty");
        InvokeMethodIfAvailable(component, "SetLayoutDirty");
        InvokeMethodIfAvailable(component, "SetMaterialDirty");
        InvokeMethodIfAvailable(component, "ForceMeshUpdate");
    }

    private static void PopulateTmpFontAsset(object fontAsset, object component)
    {
        if (GetProperty(component, "text") is not string text || string.IsNullOrEmpty(text))
        {
            return;
        }

        PopulateTmpFontAsset(fontAsset, text);
    }

    private static void PopulateTmpFontAsset(object fontAsset, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var methods = fontAsset
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "TryAddCharacters");
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 2 ||
                parameters[0].ParameterType != typeof(string) ||
                parameters[1].ParameterType != typeof(bool))
            {
                continue;
            }

            try
            {
                method.Invoke(fontAsset, new object[] { text, true });
                return;
            }
            catch
            {
            }
        }
    }

    private static void InvokeMethodIfAvailable(object instance, string methodName)
    {
        var methods = instance
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(method => method.Name == methodName && !method.ContainsGenericParameters)
            .OrderBy(method => method.GetParameters().Length);

        foreach (var method in methods)
        {
            var arguments = BuildSafeMethodArguments(method);
            if (arguments == null)
            {
                continue;
            }

            try
            {
                method.Invoke(instance, arguments);
                return;
            }
            catch
            {
            }
        }
    }

    private static object?[]? BuildSafeMethodArguments(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameter.HasDefaultValue)
            {
                arguments[i] = parameter.DefaultValue;
                continue;
            }

            if (parameter.ParameterType == typeof(bool))
            {
                arguments[i] = false;
                continue;
            }

            if (parameter.ParameterType.IsValueType)
            {
                arguments[i] = Activator.CreateInstance(parameter.ParameterType);
                continue;
            }

            arguments[i] = null;
        }

        return arguments;
    }

    private static bool CollectionContains(object collection, object item)
    {
        if (collection is IList list)
        {
            try
            {
                return list.Contains(item);
            }
            catch
            {
                return false;
            }
        }

        var containsMethods = collection
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "Contains" && method.GetParameters().Length == 1);
        foreach (var method in containsMethods)
        {
            var parameter = method.GetParameters()[0];
            if (!IsCompatibleValue(parameter.ParameterType, item))
            {
                continue;
            }

            try
            {
                return method.Invoke(collection, new[] { item }) is true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool CollectionAdd(object collection, object item)
    {
        if (collection is IList list)
        {
            try
            {
                list.Add(item);
                return true;
            }
            catch
            {
                return false;
            }
        }

        var addMethods = collection
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "Add" && method.GetParameters().Length == 1);
        foreach (var method in addMethods)
        {
            var parameter = method.GetParameters()[0];
            if (!IsCompatibleValue(parameter.ParameterType, item))
            {
                continue;
            }

            try
            {
                method.Invoke(collection, new[] { item });
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TrySetPropertyValue(object instance, PropertyInfo property, object? value)
    {
        try
        {
            property.SetValue(instance, value, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool InvokeSetter(object instance, string propertyName, object? value)
    {
        var setterName = $"set_{propertyName}";
        var setters = instance
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(method => method.Name == setterName && method.GetParameters().Length == 1);
        foreach (var setter in setters)
        {
            var parameter = setter.GetParameters()[0];
            var argument = value;
            if (argument == null)
            {
                if (parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) == null)
                {
                    continue;
                }
            }
            else if (parameter.ParameterType.IsEnum && argument is string enumName)
            {
                argument = Enum.Parse(parameter.ParameterType, enumName);
            }
            else if (!IsCompatibleValue(parameter.ParameterType, argument))
            {
                continue;
            }

            try
            {
                setter.Invoke(instance, new[] { argument });
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TrySetFieldValue(object instance, FieldInfo field, object? value)
    {
        try
        {
            field.SetValue(instance, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCompatibleValue(Type targetType, object value)
    {
        return targetType.IsInstanceOfType(value) ||
            string.Equals(targetType.FullName, value.GetType().FullName, StringComparison.Ordinal);
    }

    private void WarnNoUnityFont()
    {
        if (_warnedNoUnityFont)
        {
            return;
        }

        _warnedNoUnityFont = true;
        _logger.LogWarning("已开启字体替换，但没有找到可用的中文后备字体。");
    }

    private void WarnTmpUnavailable()
    {
        if (_warnedTmpUnavailable)
        {
            return;
        }

        _warnedTmpUnavailable = true;
        _logger.LogWarning("TMP 字体替换已跳过：当前游戏缺少可用的 TextMeshPro 设置或字体接口。");
    }

    private void WarnTmpFallbackListUnavailable()
    {
        if (_warnedTmpFallbackListUnavailable)
        {
            return;
        }

        _warnedTmpFallbackListUnavailable = true;
        _logger.LogWarning("TMP 全局后备字体未安装：当前游戏未暴露 TMP_Settings.fallbackFontAssets，已改为对捕获到的 TMP 文本直接替换字体。");
    }

    private void WarnTmpDirectAssignmentFailed(UnityEngine.Object component, object fontAsset)
    {
        if (_warnedTmpDirectAssignmentFailure)
        {
            return;
        }

        _warnedTmpDirectAssignmentFailure = true;
        _logger.LogWarning(
            "TMP 组件字体直接替换失败：" +
            $"组件={component.GetType().FullName}，字体资产={fontAsset.GetType().FullName}。");
    }

    private void LogTmpDirectAssignment(object fontAsset)
    {
        if (_loggedTmpDirectAssignment)
        {
            return;
        }

        _loggedTmpDirectAssignment = true;
        _logger.LogInfo($"TMP 组件字体已直接替换：{fontAsset.GetType().FullName}。");
    }

    private void LogUguiReplacement(ResolvedFont resolved)
    {
        if (_loggedUguiReplacement)
        {
            return;
        }

        _loggedUguiReplacement = true;
        _logger.LogInfo($"UGUI 字体已替换：{resolved.DisplayName}。");
    }

    private void WarnTmpInstanceFallbackFailed(UnityEngine.Object component, object fontAsset)
    {
        if (_warnedTmpInstanceFallbackFailure)
        {
            return;
        }

        _warnedTmpInstanceFallbackFailure = true;
        _logger.LogWarning(
            "TMP 组件字体后备表挂载失败：" +
            $"组件={component.GetType().FullName}，字体资产={fontAsset.GetType().FullName}。");
    }

    private void LogTmpInstanceFallback(object fontAsset)
    {
        if (_loggedTmpInstanceFallback)
        {
            return;
        }

        _loggedTmpInstanceFallback = true;
        _logger.LogInfo($"TMP 组件字体后备表已挂载：{fontAsset.GetType().FullName}。");
    }

    private void WarnTmpCandidatesFailed(IReadOnlyList<string> attemptedCandidates, string? lastError)
    {
        var warningKey = string.Join("|", attemptedCandidates) + "|" + (lastError ?? "none");
        if (!_warnedTmpCandidateFailureSets.Add(warningKey))
        {
            return;
        }

        _logger.LogWarning(
            "无法用候选字体创建 TMP 后备字体。" +
            $"已尝试：{string.Join(", ", attemptedCandidates)}。最后错误：{lastError ?? "无"}");
    }

    private readonly struct ImguiDrawStyle
    {
        public ImguiDrawStyle(GUIStyle? style, bool HasExplicitStyle)
        {
            Style = style;
            this.HasExplicitStyle = HasExplicitStyle;
        }

        public GUIStyle? Style { get; }

        public bool HasExplicitStyle { get; }
    }

    public sealed class ImguiFontScope : IDisposable
    {
        private readonly GUIStyle? _style;
        private readonly GUISkin? _skin;
        private readonly Font? _originalFont;
        private bool _disposed;

        private ImguiFontScope(GUIStyle? style, GUISkin? skin, Font replacementFont)
        {
            _style = style;
            _skin = skin;
            if (_style != null)
            {
                _originalFont = _style.font;
                _style.font = replacementFont;
            }
            else if (_skin != null)
            {
                _originalFont = _skin.font;
                _skin.font = replacementFont;
            }
        }

        public static ImguiFontScope? ForStyle(GUIStyle? style, Font replacementFont)
        {
            return style == null ? null : new ImguiFontScope(style, skin: null, replacementFont);
        }

        public static ImguiFontScope ForSkin(GUISkin skin, Font replacementFont)
        {
            return new ImguiFontScope(style: null, skin, replacementFont);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_style != null)
            {
                _style.font = _originalFont;
            }
            else if (_skin != null)
            {
                _skin.font = _originalFont;
            }
        }
    }

    private sealed class FontCandidate
    {
        private FontCandidate(string source, string value, bool warnOnUnityFailure, string[] regularFaceNames)
        {
            var sourceLabel = SourceLabel(source);
            Source = source;
            Value = value;
            WarnOnUnityFailure = warnOnUnityFailure;
            RegularFaceNames = regularFaceNames;
            RegularFaceNamesKey = string.Join("|", regularFaceNames);
            DisplayName = regularFaceNames.Length == 0
                ? $"{sourceLabel}:{value}"
                : $"{sourceLabel}:{value} 常规字重:{regularFaceNames[0]}";
        }

        public string Source { get; }

        public string Value { get; }

        public bool WarnOnUnityFailure { get; }

        public string[] RegularFaceNames { get; }

        public string RegularFaceNamesKey { get; }

        public string DisplayName { get; }

        public static FontCandidate? Create(string source, string? value, bool warnOnUnityFailure)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return new FontCandidate(source, trimmed, warnOnUnityFailure, ResolveVariableFontRegularFaceNames(trimmed));
        }

        private static string SourceLabel(string source)
        {
            return source switch
            {
                "row" => "行内字体",
                "file" => "字体文件",
                "name" => "字体名",
                "auto-name" => "自动字体名",
                "auto-file" => "自动字体",
                _ => source
            };
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
