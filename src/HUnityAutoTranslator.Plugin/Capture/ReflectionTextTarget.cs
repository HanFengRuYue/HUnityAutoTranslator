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
}
