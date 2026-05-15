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
    // Legacy (non-TMP) UI text component kinds this scanner handles: built-in
    // UnityEngine.UI.Text plus NGUI's UILabel (used by games built on the older
    // NGUI framework). Each entry pairs the component type with its string `text`
    // property so ReflectionTextTarget can read/write it generically.
    private readonly List<(Type Type, PropertyInfo Property)> _textKinds = new();
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
        TryAddTextKind(Type.GetType("UnityEngine.UI.Text, UnityEngine.UI"));
        TryAddTextKind(ResolveNguiLabelType());
        _enabled = _textKinds.Count > 0;
        if (!_enabled)
        {
            _logger.LogInfo("未找到 UGUI Text 类型，UGUI 捕获已关闭。");
            return;
        }

        _logger.LogInfo($"UGUI 捕获已启用，类型：{string.Join("、", _textKinds.Select(kind => kind.Type.FullName))}。");
    }

    private void TryAddTextKind(Type? type)
    {
        if (type == null)
        {
            return;
        }

        var property = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.PropertyType == typeof(string) && _textKinds.All(kind => kind.Type != type))
        {
            _textKinds.Add((type, property));
        }
    }

    // NGUI's UILabel has no namespace and is compiled into the game's own assembly,
    // so it can't be resolved by a fixed assembly-qualified name.
    private static Type? ResolveNguiLabelType()
    {
        var direct = Type.GetType("UILabel, Assembly-CSharp") ?? Type.GetType("UILabel, Assembly-CSharp-firstpass");
        if (direct != null)
        {
            return direct;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType("UILabel", throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    public int Tick(bool forceFullScan = false, int? maxTargetsOverride = null)
    {
        if (_textKinds.Count == 0)
        {
            return 0;
        }

        try
        {
            var objects = new List<UnityEngine.Object>();
            foreach (var kind in _textKinds)
            {
                objects.AddRange(UnityObjectFinder.FindObjects(kind.Type));
            }

            var configuredMaxTargets = _configProvider().MaxScanTargetsPerTick;
            var maxTargets = forceFullScan
                ? objects.Count
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

    // Each scanned component may be a UnityEngine.UI.Text or an NGUI UILabel; resolve
    // the matching string `text` property for whichever kind this component is.
    private PropertyInfo? ResolveTextProperty(UnityEngine.Object component)
    {
        foreach (var kind in _textKinds)
        {
            if (kind.Type.IsInstanceOfType(component))
            {
                return kind.Property;
            }
        }

        return null;
    }

    public void Dispose()
    {
    }

    private void Process(UnityEngine.Object component)
    {
        var textProperty = ResolveTextProperty(component);
        if (textProperty == null)
        {
            return;
        }

        var target = _targetRegistry.GetOrCreateTarget(component, textProperty);
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
        var textProperty = ResolveTextProperty(component);
        if (_changeQueue == null || textProperty == null)
        {
            return;
        }

        _changeQueue.RequeueForStability(
            new UnityTextChangeWorkItem(
                component,
                textProperty,
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
