using System.Collections;
using System.Reflection;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Capture;

internal static class UnityObjectFinder
{
#if HUNITY_IL2CPP
    private static readonly MethodInfo? FindObjectsOfTypeGeneric = typeof(UnityEngine.Object)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .FirstOrDefault(method =>
            method.Name == nameof(UnityEngine.Object.FindObjectsOfType) &&
            method.IsGenericMethodDefinition &&
            method.GetParameters().Length == 0);
#endif

    public static UnityEngine.Object[] FindObjects(Type type)
    {
#if HUNITY_IL2CPP
        if (FindObjectsOfTypeGeneric == null)
        {
            return Array.Empty<UnityEngine.Object>();
        }

        var result = FindObjectsOfTypeGeneric.MakeGenericMethod(type).Invoke(null, null);
        return ToObjectArray(result);
#else
        return UnityEngine.Object.FindObjectsOfType(type);
#endif
    }

#if HUNITY_IL2CPP
    private static UnityEngine.Object[] ToObjectArray(object? result)
    {
        if (result == null)
        {
            return Array.Empty<UnityEngine.Object>();
        }

        if (result is UnityEngine.Object[] objects)
        {
            return objects;
        }

        if (result is IEnumerable enumerable)
        {
            return enumerable
                .OfType<UnityEngine.Object>()
                .ToArray();
        }

        return Array.Empty<UnityEngine.Object>();
    }
#endif
}
