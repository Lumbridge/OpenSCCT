using System;
using System.Collections.Generic;
using System.Linq;
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
/// plus the player's changes). Options use a staged ActionBindingDraft; older callers can still rebind immediately.
/// </summary>
public sealed class KeyBindingsModel
{
    private OpenScctGame.Controls.InputBindings _bindings;

    public KeyBindingsModel(PlaySide side, bool staged = false, bool? shared = null, bool defaults = false)
    {
        Side = side;
        _bindings = OpenScctGame.Controls.InputBindings.Load(side, null, seedInputMap: false, sharedOverride: shared, draft: staged, defaults: defaults);
        if (staged) Draft = new ActionBindingDraft(_bindings.Layout);
    }

    public ActionBindingDraft? Draft { get; }
    public bool Commit() => Draft is null || _bindings.SaveDraft(Draft.Layout);
    private OpenScctGame.Controls.KeyLayout Layout => Draft?.Layout ?? _bindings.Layout;
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
        foreach ((string key, string command) in Layout.Bindings)
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

    /// <summary>
    /// What a controller button can do on this side: nothing, the menu, each bound key's action (named as the key list
    /// names it; the action is the key's first command or button), then the hub's own keys.
    /// </summary>
    /// <remarks>
    /// Both stock layouts bind the same shared key list, so the other team's actions (<see cref="SpyOnly"/>,
    /// <see cref="MercOnly"/>) are left out unless <paramref name="otherTeam"/> is set (to name a saved choice).
    /// </remarks>
    public List<(string Action, string Label)> PadChoices(bool otherTeam = false, PlaySide? forSide = null, string wheelMode = "Gadget")
    {
        PlaySide role = forSide ?? Side;
        var choices = new List<(string Action, string Label)>
        {
            ("", "(nothing)"),
            (OpenScctGame.Options.PadSettings.MenuAction, "Menu"),
        };
        // The sensitivity clutches are held by the controller itself (rl:input.clutch); the second is the merc's.
        choices.Add((OpenScctGame.Options.PadSettings.ClutchOneAction, "Hold to modify sensitivity"));
        if (role == PlaySide.Merc || otherTeam)
        {
            choices.Add((OpenScctGame.Options.PadSettings.ClutchTwoAction, "Second held sensitivity modifier"));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "", OpenScctGame.Options.PadSettings.MenuAction, OpenScctGame.Options.PadSettings.ClutchOneAction,
            OpenScctGame.Options.PadSettings.ClutchTwoAction,
        };
        foreach (ControlAction action in otherTeam ? ControlActions.All : ControlActions.ForRole(role == PlaySide.Merc, wheelMode))
            if (ControlActions.Direct(action)) choices.Add((ControlActions.PadPrefix + action.Id, action.Label));

        // Keep legacy choices and their behavior. They activate a whole keyboard binding, not an independent action.
        foreach (KeyBindingRow row in otherTeam ? Rows().OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase) : Enumerable.Empty<KeyBindingRow>())
        {
            if (!otherTeam && (!UsedBy(row.Command, role) || !Layout.Expanded(row.Command).Any(p => ControlActions.ForRole(role == PlaySide.Merc, wheelMode).Any(a => a.Matches(p))))) continue;
            foreach (var a in Layout.Resolve(row.Key))
            {
                if (a.Kind == OpenScctGame.Controls.KeyActionKind.Axis) continue;
                string label = string.Join(" / ", Layout.Expanded(row.Command).Select(p =>
                    ControlActions.ForRole(role == PlaySide.Merc, wheelMode).FirstOrDefault(c => c.Matches(p))?.Label ??
                    (ControlActions.All.Any(c => c.Matches(p)) ? null : p)).Where(p => p is not null).Distinct());
                if (label.Length == 0) label = "Other role actions";
                if (seen.Add(a.Name)) choices.Add((a.Name, "Keyboard-linked: " + label));
            }
        }

        foreach ((string key, string label) in HubKeys)
        {
            choices.Add((OpenScctGame.Options.PadSettings.KeyPrefix + key, "Hub: " + label));
        }

        return choices;
    }

    /// <summary>Key list commands only the spy has (Reloaded's spy-only pad binds, rl:input.padbinds).</summary>
    public static readonly string[] SpyOnly = ["HackComm", "PrevStickyCam", "Btw", "PerformCoopMvt", "ChangeSpeed"];

    /// <summary>Key list commands only the mercenary has (Reloaded's merc-only pad binds, rl:input.padbinds).</summary>
    public static readonly string[] MercOnly = ["ROF"];

    /// <summary>Whether <paramref name="side"/> uses the key list command <paramref name="command"/>.</summary>
    public static bool UsedBy(string command, PlaySide side)
    {
        string[] other = side == PlaySide.Merc ? SpyOnly : MercOnly;
        return Array.FindIndex(other, c => string.Equals(c, command.Trim(), StringComparison.OrdinalIgnoreCase)) < 0;
    }

    /// <summary>The hub's own keys (HubHud), which are not layout commands.</summary>
    public static readonly (string Key, string Label)[] HubKeys =
    [
        ("Enter", "chat"),
        ("N", "back to the plaza"),
        ("U", "use"),
        ("X", "leave the activity"),
        ("G", "emotes"),
        ("P", "player list"),
        ("O", "armoury"),
        ("V", "vote"),
        ("H", "watch"),
        ("J", "play spy"),
        ("K", "play mercenary"),
    ];

    /// <summary>The command bound to <paramref name="key"/>, or null.</summary>
    public string? CommandOf(string key) => Layout.Bindings.TryGetValue(key, out string? c) ? c : null;

    /// <summary>The UE2 key name of a key or mouse button press, or null.</summary>
    public static string? KeyOf(InputEvent e) => OpenScctGame.Controls.GodotKeys.ToIk(e);

    /// <summary>Moves the command of <paramref name="oldKey"/> to <paramref name="newKey"/>. When
    /// <paramref name="newKey"/> already had a command, the two keys swap, so no command loses its key (and its row);
    /// otherwise <paramref name="oldKey"/> is cleared.</summary>
    public void Rebind(string oldKey, string newKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldKey);
        ArgumentException.ThrowIfNullOrEmpty(newKey);
        if (string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase) || CommandOf(oldKey) is not { Length: > 0 } command)
        {
            return;
        }

        string displaced = CommandOf(newKey) ?? "";
        if (displaced.StartsWith("Axis ", StringComparison.OrdinalIgnoreCase))
        {
            displaced = "";
        }

        if (Draft is not null)
        {
            Draft.Layout.Bind(newKey, command);
            Draft.Layout.Bind(oldKey, displaced);
            return;
        }
        _bindings = _bindings.Rebind(newKey, command);
        _bindings = _bindings.Rebind(oldKey, displaced);
    }
}
