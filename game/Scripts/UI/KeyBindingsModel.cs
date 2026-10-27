using System;
using System.Collections.Generic;
using Godot;
using OpenScctGame.Play.Session;

namespace OpenScctGame.UI;

/// <summary>
/// One bound key of a layout: the UE2 key name (IK_ prefix removed), its command line and the English name the list
/// shows for it (<see cref="BindingLabels"/>: the stock command lines use French aliases such as AliasHaut).
/// </summary>
public sealed record KeyBindingRow(string Key, string Command, string Label);

/// <summary>
/// The options screen's view of a side's key layout (OpenScctGame.Controls.InputBindings, the original Default_kbd_0/1.ini
/// plus the player's changes). Rebinding moves a command to a new key and clears the old one; every change is saved
/// to the side's user file by the input layer.
/// </summary>
public sealed class KeyBindingsModel
{
    private OpenScctGame.Controls.InputBindings _bindings;

    public KeyBindingsModel(PlaySide side)
    {
        Side = side;
        _bindings = OpenScctGame.Controls.InputBindings.Load(side, null, seedInputMap: false);
    }

    public PlaySide Side { get; }

    /// <summary>Where the layout came from (an ini path, or "built-in").</summary>
    public string Source => _bindings.Source;

    /// <summary>
    /// Bound keys except the mouse axes, in the order of the original's key list (bindings it has no row for after,
    /// by name), then by key.
    /// </summary>
    public List<KeyBindingRow> Rows()
    {
        var rows = new List<KeyBindingRow>();
        foreach ((string key, string command) in _bindings.Layout.Bindings)
        {
            if (command.Length > 0 && !command.StartsWith("Axis ", StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(new KeyBindingRow(key, command, LabelOf(command)));
            }
        }

        rows.Sort((a, b) =>
        {
            int c = BindingLabels.Order(a.Command).CompareTo(BindingLabels.Order(b.Command));
            if (c == 0)
            {
                c = string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            }

            return c != 0 ? c : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
        });
        return rows;
    }

    /// <summary>The English name of a binding: GUI.int's key list label for its row, else our English (<see cref="EnglishText"/>).</summary>
    public static string LabelOf(string command) =>
        BindingLabels.Label(command, (section, key) => UiStyle.Assets?.Localize("GUI", section, key, "") is { Length: > 0 } s ? s : null);

    /// <summary>The command bound to <paramref name="key"/>, or null.</summary>
    public string? CommandOf(string key) => _bindings.Layout.Bindings.TryGetValue(key, out string? c) ? c : null;

    /// <summary>The UE2 key name of a key or mouse button press, or null.</summary>
    public static string? KeyOf(InputEvent e) => OpenScctGame.Controls.GodotKeys.ToIk(e);

    /// <summary>Moves the command of <paramref name="oldKey"/> to <paramref name="newKey"/> (whatever that key did
    /// is replaced) and clears <paramref name="oldKey"/>.</summary>
    public void Rebind(string oldKey, string newKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldKey);
        ArgumentException.ThrowIfNullOrEmpty(newKey);
        if (string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase) || CommandOf(oldKey) is not { } command)
        {
            return;
        }

        _bindings = _bindings.Rebind(newKey, command);
        _bindings = _bindings.Rebind(oldKey, "");
    }
}
