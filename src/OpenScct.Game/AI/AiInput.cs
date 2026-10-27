using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;

namespace OpenScct.Game.AI;

/// <summary>An exec command a key press runs (the functions the original's input aliases call).</summary>
public enum AiCommand : byte
{
    None = 0,
    Fire,
    AltFire,
    Jump,

    /// <summary>GadgetQuickChoice(Arg): a spy's quick launch of gadget slot Arg.</summary>
    GadgetQuickChoice,

    /// <summary>SelectGadget(Arg): gadget slot 0 to 3.</summary>
    SelectGadget,

    /// <summary>SwitchHeadset(Arg): 1 and 2 the team's vision modes, 3 cycles.</summary>
    SwitchHeadset,

    GadTorcheSwitch,

    /// <summary>The co-op key (exec PerformCoopMvt, T): offer or take a boost or a hoist.</summary>
    PerformCoopMvt,
}

/// <summary>
/// One tick of a bot's input, in the shape a player's input leaves on the controller: the input bytes the keys hold
/// (BT_Up, bCrouch, bUsing, bFire...), the view rotation the mouse produced, and up to two exec commands pressed this
/// tick. <see cref="ApplyTo"/> writes it on the controller before the world ticks, as the client's input layer does,
/// so the bot moves and acts only through the rules a human player's input goes through (NativeComputeControlVariables
/// and the state code).
/// </summary>
public struct AiInput
{
    public byte BT_Up;
    public byte BT_Down;
    public byte BT_Left;
    public byte BT_Right;

    /// <summary>The crouch key (a toggle: press one tick, release the next).</summary>
    public byte bCrouch;

    public byte bRoll;
    public byte bUsing;
    public byte bFire;
    public byte bAltFire;
    public byte bFireMode;
    public byte bReload;
    public byte bUseBackToWall;

    /// <summary>The run toggle (the spy's walk and run speed mode).</summary>
    public bool FastSpeed;

    /// <summary>The view (controller rotation) after this tick's mouse movement, when set.</summary>
    public FRotator? View;

    /// <summary>
    /// The aim (MyDesiredRotation and LocalMyDesiredRotation, what the rifle fires along), when set. A player's mouse
    /// moves it with the view; a bot sets it with the same turn limits.
    /// </summary>
    public FRotator? Aim;

    public AiCommand Command1;
    public int Arg1;
    public AiCommand Command2;
    public int Arg2;

    public static AiInput Idle => default;

    /// <summary>Queues an exec command (two per tick at most; a third is dropped).</summary>
    public void Press(AiCommand command, int arg = 0)
    {
        if (Command1 == AiCommand.None)
        {
            Command1 = command;
            Arg1 = arg;
        }
        else if (Command2 == AiCommand.None)
        {
            Command2 = command;
            Arg2 = arg;
        }
    }

    /// <summary>True when any movement key is held.</summary>
    public readonly bool Moving => (BT_Up | BT_Down | BT_Left | BT_Right) != 0;

    /// <summary>Writes the input on <paramref name="pc"/> and runs the commands, as the input layer would.</summary>
    public readonly void ApplyTo(SPlayerController pc)
    {
        pc.BT_Up = BT_Up;
        pc.BT_Down = BT_Down;
        pc.BT_Left = BT_Left;
        pc.BT_Right = BT_Right;
        pc.bCrouch = bCrouch;
        pc.bRoll = bRoll;
        pc.bUsing = bUsing;
        pc.bFire = bFire;
        pc.bAltFire = bAltFire;
        pc.bFireMode = bFireMode;
        pc.bReload = bReload;
        pc.bUseBackToWall = bUseBackToWall;
        pc.bFastSpeed = FastSpeed;
        pc.aTurn = 0f;
        pc.aLookUp = 0f;

        // A sticky shock freezes the mouse (NativeComputeControlVariables with iDontUpdatePadInput): the bot's view
        // and aim, which stand for its mouse, stay where they are.
        bool frozen = pc.iDontUpdatePadInput != 0;
        if (View is { } view && !frozen)
        {
            pc.Rotation = view;
        }

        if (Aim is { } aim && !frozen)
        {
            pc.MyDesiredRotation = aim;
            pc.LocalMyDesiredRotation = aim;
        }

        Run(pc, Command1, Arg1);
        Run(pc, Command2, Arg2);
    }

    private static void Run(SPlayerController pc, AiCommand command, int arg)
    {
        switch (command)
        {
            case AiCommand.Fire:
                pc.eventFire(0f);
                break;
            case AiCommand.AltFire:
                pc.eventAltFire(0f);
                break;
            case AiCommand.Jump:
                pc.eventJump();
                break;
            case AiCommand.GadgetQuickChoice:
                pc.eventGadgetQuickChoice(arg);
                break;
            case AiCommand.SelectGadget:
                pc.SelectGadget(arg);
                break;
            case AiCommand.SwitchHeadset:
                pc.eventSwitchHeadset(arg);
                break;
            case AiCommand.GadTorcheSwitch:
                pc.eventGadTorcheSwitch();
                break;
            case AiCommand.PerformCoopMvt:
                pc.eventPerformCoopMvt();
                break;
        }
    }
}

/// <summary>
/// The player object of a server-side bot: a local viewport (UE2 UViewport) without a screen, so the bot's controller
/// is a player controlled on the server's machine. Its PlayerTick runs the input and movement code there, as for a
/// listen server's own player. It is never the level's LocalPlayerController.
/// </summary>
public sealed class AiViewport : UViewport
{
}
