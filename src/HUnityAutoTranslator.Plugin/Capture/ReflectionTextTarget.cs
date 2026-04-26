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

    private static string BuildHierarchyPath(Transform transform)
    {
        var names = new Stack<string>();
        for (var current = transform; current != null; current = current.parent)
        {
            names.Push(current.name);
        }

        return string.Join("/", names);
    }
}
