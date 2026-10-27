using System;
using System.Collections.Generic;
using OpenScct.Provenance;

namespace OpenScctGame.UI;

/// <summary>A row of the original key list: the binding (a whole command line) and where GUI.int names it.</summary>
public sealed record BindingRow(string Command, string Section, string Key);

/// <summary>
/// The names the options show for key bindings. The original's key list (GUI.Browser_Keys) does not show a binding's
/// command line: each row pairs a command line with a label from GUI.int ([Browser_Keys] KeyLabel[n], and
/// [ExtraKeyRows] for the chat, voice and camera rows Reloaded adds), and a key whose binding equals a row's command
/// line (ignoring case) shows that row's label. A binding with no row is named from <see cref="EnglishText"/>.
/// No Godot types: the logic tests check it.
/// </summary>
public static class BindingLabels
{
    /// <summary>A string of GUI.int by section and key, or null.</summary>
    public delegate string? Lookup(string section, string key);

    /// <summary>The rows in the original's order (the section headings, which bind nothing, are left out).</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.Browser_Keys defaultproperties Alias[n] and KeyLabel[n]; Browser_Keys.AddCommsRows and AddCinematicCameraRows (rows 43 to 49)")]
    public static readonly IReadOnlyList<BindingRow> Rows =
    [
        Row("AliasHaut", 1), Row("AliasBas", 2), Row("AliasGauche", 3), Row("AliasDroite", 4), Row("Use", 5),
        Row("Roll | GadLaserSwitch", 6), Row("FireMode | GadTorcheSwitch", 7), Row("Fire | GadgetQuickChoice 0", 8),
        Row("AltFire | GadgetQuickChoice 1 | HoldBreath", 9), Row("Jump", 10), Row("Crouch", 11), Row("PerformCoopMvt", 12),
        Row("HackComm", 13), Row("SwitchHeadset 3", 14), Row("SwitchHeadset 1", 15), Row("SwitchHeadset 2", 16),
        Row("SwitchEnhancedReality", 17), Row("SelectGadget 0", 18), Row("SelectGadget 1", 19), Row("SelectGadget 2", 20),
        Row("SelectGadget 3", 21), Row("AliasNextItem", 22), Row("AliasPrevItem", 23), Row("SwitchBinocularVision", 24),
        Row("ZoomIn", 25), Row("ZoomOut", 26), Row("Btw", 28), Row("SwitchLaser", 29), Row("PrevStickyCam", 30),
        Row("ChangeSpeed", 31), Row("ROF", 33), Row("Reload", 34), Row("DisplayDefaultControls", 36), Row("DisplayHud", 37),
        Row("DisplayStrategicMap", 38), Row("DisplaySessionStatus", 39), Row("ShowConnectedPlayers", 40), Row("Quit", 41),
        new("Talk", ExtraSection, "ChatLabel"), new("TeamTalk", ExtraSection, "TeamChatLabel"),
        new("PushToTalk", ExtraSection, "PushToTalkLabel"), new("ToggleVoiceMute", ExtraSection, "MuteMicrophoneLabel"),
        new("HideHud", ExtraSection, "HideHudLabel"), new("FreeCam", ExtraSection, "FreeCameraLabel"),
    ];

    private const string KeysSection = "Browser_Keys";
    private const string ExtraSection = "ExtraKeyRows";

    /// <summary>The name of the binding <paramref name="command"/>: its row's GUI.int label, else the English of its commands.</summary>
    public static string Label(string command, Lookup? gui)
    {
        ArgumentNullException.ThrowIfNull(command);
        int row = RowOf(command);
        if (row >= 0 && gui?.Invoke(Rows[row].Section, Rows[row].Key) is { Length: > 0 } label)
        {
            return label;
        }

        var parts = new List<string>();
        foreach (string part in command.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string english = EnglishText.Command(part);
            if (english.Length > 0 && !parts.Contains(english))
            {
                parts.Add(english);
            }
        }

        return string.Join(" / ", parts);
    }

    /// <summary>The binding's place in the original's list (bindings with no row come after every row).</summary>
    public static int Order(string command)
    {
        int row = RowOf(command);
        return row >= 0 ? row : Rows.Count;
    }

    /// <summary>The index in <see cref="Rows"/> of the row whose command line is <paramref name="command"/>, or -1.</summary>
    public static int RowOf(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        string normal = Normal(command);
        for (int i = 0; i < Rows.Count; i++)
        {
            if (string.Equals(Normal(Rows[i].Command), normal, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    // Browser_Keys defaultproperties Alias[n] with KeyLabel[n] (GUI.u); the label text itself is GUI.int's.
    private static BindingRow Row(string command, int label) => new(command, KeysSection, $"KeyLabel[{label}]");

    private static string Normal(string command) =>
        string.Join(" | ", Array.ConvertAll(command.Split('|', StringSplitOptions.TrimEntries),
            p => string.Join(' ', p.Split(' ', StringSplitOptions.RemoveEmptyEntries))));
}
