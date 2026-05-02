using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Runtime;
using HUnityAutoTranslator.Plugin.Unity;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Capture;

internal sealed class UnityTextChangeHookInstaller : IDisposable
{
    private const string HarmonyId = "com.hanfeng.hunityautotranslator.textchange";
    private static readonly string[] TmpCandidateTypeNames =
    {
        "TMPro.TMP_Text, Unity.TextMeshPro",
        "TMPro.TMP_Text, Assembly-CSharp",
        "TMPro.TMP_Text, Unity.TextMeshProModule"
    };
    private static UnityTextChangeHookInstaller? _instance;

    private readonly ManualLogSource _logger;
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly UnityTextTargetProcessor _processor;
    private readonly HashSet<MethodBase> _patchedMethods = new();
    private Harmony? _harmony;
    private Type? _uguiTextType;
    private PropertyInfo? _uguiTextProperty;
    private Type? _tmpTextType;
    private PropertyInfo? _tmpTextProperty;
    private int _suppressDepth;
    private bool _enabled;
    private bool _warned;

    public UnityTextChangeHookInstaller(
        TextPipeline pipeline,
        UnityMainThreadResultApplier applier,
        ManualLogSource logger,
        Func<RuntimeConfig> configProvider,
        UnityTextFontReplacementService? fontReplacement = null,
        UnityTextStabilityGate? stabilityGate = null)
    {
        _logger = logger;
        _configProvider = configProvider;
        _processor = new UnityTextTargetProcessor(pipeline, applier, configProvider, fontReplacement, RunSuppressed, stabilityGate);
    }

    public void Start()
    {
        try
        {
            _instance = this;
            _harmony = new Harmony(HarmonyId);
            _logger.LogInfo("正在安装 UGUI/TMP 即时文本变化捕获。");
            var patched = 0;
            patched += PatchUguiTextSetter();
            patched += PatchTmpTextEntryPoints();
            _enabled = patched > 0;
            if (_enabled)
            {
                _logger.LogInfo($"UGUI/TMP 即时文本变化捕获已启用，已安装 {patched} 个入口。");
            }
            else
            {
                _logger.LogInfo("未找到可安装的 UGUI/TMP 即时文本变化入口，将继续使用周期扫描。");
            }
        }
        catch (Exception ex)
        {
            _enabled = false;
            WarnOnce($"UGUI/TMP 即时文本变化捕获安装失败，将继续使用周期扫描：{ex.Message}");
        }
    }

    private int PatchUguiTextSetter()
    {
        _uguiTextType = Type.GetType("UnityEngine.UI.Text, UnityEngine.UI");
        _uguiTextProperty = _uguiTextType?.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
        var setter = _uguiTextProperty?.GetSetMethod();
        return PatchTextMethod(setter);
    }

    private int PatchTmpTextEntryPoints()
    {
        foreach (var candidate in TmpCandidateTypeNames)
        {
            _tmpTextType = Type.GetType(candidate);
            if (_tmpTextType != null)
            {
                break;
            }
        }

        _tmpTextProperty = _tmpTextType?.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
        var patched = PatchTextMethod(_tmpTextProperty?.GetSetMethod());
        if (_tmpTextType == null)
        {
            return patched;
        }

        foreach (var method in AccessTools.GetDeclaredMethods(_tmpTextType))
        {
            if (!IsTmpSetTextMethod(method))
            {
                continue;
            }

            patched += PatchTextMethod(method);
        }

        return patched;
    }

    private int PatchTextMethod(MethodBase? method)
    {
        if (method == null || _harmony == null || !_patchedMethods.Add(method))
        {
            return 0;
        }

        try
        {
            _harmony.Patch(method, postfix: new HarmonyMethod(typeof(UnityTextChangeHookInstaller), nameof(PostfixTextChanged)));
            return 1;
        }
        catch (Exception ex)
        {
            WarnOnce($"安装即时文本变化入口失败：{method.DeclaringType?.FullName}.{method.Name}：{ex.Message}");
            return 0;
        }
    }

    private static bool IsTmpSetTextMethod(MethodInfo method)
    {
        if (method.Name != "SetText" || method.ReturnType != typeof(void))
        {
            return false;
        }

        var parameters = method.GetParameters();
        return parameters.Length > 0 && parameters[0].ParameterType == typeof(string);
    }

    private static void PostfixTextChanged(object __instance)
    {
        if (_instance == null ||
            !_instance._enabled ||
            _instance.IsSuppressed ||
            __instance is not UnityEngine.Object component)
        {
            return;
        }

        _instance.ProcessChangedText(component);
    }

    private bool IsSuppressed => _suppressDepth > 0;

    private void ProcessChangedText(UnityEngine.Object component)
    {
        var config = _configProvider();
        if (!config.Enabled)
        {
            return;
        }

        try
        {
            if (_uguiTextType != null &&
                _uguiTextProperty != null &&
                config.EnableUgui &&
                _uguiTextType.IsInstanceOfType(component))
            {
                _processor.Process(component, _uguiTextProperty, UnityTextTargetKind.Ugui);
                return;
            }

            if (_tmpTextType != null &&
                _tmpTextProperty != null &&
                config.EnableTmp &&
                _tmpTextType.IsInstanceOfType(component))
            {
                _processor.Process(component, _tmpTextProperty, UnityTextTargetKind.Tmp);
            }
        }
        catch (Exception ex)
        {
            WarnOnce($"即时文本变化处理失败，将由周期扫描兜底：{ex.Message}");
        }
    }

    public void RunSuppressed(Action action)
    {
        _suppressDepth++;
        try
        {
            action();
        }
        finally
        {
            _suppressDepth--;
        }
    }

    public void Dispose()
    {
        _harmony?.UnpatchSelf();
        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
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
