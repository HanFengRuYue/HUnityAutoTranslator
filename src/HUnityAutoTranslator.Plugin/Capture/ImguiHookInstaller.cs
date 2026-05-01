using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Runtime;
using HUnityAutoTranslator.Plugin.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HUnityAutoTranslator.Plugin.Capture;

internal sealed class ImguiHookInstaller : ITextCaptureModule
{
    private const string HarmonyId = "com.hanfeng.hunityautotranslator.imgui";
    private const int MaxImguiStateEntries = 2048;
    private const int MaxImguiNewCapturesPerFrame = 16;
    private const int MaxImguiCacheRefreshesPerFrame = 32;
    private const int DefaultImguiFontSize = 14;
    private const double ImguiPendingRefreshSeconds = 1;
    private const double ImguiStateTtlSeconds = 300;
    private const double ImguiNewCaptureIntervalSeconds = 0.05;
    private const double ImguiCacheRefreshIntervalSeconds = 0.05;
    private static ImguiHookInstaller? _instance;

    private readonly TextPipeline _pipeline;
    private readonly ITranslationCache _cache;
    private readonly ManualLogSource _logger;
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly UnityTextFontReplacementService? _fontReplacement;
    private readonly ImguiTranslationStateCache _stateCache = new(
        MaxImguiStateEntries,
        MaxImguiNewCapturesPerFrame,
        MaxImguiCacheRefreshesPerFrame,
        ImguiPendingRefreshSeconds,
        ImguiStateTtlSeconds,
        ImguiNewCaptureIntervalSeconds,
        ImguiCacheRefreshIntervalSeconds);
    private Harmony? _harmony;
    private bool _enabled;
    private bool _warned;

    public ImguiHookInstaller(TextPipeline pipeline, ITranslationCache cache, ManualLogSource logger, RuntimeConfig config)
        : this(pipeline, cache, logger, () => config, fontReplacement: null)
    {
    }

    public ImguiHookInstaller(
        TextPipeline pipeline,
        ITranslationCache cache,
        ManualLogSource logger,
        Func<RuntimeConfig> configProvider,
        UnityTextFontReplacementService? fontReplacement = null)
    {
        _pipeline = pipeline;
        _cache = cache;
        _logger = logger;
        _configProvider = configProvider;
        _fontReplacement = fontReplacement;
    }

    public string Name => "IMGUI";

    public bool IsEnabled => _enabled && _configProvider().EnableImgui;

    public void Start()
    {
        try
        {
            _instance = this;
            _harmony = new Harmony(HarmonyId);
            PatchStringTextMethods(typeof(UnityEngine.GUI));
            PatchStringTextMethods(typeof(UnityEngine.GUILayout));
            _enabled = true;
            _logger.LogInfo("IMGUI 捕获已启用。");
        }
        catch (Exception ex)
        {
            _enabled = false;
            WarnOnce($"IMGUI 钩子安装失败，IMGUI 捕获已关闭：{ex.Message}");
        }
    }

    public void Tick(bool forceFullScan = false)
    {
    }

    public void Dispose()
    {
        _harmony?.UnpatchSelf();
        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }

    private void PatchStringTextMethods(Type type)
    {
        foreach (var method in AccessTools.GetDeclaredMethods(type))
        {
            if (!IsSupportedMethod(method))
            {
                continue;
            }

            try
            {
                _harmony!.Patch(
                    method,
                    prefix: new HarmonyMethod(typeof(ImguiHookInstaller), nameof(PrefixStringText)),
                    postfix: new HarmonyMethod(typeof(ImguiHookInstaller), nameof(PostfixStringText)));
            }
            catch (Exception ex)
            {
                WarnOnce($"给 {type.FullName}.{method.Name} 安装钩子失败：{ex.Message}");
            }
        }
    }

    private static bool IsSupportedMethod(MethodInfo method)
    {
        if (method.Name is not ("Label" or "Button" or "Toggle" or "TextField"))
        {
            return false;
        }

        return method.GetParameters().Any(parameter => parameter.Name == "text" && parameter.ParameterType == typeof(string));
    }

    private static void PrefixStringText(MethodBase __originalMethod, object[] __args, ref string text, out ImguiDrawState? __state)
    {
        __state = null;
        if (_instance == null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var resolution = _instance.TranslateOrQueue(text);
        text = resolution.DisplayText;
        if (resolution.IsTranslated)
        {
            __state = _instance.TryBeginFontScope(__originalMethod, __args, resolution);
        }
    }

    private static void PostfixStringText(ImguiDrawState? __state)
    {
        __state?.Restore();
    }

    private ImguiTextResolution TranslateOrQueue(string text)
    {
        var config = _configProvider();
        var promptPolicyVersion = TextPipeline.GetPromptPolicyVersion(config);
        var key = TranslationCacheKey.Create(text, config.TargetLanguage, config.Provider, promptPolicyVersion);
        var sceneName = GetActiveSceneName();
        var context = new TranslationCacheContext(sceneName, ComponentHierarchy: null, ComponentType: "IMGUI");
        var stateResult = _stateCache.Resolve(
            text,
            key.TargetLanguage,
            key.PromptPolicyVersion,
            sceneName,
            Time.unscaledTime,
            Time.frameCount,
            () => TryGetCachedImguiTranslation(key, context),
            () => ProcessImguiText(text, context));

        if (stateResult.IsTranslated)
        {
            return new ImguiTextResolution(stateResult.DisplayText, true, key, context);
        }

        return new ImguiTextResolution(stateResult.DisplayText, false, key, context);
    }

    private ImguiDrawState? TryBeginFontScope(
        MethodBase originalMethod,
        object[] args,
        ImguiTextResolution resolution)
    {
        if (_fontReplacement == null)
        {
            return null;
        }

        var style = ResolveStyle(originalMethod, args);
        var fontSize = ResolveFontSize(style);
        if (!_fontReplacement.TryResolveImguiFont(resolution.Key, resolution.Context, fontSize, resolution.DisplayText, out var font))
        {
            return null;
        }

        if (style != null)
        {
            return ImguiDrawState.ApplyStyle(style, font);
        }

        return GUI.skin != null ? ImguiDrawState.ApplySkin(GUI.skin, font) : null;
    }

    private static GUIStyle? ResolveStyle(MethodBase originalMethod, object[] args)
    {
        foreach (var arg in args)
        {
            if (arg is GUIStyle style)
            {
                return style;
            }
        }

        var skin = GUI.skin;
        if (skin == null)
        {
            return null;
        }

        return originalMethod.Name switch
        {
            "Button" => skin.button,
            "Toggle" => skin.toggle,
            "TextField" => skin.textField,
            _ => skin.label
        };
    }

    private static int ResolveFontSize(GUIStyle? style)
    {
        if (style?.fontSize > 0)
        {
            return ClampImguiFontSize(style.fontSize);
        }

        var styleFontSize = TryGetFontSize(style?.font);
        if (styleFontSize > 0)
        {
            return ClampImguiFontSize(styleFontSize);
        }

        var skinFontSize = TryGetFontSize(GUI.skin?.font);
        return ClampImguiFontSize(skinFontSize > 0 ? skinFontSize : DefaultImguiFontSize);
    }

    private static int TryGetFontSize(Font? font)
    {
        if (font == null)
        {
            return 0;
        }

        try
        {
            return font.fontSize;
        }
        catch
        {
            return 0;
        }
    }

    private static int ClampImguiFontSize(int fontSize)
    {
        return Math.Max(8, Math.Min(96, fontSize));
    }

    private string? TryGetCachedImguiTranslation(TranslationCacheKey key, TranslationCacheContext context)
    {
        if (_cache.TryGet(key, context, out var translated))
        {
            return translated;
        }

        return null;
    }

    private string? ProcessImguiText(string text, TranslationCacheContext context)
    {
        var decision = _pipeline.Process(new CapturedText(
            "imgui:" + text,
            text,
            isVisible: true,
            context,
            publishResult: false));
        return decision.Kind == PipelineDecisionKind.UseCachedTranslation
            ? decision.TranslatedText
            : null;
    }

    private static string? GetActiveSceneName()
    {
        try
        {
            return SceneManager.GetActiveScene().name;
        }
        catch
        {
            return null;
        }
    }

    private sealed class ImguiTextResolution
    {
        public ImguiTextResolution(
            string displayText,
            bool isTranslated,
            TranslationCacheKey key,
            TranslationCacheContext context)
        {
            DisplayText = displayText;
            IsTranslated = isTranslated;
            Key = key;
            Context = context;
        }

        public string DisplayText { get; }

        public bool IsTranslated { get; }

        public TranslationCacheKey Key { get; }

        public TranslationCacheContext Context { get; }
    }

    private sealed class ImguiDrawState
    {
        private readonly GUIStyle? _style;
        private readonly GUISkin? _skin;
        private readonly Font? _originalFont;
        private bool _restored;

        private ImguiDrawState(GUIStyle? style, GUISkin? skin, Font? originalFont)
        {
            _style = style;
            _skin = skin;
            _originalFont = originalFont;
        }

        public static ImguiDrawState ApplyStyle(GUIStyle style, Font font)
        {
            var state = new ImguiDrawState(style, skin: null, style.font);
            style.font = font;
            return state;
        }

        public static ImguiDrawState ApplySkin(GUISkin skin, Font font)
        {
            var state = new ImguiDrawState(style: null, skin, skin.font);
            skin.font = font;
            return state;
        }

        public void Restore()
        {
            if (_restored)
            {
                return;
            }

            _restored = true;
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

    private void WarnOnce(string message)
    {
        if (_warned)
        {
            return;
        }

        _warned = true;
        _logger.LogWarning(message);
    }
}
