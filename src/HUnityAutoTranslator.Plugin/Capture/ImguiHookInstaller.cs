using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Plugin.Unity;
using UnityEngine.SceneManagement;

namespace HUnityAutoTranslator.Plugin.Capture;

internal sealed class ImguiHookInstaller : ITextCaptureModule
{
    private const string HarmonyId = "com.hanfeng.hunityautotranslator.imgui";
    private static ImguiHookInstaller? _instance;

    private readonly TextPipeline _pipeline;
    private readonly ITranslationCache _cache;
    private readonly ManualLogSource _logger;
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly UnityTextFontReplacementService? _fontReplacement;
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
            _logger.LogInfo("IMGUI capture enabled.");
        }
        catch (Exception ex)
        {
            _enabled = false;
            WarnOnce($"IMGUI hook installation failed; IMGUI capture disabled: {ex.Message}");
        }
    }

    public void Tick()
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
                WarnOnce($"Failed to patch {type.FullName}.{method.Name}: {ex.Message}");
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
        var key = TranslationCacheKey.Create(text, config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        var context = new TranslationCacheContext(GetActiveSceneName(), ComponentHierarchy: null, ComponentType: "IMGUI");
        _fontReplacement?.ApplyToImgui(key, context);
        if (_cache.TryGet(key, out var translated))
        {
            return translated;
        }

        _pipeline.Process(new CapturedText("imgui:" + key.SourceText, text, isVisible: true, context));
        return text;
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
