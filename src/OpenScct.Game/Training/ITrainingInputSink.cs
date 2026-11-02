using System;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Training;

public enum TrainingInputSource : byte { Remote, Bot, Local }
public enum TrainingInputKind : byte { Movement, Buttons }
public enum TrainingActionKind : byte { Native, Exec }

/// <summary>Stable input names, independent of engine property offsets. Null means buttons were not transmitted.</summary>
[Flags]
public enum TrainingButtons : uint
{
    None = 0, Forward = 1 << 0, Back = 1 << 1, Left = 1 << 2, Right = 1 << 3,
    PreviousItem = 1 << 4, NextItem = 1 << 5, Fire = 1 << 6, AltFire = 1 << 7,
    Crouch = 1 << 8, Roll = 1 << 9, Use = 1 << 10, FireMode = 1 << 11,
    BackToWall = 1 << 12, Reload = 1 << 13, RateOfFire = 1 << 14, HoldBreath = 1 << 15,
    SpecialsChat = 1 << 16, ZoomIn = 1 << 17, ZoomOut = 1 << 18, FastSpeed = 1 << 19,
}

/// <summary>
/// Applied input, before simulation consumes it. All fields are primitives; missing wire data remains null.
/// Movement acceleration is in game units, not the protocol's scaled integer representation.
/// Angles retain the game's units (65536 per turn). ServerTick identifies the authority step, InputTick the
/// client's command clock; they can differ while the server catches up. This is not a raw packet log.
/// </summary>
public readonly record struct TrainingInput
{
    public TrainingInputSource Source { get; init; }
    public TrainingInputKind Kind { get; init; }
    public long ServerTick { get; init; }
    public long? InputTick { get; init; }
    public uint? Sequence { get; init; }
    public uint? Epoch { get; init; }
    public float? ClientTimeSeconds { get; init; }
    public float? DeltaSeconds { get; init; }
    public float? AccelerationX { get; init; }
    public float? AccelerationY { get; init; }
    public float? AccelerationZ { get; init; }
    public int? ViewPitch { get; init; }
    public int? ViewYaw { get; init; }
    public int? ViewRoll { get; init; }
    public int? AimPitch { get; init; }
    public int? AimYaw { get; init; }
    public int? AimRoll { get; init; }
    public TrainingButtons? Buttons { get; init; }
    public byte? MovementFlags { get; init; }
    public bool? Jump { get; init; }
    public float? MouseDeltaX { get; init; }
    public float? MouseDeltaY { get; init; }
    public float? UpAxis { get; init; }
    public float? AnalogForward { get; init; }
    public float? AnalogStrafe { get; init; }
}

/// <summary>
/// A native action passed to the pawn after the server's admission checks, or a known local/bot exec dispatched
/// to its controller. Dispatch does not assert that the game allowed an effect (for example firing without ammo).
/// Outcomes belong to the separate gameplay events. No engine object is serialized in this record.
/// </summary>
public readonly record struct TrainingAction
{
    public TrainingInputSource Source { get; init; }
    public TrainingActionKind Kind { get; init; }
    public long ServerTick { get; init; }
    public long? InputTick { get; init; }
    public uint? Sequence { get; init; }
    public uint? Epoch { get; init; }
    public float? ClientTimeSeconds { get; init; }
    public int? ActionCode { get; init; }
    public string? Command { get; init; }
    public int? Argument { get; init; }
}

/// <summary>
/// Optional authority-level recording tap. The controller is supplied only for mapping to stable recording IDs;
/// implementations must serialize the primitive records, never the controller. Sink failures must be contained
/// by the implementation so recording cannot alter gameplay. No sink is installed during ordinary play.
/// </summary>
public interface ITrainingInputSink
{
    void RecordInput(SPlayerController controller, in TrainingInput input);
    void RecordAction(SPlayerController controller, in TrainingAction action);
}

public static class TrainingInputCapture
{
    /// <summary>Reads local/bot buttons after assignment, before execs and movement consume them.</summary>
    public static TrainingInput Buttons(SPlayerController pc, TrainingInputSource source) => new()
    {
        Source = source, Kind = TrainingInputKind.Buttons,
        ServerTick = pc.XLevel.TickCount + (pc.XLevel.IsTicking ? 0 : 1),
        ViewPitch = pc.Rotation.Pitch, ViewYaw = pc.Rotation.Yaw, ViewRoll = pc.Rotation.Roll,
        AimPitch = pc.MyDesiredRotation.Pitch, AimYaw = pc.MyDesiredRotation.Yaw, AimRoll = pc.MyDesiredRotation.Roll,
        Buttons = ButtonsOf(pc), AnalogForward = pc.bPadStickMove ? pc.PadStickForward : null,
        AnalogStrafe = pc.bPadStickMove ? pc.PadStickStrafe : null,
    };

    private static TrainingButtons ButtonsOf(SPlayerController pc)
    {
        TrainingButtons result = TrainingButtons.None;
        if (pc.BT_Up != 0) result |= TrainingButtons.Forward;
        if (pc.BT_Down != 0) result |= TrainingButtons.Back;
        if (pc.BT_Left != 0) result |= TrainingButtons.Left;
        if (pc.BT_Right != 0) result |= TrainingButtons.Right;
        if (pc.BT_PrevItem != 0) result |= TrainingButtons.PreviousItem;
        if (pc.BT_NextItem != 0) result |= TrainingButtons.NextItem;
        if (pc.bFire != 0) result |= TrainingButtons.Fire;
        if (pc.bAltFire != 0) result |= TrainingButtons.AltFire;
        if (pc.bCrouch != 0) result |= TrainingButtons.Crouch;
        if (pc.bRoll != 0) result |= TrainingButtons.Roll;
        if (pc.bUsing != 0) result |= TrainingButtons.Use;
        if (pc.bFireMode != 0) result |= TrainingButtons.FireMode;
        if (pc.bUseBackToWall != 0) result |= TrainingButtons.BackToWall;
        if (pc.bReload != 0) result |= TrainingButtons.Reload;
        if (pc.bROF != 0) result |= TrainingButtons.RateOfFire;
        if (pc.bHoldBreath != 0) result |= TrainingButtons.HoldBreath;
        if (pc.bSpecialsChat != 0) result |= TrainingButtons.SpecialsChat;
        if (pc.bZoomIn != 0) result |= TrainingButtons.ZoomIn;
        if (pc.bZoomOut != 0) result |= TrainingButtons.ZoomOut;
        if (pc.bFastSpeed) result |= TrainingButtons.FastSpeed;
        return result;
    }
}
