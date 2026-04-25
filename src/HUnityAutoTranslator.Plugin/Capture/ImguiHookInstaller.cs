using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Pipeline;

namespace HUnityAutoTranslator.Plugin.Capture;

internal sealed class ImguiHookInstaller : ITextCaptureModule
{
    private const string HarmonyId = "com.hanfeng.hunityautotranslator.imgui";
    private static ImguiHookInstaller? _instance;

    private readonly TextPipeline _pipeline;
    private readonly ITranslationCache _cache;
    private readonly ManualLogSource _logger;
    private readonly RuntimeConfig _config;
    private Harmony? _harmony;
    private bool _enabled;
    private bool _warned;

    public ImguiHookInstaller(TextPipeline pipeline, ITranslationCache cache, ManualLogSource logger, RuntimeConfig config)
    {
        _pipeline = pipeline;
        _cache = cache;
        _logger = logger;
        _config = config;
    }

    public string Name => "IMGUI";

    public bool IsEnabled => _enabled && _config.EnableImgui;

    public void Start()
    {
        try
        {
            _instance = this;
            _harmony = new Harmony(HarmonyId);
            PatchStringTextMethods(typeof(UnityEngine.GUI));
            PatchStringTextMethods(typeof(UnityEngine.GUILayout));
            _enabled = true;
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
        var key = TranslationCacheKey.Create(text, _config.TargetLanguage, _config.Provider, TextPipeline.PromptPolicyVersion);
        if (_cache.TryGet(key, out var translated))
        {
            return translated;
        }

        _pipeline.Process(new CapturedText("imgui:" + key.Value, text, isVisible: true));
        return text;
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
