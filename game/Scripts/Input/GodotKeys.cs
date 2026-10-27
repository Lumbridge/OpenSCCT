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
        ["MouseWheelUp"] = MouseButton.WheelUp,
        ["MouseWheelDown"] = MouseButton.WheelDown,
    };

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

        for (int f = 1; f <= 12; f++)
        {
            map["F" + f.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Key.F1 + (f - 1);
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
