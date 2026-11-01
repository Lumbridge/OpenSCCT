using System;
using System.Collections.Generic;
using Godot;

namespace OpenScctGame.Controls;

/// <summary>The UE2 input key names of the layouts (IK_ prefix removed) and the Godot keys and buttons they are.</summary>
public static class GodotKeys
{
    private static readonly Dictionary<string, Key> s_keys = Build();

    private static readonly Dictionary<string, MouseButton> s_mouse = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LeftMouse"] = MouseButton.Left,
        ["RightMouse"] = MouseButton.Right,
        ["MiddleMouse"] = MouseButton.Middle,
        ["MouseX1"] = MouseButton.Xbutton1,
        ["MouseX2"] = MouseButton.Xbutton2,
        ["MouseWheelUp"] = MouseButton.WheelUp,
        ["MouseWheelDown"] = MouseButton.WheelDown,
    };

    /// <summary>Readable presentation only; saved IK names remain unchanged.</summary>
    public static string DisplayName(string key)
    {
        if (key.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase)) return "Numpad " + key[6..].Replace("Period", ".", StringComparison.OrdinalIgnoreCase);
        return key.ToUpperInvariant() switch
        {
            "GREYSLASH" => "Numpad /", "GREYSTAR" => "Numpad *", "GREYPLUS" => "Numpad +", "GREYMINUS" => "Numpad -",
            "LEFTMOUSE" => "LMB", "RIGHTMOUSE" => "RMB", "MIDDLEMOUSE" => "MMB",
            "MOUSEX1" => "MB4", "MOUSEX2" => "MB5", "MOUSEWHEELUP" => "WH UP", "MOUSEWHEELDOWN" => "WH DN",
            "PAGEUP" => "PG UP", "PAGEDOWN" => "PG DN", "CAPSLOCK" => "CAPS", "NUMLOCK" => "NUM LK", "SCROLLLOCK" => "SCR LK",
            "UP" => "↑", "DOWN" => "↓", "LEFT" => "←", "RIGHT" => "→",
            "SPACE" => "Space", "CTRL" => "Ctrl", "TILDE" => "` / ~", "SINGLEQUOTE" => "'",
            "SEMICOLON" => ";", "EQUALS" => "=", "COMMA" => ",", "MINUS" => "-",
            "PERIOD" => ".", "SLASH" => "/", "BACKSLASH" => "\\",
            "LEFTBRACKET" => "[", "RIGHTBRACKET" => "]", "INSERT" => "INS", "DELETE" => "DEL", "BACKSPACE" => "BKSP", _ => key,
        };
    }

    private static Dictionary<string, Key> Build()
    {
        var map = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase);
        for (char c = 'A'; c <= 'Z'; c++)
        {
            map[c.ToString()] = Key.A + (c - 'A');
        }

        for (int d = 0; d <= 9; d++)
        {
            map[d.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Key.Key0 + d;
        }

        for (int f = 1; f <= 24; f++)
        {
            map["F" + f.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Key.F1 + (f - 1);
        }

        for (int d = 0; d <= 9; d++)
        {
            map["NumPad" + d.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Key.Kp0 + d;
        }

        map["Shift"] = Key.Shift;
        map["Ctrl"] = Key.Ctrl;
        map["Alt"] = Key.Alt;
        map["Space"] = Key.Space;
        map["Tab"] = Key.Tab;
        map["Escape"] = Key.Escape;
        map["Enter"] = Key.Enter;
        map["Backspace"] = Key.Backspace;
        map["PageUp"] = Key.Pageup;
        map["PageDown"] = Key.Pagedown;
        map["Home"] = Key.Home;
        map["End"] = Key.End;
        map["Insert"] = Key.Insert;
        map["Delete"] = Key.Delete;
        map["Up"] = Key.Up;
        map["Down"] = Key.Down;
        map["Left"] = Key.Left;
        map["Right"] = Key.Right;
        map["Tilde"] = Key.Quoteleft;
        map["CapsLock"] = Key.Capslock;
        map["Pause"] = Key.Pause;
        map["NumLock"] = Key.Numlock;
        map["ScrollLock"] = Key.Scrolllock;
        map["GreyStar"] = Key.KpMultiply;
        map["GreyPlus"] = Key.KpAdd;
        map["GreyMinus"] = Key.KpSubtract;
        map["GreySlash"] = Key.KpDivide;
        map["NumPadPeriod"] = Key.KpPeriod;
        map["Semicolon"] = Key.Semicolon;
        map["Equals"] = Key.Equal;
        map["Comma"] = Key.Comma;
        map["Minus"] = Key.Minus;
        map["Period"] = Key.Period;
        map["Slash"] = Key.Slash;
        map["LeftBracket"] = Key.Bracketleft;
        map["Backslash"] = Key.Backslash;
        map["RightBracket"] = Key.Bracketright;
        map["SingleQuote"] = Key.Apostrophe;
        return map;
    }

    /// <summary>The IK name of a key or mouse button event, or null.</summary>
    public static string? ToIk(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventKey key:
            {
                Key k = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
                foreach (KeyValuePair<string, Key> pair in s_keys)
                {
                    if (pair.Value == k)
                    {
                        return pair.Key;
                    }
                }

                return null;
            }

            case InputEventMouseButton button:
                foreach (KeyValuePair<string, MouseButton> pair in s_mouse)
                {
                    if (pair.Value == button.ButtonIndex)
                    {
                        return pair.Key;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    /// <summary>A Godot event for an IK name (for the InputMap), or null for axes and unknown names.</summary>
    public static InputEvent? ToEvent(string ik)
    {
        if (s_keys.TryGetValue(ik, out Key key))
        {
            return new InputEventKey { PhysicalKeycode = key };
        }

        if (s_mouse.TryGetValue(ik, out MouseButton button))
        {
            return new InputEventMouseButton { ButtonIndex = button };
        }

        return null;
    }
}
