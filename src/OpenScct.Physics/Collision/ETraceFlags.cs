using System;
using OpenScct.Provenance;

namespace OpenScct.Physics.Collision;

/// <summary>
/// Trace flags (UE2.5 ETraceFlags) with the bit meanings of this build, decoded from the checks that read them
/// (spec/native/collision-traces.md). The physics natives pass the masks in <see cref="K.Flags"/>. One channel bit at
/// the top is an SVM addition for the bullet pass-through the original keeps on the hit.
/// </summary>
[Flags]
[Provenance(ProvenanceKind.Decoded, "Readers of the flags: SingleLineCheck 0x110be450, ShouldTrace 0x110ac870 / 0x110dc370 (editor)")]
public enum ETraceFlags : uint
{
    None = 0,

    /// <summary>Pawns (APawn::ShouldTrace returns this bit).</summary>
    TRACE_Pawns = 0x01,

    /// <summary>Movers (a hash bit; movers answer to it).</summary>
    TRACE_Movers = 0x02,

    /// <summary>The level's BSP.</summary>
    TRACE_Level = 0x04,

    /// <summary>Volumes (a hash bit; volumes answer to it).</summary>
    TRACE_Volumes = 0x08,

    /// <summary>Non-pawn, non-world actors (glass, gameplay objects, attached collision, decorations).</summary>
    TRACE_Others = 0x10,

    /// <summary>With Others: only bProjTarget actors, or those with bBlockActors and bBlockPlayers.</summary>
    TRACE_OnlyProjActor = 0x20,

    /// <summary>With Others and for pawns: only what blocks the source (IsBlockedBy).</summary>
    TRACE_Blocking = 0x40,

    /// <summary>bWorldGeometry actors (static meshes, blocking brushes); a pawn source sees a static mesh only with bBlockPlayers.</summary>
    TRACE_LevelGeometry = 0x80,

    /// <summary>Only bShadowCast actors; static meshes use their per-poly triangles.</summary>
    TRACE_ShadowCast = 0x100,

    /// <summary>Stop at the BSP hit or the first actor hit (clearance tests).</summary>
    TRACE_StopAtFirstHit = 0x200,

    /// <summary>Added by SingleLineCheck unless <see cref="TRACE_Sight"/> is set.</summary>
    TRACE_SingleResult = 0x400,

    /// <summary>Report the BSP surface material (always reported here).</summary>
    TRACE_Material = 0x1000,

    /// <summary>Actors drawn as a static mesh or mesh count.</summary>
    TRACE_VisibleMesh = 0x2000,

    /// <summary>Only bAcceptsProjectors actors.</summary>
    TRACE_AcceptProjectors = 0x8000,

    /// <summary>Camera traces: the hit actor needs bBlockCamera (colliders: blockCamera).</summary>
    TRACE_Camera = 0x10000,

    /// <summary>Sight traces: SingleLineCheck does not add SingleResult.</summary>
    TRACE_Sight = 0x20000,

    /// <summary>With ShadowCast: SingleLineCheck skips BSP hits on backdrop surfaces.</summary>
    TRACE_BackdropSkip = 0x80000,

    TRACE_Hash = TRACE_Pawns | TRACE_Movers | TRACE_Volumes | TRACE_Others | TRACE_LevelGeometry,
    TRACE_Actors = TRACE_Pawns | TRACE_Movers | TRACE_Others | TRACE_LevelGeometry,
    TRACE_AllColliding = TRACE_Level | TRACE_Actors,
    TRACE_AllBlocking = TRACE_Blocking | TRACE_AllColliding,
    TRACE_World = TRACE_Level | TRACE_Movers | TRACE_LevelGeometry,
    TRACE_Everything = TRACE_AllColliding | TRACE_Volumes,
    TRACE_ProjTargets = TRACE_OnlyProjActor | TRACE_AllColliding,

    /// <summary>SVM channel: a bullet trace, which passes colliders flagged bulletGoesThrough (bBulletGoTru).</summary>
    [Provenance(ProvenanceKind.Estimated, "The original reports such hits through the FCheckResult pass-through word (BulletWentTru); the fire trace flags belong to the combat spec")]
    TRACE_Bullet = 0x0200_0000,
}
