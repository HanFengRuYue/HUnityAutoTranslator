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
    private const int MaxTmpMaterialDiagnostics = 32;
    private const int MaxTmpSubTextMaterialDiagnostics = 8;
    private const float TmpOutlineConstraintThreshold = 0.3f;
    private const float TmpFaceDilateConstraintThreshold = 0.2f;
    private const float TmpNameHintOutlineConstraintThreshold = 0.05f;
    private const float TmpConstrainedWeightNormal = 0f;
    private const float TmpConstrainedWeightBold = 0f;
    private const float TmpConstrainedFaceDilate = 0.04f;
    private const float TmpConstrainedOutlineWidth = 0.015f;
    private const float TmpConstrainedOutlineSoftness = 0f;

    private static readonly string[] CandidateFontNames =
    {
        PreferredAutomaticFontName,
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "DengXian",
        "DengXian Light",
        "SimSun",
        "SimHei",
        "Arial Unicode MS",
        "Noto Sans CJK SC"
    };

    private static readonly string[] CandidateFontFiles =
    {
        PreferredAutomaticFontFile,
        @"C:\Windows\Fonts\Deng.ttf",
        @"C:\Windows\Fonts\Dengl.ttf",
        @"C:\Windows\Fonts\simfang.ttf",
        @"C:\Windows\Fonts\simkai.ttf",
        @"C:\Windows\Fonts\simsunb.ttf",
        @"C:\Windows\Fonts\NotoSerifSC-VF.ttf",
        @"C:\Windows\Fonts\simhei.ttf"
    };

    private static readonly string[] StandardTmpAutomaticFontFiles =
    {
        PreferredAutomaticFontFile,
        @"C:\Windows\Fonts\simhei.ttf",
        @"C:\Windows\Fonts\Deng.ttf",
        @"C:\Windows\Fonts\Dengl.ttf",
        @"C:\Windows\Fonts\simfang.ttf",
        @"C:\Windows\Fonts\simkai.ttf",
        @"C:\Windows\Fonts\simsunb.ttf",
        @"C:\Windows\Fonts\NotoSerifSC-VF.ttf"
    };

    private static readonly string[] OutlineConstrainedTmpAutomaticFontFiles =
    {
        PreferredAutomaticFontFile,
        @"C:\Windows\Fonts\simhei.ttf",
        @"C:\Windows\Fonts\Dengl.ttf",
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

    private static readonly string[] TmpMaterialManagerTypeNames =
    {
        "TMPro.TMP_MaterialManager, Unity.TextMeshPro",
        "TMPro.TMP_MaterialManager, Assembly-CSharp",
        "TMPro.TMP_MaterialManager, Unity.TextMeshProModule"
    };

    private enum TmpFallbackStyleProfile
    {
        PreserveSource,
        ConstrainOutline
    }

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
    private readonly HashSet<string> _loggedAutomaticTmpFontFallbacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loggedTmpMaterialDiagnostics = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loggedTmpSubTextMaterialDiagnostics = new(StringComparer.Ordinal);
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
    private bool _warnedTmpMatchMaterialPresetUnavailable;
    private bool _loggedTmpDirectAssignment;
    private bool _loggedTmpMatchMaterialPreset;
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
        if (fontAsset != null && resolved != null)
        {
            TryEnableTmpMatchMaterialPreset();
            if (AddTmpFallback(fontAsset))
            {
                _logger.LogInfo($"已安装 TMP 后备字体：{resolved.Font.name}。");
            }
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

        var hasComponentFontOverride = HasComponentFontOverride(key, context);
        if (!hasComponentFontOverride && OriginalUguiFontCanRenderText(component, translatedText))
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

        var hasComponentFontOverride = HasComponentFontOverride(key, context);
        if (!hasComponentFontOverride && OriginalTmpFontCanRenderText(component, translatedText))
        {
            RestoreTmp(component);
            return;
        }

        var samplingPointSize = ResolveComponentFontSamplingPointSize(component, config);
        var componentMaterial = GetTmpComponentMaterial(component);
        var styleProfile = ResolveTmpFallbackStyleProfile(componentMaterial, ResolvedFont.AutomaticProbe);
        var fontAsset = ResolveTmpFontAsset(config, key, context, samplingPointSize, styleProfile, out var resolved);
        if (fontAsset == null)
        {
            return;
        }

        TryEnableTmpMatchMaterialPreset();
        if (resolved?.Source == "row")
        {
            RememberFontTarget(component, _tmpFontTargets, _tmpOriginalFonts);
            _tmpReplacementFonts[component.GetInstanceID()] = fontAsset;
            if (SetTmpFont(component, fontAsset, populateCharacters: true, resolved))
            {
                LogTmpDirectAssignment(fontAsset);
                return;
            }

            WarnTmpDirectAssignmentFailed(component, fontAsset);
        }

        PopulateTmpFontAsset(fontAsset, translatedText);
        PrepareTmpFallbackMaterial(component, fontAsset, resolved);
        LogTmpMaterialDiagnosticsIfNeeded(config, component, context, translatedText, fontAsset, resolved);
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

        if (componentFallbackInstalled || globalFallbackInstalled)
        {
            MarkTmpTextDirty(component);
            LogTmpSubTextDiagnosticsIfNeeded(config, component, context, translatedText);
        }

        if (!componentFallbackInstalled && !globalFallbackInstalled)
        {
            RememberFontTarget(component, _tmpFontTargets, _tmpOriginalFonts);
            _tmpReplacementFonts[component.GetInstanceID()] = fontAsset;
            if (SetTmpFont(component, fontAsset, populateCharacters: true, resolved))
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
            SetTmpFont(component, originalFont, populateCharacters: false);
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

            if (originalFonts.TryGetValue(item.Key, out var originalFont) &&
                SetTmpFont(item.Value, originalFont, populateCharacters: false))
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

            if (replacementFonts.TryGetValue(item.Key, out var replacementFont) &&
                SetTmpFont(item.Value, replacementFont, populateCharacters: true))
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
            ClearAutomaticFontFallbackReport();
            return;
        }

        var cacheKey = $"auto:{config.FontSamplingPointSize}";
        if (!string.Equals(cacheKey, _automaticFontFallbackConfigKey, StringComparison.Ordinal))
        {
            _automaticFontFallbackConfigKey = cacheKey;
            _automaticFontFallbackName = null;
            _automaticFontFallbackFile = null;
        }

        _automaticFontFallbackReporter(_automaticFontFallbackName, _automaticFontFallbackFile);
    }

    private void ReportActualTmpAutomaticFontFallback(ResolvedFont resolved)
    {
        if (!IsAutomaticFontSource(resolved.Source))
        {
            return;
        }

        var actualName = ResolveAutomaticFontReportName(resolved);
        var actualFile = string.Equals(resolved.Source, "auto-file", StringComparison.OrdinalIgnoreCase)
            ? resolved.Value
            : null;

        if (!string.Equals(_automaticFontFallbackName, actualName, StringComparison.Ordinal) ||
            !string.Equals(_automaticFontFallbackFile, actualFile, StringComparison.Ordinal))
        {
            _automaticFontFallbackName = actualName;
            _automaticFontFallbackFile = actualFile;
            _automaticFontFallbackReporter(_automaticFontFallbackName, _automaticFontFallbackFile);
        }

        var logKey = $"{resolved.Source}:{resolved.Value}:{actualName}";
        if (_loggedAutomaticTmpFontFallbacks.Add(logKey))
        {
            var logName = string.IsNullOrWhiteSpace(resolved.Font.name)
                ? resolved.DisplayName
                : resolved.Font.name;
            var preferredText = string.Equals(resolved.Source, "auto-file", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeFontPath(resolved.Value), NormalizeFontPath(PreferredAutomaticFontFile), StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $"（首选 {PreferredAutomaticFontName} 不可用，已降级）";
            _logger.LogInfo($"TMP 自动字体实际使用：{logName}{preferredText}。");
        }
    }

    private static string ResolveAutomaticFontReportName(ResolvedFont resolved)
    {
        if (string.Equals(resolved.Source, "auto-file", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(NormalizeFontPath(resolved.Value), NormalizeFontPath(PreferredAutomaticFontFile), StringComparison.OrdinalIgnoreCase))
            {
                return PreferredAutomaticFontName;
            }

            var fileName = Path.GetFileNameWithoutExtension(resolved.Value);
            var fallbackName = string.IsNullOrWhiteSpace(fileName) ? resolved.Font.name : fileName;
            return $"TMP 实际：{fallbackName}（首选 {PreferredAutomaticFontName} 不可用）";
        }

        return string.IsNullOrWhiteSpace(resolved.Font.name)
            ? resolved.DisplayName
            : resolved.Font.name;
    }

    private static bool IsAutomaticFontSource(string source)
    {
        return string.Equals(source, "auto-file", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, "auto-name", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearAutomaticFontFallbackReport()
    {
        _automaticFontFallbackConfigKey = null;
        _automaticFontFallbackName = null;
        _automaticFontFallbackFile = null;
        _automaticFontFallbackReporter(null, null);
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
        return TmpFontAssetCanRenderText(ResolveOriginalTmpFontAsset(component), translatedText, includeFallbacks: false);
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

        if (TryTmpFontAssetContainsCharacterReadOnly(fontAsset, character, out var hasCharacter) && hasCharacter)
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

    private static bool TryTmpFontAssetContainsCharacterReadOnly(
        object fontAsset,
        char character,
        out bool hasCharacter)
    {
        hasCharacter = false;
        var unicode = (uint)character;
        foreach (var lookupTable in EnumerateTmpCharacterLookupTables(fontAsset))
        {
            if (CollectionContainsUnicodeKey(lookupTable, unicode))
            {
                hasCharacter = true;
                return true;
            }
        }

        foreach (var characterTable in EnumerateTmpCharacterTables(fontAsset))
        {
            foreach (var item in EnumerateCollectionItems(characterTable))
            {
                if (TryGetUnicode(item, out var itemUnicode) && itemUnicode == unicode)
                {
                    hasCharacter = true;
                    return true;
                }
            }
        }

        return true;
    }

    private static IEnumerable<object?> EnumerateTmpCharacterLookupTables(object fontAsset)
    {
        yield return GetProperty(fontAsset, "characterLookupTable");
        yield return GetField(fontAsset, "m_CharacterLookupDictionary");
    }

    private static IEnumerable<object?> EnumerateTmpCharacterTables(object fontAsset)
    {
        yield return GetProperty(fontAsset, "characterTable");
        yield return GetField(fontAsset, "m_CharacterTable");
    }

    private static bool CollectionContainsUnicodeKey(object? collection, uint unicode)
    {
        if (collection == null)
        {
            return false;
        }

        if (collection is IDictionary dictionary)
        {
            foreach (var key in dictionary.Keys)
            {
                if (KeyMatchesUnicode(key, unicode))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var method in collection
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "ContainsKey" && method.GetParameters().Length == 1))
        {
            var argument = ConvertUnicodeKey(unicode, method.GetParameters()[0].ParameterType);
            if (argument == null)
            {
                continue;
            }

            try
            {
                if (method.Invoke(collection, new[] { argument }) is true)
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private static object? ConvertUnicodeKey(uint unicode, Type targetType)
    {
        if (targetType == typeof(uint))
        {
            return unicode;
        }

        if (targetType == typeof(int) && unicode <= int.MaxValue)
        {
            return (int)unicode;
        }

        if (targetType == typeof(char) && unicode <= char.MaxValue)
        {
            return (char)unicode;
        }

        return null;
    }

    private static bool KeyMatchesUnicode(object? key, uint unicode)
    {
        return key switch
        {
            uint uintValue => uintValue == unicode,
            int intValue => intValue >= 0 && (uint)intValue == unicode,
            char charValue => charValue == unicode,
            _ => false
        };
    }

    private static bool TryGetUnicode(object? character, out uint unicode)
    {
        unicode = 0;
        if (character == null)
        {
            return false;
        }

        foreach (var memberName in new[] { "unicode", "m_Unicode" })
        {
            var value = GetProperty(character, memberName) ?? GetField(character, memberName);
            switch (value)
            {
                case uint uintValue:
                    unicode = uintValue;
                    return true;
                case int intValue when intValue >= 0:
                    unicode = (uint)intValue;
                    return true;
                case char charValue:
                    unicode = charValue;
                    return true;
            }
        }

        return false;
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

    private bool HasComponentFontOverride(TranslationCacheKey key, TranslationCacheContext context)
    {
        return _cache.TryGetReplacementFont(key, context, out _);
    }

    private object? ResolveTmpFontAsset(
        RuntimeConfig config,
        TranslationCacheKey? key,
        TranslationCacheContext? context,
        out ResolvedFont? resolvedFont)
    {
        return ResolveTmpFontAsset(
            config,
            key,
            context,
            config.FontSamplingPointSize,
            TmpFallbackStyleProfile.PreserveSource,
            out resolvedFont);
    }

    private object? ResolveTmpFontAsset(
        RuntimeConfig config,
        TranslationCacheKey? key,
        TranslationCacheContext? context,
        int samplingPointSize,
        TmpFallbackStyleProfile styleProfile,
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
        foreach (var candidate in EnumerateTmpFontCandidates(config, key, context, styleProfile))
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
                ReportActualTmpAutomaticFontFallback(resolved);
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
            ReportActualTmpAutomaticFontFallback(resolved);
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
        TranslationCacheContext? context,
        TmpFallbackStyleProfile styleProfile)
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

        foreach (var fontFile in SelectTmpAutomaticFontFiles(styleProfile))
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

    private static IEnumerable<string> SelectTmpAutomaticFontFiles(TmpFallbackStyleProfile styleProfile)
    {
        return styleProfile == TmpFallbackStyleProfile.ConstrainOutline
            ? OutlineConstrainedTmpAutomaticFontFiles
            : StandardTmpAutomaticFontFiles;
    }

    private ResolvedFont? ResolveExplicitFont(FontCandidate candidate, int size)
    {
        var cacheKey = $"{candidate.Source}:{candidate.Value}:{candidate.RegularFaceNamesKey}:{size}";
        if (_unityFonts.TryGetValue(cacheKey, out var cached))
        {
            return new ResolvedFont(cacheKey, candidate.Source, candidate.Value, candidate.DisplayName, cached);
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
        return new ResolvedFont(cacheKey, candidate.Source, candidate.Value, candidate.DisplayName, font);
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

    private void TryEnableTmpMatchMaterialPreset()
    {
        var settingsType = ResolveType(TmpSettingsTypeNames);
        var settings = GetStaticProperty(settingsType, "instance");
        if (settings == null)
        {
            WarnTmpMatchMaterialPresetUnavailable();
            return;
        }

        var changed = SetField(settings, "m_matchMaterialPreset", true);
        changed |= SetProperty(settings, "matchMaterialPreset", true);
        if (!changed)
        {
            WarnTmpMatchMaterialPresetUnavailable();
            return;
        }

        if (!_loggedTmpMatchMaterialPreset)
        {
            _loggedTmpMatchMaterialPreset = true;
            _logger.LogInfo("TMP 后备字体材质匹配已启用。");
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

    private static object? GetStaticProperty(Type? type, string propertyName)
    {
        var property = type?.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (property == null || !property.CanRead)
        {
            return null;
        }

        try
        {
            return property.GetValue(null, null);
        }
        catch
        {
            return null;
        }
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
        return SetTmpFont(component, fontAsset, populateCharacters: false, resolved: null);
    }

    private static bool SetTmpFont(object component, object? fontAsset, bool populateCharacters)
    {
        return SetTmpFont(component, fontAsset, populateCharacters, resolved: null);
    }

    private static bool SetTmpFont(object component, object? fontAsset, ResolvedFont? resolved)
    {
        return SetTmpFont(component, fontAsset, populateCharacters: false, resolved);
    }

    private static bool SetTmpFont(object component, object? fontAsset, bool populateCharacters, ResolvedFont? resolved)
    {
        var componentMaterial = GetTmpComponentMaterial(component);
        if (fontAsset != null && populateCharacters)
        {
            PopulateTmpFontAsset(fontAsset, component);
        }

        var changed = SetProperty(component, "font", fontAsset);
        changed |= SetProperty(component, "fontAsset", fontAsset);
        changed |= SetField(component, "m_fontAsset", fontAsset);
        changed |= SetField(component, "m_currentFontAsset", fontAsset);

        if (fontAsset != null)
        {
            var styleProfile = ResolveTmpFallbackStyleProfile(componentMaterial, resolved);
            var fontAssetMaterial = EnsureTmpFontAssetMaterial(fontAsset, componentMaterial);
            ApplyTmpAutomaticFallbackStyle(fontAssetMaterial, resolved, styleProfile);
            var matchedMaterial = ResolveTmpFallbackMaterial(fontAsset, componentMaterial, fontAssetMaterial);
            if (matchedMaterial != null)
            {
                ApplyTmpAutomaticFallbackStyle(matchedMaterial, resolved, styleProfile);
                ApplyTmpComponentColorToMaterial(component, matchedMaterial);
                changed |= SetTmpMaterial(component, matchedMaterial);
            }
        }

        MarkTmpTextDirty(component);
        return changed;
    }

    private static void PrepareTmpFallbackMaterial(object component, object fontAsset, ResolvedFont? resolved)
    {
        var componentMaterial = GetTmpComponentMaterial(component);
        if (componentMaterial == null)
        {
            return;
        }

        var styleProfile = ResolveTmpFallbackStyleProfile(componentMaterial, resolved);
        var fontAssetMaterial = EnsureTmpFontAssetMaterial(fontAsset, componentMaterial);
        if (fontAssetMaterial != null)
        {
            CopyTmpMaterialPresetProperties(componentMaterial, fontAssetMaterial);
            ApplyTmpAutomaticFallbackStyle(fontAssetMaterial, resolved, styleProfile);
            ApplyTmpComponentColorToMaterial(component, fontAssetMaterial);
        }

        var matchedMaterial = ResolveTmpFallbackMaterial(fontAsset, componentMaterial, fontAssetMaterial);
        ApplyTmpAutomaticFallbackStyle(matchedMaterial, resolved, styleProfile);
        ApplyTmpComponentColorToMaterial(component, matchedMaterial);
    }

    private static object? GetTmpComponentMaterial(object component)
    {
        return GetProperty(component, "fontSharedMaterial") ??
            GetProperty(component, "fontMaterial") ??
            GetProperty(component, "sharedMaterial") ??
            GetProperty(component, "material") ??
            GetField(component, "m_fontMaterial") ??
            GetField(component, "m_sharedMaterial") ??
            GetField(component, "m_currentMaterial") ??
            GetField(component, "m_material");
    }

    private static object? GetTmpFontAssetMaterial(object fontAsset)
    {
        return GetProperty(fontAsset, "material") ??
            GetProperty(fontAsset, "fontMaterial") ??
            GetField(fontAsset, "material") ??
            GetField(fontAsset, "m_Material") ??
            GetField(fontAsset, "m_material") ??
            GetField(fontAsset, "m_fontMaterial") ??
            GetField(fontAsset, "m_fontAssetMaterial");
    }

    private static object? EnsureTmpFontAssetMaterial(object fontAsset, object? templateMaterial)
    {
        var material = GetTmpFontAssetMaterial(fontAsset);
        if (templateMaterial is not Material template)
        {
            return material;
        }

        var existingMaterial = material as Material;
        if (existingMaterial != null && MaterialShadersMatch(existingMaterial, template))
        {
            return existingMaterial;
        }

        var atlasTexture = GetTmpFontAssetAtlasTexture(fontAsset) ?? GetTmpMaterialTexture(existingMaterial, "_MainTex");
        if (atlasTexture == null)
        {
            return existingMaterial;
        }

        var fallbackMaterial = new Material(template)
        {
            name = $"{GetProperty(fontAsset, "name") ?? "TMP Fallback"} Material"
        };
        ApplyTmpAtlasTexture(fallbackMaterial, atlasTexture);
        if (existingMaterial != null && !MaterialShadersMatch(existingMaterial, template))
        {
            CopyTmpFontAtlasMetrics(existingMaterial, fallbackMaterial);
        }

        SetTmpFontAssetMaterial(fontAsset, fallbackMaterial);
        return fallbackMaterial;
    }

    private static bool MaterialShadersMatch(Material left, Material right)
    {
        try
        {
            return left.shader == right.shader ||
                string.Equals(left.shader?.name, right.shader?.name, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static void CopyTmpFontAtlasMetrics(Material source, Material target)
    {
        CopyMaterialFloat(source, target, "_TextureWidth");
        CopyMaterialFloat(source, target, "_TextureHeight");
        CopyMaterialFloat(source, target, "_GradientScale");
    }

    private static void CopyMaterialFloat(Material source, Material target, string name)
    {
        try
        {
            if (source.HasProperty(name) && target.HasProperty(name))
            {
                target.SetFloat(name, source.GetFloat(name));
            }
        }
        catch
        {
        }
    }

    private static void ApplyTmpAutomaticFallbackStyle(object? material, ResolvedFont? resolved, TmpFallbackStyleProfile styleProfile)
    {
        if (resolved == null ||
            !IsAutomaticFontSource(resolved.Source) ||
            styleProfile != TmpFallbackStyleProfile.ConstrainOutline)
        {
            return;
        }

        SetTmpMaterialFloatAtMost(material, "_WeightNormal", TmpConstrainedWeightNormal);
        SetTmpMaterialFloatAtMost(material, "_WeightBold", TmpConstrainedWeightBold);
        SetTmpMaterialFloatAtMost(material, "_FaceDilate", TmpConstrainedFaceDilate);
        SetTmpMaterialFloatAtMost(material, "_OutlineWidth", TmpConstrainedOutlineWidth);
        SetTmpMaterialFloatAtMost(material, "_OutlineSoftness", TmpConstrainedOutlineSoftness);
    }

    private static TmpFallbackStyleProfile ResolveTmpFallbackStyleProfile(object? sourceMaterial, ResolvedFont? resolved)
    {
        if (resolved == null || !IsAutomaticFontSource(resolved.Source))
        {
            return TmpFallbackStyleProfile.PreserveSource;
        }

        return SourceTmpMaterialNeedsOutlineConstraint(sourceMaterial)
            ? TmpFallbackStyleProfile.ConstrainOutline
            : TmpFallbackStyleProfile.PreserveSource;
    }

    private static bool SourceTmpMaterialNeedsOutlineConstraint(object? sourceMaterial)
    {
        if (sourceMaterial is not Material material)
        {
            return false;
        }

        if (TryGetMaterialFloat(material, "_OutlineWidth", out var outlineWidth) &&
            outlineWidth >= TmpOutlineConstraintThreshold)
        {
            return true;
        }

        if (TryGetMaterialFloat(material, "_FaceDilate", out var faceDilate) &&
            faceDilate >= TmpFaceDilateConstraintThreshold)
        {
            return true;
        }

        return TryGetMaterialFloat(material, "_OutlineWidth", out outlineWidth) &&
            outlineWidth >= TmpNameHintOutlineConstraintThreshold &&
            TmpMaterialNameSuggestsPixelOutline(material);
    }

    private static bool TmpMaterialNameSuggestsPixelOutline(Material material)
    {
        var materialName = SafeUnityObjectName(material);
        var shaderName = SafeShaderName(material);
        return ContainsStyleHint(materialName, "3270") ||
            ContainsStyleHint(materialName, "pixel") ||
            ContainsStyleHint(materialName, "bitmap") ||
            ContainsStyleHint(shaderName, "pixel") ||
            ContainsStyleHint(shaderName, "bitmap");
    }

    private static bool ContainsStyleHint(string value, string hint)
    {
        return value.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string SafeUnityObjectName(UnityEngine.Object instance)
    {
        try
        {
            return instance.name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeShaderName(Material material)
    {
        try
        {
            return material.shader?.name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void SetTmpMaterialFloatAtMost(object? material, string name, float maxValue)
    {
        if (material is not Material target)
        {
            return;
        }

        try
        {
            if (target.HasProperty(name))
            {
                var current = target.GetFloat(name);
                if (current > maxValue)
                {
                    target.SetFloat(name, maxValue);
                }
            }
        }
        catch
        {
        }
    }

    private static Texture? GetTmpMaterialTexture(Material? material, string name)
    {
        if (material == null)
        {
            return null;
        }

        try
        {
            return material.HasProperty(name) ? material.GetTexture(name) : null;
        }
        catch
        {
            return null;
        }
    }

    private static Texture? GetTmpFontAssetAtlasTexture(object fontAsset)
    {
        if (GetProperty(fontAsset, "atlasTexture") is Texture atlasTexture)
        {
            return atlasTexture;
        }

        if (GetField(fontAsset, "m_AtlasTexture") is Texture atlasTextureField)
        {
            return atlasTextureField;
        }

        var atlasTextures = GetProperty(fontAsset, "atlasTextures") ?? GetField(fontAsset, "m_AtlasTextures");
        if (atlasTextures is Array textures)
        {
            foreach (var texture in textures)
            {
                if (texture is Texture item)
                {
                    return item;
                }
            }
        }

        return null;
    }

    private static bool SetTmpFontAssetMaterial(object fontAsset, Material fallbackMaterial)
    {
        var changed = SetProperty(fontAsset, "material", fallbackMaterial);
        changed |= SetProperty(fontAsset, "fontMaterial", fallbackMaterial);
        changed |= SetField(fontAsset, "material", fallbackMaterial);
        changed |= SetField(fontAsset, "m_Material", fallbackMaterial);
        changed |= SetField(fontAsset, "m_material", fallbackMaterial);
        changed |= SetField(fontAsset, "m_fontMaterial", fallbackMaterial);
        changed |= SetField(fontAsset, "m_fontAssetMaterial", fallbackMaterial);
        return changed;
    }

    private static void ApplyTmpAtlasTexture(Material material, Texture atlasTexture)
    {
        try
        {
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", atlasTexture);
            }

            if (material.HasProperty("_TextureWidth"))
            {
                material.SetFloat("_TextureWidth", atlasTexture.width);
            }

            if (material.HasProperty("_TextureHeight"))
            {
                material.SetFloat("_TextureHeight", atlasTexture.height);
            }
        }
        catch
        {
        }
    }

    private static bool SetTmpMaterial(object component, object matchedMaterial)
    {
        var changed = SetProperty(component, "fontMaterial", matchedMaterial);
        changed |= SetProperty(component, "fontSharedMaterial", matchedMaterial);
        changed |= SetProperty(component, "sharedMaterial", matchedMaterial);
        changed |= SetProperty(component, "material", matchedMaterial);
        changed |= SetField(component, "m_fontMaterial", matchedMaterial);
        changed |= SetField(component, "m_sharedMaterial", matchedMaterial);
        changed |= SetField(component, "m_currentMaterial", matchedMaterial);
        changed |= SetField(component, "m_material", matchedMaterial);
        return changed;
    }

    private static void CopyTmpMaterialPresetProperties(object sourceMaterial, object targetMaterial)
    {
        if (sourceMaterial is not Material source || targetMaterial is not Material target)
        {
            return;
        }

        var mainTex = target.HasProperty("_MainTex") ? target.GetTexture("_MainTex") : null;
        var hasGradientScale = TryGetMaterialFloat(target, "_GradientScale", out var gradientScale);
        var hasTextureWidth = TryGetMaterialFloat(target, "_TextureWidth", out var textureWidth);
        var hasTextureHeight = TryGetMaterialFloat(target, "_TextureHeight", out var textureHeight);

        try
        {
            target.CopyPropertiesFromMaterial(source);
            SetProperty(target, "shaderKeywords", GetProperty(source, "shaderKeywords"));
        }
        catch
        {
            return;
        }

        if (mainTex != null && target.HasProperty("_MainTex"))
        {
            target.SetTexture("_MainTex", mainTex);
        }

        RestoreMaterialFloat(target, "_GradientScale", hasGradientScale, gradientScale);
        RestoreMaterialFloat(target, "_TextureWidth", hasTextureWidth, textureWidth);
        RestoreMaterialFloat(target, "_TextureHeight", hasTextureHeight, textureHeight);
        CopyMaterialFloat(source, target, "_WeightNormal");
        CopyMaterialFloat(source, target, "_WeightBold");
        CopyMaterialFloat(source, target, "_FaceDilate");
        CopyMaterialFloat(source, target, "_OutlineWidth");
        CopyMaterialFloat(source, target, "_OutlineSoftness");
    }

    private static void ApplyTmpComponentColorToMaterial(object component, object? targetMaterial)
    {
        if (targetMaterial is not Material target ||
            GetProperty(component, "color") is not Color componentColor)
        {
            return;
        }

        SetTmpMaterialColor(target, "_FaceColor", componentColor);
        SetTmpMaterialColor(target, "_Color", componentColor);
    }

    private static void SetTmpMaterialColor(Material target, string name, Color componentColor)
    {
        try
        {
            if (target.HasProperty(name))
            {
                target.SetColor(name, componentColor);
            }
        }
        catch
        {
        }
    }

    private void LogTmpMaterialDiagnosticsIfNeeded(
        RuntimeConfig config,
        object component,
        TranslationCacheContext context,
        string translatedText,
        object fontAsset,
        ResolvedFont? resolved)
    {
        if (!config.EnableTranslationDebugLogs ||
            _loggedTmpMaterialDiagnostics.Count >= MaxTmpMaterialDiagnostics)
        {
            return;
        }

        var text = GetProperty(component, "text") as string ?? translatedText;
        var key = string.Join(
            "|",
            context.SceneName ?? string.Empty,
            context.ComponentHierarchy ?? string.Empty,
            component.GetType().FullName ?? string.Empty,
            text);
        if (!_loggedTmpMaterialDiagnostics.Add(key))
        {
            return;
        }

        var componentMaterial = GetTmpComponentMaterial(component);
        var fontAssetMaterial = GetTmpFontAssetMaterial(fontAsset);
        var styleProfile = ResolveTmpFallbackStyleProfile(componentMaterial, resolved);
        _logger.LogInfo(
            "TMP 材质诊断：" +
            $"层级={context.ComponentHierarchy ?? "未知"}；" +
            $"组件={component.GetType().FullName}；" +
            $"文本={TrimDiagnosticText(text)}；" +
            $"fallbackStyleProfile={styleProfile}；" +
            $"组件颜色={FormatTmpColor(GetProperty(component, "color"))}；" +
            $"组件材质={FormatTmpMaterial(componentMaterial)}；" +
            $"后备字体材质={FormatTmpMaterial(fontAssetMaterial)}。");
    }

    private void LogTmpSubTextDiagnosticsIfNeeded(
        RuntimeConfig config,
        object component,
        TranslationCacheContext context,
        string translatedText)
    {
        if (!config.EnableTranslationDebugLogs ||
            _loggedTmpSubTextMaterialDiagnostics.Count >= MaxTmpSubTextMaterialDiagnostics)
        {
            return;
        }

        var text = GetProperty(component, "text") as string ?? translatedText;
        var key = string.Join(
            "|",
            context.SceneName ?? string.Empty,
            context.ComponentHierarchy ?? string.Empty,
            component.GetType().FullName ?? string.Empty,
            text);
        if (!_loggedTmpSubTextMaterialDiagnostics.Add(key))
        {
            return;
        }

        InvokeMethodIfAvailable(component, "ForceMeshUpdate");
        if (GetField(component, "m_subTextObjects") is not Array subTextObjects)
        {
            _logger.LogInfo($"TMP 子材质诊断：层级={context.ComponentHierarchy ?? "未知"}；未找到 m_subTextObjects。");
            return;
        }

        for (var i = 0; i < Math.Min(subTextObjects.Length, 6); i++)
        {
            var subText = subTextObjects.GetValue(i);
            if (subText == null)
            {
                continue;
            }

            _logger.LogInfo(
                "TMP 子材质诊断：" +
                $"层级={context.ComponentHierarchy ?? "未知"}；" +
                $"索引={i}；" +
                $"fontAsset={GetProperty(subText, "fontAsset")?.GetType().FullName ?? "<null>"}；" +
                $"shared={FormatTmpMaterial(GetProperty(subText, "sharedMaterial"))}；" +
                $"fallback={FormatTmpMaterial(GetProperty(subText, "fallbackMaterial"))}；" +
                $"fallbackSource={FormatTmpMaterial(GetProperty(subText, "fallbackSourceMaterial"))}。");
        }
    }

    private static string FormatTmpMaterial(object? material)
    {
        if (material is not Material tmpMaterial)
        {
            return material == null ? "<null>" : material.GetType().FullName ?? material.ToString() ?? "<unknown>";
        }

        return string.Join(
            ", ",
            $"name={tmpMaterial.name}",
            $"id={tmpMaterial.GetInstanceID()}",
            $"shader={FormatTmpShader(tmpMaterial)}",
            $"_FaceColor={FormatTmpMaterialColor(tmpMaterial, "_FaceColor")}",
            $"_Color={FormatTmpMaterialColor(tmpMaterial, "_Color")}",
            $"_OutlineColor={FormatTmpMaterialColor(tmpMaterial, "_OutlineColor")}",
            $"_OutlineWidth={FormatTmpMaterialFloat(tmpMaterial, "_OutlineWidth")}",
            $"_OutlineSoftness={FormatTmpMaterialFloat(tmpMaterial, "_OutlineSoftness")}",
            $"_WeightNormal={FormatTmpMaterialFloat(tmpMaterial, "_WeightNormal")}",
            $"_WeightBold={FormatTmpMaterialFloat(tmpMaterial, "_WeightBold")}",
            $"_FaceDilate={FormatTmpMaterialFloat(tmpMaterial, "_FaceDilate")}",
            $"_GradientScale={FormatTmpMaterialFloat(tmpMaterial, "_GradientScale")}",
            $"_MainTex={FormatTmpMaterialTexture(tmpMaterial, "_MainTex")}");
    }

    private static string FormatTmpColor(object? color)
    {
        return color is Color componentColor ? componentColor.ToString() : color?.ToString() ?? "<null>";
    }

    private static string FormatTmpShader(Material material)
    {
        try
        {
            return material.shader == null ? "<null>" : material.shader.name;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string FormatTmpMaterialColor(Material material, string name)
    {
        try
        {
            return material.HasProperty(name) ? material.GetColor(name).ToString() : "<missing>";
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string FormatTmpMaterialFloat(Material material, string name)
    {
        try
        {
            return material.HasProperty(name) ? material.GetFloat(name).ToString("0.###") : "<missing>";
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string FormatTmpMaterialTexture(Material material, string name)
    {
        try
        {
            if (!material.HasProperty(name))
            {
                return "<missing>";
            }

            var texture = material.GetTexture(name);
            return texture == null ? "<null>" : $"{texture.name}#{texture.GetInstanceID()}";
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string TrimDiagnosticText(string text)
    {
        var normalized = text.Replace("\r", "\\r").Replace("\n", "\\n");
        return normalized.Length <= 80 ? normalized : normalized[..80] + "...";
    }

    private static bool TryGetMaterialFloat(Material material, string name, out float value)
    {
        value = 0;
        try
        {
            if (!material.HasProperty(name))
            {
                return false;
            }

            value = material.GetFloat(name);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RestoreMaterialFloat(Material material, string name, bool hasValue, float value)
    {
        if (!hasValue)
        {
            return;
        }

        try
        {
            if (material.HasProperty(name))
            {
                material.SetFloat(name, value);
            }
        }
        catch
        {
        }
    }

    private static object? ResolveTmpFallbackMaterial(object? fontAsset, object? componentMaterial, object? fontAssetMaterial)
    {
        if (componentMaterial == null)
        {
            return fontAssetMaterial;
        }

        var materialManagerType = ResolveType(TmpMaterialManagerTypeNames);
        if (fontAsset != null)
        {
            const int atlasIndex = 0;
            var atlasMethod = materialManagerType?
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(candidate => candidate.Name == "GetFallbackMaterial")
                .FirstOrDefault(candidate =>
                {
                    var parameters = candidate.GetParameters();
                    return parameters.Length == 3 &&
                        IsCompatibleValue(parameters[0].ParameterType, fontAsset) &&
                        IsCompatibleValue(parameters[1].ParameterType, componentMaterial) &&
                        parameters[2].ParameterType == typeof(int);
                });
            if (atlasMethod != null)
            {
                try
                {
                    return atlasMethod.Invoke(null, new object[] { fontAsset, componentMaterial, atlasIndex }) ?? fontAssetMaterial ?? componentMaterial;
                }
                catch
                {
                }
            }
        }

        if (fontAssetMaterial == null)
        {
            return componentMaterial;
        }

        var method = materialManagerType?
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(candidate => candidate.Name == "GetFallbackMaterial")
            .FirstOrDefault(candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 2 &&
                    IsCompatibleValue(parameters[0].ParameterType, componentMaterial) &&
                    IsCompatibleValue(parameters[1].ParameterType, fontAssetMaterial);
            });
        if (method == null)
        {
            return fontAssetMaterial;
        }

        try
        {
            return method.Invoke(null, new[] { componentMaterial, fontAssetMaterial }) ?? fontAssetMaterial;
        }
        catch
        {
            return fontAssetMaterial;
        }
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

    private void WarnTmpMatchMaterialPresetUnavailable()
    {
        if (_warnedTmpMatchMaterialPresetUnavailable)
        {
            return;
        }

        _warnedTmpMatchMaterialPresetUnavailable = true;
        _logger.LogWarning("TMP 后备字体材质匹配未启用：当前游戏未暴露 TMP_Settings.matchMaterialPreset。");
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
        public static readonly ResolvedFont AutomaticProbe = new(
            cacheKey: "automatic-probe",
            source: "auto-file",
            value: string.Empty,
            displayName: string.Empty,
            font: null!);

        public ResolvedFont(string cacheKey, string source, string value, string displayName, Font font)
        {
            CacheKey = cacheKey;
            Source = source;
            Value = value;
            DisplayName = displayName;
            Font = font;
        }

        public string CacheKey { get; }

        public string Source { get; }

        public string Value { get; }

        public string DisplayName { get; }

        public Font Font { get; }
    }
}
