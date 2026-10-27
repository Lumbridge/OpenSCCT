using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.Combat;

/// <summary>Constants of the combat port (spec/combat), with their evidence.</summary>
public static class K
{
    // ---- damage pipeline (spec/combat/damage-pipeline.md) -------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.StandardReactionsToDamage: SDamage_Explosion ActualDamage 200")]
    public const int ExplosionDamage = 200;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.StandardReactionsToDamage: taser int(default.Health * 0.51)")]
    public const float TaserHealthFactor = 0.51f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.StandardReactionsToDamage: zone table percent * 0.01")]
    public const float ZoneScale = 0.01f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.StandardReactionsToDamage: real pills 1 to 15")]
    public const int FirstPill = 1;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.StandardReactionsToDamage: real pills 1 to 15")]
    public const int LastPill = 15;

    [Provenance(ProvenanceKind.Decoded, "taser, spawn shock and UseBulletSet PillTag -10")]
    public const int TaserPill = -10;

    [Provenance(ProvenanceKind.Decoded, "Engine.Pawn default Health (effective for SPawnAttaque and SPAWNDEFENSE)")]
    public const int DefaultHealth = 100;

    [Provenance(ProvenanceKind.Decoded, "PawnTakeDamage: PlayAnimDmg(PillTag, Momentum * 1000)")]
    public const float HitAnimMomentumScale = 1000f;

    [Provenance(ProvenanceKind.Decoded, "PlayAnimDmg: front and back threshold 0.6")]
    public const float HitAnimFrontBack = 0.6f;

    [Provenance(ProvenanceKind.Decoded, "execNativeTick editor 0x1120ff60: the refill stops at the literal 100")]
    public const int RefillHealthCap = 100;

    [Provenance(ProvenanceKind.Decoded, "execNativeTick editor 0x11210250: still means |Velocity|^2 below 0.01")]
    public const float RefillStillSpeedSquared = 0.01f;

    [Provenance(ProvenanceKind.Decoded, "execNativeTick editor 0x112101af: the poison's last PawnTakeDamage is 1500")]
    public const int PoisonKillDamage = 1500;

    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 8: the poison kill's pill tag 3")]
    public const int PoisonKillPill = 3;

    [Provenance(ProvenanceKind.Decoded, "ChooseDeathAnimation 0x10acd3e0: forward at least 0.7, backward at most -0.7")]
    public const float DeathFrontBack = 0.7f;

    [Provenance(ProvenanceKind.Decoded, "GetValidDeathDirection 0x11206c00: the room box is R + 20 wide")]
    public const float DeathRoomExtentGrow = 20f;

    [Provenance(ProvenanceKind.Decoded, "GetValidDeathDirection 0x11206c00: the room box is H - 15 high")]
    public const float DeathRoomExtentShrinkZ = 15f;

    [Provenance(ProvenanceKind.Decoded, "GetValidDeathDirection 0x11206c00: the floor lines go 1.25 H down")]
    public const float DeathFloorDepth = 1.25f;

    [Provenance(ProvenanceKind.Decoded, "GetValidDeathDirection 0x11206c00: first floor line at 1.5 R")]
    public const float DeathFloorNear = 1.5f;

    [Provenance(ProvenanceKind.Decoded, "GetValidDeathDirection 0x11206c00: second floor line at 2.5 R")]
    public const float DeathFloorFar = 2.5f;

    [Provenance(ProvenanceKind.Decoded, "GetValidDeathDirection 0x11206c00: SingleLineCheck flags 0x2df")]
    public const uint DeathFloorTraceFlags = 0x2df;

    [Provenance(ProvenanceKind.Decoded, "ClientDamage: merc Shake(800, 20000, 500)")]
    public const int MercHitShake = 800;

    [Provenance(ProvenanceKind.Decoded, "ClientDamage: spy Tilt(400, 20000, 500)")]
    public const int SpyHitTilt = 400;

    [Provenance(ProvenanceKind.Decoded, "ClientDamage: spy Shake(100, 20000, 500)")]
    public const int SpyHitShake = 100;

    [Provenance(ProvenanceKind.Decoded, "ClientDamage camera speed 20000")]
    public const int HitShakeSpeed = 20000;

    [Provenance(ProvenanceKind.Decoded, "ClientDamage camera fade-out 500")]
    public const int HitShakeFadeOut = 500;

    [Provenance(ProvenanceKind.Decoded, "ClientDamage: fLyingOnGroundDuration 1.0 after a drop by the same SPawnType")]
    public const float SameTypeDropLyingTime = 1f;

    [Provenance(ProvenanceKind.Decoded, "PlayAnimDmg: snd_HeartBeat radius 100")]
    public const float HeartBeatRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.GetApproxPillFromHit head threshold 0.68")]
    public const float ApproxHeadThreshold = 0.68f;

    // ---- spawn protection and points (spec/combat/spawn-protection-and-scoring.md) ------------------------------

    [Provenance(ProvenanceKind.Decoded, "StandardReactionsToDamage: fSpawnProtectionEnd - 3 + 1 (MercDMGunProtectionTime)")]
    public const float MercDmGunProtection = 2f;

    [Provenance(ProvenanceKind.Decoded, "LevelInfo POINT index AP_D_KillWithWeapon")]
    public const int AP_D_KillWithWeapon = 0;

    [Provenance(ProvenanceKind.Decoded, "LevelInfo POINT index AP_A_Grab_Begin")]
    public const int AP_A_Grab_Begin = 9;

    [Provenance(ProvenanceKind.Decoded, "LevelInfo POINT index AP_D_KnockOut")]
    public const int AP_D_KnockOut = 16;

    [Provenance(ProvenanceKind.Decoded, "LevelInfo POINT index AP_C_Suicide")]
    public const int AP_C_Suicide = 17;

    [Provenance(ProvenanceKind.Decoded, "LevelInfo POINT index AP_C_KillFriend")]
    public const int AP_C_KillFriend = 18;

    // ---- shock (spec/combat/shock-and-taser.md) -------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "s_Inactive.Checkshock: PillTag -10 becomes 5")]
    public const int InactiveTaserPill = 5;

    [Provenance(ProvenanceKind.Decoded, "s_GrabbedByPlayer.PawnTakeDamage: the holding spy is shocked with pill 2")]
    public const int ConductedShockPill = 2;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.Client_Jammed HUD text TextJammered, 2 s")]
    public const float JammerMessageTime = 2f;

    // ---- knock-out and terminate (spec/combat/knockout-and-terminate.md) ------------------------------------

    [Provenance(ProvenanceKind.Decoded, "s_LyingOnGround.EndState: fLyingOnGroundDuration reset to 7.0")]
    public const float DefaultLyingTime = 7f;

    [Provenance(ProvenanceKind.Decoded, "s_TerminatedPawn_Released.BeginState lying time 1.0")]
    public const float TerminateReleasedLyingTime = 1f;

    [Provenance(ProvenanceKind.Decoded, "FindPawnToElbowShotAndGoForIt: now - iLastWakeTime > 3.8 + 0.2")]
    public const float ElbowWakeProtection = 4f;

    [Provenance(ProvenanceKind.Decoded, "GoInSmoke: merc ignores smoke 2.0 s after fLastTimeWakeUp")]
    public const float SmokeWakeGrace = 2f;

    [Provenance(ProvenanceKind.Decoded, "FindPawnToReanimate(OrTerminate): 250 uu")]
    public const float ReviveRange = 250f;

    [Provenance(ProvenanceKind.Decoded, "FindPawnToReanimate(OrTerminate): facing dot > -0.5")]
    public const float ReviveFacing = -0.5f;

    [Provenance(ProvenanceKind.Decoded, "FindPawnToReanimateOrTerminate: feet height tolerance 10")]
    public const float TerminateHeightTolerance = 10f;

    [Provenance(ProvenanceKind.Decoded, "FindPawnToReanimateOrTerminate: standing spot 110 behind the victim")]
    public const float TerminateStandOffset = 110f;

    [Provenance(ProvenanceKind.Decoded, "FindPawnToReanimateOrTerminate: clearance box (35, 35, 80) at 95 above the feet")]
    public static readonly FVector TerminateClearanceExtent = new(35f, 35f, 80f);

    [Provenance(ProvenanceKind.Decoded, "FindPawnToReanimateOrTerminate: clearance box centre 95 above the feet")]
    public const float TerminateClearanceRaise = 95f;

    [Provenance(ProvenanceKind.Decoded, "AskServerToTerminatePawn: 2D range 350")]
    public const float TerminateServerRange = 350f;

    [Provenance(ProvenanceKind.Decoded, "s_TerminatePawn_ReposBegin owner timeout 2.0 s")]
    public const float TerminateApproachTimeout = 2f;

    [Provenance(ProvenanceKind.Decoded, "s_TransitionTPK.BeginState: PawnTakeDamage(300, SDTBase)")]
    public const int TerminateDamage = 300;

    [Provenance(ProvenanceKind.Decoded, "s_TerminatedPawn.BeginState victim check period 0.2 s")]
    public const float TerminatedCheckPeriod = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "PlayerFallingKO.MyNotifyLanded: minimum KO fall damage 10")]
    public const int KoFallMinDamage = 10;

    [Provenance(ProvenanceKind.Decoded, "PlayerFallingInerte.BeginState: 10 s timer to s_Dead")]
    public const float FallingInerteTimeout = 10f;

    [Provenance(ProvenanceKind.Decoded, "PlayerFallingInerte.BeginState: SetVelocityForDyingPawn(-200 * X)")]
    public const float DyingPushBack = 200f;

    [Provenance(ProvenanceKind.Decoded, "PlayerTick merc cough cadence 1.0 s")]
    public const float CoughPeriod = 1f;

    [Provenance(ProvenanceKind.Decoded, "PlayerTick cough sound RA_Tousse volume 10")]
    public const float CoughVolume = 10f;

    [Provenance(ProvenanceKind.Decoded, "PlayerTick cough sound radius 100")]
    public const float CoughRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "PCChangeServerState gas credit: attribution older than 0.5 s")]
    public const float GasCreditRefresh = 0.5f;

    // ---- grab (spec/combat/spy-grab.md) ---------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "AskToGrabPawn height tolerance 15")]
    public const float GrabHeightTolerance = 15f;

    [Provenance(ProvenanceKind.Decoded, "CanGrabPawn front cone dot > 0.75")]
    public const float GrabFrontCone = 0.75f;

    [Provenance(ProvenanceKind.Decoded, "AskToGrabPawn: G = hit + (0, 0, 1.1 * CollisionHeight)")]
    public const float GrabSpotRaise = 1.1f;

    [Provenance(ProvenanceKind.Decoded, "AskToGrabPawn: extent Z -= difference / 2 + 2")]
    public const float GrabExtentShrink = 2f;

    [Provenance(ProvenanceKind.Decoded, "AskServerToGrabPawn doubles fGrabDistance for its test")]
    public const float GrabServerDistanceFactor = 2f;

    [Provenance(ProvenanceKind.Decoded, "s_Grabbing_GotoPosition owner timeout 3.0 s")]
    public const float GrabApproachTimeout = 3f;

    [Provenance(ProvenanceKind.Decoded, "s_Grabbing_GotoPosition height abort 32")]
    public const float GrabApproachHeightAbort = 32f;

    [Provenance(ProvenanceKind.Decoded, "s_GrabbedByPlayer_Begin.Timer escape speed 500")]
    public const float GrabEscapeSpeed = 500f;

    [Provenance(ProvenanceKind.Decoded, "s_GrabbedByPlayer_Begin.BeginState timer 0.001 s")]
    public const float GrabEscapeCheckPeriod = 0.001f;

    [Provenance(ProvenanceKind.Decoded, "s_GrabbedByPlayer_Begin.PlayerMove DampRotation 20000 units/s")]
    public const float GrabbedTurnRate = 20000f;

    [Provenance(ProvenanceKind.Decoded, "s_GrabbingPlayer.BeginState 15.0 s hold timer")]
    public const float GrabHoldLimit = 15f;

    [Provenance(ProvenanceKind.Decoded, "AskServerToUnGrabPawn: int(default.Health * 0.51)")]
    public const float GrabReleaseHealthFactor = 0.51f;

    [Provenance(ProvenanceKind.Decoded, "AskServerToUnGrabPawn: GrabbingDamaged and BothDie keep min(Health, 10)")]
    public const int GrabBothDieHealthCap = 10;

    [Provenance(ProvenanceKind.Decoded, "s_GrabbedByPlayer_Released_STRANGLE: dead after 4.0 s")]
    public const float StrangleToDead = 4f;

    [Provenance(ProvenanceKind.Decoded, "s_GrabbedByPlayer_Released_STRANGLE: fTempsAvantClignote 1.0")]
    public const float StrangleBlinkDelay = 1f;

    [Provenance(ProvenanceKind.Decoded, "s_GrabbedByPlayer.BeginState HUD perturbation 17 s")]
    public const float GrabbedHudPerturbation = 17f;

    /// <summary>The HUD perturbation of an ordinary hit (merc only), from ClientDamageHudEffect.</summary>
    [Provenance(ProvenanceKind.Decoded, "ClientDamageHudEffect game 0x10ab09c5: SetHudPerturbationLength(1.5) for every type but 7, 8 and 12, with a pawn")]
    public const float DamageHudPerturbation = 1.5f;

    [Provenance(ProvenanceKind.Decoded, "s_GrabbedByPlayer.AnimEnd: the held loop is replayed only 0.5 s after the hold began")]
    public const float GrabbedReloopDelay = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "s_GrabbingPlayer.PlayerMove input clamp 6000")]
    public const float GrabInputClamp = 6000f;

    [Provenance(ProvenanceKind.Decoded, "s_GrabbingPlayer.PlayerMove GroundSpeed = RunningSpeed * |accel| / 3000")]
    public const float GrabSpeedDivisor = 3000f;

    [Provenance(ProvenanceKind.Decoded, "s_GrabbingPlayer.PlayerMove stop below 0.47 * GrabbingSpeed")]
    public const float GrabStopFactor = 0.47f;

    [Provenance(ProvenanceKind.Decoded, "spy-grab.md rule 13: the carry turn uses 0.6 of the inputs")]
    public const float GrabCarryInputShare = 0.6f;

    [Provenance(ProvenanceKind.Decoded, "spy-grab.md rule 13: carry turn 48000 yaw units/s (80000 * 0.6)")]
    public const float GrabCarryTurnRate = 48000f;

    [Provenance(ProvenanceKind.Decoded, "spy-grab.md rule 13: carry turn step capped at 32000 / 48000 s")]
    public const float GrabCarryMaxStep = 32000f;

    [Provenance(ProvenanceKind.Decoded, "spy-grab.md rule 13: view turn 50 * DeltaTime * input * sensitivity")]
    public const float GrabViewRate = 50f;

    [Provenance(ProvenanceKind.Decoded, "spy-grab.md rule 13: view pitch limited to 15000 up")]
    public const int GrabViewPitchUp = 15000;

    [Provenance(ProvenanceKind.Decoded, "spy-grab.md rule 13: view pitch limited to 50536 down (unsigned)")]
    public const int GrabViewPitchDown = 50536;

    [Provenance(ProvenanceKind.Decoded, "spy-grab.md rule 12: held merc camera on B Head offset (13, 0, 9)")]
    public const float GrabbedCameraX = 13f;

    [Provenance(ProvenanceKind.Decoded, "spy-grab.md rule 12: held merc camera on B Head offset (13, 0, 9)")]
    public const float GrabbedCameraZ = 9f;

    [Provenance(ProvenanceKind.Decoded, "spy-grab.md rule 23: the strangled merc's view turns 180 degrees")]
    public const int HalfTurn = 32768;

    [Provenance(ProvenanceKind.Decoded, "GetFreeLocationToRelease push back + 30")]
    public const float ReleasePushMargin = 30f;

    [Provenance(ProvenanceKind.Decoded, "CheckForGround probe 35 down, P.Z + 2")]
    public const float GroundProbeDepth = 35f;

    [Provenance(ProvenanceKind.Decoded, "CheckForGround: P.Z += 2, extent height - 2")]
    public const float GroundProbeLift = 2f;

    [Provenance(ProvenanceKind.Decoded, "GetFreeLocationToRelease yaw quarter 16384 and sector half 8192")]
    public const int ReleaseYawQuarter = 16384;

    [Provenance(ProvenanceKind.Decoded, "GetFreeLocationToRelease yaw offset 8192")]
    public const int ReleaseYawEighth = 8192;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 20: has fallen unconscious")]
    public const int MsgFallenUnconscious = 20;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 57: You win counter grab match")]
    public const int MsgWinCounterGrab = 57;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 58: You lose counter grab match")]
    public const int MsgLoseCounterGrab = 58;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 17: knocked down by a strike attack")]
    public const int MsgStrikeKnockDown = 17;

    [Provenance(ProvenanceKind.Decoded, "s_GrabRelease_Counter lying time fKOAfterCounterGrab 3.0")]
    public const float CounterGrabLyingTime = 3f;

    [Provenance(ProvenanceKind.Decoded, "s_GrabbedByPlayer_Released_KO sleep overlay 0 to 255 over 3 s")]
    public const float KoReleaseOverlayTime = 3f;

    // ---- ledge grab (spec/combat/spy-grab.md rules 30 to 36) ----

    [Provenance(ProvenanceKind.Decoded, "OnLedge.CanGrabPawn: ideal merc Z = spy Z + 210 - spy CH + merc CH")]
    public const float LedgeGrabHeight = 210f;

    [Provenance(ProvenanceKind.Decoded, "OnLedge.CanGrabPawn: |ideal Z - merc Z| < 10")]
    public const float LedgeGrabHeightTolerance = 10f;

    [Provenance(ProvenanceKind.Decoded, "AskServerToGrabPawnFromLedge: fGrabFromLedgeDist1 and Dist2 times 1.5")]
    public const float LedgeGrabServerBoxFactor = 1.5f;

    [Provenance(ProvenanceKind.Decoded, "AskServerToGrabPawnFromLedge: pull spot m_LedgeGrabArmsZone.X + 16 + CollisionRadius + 4 ahead")]
    public const float LedgeGrabArmsMargin = 16f;

    [Provenance(ProvenanceKind.Decoded, "AskServerToGrabPawnFromLedge: pull spot m_LedgeGrabArmsZone.X + 16 + CollisionRadius + 4 ahead")]
    public const float LedgeGrabRadiusMargin = 4f;

    [Provenance(ProvenanceKind.Decoded, "AskServerToGrabPawnFromLedge: merc box trace (35, 35, 87.5) to the pull spot")]
    public static readonly FVector LedgeGrabPathExtent = new(35f, 35f, 87.5f);

    [Provenance(ProvenanceKind.Decoded, "s_GrabbingFromLedge_Begin and s_GrabbedFromLedge_Begin SetTimer(1.5)")]
    public const float LedgeGrabTimeout = 1.5f;

    [Provenance(ProvenanceKind.Decoded, "s_GrabbedFromLedge_Begin.PlayerMove: abort after 3.0 s out of root motion")]
    public const float LedgeGrabbedAbortTime = 3f;

    [Provenance(ProvenanceKind.Decoded, "s_FallAfterGrabbedFromLedge.MyNotifyLanded DamagePawn_C2S 200 DamTypeGrabBreak")]
    public const int LedgeGrabFallDamage = 200;

    [Provenance(ProvenanceKind.Decoded, "s_FallAfterGrabbedFromLedge.NotifyBump: Other.Z + 0.8 Other.CH <= Z - CH")]
    public const float LedgeFallHeadShare = 0.8f;

    // ---- sticky blast (SPlayerController.StickyBlast) ----

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.StickyBlast DamagePawn_C2S 200 SDTStickyBlast")]
    public const int StickyBlastDamage = 200;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.StickyBlast: vDir = vector(Rotation), vDir.Z = -1.5")]
    public const float StickyBlastDirZ = -1.5f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.StickyBlast: velocity -2 * BlastSpeedMin * Normal(vDir)")]
    public const float StickyBlastSpeedFactor = -2f;

    // ---- melee (spec/combat/melee-elbow-charge-berserk.md) --------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "FindPawnToElbowShotAndGoForIt reach 150")]
    public const float ElbowReach = 150f;

    [Provenance(ProvenanceKind.Decoded, "AskServerToElbowAttack reach 350")]
    public const float ElbowServerReach = 350f;

    [Provenance(ProvenanceKind.Decoded, "FindPawnToElbowShotAndGoForIt merc cone dot > 0.5")]
    public const float ElbowMercCone = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "FindPawnToElbowShotAndGoForIt spy cone dot > 0.8")]
    public const float ElbowSpyCone = 0.8f;

    [Provenance(ProvenanceKind.Decoded, "FindPawnToElbowShotAndGoForIt box (35, 35, 87.5)")]
    public static readonly FVector ElbowBox = new(35f, 35f, 87.5f);

    [Provenance(ProvenanceKind.Decoded, "drop-attack.md: PawnTakeDamage(17, ..., SDamage_DropAttacked, 1)")]
    public const int DropAttackDamage = 17;

    [Provenance(ProvenanceKind.Decoded, "drop-attack.md: the drop hits pill 1")]
    public const int DropAttackPill = 1;

    [Provenance(ProvenanceKind.Decoded, "drop-attack.md: SGameMessages 16 has neutralized")]
    public const int MsgNeutralized = 16;

    [Provenance(ProvenanceKind.Decoded, "melee rule 8: a merc's view turns with UpdateRotation(dt, 3.0) during the strike")]
    public const float ElbowRotationPitch = 3f;

    [Provenance(ProvenanceKind.Decoded, "melee rule 4: the attack clips play on pawn channel 19")]
    public const int AttackChannel = 19;

    [Provenance(ProvenanceKind.Decoded, "melee rule 3: post-charge input factor 0.5 for 2 s")]
    public const float PostChargeSlowFactor = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "melee rule 3: post-charge input factor 0.5 for 2 s")]
    public const float PostChargeSlowTime = 2f;

    [Provenance(ProvenanceKind.Decoded, "AskServerToElbowAttack PercentDamageRefillable 100")]
    public const int MeleeRefillable = 100;

    [Provenance(ProvenanceKind.Decoded, "AskServerToElbowAttack ForceRotateTo step 3")]
    public const int ForceRotateStep = 3;

    [Provenance(ProvenanceKind.Decoded, "s_ElbowShooting.BeginState sound radius 100")]
    public const float MeleeSoundRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "s_ElbowShot_GoBackward cross-eye blur over 2 + 5c s")]
    public const float ElbowBlurBase = 2f;

    [Provenance(ProvenanceKind.Decoded, "s_ElbowShot_GoBackward cross-eye blur over 2 + 5c s")]
    public const float ElbowBlurScale = 5f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerCharging.BeginState timer 0.6 s")]
    public const float ChargeFirstPhase = 0.6f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerCharging.FinDeCharge second timer 0.4 s")]
    public const float ChargeSecondPhase = 0.4f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerCharging.PlayerMove k = dt * 50")]
    public const float ChargeSteerRate = 50f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerCharging.PlayerMove target weight 14 * (t + 1)")]
    public const float ChargeTargetWeight = 14f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerCharging.PlayerMove GroundSpeed ramp 750 per s")]
    public const float ChargeSpeedRamp = 750f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerCharging.PlayerMove GroundSpeed clamp 400")]
    public const float ChargeSpeedMin = 400f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerCharging.PlayerMove GroundSpeed clamp 770")]
    public const float ChargeSpeedMax = 770f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerCharging.PlayerMove acceleration scale 7.7922")]
    public const float ChargeAccelScale = 7.7922f;

    [Provenance(ProvenanceKind.Decoded, "FindTargetToCharge cone dot > 0.8")]
    public const float ChargeTargetCone = 0.8f;

    [Provenance(ProvenanceKind.Decoded, "FindTargetToCharge range 1500")]
    public const float ChargeTargetRange = 1500f;

    [Provenance(ProvenanceKind.Decoded, "FindTargetToCharge target point lowered 10")]
    public const float ChargeTargetDrop = 10f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerCharging.PlayerTick camera tilt every 0.35 s")]
    public const float ChargeTiltPeriod = 0.35f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerCharging_HitStickyShocked 3.5 s timer")]
    public const float ChargeSelfShockRecovery = 3.5f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerCharging.NotifyHitWall HUD perturbation 2.0 s")]
    public const float ChargeWallPerturbation = 2f;

    [Provenance(ProvenanceKind.Decoded, "s_Berserk360.NotifyBump hit window: channel 0 position below 0.8")]
    public const float BerserkHitWindow = 0.8f;

    [Provenance(ProvenanceKind.Decoded, "s_Charge_GoBackward merc HUD perturbation 1.5 s")]
    public const float ChargeVictimPerturbation = 1.5f;

    [Provenance(ProvenanceKind.Decoded, "s_Charge_GoBackward merc sleep overlay over 3.5 s")]
    public const float ChargeVictimOverlay = 3.5f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerCharging.BeginState HUD pulse FRand() * 0.5 + 0.25")]
    public const float ChargePulseBase = 0.25f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerCharging.BeginState HUD pulse FRand() * 0.5 + 0.25")]
    public const float ChargePulseRandom = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "s_PlayerCharging.PlayerTick HUD pulse 0.16 s")]
    public const float ChargePulseLength = 0.16f;

    // ---- blast (spec/combat/blast-and-fall-damage.md) -------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "s_Blast.MyNotifyLanded: landing ignored for 0.3 s")]
    public const float BlastLandingGrace = 0.3f;

    [Provenance(ProvenanceKind.Decoded, "s_Blast.PlayerMove probe box (35, 35, 87.5)")]
    public static readonly FVector BlastProbeExtent = new(35f, 35f, 87.5f);

    [Provenance(ProvenanceKind.Decoded, "s_Blast.PlayerMove probe length Velocity * dt * 1.2")]
    public const float BlastProbeScale = 1.2f;

    [Provenance(ProvenanceKind.Decoded, "s_Blast.PlayerMove obstacle normal Z < 0.5")]
    public const float BlastObstacleNormalZ = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "s_Blast.PlayerMove obstacle facing < -0.7")]
    public const float BlastObstacleFacing = -0.7f;

    [Provenance(ProvenanceKind.Decoded, "s_BlastFallAfterObstacle: 4.0 s fall limit")]
    public const float BlastObstacleFallLimit = 4f;

    [Provenance(ProvenanceKind.Decoded, "s_SleepAfterKOBlastFall: 3.0 s")]
    public const float BlastObstacleSleep = 3f;

    [Provenance(ProvenanceKind.Decoded, "ServerSetVelocityAndOrientationForBlast(100 * v), applied as arg * 0.01")]
    public const float BlastVelocityWireScale = 100f;
}
