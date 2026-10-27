using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Physics.Movement;

namespace OpenScct.Game.SBase;

// SBase.SBasePawn as the SCCT natives see it (docs/PHYSICS.md, pawn movement API): every field the physics reads and
// writes is this pawn's own field, handed out by reference, and the class defaults come from the default object.
public partial class SBasePawn : ISBasePawnPhysics
{
    private PawnRootMotion? _rootMotion;

    ref FVector ISBasePawnPhysics.ZipP1 => ref ZipP1;

    ref FVector ISBasePawnPhysics.ZipP2 => ref ZipP2;

    ref FVector ISBasePawnPhysics.NewZipP1 => ref NewZipP1;

    ref FVector ISBasePawnPhysics.NewZipP2 => ref NewZipP2;

    ref FVector ISBasePawnPhysics.m_geoNormal => ref m_geoNormal;

    ref FVector ISBasePawnPhysics.vBackFeetNormal => ref vBackFeetNormal;

    ref FVector ISBasePawnPhysics.m_CurrentArmsZone => ref m_CurrentArmsZone;

    ref float ISBasePawnPhysics.m_CurrentArmsRadius => ref m_CurrentArmsRadius;

    ref FVector ISBasePawnPhysics.m_CurrentFeetZone => ref m_CurrentFeetZone;

    ref float ISBasePawnPhysics.m_CurrentFeetRadius => ref m_CurrentFeetRadius;

    ref FVector ISBasePawnPhysics.m_LedgeGrabArmsZone => ref m_LedgeGrabArmsZone;

    ref float ISBasePawnPhysics.m_LedgeGrabArmsRadius => ref m_LedgeGrabArmsRadius;

    ref FVector ISBasePawnPhysics.m_PipeArmsZone => ref m_PipeArmsZone;

    ref FVector ISBasePawnPhysics.m_HandOverHandArmsZone => ref m_HandOverHandArmsZone;

    ref FVector ISBasePawnPhysics.m_NarrowLadderArmsZone => ref m_NarrowLadderArmsZone;

    ref FVector ISBasePawnPhysics.m_ZipLineArmsZone => ref m_ZipLineArmsZone;

    ref float ISBasePawnPhysics.m_ZipLineArmsRadius => ref m_ZipLineArmsRadius;

    ref FVector ISBasePawnPhysics.m_FenceArmsZone => ref m_FenceArmsZone;

    ref FVector ISBasePawnPhysics.m_NormalFeetZone => ref m_NormalFeetZone;

    float[] ISBasePawnPhysics.ZCDecal => ZCDecal;

    ref byte ISBasePawnPhysics.ZoneConfig => ref ZoneConfig;

    ref byte ISBasePawnPhysics.ZoneConfigForClient => ref ZoneConfigForClient;

    ref float ISBasePawnPhysics.ZipHeight => ref ZipHeight;

    ref bool ISBasePawnPhysics.bIsZipSpeed => ref bIsZipSpeed;

    ref float ISBasePawnPhysics.Pipe4Height => ref Pipe4Height;

    ref float ISBasePawnPhysics.Pipe4DecalCol => ref Pipe4DecalCol;

    ref bool ISBasePawnPhysics.bIsPipe4Decal => ref bIsPipe4Decal;

    byte ISBasePawnPhysics.TypeLedge
    {
        get => (byte)TypeLedge;
        set => TypeLedge = (LedgeType)value;
    }

    ref bool ISBasePawnPhysics.bIsLeftPart => ref bIsLeftPart;

    ref FVector ISBasePawnPhysics.vDeltaMove => ref vDeltaMove;

    ref FVector ISBasePawnPhysics.vVandammePos => ref vVandammePos;

    ref FVector ISBasePawnPhysics.vWallJumpDir => ref vWallJumpDir;

    ref FVector ISBasePawnPhysics.vSpeedAfterRM => ref vSpeedAfterRM;

    ref FVector ISBasePawnPhysics.vMoveAfterRM => ref vMoveAfterRM;

    ref int ISBasePawnPhysics.rRotAfterRM => ref rRotAfterRM;

    ref bool ISBasePawnPhysics.bReverseRM => ref bReverseRM;

    ref int ISBasePawnPhysics.iLockRootMotionType => ref iLockRootMotionType;

    ref float ISBasePawnPhysics.fLastRootMotionTimeCalled => ref fLastRootMotionTimeCalled;

    ref EPhysics ISBasePawnPhysics.RootMotionAdditionnalPhysics => ref RootMotionAdditionnalPhysics;

    ref float ISBasePawnPhysics.fPhysReposLinearSpeed => ref fPhysReposLinearSpeed;

    ref bool ISBasePawnPhysics.bUseAdditionnalPhysicsForRepos => ref bUseAdditionnalPhysicsForRepos;

    ref bool ISBasePawnPhysics.bNoReposAtEnd => ref bNoReposAtEnd;

    ref bool ISBasePawnPhysics.bKeepLocalVandammePos => ref bKeepLocalVandammePos;

    ref bool ISBasePawnPhysics.bActionsSendByServerMove => ref bActionsSendByServerMove;

    /// <summary>The root motion of the base animation channel (the movement layer's animation).</summary>
    IRootMotionMesh? ISBasePawnPhysics.RootMotionMesh => _rootMotion ??= new PawnRootMotion(this);

    Pawn? ISBasePawnPhysics.m_AttackTarget
    {
        get => m_AttackTarget;
        set => m_AttackTarget = value as SBasePawn;
    }

    Pawn? ISBasePawnPhysics.m_AttackedBy => m_AttackedBy;

    FVector ISBasePawnPhysics.vGrabOffset => vGrabOffset;

    PawnType ISBasePawnPhysics.SPawnType => SPawnType;

    bool ISBasePawnPhysics.bStickySkinned => bStickySkinned;

    ref float ISBasePawnPhysics.Fell_MinHeightForDmg => ref Fell_MinHeightForDmg;

    ref float ISBasePawnPhysics.Fell_MaxHeightForDmg => ref Fell_MaxHeightForDmg;

    ref int ISBasePawnPhysics.nJumpNumber => ref nJumpNumber;

    ref int ISBasePawnPhysics.sAction => ref sAction;

    float ISBasePawnPhysics.DefaultCollisionRadius => Defaults.CollisionRadius;

    float ISBasePawnPhysics.DefaultCollisionHeight => Defaults.CollisionHeight;

    FVector ISBasePawnPhysics.DefaultPrePivot => Defaults.PrePivot;

    int ISBasePawnPhysics.DefaultHealth => Defaults.Health;
}
