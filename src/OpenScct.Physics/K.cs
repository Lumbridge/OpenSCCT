using OpenScct.Core;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Physics;

/// <summary>
/// Numbers the physics takes from the original, each with its evidence. The nested classes group the constants of
/// one native (spec/native/physics-*.md, collision-*.md); a nested class's <see cref="ProvenanceAttribute"/> covers
/// every constant in it unless the constant carries its own.
/// </summary>
public static partial class K
{
    // ---- collision -------------------------------------------------------------------------------------------

    /// <summary>
    /// Margin added to an encroacher's push so the pushed actor ends clear of the encroacher rather than touching it.
    /// Traces no longer use it: their pull-backs are the decoded per-primitive rules in <see cref="Pullback"/>.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Push margin of the shove; ActorEncroachmentCheck internals are not decoded")]
    public const float TraceSkin = 0.1f;

    /// <summary>
    /// How far a box may overlap a world BSP or brush triangle, or a hull, without touching it: a box sweep's hit on them
    /// counts only when the box shrunk by this much hits as well (time and normal stay the full box's), and a point
    /// check overlaps only with the shrunk box (lines and static meshes are not affected). A box exactly
    /// on a face, as the server's copy of a pawn lands when a whole-unit network location puts it against a wall on
    /// the unit grid, then neither stops on geometry behind the face nor sticks in the walking and falling wall
    /// probes, which reach exactly the pawn's radius.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "UE2's BSP box check is not decoded (our box sweeps are an estimated exact separating-axis test); a touching contact of zero overlap is not a collision")]
    public const float BoxContactSkin = 0.001f;

    /// <summary>KillZ sits this far below the lowest collision geometry when the map gives none.</summary>
    [Provenance(ProvenanceKind.Estimated, "SCCT uses LevelInfo.fDeathPawnHeight; this fallback keeps FellOutOfWorld reachable when it is 0")]
    public const float KillZBelowWorld = 1000f;

    // ---- walking and falling -------------------------------------------------------------------------------

    /// <summary>Height stepUp lifts the pawn (stock UE2 has 35).</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.u Actor const MAXSTEPHEIGHT; game .rdata 0x10c10f34 (stepUp)")]
    [Provenance(ProvenanceKind.Measured, "RE+ MapBuilderPatterns MoveLabD: a 32 step is walked, a 36 step is not")]
    public const float MAXSTEPHEIGHT = 34f;

    /// <summary>Lowest floor normal Z a pawn can walk on or land on.</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.u Actor const MINFLOORZ; physWalking and physFalling floor tests")]
    [Provenance(ProvenanceKind.Measured, "RE+ ClimbLabD: 45 degree ramps are walked up and down at full speed")]
    public const float MINFLOORZ = 0.7f;

    /// <summary>Gravity Z of pawn falling physics (hard-coded; zone gravity is not read).</summary>
    [Provenance(ProvenanceKind.Decoded, "physFalling gravity, game .rdata 0x10c10b78; def:Engine.LevelInfo DefaultGravity")]
    [Provenance(ProvenanceKind.Measured, "RE+ jump heights (124 spy, 82 merc) and fall times")]
    public const float DefaultGravity = -1500f;

    /// <summary>Ground friction of MycalcVelocity (hard-coded; the zone value is not read).</summary>
    [Provenance(ProvenanceKind.Decoded, "physWalking passes friction 8 to MycalcVelocity (spec/native/physics-walking.md 1)")]
    public const float GroundFriction = 8f;

    /// <summary>Fastest 3D speed while falling.</summary>
    [Provenance(ProvenanceKind.Decoded, "physFalling 6.25e6 squared-speed compare (spec/native/physics-falling.md 5.7)")]
    [Provenance(ProvenanceKind.Measured, "RE+: 2110 uu/s after a 1500 drop")]
    public const float TerminalVelocity = 2500f;

    /// <summary>Longest physics sub-step of walking and falling (a longer remainder is halved).</summary>
    [Provenance(ProvenanceKind.Decoded, "physWalking / physFalling tick = min(0.05, T / 2) when T > 0.05")]
    public const float MaxSubStep = 0.05f;

    /// <summary>Walking and falling sub-step loops run while Iterations is below this.</summary>
    [Provenance(ProvenanceKind.Decoded, "physWalking / physFalling loop condition Iterations < 8")]
    public const int MaxPhysicsIterations = 8;

    /// <summary>Braking strength of MycalcVelocity with no acceleration (V -= 2 V f h).</summary>
    [Provenance(ProvenanceKind.Decoded, "MycalcVelocity braking (spec/native/physics-walking.md 2.1)")]
    public const float BrakingFactor = 2f;

    /// <summary>Hit normals with |Z| below this are near-vertical walls for stepUp.</summary>
    [Provenance(ProvenanceKind.Decoded, "APawn::stepUp near-vertical wall test (spec/native/physics-walking.md 5)")]
    public const float StepUpWallZ = 0.08f;

    // ---- traversal (GE) ------------------------------------------------------------------------------------

    /// <summary>Extent of the two box traces that must hit nothing before a Ledge event.</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10aa4420 CheckLedgeArms (spec/native/physics-ge-queries.md 4)")]
    public static readonly FVector LedgeClearanceExtent = new(35f, 35f, 87.5f);

    /// <summary>Trace flags of the ledge clearance traces.</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10aa4420 CheckLedgeArms SingleLineCheck flags")]
    public const ETraceFlags LedgeClearanceTraceFlags = (ETraceFlags)0x2DF;

    /// <summary>Height of the second clearance trace's end above the hang spot.</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10aa4420 CheckLedgeArms")]
    public const float LedgeClearanceRaise = 50f;

    /// <summary>Pull-up offset from the hang spot to standing on the ledge (forward, right, up).</summary>
    [Provenance(ProvenanceKind.Decoded, "SBasePawn const C_LEDGE_UPST")]
    public static readonly FVector C_LEDGE_UPST = new(57.8f, 0f, 210.8f);

    /// <summary>Standing half height of both teams.</summary>
    [Provenance(ProvenanceKind.Decoded, "def:SBase.SBasePawn CollisionHeight")]
    public const float StandingCollisionHeight = 87.5f;

    /// <summary>GE kinds a spy takes (FENCE, HOH, LG, LADDER, PIPE, ZL).</summary>
    [Provenance(ProvenanceKind.Measured, "RE+: only spies take LG, PIPE, HOH, ZL and FENCE; both teams take LADDER")]
    public const int SpyGEKinds = 2 | 4 | 8 | 16 | 32 | 64;

    /// <summary>GE kinds a merc takes.</summary>
    [Provenance(ProvenanceKind.Measured, "RE+: mercs take ladders only")]
    public const int MercGEKinds = 16;
}
