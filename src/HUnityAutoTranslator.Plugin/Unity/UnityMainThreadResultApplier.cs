using System.Reflection;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Runtime;

namespace HUnityAutoTranslator.Plugin.Unity;

internal sealed class UnityMainThreadResultApplier
{
    private const int MaxPendingComponentRefreshes = 512;
    private const int MaxFontSizeAdjustmentLogCount = 20;
    private const float TmpOverflowAutoShrinkFitRatio = 0.92f;
    private const float TmpOverflowAutoShrinkStepRatio = 0.9f;
    private const float TmpOverflowAutoShrinkMinimumScale = 0.45f;
    private const float TmpOverflowAutoShrinkMinimumDelta = 0.25f;
    private const float TmpOverflowHeightTolerance = 0.5f;

    private readonly Dictionary<string, IUnityTextTarget> _targets = new();
    private readonly TranslationWritebackTracker _writebacks = new();
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly Action<string>? _fontSizeAdjustmentLogger;
    private readonly Dictionary<string, float> _originalFontSizes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loggedFontSizeAdjustmentTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TranslationResult> _pendingComponentRefreshes = new(StringComparer.Ordinal);
    private readonly RoundRobinCursor _reapplyCursor = new();
    private int _fontSizeAdjustmentLogCount;
    private bool _useTranslatedText = true;
    private UnityTextFontReplacementService? _fontReplacement;

    public UnityMainThreadResultApplier()
        : this(RuntimeConfig.CreateDefault, fontSizeAdjustmentLogger: null)
    {
    }

    public UnityMainThreadResultApplier(Func<RuntimeConfig> configProvider, Action<string>? fontSizeAdjustmentLogger = null)
    {
        _configProvider = configProvider;
        _fontSizeAdjustmentLogger = fontSizeAdjustmentLogger;
    }

    public void SetFontReplacementService(UnityTextFontReplacementService? fontReplacement)
    {
        _fontReplacement = fontReplacement;
    }

    public void Register(IUnityTextTarget target)
    {
        _targets[target.Id] = target;
        RememberOriginalFontSize(target);
        ApplyCurrentFontSizeState(target);
        TryApplyPendingComponentRefresh(target);
    }

    public IReadOnlyList<TranslationHighlightTarget> SnapshotTargets()
    {
        var targets = new List<TranslationHighlightTarget>();
        foreach (var target in _targets.Values.ToArray())
        {
            if (!target.IsAlive)
            {
                ForgetTarget(target.Id);
                continue;
            }

            targets.Add(new TranslationHighlightTarget(
                target.Id,
                target.SceneName,
                target.HierarchyPath,
                target.ComponentType,
                IsAlive: true,
                target.IsVisible));
        }

        return targets;
    }

    public bool TryGetTarget(string targetId, out IUnityTextTarget target)
    {
        if (_targets.TryGetValue(targetId, out target!) && target.IsAlive)
        {
            return true;
        }

        if (target != null)
        {
            ForgetTarget(targetId);
        }

        target = null!;
        return false;
    }

    public bool RememberAndApply(IUnityTextTarget target, string sourceText, string translatedText)
    {
        Register(target);
        _writebacks.Remember(target.Id, sourceText, translatedText);
        return TryApplyRemembered(target);
    }

    public bool IsRememberedTranslation(string targetId, string? currentText)
    {
        return _writebacks.IsRememberedTranslation(targetId, currentText);
    }

    public bool TryGetRememberedSourceText(string targetId, string? currentText, out string sourceText)
    {
        return _writebacks.TryGetRememberedSourceText(targetId, currentText, out sourceText);
    }

    public int Apply(IReadOnlyList<TranslationResult> results)
    {
        var applied = 0;
        foreach (var result in results)
        {
            if (!TryFindTarget(result, out var target))
            {
                RememberPendingComponentRefresh(result);
                continue;
            }

            if (!ApplyResultToTarget(result, target))
            {
                RememberPendingComponentRefresh(result);
                continue;
            }

            applied++;
        }

        return applied;
    }

    public int ReapplyRemembered(int maxCount)
    {
        if (maxCount <= 0)
        {
            return 0;
        }

        var applied = 0;
        foreach (var target in _reapplyCursor.TakeFullRound(_targets.Values.ToArray()))
        {
            if (applied >= maxCount)
            {
                break;
            }

            if (!target.IsAlive)
            {
                ForgetTarget(target.Id);
                continue;
            }

            if (TryApplyRemembered(target))
            {
                applied++;
            }
            else
            {
                ApplyCurrentFontSizeState(target);
            }
        }

        return applied;
    }

    public int SetTranslatedTextMode(bool useTranslatedText, int maxCount)
    {
        _useTranslatedText = useTranslatedText;
        if (maxCount <= 0)
        {
            return 0;
        }

        var applied = 0;
        foreach (var target in _reapplyCursor.TakeFullRound(_targets.Values.ToArray()))
        {
            if (applied >= maxCount)
            {
                break;
            }

            if (!target.IsAlive)
            {
                ForgetTarget(target.Id);
                continue;
            }

            var currentText = target.GetText();
            if (_writebacks.TryGetDisplayText(target.Id, currentText, useTranslatedText, out var replacement))
            {
                if (useTranslatedText && _writebacks.IsRememberedSourceText(target.Id, currentText))
                {
                    CaptureTmpVisibleColorBeforeWriteback(target);
                }

                target.SetText(replacement);
                ApplyFontSizeState(target, translatedTextIsActive: useTranslatedText);
                applied++;
            }
            else
            {
                ApplyCurrentFontSizeState(target);
            }
        }

        return applied;
    }

    private bool TryFindTarget(TranslationResult result, out IUnityTextTarget target)
    {
        if (!string.IsNullOrEmpty(result.TargetId))
        {
            if (_targets.TryGetValue(result.TargetId, out var directTarget) && directTarget.IsAlive)
            {
                target = directTarget;
                return true;
            }

            ForgetTarget(result.TargetId);
        }

        return TryFindTargetByComponentContext(result, out target);
    }

    private bool TryFindTargetByComponentContext(TranslationResult result, out IUnityTextTarget target)
    {
        var best = _targets.Values
            .Where(target => target.IsAlive)
            .Where(target => ComponentContextMatches(result, target))
            .OrderByDescending(target => target.IsVisible)
            .FirstOrDefault();

        if (best != null)
        {
            target = best;
            return true;
        }

        target = null!;
        return false;
    }

    private bool ApplyResultToTarget(TranslationResult result, IUnityTextTarget target)
    {
        if (result.RestoreSourceText)
        {
            return ApplyRestoreSourceToTarget(result, target);
        }

        var currentText = target.GetText();
        if (!_writebacks.TryRememberForCurrentText(
            target.Id,
            currentText,
            result.SourceText,
            result.TranslatedText,
            result.PreviousTranslatedText))
        {
            return false;
        }

        ForgetPendingComponentRefresh(result);
        var appliedText = TryApplyRemembered(target);
        var appliedFont = ApplyFontForResult(result, target);
        if (appliedFont)
        {
            ApplyFontSizeState(target, translatedTextIsActive: _useTranslatedText);
        }

        return appliedText || appliedFont;
    }

    private bool ApplyRestoreSourceToTarget(TranslationResult result, IUnityTextTarget target)
    {
        var currentText = target.GetText();
        if (!_writebacks.TryRestoreSourceText(
            target.Id,
            currentText,
            result.SourceText,
            result.PreviousTranslatedText,
            out var replacement))
        {
            return false;
        }

        ForgetPendingComponentRefresh(result);
        if (!string.Equals(currentText, replacement, StringComparison.Ordinal))
        {
            target.SetText(replacement);
        }

        RestoreOriginalFontSize(target);
        _loggedFontSizeAdjustmentTargets.Remove(target.Id);
        return true;
    }

    private bool RememberPendingComponentRefresh(TranslationResult result)
    {
        if (!result.HasComponentContext)
        {
            return false;
        }

        var key = ComponentContextKey(result.SceneName, result.ComponentHierarchy, result.ComponentType);
        if (_pendingComponentRefreshes.TryGetValue(key, out var existing) && existing.UpdatedUtc > result.UpdatedUtc)
        {
            return true;
        }

        _pendingComponentRefreshes[key] = result;
        TrimPendingComponentRefreshes();
        return true;
    }

    private void TryApplyPendingComponentRefresh(IUnityTextTarget target)
    {
        var pending = _pendingComponentRefreshes.Values
            .Where(result => ComponentContextMatches(result, target))
            .OrderByDescending(result => result.UpdatedUtc)
            .ToArray();
        foreach (var result in pending)
        {
            if (ApplyResultToTarget(result, target))
            {
                break;
            }
        }
    }

    private void ForgetPendingComponentRefresh(TranslationResult result)
    {
        if (!result.HasComponentContext)
        {
            return;
        }

        _pendingComponentRefreshes.Remove(ComponentContextKey(result.SceneName, result.ComponentHierarchy, result.ComponentType));
    }

    private void TrimPendingComponentRefreshes()
    {
        var excess = _pendingComponentRefreshes.Count - MaxPendingComponentRefreshes;
        if (excess <= 0)
        {
            return;
        }

        foreach (var key in _pendingComponentRefreshes
            .OrderBy(item => item.Value.UpdatedUtc)
            .Take(excess)
            .Select(item => item.Key)
            .ToArray())
        {
            _pendingComponentRefreshes.Remove(key);
        }
    }

    private static bool ComponentContextMatches(TranslationResult result, IUnityTextTarget target)
    {
        if (!result.HasComponentContext)
        {
            return false;
        }

        var resultHierarchy = Normalize(result.ComponentHierarchy);
        if (!string.Equals(Normalize(target.HierarchyPath), resultHierarchy, StringComparison.Ordinal))
        {
            return false;
        }

        var resultScene = Normalize(result.SceneName);
        if (resultScene.Length > 0 && !string.Equals(Normalize(target.SceneName), resultScene, StringComparison.Ordinal))
        {
            return false;
        }

        return ComponentTypeMatches(result.ComponentType, target.ComponentType);
    }

    private static bool ComponentTypeMatches(string? expected, string? actual)
    {
        var normalizedExpected = Normalize(expected);
        if (normalizedExpected.Length == 0)
        {
            return true;
        }

        var normalizedActual = Normalize(actual);
        return string.Equals(normalizedActual, normalizedExpected, StringComparison.Ordinal) ||
            string.Equals(SimpleName(normalizedActual), SimpleName(normalizedExpected), StringComparison.Ordinal);
    }

    private static bool IsTmpTarget(string? componentType)
    {
        var value = Normalize(componentType);
        return value.StartsWith("TMPro.", StringComparison.Ordinal) ||
            value.Contains("TextMeshPro", StringComparison.Ordinal);
    }

    private static bool IsUguiTarget(string? componentType)
    {
        return string.Equals(Normalize(componentType), "UnityEngine.UI.Text", StringComparison.Ordinal);
    }

    private static string ComponentContextKey(string? sceneName, string? componentHierarchy, string? componentType)
    {
        return string.Join(
            "\u001f",
            Normalize(sceneName),
            Normalize(componentHierarchy),
            Normalize(componentType));
    }

    private static string SimpleName(string value)
    {
        var index = value.LastIndexOf('.');
        return index < 0 || index == value.Length - 1 ? value : value.Substring(index + 1);
    }

    private static string Normalize(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private bool TryApplyRemembered(IUnityTextTarget target)
    {
        var currentText = target.GetText();
        if (!_writebacks.TryGetDisplayText(target.Id, currentText, _useTranslatedText, out var replacement))
        {
            return false;
        }

        if (_useTranslatedText && _writebacks.IsRememberedSourceText(target.Id, currentText))
        {
            CaptureTmpVisibleColorBeforeWriteback(target);
        }

        target.SetText(replacement);
        ApplyFontSizeState(target, translatedTextIsActive: _useTranslatedText);
        return true;
    }

    private void CaptureTmpVisibleColorBeforeWriteback(IUnityTextTarget target)
    {
        if (_fontReplacement == null || !IsTmpTarget(target.ComponentType))
        {
            return;
        }

        _fontReplacement.CaptureTmpVisibleColor(target.Component);
    }

    private bool ApplyFontForResult(TranslationResult result, IUnityTextTarget target)
    {
        if (_fontReplacement == null ||
            result.RestoreSourceText ||
            string.IsNullOrWhiteSpace(result.SourceText) ||
            string.IsNullOrWhiteSpace(result.TranslatedText) ||
            !string.Equals(target.GetText(), result.TranslatedText, StringComparison.Ordinal))
        {
            return false;
        }

        var config = _configProvider();
        var targetLanguage = string.IsNullOrWhiteSpace(result.TargetLanguage)
            ? config.TargetLanguage
            : result.TargetLanguage!;
        var key = TranslationCacheKey.Create(
            result.SourceText,
            targetLanguage,
            config.Provider,
            TextPipeline.GetPromptPolicyVersion(config));
        var context = new TranslationCacheContext(
            result.SceneName ?? target.SceneName,
            result.ComponentHierarchy ?? target.HierarchyPath,
            result.ComponentType ?? target.ComponentType);

        if (IsTmpTarget(target.ComponentType))
        {
            _fontReplacement?.ApplyToTmp(target.Component, key, context, result.TranslatedText);
            return true;
        }

        if (IsUguiTarget(target.ComponentType))
        {
            _fontReplacement?.ApplyToUgui(target.Component, key, context, result.TranslatedText);
            return true;
        }

        return false;
    }

    private void RememberOriginalFontSize(IUnityTextTarget target)
    {
        if (_originalFontSizes.ContainsKey(target.Id))
        {
            return;
        }

        if (target.TryGetFontSize(out var fontSize) && fontSize > 0)
        {
            _originalFontSizes[target.Id] = fontSize;
        }
    }

    private void ApplyCurrentFontSizeState(IUnityTextTarget target)
    {
        var translatedTextIsActive = _useTranslatedText && _writebacks.IsRememberedTranslation(target.Id, target.GetText());
        ApplyFontSizeState(target, translatedTextIsActive);
    }

    private void ApplyFontSizeState(IUnityTextTarget target, bool translatedTextIsActive)
    {
        RememberOriginalFontSize(target);
        if (!_originalFontSizes.TryGetValue(target.Id, out var originalSize))
        {
            return;
        }

        var config = _configProvider();
        if (!translatedTextIsActive)
        {
            RestoreOriginalFontSize(target);
            return;
        }

        if (FontSizeAdjustment.IsEnabled(config.FontSizeAdjustmentMode, config.FontSizeAdjustmentValue))
        {
            var adjustedSize = FontSizeAdjustment.Calculate(
                originalSize,
                config.FontSizeAdjustmentMode,
                config.FontSizeAdjustmentValue);
            if (target.TrySetFontSize(adjustedSize))
            {
                LogFontSizeAdjustment(target, originalSize, adjustedSize, config);
            }
        }
        else
        {
            target.TrySetFontSize(originalSize);
        }

        if (config.EnableTmpOverflowAutoShrink)
        {
            TryAutoShrinkTmpOverflowingTranslatedText(target, originalSize);
        }
    }

    private bool TryAutoShrinkTmpOverflowingTranslatedText(IUnityTextTarget target, float originalSize)
    {
        if (!IsTmpTarget(target.ComponentType) ||
            target.Component == null ||
            !target.TryGetFontSize(out var currentSize) ||
            currentSize <= 0 ||
            !IsTmpTruncateOverflowMode(target.Component))
        {
            return false;
        }

        RefreshTmpLayout(target.Component);
        var isOverflowing = ReadTmpBool(target.Component, "isTextOverflowing") == true;
        var isTruncated = ReadTmpBool(target.Component, "isTextTruncated") == true;
        var hasPreferredHeight = ReadTmpFloat(target.Component, "preferredHeight", out var preferredHeight);
        var hasRectHeight = TryGetTmpRectHeight(target.Component, out var rectHeight);
        var heightOverflow = hasPreferredHeight &&
            hasRectHeight &&
            preferredHeight > rectHeight + TmpOverflowHeightTolerance;

        if (!isOverflowing && !isTruncated && !heightOverflow)
        {
            return false;
        }

        var shrinkRatio = heightOverflow && rectHeight > 0 && preferredHeight > 0
            ? Math.Min(TmpOverflowAutoShrinkStepRatio, (rectHeight * TmpOverflowAutoShrinkFitRatio) / preferredHeight)
            : TmpOverflowAutoShrinkStepRatio;
        if (shrinkRatio <= 0 || shrinkRatio >= 1)
        {
            shrinkRatio = TmpOverflowAutoShrinkStepRatio;
        }

        var minimumSize = Math.Max(1f, originalSize * TmpOverflowAutoShrinkMinimumScale);
        var shrunkSize = Math.Max(minimumSize, currentSize * shrinkRatio);
        if (currentSize - shrunkSize < TmpOverflowAutoShrinkMinimumDelta)
        {
            return false;
        }

        if (!target.TrySetFontSize(shrunkSize))
        {
            return false;
        }

        RefreshTmpLayout(target.Component);
        LogTmpOverflowFontSizeAdjustment(target, currentSize, shrunkSize);
        return true;
    }

    private static bool IsTmpTruncateOverflowMode(UnityEngine.Object component)
    {
        var overflowMode = ReadTmpValue(component, "overflowMode") ?? ReadTmpValue(component, "m_overflowMode");
        var modeName = overflowMode?.ToString();
        return !string.IsNullOrEmpty(modeName) &&
            modeName.IndexOf("Truncate", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool? ReadTmpBool(UnityEngine.Object component, string name)
    {
        var value = ReadTmpValue(component, name);
        return value is bool boolValue ? boolValue : null;
    }

    private static bool ReadTmpFloat(UnityEngine.Object component, string name, out float value)
    {
        value = 0;
        var rawValue = ReadTmpValue(component, name);
        switch (rawValue)
        {
            case float floatValue:
                value = floatValue;
                return value > 0;
            case double doubleValue:
                value = (float)doubleValue;
                return value > 0;
            case int intValue:
                value = intValue;
                return value > 0;
            default:
                return false;
        }
    }

    private static bool TryGetTmpRectHeight(UnityEngine.Object component, out float rectHeight)
    {
        rectHeight = 0f;
        var rectTransform = ReadTmpValue(component, "rectTransform") as UnityEngine.RectTransform;
        if (rectTransform == null && component is UnityEngine.Component unityComponent)
        {
            rectTransform = unityComponent.transform as UnityEngine.RectTransform;
        }

        if (rectTransform == null)
        {
            return false;
        }

        try
        {
            rectHeight = Math.Abs(rectTransform.rect.height);
            return rectHeight > 0;
        }
        catch
        {
            rectHeight = 0f;
            return false;
        }
    }

    private static object? ReadTmpValue(UnityEngine.Object component, string name)
    {
        var type = component.GetType();
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property is { CanRead: true })
        {
            try
            {
                return property.GetValue(component, null);
            }
            catch
            {
            }
        }

        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            return null;
        }

        try
        {
            return field.GetValue(component);
        }
        catch
        {
            return null;
        }
    }

    private static void RefreshTmpLayout(UnityEngine.Object component)
    {
        SetTmpValue(component, "havePropertiesChanged", true);
        SetTmpValue(component, "m_havePropertiesChanged", true);
        InvokeTmpMethodIfAvailable(component, "SetVerticesDirty");
        InvokeTmpMethodIfAvailable(component, "SetLayoutDirty");
        InvokeTmpMethodIfAvailable(component, "SetMaterialDirty");
        InvokeTmpMethodIfAvailable(component, "ForceMeshUpdate", preferTrueForBool: true);
    }

    private static void SetTmpValue(UnityEngine.Object component, string name, object value)
    {
        var type = component.GetType();
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property is { CanWrite: true })
        {
            try
            {
                property.SetValue(component, value, null);
                return;
            }
            catch
            {
            }
        }

        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            return;
        }

        try
        {
            field.SetValue(component, value);
        }
        catch
        {
        }
    }

    private static void InvokeTmpMethodIfAvailable(UnityEngine.Object component, string methodName, bool preferTrueForBool = false)
    {
        var methods = component
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(method => method.Name == methodName && !method.ContainsGenericParameters)
            .OrderBy(method => method.GetParameters().Length);
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var arguments = new object?[parameters.Length];
            var supported = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].HasDefaultValue)
                {
                    arguments[i] = parameters[i].DefaultValue;
                }
                else if (parameters[i].ParameterType == typeof(bool))
                {
                    arguments[i] = preferTrueForBool;
                }
                else
                {
                    supported = false;
                    break;
                }
            }

            if (!supported)
            {
                continue;
            }

            try
            {
                method.Invoke(component, arguments);
                return;
            }
            catch
            {
            }
        }
    }

    private void RestoreOriginalFontSize(IUnityTextTarget target)
    {
        if (_originalFontSizes.TryGetValue(target.Id, out var originalSize))
        {
            target.TrySetFontSize(originalSize);
        }
    }

    private void ForgetTarget(string targetId)
    {
        _targets.Remove(targetId);
        _writebacks.Forget(targetId);
        _originalFontSizes.Remove(targetId);
        _loggedFontSizeAdjustmentTargets.Remove(targetId);
    }

    private void LogFontSizeAdjustment(
        IUnityTextTarget target,
        float originalSize,
        float adjustedSize,
        RuntimeConfig config)
    {
        if (_fontSizeAdjustmentLogger == null ||
            _fontSizeAdjustmentLogCount >= MaxFontSizeAdjustmentLogCount ||
            !_loggedFontSizeAdjustmentTargets.Add(target.Id))
        {
            return;
        }

        _fontSizeAdjustmentLogCount++;
        var adjustmentDescription = FormatFontSizeAdjustment(config.FontSizeAdjustmentMode, config.FontSizeAdjustmentValue);
        _fontSizeAdjustmentLogger(
            $"已调整 {target.ComponentType} 的字号：{originalSize:0.##} -> {adjustedSize:0.##} " +
            $"（{adjustmentDescription}）。");
    }

    private static string FormatFontSizeAdjustment(FontSizeAdjustmentMode mode, double value)
    {
        return mode switch
        {
            FontSizeAdjustmentMode.Percent => $"百分比 {value:0.##}%",
            FontSizeAdjustmentMode.Points => $"点数 {value:0.##}",
            _ => $"关闭 {value:0.##}"
        };
    }

    private void LogTmpOverflowFontSizeAdjustment(IUnityTextTarget target, float originalSize, float adjustedSize)
    {
        if (_fontSizeAdjustmentLogger == null ||
            _fontSizeAdjustmentLogCount >= MaxFontSizeAdjustmentLogCount ||
            !_loggedFontSizeAdjustmentTargets.Add(target.Id))
        {
            return;
        }

        _fontSizeAdjustmentLogCount++;
        _fontSizeAdjustmentLogger(
            $"TMP 译文字号已自动缩小以避免裁切：{target.ComponentType} {originalSize:0.##} -> {adjustedSize:0.##}。");
    }
}
