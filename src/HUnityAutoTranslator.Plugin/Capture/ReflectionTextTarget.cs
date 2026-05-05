using System.Reflection;
using HUnityAutoTranslator.Plugin.Unity;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Capture;

internal sealed class ReflectionTextTarget : IUnityTextTarget
{
    private readonly UnityEngine.Object _component;
    private readonly PropertyInfo _textProperty;

    public ReflectionTextTarget(UnityEngine.Object component, PropertyInfo textProperty)
    {
        _component = component;
        _textProperty = textProperty;
        Id = component.GetInstanceID().ToString();
    }

    public string Id { get; }

    public UnityEngine.Object Component => _component;

    public bool IsAlive => _component != null;

    public bool IsVisible
    {
        get
        {
            try
            {
                var component = _component as Component;
                return component == null || component.gameObject.activeInHierarchy;
            }
            catch
            {
                return true;
            }
        }
    }

    public string? SceneName
    {
        get
        {
            try
            {
                var component = _component as Component;
                return component?.gameObject.scene.name;
            }
            catch
            {
                return null;
            }
        }
    }

    public string? HierarchyPath
    {
        get
        {
            try
            {
                var component = _component as Component;
                return component == null ? null : BuildHierarchyPath(component.transform);
            }
            catch
            {
                return null;
            }
        }
    }

    public string ComponentType => _component.GetType().FullName ?? _component.GetType().Name;

    public string? GetText()
    {
        try
        {
            return _textProperty.GetValue(_component, null) as string;
        }
        catch
        {
            return null;
        }
    }

    public void SetText(string value)
    {
        _textProperty.SetValue(_component, value, null);
    }

    public bool TryGetFontSize(out float fontSize)
    {
        fontSize = 0;
        var property = FindFontSizeProperty();
        if (property == null || !property.CanRead)
        {
            return false;
        }

        try
        {
            var value = property.GetValue(_component, null);
            switch (value)
            {
                case int intValue:
                    fontSize = intValue;
                    return fontSize > 0;
                case float floatValue:
                    fontSize = floatValue;
                    return fontSize > 0;
                case double doubleValue:
                    fontSize = (float)doubleValue;
                    return fontSize > 0;
                default:
                    return false;
            }
        }
        catch
        {
            fontSize = 0;
            return false;
        }
    }

    public bool TrySetFontSize(float fontSize)
    {
        if (fontSize <= 0)
        {
            return false;
        }

        var property = FindFontSizeProperty();
        if (property == null || !property.CanWrite)
        {
            return false;
        }

        try
        {
            object value;
            if (property.PropertyType == typeof(int))
            {
                value = Math.Max(1, (int)Math.Round(fontSize));
            }
            else if (property.PropertyType == typeof(float))
            {
                value = fontSize;
            }
            else if (property.PropertyType == typeof(double))
            {
                value = (double)fontSize;
            }
            else
            {
                return false;
            }

            property.SetValue(_component, value, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryGetLineSpacing(out float lineSpacing)
    {
        return TryGetFloatMember(_component, "lineSpacing", out lineSpacing);
    }

    public bool TrySetLineSpacing(float lineSpacing)
    {
        return lineSpacing > 0 && TrySetFloatMember(_component, "lineSpacing", lineSpacing);
    }

    public bool TryGetFontLineHeight(out float lineHeight)
    {
        lineHeight = 0;
        var font =
            GetMemberValue(_component, "font") ??
            GetMemberValue(_component, "fontAsset") ??
            GetMemberValue(_component, "m_fontAsset");
        return TryReadFontLineHeight(font, out lineHeight);
    }

    public bool TryGetPreferredHeight(out float preferredHeight)
    {
        return TryGetFloatMember(_component, "preferredHeight", out preferredHeight);
    }

    public bool TryGetRenderedHeight(out float renderedHeight)
    {
        if (TryGetFloatMember(_component, "renderedHeight", out renderedHeight))
        {
            return true;
        }

        foreach (var method in _component.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(method => method.Name == "GetRenderedValues" && !method.ContainsGenericParameters)
            .OrderBy(method => method.GetParameters().Length))
        {
            var parameters = method.GetParameters();
            if (parameters.Length > 1 || parameters.Any(parameter => parameter.ParameterType != typeof(bool)))
            {
                continue;
            }

            try
            {
                var result = parameters.Length == 0
                    ? method.Invoke(_component, null)
                    : method.Invoke(_component, new object[] { true });
                if (TryReadVectorHeight(result, out renderedHeight))
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        renderedHeight = 0;
        return false;
    }

    public bool TryGetRectHeight(out float rectHeight)
    {
        rectHeight = 0;
        try
        {
            var rectTransform = GetMemberValue(_component, "rectTransform") as RectTransform;
            if (rectTransform == null && _component is Component component)
            {
                rectTransform = component.transform as RectTransform;
            }

            if (rectTransform == null)
            {
                return false;
            }

            rectHeight = Math.Abs(rectTransform.rect.height);
            return rectHeight > 0;
        }
        catch
        {
            rectHeight = 0;
            return false;
        }
    }

    public bool TryGetScreenRect(out Rect screenRect)
    {
        screenRect = default;
        if (_component is not Component component)
        {
            return false;
        }

        try
        {
            if (component.transform is RectTransform rectTransform && TryGetRectTransformScreenRect(component, rectTransform, out screenRect))
            {
                return true;
            }

            return TryGetRendererScreenRect(component, out screenRect);
        }
        catch
        {
            screenRect = default;
            return false;
        }
    }

    private PropertyInfo? FindFontSizeProperty()
    {
        return _component.GetType().GetProperty("fontSize", BindingFlags.Instance | BindingFlags.Public);
    }

    private static bool TryReadFontLineHeight(object? font, out float lineHeight)
    {
        lineHeight = 0;
        if (font == null)
        {
            return false;
        }

        if (font is Font unityFont)
        {
            lineHeight = unityFont.lineHeight;
            return lineHeight > 0;
        }

        if (TryGetFloatMember(font, "lineHeight", out lineHeight) && lineHeight > 0)
        {
            return true;
        }

        var faceInfo =
            GetMemberValue(font, "faceInfo") ??
            GetMemberValue(font, "m_FaceInfo") ??
            GetMemberValue(font, "m_faceInfo");
        return TryGetFloatMember(faceInfo, "lineHeight", out lineHeight) && lineHeight > 0;
    }

    private static bool TryGetFloatMember(object? instance, string memberName, out float value)
    {
        value = 0;
        var rawValue = GetMemberValue(instance, memberName);
        return TryConvertToFloat(rawValue, out value);
    }

    private static bool TrySetFloatMember(object instance, string memberName, float value)
    {
        var type = instance.GetType();
        var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (property is { CanWrite: true } &&
            TryConvertFromFloat(value, property.PropertyType, out var propertyValue))
        {
            try
            {
                property.SetValue(instance, propertyValue, null);
                return true;
            }
            catch
            {
            }
        }

        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && TryConvertFromFloat(value, field.FieldType, out var fieldValue))
        {
            try
            {
                field.SetValue(instance, fieldValue);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static object? GetMemberValue(object? instance, string memberName)
    {
        if (instance == null)
        {
            return null;
        }

        var type = instance.GetType();
        var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (property is { CanRead: true })
        {
            try
            {
                return property.GetValue(instance, null);
            }
            catch
            {
            }
        }

        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
        {
            return null;
        }

        try
        {
            return field.GetValue(instance);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadVectorHeight(object? value, out float height)
    {
        height = 0;
        if (value is Vector2 vector)
        {
            height = vector.y;
            return height > 0;
        }

        return TryGetFloatMember(value, "y", out height) && height > 0;
    }

    private static bool TryConvertToFloat(object? value, out float result)
    {
        switch (value)
        {
            case float floatValue:
                result = floatValue;
                return result > 0 && !float.IsNaN(result) && !float.IsInfinity(result);
            case int intValue:
                result = intValue;
                return result > 0;
            case double doubleValue:
                result = (float)doubleValue;
                return result > 0 && !float.IsNaN(result) && !float.IsInfinity(result);
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryConvertFromFloat(float value, Type targetType, out object converted)
    {
        converted = null!;
        if (targetType == typeof(float))
        {
            converted = value;
            return true;
        }

        if (targetType == typeof(double))
        {
            converted = (double)value;
            return true;
        }

        if (targetType == typeof(int))
        {
            converted = Math.Max(1, (int)Math.Round(value));
            return true;
        }

        return false;
    }

    private static string BuildHierarchyPath(Transform transform)
    {
        var names = new Stack<string>();
        for (var current = transform; current != null; current = current.parent)
        {
            names.Push(current.name);
        }

        return string.Join("/", names);
    }

    private static bool TryGetRectTransformScreenRect(Component component, RectTransform rectTransform, out Rect screenRect)
    {
        var camera = ResolveCanvasCamera(component);
        var corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);
        return TryBuildScreenRect(corners.Select(corner => RectTransformUtility.WorldToScreenPoint(camera, corner)), out screenRect);
    }

    private static bool TryGetRendererScreenRect(Component component, out Rect screenRect)
    {
        screenRect = default;
        var camera = Camera.main;
        var renderer = component.GetComponent<Renderer>();
        if (camera == null || renderer == null)
        {
            return false;
        }

        var bounds = renderer.bounds;
        var min = bounds.min;
        var max = bounds.max;
        var corners = new[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(max.x, max.y, max.z)
        };

        var screenPoints = new List<Vector2>(corners.Length);
        foreach (var corner in corners)
        {
            var point = camera.WorldToScreenPoint(corner);
            if (point.z < 0f)
            {
                continue;
            }

            screenPoints.Add(point);
        }

        return TryBuildScreenRect(screenPoints, out screenRect);
    }

    private static Camera? ResolveCanvasCamera(Component component)
    {
        var canvas = component.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
    }

    private static bool TryBuildScreenRect(IEnumerable<Vector2> screenPoints, out Rect screenRect)
    {
        screenRect = default;
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        var count = 0;

        foreach (var point in screenPoints)
        {
            minX = Math.Min(minX, point.x);
            minY = Math.Min(minY, point.y);
            maxX = Math.Max(maxX, point.x);
            maxY = Math.Max(maxY, point.y);
            count++;
        }

        if (count == 0 || maxX - minX <= 1f || maxY - minY <= 1f)
        {
            return false;
        }

        const float padding = 6f;
        screenRect = new Rect(
            minX - padding,
            Screen.height - maxY - padding,
            maxX - minX + padding * 2f,
            maxY - minY + padding * 2f);
        return true;
    }
}
