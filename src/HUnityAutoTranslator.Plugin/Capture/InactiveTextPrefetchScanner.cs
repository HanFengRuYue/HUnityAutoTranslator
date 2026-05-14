using System.Diagnostics;
using System.Reflection;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Runtime;
using HUnityAutoTranslator.Plugin.Unity;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Capture;

// Discovers UGUI/TMP text on INACTIVE scene objects (settings panels, pause menus, dialogs
// authored hidden) and pre-translates it so it is cached/applied before the panel is opened.
// The regular scanners only see active objects, so statically-authored hidden text would
// otherwise not be translated until the panel is activated.
internal sealed class InactiveTextPrefetchScanner : ITextCaptureModule
{
    private const float PeriodicDeepScanSeconds = 30f;
    private const int DeepScanTargetsPerTick = 48;

    private static readonly string[] TmpCandidateTypeNames =
    {
        "TMPro.TMP_Text, Unity.TextMeshPro",
        "TMPro.TMP_Text, Assembly-CSharp",
        "TMPro.TMP_Text, Unity.TextMeshProModule"
    };

    private readonly TextPipeline _pipeline;
    private readonly UnityMainThreadResultApplier _applier;
    private readonly ManualLogSource _logger;
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly UnityTextTargetRegistry _targetRegistry;
    private readonly ControlPanelMetrics? _metrics;
    private readonly RoundRobinCursor _uguiCursor = new();
    private readonly RoundRobinCursor _tmpCursor = new();
    private Type? _uguiTextType;
    private PropertyInfo? _uguiTextProperty;
    private Type? _tmpTextType;
    private PropertyInfo? _tmpTextProperty;
    private bool _enabled;
    private bool _warned;
    private float _nextDeepScanTime;
    private bool _deepScanRequested = true;

    public InactiveTextPrefetchScanner(
        TextPipeline pipeline,
        UnityMainThreadResultApplier applier,
        ManualLogSource logger,
        Func<RuntimeConfig> configProvider,
        UnityTextTargetRegistry targetRegistry,
        ControlPanelMetrics? metrics = null)
    {
        _pipeline = pipeline;
        _applier = applier;
        _logger = logger;
        _configProvider = configProvider;
        _targetRegistry = targetRegistry;
        _metrics = metrics;
    }

    public string Name => "InactivePrefetch";

    public bool IsEnabled
    {
        get
        {
            if (!_enabled)
            {
                return false;
            }

            var config = _configProvider();
            return config.PreTranslateInactiveText && (config.EnableUgui || config.EnableTmp);
        }
    }

    public bool UsesGlobalObjectScan => true;

    public void Start()
    {
        _uguiTextType = Type.GetType("UnityEngine.UI.Text, UnityEngine.UI");
        _uguiTextProperty = _uguiTextType?.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);

        foreach (var candidate in TmpCandidateTypeNames)
        {
            _tmpTextType = Type.GetType(candidate);
            if (_tmpTextType != null)
            {
                break;
            }
        }

        _tmpTextProperty = _tmpTextType?.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);

        var uguiReady = _uguiTextType != null && _uguiTextProperty != null;
        var tmpReady = _tmpTextType != null && _tmpTextProperty != null;
        _enabled = uguiReady || tmpReady;
        _logger.LogInfo(_enabled
            ? "未激活 UI 文本预翻译已启用。"
            : "未找到 UGUI/TMP 文本类型，未激活 UI 文本预翻译已关闭。");
    }

    // Called on scene load so a freshly loaded scene's hidden panels are covered immediately
    // instead of waiting for the periodic cadence.
    public void RequestDeepScan()
    {
        _deepScanRequested = true;
    }

    public int Tick(bool forceFullScan = false, int? maxTargetsOverride = null)
    {
        if (!_enabled)
        {
            return 0;
        }

        var now = Time.unscaledTime;
        if (!forceFullScan && !_deepScanRequested && now < _nextDeepScanTime)
        {
            return 0;
        }

        _deepScanRequested = false;
        _nextDeepScanTime = now + PeriodicDeepScanSeconds;

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var config = _configProvider();
            var processed = 0;
            if (config.EnableUgui && _uguiTextType != null && _uguiTextProperty != null)
            {
                processed += ScanType(_uguiTextType, _uguiTextProperty, _uguiCursor, forceFullScan);
            }

            if (config.EnableTmp && _tmpTextType != null && _tmpTextProperty != null)
            {
                processed += ScanType(_tmpTextType, _tmpTextProperty, _tmpCursor, forceFullScan);
            }

            stopwatch.Stop();
            _metrics?.RecordGlobalTextScan(stopwatch.Elapsed, processed);
            return processed;
        }
        catch (Exception ex)
        {
            WarnOnce($"未激活 UI 文本预翻译扫描失败，稍后会重试：{ex.Message}");
        }

        return 0;
    }

    public void Dispose()
    {
    }

    private int ScanType(Type textType, PropertyInfo textProperty, RoundRobinCursor cursor, bool forceFullScan)
    {
        var objects = UnityObjectFinder.FindAllObjects(textType);
        var inactive = new List<UnityEngine.Object>();
        foreach (var candidate in objects)
        {
            if (IsRealInactiveSceneComponent(candidate))
            {
                inactive.Add(candidate);
            }
        }

        if (inactive.Count == 0)
        {
            return 0;
        }

        var maxTargets = forceFullScan ? inactive.Count : DeepScanTargetsPerTick;
        var processed = 0;
        foreach (var component in cursor.TakeWindow(inactive, maxTargets))
        {
            Process(component, textProperty);
            processed++;
        }

        return processed;
    }

    private void Process(UnityEngine.Object component, PropertyInfo textProperty)
    {
        var target = _targetRegistry.GetOrCreateTarget(component, textProperty);
        var text = target.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Register the id so a completed translation can be written back by target id even
        // while the component is still inactive.
        _applier.Register(target);
        text = target.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Already translated by an earlier pass; it will re-apply when the panel is shown.
        if (_applier.IsRememberedTranslation(target.Id, text))
        {
            return;
        }

        var context = new TranslationCacheContext(target.SceneName, target.HierarchyPath, target.ComponentType);

        // Static authored text is stable by definition, so the stability gate is intentionally
        // skipped. AllowInvisiblePrefetch lets it past the IgnoreInvisibleText gate and queues
        // it at the lowest priority so visible UI is never starved.
        var capturedText = new CapturedText(
            target.Id,
            text,
            target.IsVisible,
            context,
            publishResult: true,
            allowInvisiblePrefetch: true);
        var decision = _pipeline.Process(capturedText);
        if (decision.Kind == PipelineDecisionKind.UseCachedTranslation && decision.TranslatedText != null)
        {
            _applier.RememberAndApply(target, text, decision.TranslatedText);
        }
    }

    // Resources.FindObjectsOfTypeAll also returns prefab/asset objects and engine-internal
    // objects; keep only real instantiated scene objects that are currently hidden.
    private static bool IsRealInactiveSceneComponent(UnityEngine.Object candidate)
    {
        if (candidate is not Component component || component == null)
        {
            return false;
        }

        var gameObject = component.gameObject;
        if (gameObject == null)
        {
            return false;
        }

        if ((gameObject.hideFlags & (HideFlags.HideAndDontSave | HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor)) != 0)
        {
            return false;
        }

        // A prefab asset loaded in memory has an invalid scene; a real scene instance (including
        // DontDestroyOnLoad objects) has a valid one.
        if (!gameObject.scene.IsValid())
        {
            return false;
        }

        // Active components are already handled by the regular UGUI/TMP scanners.
        return component is not Behaviour behaviour || !behaviour.isActiveAndEnabled;
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
