using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SBasePawn co-op natives: the short-ladder spot and the hoist climb type (spec/native/ANSWERS-moveset.md M3
// to M5). The geometry is in the physics driver.
public partial class SBasePawn
{
    /// <summary>
    /// A boost spot under a ledge 502 to 512 above the feet: Loc where the helper stands, Rot the direction away from
    /// the wall (a vector, as SetInfoForVandamme takes it). False when there is none.
    /// </summary>
    [UcNative("ASBasePawn::execCheckIfCanStartSHL", Exe = 0x10ae6ff0, Editor = 0x112125a0)]
    [Uc("SBase.SBasePawn.CheckIfCanStartSHL", Sha1 = "e5dc479bb67c66670348a486d77d0b9294d2367c")]
    [PortOf("nat:SBase.SBasePawn.CheckIfCanStartSHL", Spec = "spec/native/ANSWERS-moveset.md#m3-checkifcanstartshlout-loc-out-dir---bool-editor-exec-0x112125a0-body-0x112122e0")]
    public bool CheckIfCanStartSHL(out FVector Loc, out FVector Rot)
    {
        Loc = default;
        Rot = default;
        return MovementNatives.Driver(this) is { } driver && driver.CheckIfCanStartSHL(this, false, out Loc, out Rot, out _, out _);
    }

    /// <summary>
    /// The climb under this hanging pawn: SHL_Standard, SHL_Barrier, SHL_StandardNoWall, SHL_BarrierNoWall, or
    /// SHL_None when there is no floor, no room or no way over the top.
    /// </summary>
    [UcNative("ASBasePawn::execFindHoistClimbType", Exe = 0x10ae5150, Editor = 0x11215680)]
    [Uc("SBase.SBasePawn.FindHoistClimbType", Sha1 = "140fd3c78adb8e36469d060163ddd3145f9bceb2")]
    [PortOf("nat:SBase.SBasePawn.FindHoistClimbType", Spec = "spec/native/ANSWERS-moveset.md#m5-findhoistclimbtype-editor-body-0x11211130-exec-0x11215680")]
    public byte FindHoistClimbType(bool bIgnorePawn = false, bool bQuickCheckBarier = false) =>
        MovementNatives.Driver(this) is { } driver ? driver.FindHoistClimbType(this, bIgnorePawn, bQuickCheckBarier) : (byte)SHL_None;
}
