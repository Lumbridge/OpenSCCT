using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

// Constants of the ladder, hand-over-hand, fence, wall jump, split jump, Vandamme, barrier, low ledge and co-op
// moves (spec/movement/*.md, spec/native/ANSWERS-moveset.md).
public static partial class MovementK
{
    /// <summary>The standing clearance box the traversal natives sweep.</summary>
    [Provenance(ProvenanceKind.Decoded, "CheckCeilingForLadder, CheckRoomToChangeSideLadder, bCanGoInPipe4: extent (35, 35, 87.5)")]
    public static readonly FVector StandingBox = new(35f, 35f, 87.5f);

    // ---- ladder (spec/movement/ladder.md) ----------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "const SpeedForLadder 126; s_OnLadder, s_LadderUp2, s_LadderToTopTr")]
    [Provenance(ProvenanceKind.Measured, "RE+ MapBuilderPatterns: ladder 126 uu/s, both teams")]
    public const float SpeedForLadder = 126f;

    [Provenance(ProvenanceKind.Decoded, "s_OnLadder.PlayerMove: rungs every 64 uu; CheckCeilingForLadder probes 64 up")]
    public const float LadderRungSpacing = 64f;

    [Provenance(ProvenanceKind.Decoded, "s_OnLadder.PlayerMove: Location.Z - bottom - 26 - C_LadderDecalZ (2)")]
    public const float LadderRungOffset = 26f + 2f;

    [Provenance(ProvenanceKind.Decoded, "s_GrabLadder, s_OnLadderTop: move (0, 20, 0)")]
    public static readonly FVector LadderApproachAccel = new(0f, 20f, 0f);

    [Provenance(ProvenanceKind.Decoded, "s_LadderGrabFall: move (0, 15, 0)")]
    public static readonly FVector LadderCatchAccel = new(0f, 15f, 0f);

    [Provenance(ProvenanceKind.Decoded, "s_SpeedLadder: newAccel X 524 (Down released) or -524 (held)")]
    public const float LadderSlideTerm = 524f;

    [Provenance(ProvenanceKind.Decoded, "s_SpeedLadderFromTop: newAccel X -525")]
    public const float LadderSlideFromTopTerm = 525f;

    [Provenance(ProvenanceKind.Decoded, "s_LadderGrabFall.AnimEnd: Location.Z - top < -130 stops on a rung")]
    public const float LadderCatchNearTop = 130f;

    [Provenance(ProvenanceKind.Decoded, "ladder states: merc desired yaw = pawn yaw + 1.4 x view offset")]
    public const float MercLadderAimFactor = 1.4f;

    [Provenance(ProvenanceKind.Decoded, "s_GrabLadder.BeginState: AttachCamToBone(true, 'B Head', (13, 0, 9))")]
    public static readonly Name MercLadderCameraBone = new("B Head");

    [Provenance(ProvenanceKind.Decoded, "s_GrabLadder.BeginState: AttachCamToBone offset (13, 0, 9)")]
    public static readonly FVector MercLadderCameraOffset = new(13f, 0f, 9f);

    [Provenance(ProvenanceKind.Decoded, "s_downTopLadder.AnimEnd: ActivateSmooth(10)")]
    public const float LadderTopSmooth = 10f;

    [Provenance(ProvenanceKind.Decoded, "CheckRoomToChangeSideLadder game 0x10aa3b00: start 2 uu behind the pawn")]
    public const float LadderSideChangeBack = 2f;

    [Provenance(ProvenanceKind.Decoded, "CheckRoomToChangeSideLadder: 70 aside")]
    public const float LadderSideChangeAside = 70f;

    [Provenance(ProvenanceKind.Decoded, "CheckRoomToChangeSideLadder: forward 2 m_NarrowLadderArmsZone.X + 4")]
    public const float LadderSideChangeExtra = 4f;

    // ---- hand over hand (spec/movement/pipe-horizontal.md) -----------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "s_OnPipeH.PlayerMove: newAccel.X 126")]
    public const float HohMoveRequest = 126f;

    [Provenance(ProvenanceKind.Decoded, "s_OnPipeH_4.PlayerMove: newAccel.X +-113")]
    public const float HohLegsOverMoveRequest = 113f;

    [Provenance(ProvenanceKind.Decoded, "bCanGoInPipe4 editor 0x111e6680: the hands point 95 forward must stay on the bar")]
    public const float HohLegsOverReach = 95f;

    [Provenance(ProvenanceKind.Decoded, "bCanGoInPipe4: a standing box 70 forward must be clear")]
    public const float HohLegsOverRoom = 70f;

    [Provenance(ProvenanceKind.Decoded, "TestRoomAfterPipe4: box (35, 35, 27.5), 70 ahead, 120 down, 60 up, back 70 with (35, 35, 87.5)")]
    public const float Pipe4RoomAhead = 70f;

    [Provenance(ProvenanceKind.Decoded, "TestRoomAfterPipe4: 120 down")]
    public const float Pipe4RoomDown = 120f;

    [Provenance(ProvenanceKind.Decoded, "TestRoomAfterPipe4: 60 up")]
    public const float Pipe4RoomUp = 60f;

    [Provenance(ProvenanceKind.Decoded, "s_PipH_TR2Fire2: limited view yaw +-10000, pitch -10000..5000")]
    public const int HohFire2Yaw = 10000;

    [Provenance(ProvenanceKind.Decoded, "s_PipH_TR2Fire2: pitch -10000..5000")]
    public const int HohFire2PitchMin = -10000;

    [Provenance(ProvenanceKind.Decoded, "s_PipH_TR2Fire2: pitch -10000..5000")]
    public const int HohFire2PitchMax = 5000;

    [Provenance(ProvenanceKind.Decoded, "s_PipH_TR2Fire4: yaw +-10000, pitch +-10000 around pawn yaw + 32768")]
    public const int HohFire4TransitionYaw = 10000;

    [Provenance(ProvenanceKind.Decoded, "s_PipH_Fire4: yaw +-16300 around pawn yaw + 32768")]
    public const int HohFire4Yaw = 16300;

    [Provenance(ProvenanceKind.Decoded, "s_PipH_TR2Fire4, s_PipH_Fire4: pitch +-10000")]
    public const int HohFire4Pitch = 10000;

    [Provenance(ProvenanceKind.Decoded, "s_Tr_PipeH2V and the HOH pipe join: ActivateSmooth(5)")]
    public const float HohSmooth = 5f;

    [Provenance(ProvenanceKind.Decoded, "s_OnPipeH_4 and TestRoomAfterPipe4: the legs-over body half height 27.5 (Pipe4Height)")]
    public const float Pipe4BodyHalfHeight = 27.5f;

    // ---- vertical pipe neighbours and the ledge beside a pipe (spec/native/natives2-traversal.md 4 to 7) -------

    [Provenance(ProvenanceKind.Decoded, "CheckForOtherPipeV: Q = L + (m_PipeArmsZone.X + 16) Fwd")]
    public const float PipeNeighbourExtra = 16f;

    [Provenance(ProvenanceKind.Decoded, "CheckForOtherPipeV: query box half size 5")]
    public const float PipeNeighbourBox = 5f;

    [Provenance(ProvenanceKind.Decoded, "CheckForOtherPipeV: the nearest strictly below 10")]
    public const float PipeNeighbourDistance = 10f;

    [Provenance(ProvenanceKind.Decoded, "CheckRoomToChangePipeV: aside m_PipeArmsZone.X + 8")]
    public const float PipeChangeAside = 8f;

    [Provenance(ProvenanceKind.Decoded, "CheckRoomToChangePipeV: forward 2 m_PipeArmsZone.X + 16")]
    public const float PipeChangeForward = 16f;

    [Provenance(ProvenanceKind.Decoded, "bCanTakeLedgeFromPipe: arms zone (40, 40 s, 90.3)")]
    public const float PipeLedgeZoneX = 40f;

    [Provenance(ProvenanceKind.Decoded, "bCanTakeLedgeFromPipe: arms zone Y 40 s")]
    public const float PipeLedgeZoneY = 40f;

    [Provenance(ProvenanceKind.Decoded, "bCanTakeLedgeFromPipe: arms zone Z 90.3")]
    public const float PipeLedgeZoneZ = 90.3f;

    [Provenance(ProvenanceKind.Decoded, "bCanTakeLedgeFromPipe: radius 60")]
    public const float PipeLedgeZoneRadius = 60f;

    [Provenance(ProvenanceKind.Decoded, "bCanTakeLedgeFromPipe: 14 forward")]
    public const float PipeLedgeForward = 14f;

    [Provenance(ProvenanceKind.Decoded, "bCanTakeLedgeFromPipe: 45 aside")]
    public const float PipeLedgeAside = 45f;

    [Provenance(ProvenanceKind.Decoded, "bCanTakeLedgeFromPipe: 27 down")]
    public const float PipeLedgeDown = 27f;

    [Provenance(ProvenanceKind.Decoded, "bCanTakeLedgeFromPipe: both ends squared above 35 from the closest point")]
    public const float PipeLedgeEndMarginSq = 35f;

    [Provenance(ProvenanceKind.Decoded, "bTestGrabLedgeGeo: arms zone (37, 0, 210)")]
    public static readonly FVector GrabLedgeGeoZone = new(37f, 0f, 210f);

    [Provenance(ProvenanceKind.Decoded, "bTestGrabLedgeGeo: 90 < dz")]
    public const float GrabLedgeGeoMin = 90f;

    [Provenance(ProvenanceKind.Decoded, "bTestGrabLedgeGeo: dz < 102")]
    public const float GrabLedgeGeoMax = 102f;

    [Provenance(ProvenanceKind.Decoded, "bTestGrabLedgeGeo: horizontal alignment squared below 100")]
    public const float GrabLedgeGeoAlignSq = 100f;

    [Provenance(ProvenanceKind.Decoded, "bTestGrabLedgeGeo: the controller's spot 20 either side")]
    public const float GrabLedgeGeoAside = 20f;

    // ---- fence (spec/movement/fence.md) ------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "OnFence.PlayerMove: 120 vertical, sideways and diagonal")]
    public const float FenceClimbSpeed = 120f;

    [Provenance(ProvenanceKind.Decoded, "GrabFence, OnFence, OnTopFence: limited view yaw and pitch +-15000 around the pawn yaw")]
    public const int FenceView = 15000;

    // ---- wall jump, split jump, Vandamme (spec/movement/wall-jump-split-vandamme.md) ----------------------------

    [Provenance(ProvenanceKind.Decoded, "s_FirstWallJumpPrepare*: newAccel 3000 X, GroundSpeed 385")]
    public const float WallJumpRunAccel = 3000f;

    [Provenance(ProvenanceKind.Decoded, "s_FirstWallJumpPrepare*: GroundSpeed 385")]
    public const float WallJumpRunSpeed = 385f;

    [Provenance(ProvenanceKind.Decoded, "s_FirstWallJumpPrepare*: a wall closer than 105 kicks")]
    public const float WallJumpKickDistance = 105f;

    [Provenance(ProvenanceKind.Decoded, "s_FirstWallJumpPrepareFromJump: 2 s timeout")]
    public const float WallJumpPrepareTimeout = 2f;

    [Provenance(ProvenanceKind.Decoded, "s_FirstWallJumpSaltoPrepareFromJump: 3 s timeout")]
    public const float WallJumpSaltoPrepareTimeout = 3f;

    [Provenance(ProvenanceKind.Decoded, "s_FirstWallJump.AnimEnd: more than 175 below the GE climbs up")]
    public const float WallJumpUpAbove = 175f;

    [Provenance(ProvenanceKind.Decoded, "s_FirstWallJump.AnimEnd: more than 150 below the GE climbs up small; s_WallJumpSalto.Ledge: under 150")]
    public const float WallJumpUpSmallAbove = 150f;

    [Provenance(ProvenanceKind.Decoded, "s_WallJumpSalto.Ledge: WljpStAlUmL catches only past frame 0.65")]
    public const float WallJumpUpSmallCatchFrame = 0.65f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerJump and s_SplitJump: the side dead band 0.1")]
    public const float SplitSideDeadBand = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerJump.PlayerMove: the wall must face less than 0.707 along X")]
    public const float SplitWallFacing = 0.707f;

    [Provenance(ProvenanceKind.Decoded, "Vandamme.PlayerMove: UpdateLimitedRotation(-40000, 40000, -12000, 12000)")]
    public const int VandammeViewYaw = 40000;

    [Provenance(ProvenanceKind.Decoded, "Vandamme.PlayerMove: pitch -12000..12000")]
    public const int VandammeViewPitch = 12000;

    [Provenance(ProvenanceKind.Decoded, "VandammeWeapon.PlayerMove: yaw -12000..12000")]
    public const int VandammeWeaponViewYaw = 12000;

    [Provenance(ProvenanceKind.Decoded, "VandammeWeapon.PlayerMove: pitch -11500..6730")]
    public const int VandammeWeaponPitchMin = -11500;

    [Provenance(ProvenanceKind.Decoded, "VandammeWeapon.PlayerMove: pitch -11500..6730")]
    public const int VandammeWeaponPitchMax = 6730;

    // ---- co-op (spec/movement/coop.md) -------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "s_ShortLadder_S_CheckIfOK: 70.5 in front of the helper")]
    public const float ShortLadderSpot = 70.5f;

    [Provenance(ProvenanceKind.Decoded, "s_ShortLadder_S_CheckIfOK: +27.5 when crouched")]
    public const float ShortLadderCrouchLift = 27.5f;

    [Provenance(ProvenanceKind.Decoded, "const HoistOffsetX 130.7")]
    public const float HoistOffsetX = 130.7f;

    [Provenance(ProvenanceKind.Decoded, "const HoistOffsetZ 296.7")]
    public const float HoistOffsetZ = 296.7f;

    [Provenance(ProvenanceKind.Decoded, "s_Hoist_S_CheckIfOK: a spot more than 15 above the climber keeps his own Z")]
    public const float HoistSpotTolerance = 15f;

    [Provenance(ProvenanceKind.Decoded, "s_Coop_S_Goto.BeginState: SetTimer(8)")]
    public const float CoopApproachTimeout = 8f;

    [Provenance(ProvenanceKind.Decoded, "helper waiting states: |aForward| and |aStrafe| under 100 keep the helper ready")]
    public const float CoopCancelStick = 100f;
}
