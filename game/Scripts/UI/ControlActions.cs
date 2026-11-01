using System;
using System.Collections.Generic;
using System.Linq;
using OpenScctGame.Controls;

namespace OpenScctGame.UI;

/// <summary>Player-facing action, verified against LocalInput and SPlayerController's input/state handlers.
/// Command groups keep required held bytes and press callbacks together; unrelated actions may be separated.</summary>
public sealed record ControlAction(string Id, string Category, string Label, string Description, string Command,
    string[] Tokens, bool Spy = true, bool Merc = true, bool Available = true)
{
    public bool Matches(string part) => Tokens.Contains(ControlActions.Token(part), StringComparer.OrdinalIgnoreCase);
}

public static class ControlActions
{
    // Sources: LocalInput.Buttons/PlayerCommands; SPlayerController.CombatInput, Movement, CombatScope;
    // spec/equipment/{spy-shocker,grenade-throw,vision-goggles,merc-rifle}.md and UI/PlayMenu handlers.
    public static readonly IReadOnlyList<ControlAction> All =
    [
        A("forward", "Movement", "Move forward", "Hold to move forward.", "Button BT_Up", "BT_Up"),
        A("back", "Movement", "Move backward", "Hold to move backward.", "Button BT_Down", "BT_Down"),
        A("left", "Movement", "Move left", "Hold to move left.", "Button BT_Left", "BT_Left"),
        A("right", "Movement", "Move right", "Hold to move right.", "Button BT_Right", "BT_Right"),
        A("jump", "Movement", "Jump / contextual traversal", "Jump or perform the traversal available in your current position.", "Function Jump | Axis aUp Speed=+280.0", "Jump", "aUp"),
        A("crouch", "Movement", "Crouch", "Crouch; behavior can depend on your movement state.", "Button bCrouch", "bCrouch"),
        A("roll", "Movement", "Roll", "Roll from a crouch or dive-roll while running, when movement and clearance allow it.", "Button bRoll", "bRoll") with { Merc = false },
        A("use", "Movement", "Interact / action menu", "Use nearby objects or interact with a player. Hold where an interaction or action menu requires it.", "InvButton bUsing", "bUsing"),
        A("wall", "Movement", "Back to wall", "Enter or leave a back-to-wall position where supported.", "InvButton bUseBackToWall", "bUseBackToWall") with { Merc = false },
        A("coop", "Movement", "Co-op move", "Request the co-op movement available with your teammate.", "PerformCoopMvt", "PerformCoopMvt") with { Merc = false },
        A("run_compat", "Movement", "Run (saved shortcut)", "Compatibility for a previously configured run shortcut.", "Run", "Run") with { Available = false },
        A("speed", "Movement", "Walk", "Run by default. Hold or toggle Walk using the behavior selected for this role. Wheel bindings always toggle. Contextual scrolling takes priority in zoom views and interaction menus.", "Walk", "Walk", "ChangeSpeed"),
        A("fire", "Combat", "Primary fire / contextual attack", "Fire the weapon or perform the primary action allowed by your current combat state.", "Button bFire | Function Fire", "bFire", "Fire"),
        A("altfire", "Combat", "Use selected gadget / alternate action", "Use the selected gadget or the alternate action of the current state. Grenade use can require holding and releasing.", "Button bAltFire | Function AltFire", "bAltFire", "AltFire"),
        A("ready", "Combat", "Ready / holster weapon", "With the weapon holstered, tap to ready it with the laser; hold longer than 0.5 seconds to ready it without the laser. Press again to leave weapon mode where permitted.", "InvButton bFireMode", "bFireMode") with { Merc = false },
        A("reload", "Combat", "Reload", "Reload the current weapon when permitted.", "Button bReload", "bReload"),
        A("swapweapon", "Combat", "Swap weapon", "Switch between your primary and carried pistol. A pistol occupies one gadget slot.", "SwapWeapon", "SwapWeapon") with { Spy = false },
        A("rof", "Combat", "Switch fire mode", "Switch the mercenary rifle's firing mode (burst / full auto), once per press.", "Button bROF", "bROF", "SwitchROF") with { Spy = false },
        A("breath", "Combat", "Hold breath", "Hold to steady the mercenary sniper scope when available.", "Button bHoldBreath", "bHoldBreath"),
        A("quick1", "Gadgets", "Quick-use gadget slot 1", "While the spy is unarmed, quick-use slot 1: quick-launch a supported grenade or use cloak when applicable.", "GadgetQuickChoice 0", "GadgetQuickChoice 0") with { Merc = false },
        A("quick2", "Gadgets", "Quick-use gadget slot 2", "While the spy is unarmed, quick-launch a supported gadget from slot 2. Cloak is excluded from this shortcut.", "GadgetQuickChoice 1", "GadgetQuickChoice 1") with { Merc = false },
        A("launch1", "Gadgets", "Launch gadget slot 1", "Select and fire gadget slot 1 with the weapon drawn.", "LaunchGadget 0", "LaunchGadget 0") with { Merc = false },
        A("launch2", "Gadgets", "Launch gadget slot 2", "Select and fire gadget slot 2 with the weapon drawn.", "LaunchGadget 1", "LaunchGadget 1") with { Merc = false },
        A("launch3", "Gadgets", "Launch gadget slot 3", "Select and fire gadget slot 3 with the weapon drawn.", "LaunchGadget 2", "LaunchGadget 2") with { Merc = false },
        A("launch4", "Gadgets", "Launch gadget slot 4", "Select and fire gadget slot 4 with the weapon drawn.", "LaunchGadget 3", "LaunchGadget 3") with { Merc = false },
        A("longroll", "Movement", "Long roll", "Stand from crouch when needed and roll.", "LongRoll | Button bRoll", "LongRoll") with { Merc = false },
        A("cloakquick", "Gadgets", "Toggle selected cloak", "Use the selected cloak.", "PadCloakQuick", "PadCloakQuick") with { Merc = false, Available = false },
        A("cameragas", "Gadgets", "Release sticky camera gas", "Release gas from the camera being used.", "PadCameraGas", "PadCameraGas") with { Merc = false, Available = false },
        A("cameraexit", "Gadgets", "Leave camera", "Leave the current sticky camera or camera network view.", "PadCameraExit", "PadCameraExit") with { Available = false },
        A("slot1", "Gadgets", "Select gadget slot 1", "Select the gadget in slot 1.", "SelectGadget 0", "SelectGadget 0"),
        A("slot2", "Gadgets", "Select gadget slot 2", "Select the gadget in slot 2.", "SelectGadget 1", "SelectGadget 1"),
        A("slot3", "Gadgets", "Select gadget slot 3", "Select the gadget in slot 3.", "SelectGadget 2", "SelectGadget 2"),
        A("slot4", "Gadgets", "Select gadget slot 4", "Select the gadget in slot 4.", "SelectGadget 3", "SelectGadget 3"),
        A("next", "Gadgets", "Next gadget", "Select the next gadget.", "NextGadget", "NextGadget", "BT_NextItem"),
        A("previous", "Gadgets", "Previous gadget", "Select the previous gadget.", "PreviousGadget", "PreviousGadget", "BT_PrevItem"),
        A("laser", "Gadgets", "Toggle laser gadget", "Switch to the laser sight and toggle it. The laser sight and flashlight cannot be active together.", "GadLaserSwitch", "GadLaserSwitch") with { Spy = false },
        A("torch", "Gadgets", "Toggle flashlight gadget", "Switch to the flashlight and toggle it. The flashlight and laser sight cannot be active together.", "GadTorcheSwitch", "GadTorcheSwitch") with { Spy = false },
        A("weaponlaser", "Gadgets", "Toggle weapon laser", "Toggle the spy weapon laser in weapon states that support it.", "SwitchLaser", "SwitchLaser") with { Merc = false },
        A("sticky", "Gadgets", "Previous sticky camera", "Switch to the previous available sticky camera.", "PrevStickyCam", "PrevStickyCam") with { Merc = false },
        A("vision1", "Vision", "First vision mode", "Toggle the role's first vision mode, when allowed by match settings.", "SwitchHeadset 1", "SwitchHeadset 1"),
        A("vision2", "Vision", "Second vision mode", "Toggle the role's second vision mode, when allowed by match settings.", "SwitchHeadset 2", "SwitchHeadset 2"),
        A("visioncycle", "Vision", "Cycle vision modes", "Cycle the available vision modes.", "SwitchHeadset 3", "SwitchHeadset 3"),
        A("scope", "Vision", "Binoculars / sniper scope", "Toggle the role's zoom view. A spy with the weapon drawn first requests holstering it.", "SwitchBinocularVision", "SwitchBinocularVision"),
        A("perspective", "Vision", "First / third person", "Use first person for tight spaces. Toggle switches perspective on each press; Hold uses first person until released. Wheel bindings always toggle. The perspective behavior above also applies to controller bindings.", "SwitchSpyPerspective", "SwitchSpyPerspective") with { Merc = false },
        A("zoomin", "Vision", "Zoom in", "Increase zoom in views that support it.", "Button bZoomIn | Function ZoomIn", "ZoomIn", "bZoomIn"),
        A("zoomout", "Vision", "Zoom out", "Decrease zoom in views that support it.", "Button bZoomOut | Function ZoomOut", "ZoomOut", "bZoomOut"),
        A("er", "Vision", "Toggle Enhanced Reality", "Toggle the Enhanced Reality display.", "SwitchEnhancedReality", "SwitchEnhancedReality"),
        A("wheelup", "Gadgets", "Mouse wheel up action", "Uses the shared mouse-wheel preference: gadget selection or movement speed.", "WheelUp", "WheelUp") with { Available = false },
        A("wheeldown", "Gadgets", "Mouse wheel down action", "Uses the shared mouse-wheel preference: gadget selection or movement speed.", "WheelDown", "WheelDown") with { Available = false },
        A("hackcomms", "Communication", "Hack enemy communications", "Request communications hacking when available.", "Button bSpecialsChat", "bSpecialsChat"),
        A("chat", "Communication", "Chat", "Open public text chat.", "Talk", "Talk"),
        A("teamchat", "Communication", "Team chat", "Open team text chat.", "TeamTalk", "TeamTalk"),
        A("voice", "Communication", "Push to talk", "Hold to transmit on the all channel; also forces transmission in voice activation mode.", "PushToTalk", "PushToTalk"),
        A("teamvoice", "Communication", "Team push to talk", "Hold to transmit to your team only.", "TeamPushToTalk", "TeamPushToTalk"),
        A("mute", "Communication", "Toggle microphone mute", "Mute or unmute your microphone.", "ToggleVoiceMute", "ToggleVoiceMute"),
        A("controls", "Interface", "Controls reference (unavailable)", "This legacy shortcut has no active handler in the current build.", "DisplayDefaultControls", "DisplayDefaultControls") with { Available = false },
        A("hudhelp", "Interface", "HUD help (unavailable)", "This legacy shortcut has no active handler in the current build.", "DisplayHud", "DisplayHud") with { Available = false },
        A("map", "Interface", "Show strategic map", "Open the strategic map.", "DisplayStrategicMap", "DisplayStrategicMap"),
        A("session", "Interface", "Show session status", "Show session status.", "DisplaySessionStatus", "DisplaySessionStatus"),
        A("players", "Interface", "Show connected players", "Show the player list.", "ShowConnectedPlayers", "ShowConnectedPlayers"),
        A("gear", "Interface", "Change gear", "Open the gear picker where gear changes are allowed.", "ChangeGear", "ChangeGear"),
        A("hidehud", "Interface", "Toggle HUD visibility", "Toggle HUD visibility when cinematic controls are allowed by the host.", "HideHud", "HideHud"),
        A("freecam", "Interface", "Toggle cinematic free camera", "Toggle cinematic free camera when allowed by the host.", "FreeCam", "FreeCam"),
        A("menu", "Interface", "Open game menu", "Open the in-game menu.", "ShowMenu", "ShowMenu"),
        A("quit", "Interface", "Quit shortcut (unavailable)", "This legacy shortcut has no active handler in the current build. Use the game menu to quit.", "Quit", "Quit") with { Available = false },
    ];

    public const string PadPrefix = "Action:";
    public const string VirtualPrefix = "svm-action:";
    public static bool Direct(ControlAction action) => action.Category is not ("Communication" or "Interface") && action.Id != "er" || action.Id is "freecam" or "hidehud" or "hackcomms" or "session" or "menu";
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<KeyAction>> DirectActions = All.Where(Direct)
        .ToDictionary(a => a.Id, a => (IReadOnlyList<KeyAction>)KeyLayout.Parse("[Engine.Input]\nIK_X=" + a.Command).Resolve("X"));

    public static IReadOnlyList<KeyAction> VirtualActions(string key)
    {
        if (!key.StartsWith(VirtualPrefix, StringComparison.Ordinal)) return Array.Empty<KeyAction>();
        string id = key[(key.LastIndexOf(':') + 1)..];
        return DirectActions.TryGetValue(id, out var actions) ? actions : Array.Empty<KeyAction>();
    }

    private static ControlAction A(string id, string category, string label, string description, string command, params string[] tokens) =>
        new(id, category, label, description, command, tokens);

    public static IEnumerable<ControlAction> ForRole(bool merc, string wheelMode = "Gadget") => All.Where(a => a.Available && (merc ? a.Merc : a.Spy)).Select(a => a.Id switch
    {
        "wheelup" => a with { Label = wheelMode == "Gadget" ? "Next gadget (wheel up)" : "Run (wheel up)", Description = (wheelMode == "Gadget" ? "Select the next gadget." : "Set movement speed to run.") + " In zoom views, the current view can use this input to zoom in instead." },
        "wheeldown" => a with { Label = wheelMode == "Gadget" ? "Previous gadget (wheel down)" : "Walk (wheel down)", Description = (wheelMode == "Gadget" ? "Select the previous gadget." : "Set movement speed to walk.") + " In zoom views, the current view can use this input to zoom out instead." },
        "breath" when !merc => a with { Category = "Gadgets", Label = "Cloak / contextual quick-use", Description = "With cloak selected, this input operates the cloak. Holding beyond 0.2 seconds can activate it; release or a short tap can deactivate it or request a contextual quick gadget action." },
        "vision1" => a with { Label = merc ? "Motion tracking vision" : "Night vision" },
        "vision2" => a with { Label = merc ? "EMF vision" : "Thermal vision" },
        "scope" => a with { Label = merc ? "Sniper scope" : "Binoculars", Description = merc ? "Aim through the sniper scope using the Hold or Toggle behavior selected above. Wheel assignments always toggle." : "Toggle binoculars. With the weapon drawn, this first requests holstering it." },
        _ => a,
    }).OrderBy(a => a.Category switch
    {
        "Movement" => 0, "Combat" => 1, "Gadgets" => 2, "Vision" => 3, "Communication" => 4, _ => 5,
    });

    public static string Token(string raw)
    {
        string[] words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "";
        int start = words[0].ToUpperInvariant() is "BUTTON" or "INVBUTTON" or "FUNCTION" or "AXIS" ? 1 : 0;
        if (start >= words.Length) return raw;
        string name = words[start];
        return name.Equals("SelectGadget", StringComparison.OrdinalIgnoreCase) || name.Equals("GadgetQuickChoice", StringComparison.OrdinalIgnoreCase) || name.Equals("LaunchGadget", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("SwitchHeadset", StringComparison.OrdinalIgnoreCase) ? name + " " + (words.Length > start + 1 ? words[start + 1] : "0") : name;
    }
}

/// <summary>In-memory action editing. No files or live input state change until the caller commits Layout.</summary>
public sealed class ActionBindingDraft
{
    public ActionBindingDraft(KeyLayout layout) { Layout = layout.Clone(); Baseline = Layout.ToIni(); }
    public KeyLayout Layout { get; private set; }
    public string Baseline { get; private set; }
    public bool Dirty => Layout.ToIni() != Baseline;
    public void Accept() => Baseline = Layout.ToIni();
    public void Reset(KeyLayout defaults) => Layout = defaults.Clone();
    public IEnumerable<string> Keys(ControlAction action) => Layout.Bindings.Where(b => Layout.Expanded(b.Value).Any(action.Matches)).Select(b => b.Key);
    public string CommandOf(string key) => Layout.Bindings.TryGetValue(key, out string? c) ? c : "";
    public void Remove(ControlAction action, string? key = null)
    {
        foreach (string k in Keys(action).Where(k => key is null || k.Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray())
            Layout.Bind(k, string.Join(" | ", Layout.Expanded(CommandOf(k)).Where(p => !action.Matches(p))));
    }
    public void Assign(ControlAction action, string key, bool replace = false)
    {
        Remove(action);
        string existing = replace ? "" : CommandOf(key);
        Layout.Bind(key, existing.Length == 0 ? action.Command : existing + " | " + action.Command);
    }
    public void Swap(ControlAction action, string oldKey, string newKey)
    {
        // Swap this action with the destination's complete binding; keep unrelated source actions in place.
        string displaced = CommandOf(newKey);
        Remove(action);
        string remaining = CommandOf(oldKey);
        Layout.Bind(oldKey, string.Join(" | ", new[] { remaining, displaced }.Where(s => s.Length > 0)));
        Layout.Bind(newKey, action.Command);
    }
}
