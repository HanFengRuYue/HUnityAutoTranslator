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
    private const int MaxImguiNewCapturesPerFrame = 1;
    private const int MaxImguiCacheRefreshesPerFrame = 1;
    private const double ImguiPendingRefreshSeconds = 1;
    private const double ImguiStateTtlSeconds = 300;
    private const double ImguiNewCaptureIntervalSeconds = 0.25;
    private const double ImguiCacheRefreshIntervalSeconds = 0.25;
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
                _harmony!.Patch(method, prefix: new HarmonyMethod(typeof(ImguiHookInstaller), nameof(PrefixStringText)));
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

    private static void PrefixStringText(ref string text)
    {
        if (_instance == null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        text = _instance.TranslateOrQueue(text);
    }

    private string TranslateOrQueue(string text)
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
            _fontReplacement?.ApplyToImgui(key, context);
            return stateResult.DisplayText;
        }

        _fontReplacement?.RestoreImgui();
        return stateResult.DisplayText;
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
        var decision = _pipeline.Process(new CapturedText("imgui:" + text, text, isVisible: true, context));
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
