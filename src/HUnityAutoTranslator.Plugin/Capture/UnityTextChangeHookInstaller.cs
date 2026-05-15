using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using HUnityAutoTranslator.Core.Control;
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
    private readonly UnityTextChangeQueue _changeQueue;
    private readonly Action _requestGlobalTextScan;
    private readonly ControlPanelMetrics? _metrics;
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
        UnityTextStabilityGate? stabilityGate = null,
        Action? requestGlobalTextScan = null,
        ControlPanelMetrics? metrics = null,
        UnityTextChangeQueue? changeQueue = null)
    {
        _logger = logger;
        _configProvider = configProvider;
        _changeQueue = changeQueue ?? new UnityTextChangeQueue(metrics);
        _requestGlobalTextScan = requestGlobalTextScan ?? (() => { });
        _metrics = metrics;
    }

    public void Start()
    {
        try
        {
            _instance = this;
            _harmony = new Harmony(HarmonyId);
            _logger.LogInfo("正在安装 UGUI/TMP 即时文本变化捕获。");
            var patched = PatchUguiTextSetter() + PatchTmpTextEntryPoints();
            _ = PatchGameObjectSetActive();
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

    public bool IsEnabled => _enabled;

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

    private int PatchGameObjectSetActive()
    {
        var method = typeof(GameObject).GetMethod(
            nameof(GameObject.SetActive),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(bool) },
            modifiers: null);
        if (method == null || _harmony == null || !_patchedMethods.Add(method))
        {
            return 0;
        }

        try
        {
            _harmony.Patch(method, postfix: new HarmonyMethod(typeof(UnityTextChangeHookInstaller), nameof(PostfixGameObjectSetActive)));
            return 1;
        }
        catch (Exception ex)
        {
            WarnOnce("GameObject.SetActive activation hook failed: " + ex.Message);
            return 0;
        }
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

    private static void PostfixTextChanged(object __instance, object[] __args)
    {
        if (_instance == null ||
            !_instance._enabled ||
            _instance.IsSuppressed ||
            __instance is not UnityEngine.Object component)
        {
            return;
        }

        _instance._metrics?.RecordTextChangeHookEvent();
        if (!TryGetChangedText(__args, out var changedText))
        {
            changedText = null;
        }

        _instance.EnqueueChangedText(component, changedText);
    }

    private static void PostfixGameObjectSetActive(bool value)
    {
        if (_instance == null ||
            !_instance._enabled ||
            !value)
        {
            return;
        }

        _instance._requestGlobalTextScan();
    }

    private bool IsSuppressed => _suppressDepth > 0;

    private static bool TryGetChangedText(object[] args, out string? changedText)
    {
        changedText = null;
        foreach (var arg in args)
        {
            if (arg is string value)
            {
                changedText = value;
                return true;
            }
        }

        return false;
    }

    private void EnqueueChangedText(UnityEngine.Object component, string? changedText)
    {
        var config = _configProvider();
        if (!config.Enabled)
        {
            return;
        }

        if (changedText != null && UnityTextTargetProcessor.ShouldSkipRawText(changedText, config))
        {
            _metrics?.RecordTextChangeRawPrefiltered();
            return;
        }

        try
        {
            if (_uguiTextType != null &&
                _uguiTextProperty != null &&
                config.EnableUgui &&
                _uguiTextType.IsInstanceOfType(component))
            {
                _changeQueue.Enqueue(component, _uguiTextProperty, UnityTextTargetKind.Ugui, changedText);
                return;
            }

            if (_tmpTextType != null &&
                _tmpTextProperty != null &&
                config.EnableTmp &&
                _tmpTextType.IsInstanceOfType(component))
            {
                _changeQueue.Enqueue(component, _tmpTextProperty, UnityTextTargetKind.Tmp, changedText);
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
