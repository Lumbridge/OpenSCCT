using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OpenScctGame.UI;

/// <summary>
/// English text, in our own words, for what the original shows from French names: the commands of the key layouts
/// (the stock Default_kbd_0/1.ini bind W to <c>AliasHaut</c>, A to <c>AliasGauche</c>, E to <c>FireMode |
/// GadTorcheSwitch</c>...) when GUI.int has no row for a binding, and Magma widgets whose page text is French with no
/// Magma.int entry to replace it. The game's own English (GUI.int, Magma.int) comes first; this table only fills gaps.
/// No Godot types: the logic tests check it.
/// </summary>
public static class EnglishText
{
    /// <summary>English names of exec commands and input aliases, by the whole command (with its argument) or its name.</summary>
    public static readonly IReadOnlyDictionary<string, string> Commands = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AliasHaut"] = "Move forward",
        ["AliasBas"] = "Move back",
        ["AliasGauche"] = "Move left",
        ["AliasDroite"] = "Move right",
        ["AliasPrevItem"] = "Previous gadget",
        ["AliasNextItem"] = "Next gadget",
        ["Fire"] = "Fire",
        ["AltFire"] = "Fire gadget",
        ["GadgetQuickChoice 0"] = "Throw slot 1 gadget",
        ["GadgetQuickChoice 1"] = "Throw slot 2 gadget",
        ["HoldBreath"] = "Hold breath",
        ["Use"] = "Use",
        ["Roll"] = "Roll",
        ["GadLaserSwitch"] = "Laser sight",
        ["FireMode"] = "Weapon in or out",
        ["GadTorcheSwitch"] = "Flashlight",
        ["Jump"] = "Jump",
        ["Crouch"] = "Crouch",
        ["PerformCoopMvt"] = "Co-op move",
        ["HackComm"] = "Hack enemy communications",
        ["Btw"] = "Back to wall",
        ["SwitchBinocularVision"] = "Binoculars or scope",
        ["SwitchLaser"] = "Weapon laser",
        ["Reload"] = "Reload",
        ["ROF"] = "Rate of fire",
        ["SwitchROF"] = "Rate of fire",
        ["ZoomIn"] = "Zoom in",
        ["ZoomOut"] = "Zoom out",
        ["PrevStickyCam"] = "Previous sticky camera",
        ["SelectGadget 0"] = "Gadget slot 1",
        ["SelectGadget 1"] = "Gadget slot 2",
        ["SelectGadget 2"] = "Gadget slot 3",
        ["SelectGadget 3"] = "Gadget slot 4",
        ["SwitchHeadset 1"] = "First vision mode",
        ["SwitchHeadset 2"] = "Second vision mode",
        ["SwitchHeadset 3"] = "Cycle vision modes",
        ["DisplayDefaultControls"] = "Show default controls",
        ["DisplayHud"] = "Show HUD help",
        ["DisplayStrategicMap"] = "Show strategic map",
        ["DisplaySessionStatus"] = "Show session status",
        ["SwitchEnhancedReality"] = "Enhanced Reality",
        ["ShowConnectedPlayers"] = "Show connected players",
        ["Talk"] = "Chat",
        ["TeamTalk"] = "Team chat",
        ["PushToTalk"] = "Push to talk",
        ["ToggleVoiceMute"] = "Mute microphone",
        ["HideHud"] = "Hide HUD",
        ["FreeCam"] = "Free camera",
        ["ChangeGear"] = "Change gear",
        ["Quit"] = "Quit game",
        ["ChangeSpeed"] = "Change speed",
        ["WheelUp"] = "Wheel up action",
        ["WheelDown"] = "Wheel down action",
        ["ShowMenu"] = "Menu",
    };

    /// <summary>
    /// Magma text widgets whose page text is French and that Magma.int does not replace, by area and widget: an
    /// invisible test button and the placeholders of the four-way inventory.
    /// </summary>
    public static readonly IReadOnlyDictionary<(string Area, string Widget), string> MagmaWidgets =
        new Dictionary<(string Area, string Widget), string>(new AreaWidgetComparer())
        {
            [("B_Invis", "LBL_Text")] = "Hidden button",
            [("B_Invis1", "LBL_Text")] = "Hidden button",
            [("A_InventaireComplet", "THaut")] = "_Gadget name",
            [("A_InventaireComplet", "TBas")] = "_Gadget name",
            [("A_InventaireComplet", "TGauche")] = "_Gadget name",
            [("A_InventaireComplet", "TDroite")] = "_Gadget name",
        };

    /// <summary>
    /// The text a Magma text widget shows before the page code sets it: Magma.int's entry for [area] widget when there
    /// is one (the game's localisation), else this table's English, else the text the page was saved with.
    /// </summary>
    public static string ResolveMagma(string area, string widget, string pageText, Func<string, string, string?>? magmaInt) =>
        magmaInt?.Invoke(area, widget) ?? MagmaWidget(area, widget) ?? pageText;

    /// <summary>The English text of a Magma widget that has one here, or null.</summary>
    public static string? MagmaWidget(string area, string widget) =>
        MagmaWidgets.TryGetValue((area, widget), out string? text) ? text : null;

    /// <summary>
    /// The English name of one command of a binding (<c>SelectGadget 1</c>, <c>AliasHaut</c>): the table by the whole
    /// command, then by its name with the argument after it, else the name's words (<c>ShowMenu</c> reads "Show menu").
    /// </summary>
    public static string Command(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        string trimmed = string.Join(' ', command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (trimmed.Length == 0)
        {
            return "";
        }

        if (Commands.TryGetValue(trimmed, out string? whole))
        {
            return whole;
        }

        int space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        string name = space < 0 ? trimmed : trimmed[..space];
        string arg = space < 0 ? "" : trimmed[space..];
        return (Commands.TryGetValue(name, out string? known) ? known : Words(name)) + arg;
    }

    /// <summary>A command name as words: <c>ShowConnectedPlayers</c> to "Show connected players".</summary>
    public static string Words(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var sb = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            bool boundary = i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1])));
            if (boundary)
            {
                sb.Append(' ');
                sb.Append(char.ToLower(c, CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private sealed class AreaWidgetComparer : IEqualityComparer<(string Area, string Widget)>
    {
        public bool Equals((string Area, string Widget) x, (string Area, string Widget) y) =>
            string.Equals(x.Area, y.Area, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Widget, y.Widget, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Area, string Widget) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Area), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Widget));
    }
}
