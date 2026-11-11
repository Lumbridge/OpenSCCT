using System;
using System.Collections.Generic;

namespace OpenScctGame.Controls;

[Flags]
public enum BindingModifiers
{
    None = 0, Ctrl = 1, Shift = 2, Alt = 4,

    /// <summary>
    /// Not a held key: the input pressed twice in quick succession (<c>Double+H</c>). It fires on the second press,
    /// alongside the input's ordinary binding, and stays held until that press is released.
    /// </summary>
    Double = 8,
}

/// <summary>A physical input and its optional modifiers. Existing plain IK names remain valid.</summary>
public readonly record struct BindingKey(string Base, BindingModifiers Modifiers = BindingModifiers.None)
{
    public const string DoublePrefix = "Double";
    public bool IsPulse => Base.StartsWith("MouseWheel", StringComparison.OrdinalIgnoreCase);
    public bool IsModifier => ModifierOf(Base) != BindingModifiers.None;
    public bool IsDoubleTap => Modifiers.HasFlag(BindingModifiers.Double);

    /// <summary>The held modifiers (Ctrl, Shift, Alt), without the double-tap marker.</summary>
    public BindingModifiers HeldModifiers => Modifiers & ~BindingModifiers.Double;
    public string Canonical => Join(static key => key, "+", DoublePrefix + "+");
    public string Format(Func<string, string> display, string separator = " + ") => Join(display, separator, "2x ");

    private string Join(Func<string, string> display, string separator, string doublePrefix)
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(BindingModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(BindingModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(BindingModifiers.Alt)) parts.Add("Alt");
        parts.Add(display(Base));
        string text = string.Join(separator, parts);
        return IsDoubleTap ? doublePrefix + text : text;
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
            BindingModifiers modifier = parts[i].Equals(DoublePrefix, StringComparison.OrdinalIgnoreCase)
                ? BindingModifiers.Double : ModifierOf(parts[i]);
            if (modifier == BindingModifiers.None || (modifiers & modifier) != 0) return false;
            modifiers |= modifier;
        }
        // A modifier key cannot be combined with held modifiers, but it can be double tapped (Double+Shift).
        if ((modifiers & ~BindingModifiers.Double) != BindingModifiers.None && ModifierOf(input) != BindingModifiers.None) return false;
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

    /// <summary>The double-tap binding of an input: the exact held modifiers first, then the plain double tap.</summary>
    public static string? SelectDouble(KeyLayout layout, string input, BindingModifiers modifiers)
    {
        if (ModifierOf(input) != BindingModifiers.None) modifiers = BindingModifiers.None;
        string candidate = new BindingKey(input, modifiers | BindingModifiers.Double).Canonical;
        if (layout.Bindings.ContainsKey(candidate)) return candidate;
        candidate = new BindingKey(input, BindingModifiers.Double).Canonical;
        return layout.Bindings.ContainsKey(candidate) ? candidate : null;
    }
}
