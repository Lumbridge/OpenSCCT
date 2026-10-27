using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Physics.Movement;

/// <summary>
/// The SBasePawn fields and class defaults the SCCT natives read and write (spec/native/physics-*.md). The SBasePawn
/// port implements it with explicit members that return references to its own fields, so each value has one home:
/// <code>
/// ref FVector ISBasePawnPhysics.ZipP1 => ref ZipP1;
/// byte ISBasePawnPhysics.TypeLedge { get => (byte)TypeLedge; set => TypeLedge = (LedgeType)value; }
/// </code>
/// Every SCCT pawn is an SBasePawn; a pawn without this interface gets the walking and falling rules with the
/// SBasePawn-only parts left out (no drop attack, no root-motion walking) and no traversal modes.
/// </summary>
public interface ISBasePawnPhysics
{
    // ---- GE contact: filled by the zone natives, read by the traversal modes ----------------------------------

    ref FVector ZipP1 { get; }

    ref FVector ZipP2 { get; }

    ref FVector NewZipP1 { get; }

    ref FVector NewZipP2 { get; }

    ref FVector m_geoNormal { get; }

    ref FVector vBackFeetNormal { get; }

    // ---- search zones (pawn space) and radii ------------------------------------------------------------------

    ref FVector m_CurrentArmsZone { get; }

    ref float m_CurrentArmsRadius { get; }

    ref FVector m_CurrentFeetZone { get; }

    ref float m_CurrentFeetRadius { get; }

    ref FVector m_LedgeGrabArmsZone { get; }

    ref float m_LedgeGrabArmsRadius { get; }

    ref FVector m_PipeArmsZone { get; }

    ref FVector m_HandOverHandArmsZone { get; }

    ref FVector m_NarrowLadderArmsZone { get; }

    ref FVector m_ZipLineArmsZone { get; }

    ref float m_ZipLineArmsRadius { get; }

    ref FVector m_FenceArmsZone { get; }

    ref FVector m_NormalFeetZone { get; }

    // ---- stance -----------------------------------------------------------------------------------------------

    /// <summary>Location.Z shift for a change of zone configuration, read as ZCDecal[from * 4 + to] (16 entries).</summary>
    float[] ZCDecal { get; }

    ref byte ZoneConfig { get; }

    ref byte ZoneConfigForClient { get; }

    ref float ZipHeight { get; }

    ref bool bIsZipSpeed { get; }

    /// <summary>The half height of the legs-over (stance 4) cylinder on a hand-over-hand bar.</summary>
    ref float Pipe4Height { get; }

    /// <summary>The PrePivot drop of the legs-over cylinder.</summary>
    ref float Pipe4DecalCol { get; }

    /// <summary>The legs-over cylinder is on (SetColForPipe4 until SetColAfterPipe4).</summary>
    ref bool bIsPipe4Decal { get; }

    /// <summary>SBasePawn.TypeLedge (LedgeType: 0 NORMAL, 1 WAIST, 2 BARRIER, 3 FEET).</summary>
    byte TypeLedge { get; set; }

    /// <summary>The pipe side the pawn holds (TopPipe and TopPipeCrouch pass it).</summary>
    ref bool bIsLeftPart { get; }

    // ---- root motion, repositioning and Vandamme --------------------------------------------------------------

    ref FVector vDeltaMove { get; }

    ref FVector vVandammePos { get; }

    ref FVector vWallJumpDir { get; }

    ref FVector vSpeedAfterRM { get; }

    ref FVector vMoveAfterRM { get; }

    ref int rRotAfterRM { get; }

    ref bool bReverseRM { get; }

    ref int iLockRootMotionType { get; }

    ref float fLastRootMotionTimeCalled { get; }

    ref EPhysics RootMotionAdditionnalPhysics { get; }

    ref float fPhysReposLinearSpeed { get; }

    ref bool bUseAdditionnalPhysicsForRepos { get; }

    ref bool bNoReposAtEnd { get; }

    ref bool bKeepLocalVandammePos { get; }

    ref bool bActionsSendByServerMove { get; }

    /// <summary>The skeletal mesh instance that drives root motion, or null (physRootMotion then stops the pawn).</summary>
    IRootMotionMesh? RootMotionMesh { get; }

    // ---- falling and drop attacks -----------------------------------------------------------------------------

    /// <summary>SBasePawn.m_AttackTarget (an SBasePawn), recomputed by every physFalling call on the authority.</summary>
    Pawn? m_AttackTarget { get; set; }

    /// <summary>SBasePawn.m_AttackedBy: the grabber a held pawn follows in PHYS_FollowParent (physFollow).</summary>
    Pawn? m_AttackedBy => null;

    /// <summary>SBasePawn.vGrabOffset: the held pawn's place in front of its grabber, in the grabber's axes.</summary>
    FVector vGrabOffset => default;

    PawnType SPawnType { get; }

    bool bStickySkinned { get; }

    ref float Fell_MinHeightForDmg { get; }

    ref float Fell_MaxHeightForDmg { get; }

    ref int nJumpNumber { get; }

    /// <summary>SBasePawn.sAction, the movement action playing now (MyDoJump reads SA_LEDGEFALL, 119).</summary>
    ref int sAction { get; }

    // ---- class defaults (Default.X in the natives) ------------------------------------------------------------

    float DefaultCollisionRadius { get; }

    float DefaultCollisionHeight { get; }

    FVector DefaultPrePivot { get; }

    int DefaultHealth { get; }

    // ---- pawn natives the physics calls -----------------------------------------------------------------------

    /// <summary>
    /// ASBasePawn::UnCrouch (editor 0x111fb750 / 0x111fb920): stands the crouched pawn up when the standing cylinder
    /// fits. True when the pawn is standing afterwards. The walking drop attack calls it with true.
    /// </summary>
    bool UnCrouch(bool bClientSimulation);
}

/// <summary>
/// The root-motion side of a skeletal mesh instance (the animation port implements it): the lock mode and the root
/// bone's movement since the last call.
/// </summary>
public interface IRootMotionMesh
{
    /// <summary>The current root lock mode (0 when unlocked).</summary>
    int RootLockType { get; }

    /// <summary>Locks the root bone with <paramref name="LockType"/> (0 unlocks).</summary>
    void LockRootMotion(int LockType);

    /// <summary>Advances the animation and returns the root translation and rotation since the last call.</summary>
    void AdvanceRootMotion(out FVector DeltaLocation, out FRotator DeltaRotation);
}

/// <summary>
/// The SPlayerController fields and events the SCCT natives use. Events are raised immediately from inside physics
/// (UE2 semantics), so a state or physics change made in one applies to the rest of the frame. Implemented by the
/// SPlayerController port; it also implements the shared <see cref="IPhysicsEvents"/>.
/// </summary>
public interface ISPlayerControllerPhysics : IPhysicsEvents
{
    /// <summary>Highest Location.Z of the airborne phase (fall damage). Written by setPhysics and physFalling.</summary>
    ref float fZMaxFalling { get; }

    /// <summary>Level.TimeSeconds while the pawn stands on a mover.</summary>
    ref float fLastTimeOnAMover { get; }

    bool bTestBackFeetGrab { get; }

    /// <summary>Length of the CheckGEForWallJump wall probes (500 by default).</summary>
    float fDistanceForContextualJump { get; }

    /// <summary>True when this controller's pawn may be drop-attacked now.</summary>
    bool eventCanBeDropAttacked();

    void eventStickyBlast(Actor? Other);

    /// <summary>UE2 Controller.MayFall (walking lost its floor, before the ledge decision).</summary>
    void eventMayFall();

    void eventTopSynchro();

    void eventNoGE();

    void eventLedge();

    void eventWaistLedge(bool bCanGoNoCrouch);

    void eventAutoFeetLedge();

    void eventFeetLedge();

    void eventPipe(FVector locationEnd, FRotator orientationEnd);

    void eventTopPipe(bool bLeft);

    void eventTopPipeCrouch(bool bLeft);

    void eventNarrowLadder();

    void eventTopLadder();

    void eventHandOverHand();

    void eventOrthoPipeH(bool bToLeft);

    void eventZipLine(FVector locationEnd, FRotator orientationEnd);

    /// <summary>iType 0 inside right, 1 inside left, 2 outside right, 3 outside left; P1, P2 the next ledge.</summary>
    void eventChangeLedge(int iType, FVector P1, FVector P2);

    void eventFenceTop(FVector locationEnd, FRotator orientationEnd);

    /// <summary>The server refused a client's root-motion action (bServerTestRootMotion): the client stops it.</summary>
    void eventClientStopRM(int TheAction);
}
