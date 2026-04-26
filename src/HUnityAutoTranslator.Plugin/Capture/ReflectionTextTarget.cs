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
