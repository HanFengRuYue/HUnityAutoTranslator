using System.Reflection;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Runtime;
using HUnityAutoTranslator.Plugin.Unity;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Capture;

internal sealed class UguiTextScanner : ITextCaptureModule
{
    private const float FastStaticTextRetrySeconds = 0.1f;

    private readonly TextPipeline _pipeline;
    private readonly UnityMainThreadResultApplier _applier;
    private readonly ManualLogSource _logger;
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly UnityTextFontReplacementService? _fontReplacement;
    private readonly UnityTextStabilityGate _stabilityGate;
    private readonly UnityTextTargetRegistry _targetRegistry;
    private readonly UnityTextChangeQueue? _changeQueue;
    private readonly RoundRobinCursor _scanCursor = new();
    private Type? _textType;
    private PropertyInfo? _textProperty;
    private bool _enabled;
    private bool _warned;

    public UguiTextScanner(TextPipeline pipeline, UnityMainThreadResultApplier applier, ManualLogSource logger, RuntimeConfig config)
        : this(pipeline, applier, logger, () => config, fontReplacement: null, stabilityGate: null)
    {
    }

    public UguiTextScanner(
        TextPipeline pipeline,
        UnityMainThreadResultApplier applier,
        ManualLogSource logger,
        Func<RuntimeConfig> configProvider,
        UnityTextFontReplacementService? fontReplacement = null,
        UnityTextStabilityGate? stabilityGate = null,
        UnityTextTargetRegistry? targetRegistry = null,
        UnityTextChangeQueue? changeQueue = null)
    {
        _pipeline = pipeline;
        _applier = applier;
        _logger = logger;
        _configProvider = configProvider;
        _fontReplacement = fontReplacement;
        _stabilityGate = stabilityGate ?? new UnityTextStabilityGate();
        _targetRegistry = targetRegistry ?? new UnityTextTargetRegistry();
        _changeQueue = changeQueue;
    }

    public string Name => "UGUI";

    public bool IsEnabled => _enabled && _configProvider().EnableUgui;

    public bool UsesGlobalObjectScan => true;

    public void Start()
    {
        _textType = Type.GetType("UnityEngine.UI.Text, UnityEngine.UI");
        _textProperty = _textType?.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
        _enabled = _textType != null && _textProperty != null;
        if (!_enabled)
        {
            _logger.LogInfo("未找到 UGUI Text 类型，UGUI 捕获已关闭。");
            return;
        }

        _logger.LogInfo("UGUI 捕获已启用。");
    }

    public int Tick(bool forceFullScan = false, int? maxTargetsOverride = null)
    {
        if (_textType == null || _textProperty == null)
        {
            return 0;
        }

        try
        {
            var objects = UnityObjectFinder.FindObjects(_textType);
            var configuredMaxTargets = _configProvider().MaxScanTargetsPerTick;
            var maxTargets = forceFullScan
                ? objects.Length
                : Math.Min(configuredMaxTargets, maxTargetsOverride ?? configuredMaxTargets);
            var processed = 0;
            foreach (var component in _scanCursor.TakeWindow(objects, maxTargets))
            {
                Process(component);
                processed++;
            }

            return processed;
        }
        catch (Exception ex)
        {
            WarnOnce($"UGUI 扫描失败，稍后会重试：{ex.Message}");
        }

        return 0;
    }

    public void Dispose()
    {
    }

    private void Process(UnityEngine.Object component)
    {
        var target = _targetRegistry.GetOrCreateTarget(component, _textProperty!);
        var text = target.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            _fontReplacement?.RestoreUgui(component);
            return;
        }

        _applier.Register(target);
        text = target.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            _fontReplacement?.RestoreUgui(component);
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
                _fontReplacement?.ApplyToUgui(component, rememberedKey, context, text);
                _applier.ApplyCurrentTextLayoutState(target);
            }
            else
            {
                _fontReplacement?.RestoreUgui(component);
            }

            return;
        }

        if (TryApplyExactCachedTranslation(component, target, text, context, key))
        {
            return;
        }

        var stableDecision = EvaluateStableText(target, text, context, config);
        if (stableDecision == StableTextDecisionKind.Wait)
        {
            _fontReplacement?.RestoreUgui(component);
            QueueStabilityRetry(component, text);
            return;
        }

        var capturedText = new CapturedText(target.Id, text, target.IsVisible, context);
        var decision = stableDecision == StableTextDecisionKind.RefreshCachedTranslation
            ? _pipeline.ResolveCachedTranslationOnly(capturedText)
            : _pipeline.Process(capturedText);
        if (decision.Kind == PipelineDecisionKind.UseCachedTranslation && decision.TranslatedText != null)
        {
            if (_applier.RememberAndApply(target, text, decision.TranslatedText))
            {
                _fontReplacement?.ApplyToUgui(component, key, context, decision.TranslatedText);
                _applier.ApplyCurrentTextLayoutState(target);
            }
            else
            {
                _fontReplacement?.RestoreUgui(component);
            }
        }
        else
        {
            _fontReplacement?.RestoreUgui(component);
        }
    }

    private bool TryApplyExactCachedTranslation(
        UnityEngine.Object component,
        ReflectionTextTarget target,
        string text,
        TranslationCacheContext context,
        TranslationCacheKey key)
    {
        if (!target.IsVisible)
        {
            return false;
        }

        var decision = _pipeline.ResolveExactCachedTranslation(new CapturedText(target.Id, text, isVisible: true, context));
        if (decision.Kind != PipelineDecisionKind.UseCachedTranslation || decision.TranslatedText == null)
        {
            return false;
        }

        if (_applier.RememberAndApply(target, text, decision.TranslatedText))
        {
            _fontReplacement?.ApplyToUgui(component, key, context, decision.TranslatedText);
            _applier.ApplyCurrentTextLayoutState(target);
        }
        else
        {
            _fontReplacement?.RestoreUgui(component);
        }

        return true;
    }

    private void QueueStabilityRetry(UnityEngine.Object component, string text)
    {
        if (_changeQueue == null || _textProperty == null)
        {
            return;
        }

        _changeQueue.RequeueForStability(
            new UnityTextChangeWorkItem(
                component,
                _textProperty,
                UnityTextTargetKind.Ugui,
                text,
            Time.unscaledTime),
            Time.unscaledTime + FastStaticTextRetrySeconds);
    }

    private StableTextDecisionKind EvaluateStableText(
        ReflectionTextTarget target,
        string text,
        TranslationCacheContext context,
        RuntimeConfig config)
    {
        return _stabilityGate.Evaluate(
            new StableTextContext(
                target.Id,
                config.TargetLanguage,
                TextPipeline.GetPromptPolicyVersion(config),
                context.SceneName,
                context.ComponentHierarchy,
                context.ComponentType),
            text,
            Time.unscaledTime,
            preferFastStaticRelease: true);
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
