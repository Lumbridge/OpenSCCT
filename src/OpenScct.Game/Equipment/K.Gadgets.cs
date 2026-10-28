using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

// Constants of the default gadgets beyond the grenades (spec/equipment/sticky-cam.md, heartbeat-sensor.md,
// chaff-grenade.md, mine-launcher.md, camera-network-gadget.md, gas-mask.md, spy-binoculars.md, gadget-natives.md).
public static partial class K
{
    // ---- sticky cam (sticky-cam.md) --------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "UseStickyCam 0x10aa1f9b: start 10 uu behind the FX bone")]
    public const float StickyLaunchPullBack = 10f;

    [Provenance(ProvenanceKind.Decoded, "UseStickyCam 0x10aa21f3: the cam point 2.5 uu back from the hit")]
    public const float StickyHitPullBack = 2.5f;

    [Provenance(ProvenanceKind.Decoded, "UseStickyCam 0x10aa2281: corner probes of 5.5 uu")]
    public const float StickyProbe = 5.5f;

    [Provenance(ProvenanceKind.Decoded, "UseStickyCam 0x10aa2367: a probe hit pushes the cam 5 uu off it")]
    public const float StickyProbePush = 5f;

    [Provenance(ProvenanceKind.Decoded, "UseStickyCam: the side probe axis is the cam rotation with yaw + 16384")]
    public const int StickySideYaw = 16384;

    [Provenance(ProvenanceKind.Decoded, "SGadStickyCam const PredictionTimeout 0.8")]
    public const float StickyPredictionTimeout = 0.8f;

    [Provenance(ProvenanceKind.Decoded, "SpawnCam: rotation cut to its high byte (Rot & 65280)")]
    public const int NetRotationMask = 0xFF00;

    [Provenance(ProvenanceKind.Decoded, "SStickyCam.PostBeginPlay SetCollisionSize(20, 20)")]
    public const float StickyCamCollision = 20f;

    [Provenance(ProvenanceKind.Decoded, "SStickyCamInteraction.PostBeginPlay SetCollisionSize(75, 75)")]
    public const float StickyCamInteractionCollision = 75f;

    [Provenance(ProvenanceKind.Decoded, "SStickyCam.Tick: drawn at 0.1 while its user looks through it")]
    public const float SelfViewScale = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "SStickyCam.SpawnGaz PlaySound radius 100")]
    public const float StickyGasSoundRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "SStickyCamEmitter gas point helper 0x10aa3130: Location + 192 x forward")]
    public const float StickyGasAhead = 192f;

    [Provenance(ProvenanceKind.Decoded, "SStickyCamEmitter scan 0x10aa31f0: squared distance under 16384")]
    public const float StickyGasRadiusSquared = 16384f;

    [Provenance(ProvenanceKind.Decoded, "SStickyCamEmitter scan 0x10aa31f0: awake for more than 2 s")]
    public const float StickyGasWakeGrace = 2f;

    [Provenance(ProvenanceKind.Decoded, "SStickyCamEmitter.PawnsGazes[3]")]
    public const int StickyGasVictims = 3;

    [Provenance(ProvenanceKind.Decoded, "SStickyCamInteraction.InitInteract hit location marker (-1, -2, -3)")]
    public static readonly OpenScct.Core.FVector StickyPickupMarker = new(-1f, -2f, -3f);

    [Provenance(ProvenanceKind.Decoded, "s_UseObject_StickyCam zoom keys 500 x fCoeffZoomSpeed per second")]
    public const float CamZoomKeyRate = 500f;

    [Provenance(ProvenanceKind.Decoded, "s_UseObject_StickyCam wheel 3000 x fCoeffZoomSpeed x m_dT")]
    public const float CamZoomWheelRate = 3000f;

    [Provenance(ProvenanceKind.Decoded, "s_UseObject.UpdateRotation input dead zone 150")]
    public const float CamInputDeadZone = 150f;

    [Provenance(ProvenanceKind.Decoded, "s_UseObject.UpdateRotation 50 x sensitivity x dt x input x FOV / 110")]
    public const float CamTurnScale = 50f;

    [Provenance(ProvenanceKind.Decoded, "s_UseObject.UpdateRotation FOV reference 110")]
    public const float CamTurnFovReference = 110f;

    [Provenance(ProvenanceKind.Decoded, "s_UseObject.UpdateRotation rotation sound above 2 units")]
    public const float CamRotationSoundStep = 2f;

    [Provenance(ProvenanceKind.Decoded, "SOrientableObject.Tick remote jump over 50 units plays the rotation sound")]
    public const float CamRemoteRotationSound = 50f;

    [Provenance(ProvenanceKind.Decoded, "SOrientableObject.Tick sounds stop 0.2 s after their last request")]
    public const float CamSoundLinger = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "s_UseObject_StickyCam_SmoothToPosition.BeginState SetFOV(110)")]
    public const float CamEntryFov = 110f;

    [Provenance(ProvenanceKind.Decoded, "SGadStickyCam / s_StickyCam_MenuInteraction: three menu items")]
    public const int StickyMenuItems = 3;

    [Provenance(ProvenanceKind.Decoded, "SGadStickyCam.Use / menu messages for 3 s")]
    public const float StickyMessageTime = 3f;

    /// <summary>The Give entry's "gave" message stays 3 s (AddMessageToStack time 3).</summary>
    [Provenance(ProvenanceKind.Decoded, "s_MenuInteraction.PlayerMove: AddMessageToStack(..., 3)")]
    public const int ShareMessageTime = 3;

    // ---- heartbeat sensor (heartbeat-sensor.md) ---------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "CheckForPawn 0x10abd0e0 StateOfDetect values")]
    public const int HbsIdle = 0;

    [Provenance(ProvenanceKind.Decoded, "CheckForPawn 0x10abd0e0 StateOfDetect values")]
    public const int HbsDetected = 1;

    [Provenance(ProvenanceKind.Decoded, "CheckForPawn 0x10abd0e0 StateOfDetect values")]
    public const int HbsNothing = 2;

    // ---- chaff (chaff-grenade.md) -----------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SProjChaffe.Timer Explode(Location, vect(0, 0, 10))")]
    public static readonly OpenScct.Core.FVector ChaffExplodeNormal = new(0f, 0f, 10f);

    [Provenance(ProvenanceKind.Decoded, "ChaffExplodeEffect 0x10abe760: players hear the burst at radius 100, volume 1")]
    public const float ChaffSoundRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "ChaffExplodeEffect NotifyNoise(1.0)")]
    public const float ChaffNoise = 1f;

    // ---- frag (frag-grenade.md, natives2-damage.md rule 4) ------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 4: NotifyNoise(1.0)")]
    public const float FragNoise = 1f;

    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 4: every player hears SoundExplode at radius 500")]
    public const float FragSoundRadius = 500f;

    // ---- mines (mine-launcher.md) -----------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "UseMine 0x10a9fd2d: the wall within 55 uu of the eye")]
    public const float MineReach = 55f;

    [Provenance(ProvenanceKind.Decoded, "UseMine 0x10a9fcc1: only when the view's forward Z is at most 0.4")]
    public const float MineMaxViewUp = 0.4f;

    [Provenance(ProvenanceKind.Decoded, "UseMine: a hit normal with |Z| > 0.5 is a floor or ceiling")]
    public const float MineMaxNormalZ = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "UseMine: another mine within 12.5 uu (156.25 squared)")]
    public const float MineSpacingSquared = 156.25f;

    [Provenance(ProvenanceKind.Decoded, "UseMine: a terminal within 45 uu (2025 squared)")]
    public const float MineTerminalSpacingSquared = 2025f;

    [Provenance(ProvenanceKind.Decoded, "UseMine / DropMineOnWall: the opposite wall probe starts 2 uu off the wall")]
    public const float MineWallProbeStart = 2f;

    [Provenance(ProvenanceKind.Decoded, "DropMineOnWall: the mine 5 uu off the wall")]
    public const float MineWallOffset = 5f;

    [Provenance(ProvenanceKind.Decoded, "SpawnMine: Trace 20 uu back into the wall")]
    public const float MineSnapTrace = 20f;

    [Provenance(ProvenanceKind.Decoded, "SpawnMine: snap within 256 rotation units, to whole 256 steps")]
    public const int MineSnapStep = 256;

    [Provenance(ProvenanceKind.Decoded, "DropMineOnWall: AddMessageOnHud(3) when the opposite wall is missing")]
    public const int MsgNoOppositeWall = 3;

    [Provenance(ProvenanceKind.Decoded, "DropMineOnWall / SpawnText for 3 s; TextTooManyMineInMap 3 s")]
    public const float MineMessageTime = 3f;

    [Provenance(ProvenanceKind.Decoded, "ReloadMine MineOff message 2 s")]
    public const float MineOffMessageTime = 2f;

    [Provenance(ProvenanceKind.Decoded, "s_PutMineOnWall_Grab TimeChoiceGadget = now + 1")]
    public const float MineGrabChoiceDelay = 1f;

    [Provenance(ProvenanceKind.Decoded, "SMines.PostBeginPlay placement sound radius 24")]
    public const float MinePoseSoundRadius = 24f;

    [Provenance(ProvenanceKind.Decoded, "SMineProx/SMineLaser.PostBeginPlay refloc = Location + 5 X")]
    public const float MineRefAhead = 5f;

    [Provenance(ProvenanceKind.Decoded, "SMineProx.s_Activated SetTimer(0.5, true)")]
    public const float ProxScanPeriod = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "CheckArea 0x10ac1128: BipBeforeExplode volume 1.5, radius 1000")]
    public const float ProxBipRadius = 1000f;

    [Provenance(ProvenanceKind.Decoded, "CheckArea 0x10ac1138: BipBeforeExplode volume 1.5")]
    public const float ProxBipVolume = 1.5f;

    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 5: every player hears the mine explosion at radius 1000")]
    public const float MineExplosionSoundRadius = 1000f;

    [Provenance(ProvenanceKind.Decoded, "SMineLaser.s_Activated SetTimer(3 / 60, true)")]
    public const float LaserScanPeriod = 3f / 60f;

    [Provenance(ProvenanceKind.Decoded, "ASMineLaser::Tick 0x10a93c40: re-measure the beam after 2 s")]
    public const float LaserRemeasure = 2f;

    [Provenance(ProvenanceKind.Decoded, "CheckForOppositeWall: the beam probe starts 2 uu ahead")]
    public const float LaserProbeStart = 2f;

    [Provenance(ProvenanceKind.Decoded, "CheckForOppositeWall: no wall gives fDistanceMax + 0.1")]
    public const float LaserNoWallExtra = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "CheckPresence 0x10c10dd8: close range 200")]
    public const float LaserCloseRange = 200f;

    [Provenance(ProvenanceKind.Decoded, "CheckPresence 0x10c10dd0: cone cosine 0.9")]
    public const float LaserConeCos = 0.9f;

    [Provenance(ProvenanceKind.Decoded, "SMinePiegeSpy.CheckForOppositeWall 0x10ab5330: the line starts 1 uu ahead of the trap")]
    public const float TrapProbeStart = 1f;

    [Provenance(ProvenanceKind.Decoded, "SMinePiegeSpy.SpawnLaser 0x10ab5510: beam HeatIntensity 0.4")]
    public const float TrapBeamHeat = 0.4f;

    [Provenance(ProvenanceKind.Decoded, "SMinePoison.CheckPresence 0x10ab43a0: gas and projectile sounds, volume 1, radius 100")]
    public const float PoisonSoundRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "SMinePoison.CheckPresence 0x10ab43a0: momentum (hit - mine) x 1000")]
    public const float PoisonMomentumScale = 1000f;

    [Provenance(ProvenanceKind.Decoded, "SMineLaser sounds radius fDistanceMax / 100")]
    public const float LaserSoundRadiusDivisor = 100f;

    // ---- camera network (camera-network-gadget.md) ------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SCamNetwork.Fire ServerPlaySound radius 200")]
    public const float CamShotSoundRadius = 200f;

    [Provenance(ProvenanceKind.Decoded, "SCamNetwork.Fire empty sound radius 60")]
    public const float CamEmptySoundRadius = 60f;

    [Provenance(ProvenanceKind.Decoded, "ServerFire 0x10ae9030: the shot starts 20 uu ahead of the camera")]
    public const float CamShotStart = 20f;

    [Provenance(ProvenanceKind.Decoded, "ServerFire 0x10ae9030: the shot is 15000 uu long")]
    public const float CamShotLength = 15000f;

    // ---- gas mask (gas-mask.md) -------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SGadGasMask.Activate / Tick: choking below 7.5 s of air")]
    public const float GasMaskChoking = 7.5f;

    [Provenance(ProvenanceKind.Decoded, "SGadGasMask.Use PlaySound radius 100")]
    public const float GasMaskSoundRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "SGadGasMask.Load message 2 s")]
    public const float GasMaskMessageTime = 2f;

    [Provenance(ProvenanceKind.Decoded, "SGadGasMask.SetAmbientSoundOnServer values")]
    public const byte GAD_STOPSOUND = 0;

    [Provenance(ProvenanceKind.Decoded, "SGadGasMask.SetAmbientSoundOnServer values")]
    public const byte GAD_RESPIRE = 1;

    [Provenance(ProvenanceKind.Decoded, "SGadGasMask.SetAmbientSoundOnServer values")]
    public const byte GAD_ETOUFFE = 2;

    // ---- spy binoculars (spy-binoculars.md) -------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Att zoom steps 30 / 20 / 10")]
    public const float SpyBinoFov1 = 30f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Att zoom steps 30 / 20 / 10")]
    public const float SpyBinoFov2 = 20f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Att zoom steps 30 / 20 / 10")]
    public const float SpyBinoFov3 = 10f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Att.PlayerMove zoom animation 0.2 s plus the frame")]
    public const float SpyBinoZoomTime = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Att.PlayerMove FOV ramp rate 5")]
    public const float SpyBinoZoomRate = 5f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Att.ZoomIn/ZoomOut BinocularNoiseDuration 15")]
    public const int SpyBinoNoiseFrames = 15;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Att.UpdateCamRotation x 5")]
    public const float SpyBinoTurnScale = 5f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Att.UpdateCamRotation pitch clamp -12000")]
    public const float SpyBinoPitchMin = -12000f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Att.UpdateCamRotation pitch clamp 13000")]
    public const float SpyBinoPitchMax = 13000f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Att.PlayerMove voice trace from 50 uu ahead")]
    public const float SpyBinoVoiceStart = 50f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Att.PlayerMove voice trace AudioZoomFactor x 6000")]
    public const float SpyBinoVoiceRange = 6000f;

    [Provenance(ProvenanceKind.Estimated, "Server check of a claimed voice target: the trace's longest reach plus a second of movement")]
    public const float VoiceTargetServerSlack = 1000f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Att.PlayerMove mark at most once per second")]
    public const float SpyBinoMarkRepeat = 1f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Att.PlayerMove ground speed cut 25 crouched")]
    public const float SpyBinoMinSpeedCrouched = 25f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Att.PlayerMove ground speed cut 40 standing")]
    public const float SpyBinoMinSpeedStanding = 40f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Att.ProcessCrouch 0.5 s special move guard")]
    public const float SpyBinoCrouchGuard = 0.5f;

    [Provenance(ProvenanceKind.Estimated, "DrawObjectivesBinocularsAtt picks the objective in a 40 px box at the screen centre; the port uses a view cone of about 2.5 degrees at 30 degrees FOV (cosine 0.999)")]
    public const float SpyBinoMarkConeCos = 0.999f;

    [Provenance(ProvenanceKind.Decoded, "SGoggle.SwitchMode 10: the spy binoculars view")]
    public const int GoggleBinocularsKey = 10;

    // ---- motion tracking (motion-tracking.md) -----------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SMotionTrackingVision.s_Active.BeginState SetTimer(0.5, true)")]
    public const float MotionScanPeriod = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "natives2-events.md rule 5: detectsound at the viewer, volume 1, radius 64")]
    public const float MotionDetectSoundRadius = 64f;

    [Provenance(ProvenanceKind.Decoded, "natives2-events.md rule 6: a cloaked spy has DesiredGlassEffect 255")]
    public const byte MotionCloakedGlass = 255;

    // ---- alarm snare (alarm-snare.md) ------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "UseEmitter 0x10aa1240: start 10 uu behind the FX bone")]
    public const float SnareLaunchPullBack = 10f;

    [Provenance(ProvenanceKind.Decoded, "SpawnNoiseEmitter 0x10aa1570: the launch sound at radius 100")]
    public const float SnareLaunchSoundRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "SNoiseEmitter.PostBeginPlay SetTimer(0.5) and fSpeed = 50")]
    public const float SnareFirstNoise = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "SNoiseEmitter.PostBeginPlay fSpeed = 50")]
    public const float SnareStartSpeed = 50f;

    [Provenance(ProvenanceKind.Decoded, "SNoiseEmitter.Tick: fVal += FRand() * 0.48, past 1.0 restart at FRand() * 0.16")]
    public const float SnareArrowStep = 0.48f;

    [Provenance(ProvenanceKind.Decoded, "SNoiseEmitter.Tick: restart at FRand() * 0.16")]
    public const float SnareArrowRestart = 0.16f;

    [Provenance(ProvenanceKind.Decoded, "SNoiseEmitter.Timer: FRand() < 0.2 picks a breath")]
    public const float SnareBreathChance = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "SNoiseEmitter.Timer: Action = int(FRand() * 2.9)")]
    public const float SnareBreathPick = 2.9f;

    [Provenance(ProvenanceKind.Decoded, "SNoiseEmitter.Timer: fSpeed = FRand() * 160 + 10")]
    public const float SnareSpeedRange = 160f;

    [Provenance(ProvenanceKind.Decoded, "SNoiseEmitter.Timer: fSpeed = FRand() * 160 + 10")]
    public const float SnareSpeedMin = 10f;

    [Provenance(ProvenanceKind.Decoded, "SNoiseEmitter.Timer: NotifyNoise(fNoiseCoeff * fSpeed / 200)")]
    public const float SnareNoiseSpeed = 200f;

    [Provenance(ProvenanceKind.Decoded, "SNoiseEmitter.Timer: WalkingSounds[int(fSpeed / 67)]")]
    public const float SnareFootstepBand = 67f;

    [Provenance(ProvenanceKind.Decoded, "SNoiseEmitter.Timer: footsteps at radius 1000")]
    public const float SnareFootstepRadius = 1000f;

    [Provenance(ProvenanceKind.Decoded, "SNoiseEmitter.Timer: SetTimer(40 / fSpeed, true)")]
    public const float SnareStepDistance = 40f;

    [Provenance(ProvenanceKind.Decoded, "SNoiseEmitter.Timer: FakeAlarms again after the alarm's Duration + Rand(5)")]
    public const int SnareFakeJitter = 5;

    [Provenance(ProvenanceKind.Decoded, "SNoiseEmitter.FakeAlarms: sight to the point 5 uu along the device's X axis")]
    public const float SnareDeviceProbe = 5f;

    [Provenance(ProvenanceKind.Decoded, "SGadNoiseEmitter.LocalSystemMessage AddMessageOnHud(5)")]
    public const int SnareHudOn = 5;

    [Provenance(ProvenanceKind.Decoded, "SNoiseEmitter.Destroyed AddMessageOnHud(6)")]
    public const int SnareHudOff = 6;

    // ---- gadget HUD views (gadget-hud.md) --------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "gas mask page method 0x10b27ec0: gauge frame 2 x (30 - trunc(MaskDuration))")]
    public const int GasMaskGaugeSeconds = 30;

    [Provenance(ProvenanceKind.Decoded, "gas mask page method 0x10b27ec0: fast breathing under 15 s of air")]
    public const int GasMaskFastBreathSeconds = 15;

    [Provenance(ProvenanceKind.Decoded, "heart cone 0x10b2d7a0: the detected sound at most every 1.5 s")]
    public const float HeartDetectedSoundSpacing = 1.5f;

    // ---- host settings (loadouts.md rule 7) -------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "PersistentData.InitPersistentData: b_SPY_GadgetRestriction = b_MERC_GadgetRestriction = 255")]
    public const byte AllGadgetsAllowed = 255;
}
