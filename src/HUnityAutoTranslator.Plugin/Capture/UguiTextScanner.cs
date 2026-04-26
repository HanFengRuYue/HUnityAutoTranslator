using System.Reflection;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Plugin.Unity;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Capture;

internal sealed class UguiTextScanner : ITextCaptureModule
{
    private readonly TextPipeline _pipeline;
    private readonly UnityMainThreadResultApplier _applier;
    private readonly ManualLogSource _logger;
    private readonly Func<RuntimeConfig> _configProvider;
    private Type? _textType;
    private PropertyInfo? _textProperty;
    private bool _enabled;
    private bool _warned;

    public UguiTextScanner(TextPipeline pipeline, UnityMainThreadResultApplier applier, ManualLogSource logger, RuntimeConfig config)
        : this(pipeline, applier, logger, () => config)
    {
    }

    public UguiTextScanner(TextPipeline pipeline, UnityMainThreadResultApplier applier, ManualLogSource logger, Func<RuntimeConfig> configProvider)
    {
        _pipeline = pipeline;
        _applier = applier;
        _logger = logger;
        _configProvider = configProvider;
    }

    public string Name => "UGUI";

    public bool IsEnabled => _enabled && _configProvider().EnableUgui;

    public void Start()
    {
        _textType = Type.GetType("UnityEngine.UI.Text, UnityEngine.UI");
        _textProperty = _textType?.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
        _enabled = _textType != null && _textProperty != null;
        if (!_enabled)
        {
            _logger.LogInfo("UGUI Text type not found; UGUI capture disabled.");
            return;
        }

        _logger.LogInfo("UGUI capture enabled.");
    }

    public void Tick()
    {
        if (_textType == null || _textProperty == null)
        {
            return;
        }

        try
        {
            var objects = UnityEngine.Object.FindObjectsOfType(_textType);
            var count = Math.Min(objects.Length, _configProvider().MaxScanTargetsPerTick);
            for (var i = 0; i < count; i++)
            {
                Process(objects[i]);
            }
        }
        catch (Exception ex)
        {
            WarnOnce($"UGUI scan failed and will retry later: {ex.Message}");
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
            return;
        }

        _applier.Register(target);
        if (_applier.IsRememberedTranslation(target.Id, text))
        {
            return;
        }

        var context = new TranslationCacheContext(target.SceneName, target.HierarchyPath, target.ComponentType);
        var decision = _pipeline.Process(new CapturedText(target.Id, text, target.IsVisible, context));
        if (decision.Kind == PipelineDecisionKind.UseCachedTranslation && decision.TranslatedText != null)
        {
            _applier.RememberAndApply(target, text, decision.TranslatedText);
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
