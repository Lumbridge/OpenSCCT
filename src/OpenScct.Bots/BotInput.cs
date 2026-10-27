using OpenScct.Core;
using OpenScct.Game.SBase;

namespace OpenScct.Bots;

/// <summary>
/// One frame of a bot's input, the shape of the movement port's <c>PlayerInputFrame</c>: the controller's input
/// bytes, the view written before the frame, and the fire and crouch presses. <see cref="ApplyTo"/> writes it on the
/// local player's controller before the client's world ticks, as the input layer would.
/// </summary>
public struct BotInput
{
    public byte BT_Up;
    public byte BT_Down;
    public byte BT_Left;
    public byte BT_Right;

    /// <summary>The crouch button (a toggle: hold one frame, release the next).</summary>
    public byte bCrouch;

    /// <summary>The Use button (held opens the Use menu, released acts on its entry).</summary>
    public byte bUsing;

    /// <summary>The fire button held (bFire); <see cref="FirePressed"/> also calls the Fire exec this frame.</summary>
    public bool FireHeld;

    public bool FirePressed;

    public bool FastSpeed;

    /// <summary>The view (controller rotation) for this frame, when set; a merc's aim turns with it (SetViewRotation).</summary>
    public FRotator? View;

    /// <summary>
    /// The aim for this frame, when set: the controller's desired rotation (MyDesiredRotation and
    /// LocalMyDesiredRotation), which the mouse moves and the rifle fires along; a bot sets it at once.
    /// </summary>
    public FRotator? Aim;

    public static BotInput Idle => default;

    /// <summary>Writes the input on <paramref name="pc"/>.</summary>
    public readonly void ApplyTo(SPlayerController pc)
    {
        pc.BT_Up = BT_Up;
        pc.BT_Down = BT_Down;
        pc.BT_Left = BT_Left;
        pc.BT_Right = BT_Right;
        pc.bCrouch = bCrouch;
        pc.bUsing = bUsing;
        pc.bFastSpeed = FastSpeed;
        pc.aTurn = 0f;
        pc.aLookUp = 0f;
        if (View is { } view)
        {
            // A merc's view follows his aim every frame (the mouse moves MyDesiredRotation, UpdateRotationAttaquant_Mouse),
            // so a turn written on Rotation alone is undone before the move: SetViewRotation turns the aim too.
            pc.SetViewRotation(view);
        }

        if (Aim is { } aim)
        {
            pc.MyDesiredRotation = aim;
            pc.LocalMyDesiredRotation = aim;
        }

        pc.bFire = (byte)(FireHeld ? 1 : 0);
        if (FirePressed)
        {
            pc.eventFire(1f);
        }
    }
}
