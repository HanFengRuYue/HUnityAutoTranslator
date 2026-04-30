using System.Reflection;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Caching;
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

    private readonly TextPipeline _pipeline;
    private readonly UnityMainThreadResultApplier _applier;
    private readonly ManualLogSource _logger;
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly UnityTextFontReplacementService? _fontReplacement;
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
        _pipeline = pipeline;
        _applier = applier;
        _logger = logger;
        _configProvider = configProvider;
        _fontReplacement = fontReplacement;
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
        var target = new ReflectionTextTarget(component, _textProperty!);
        var text = target.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            _fontReplacement?.RestoreTmp(component);
            return;
        }

        _applier.Register(target);
        text = target.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            _fontReplacement?.RestoreTmp(component);
            return;
        }

        var context = new TranslationCacheContext(target.SceneName, target.HierarchyPath, target.ComponentType);
        var config = _configProvider();
        var key = TranslationCacheKey.Create(text, config.TargetLanguage, config.Provider, TextPipeline.GetPromptPolicyVersion(config));
        if (_applier.IsRememberedTranslation(target.Id, text))
        {
            if (_applier.TryGetRememberedSourceText(target.Id, text, out var sourceText))
            {
                var rememberedKey = TranslationCacheKey.Create(
                    sourceText,
                    config.TargetLanguage,
                    config.Provider,
                    TextPipeline.GetPromptPolicyVersion(config));
                _fontReplacement?.ApplyToTmp(component, rememberedKey, context);
            }
            else
            {
                _fontReplacement?.RestoreTmp(component);
            }

            return;
        }

        var decision = _pipeline.Process(new CapturedText(target.Id, text, target.IsVisible, context));
        if (decision.Kind == PipelineDecisionKind.UseCachedTranslation && decision.TranslatedText != null)
        {
            if (_applier.RememberAndApply(target, text, decision.TranslatedText))
            {
                _fontReplacement?.ApplyToTmp(component, key, context);
            }
            else
            {
                _fontReplacement?.RestoreTmp(component);
            }
        }
        else
        {
            _fontReplacement?.RestoreTmp(component);
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
