using System;
using System.Collections.Generic;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;

namespace OpenScctGame.Play.Session;

/// <summary>An exec command of an input binding, run once on the press (<c>SelectGadget 1</c>, <c>Jump</c>).</summary>
public readonly record struct InputCommand(string Name, int Arg = 0)
{
    public override string ToString() => Arg == 0 ? Name : $"{Name} {Arg}";
}

/// <summary>
/// One tick of the local player's input as the original's input system leaves it on the controller: the input bytes
/// the bindings hold down (UE2 <c>Button</c> and SCCT <c>InvButton</c> aliases), the raw mouse counts of the tick,
/// the <c>aUp</c> axis, and the exec commands pressed during the tick, in order. The input layer fills one instance
/// per tick; <see cref="PlaySession"/> applies it to the local controller just before the client tick.
/// </summary>
public sealed class LocalInput
{
    /// <summary>The input bytes by their script name (BT_Up, bUsing...), 0 or 1.</summary>
    public static readonly IReadOnlyList<string> ButtonNames =
    [
        "BT_Up", "BT_Down", "BT_Left", "BT_Right", "BT_PrevItem", "BT_NextItem", "bFire", "bAltFire", "bCrouch", "bRoll",
        "bUsing", "bFireMode", "bUseBackToWall", "bReload", "bROF", "bHoldBreath", "bSpecialsChat", "bZoomIn", "bZoomOut",
    ];

    private readonly byte[] _buttons = new byte[ButtonNames.Count];

    /// <summary>Mouse counts this tick, right positive.</summary>
    public float MouseX { get; set; }

    /// <summary>Mouse counts this tick, up positive (the axis is already flipped from screen space).</summary>
    public float MouseY { get; set; }

    /// <summary>The aUp axis (the Jump alias adds +280 while held).</summary>
    public float Up { get; set; }

    /// <summary>Exec commands pressed this tick, in order.</summary>
    public List<InputCommand> Commands { get; } = new();

    /// <summary>Mouse wheel steps this tick, up positive (the free camera's speed).</summary>
    public int Wheel { get; set; }

    /// <summary>Shift is held (the free camera flies fast).</summary>
    public bool Fast { get; set; }

    /// <summary>Alt is held (the free camera flies slowly).</summary>
    public bool Slow { get; set; }

    /// <summary>
    /// The controller's left stick (rl:input.pad), forward and right positive, -1 to 1; used instead of the direction
    /// keys while <see cref="PadStick"/> is set (full circle movement and analog slow walk).
    /// </summary>
    public float PadForward { get; set; }

    /// <inheritdoc cref="PadForward"/>
    public float PadStrafe { get; set; }

    /// <summary>The stick is pushed past its deadzone this frame.</summary>
    public bool PadStick { get; set; }

    /// <summary>Sets or (with a zero stick) clears the analog stick.</summary>
    public void SetPadStick(float forward, float strafe)
    {
        PadForward = forward;
        PadStrafe = strafe;
        PadStick = forward != 0f || strafe != 0f;
    }

    /// <summary>The pad's triggers for the free camera: right minus left, -1 to 1 (up positive).</summary>
    public float PadUp { get; set; }

    /// <summary>The index of a button name, or -1.</summary>
    public static int IndexOf(string button)
    {
        for (int i = 0; i < ButtonNames.Count; i++)
        {
            if (string.Equals(ButtonNames[i], button, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public byte this[int index]
    {
        get => _buttons[index];
        set => _buttons[index] = value;
    }

    /// <summary>Sets a button by name; false for a name the controller does not have.</summary>
    public bool Set(string button, bool down)
    {
        int i = IndexOf(button);
        if (i < 0)
        {
            return false;
        }

        _buttons[i] = down ? (byte)1 : (byte)0;
        return true;
    }

    public byte Get(string button) => IndexOf(button) is var i and >= 0 ? _buttons[i] : (byte)0;

    /// <summary>Clears the per-tick parts (mouse and commands); held buttons stay.</summary>
    public void EndTick()
    {
        MouseX = 0f;
        MouseY = 0f;
        Wheel = 0;
        Commands.Clear();
    }

    /// <summary>Releases everything.</summary>
    public void Clear()
    {
        Array.Clear(_buttons);
        Up = 0f;
        Fast = false;
        Slow = false;
        SetPadStick(0f, 0f);
        PadUp = 0f;
        EndTick();
    }

    /// <summary>Copies the input bytes onto the controller.</summary>
    public void ApplyButtons(SPlayerController pc)
    {
        ArgumentNullException.ThrowIfNull(pc);
        pc.BT_Up = _buttons[0];
        pc.BT_Down = _buttons[1];
        pc.BT_Left = _buttons[2];
        pc.BT_Right = _buttons[3];
        pc.BT_PrevItem = _buttons[4];
        pc.BT_NextItem = _buttons[5];
        pc.bFire = _buttons[6];
        pc.bAltFire = _buttons[7];
        pc.bCrouch = _buttons[8];
        pc.bRoll = _buttons[9];
        pc.bUsing = _buttons[10];
        pc.bFireMode = _buttons[11];
        pc.bUseBackToWall = _buttons[12];
        pc.bReload = _buttons[13];
        pc.bROF = _buttons[14];
        pc.bHoldBreath = _buttons[15];
        pc.bSpecialsChat = _buttons[16];
        pc.bZoomIn = _buttons[17];
        pc.bZoomOut = _buttons[18];
        pc.bPadStickMove = PadStick;
        pc.PadStickForward = PadForward;
        pc.PadStickStrafe = PadStrafe;
    }
}

/// <summary>
/// Runs the exec commands of the original key layouts on the local controller (the functions the input aliases call:
/// Fire, Jump, GadgetQuickChoice, SelectGadget, SwitchHeadset...). Commands the port does not have are reported.
/// </summary>
public static class PlayerCommands
{
    /// <summary>The commands <see cref="Execute"/> knows.</summary>
    public static readonly IReadOnlyList<string> Known =
    [
        "Fire", "AltFire", "Jump", "GadgetQuickChoice", "LaunchGadget", "LongRoll", "SelectGadget", "SwitchHeadset", "SwitchBinocularVision",
        "SetWalk", "SetScope", "Walk", "Run", "NextGadget", "PreviousGadget", "ChangeSpeed", "GadLaserSwitch", "GadTorcheSwitch", "SwitchLaser", "WheelUp", "WheelDown", "ZoomIn", "ZoomOut",
        "SwapWeapon", "SwitchROF", "PrevStickyCam", "PerformCoopMvt", "FreeCam", "HideHud",
        "PadCloakQuick", "PadCameraGas", "PadCameraExit",
    ];

    /// <summary>
    /// Runs <paramref name="command"/> on <paramref name="pc"/>; false when the port has no such command. While the
    /// cinematic free camera flies only FreeCam and HideHud run: the pawn takes no orders (the other commands are
    /// swallowed).
    /// </summary>
    public static bool Execute(SPlayerController pc, InputCommand command)
    {
        ArgumentNullException.ThrowIfNull(pc);
        string name = command.Name.ToUpperInvariant();
        if (pc.FreeCameraFlying != 0 && name is not ("FREECAM" or "HIDEHUD"))
        {
            return true;
        }

        switch (name)
        {
            case "FREECAM":
                pc.FreeCam();
                return true;
            case "HIDEHUD":
                pc.HideHud();
                return true;
            case "FIRE":
                pc.eventFire(0f);
                return true;
            case "ALTFIRE":
                pc.eventAltFire(0f);
                return true;
            case "JUMP":
                pc.eventJump();
                return true;
            case "GADGETQUICKCHOICE":
                pc.eventGadgetQuickChoice(command.Arg);
                return true;
            case "LAUNCHGADGET":
                LaunchGadget(pc, command.Arg);
                return true;
            case "LONGROLL":
                // Reloaded 0x100b9df0: pulse crouch for one tick before the held roll, to leave crouch.
                // ApplyButtons clears this pulse next tick; bRoll is held independently by the action binding.
                if (pc.bCrouchWithRoll && !InCamera(pc)) pc.bCrouch = 1;
                return true;
            case "PADCLOAKQUICK":
                if (pc.SBPawn?.SelectedGadget is SGadCloak cloak) cloak.QuickUse();
                return true;
            case "PADCAMERAGAS":
                if ((InCamera(pc) || pc.IsInState("s_UseObject_StickyCam_SmoothToPosition")) && pc.SBPawn?.UsedObject is SStickyCam cam) cam.SpawnGaz();
                return true;
            case "PADCAMERAEXIT":
                if (InCamera(pc)) pc.eventAltFire(0f);
                return true;
            case "SELECTGADGET":
                pc.SelectGadget(command.Arg);
                return true;
            case "SWITCHHEADSET":
                pc.eventSwitchHeadset(command.Arg);
                return true;
            case "SWITCHBINOCULARVISION":
                pc.eventSwitchBinocularVision();
                return true;
            case "SETWALK":
                pc.bFastSpeed = command.Arg == 0;
                return true;
            case "WALK":
                pc.bFastSpeed = !pc.bFastSpeed;
                return true;
            case "RUN":
                if (pc.StateNode is SPlayerController.IWheel) pc.eventWheelUp();
                else pc.bFastSpeed = true;
                return true;
            case "SETSCOPE":
                if (pc.SBPawn is SPAWNDEFENSE && pc.bBinocularVision != (command.Arg != 0))
                {
                    if (command.Arg != 0) pc.eventSwitchBinocularVision();
                    else pc.bBinocularVision = false;
                }
                return true;
            case "NEXTGADGET":
            case "PREVIOUSGADGET":
                if (pc.StateNode is SPlayerController.IWheel)
                {
                    if (name == "NEXTGADGET") pc.eventWheelUp(); else pc.eventWheelDown();
                }
                else if (pc.SBPawn is { } gadgetPawn)
                {
                    if (name == "NEXTGADGET")
                    {
                        OpenScct.Game.Equipment.PawnInventory.NextGadget(gadgetPawn);
                        gadgetPawn.SelectedGadget?.Load();
                    }
                    else OpenScct.Game.Equipment.PawnInventory.PrevGadget(gadgetPawn);
                }
                return true;
            case "CHANGESPEED":
                pc.ChangeSpeed();
                return true;
            case "GADLASERSWITCH":
                pc.eventGadLaserSwitch();
                return true;
            case "GADTORCHESWITCH":
                pc.eventGadTorcheSwitch();
                return true;
            case "SWITCHLASER":
                pc.eventSwitchLaser();
                return true;
            case "WHEELUP":
                pc.eventWheelUp();
                return true;
            case "WHEELDOWN":
                pc.eventWheelDown();
                return true;
            case "ZOOMIN":
                pc.eventZoomIn();
                return true;
            case "ZOOMOUT":
                pc.eventZoomOut();
                return true;
            case "SWAPWEAPON":
                pc.SwapWeapon();
                return true;
            case "SWITCHROF":
                pc.eventSwitchROF();
                return true;
            case "PREVSTICKYCAM":
                pc.eventPrevStickyCam();
                return true;
            case "PERFORMCOOPMVT":
                pc.eventPerformCoopMvt();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Reloaded 0x100b0750: select and fire a slot with the Shocker drawn, including its sticky-cam path.</summary>
    private static void LaunchGadget(SPlayerController pc, int slot)
    {
        if (slot is < 0 or > 3 || pc.SBPawn is not SPawnAttaque { WeaponLoaded: 2 } spy || InCamera(pc)) return;
        pc.SelectGadget(slot);
        if (spy.SelectedGadget is SGadStickyCam sticky)
        {
            if (sticky.Charge == 0)
                pc.PlayOwnedSound(sticky.SND_EmptyCharge, ESoundSlot.SLOT_Misc, 1f, false, 1f, 1f, false);
            else sticky.UseStickyCam(false);
        }
        else pc.eventAltFire(0f);
    }

    public static bool InCamera(SPlayerController pc) =>
        pc.IsInState("s_UseObject_StickyCam") || pc.IsInState("s_UseObject_CamNetwork");
}
