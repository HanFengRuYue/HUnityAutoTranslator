using System.Reflection;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Runtime;
using HUnityAutoTranslator.Plugin.Unity;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Capture;

internal enum UnityTextTargetKind
{
    Ugui,
    Tmp
}

internal sealed class UnityTextTargetProcessor
{
    private readonly TextPipeline _pipeline;
    private readonly UnityMainThreadResultApplier _applier;
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly UnityTextFontReplacementService? _fontReplacement;
    private readonly Action<Action> _applyScope;
    private readonly UnityTextStabilityGate _stabilityGate;

    public UnityTextTargetProcessor(
        TextPipeline pipeline,
        UnityMainThreadResultApplier applier,
        Func<RuntimeConfig> configProvider,
        UnityTextFontReplacementService? fontReplacement = null,
        Action<Action>? applyScope = null,
        UnityTextStabilityGate? stabilityGate = null)
    {
        _pipeline = pipeline;
        _applier = applier;
        _configProvider = configProvider;
        _fontReplacement = fontReplacement;
        _applyScope = applyScope ?? RunUnsuppressed;
        _stabilityGate = stabilityGate ?? new UnityTextStabilityGate();
    }

    public void Process(UnityEngine.Object component, PropertyInfo textProperty, UnityTextTargetKind targetKind)
    {
        var target = new ReflectionTextTarget(component, textProperty);
        var text = target.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            RestoreFont(component, targetKind);
            return;
        }

        RunSuppressed(() =>
        {
            _applier.Register(target);
        });
        text = target.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            RestoreFont(component, targetKind);
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
                ApplyFont(component, targetKind, rememberedKey, context, text);
            }
            else
            {
                RestoreFont(component, targetKind);
            }

            return;
        }

        if (!ShouldProcessStableText(target, text, context, config))
        {
            RestoreFont(component, targetKind);
            return;
        }

        var decision = _pipeline.Process(new CapturedText(target.Id, text, target.IsVisible, context));
        if (decision.Kind == PipelineDecisionKind.UseCachedTranslation && decision.TranslatedText != null)
        {
            var applied = false;
            RunSuppressed(() => applied = _applier.RememberAndApply(target, text, decision.TranslatedText));
            if (applied)
            {
                ApplyFont(component, targetKind, key, context, decision.TranslatedText);
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
    }

    private void RunSuppressed(Action action)
    {
        _applyScope(action);
    }

    private static void RunUnsuppressed(Action action)
    {
        action();
    }

    private bool ShouldProcessStableText(
        ReflectionTextTarget target,
        string text,
        TranslationCacheContext context,
        RuntimeConfig config)
    {
        return _stabilityGate.ShouldProcess(
            new StableTextContext(
                target.Id,
                config.TargetLanguage,
                TextPipeline.GetPromptPolicyVersion(config),
                context.SceneName,
                context.ComponentHierarchy,
                context.ComponentType),
            text,
            Time.unscaledTime);
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
