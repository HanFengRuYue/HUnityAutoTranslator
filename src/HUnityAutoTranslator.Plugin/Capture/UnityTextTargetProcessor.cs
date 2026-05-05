using System.Reflection;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Runtime;
using HUnityAutoTranslator.Core.Text;
using HUnityAutoTranslator.Plugin.Unity;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Capture;

internal enum UnityTextTargetKind
{
    Ugui,
    Tmp
}

internal enum UnityTextProcessResult
{
    Completed,
    Ignored,
    WaitForStability
}

internal sealed class UnityTextTargetProcessor
{
    private readonly TextPipeline _pipeline;
    private readonly UnityMainThreadResultApplier _applier;
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly UnityTextFontReplacementService? _fontReplacement;
    private readonly Action<Action> _applyScope;
    private readonly UnityTextStabilityGate _stabilityGate;
    private readonly UnityTextTargetRegistry _targetRegistry;
    private readonly ControlPanelMetrics? _metrics;

    public UnityTextTargetProcessor(
        TextPipeline pipeline,
        UnityMainThreadResultApplier applier,
        Func<RuntimeConfig> configProvider,
        UnityTextFontReplacementService? fontReplacement = null,
        Action<Action>? applyScope = null,
        UnityTextStabilityGate? stabilityGate = null,
        UnityTextTargetRegistry? targetRegistry = null,
        ControlPanelMetrics? metrics = null)
    {
        _pipeline = pipeline;
        _applier = applier;
        _configProvider = configProvider;
        _fontReplacement = fontReplacement;
        _applyScope = applyScope ?? RunUnsuppressed;
        _stabilityGate = stabilityGate ?? new UnityTextStabilityGate();
        _targetRegistry = targetRegistry ?? new UnityTextTargetRegistry(metrics);
        _metrics = metrics;
    }

    public UnityTextProcessResult Process(
        UnityEngine.Object component,
        PropertyInfo textProperty,
        UnityTextTargetKind targetKind,
        string? observedText = null)
    {
        var config = _configProvider();
        if (observedText != null && ShouldSkipRawText(observedText, config))
        {
            _metrics?.RecordTextChangeRawPrefiltered();
            return UnityTextProcessResult.Ignored;
        }

        var target = _targetRegistry.GetOrCreateTarget(component, textProperty);
        var text = target.GetText();
        if (ShouldSkipRawText(text, config))
        {
            _metrics?.RecordTextChangeRawPrefiltered();
            return UnityTextProcessResult.Ignored;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            RestoreFont(component, targetKind);
            return UnityTextProcessResult.Ignored;
        }

        RunSuppressed(() =>
        {
            _applier.Register(target);
        });
        text = target.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            RestoreFont(component, targetKind);
            return UnityTextProcessResult.Ignored;
        }

        var context = new TranslationCacheContext(target.SceneName, target.HierarchyPath, target.ComponentType);
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
                ApplyFont(component, targetKind, rememberedKey, context, text);
                _applier.ApplyCurrentTextLayoutState(target);
            }
            else
            {
                RestoreFont(component, targetKind);
            }

            return UnityTextProcessResult.Completed;
        }

        if (TryApplyExactCachedTranslation(component, target, targetKind, text, context, key))
        {
            return UnityTextProcessResult.Completed;
        }

        var stableDecision = EvaluateStableText(target, text, context, config);
        if (stableDecision == StableTextDecisionKind.Wait)
        {
            RestoreFont(component, targetKind);
            return UnityTextProcessResult.WaitForStability;
        }

        var capturedText = new CapturedText(target.Id, text, target.IsVisible, context);
        var decision = stableDecision == StableTextDecisionKind.RefreshCachedTranslation
            ? _pipeline.ResolveCachedTranslationOnly(capturedText)
            : _pipeline.Process(capturedText);
        if (decision.Kind == PipelineDecisionKind.UseCachedTranslation && decision.TranslatedText != null)
        {
            var applied = false;
            RunSuppressed(() => applied = _applier.RememberAndApply(target, text, decision.TranslatedText));
            if (applied)
            {
                ApplyFont(component, targetKind, key, context, decision.TranslatedText);
                _applier.ApplyCurrentTextLayoutState(target);
            }
            else
            {
                RestoreFont(component, targetKind);
            }
        }
        else
        {
            RestoreFont(component, targetKind);
        }

        return UnityTextProcessResult.Completed;
    }

    private void RunSuppressed(Action action)
    {
        _applyScope(action);
    }

    private static void RunUnsuppressed(Action action)
    {
        action();
    }

    public static bool ShouldSkipRawText(string? text, RuntimeConfig config)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > config.MaxSourceTextLength)
        {
            return true;
        }

        return !TextFilter.ShouldTranslate(text) ||
            TextFilter.IsAlreadyTargetLanguageSource(text, config.TargetLanguage);
    }

    private bool TryApplyExactCachedTranslation(
        UnityEngine.Object component,
        ReflectionTextTarget target,
        UnityTextTargetKind targetKind,
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

        var applied = false;
        RunSuppressed(() => applied = _applier.RememberAndApply(target, text, decision.TranslatedText));
        if (applied)
        {
            ApplyFont(component, targetKind, key, context, decision.TranslatedText);
            _applier.ApplyCurrentTextLayoutState(target);
        }
        else
        {
            RestoreFont(component, targetKind);
        }

        return true;
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

    private void ApplyFont(
        UnityEngine.Object component,
        UnityTextTargetKind targetKind,
        TranslationCacheKey key,
        TranslationCacheContext context,
        string translatedText)
    {
        switch (targetKind)
        {
            case UnityTextTargetKind.Ugui:
                _fontReplacement?.ApplyToUgui(component, key, context, translatedText);
                break;
            case UnityTextTargetKind.Tmp:
                _fontReplacement?.ApplyToTmp(component, key, context, translatedText);
                break;
        }
    }

    private void RestoreFont(UnityEngine.Object component, UnityTextTargetKind targetKind)
    {
        switch (targetKind)
        {
            case UnityTextTargetKind.Ugui:
                _fontReplacement?.RestoreUgui(component);
                break;
            case UnityTextTargetKind.Tmp:
                _fontReplacement?.RestoreTmp(component);
                break;
        }
    }
}
