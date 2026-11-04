using System;
using System.Collections.Generic;

namespace OpenScctGame.Controls;

[Flags]
public enum BindingModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

/// <summary>A physical input and its optional modifiers. Existing plain IK names remain valid.</summary>
public readonly record struct BindingKey(string Base, BindingModifiers Modifiers = BindingModifiers.None)
{
    public bool IsPulse => Base.StartsWith("MouseWheel", StringComparison.OrdinalIgnoreCase);
    public bool IsModifier => ModifierOf(Base) != BindingModifiers.None;
    public string Canonical => Format(static key => key, "+");
    public string Format(Func<string, string> display, string separator = " + ")
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(BindingModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(BindingModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(BindingModifiers.Alt)) parts.Add("Alt");
        parts.Add(display(Base));
        return string.Join(separator, parts);
    }

    public static BindingModifiers ModifierOf(string key) => key.ToUpperInvariant() switch
    {
        "CTRL" => BindingModifiers.Ctrl, "SHIFT" => BindingModifiers.Shift,
        "ALT" => BindingModifiers.Alt, _ => BindingModifiers.None,
    };

    public static BindingKey Parse(string text) => TryParse(text, out var key)
        ? key : throw new FormatException("Invalid binding input: " + text);

    public static bool TryParse(string? text, out BindingKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] parts = text.Split('+', StringSplitOptions.TrimEntries);
        string input = parts[^1];
        if (input.Length == 0 || input.IndexOfAny([' ', '\r', '\n', '=']) >= 0) return false;
        BindingModifiers modifiers = BindingModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            BindingModifiers modifier = ModifierOf(parts[i]);
            if (modifier == BindingModifiers.None || (modifiers & modifier) != 0) return false;
            modifiers |= modifier;
        }
        if (parts.Length > 1 && ModifierOf(input) != BindingModifiers.None) return false;
        if (ModifierOf(input) is var own && own != BindingModifiers.None) input = own.ToString();
        key = new BindingKey(input, modifiers);
        return true;
    }

    /// <summary>Only an exact modifier match overrides the ordinary input; extra modifiers fall back to plain.</summary>
    public static string? Select(KeyLayout layout, string input, BindingModifiers modifiers)
    {
        if (ModifierOf(input) != BindingModifiers.None) modifiers = BindingModifiers.None;
        string candidate = new BindingKey(input, modifiers).Canonical;
        if (layout.Bindings.ContainsKey(candidate)) return candidate;
        return layout.Bindings.ContainsKey(input) ? input : null;
    }
}
