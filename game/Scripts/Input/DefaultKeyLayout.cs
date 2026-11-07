using System;
using System.Linq;

namespace OpenScctGame.Controls;

/// <summary>The SCCT Enhanced keyboard defaults, independent of the original install or converted UI keys.</summary>
public static class DefaultKeyLayout
{
    public const string GearKey = "I";
    public const string SpyPerspectiveKey = "X";
    public const string PerspectiveBindingMarker = "; OpenSCCT spy perspective binding v1";
    public const string CloakKey = "K";
    public const string CloakBindingMarker = "; OpenSCCT innate cloak binding v1";
    public const string EnhancedDefaultsMarker = "; OpenSCCT Enhanced keyboard defaults v1";

    /// <summary>Both Enhanced Beta 3 and Beta 5 Default_kbd_0/1.ini layouts resolve to these same bindings.</summary>
    public static KeyLayout Create() => KeyLayout.Parse(StockLayout);

    /// <summary>
    /// Old downloaded content shipped the original arrow/numpad merc preset. Only untouched complete snapshots
    /// of that preset are upgraded; one changed or unbound action, or an edited slot order, preserves the file.
    /// </summary>
    public static bool IsLegacyMercDefault(string? saved, string wheelMode)
    {
        if (saved is null || !saved.StartsWith(KeyLayout.SnapshotMarker, StringComparison.Ordinal) ||
            saved.Contains(EnhancedDefaultsMarker, StringComparison.Ordinal) ||
            saved.Contains("; OpenSCCT input order", StringComparison.Ordinal)) return false;

        KeyLayout legacy = Create();
        (string From, string To)[] remaps =
        [
            ("A", "Left"), ("D", "Right"), ("W", "Up"), ("S", "Down"), ("Shift", "Enter"),
            ("Q", "NumPad0"), ("T", "Insert"), ("E", "NumPad1"), ("Space", "Shift"), ("C", "Ctrl"),
            ("H", "GreyMinus"), ("Z", "NumPadPeriod"), ("B", "Home"), ("L", "GreyStar"), ("R", "GreyPlus"),
            ("F", "GreySlash"), ("V", "NumPad2"), ("1", "NumPad4"), ("2", "NumPad5"), ("3", "NumPad7"),
            ("4", "NumPad8"), ("5", "NumPad6"), ("6", "NumPad9"), ("Tab", "Backspace"), ("F1", "Delete"), ("F2", "End"),
        ];
        KeyLayout enhanced = Create();
        foreach (var pair in remaps) legacy.Bind(pair.From, "");
        foreach (var pair in remaps) legacy.Bind(pair.To, enhanced.Bindings[pair.From]);
        foreach (string key in new[] { "MouseX", "MouseY", "MouseWheelUp", "MouseWheelDown", "Escape" }) legacy.Bind(key, "");
        legacy.UpgradeWheelBindings(wheelMode);

        KeyLayout candidate = KeyLayout.Parse(saved);
        candidate.UpgradeWheelBindings(wheelMode);
        foreach ((string key, string command) in new[]
        {
            (GearKey, "ChangeGear"), ("G", "SwapWeapon"), ("N", "ToggleTeammateNames"),
            ("CapsLock", "PushToTalk"), ("Y", "TeamPushToTalk"),
            (SpyPerspectiveKey, "SwitchSpyPerspective"), (CloakKey, "Button bCloak"),
        })
        {
            KeyLayout action = KeyLayout.Parse("IK_" + key + "=" + command);
            if (candidate.Resolve(key).SequenceEqual(action.Resolve(key))) candidate.Bind(key, "");
        }
        if (candidate.Resolve("F8").Select(a => a.Name).SequenceEqual(new[] { "DisplaySessionStatus", "ToggleVoiceMute" }))
            candidate.Bind("F8", "DisplaySessionStatus");

        string[] keys = candidate.Bindings.Keys.Where(key => candidate.Resolve(key).Count > 0).ToArray();
        return keys.Length == legacy.Bindings.Count && keys.All(key =>
            legacy.Bindings.ContainsKey(key) && candidate.Resolve(key).SequenceEqual(legacy.Resolve(key)));
    }

    /// <summary>
    /// Prepares the complete layout before it is used or saved. Complete snapshots remain authoritative, including
    /// deliberately unbound actions; legacy overlays receive additions without displacing existing bindings.
    /// </summary>
    public static KeyLayout ApplyProfile(KeyLayout source, string? saved, string wheelMode)
    {
        ArgumentNullException.ThrowIfNull(source);
        KeyLayout layout = saved is null ? source.Clone() : KeyLayout.ApplyOverrides(source, saved);
        if (saved?.StartsWith(KeyLayout.SnapshotMarker, StringComparison.Ordinal) != true)
        {
            AddIfFree(layout, GearKey, "ChangeGear");
            AddIfFree(layout, "G", "SwapWeapon");
            AddIfFree(layout, "N", "ToggleTeammateNames");
            foreach ((string key, string command) in new[] { ("CapsLock", "PushToTalk"), ("Y", "TeamPushToTalk"), ("F8", "ToggleVoiceMute") })
            {
                if (HasAction(layout, command)) continue;
                string existing = layout.Bindings.TryGetValue(key, out string? raw) ? raw : "";
                layout.Bind(key, existing.Length == 0 ? command : existing + " | " + command);
            }
        }

        // Older complete snapshots predate this action. Add its default only when the key is free;
        // new snapshots carry the marker even when the player deliberately leaves it unbound.
        if (saved?.Contains(PerspectiveBindingMarker, StringComparison.Ordinal) != true)
            AddIfFree(layout, SpyPerspectiveKey, "SwitchSpyPerspective");
        if (saved?.Contains(CloakBindingMarker, StringComparison.Ordinal) != true)
            AddIfFree(layout, CloakKey, "Button bCloak");
        layout.UpgradeWheelBindings(wheelMode);
        return layout;
    }

    private static bool HasAction(KeyLayout layout, string command) =>
        layout.Bindings.Keys.Any(key => layout.Resolve(key).Any(a => a.Name.Equals(command, StringComparison.OrdinalIgnoreCase)));

    private static void AddIfFree(KeyLayout layout, string key, string command)
    {
        string action = command.StartsWith("Button ", StringComparison.Ordinal) ? command[7..] : command;
        if (!layout.Bindings.ContainsKey(key) && !HasAction(layout, action)) layout.Bind(key, command);
    }

    // Expanded commands keep the fallback independent of the original INI's alias names.
    private const string StockLayout = """
        [Engine.Input]
        IK_MiddleMouse=ChangeSpeed
        IK_A=Button BT_Left
        IK_D=Button BT_Right
        IK_W=Button BT_Up
        IK_S=Button BT_Down
        IK_Shift=InvButton bUsing
        IK_Q=Button bRoll | GadLaserSwitch
        IK_T=PerformCoopMvt
        IK_E=InvButton bFireMode | GadTorcheSwitch
        IK_LeftMouse=Button bFire | Function Fire | GadgetQuickChoice 0
        IK_RightMouse=Button bAltFire | Function AltFire | GadgetQuickChoice 1 | Button bHoldBreath
        IK_Space=Function Jump | Axis aUp Speed=+280.0
        IK_C=Button bCrouch
        IK_H=Button bSpecialsChat
        IK_Z=InvButton bUseBackToWall
        IK_B=SwitchBinocularVision
        IK_L=SwitchLaser
        IK_R=Button bReload
        IK_F=Button bROF
        IK_PageUp=Button bZoomIn | Function ZoomIn
        IK_PageDown=Button bZoomOut | Function ZoomOut
        IK_V=PrevStickyCam
        IK_1=SelectGadget 0
        IK_2=SelectGadget 1
        IK_3=SelectGadget 2
        IK_4=SelectGadget 3
        IK_5=Button BT_PrevItem
        IK_6=Button BT_NextItem
        IK_Tab=SwitchHeadset 3
        IK_F1=SwitchHeadset 1
        IK_F2=SwitchHeadset 2
        IK_F3=DisplayDefaultControls
        IK_F4=DisplayHud
        IK_F5=Talk
        IK_F6=TeamTalk
        IK_F7=DisplayStrategicMap
        IK_F8=DisplaySessionStatus
        IK_F9=SwitchEnhancedReality
        IK_F10=ShowConnectedPlayers
        IK_F11=Quit
        IK_MouseX=Axis aMouseX Speed=6.00
        IK_MouseY=Axis aMouseY Speed=6.00
        IK_MouseWheelDown=WheelDown
        IK_MouseWheelUp=WheelUp
        IK_Escape=ShowMenu
        """;
}
