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

    private static readonly MethodInfo? FindObjectsOfTypeAllGeneric = typeof(Resources)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .FirstOrDefault(method =>
            method.Name == nameof(Resources.FindObjectsOfTypeAll) &&
            method.IsGenericMethodDefinition &&
            method.GetParameters().Length == 0);

    private static readonly object ClosedMethodGate = new();
    private static readonly Dictionary<Type, MethodInfo> ClosedFindObjectsMethods = new();
    private static readonly Dictionary<Type, MethodInfo> ClosedFindObjectsAllMethods = new();
#endif

    public static UnityEngine.Object[] FindObjects(Type type)
    {
#if HUNITY_IL2CPP
        var closed = GetClosedMethod(ClosedFindObjectsMethods, FindObjectsOfTypeGeneric, type);
        var fast = closed == null ? Array.Empty<UnityEngine.Object>() : ToObjectArray(closed.Invoke(null, null));
#else
        var fast = UnityEngine.Object.FindObjectsOfType(type);
#endif
        if (fast.Length > 0)
        {
            return fast;
        }

        // FindObjectsOfType returns nothing on some Unity Mono runtimes (observed on
        // Unity 2019.4) even when active components of the type exist in the scene.
        // FindObjectsOfTypeAll resolves them reliably across runtimes; filter it down
        // to active real scene instances so this matches FindObjectsOfType semantics.
        return FilterActiveSceneComponents(FindAllObjects(type));
    }

    private static UnityEngine.Object[] FilterActiveSceneComponents(UnityEngine.Object[] candidates)
    {
        if (candidates.Length == 0)
        {
            return candidates;
        }

        var result = new List<UnityEngine.Object>(candidates.Length);
        foreach (var candidate in candidates)
        {
            if (candidate is not Component component || component == null)
            {
                continue;
            }

            var gameObject = component.gameObject;
            if (gameObject == null)
            {
                continue;
            }

            // Engine-internal objects returned by FindObjectsOfTypeAll.
            if ((gameObject.hideFlags & (HideFlags.HideAndDontSave | HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor)) != 0)
            {
                continue;
            }

            // Prefab/asset objects loaded in memory have an invalid scene; real scene
            // instances (including DontDestroyOnLoad) have a valid one.
            if (!gameObject.scene.IsValid())
            {
                continue;
            }

            // Inactive scene objects are covered separately by InactiveTextPrefetchScanner.
            if (!gameObject.activeInHierarchy)
            {
                continue;
            }

            result.Add(candidate);
        }

        return result.Count == 0 ? Array.Empty<UnityEngine.Object>() : result.ToArray();
    }

    // Unlike FindObjects, this also returns inactive scene objects (used to pre-translate
    // hidden UI panels). It additionally returns prefab/asset objects, so callers must filter
    // results down to real scene instances.
    public static UnityEngine.Object[] FindAllObjects(Type type)
    {
#if HUNITY_IL2CPP
        var closed = GetClosedMethod(ClosedFindObjectsAllMethods, FindObjectsOfTypeAllGeneric, type);
        if (closed != null)
        {
            return ToObjectArray(closed.Invoke(null, null));
        }

        var nonGeneric = typeof(Resources).GetMethod(
            nameof(Resources.FindObjectsOfTypeAll),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(Type) },
            modifiers: null);
        if (nonGeneric == null)
        {
            return Array.Empty<UnityEngine.Object>();
        }

        return ToObjectArray(nonGeneric.Invoke(null, new object[] { type }));
#else
        return Resources.FindObjectsOfTypeAll(type);
#endif
    }

#if HUNITY_IL2CPP
    private static MethodInfo? GetClosedMethod(
        Dictionary<Type, MethodInfo> cache,
        MethodInfo? genericDefinition,
        Type type)
    {
        if (genericDefinition == null)
        {
            return null;
        }

        lock (ClosedMethodGate)
        {
            if (!cache.TryGetValue(type, out var closed))
            {
                closed = genericDefinition.MakeGenericMethod(type);
                cache[type] = closed;
            }

            return closed;
        }
    }

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
