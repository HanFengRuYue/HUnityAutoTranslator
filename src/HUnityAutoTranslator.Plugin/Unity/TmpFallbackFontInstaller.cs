using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Unity;

internal static class TmpFallbackFontInstaller
{
    private static readonly string[] CandidateFontNames =
    {
        "Noto Sans SC",
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "SimSun",
        "SimHei",
        "DengXian",
        "Arial Unicode MS",
        "Noto Sans CJK SC"
    };

    private static readonly string[] CandidateFontFiles =
    {
        @"C:\Windows\Fonts\NotoSansSC-VF.ttf",
        @"C:\Windows\Fonts\msyh.ttc",
        @"C:\Windows\Fonts\simhei.ttf",
        @"C:\Windows\Fonts\simsun.ttc"
    };

    public static void TryInstall(ManualLogSource logger)
    {
        try
        {
            var settingsType = Type.GetType("TMPro.TMP_Settings, Unity.TextMeshPro");
            var fontAssetType = Type.GetType("TMPro.TMP_FontAsset, Unity.TextMeshPro");
            if (settingsType == null || fontAssetType == null)
            {
                return;
            }

            var fallbackProperty = settingsType.GetProperty("fallbackFontAssets", BindingFlags.Public | BindingFlags.Static);
            if (fallbackProperty?.GetValue(null, null) is not IList fallbacks)
            {
                logger.LogWarning("TMP fallback font installation skipped because TMP_Settings.fallbackFontAssets was not available.");
                return;
            }

            string? lastError = null;
            foreach (var osFont in CreateOsFontCandidates())
            {
                var fontAsset = CreateFontAsset(fontAssetType, osFont, out lastError);
                if (fontAsset == null)
                {
                    continue;
                }

                EnableDynamicAtlas(fontAsset);
                if (!fallbacks.Contains(fontAsset))
                {
                    fallbacks.Add(fontAsset);
                }

                logger.LogInfo($"TMP fallback font installed from OS font: {osFont.name}.");
                return;
            }

            logger.LogWarning($"TMP fallback font installation skipped because no CJK fallback font asset could be created. Last error: {lastError ?? "none"}");
        }
        catch (Exception ex)
        {
            logger.LogWarning($"TMP fallback font installation failed: {ex.Message}");
        }
    }

    private static IEnumerable<Font> CreateOsFontCandidates()
    {
        foreach (var fontName in CandidateFontNames)
        {
            Font? dynamicFont = null;
            try
            {
                dynamicFont = Font.CreateDynamicFontFromOSFont(fontName, 90);
            }
            catch
            {
            }

            if (dynamicFont != null)
            {
                yield return dynamicFont;
            }

            Font? namedFont = null;
            try
            {
                namedFont = new Font(fontName);
            }
            catch
            {
            }

            if (namedFont != null)
            {
                yield return namedFont;
            }
        }

        foreach (var fontFile in CandidateFontFiles)
        {
            if (!File.Exists(fontFile))
            {
                continue;
            }

            Font? fileFont = null;
            try
            {
                fileFont = new Font(fontFile);
            }
            catch
            {
            }

            if (fileFont != null)
            {
                yield return fileFont;
            }
        }
    }

    private static object? CreateFontAsset(Type fontAssetType, Font osFont, out string? lastError)
    {
        lastError = null;
        var methods = fontAssetType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "CreateFontAsset")
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length > 0 && parameters[0].ParameterType == typeof(Font);
            })
            .OrderByDescending(method => method.GetParameters().Length);

        foreach (var method in methods)
        {
            try
            {
                return method.Invoke(null, BuildCreateFontAssetArguments(method, osFont));
            }
            catch (Exception ex)
            {
                lastError = ex.InnerException?.Message ?? ex.Message;
            }
        }

        return null;
    }

    private static object?[] BuildCreateFontAssetArguments(MethodInfo method, Font osFont)
    {
        var parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (i == 0)
            {
                arguments[i] = osFont;
                continue;
            }

            if (parameter.HasDefaultValue)
            {
                arguments[i] = parameter.DefaultValue;
                continue;
            }

            arguments[i] = CreateFallbackArgument(parameter);
        }

        return arguments;
    }

    private static object? CreateFallbackArgument(ParameterInfo parameter)
    {
        if (parameter.ParameterType == typeof(int))
        {
            return parameter.Name switch
            {
                "samplingPointSize" => 90,
                "atlasPadding" => 9,
                "atlasWidth" => 4096,
                "atlasHeight" => 4096,
                _ => 0
            };
        }

        if (parameter.ParameterType == typeof(bool))
        {
            return true;
        }

        if (parameter.ParameterType.IsEnum)
        {
            var preferred = parameter.ParameterType.Name.Contains("AtlasPopulationMode", StringComparison.Ordinal)
                ? "Dynamic"
                : "SDFAA";
            return Enum.Parse(parameter.ParameterType, preferred);
        }

        return parameter.ParameterType.IsValueType
            ? Activator.CreateInstance(parameter.ParameterType)
            : null;
    }

    private static void EnableDynamicAtlas(object fontAsset)
    {
        SetProperty(fontAsset, "atlasPopulationMode", "Dynamic");
        SetProperty(fontAsset, "isMultiAtlasTexturesEnabled", true);
    }

    private static void SetProperty(object instance, string propertyName, object value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null || !property.CanWrite)
        {
            return;
        }

        if (property.PropertyType.IsEnum && value is string enumName)
        {
            property.SetValue(instance, Enum.Parse(property.PropertyType, enumName), null);
            return;
        }

        property.SetValue(instance, value, null);
    }
}
