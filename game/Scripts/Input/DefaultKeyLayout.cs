using System;
using System.Linq;

namespace OpenScctGame.Controls;

/// <summary>The stock keyboard actions, also available without an original install or converted UI keys.</summary>
public static class DefaultKeyLayout
{
    public const string GearKey = "I";

    /// <summary>Both original Default_kbd_0/1.ini layouts resolve to these same bindings.</summary>
    public static KeyLayout Create() => KeyLayout.Parse(StockLayout);

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
            foreach ((string key, string command) in new[] { ("CapsLock", "PushToTalk"), ("Y", "TeamPushToTalk"), ("F8", "ToggleVoiceMute") })
            {
                if (HasAction(layout, command)) continue;
                string existing = layout.Bindings.TryGetValue(key, out string? raw) ? raw : "";
                layout.Bind(key, existing.Length == 0 ? command : existing + " | " + command);
            }
        }

        layout.UpgradeWheelBindings(wheelMode);
        return layout;
    }

    private static bool HasAction(KeyLayout layout, string command) =>
        layout.Bindings.Keys.Any(key => layout.Resolve(key).Any(a => a.Name.Equals(command, StringComparison.OrdinalIgnoreCase)));

    private static void AddIfFree(KeyLayout layout, string key, string command)
    {
        if (!layout.Bindings.ContainsKey(key) && !HasAction(layout, command)) layout.Bind(key, command);
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
