using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController GE and geometry natives the movement states call. They run in the level's physics driver
// (spec/native/physics-ge-queries.md, docs/PHYSICS.md); the zone searches raise the GE events on this controller.
//
// A test whose move is not ported in the script yet (barrier vault, wall jump, split jump, the merc grab from the
// hang) reports nothing, so the player keeps the move he was making instead of entering a missing state.
public partial class SPlayerController
{
    /// <summary>The arms GE search (raises at most one GE event).</summary>
    [UcNative("ASPlayerController::execCheckArmsZone", Exe = 0x10aa54d0, Editor = 0x111f2330)]
    [PortOf("nat:SBase.SPlayerController.CheckArmsZone", Spec = "spec/native/physics-ge-queries.md#3-checkarmszone")]
    public virtual void CheckArmsZone()
    {
        if (SBPawn is { } pawn)
        {
            MovementNatives.Driver(this)?.CheckArmsZone(pawn);
        }
    }

    /// <summary>The feet GE search (TopPipe, TopLadder, FeetLedge).</summary>
    [UcNative("ASPlayerController::execCheckFeetZone", Exe = 0x10aa7a80, Editor = 0x111f26c0)]
    [PortOf("nat:SBase.SPlayerController.CheckFeetZone", Spec = "spec/native/physics-ge-queries.md#6-checkfeetzone")]
    public virtual void CheckFeetZone()
    {
        if (SBPawn is { } pawn)
        {
            MovementNatives.Driver(this)?.CheckFeetZone(pawn);
        }
    }

    /// <summary>A ledge behind the feet; fills ZipP and vBackFeetNormal.</summary>
    [UcNative("ASPlayerController::execCheckBackFeetZone", Exe = 0x10aa7e00, Editor = 0x111ec6d0)]
    [PortOf("nat:SBase.SPlayerController.CheckBackFeetZone", Spec = "spec/native/physics-ge-queries.md#7-checkbackfeetzone---bool")]
    public bool CheckBackFeetZone() =>
        SBPawn is { } pawn && MovementNatives.Driver(this) is { } driver && driver.CheckBackFeetZone(pawn);

    /// <summary>The room above a hung ledge: 1 stand, 2 crouch, 3 vault over, 4 barrier above, 0 none.</summary>
    [UcNative("ASPlayerController::execTestTopLedge", Exe = 0x10aac030, Editor = 0x111e6920)]
    [PortOf("nat:SBase.SPlayerController.TestTopLedge", Spec = "spec/native/physics-ge-queries.md#8-testtopledgebcrouchonly---int")]
    public int TestTopLedge(bool bCrouchOnly) =>
        SBPawn is { } pawn && MovementNatives.Driver(this) is { } driver ? driver.TestTopLedge(pawn, bCrouchOnly) : 0;

    /// <summary>The barrier vault test; reports nothing until the barrier states are ported.</summary>
    [UcNative("ASPlayerController::execCheckBarrier", Exe = 0x10aa5390, Editor = 0x111ea600)]
    [PortOf("nat:SBase.SPlayerController.CheckBarrier", Spec = "spec/native/physics-ge-queries.md#11-checkbarrier---bool")]
    public bool CheckBarrier() =>
        GetStateTable().Find(MovementStateNames.s_BarrierJumpDetect) is not null && SBPawn is { } pawn &&
        MovementNatives.Driver(this) is { } driver && driver.CheckBarrier(pawn);

    /// <summary>
    /// The wall jump test of a running jump: a wall ahead and the ledge GE above it (see the physics driver). Reloaded's
    /// wrapper first puts fGEZLocation back to its default -10, so a ledge height left from an earlier wall jump never
    /// carries into this one.
    /// </summary>
    [UcNative("ASPlayerController::execCheckGEForWallJump", Exe = 0x10aa6250, Editor = 0x111eaf20)]
    [PortOf("nat:SBase.SPlayerController.CheckGEForWallJump", Spec = "spec/native/natives2-traversal.md#12-checkgeforwalljump-room-boxes-completes-physics-ge-queriesmd-rule-12")]
    [PortOf("rl:SBase.SPlayerController.CheckGEForWallJump", Spec = "spec/native/reloaded-hooks.md#gameplay")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x10023930: fGEZLocation (+0xb50) = -10.0 (0xc1200000) before the original native")]
    public bool CheckGEForWallJump(FVector X, FVector Y, FVector Z, out float GEZPosition)
    {
        fGEZLocation = MovementK.ReloadedWallJumpGEZPreset;
        GEZPosition = 0f;
        return SBPawn is { } pawn && MovementNatives.Driver(this) is { } driver && driver.CheckGEForWallJump(pawn, X, Y, Z, out GEZPosition);
    }

    /// <summary>
    /// The split jump wall test (see the physics driver). <paramref name="HitNormal"/> is written only when a wall is
    /// found, as the script's out parameter.
    /// </summary>
    [UcNative("ASPlayerController::execCheckWallJump", Exe = 0x10aa5a10, Editor = 0x111ea780)]
    [PortOf("nat:SBase.SPlayerController.CheckWallJump", Spec = "spec/native/natives2-traversal.md#11-checkwalljump-helpers-completes-physics-ge-queriesmd-rule-12")]
    public bool CheckWallJump(FVector X, FVector Y, FVector Z, ref FVector HitNormal) =>
        SBPawn is { } pawn && MovementNatives.Driver(this) is { } driver && driver.CheckWallJump(pawn, X, Y, Z, ref HitNormal);
}
