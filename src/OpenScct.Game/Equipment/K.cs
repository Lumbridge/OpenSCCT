using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>Constants of the equipment port (spec/equipment), with their evidence.</summary>
public static partial class K
{
    // ---- impacts (spec/equipment/impact-manager.md) -----------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SImpactManager const TABSIZE = 50")]
    public const int ImpactSlots = 50;

    [Provenance(ProvenanceKind.Decoded, "SImpactManager const Lifetime = 15, TabTimer = int(Level.TimeSeconds + 15)")]
    public const float ImpactLifetime = 15f;

    [Provenance(ProvenanceKind.Decoded, "SImpactManager.PostBeginPlay SetTimer(1.0, true)")]
    public const float ImpactTimerPeriod = 1f;

    [Provenance(ProvenanceKind.Decoded, "SImpactManager.AddImpact decal at Loc + 10 * X")]
    public const float ImpactDecalOffset = 10f;

    [Provenance(ProvenanceKind.Decoded, "SImpactEffectBase.Timer PlaySound(SndBullet, , 0.8, , 100)")]
    public const float ImpactSoundVolume = 0.8f;

    [Provenance(ProvenanceKind.Decoded, "SImpactEffectBase.Timer PlaySound(SndBullet, , 0.8, , 100)")]
    public const float ImpactSoundRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "SImpactEffectBase.Effects SndSpeed = 43561")]
    public const float ImpactSoundSpeed = 43561f;

    // ---- merc rifle (spec/equipment/merc-rifle.md) --------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SWeaponDefense.TraceFire literal 15000")]
    public const float TraceLength = 15000f;

    [Provenance(ProvenanceKind.Decoded, "SWeaponDefense.TraceFire spread Accuracy * (FRand - 0.5) * 1000")]
    public const float SpreadScale = 1000f;

    [Provenance(ProvenanceKind.Decoded, "TraceFire momentum (HitLocation - StartTrace) * 1000 and -HitNormal * 1000")]
    public const float BulletMomentumScale = 1000f;

    [Provenance(ProvenanceKind.Decoded, "TraceFire moving-mover aim height crouched")]
    public const float MoverAimCrouched = 48f;

    [Provenance(ProvenanceKind.Decoded, "TraceFire moving-mover aim height standing")]
    public const float MoverAimStanding = 55f;

    [Provenance(ProvenanceKind.Decoded, "TraceFire FakeCeiling exit offset 16")]
    public const float FakeCeilingExit = 16f;

    [Provenance(ProvenanceKind.Decoded, "SWeaponDefense.GetFireStart laser pull-back 50")]
    public const float LaserFireStartPullBack = 50f;

    [Provenance(ProvenanceKind.Decoded, "CheckIfCanFire merc muzzle clearance from 56 to 38 behind FX")]
    public const float MuzzleClearanceFar = 56f;

    [Provenance(ProvenanceKind.Decoded, "CheckIfCanFire merc muzzle clearance from 56 to 38 behind FX")]
    public const float MuzzleClearanceNear = 38f;

    [Provenance(ProvenanceKind.Decoded, "CheckIfCanFire spy ray 40 uu when walking")]
    public const float SpyMuzzleWalking = 40f;

    [Provenance(ProvenanceKind.Decoded, "CheckIfCanFire spy ray 0.9 x FX distance otherwise")]
    public const float SpyMuzzleScale = 0.9f;

    [Provenance(ProvenanceKind.Decoded, "ApplyWeaponJump pitch factor 33")]
    public const float RecoilPitchFactor = 33f;

    [Provenance(ProvenanceKind.Decoded, "ApplyWeaponJump camera yaw factor 50")]
    public const float RecoilCameraYawFactor = 50f;

    [Provenance(ProvenanceKind.Decoded, "ApplyWeaponJump aim yaw factor 33")]
    public const float RecoilAimYawFactor = 33f;

    [Provenance(ProvenanceKind.Decoded, "ApplyWeaponJump random add 5")]
    public const float RecoilRandom = 5f;

    [Provenance(ProvenanceKind.Decoded, "ApplyWeaponJump pitch clamp +-13000")]
    public const float RecoilPitchClamp = 13000f;

    [Provenance(ProvenanceKind.Decoded, "GetNbBulletsFromROF burst size 3")]
    public const int BurstSize = 3;

    [Provenance(ProvenanceKind.Decoded, "GetAccuracy speed term |V| / 600 (prior decode 0x10abcfe0)")]
    public const float AccuracySpeedDivisor = 600f;

    [Provenance(ProvenanceKind.Decoded, "GetAccuracy burst term 0.1 per bullet in a row (prior decode)")]
    public const float AccuracyBurstTerm = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "NativeTickSWeaponDefense / s_IdleGrabbed.Tick decay step 0.02 s")]
    public const float BulletInARowDecay = 0.02f;

    [Provenance(ProvenanceKind.Decoded, "ServerFireWeapon shot sound radius 500")]
    public const float ShotSoundRadius = 500f;

    [Provenance(ProvenanceKind.Decoded, "Fire, Reload, SwitchROF sound radius 60")]
    public const float WeaponSoundRadius = 60f;

    [Provenance(ProvenanceKind.Decoded, "s_Reloading reticle message 3 s, priority 2")]
    public const float ReloadMessageTime = 3f;

    [Provenance(ProvenanceKind.Decoded, "s_FullReloading reticle message 2 s")]
    public const float FullReloadMessageTime = 2f;

    [Provenance(ProvenanceKind.Decoded, "s_Reloading reticle message priority 2")]
    public const int ReloadMessagePriority = 2;

    [Provenance(ProvenanceKind.Decoded, "FireWeapon shot clip alpha 0.8")]
    public const float ShotClipAlpha = 0.8f;

    [Provenance(ProvenanceKind.Decoded, "SFireTrace tracer chance PourcBT* / 100")]
    public const float TracerPercent = 100f;

    [Provenance(ProvenanceKind.Decoded, "TraceFire impact roll int(FRand() * 65536)")]
    public const float RandomRoll = 65536f;

    // ---- clip data (decoded from SWeaponFirst.ukx, NumFrames at Rate fps) ----------------------------------

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstShooStFd2 8 frames at 15 fps")]
    public static readonly ClipData FirstShooStFd2 = new(8, 15f);

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstReloStFd2 31 frames at 15 fps")]
    public static readonly ClipData FirstReloStFd2 = new(31, 15f);

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstReloStFd1 26 frames at 15 fps")]
    public static readonly ClipData FirstReloStFd1 = new(26, 15f);

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstDrawStEd2 11 frames at 15 fps")]
    public static readonly ClipData FirstDrawStEd2 = new(11, 15f);

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstGrbdStBg2 16 frames at 15 fps")]
    public static readonly ClipData FirstGrbdStBg2 = new(16, 15f);

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstElecStFd2 6 frames at 15 fps")]
    public static readonly ClipData FirstElecStFd2 = new(6, 15f);

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstSnipStBg2 7 frames at 15 fps")]
    public static readonly ClipData FirstSnipStBg2 = new(7, 15f);

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx DrawP190 4 frames at 15 fps")]
    public static readonly ClipData DrawP190 = new(4, 15f);

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx SnipeP190 5 frames at 15 fps")]
    public static readonly ClipData SnipeP190 = new(5, 15f);

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx WaitP190 5 frames at 15 fps")]
    public static readonly ClipData WaitP190 = new(5, 15f);

    [Provenance(ProvenanceKind.Estimated, "FirstButtStFd2 clip data not decoded: about the strike's 0.6 s")]
    public static readonly ClipData FirstButtStFd2 = new(10, 15f);

    [Provenance(ProvenanceKind.Estimated, "FirstAtakChEd2 clip data not decoded: the 0.4 s charge run-out")]
    public static readonly ClipData FirstAtakChEd2 = new(7, 15f);

    [Provenance(ProvenanceKind.Estimated, "FirstAtakChit2 clip data not decoded: about the bump clip's 0.6 s")]
    public static readonly ClipData FirstAtakChit2 = new(10, 15f);

    [Provenance(ProvenanceKind.Decoded, "s_Sniping plays FirstSnipStBg2 at rate 1.6")]
    public const float SnipeClipRate = 1.6f;

    [Provenance(ProvenanceKind.Decoded, "s_IdleInHolster.BeginState FirstDrawStEd2 at rate 4")]
    public const float HolsterClipRate = 4f;

    [Provenance(ProvenanceKind.Decoded, "AnimateDraw FirstDrawStEd2 backward at rate 2")]
    public const float DrawClipRate = 2f;

    [Provenance(ProvenanceKind.Decoded, "AnimateWaiting moving above 10 uu/s")]
    public const float WalkClipMinSpeed = 10f;

    [Provenance(ProvenanceKind.Decoded, "AnimateWaiting: LoopAnim('FirstWalkStFd2', FClamp(VSize(Velocity) * 0.006, 0, 3))")]
    [Provenance(ProvenanceKind.Measured, "sandbox merc walking at 346.5 uu/s: weapon channel 0 rate 2.835/s = 2.079 x 15 / 11")]
    public const float WalkClipRatePerSpeed = 0.006f;

    [Provenance(ProvenanceKind.Decoded, "AnimateWaiting: FClamp(..., 0, 3)")]
    public const float WalkClipMaxRate = 3f;

    // ---- spy shocker and energy (spec/equipment/spy-shocker.md, spy-energy.md) ----------------------------

    [Provenance(ProvenanceKind.Decoded, "SWeaponAttaque.GetAxesAndLocation +30 X")]
    public const float ShockerStartForward = 30f;

    [Provenance(ProvenanceKind.Decoded, "SWeaponAttaque.GetAxesAndLocation +8 Z")]
    public const float ShockerStartUp = 8f;

    [Provenance(ProvenanceKind.Decoded, "SWeaponAttaque.GetAxesAndLocation -4 Y")]
    public const float ShockerStartLeft = 4f;

    [Provenance(ProvenanceKind.Decoded, "TraceFireWithPos water conduction probe 100 down")]
    public const float WaterProbe = 100f;

    [Provenance(ProvenanceKind.Decoded, "TraceFireWithPos impact at Hit + 10 N")]
    public const float ShockerImpactOffset = 10f;

    [Provenance(ProvenanceKind.Decoded, "ServerFireWeaponWithLocation shot sound radius 200")]
    public const float ShockerSoundRadius = 200f;

    [Provenance(ProvenanceKind.Decoded, "SWeaponAttaque.AnimateDrawWeapon LastShot = now + 0.1")]
    public const float DrawShotDelay = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "FindAttClosestTarget snap cone 6000 yaw and pitch")]
    public const int SnapCone = 6000;

    [Provenance(ProvenanceKind.Decoded, "FindAttClosestTarget minimum range 200")]
    public const float SnapMinRange = 200f;

    [Provenance(ProvenanceKind.Decoded, "FindAttClosestTarget pelvis + 40 from, pelvis + 15 to")]
    public const float SnapFromRaise = 40f;

    [Provenance(ProvenanceKind.Decoded, "FindAttClosestTarget pelvis + 40 from, pelvis + 15 to")]
    public const float SnapToRaise = 15f;

    [Provenance(ProvenanceKind.Decoded, "FindAttClosestTarget side offset 12")]
    public const float SnapSideOffset = 12f;

    [Provenance(ProvenanceKind.Decoded, "FindAttClosestTarget stored pitch * 1.133")]
    public const float SnapPitchFactor = 1.133f;

    [Provenance(ProvenanceKind.Decoded, "SGadCloak.CanTurnOn speed limit 240")]
    public const float CloakMaxSpeed = 240f;

    [Provenance(ProvenanceKind.Decoded, "SGadCloak.QuickUse ignores within 0.5 s of LastUsed")]
    public const float CloakToggleGuard = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "PlayerTick cloak button hold threshold 0.2 s")]
    public const float CloakButtonHold = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "SGadCloak.QuickUse message Camo. Suit unavailable 2 s")]
    public const float CloakMessageTime = 2f;

    [Provenance(ProvenanceKind.Decoded, "ComputeEnergyLevel forced decloak over 1 s")]
    public const float CloakForcedOff = 1f;

    [Provenance(ProvenanceKind.Decoded, "SetWeaponLoaded uncloak over 2 s")]
    public const float DrawUncloakTime = 2f;

    [Provenance(ProvenanceKind.Decoded, "GlassEffect 255 is a full cloak")]
    public const byte FullGlass = 255;

    /// <summary>The cloak ramp is evaluated this far ahead of the level time.</summary>
    [Provenance(ProvenanceKind.Decoded, "SPawnAttaque.TickNative game 0x10af5cf0 and Reloaded body 0x100caae0: (TimeSeconds + 0.1) * SlopeGlassEffect + BGlassEffect")]
    public const float GlassRampLead = 0.1f;

    /// <summary>The perturbation frequency of a toggle (SetPerturbationGlass with no value).</summary>
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.SetPerturbationGlass and SetPerturbationGlassOnClients: Value 0 becomes 12.8")]
    public const float GlassPerturbationValue = 12.8f;

    /// <summary>The jitter range at the start of a perturbation (it shrinks linearly to 0 over its duration).</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded TickNative body 0x100caae0: int(510 / Duration * (Start + Duration - now))")]
    public const float GlassJitterAmplitude = 510f;

    [Provenance(ProvenanceKind.Decoded, "Reloaded TickNative body 0x100caae0: the range kept in [10, 510]")]
    public const int GlassJitterMin = 10;

    [Provenance(ProvenanceKind.Decoded, "Reloaded TickNative body 0x100caae0: the range kept in [10, 510]")]
    public const int GlassJitterMax = 510;

    /// <summary>A jittered value equal to the target is drawn as this.</summary>
    [Provenance(ProvenanceKind.Decoded, "TickNative game 0x10af5cf0 and Reloaded 0x100caae0: GlassEffect == DesiredGlassEffect becomes 0x7d")]
    public const byte GlassJitterTargetStandIn = 125;

    /// <summary>The radius of the cloak's in/out and perturbation sounds.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.SetDesiredGlassValueOnClients and SetPerturbationGlassOnClients: PlaySound(..., , 1.0, , 300.0)")]
    public const float CloakSoundRadius = 300f;

    // ---- grenades (spec/equipment/grenade-throw.md, frag-grenade.md, smoke-grenade.md) -------------------

    [Provenance(ProvenanceKind.Decoded, "SGadGrenLauncher.Use force int((v + 1)^2 * ProjectileSpeed * 0.5)")]
    public const float ThrowForceScale = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "grenade-throw.md rule 2: a tap under 0.25 s throws at full strength")]
    public const float ThrowTapWindow = 0.25f;

    [Provenance(ProvenanceKind.Decoded, "grenade-throw.md rule 2: the gauge ramps over 1.0 s after the tap window")]
    public const float ThrowRamp = 1f;

    [Provenance(ProvenanceKind.Decoded, "Empty function body hash of the decompiler index (no statements)")]
    public const string EmptyBodySha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e";

    [Provenance(ProvenanceKind.Decoded, "GadgetQuickChoice cooldown 2 s")]
    public const float QuickLaunchCooldown = 2f;

    [Provenance(ProvenanceKind.Decoded, "SProjGrenade.PostNetBeginPlay yaw spin 10000 + 5000 FRand")]
    public const float SpinYawBase = 10000f;

    [Provenance(ProvenanceKind.Decoded, "SProjGrenade.PostNetBeginPlay yaw spin 10000 + 5000 FRand")]
    public const float SpinYawRandom = 5000f;

    [Provenance(ProvenanceKind.Decoded, "SProjGrenade.PostNetBeginPlay pitch spin 80000 + 10000 FRand")]
    public const float SpinPitchBase = 80000f;

    [Provenance(ProvenanceKind.Decoded, "SProjGrenade.PostNetBeginPlay pitch spin 80000 + 10000 FRand")]
    public const float SpinPitchRandom = 10000f;

    [Provenance(ProvenanceKind.Decoded, "SGadLaunchEffects life 0.1 s")]
    public const float LaunchFlashLife = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "SGadLaunchEffects scale 1.5 + 2 FRand")]
    public const float LaunchFlashScaleBase = 1.5f;

    [Provenance(ProvenanceKind.Decoded, "SGadLaunchEffects scale 1.5 + 2 FRand")]
    public const float LaunchFlashScaleRandom = 2f;

    [Provenance(ProvenanceKind.Decoded, "SProjFrag launch sound radius 100")]
    public const float GrenadeSoundRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "SGadGrenLauncher.Use rumble 0.12 s")]
    public const float GadgetRumble = 0.12f;

    [Provenance(ProvenanceKind.Decoded, "SProjSmoke explode: emitter raised by the collision height plus 1 uu (prior decode 0x10ac1b20)")]
    public const float SmokeRaise = 1f;

    // ---- vision, torch and laser (spec/equipment/vision-goggles.md, merc-flashlight.md, merc-laser.md) ----

    [Provenance(ProvenanceKind.Decoded, "SGoggle.Activate messages 3 s")]
    public const float VisionMessageTime = 3f;

    [Provenance(ProvenanceKind.Decoded, "SGadTorche.Use messages 2 s")]
    public const float TorchMessageTime = 2f;

    [Provenance(ProvenanceKind.Decoded, "SGadTorche s_Malfunction.Tick Rand(3) == 0")]
    public const int MalfunctionOdds = 3;

    [Provenance(ProvenanceKind.Decoded, "SGadTorcheLaser.ThrowSpyBullet SGameMessages 60 to team 1")]
    public const int MsgSpyMarked = 60;

    [Provenance(ProvenanceKind.Decoded, "SwitchHeadset refused: HUD number message 7")]
    public const int MsgVisionRefused = 7;

    // ---- merc scope (spec/equipment/merc-scope.md) -----------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def zoom FOVs 30 / 20 / 10")]
    public const float ScopeFov1 = 30f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def zoom FOVs 30 / 20 / 10")]
    public const float ScopeFov2 = 20f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def zoom FOVs 30 / 20 / 10")]
    public const float ScopeFov3 = 10f;

    [Provenance(ProvenanceKind.Decoded, "ZoomIn / ZoomOut audio zoom 0.33 / 0.66 / 1.0")]
    public const float AudioZoom1 = 0.33f;

    [Provenance(ProvenanceKind.Decoded, "ZoomIn / ZoomOut audio zoom 0.33 / 0.66 / 1.0")]
    public const float AudioZoom2 = 0.66f;

    [Provenance(ProvenanceKind.Decoded, "ZoomIn / ZoomOut audio zoom 0.33 / 0.66 / 1.0")]
    public const float AudioZoom3 = 1f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def.PlayerMove zoom effect time 0.4 + dt")]
    public const float ZoomEffectTime = 0.4f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def.PlayerMove FOV interpolation below 0.15 s")]
    public const float ZoomInterpolationStart = 0.15f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def.PlayerMove FOV interpolation factor 5")]
    public const float ZoomInterpolationRate = 5f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def.BeginState first zoom effect time 1.0")]
    public const float FirstZoomEffectTime = 1f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def BinocularNoiseDuration 15 ticks")]
    public const int ScopeNoiseTicks = 15;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def.BeginState breath reset after 5 s out of the scope")]
    public const float BreathResetDelay = 5f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur breath phase speed 0.8 pi rad/s")]
    public const float BreathPhaseSpeed = 0.8f * 3.1415927f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur inspire start phase 0.2 pi")]
    public const float InspireStartPhase = 0.2f * 3.1415927f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur horizontal breath amplitude 4")]
    public const float BreathRightAmplitude = 4f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur exhausted amplitude penalty 140 / 100 / 50")]
    public const float BreathPenalty1 = 140f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur exhausted amplitude penalty 140 / 100 / 50")]
    public const float BreathPenalty2 = 100f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur exhausted amplitude penalty 140 / 100 / 50")]
    public const float BreathPenalty3 = 50f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur breath lockout after exhaustion 3 s")]
    public const float BreathExhaustedLockout = 3f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur breath lockout after release 1 s")]
    public const float BreathReleaseLockout = 1f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur recovery gap 5 / FClamp(reserve, 2, 5) - 1")]
    public const float BreathGapMin = 2f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur steady hold snap 0.2 per tick")]
    public const float BreathHoldSnap = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur sway only with |turn| and |look| below 30")]
    public const float BreathStillInput = 30f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur view lag: Dif / (20 / (dt * 100)), rate 5 per second")]
    public const float ScopeLagDivisor = 20f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur aim pitch clamp at FOV 30: -10000 to 13000")]
    public const int ScopePitchMin30 = -10000;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur aim pitch clamp at FOV 30: -10000 to 13000")]
    public const int ScopePitchMax30 = 13000;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur aim pitch clamp at FOV 20: -20000 to 26000")]
    public const int ScopePitchMin20 = -20000;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur aim pitch clamp at FOV 20: -20000 to 26000")]
    public const int ScopePitchMax20 = 26000;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur commit wraps axes into the signed half turn")]
    public const int ScopeWrapHalf = 32768;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur breath phase limits -pi/2 and pi/2")]
    public const float HalfPi = 0.5f * 3.1415927f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur BreathRight re-roll int(RandRange(0, 20)) > 10")]
    public const float BreathRightRoll = 20f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur BreathRight re-roll int(RandRange(0, 20)) > 10")]
    public const int BreathRightThreshold = 10;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur kick FOV share 0.6 while rising")]
    public const float KickRiseShare = 0.6f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur kick and FOV share 0.4 while falling")]
    public const float KickFallShare = 0.4f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def.PlayerMove input curve over aForward / 6000")]
    public const float ScopeInputSpan = 6000f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def.PlayerMove standing curve (-0.05 a + 0.15) a + 0.9")]
    public const float ScopeStandA = -0.05f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def.PlayerMove standing curve (-0.05 a + 0.15) a + 0.9")]
    public const float ScopeStandB = 0.15f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def.PlayerMove standing curve (-0.05 a + 0.15) a + 0.9")]
    public const float ScopeStandC = 0.9f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def.PlayerMove crouched curve (-0.02468 a + 0.05065) a + 0.3117")]
    public const float ScopeCrouchA = -0.02468f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def.PlayerMove crouched curve (-0.02468 a + 0.05065) a + 0.3117")]
    public const float ScopeCrouchB = 0.05065f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def.PlayerMove crouched curve (-0.02468 a + 0.05065) a + 0.3117")]
    public const float ScopeCrouchC = 0.3117f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def.PlayerMove GroundSpeed = RunningSpeed * |accel| / 3000")]
    public const float ScopeAccelNorm = 3000f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def.PlayerMove speed floor 15 crouched")]
    public const float ScopeMinSpeedCrouched = 15f;

    [Provenance(ProvenanceKind.Decoded, "s_Binoculars_Def.PlayerMove speed floor 40 standing")]
    public const float ScopeMinSpeedStanding = 40f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur pitch edge slow-down from 6000")]
    public const float ScopeEdgeStart = 6000f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur pitch edge slow-down over 7000")]
    public const float ScopeEdgeSpan = 7000f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur yaw speed factor 2")]
    public const float ScopeYawFactor = 2f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur kick time split at 0.24 of 0.4 s")]
    public const float KickSplit = 0.24f;

    [Provenance(ProvenanceKind.Decoded, "UpdateRotationDefenseur kick q = 0.5 * sensitivity * dt * 50")]
    public const float KickQ = 25f;

    // ---- inventory (spec/equipment/inventory-selection.md, loadouts.md) ---------------------------------

    [Provenance(ProvenanceKind.Decoded, "STeamInfo.iNBGadgets / HUD slot range 0..3")]
    public const int GadgetSlots = 4;

    [Provenance(ProvenanceKind.Decoded, "loadouts.md: 8 team options per side (ClassInventory)")]
    public const int TeamOptions = 8;

    [Provenance(ProvenanceKind.Decoded, "PlayerTick next/previous item repeat 0.2 s")]
    public const float ItemCycleRepeat = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "PrevGadget scan limit 10")]
    public const int GadgetScanLimit = 10;

    [Provenance(ProvenanceKind.Decoded, "DeleteAllInventory limit 16 gadgets")]
    public const int GadgetDeleteLimit = 16;

    [Provenance(ProvenanceKind.Decoded, "SGadget.PostNetBeginPlay offline charge 999")]
    public const int OfflineCharge = 999;

    [Provenance(ProvenanceKind.Decoded, "Inventory.Charge -10 is unlimited")]
    public const int UnlimitedCharge = -10;

    [Provenance(ProvenanceKind.Decoded, "AltFire empty-charge sound radius 100")]
    public const float EmptyChargeRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "ReloadAmmoStock sound radius 100")]
    public const float AmmoSoundRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "vision-goggles.md rule 6: the goggle clip plays on pawn channel 18")]
    public const int GoggleChannel = 18;

    [Provenance(ProvenanceKind.Decoded, "merc-rifle.md rule 23: scoped tracer start 75 uu above the instigator")]
    public const float FireTraceScopeRaise = 75f;
}

/// <summary>A clip's frame count and rate at play rate 1.</summary>
public readonly record struct ClipData(int Frames, float Fps)
{
    /// <summary>
    /// How long a one-shot plays before its AnimEnd at <paramref name="Rate"/>: the end is at (N - 1) / N of the clip
    /// (spec/native/anim.md rule 2: a clip ends at AnimLast = 1 - 1 / N, advancing at Rate x Fps / N per second).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/anim.md rule 2: AnimLast 1 - 1/N, AnimRate Rate x Fps / N")]
    public float Seconds(float Rate) => Frames <= 1 || Rate <= 0f ? 0f : (Frames - 1) / (Fps * Rate);
}
