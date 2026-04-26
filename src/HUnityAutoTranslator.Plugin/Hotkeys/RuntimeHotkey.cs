using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Hotkeys;

internal readonly struct RuntimeHotkey
{
    private readonly KeyCode _key;
    private readonly bool _ctrl;
    private readonly bool _shift;
    private readonly bool _alt;
    private readonly bool _disabled;

    private RuntimeHotkey(KeyCode key, bool ctrl, bool shift, bool alt, bool disabled)
    {
        _key = key;
        _ctrl = ctrl;
        _shift = shift;
        _alt = alt;
        _disabled = disabled;
    }

    public static bool TryParse(string? value, out RuntimeHotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('+').Select(part => part.Trim()).ToArray();
        if (parts.Any(part => part.Length == 0))
        {
            return false;
        }

        if (parts.Length == 1 && string.Equals(parts[0], "None", StringComparison.OrdinalIgnoreCase))
        {
            hotkey = new RuntimeHotkey(KeyCode.None, ctrl: false, shift: false, alt: false, disabled: true);
            return true;
        }

        var ctrl = false;
        var shift = false;
        var alt = false;
        KeyCode? key = null;
        foreach (var part in parts)
        {
            if (IsModifier(part, "Ctrl", "Control"))
            {
                if (ctrl)
                {
                    return false;
                }

                ctrl = true;
                continue;
            }

            if (IsModifier(part, "Shift"))
            {
                if (shift)
                {
                    return false;
                }

                shift = true;
                continue;
            }

            if (IsModifier(part, "Alt"))
            {
                if (alt)
                {
                    return false;
                }

                alt = true;
                continue;
            }

            if (key.HasValue || !TryParseKey(part, out var parsedKey))
            {
                return false;
            }

            key = parsedKey;
        }

        if (!key.HasValue || (!ctrl && !shift && !alt))
        {
            return false;
        }

        hotkey = new RuntimeHotkey(key.Value, ctrl, shift, alt, disabled: false);
        return true;
    }

    public bool IsPressed()
    {
        return !_disabled &&
            Input.GetKeyDown(_key) &&
            IsCtrlDown() == _ctrl &&
            IsShiftDown() == _shift &&
            IsAltDown() == _alt;
    }

    private static bool IsModifier(string value, params string[] aliases)
    {
        return aliases.Any(alias => string.Equals(value, alias, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseKey(string value, out KeyCode keyCode)
    {
        keyCode = KeyCode.None;
        var normalized = value.Trim();
        if (normalized.Length == 1 && char.IsDigit(normalized[0]))
        {
            normalized = "Alpha" + normalized;
        }
        else if (string.Equals(normalized, "Enter", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "Return";
        }

        if (!Enum.TryParse(normalized, ignoreCase: true, out keyCode))
        {
            return false;
        }

        return IsAllowedKey(keyCode);
    }

    private static bool IsAllowedKey(KeyCode keyCode)
    {
        return (keyCode >= KeyCode.A && keyCode <= KeyCode.Z) ||
            (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9) ||
            (keyCode >= KeyCode.F1 && keyCode <= KeyCode.F15) ||
            keyCode is KeyCode.Space or KeyCode.Return or KeyCode.Tab or KeyCode.Backspace or
                KeyCode.Escape or KeyCode.Insert or KeyCode.Delete or KeyCode.Home or KeyCode.End or
                KeyCode.PageUp or KeyCode.PageDown or KeyCode.UpArrow or KeyCode.DownArrow or
                KeyCode.LeftArrow or KeyCode.RightArrow;
    }

    private static bool IsCtrlDown()
    {
        return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    }

    private static bool IsShiftDown()
    {
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }

    private static bool IsAltDown()
    {
        return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
    }
}
