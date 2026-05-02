using System.Reflection;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Runtime;
using HUnityAutoTranslator.Plugin.Unity;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Capture;

internal sealed class TmpTextScanner : ITextCaptureModule
{
    private static readonly string[] CandidateTypeNames =
    {
        "TMPro.TMP_Text, Unity.TextMeshPro",
        "TMPro.TMP_Text, Assembly-CSharp",
        "TMPro.TMP_Text, Unity.TextMeshProModule"
    };

    private readonly ManualLogSource _logger;
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly UnityTextTargetProcessor _processor;
    private readonly RoundRobinCursor _scanCursor = new();
    private Type? _textType;
    private PropertyInfo? _textProperty;
    private bool _enabled;
    private bool _warned;

    public TmpTextScanner(TextPipeline pipeline, UnityMainThreadResultApplier applier, ManualLogSource logger, RuntimeConfig config)
        : this(pipeline, applier, logger, () => config, fontReplacement: null)
    {
    }

    public TmpTextScanner(
        TextPipeline pipeline,
        UnityMainThreadResultApplier applier,
        ManualLogSource logger,
        Func<RuntimeConfig> configProvider,
        UnityTextFontReplacementService? fontReplacement = null)
    {
        _logger = logger;
        _configProvider = configProvider;
        _processor = new UnityTextTargetProcessor(pipeline, applier, configProvider, fontReplacement);
    }

    public string Name => "TextMeshPro";

    public bool IsEnabled => _enabled && _configProvider().EnableTmp;

    public void Start()
    {
        foreach (var candidate in CandidateTypeNames)
        {
            _textType = Type.GetType(candidate);
            if (_textType != null)
            {
                break;
            }
        }

        _textProperty = _textType?.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
        _enabled = _textType != null && _textProperty != null;
        if (!_enabled)
        {
            _logger.LogInfo("未找到 TextMeshPro 类型，TMP 捕获已关闭。");
            return;
        }

        _logger.LogInfo($"TMP 捕获已启用，类型：{_textType!.FullName}。");
    }

    public void Tick(bool forceFullScan = false)
    {
        if (_textType == null || _textProperty == null)
        {
            return;
        }

        try
        {
            var objects = UnityObjectFinder.FindObjects(_textType);
            var maxTargets = forceFullScan ? objects.Length : _configProvider().MaxScanTargetsPerTick;
            foreach (var component in _scanCursor.TakeWindow(objects, maxTargets))
            {
                Process(component);
            }
        }
        catch (Exception ex)
        {
            _enabled = false;
            WarnOnce($"TMP 扫描失败，TMP 捕获已关闭：{ex.Message}");
        }
    }

    public void Dispose()
    {
    }

    private void Process(UnityEngine.Object component)
    {
        _processor.Process(component, _textProperty!, UnityTextTargetKind.Tmp);
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
