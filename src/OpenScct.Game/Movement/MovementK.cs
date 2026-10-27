using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

/// <summary>Constants of the pawn and controller movement (spec/movement, spec/native), with their evidence.</summary>
public static partial class MovementK
{
    // ---- input (spec/movement/input-and-rotation.md) ----------------------------------------------------------

    [Provenance(ProvenanceKind.Measured, "golden movement.spy-run-forward: BT_Up gives aForward 6000 before the Mario 1.1 boost")]
    [Provenance(ProvenanceKind.Decoded, "walking PlayerMove stick clamp 6000 (squared 36000000)")]
    public const float StickFullScale = 6000f;

    [Provenance(ProvenanceKind.Decoded, "walking PlayerMove: aForward^2 + aStrafe^2 > 36000000")]
    public const float StickClampSquared = 36000000f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerJump.PlayerMove and PlayerLanding.PlayerMove: |aForward| < 100 and |aStrafe| < 100")]
    public const float StickDeadSquare = 100f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationAttaquant_Mouse, UpdateCamRotation: rate scale 50")]
    public const float RotationRateScale = 50f;

    /// <summary>NativeComputeControlVariables: on a ledge or a fence the raw stick beyond this sets a direction bit.</summary>
    [Provenance(ProvenanceKind.Decoded, "NativeComputeControlVariables editor 0x111f37d0: 2100")]
    public const float LedgeStick = 2100f;

    /// <summary>NativeComputeControlVariables: the camera-relative move beyond this along the pawn's left axis sets left or right.</summary>
    [Provenance(ProvenanceKind.Decoded, "NativeComputeControlVariables editor 0x111f37d0: 2100")]
    public const float SideStick = 2100f;

    /// <summary>NativeComputeControlVariables: the move along the pawn's facing beyond this sets forward (and below its negative, back).</summary>
    [Provenance(ProvenanceKind.Decoded, "NativeComputeControlVariables editor 0x111f37d0: 1.5 x 3000")]
    public const float ForwardStick = 4500f;

    /// <summary>NativeComputeControlVariables: a climbing spy's normalised stick sets up or down beyond this cosine.</summary>
    [Provenance(ProvenanceKind.Decoded, "NativeComputeControlVariables editor 0x111f37d0: 0.45")]
    public const float ClimbStickCos = 0.45f;

    /// <summary>NativeComputeControlVariables: the third component of the climbing spy's stick before it is normalised.</summary>
    [Provenance(ProvenanceKind.Decoded, "NativeComputeControlVariables editor 0x111f37d0: (aStrafe, -aForward, 0.01)")]
    public const float ClimbStickDepth = 0.01f;

    /// <summary>NativeComputeControlVariables: the forward threshold while last frame's iMove had forward.</summary>
    [Provenance(ProvenanceKind.Decoded, "NativeComputeControlVariables editor 0x111f37d0: 1.0 x 3000")]
    public const float ForwardStickHeld = 3000f;

    // ---- back-to-wall natives (spec/native/natives2-traversal.md rules 1 to 3) -----------------------------------

    [Provenance(ProvenanceKind.Decoded, "CheckBTW editor 0x111e5640 / game 0x10aa97e0: probes 1.5 R long")]
    public const float BtwProbeReachR = 1.5f;

    [Provenance(ProvenanceKind.Decoded, "CheckBTW: waist probes 0.5 R either side")]
    public const float BtwWaistSideR = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "CheckBTW: waist probes lowered by 0.5 H")]
    public const float BtwWaistDownH = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "CheckBTW and CheckBTWEyes: head height 0.8 H")]
    public const float BtwHeadH = 0.8f;

    [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md Data: 0xdf plus stop-at-first-hit (controllers never collide)")]
    public const uint BtwProbeFlags = 0x2df;

    [Provenance(ProvenanceKind.Decoded, "CheckBTWEyes editor 0x111e5dc0: boxes (5, 5, 5)")]
    public const float BtwEyesBox = 5f;

    [Provenance(ProvenanceKind.Decoded, "CheckBTWEyes: flags 0xdf, no stop-at-first-hit")]
    public const uint BtwEyesFlags = 0xdf;

    [Provenance(ProvenanceKind.Decoded, "CheckBTWEyes: side point R + 4")]
    public const float BtwEyesSide = 4f;

    [Provenance(ProvenanceKind.Decoded, "CheckBTWEyes: wall box 2 R toward the wall")]
    public const float BtwEyesReachR = 2f;

    [Provenance(ProvenanceKind.Decoded, "CheckBTWEyes: floor box 5 aside")]
    public const float BtwEyesFloorSide = 5f;

    [Provenance(ProvenanceKind.Decoded, "CheckBTWEyes: floor box H + 31 down, the second box to 31 below the floor")]
    public const float BtwEyesFloorDrop = 31f;

    [Provenance(ProvenanceKind.Decoded, "CheckBTWEyes: peek line 100 to the side")]
    public const float BtwEyesPeek = 100f;

    [Provenance(ProvenanceKind.Decoded, "CheckBTWCorner editor 0x111e5050: same plane within a cross length of 0.1")]
    public const float BtwCornerPlane = 0.1f;

    /// <summary>LastZipP when no GE was let go of (AnimateWalking resets it to this).</summary>
    [Provenance(ProvenanceKind.Decoded, "ASBasePawn::AnimateWalking 0x111fd360: LastZipP = (-1e7, 0, -1e7)")]
    public static readonly FVector FarZipPoint = new(-10000000f, 0f, -10000000f);

    [Provenance(ProvenanceKind.Decoded, "UpdateRotation*: binocular ratio 0.5")]
    public const float BinocularRotationRatio = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationAttaquant_Mouse / unarmed UpdateCamRotation pitch clamp")]
    public const int PitchMin = -12000;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationAttaquant_Mouse / unarmed UpdateCamRotation pitch clamp")]
    public const int PitchMax = 13000;

    [Provenance(ProvenanceKind.Decoded, "UpdateCamRotation armed pitch clamp")]
    public const int ArmedPitchMax = 15000;

    [Provenance(ProvenanceKind.Decoded, "UpdateCamRotation armed pitch repulsion start")]
    public const int ArmedPitchRepulsionStart = 6000;

    [Provenance(ProvenanceKind.Decoded, "UpdateCamRotation armed pitch repulsion span")]
    public const float ArmedPitchRepulsionSpan = 9000f;

    [Provenance(ProvenanceKind.Decoded, "UpdateLimitedRotation pitch factor")]
    public const float LimitedPitchFactor = 0.667f;

    [Provenance(ProvenanceKind.Decoded, "UpdateLimitedRotation recentre speed 50000 x m_dT")]
    public const float LimitedRecentreSpeed = 50000f;

    [Provenance(ProvenanceKind.Decoded, "UpdatePawnRotationMario slow-turn threshold 80")]
    public const float MarioSlowTurnSpeed = 80f;

    [Provenance(ProvenanceKind.Decoded, "UpdatePawnRotationMario stop-turn threshold 10")]
    public const float MarioStopTurnSpeed = 10f;

    [Provenance(ProvenanceKind.Decoded, "UpdatePawnRotationMario max step 32000")]
    public const float MarioMaxStep = 32000f;

    [Provenance(ProvenanceKind.Decoded, "UpdatePawnRotationMarioJump: h > 10")]
    public const float MarioJumpSteerSpeed = 10f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur desired rotation resend 0.2 s")]
    public const float DesiredRotationResend = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "PlayerTick camera position report 0.14 s")]
    public const float PositionReportInterval = 0.14f;

    [Provenance(ProvenanceKind.Decoded, "PlayerTick jammer minimum input scale 0.5")]
    public const float JammerMinScale = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "PlayerTick adrenaline end: fTimeWhenHit reset to -10")]
    public const float AdrenalineReset = -10f;

    [Provenance(ProvenanceKind.Decoded, "ComputeCrouchOrRoll roll minimum stick 0.3")]
    public const float RollMinStick = 0.3f;

    [Provenance(ProvenanceKind.Decoded, "ComputeCrouchOrRoll and roll start: roll minimum speed 65")]
    public const float RollMinSpeed = 65f;

    // ---- speed mode (GetSpeedMode) -----------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "GetSpeedMode: binoculars")]
    public const float SpeedModeBinoculars = 0.8f;

    [Provenance(ProvenanceKind.Decoded, "GetSpeedMode: merc, fast or no pawn")]
    public const float SpeedModeFull = 1f;

    [Provenance(ProvenanceKind.Decoded, "GetSpeedMode: weapon movement, slow")]
    public const float SpeedModeWeaponSlow = 0.6f;

    [Provenance(ProvenanceKind.Decoded, "GetSpeedMode: slow")]
    public const float SpeedModeSlow = 0.4f;

    // ---- walking (spec/movement/walking.md) --------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "s_PlayerWalkingMario.PlayerMove standing boost 1.1")]
    public const float MarioStandingBoost = 1.1f;

    [Provenance(ProvenanceKind.Decoded, "walking PlayerMove: acceleration request int(0.5 * stick)")]
    public const float AccelRequestScale = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "GroundSpeed = RunningSpeed * |newAccel| / 3000")]
    public const float SpeedDivisor = 3000f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerWalkingMario.PlayerMove dead zone hysteresis 10")]
    public const float SpeedHysteresis = 10f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerWalkingWeapon.PlayerMove dead zone standing 75")]
    public const float WeaponDeadZoneStanding = 75f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerWalkingWeapon.PlayerMove dead zone crouched 50")]
    public const float WeaponDeadZoneCrouched = 50f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerWalkingMario.PlayerMove low ceiling cap 700")]
    public const float LowCeilingAccel = 700f;

    [Provenance(ProvenanceKind.Decoded, "ceiling probe up 20")]
    public const float CeilingProbeUp = 20f;

    [Provenance(ProvenanceKind.Decoded, "ceiling probe extent (35, 35, 50)")]
    public const float CeilingProbeRadius = 35f;

    [Provenance(ProvenanceKind.Decoded, "ceiling probe extent (35, 35, 50)")]
    public const float CeilingProbeHalfHeight = 50f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerWalkingMario.PlayerMove wall jump speed gate 335")]
    public const float WallJumpSpeedGate = 335f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerWalkingMario.PlayerMove wall jump GE window low 130")]
    public const float WallJumpGeLow = 130f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerWalkingMario.PlayerMove wall jump GE window high 335")]
    public const float WallJumpGeHigh = 335f;

    [Provenance(ProvenanceKind.Decoded, "PlayerMove quick gadget date floor now - 1.6")]
    public const float QuickGadgetDateFloor = 1.6f;

    [Provenance(ProvenanceKind.Decoded, "ProcessCrouch stance cooldown 0.5 s")]
    public const float StanceCooldown = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "weapon curve standing k = (-0.05 u + 0.15) u + 0.9")]
    public const float WeaponCurveStandA = -0.05f;

    [Provenance(ProvenanceKind.Decoded, "weapon curve standing k = (-0.05 u + 0.15) u + 0.9")]
    public const float WeaponCurveStandB = 0.15f;

    [Provenance(ProvenanceKind.Decoded, "weapon curve standing k = (-0.05 u + 0.15) u + 0.9")]
    public const float WeaponCurveStandC = 0.9f;

    [Provenance(ProvenanceKind.Decoded, "weapon curve crouched k = (-0.02468 u + 0.05065) u + 0.3117")]
    public const float WeaponCurveCrouchA = -0.02468f;

    [Provenance(ProvenanceKind.Decoded, "weapon curve crouched k = (-0.02468 u + 0.05065) u + 0.3117")]
    public const float WeaponCurveCrouchB = 0.05065f;

    [Provenance(ProvenanceKind.Decoded, "weapon curve crouched k = (-0.02468 u + 0.05065) u + 0.3117")]
    public const float WeaponCurveCrouchC = 0.3117f;

    [Provenance(ProvenanceKind.Decoded, "spy armed standing forward factor 0.39")]
    public const float ArmedForward = 0.39f;

    [Provenance(ProvenanceKind.Decoded, "spy armed standing back factor 0.5")]
    public const float ArmedBack = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "spy armed standing strafe factor 0.4")]
    public const float ArmedStrafe = 0.4f;

    [Provenance(ProvenanceKind.Decoded, "spy armed crouched factor 0.8")]
    public const float ArmedCrouched = 0.8f;

    [Provenance(ProvenanceKind.Decoded, "merc smoke crouched factors 0.7 / 0.65 / 0.66")]
    public const float SmokeCrouchForward = 0.7f;

    [Provenance(ProvenanceKind.Decoded, "merc smoke crouched factors 0.7 / 0.65 / 0.66")]
    public const float SmokeCrouchBack = 0.65f;

    [Provenance(ProvenanceKind.Decoded, "merc smoke crouched factors 0.7 / 0.65 / 0.66")]
    public const float SmokeCrouchStrafe = 0.66f;

    [Provenance(ProvenanceKind.Decoded, "merc factor 0.9 x k")]
    public const float MercSpeedFactor = 0.9f;

    [Provenance(ProvenanceKind.Decoded, "merc after-charge slow: 0.5 for 2 s")]
    public const float MercChargeSlow = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "merc after-charge slow: 2 s")]
    public const float MercChargeSlowTime = 2f;

    [Provenance(ProvenanceKind.Decoded, "merc run animation switch above 280")]
    public const float MercRunAbove = 280f;

    [Provenance(ProvenanceKind.Decoded, "merc walk animation switch below 220")]
    public const float MercWalkBelow = 220f;

    [Provenance(ProvenanceKind.Decoded, "native run action sets fBlendCoeffWalkAnim to 327")]
    public const float MercRunBlendCoeff = 327f;

    // ---- roll (spec/movement/roll.md) --------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "roll trace start standing -7.5")]
    public const float RollTraceStandingDrop = 7.5f;

    [Provenance(ProvenanceKind.Decoded, "roll trace start crouched +10")]
    public const float RollTraceCrouchedLift = 10f;

    [Provenance(ProvenanceKind.Decoded, "roll velocity retrace cone 0.866")]
    public const float RollRetraceCone = 0.866f;

    [Provenance(ProvenanceKind.Decoded, "crouched roll magnitude 3000")]
    public const float CrouchedRollMagnitude = 3000f;

    [Provenance(ProvenanceKind.Decoded, "dive roll magnitude clamp(5.83 v + 2625, 3500, 4700)")]
    public const float DiveRollSlope = 5.83f;

    [Provenance(ProvenanceKind.Decoded, "dive roll magnitude clamp(5.83 v + 2625, 3500, 4700)")]
    public const float DiveRollOffset = 2625f;

    [Provenance(ProvenanceKind.Decoded, "dive roll magnitude clamp(5.83 v + 2625, 3500, 4700)")]
    public const float DiveRollMin = 3500f;

    [Provenance(ProvenanceKind.Decoded, "dive roll magnitude clamp(5.83 v + 2625, 3500, 4700)")]
    public const float DiveRollMax = 4700f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerRolling.performDropAttack damage 17")]
    public const int RollKnockDamage = 17;

    // ---- jump, fall, land (spec/movement/jump-fall-land.md) ----------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "s_PlayerJump.MyNotifyLanded landing still threshold 40")]
    public const float LandingStillSpeed = 40f;

    [Provenance(ProvenanceKind.Decoded, "MyFalling back ledge stick limit fNormePadMove < 0.7")]
    public const float BackLedgeStickLimit = 0.7f;

    [Provenance(ProvenanceKind.Decoded, "MyFalling back ledge floor probe down 175")]
    public const float BackLedgeFloorProbe = 175f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerJump.PlayerMove back ledge turn rate 80000")]
    public const float BackLedgeTurnRate = 80000f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerJump.PlayerMove split jump apex window |Velocity.Z| < 150")]
    public const float SplitJumpApexWindow = 150f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerJump fence grab cooldown 1 s")]
    public const float FenceGrabCooldown = 1f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerJump ladder grab cooldown 1 s")]
    public const float LadderGrabCooldown = 1f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerJump zip grab cooldown 2 s")]
    public const float ZipGrabCooldown = 2f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerJump.NarrowLadder catch speed -400")]
    public const float LadderCatchSpeed = -400f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerJump.Ledge re-grab height 20")]
    public const float LedgeRegrabHeight = 20f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerFalling_OutOfWorld probe 15000 down")]
    public const float OutOfWorldProbe = 15000f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerFalling_OutOfWorld respawn line hit + 8 x CollisionHeight")]
    public const float OutOfWorldRespawnHeights = 8f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerFalling_OutOfWorld no hit: Location.Z - 1500")]
    public const float OutOfWorldNoHitDrop = 1500f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerFalling_OutOfWorld speed exit 100")]
    public const float OutOfWorldSpeedExit = 100f;

    [Provenance(ProvenanceKind.Decoded, "PlayerFallingInerte timeout 10 s")]
    public const float InertFallTimeout = 10f;

    [Provenance(ProvenanceKind.Decoded, "PlayerLanding landing noise loudness 1.0")]
    public const float LandingNoise = 1f;

    // ---- ledge (spec/movement/ledge.md) ------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "OnLedge.PlayerMove shimmy acceleration 80")]
    public const float LedgeShimmyAccel = 80f;

    // ---- clips (spec/movement/native-contracts.md, clip timing) -----------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SBasePawn const DBLJUMPCHANNEL")]
    public const int DoubleJumpChannel = 15;

    // ---- trace flags (spec/native/collision-traces.md) ---------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "clearance tests 0x2df")]
    public const uint TraceClearance = 0x2df;

    [Provenance(ProvenanceKind.Decoded, "pawn-and-mover-only clearance 0x243")]
    public const uint TracePawnsAndMovers = 0x243;

    [Provenance(ProvenanceKind.Decoded, "floor probes 0xdf")]
    public const uint TraceFloor = 0xdf;

    [Provenance(ProvenanceKind.Estimated, "UE2 AActor::execTrace: TRACE_ProjTargets with bTraceActors (0x20 | AllColliding)")]
    public const uint TraceScriptActors = 0x20 | 0x04 | 0x01 | 0x02 | 0x10 | 0x80;

    [Provenance(ProvenanceKind.Estimated, "UE2 AActor::execTrace: TRACE_World without bTraceActors (Level, Movers, LevelGeometry)")]
    public const uint TraceScriptWorld = 0x04 | 0x02 | 0x80;

    // ---- traversal -----------------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "pipe-vertical.md s_OnPipe.PlayerMove: newAccel Z 145 (measured 145 uu/s)")]
    public const float PipeClimbSpeed = 145f;

    [Provenance(ProvenanceKind.Decoded, "pipe-vertical.md s_SpeedPipe.PlayerMove: slide term 524 (negative speeds the slide up)")]
    public const float PipeSlideAccel = 524f;

    [Provenance(ProvenanceKind.Decoded, "zip-line.md s_GrabZip.PlayerMove: newAccel = 100 m_geoNormal, GroundSpeed = RunningSpeed x 100 / 191")]
    public const float ZipGrabPull = 100f;

    [Provenance(ProvenanceKind.Decoded, "zip-line.md: GroundSpeed = RunningSpeed x v / 191")]
    public const float ZipSpeedDivisor = 191f;

    [Provenance(ProvenanceKind.Decoded, "zip-line.md slide: v = 210 + 0.015 aForward")]
    public const float ZipSlideBase = 210f;

    [Provenance(ProvenanceKind.Decoded, "zip-line.md slide: v = 210 + 0.015 aForward")]
    public const float ZipSlideStick = 0.015f;

    [Provenance(ProvenanceKind.Decoded, "zip-line.md: speed pose in above aForward 1000, out below 500")]
    public const float ZipSpeedIn = 1000f;

    [Provenance(ProvenanceKind.Decoded, "zip-line.md s_ZipLineSpeed: out below aForward 500")]
    public const float ZipSpeedOut = 500f;

    [Provenance(ProvenanceKind.Decoded, "zip-line.md TestZipWall: 60 ahead, extent (35, 35, 20), facing below -0.7")]
    public const float ZipWallProbe = 60f;

    [Provenance(ProvenanceKind.Decoded, "zip-line.md TestZipWall extent height")]
    public const float ZipWallProbeHeight = 20f;

    [Provenance(ProvenanceKind.Decoded, "zip-line.md TestZipWall facing")]
    public const float ZipWallFacing = -0.7f;

    [Provenance(ProvenanceKind.Decoded, "back-to-wall.md ProcessWorldInteract: stick inside the 5000 radius (aForward^2 + aStrafe^2 < 25000000)")]
    public const float BtwEntryStickSq = 25000000f;

    [Provenance(ProvenanceKind.Decoded, "back-to-wall.md MyPlayerMove: crouched sidestep x 0.62")]
    public const float BtwCrouchedStick = 0.62f;

    [Provenance(ProvenanceKind.Decoded, "back-to-wall.md MyPlayerMove: newAccel x 0.17")]
    public const float BtwAccelScale = 0.17f;

    [Provenance(ProvenanceKind.Decoded, "back-to-wall.md MyPlayerMove: GroundSpeed = 385 |newAccel| / 3000")]
    public const float BtwSpeedDivisor = 3000f;

    [Provenance(ProvenanceKind.Decoded, "back-to-wall.md MyPlayerMove: sidestep zeroed below 100 standing")]
    public const float BtwMinSpeedStanding = 100f;

    [Provenance(ProvenanceKind.Decoded, "back-to-wall.md MyPlayerMove: sidestep zeroed below 50 crouched")]
    public const float BtwMinSpeedCrouched = 50f;

    [Provenance(ProvenanceKind.Decoded, "back-to-wall.md MyPlayerMove: peek above |aStrafe| 1000 at the edge")]
    public const float BtwPeekStick = 1000f;

    [Provenance(ProvenanceKind.Decoded, "back-to-wall.md MyPlayerMove: leave walking above 70 uu/s")]
    public const float BtwLeaveSpeed = 70f;

    [Provenance(ProvenanceKind.Decoded, "back-to-wall.md s_BTWCorner: 3 x 16384 per second")]
    public const float BtwCornerTurnRate = 3f * 16384f;

    [Provenance(ProvenanceKind.Decoded, "back-to-wall.md s_BTW.NotifyHitWall: 2 s since the last special move")]
    public const float BtwCornerCooldown = 2f;

    [Provenance(ProvenanceKind.Decoded, "back-to-wall.md stance changes: 0.5 s")]
    public const float BtwStanceCooldown = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "back-to-wall.md peek view: yaw and pitch +-15000 around pawn yaw + 32768")]
    public const int BtwPeekView = 15000;
}
