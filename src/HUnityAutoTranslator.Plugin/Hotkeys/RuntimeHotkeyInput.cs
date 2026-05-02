using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Hotkeys;

internal sealed class RuntimeHotkeyInput
{
    private const string KeyboardTypeName = "UnityEngine.InputSystem.Keyboard";
    private const string LegacyInputSystemOnlyMessage = "switched active Input handling to Input System";

    private readonly ManualLogSource _logger;
    private readonly NewInputSystemKeyboard? _newInputKeyboard;
    private bool _legacyInputDisabled;
    private bool _legacyInputWarningLogged;

    public RuntimeHotkeyInput(ManualLogSource logger)
    {
        _logger = logger;
        _newInputKeyboard = NewInputSystemKeyboard.TryCreate();
    }

    public bool GetKeyDown(KeyCode key)
    {
        if (_newInputKeyboard?.TryRead(key, "wasPressedThisFrame", out var pressed) == true)
        {
            return pressed;
        }

        return TryReadLegacyInput(() => Input.GetKeyDown(key), out pressed) && pressed;
    }

    public bool GetKey(KeyCode key)
    {
        if (_newInputKeyboard?.TryRead(key, "isPressed", out var pressed) == true)
        {
            return pressed;
        }

        return TryReadLegacyInput(() => Input.GetKey(key), out pressed) && pressed;
    }

    private bool TryReadLegacyInput(Func<bool> read, out bool pressed)
    {
        pressed = false;
        if (_legacyInputDisabled)
        {
            return false;
        }

        try
        {
            pressed = read();
            return true;
        }
        catch (InvalidOperationException ex) when (IsLegacyInputDisabledException(ex))
        {
            _legacyInputDisabled = true;
            LogLegacyInputDisabled(ex);
            return false;
        }
    }

    private void LogLegacyInputDisabled(InvalidOperationException ex)
    {
        if (_legacyInputWarningLogged)
        {
            return;
        }

        _legacyInputWarningLogged = true;
        _logger.LogWarning($"当前游戏禁用了 Unity 旧输入接口，插件已停止轮询旧输入热键以避免控制台刷屏：{ex.Message}");
    }

    private static bool IsLegacyInputDisabledException(Exception ex)
    {
        return ex.Message.IndexOf(LegacyInputSystemOnlyMessage, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private sealed class NewInputSystemKeyboard
    {
        private readonly Type _keyboardType;
        private readonly PropertyInfo _currentProperty;
        private readonly Dictionary<string, PropertyInfo?> _keyProperties = new(StringComparer.Ordinal);
        private readonly Dictionary<Type, Dictionary<string, PropertyInfo?>> _stateProperties = new();

        private NewInputSystemKeyboard(Type keyboardType, PropertyInfo currentProperty)
        {
            _keyboardType = keyboardType;
            _currentProperty = currentProperty;
        }

        public static NewInputSystemKeyboard? TryCreate()
        {
            var keyboardType = ResolveType(KeyboardTypeName);
            var currentProperty = keyboardType?.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
            return keyboardType == null || currentProperty == null
                ? null
                : new NewInputSystemKeyboard(keyboardType, currentProperty);
        }

        public bool TryRead(KeyCode key, string statePropertyName, out bool pressed)
        {
            pressed = false;
            var keyPropertyName = GetKeyPropertyName(key);
            if (keyPropertyName == null)
            {
                return false;
            }

            var keyboard = _currentProperty.GetValue(null, null);
            if (keyboard == null)
            {
                return true;
            }

            var keyProperty = GetKeyProperty(keyPropertyName);
            var keyControl = keyProperty?.GetValue(keyboard, null);
            if (keyControl == null)
            {
                return true;
            }

            var stateProperty = GetStateProperty(keyControl.GetType(), statePropertyName);
            if (stateProperty?.GetValue(keyControl, null) is bool value)
            {
                pressed = value;
                return true;
            }

            return false;
        }

        private PropertyInfo? GetKeyProperty(string name)
        {
            if (!_keyProperties.TryGetValue(name, out var property))
            {
                property = _keyboardType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                _keyProperties[name] = property;
            }

            return property;
        }

        private PropertyInfo? GetStateProperty(Type controlType, string name)
        {
            if (!_stateProperties.TryGetValue(controlType, out var byName))
            {
                byName = new Dictionary<string, PropertyInfo?>(StringComparer.Ordinal);
                _stateProperties[controlType] = byName;
            }

            if (!byName.TryGetValue(name, out var property))
            {
                property = controlType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                byName[name] = property;
            }

            return property;
        }

        private static Type? ResolveType(string fullName)
        {
            var type = Type.GetType(fullName + ", Unity.InputSystem", throwOnError: false);
            if (type != null)
            {
                return type;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static string? GetKeyPropertyName(KeyCode key)
        {
            if (key >= KeyCode.A && key <= KeyCode.Z)
            {
                var offset = (int)key - (int)KeyCode.A;
                return ((char)('a' + offset)).ToString() + "Key";
            }

            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                return "digit" + ((int)key - (int)KeyCode.Alpha0) + "Key";
            }

            if (key >= KeyCode.F1 && key <= KeyCode.F15)
            {
                return "f" + ((int)key - (int)KeyCode.F1 + 1) + "Key";
            }

            return key switch
            {
                KeyCode.Space => "spaceKey",
                KeyCode.Return => "enterKey",
                KeyCode.Tab => "tabKey",
                KeyCode.Backspace => "backspaceKey",
                KeyCode.Escape => "escapeKey",
                KeyCode.Insert => "insertKey",
                KeyCode.Delete => "deleteKey",
                KeyCode.Home => "homeKey",
                KeyCode.End => "endKey",
                KeyCode.PageUp => "pageUpKey",
                KeyCode.PageDown => "pageDownKey",
                KeyCode.UpArrow => "upArrowKey",
                KeyCode.DownArrow => "downArrowKey",
                KeyCode.LeftArrow => "leftArrowKey",
                KeyCode.RightArrow => "rightArrowKey",
                KeyCode.LeftControl => "leftCtrlKey",
                KeyCode.RightControl => "rightCtrlKey",
                KeyCode.LeftShift => "leftShiftKey",
                KeyCode.RightShift => "rightShiftKey",
                KeyCode.LeftAlt => "leftAltKey",
                KeyCode.RightAlt => "rightAltKey",
                _ => null
            };
        }
    }
}
