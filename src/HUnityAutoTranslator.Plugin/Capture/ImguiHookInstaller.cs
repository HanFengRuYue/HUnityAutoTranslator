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
    private const int MaxImguiPendingBatchSize = 96;
    private const double ImguiPendingRefreshSeconds = 1;
    private const double ImguiStateTtlSeconds = 300;
    private static ImguiHookInstaller? _instance;

    private readonly TextPipeline _pipeline;
    private readonly ITranslationCache _cache;
    private readonly ManualLogSource _logger;
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly UnityTextFontReplacementService? _fontReplacement;
    private readonly ImguiTranslationStateCache _stateCache = new(
        MaxImguiStateEntries,
        ImguiPendingRefreshSeconds,
        ImguiStateTtlSeconds);
    private Harmony? _harmony;
    private bool _enableImguiForDraw;
    private string _drawTargetLanguage = "zh-Hans";
    private string _drawPromptPolicyVersion = string.Empty;
    private string? _drawSceneName;
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

    public bool IsEnabled => _enabled && _enableImguiForDraw;

    public bool UsesGlobalObjectScan => false;

    public void Start()
    {
        try
        {
            _instance = this;
            _harmony = new Harmony(HarmonyId);
            RefreshDrawContext(_configProvider());
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

    public int Tick(bool forceFullScan = false, int? maxTargetsOverride = null)
    {
        var config = _configProvider();
        RefreshDrawContext(config);
        var maxCount = Math.Min(MaxImguiPendingBatchSize, Math.Max(1, config.MaxScanTargetsPerTick));
        var processed = 0;
        foreach (var pendingText in _stateCache.TakePendingBatch(maxCount, Time.unscaledTime))
        {
            ProcessPendingImguiText(pendingText);
            processed++;
        }

        return processed;
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

    private static void PrefixStringText(
        MethodBase __originalMethod,
        object[] __args,
        ref string text,
        ref UnityTextFontReplacementService.ImguiFontScope? __state)
    {
        __state = null;
        if (_instance == null ||
            !_instance._enabled ||
            !_instance._enableImguiForDraw ||
            string.IsNullOrEmpty(text) ||
            Event.current?.type != EventType.Repaint)
        {
            return;
        }

        var sourceText = text;
        var resolution = _instance.ResolveForDraw(sourceText);
        text = resolution.DisplayText;
        if (_instance._fontReplacement == null || string.IsNullOrEmpty(resolution.DisplayText))
        {
            return;
        }

        var config = _instance._configProvider();
        var key = TranslationCacheKey.Create(
            sourceText,
            _instance._drawTargetLanguage,
            config.Provider,
            _instance._drawPromptPolicyVersion);
        var context = new TranslationCacheContext(_instance._drawSceneName, ComponentHierarchy: null, ComponentType: "IMGUI");
        __state = _instance._fontReplacement?.BeginImguiDrawFontScope(__originalMethod, __args, key, context, resolution.DisplayText);
    }

    private static void PostfixStringText(UnityTextFontReplacementService.ImguiFontScope? __state)
    {
        __state?.Dispose();
    }

    private ImguiTranslationStateResult ResolveForDraw(string text)
    {
        return _stateCache.ResolveForDraw(
            text,
            _drawTargetLanguage,
            _drawPromptPolicyVersion,
            _drawSceneName,
            Time.unscaledTime,
            Time.frameCount);
    }

    private void ProcessPendingImguiText(ImguiPendingText pendingText)
    {
        var nowSeconds = Time.unscaledTime;
        var config = _configProvider();
        var key = TranslationCacheKey.Create(
            pendingText.SourceText,
            pendingText.TargetLanguage,
            config.Provider,
            pendingText.PromptPolicyVersion);
        var context = new TranslationCacheContext(pendingText.SceneName, ComponentHierarchy: null, ComponentType: "IMGUI");

        if (ImguiTextClassifier.ShouldSkipTranslation(pendingText.SourceText, pendingText.TargetLanguage))
        {
            _stateCache.MarkIgnored(pendingText, nowSeconds);
            return;
        }

        if (_cache.TryGet(key, context, out var translatedText))
        {
            _stateCache.MarkCached(pendingText, translatedText, nowSeconds);
            return;
        }

        if (!pendingText.ShouldProcessSource)
        {
            _stateCache.MarkCacheMiss(pendingText, nowSeconds);
            return;
        }

        var decision = _pipeline.Process(new CapturedText(
            "imgui:" + pendingText.SourceText,
            pendingText.SourceText,
            isVisible: true,
            context,
            publishResult: false));
        if (decision.Kind == PipelineDecisionKind.UseCachedTranslation && decision.TranslatedText != null)
        {
            _stateCache.MarkCached(pendingText, decision.TranslatedText, nowSeconds);
        }
        else if (decision.Kind == PipelineDecisionKind.Ignored)
        {
            _stateCache.MarkIgnored(pendingText, nowSeconds);
        }
        else
        {
            _stateCache.MarkQueued(pendingText, nowSeconds);
        }
    }

    private void RefreshDrawContext(RuntimeConfig config)
    {
        _enableImguiForDraw = config.EnableImgui;
        _drawTargetLanguage = config.TargetLanguage;
        _drawPromptPolicyVersion = TextPipeline.GetPromptPolicyVersion(config);
        _drawSceneName = GetActiveSceneName();
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
