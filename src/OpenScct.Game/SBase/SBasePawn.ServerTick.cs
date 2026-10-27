using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SBasePawn on the server for a remote player: the part of ASBasePawn::Tick that replaces the engine's stalled
// move catch-up. Written from spec/native/net-moves.md rule 18.
public partial class SBasePawn
{
    /// <summary>Seconds without a move after which a falling remote pawn's physics catches up (rule 18).</summary>
    [Provenance(ProvenanceKind.Decoded, "ASBasePawn::Tick editor 0x11205fb0: 0.1 compared with Level.TimeSeconds - ServerTimeStamp")]
    public const float FallingCatchUpDelay = 0.1f;

    /// <summary>
    /// ASBasePawn::Tick for a pawn whose remote player sends its moves (rule 18): the client sends no move in root
    /// motion or while following a parent, so the server runs those (and a repositioning it controls) itself every
    /// tick for the time since the last move; a falling pawn whose moves stopped for more than 0.1 s falls on for the
    /// elapsed time, and that time counts as processed (CurrentTimeStamp). Other modes wait for their moves.
    /// </summary>
    [UcNative("ASBasePawn::Tick", Editor = 0x11205fb0)]
    [PortOf("eng:ASBasePawn::Tick", Spec = "spec/native/net-moves.md#rules")]
    protected override void CatchUpRemoteAutonomous(PlayerController pc)
    {
        float now = XLevel.TimeSeconds;
        float elapsed = now - pc.ServerTimeStamp;
        if (Physics is EPhysics.PHYS_RootMotion or EPhysics.PHYS_FollowParent ||
            (Physics == EPhysics.PHYS_Repositionnal && bServerControlsRepos))
        {
            XLevel.PhysicsDriver?.performPhysics(this, elapsed);
            pc.ServerTimeStamp = now;
        }
        else if (Physics == EPhysics.PHYS_Falling && elapsed > FallingCatchUpDelay && pc.ServerTimeStamp != 0f)
        {
            XLevel.PhysicsDriver?.performPhysics(this, elapsed);
            pc.CurrentTimeStamp += elapsed;
            pc.ServerTimeStamp = now;
        }
    }
}
