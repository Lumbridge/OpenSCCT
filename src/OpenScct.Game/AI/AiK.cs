using OpenScct.Provenance;

namespace OpenScct.Game.AI;

/// <summary>
/// Numbers of the bots. The original has no bots: values marked Decoded or Measured come from the original game (the
/// rules the bots perceive and move by); the others are tuning choices of the bots, documented in docs/AI.md.
/// </summary>
public static class AiK
{
    // ---- the pawn lighting rule (docs/RENDERING.md, "Actor lighting"), shared with the renderer ----------------

    /// <summary>Lights that light one actor (Actor.MaxLights default).</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.u Actor.MaxLights default 4")]
    public const int MaxActorLights = 4;

    /// <summary>Point light gain (the colour carries /510 and the stage modulates 2x).</summary>
    [Provenance(ProvenanceKind.Decoded, "RenderingModeNormal::SetLight falloff f(d) = 2 S(d/R), editor 0x10f29790")]
    public const float PointLightGain = 2f;

    /// <summary>LE_Sunlight gain, unattenuated.</summary>
    [Provenance(ProvenanceKind.Decoded, "RenderingModeNormal::SetLight sun branch 1.75 x C")]
    public const float SunLightGain = 1.75f;

    /// <summary>FGetHSV brightness at V 255.</summary>
    [Provenance(ProvenanceKind.Decoded, "FGetHSV, editor 0x11096ab0")]
    public const float HsvFullBrightness = 0.8213f;

    /// <summary>World light radius in uu per LightRadius unit: R = 25 x (LightRadius + 1).</summary>
    [Provenance(ProvenanceKind.Decoded, "AActor::WorldLightRadius, editor 0x10f57a20")]
    public const float LightRadiusUnits = 25f;

    /// <summary>Spot cone: Phi = 1.15 acos((1 - LightCone / 256)^2).</summary>
    [Provenance(ProvenanceKind.Decoded, "RenderingModeNormal::SetLight spot branch")]
    public const float SpotPhiScale = 1.15f;

    /// <summary>The display term is capped at 2 (min(2, A + sum)).</summary>
    [Provenance(ProvenanceKind.Decoded, "docs/RENDERING.md actor lighting: tex x min(2, A + sum)")]
    public const float LightCap = 2f;

    [Provenance(ProvenanceKind.Decoded, "Engine.u ELightEffect: 19 LE_Sunlight")]
    public const int LE_Sunlight = 19;

    [Provenance(ProvenanceKind.Decoded, "Engine.u ELightEffect: 22 LE_ESpotShadow")]
    public const int LE_ESpotShadow = 22;

    [Provenance(ProvenanceKind.Decoded, "Engine.u ELightEffect: 8 LE_StaticSpot")]
    public const int LE_StaticSpot = 8;

    [Provenance(ProvenanceKind.Decoded, "Engine.u ELightEffect: 12 LE_Spotlight")]
    public const int LE_Spotlight = 12;

    /// <summary>The merc torch's range (EchelonRange) and spot size at that range (SpotWidth and SpotHeight).</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/equipment/merc-flashlight.md: EchelonRange 1500, SpotWidth / SpotHeight 400")]
    public const float TorchRange = 1500f;

    [Provenance(ProvenanceKind.Decoded, "spec/equipment/merc-flashlight.md: SpotWidth 400")]
    public const float TorchSpotSize = 400f;

    /// <summary>The torch light: brightness 304, hue 21, saturation 255.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/equipment/merc-flashlight.md: LightBrightness 304, LightHue 21, LightSaturation 255")]
    public const float TorchBrightness = 304f;

    [Provenance(ProvenanceKind.Decoded, "spec/equipment/merc-flashlight.md: LightHue 21")]
    public const byte TorchHue = 21;

    [Provenance(ProvenanceKind.Decoded, "spec/equipment/merc-flashlight.md: LightSaturation 255")]
    public const byte TorchSaturation = 255;

    /// <summary>The spot shadow light's gain (the Echelon light pass is not decoded; the renderer uses 2).</summary>
    [Provenance(ProvenanceKind.Estimated, "docs/RENDERING.md spot shadow lights: colour x 2 x cookie x linear attenuation")]
    public const float TorchGain = 2f;

    /// <summary>Night vision's light boost on lit surfaces: 1.4 x 30 / 255.</summary>
    [Provenance(ProvenanceKind.Measured, "docs/RENDERING.md vision modes: mode 34 boost 1.4 x 30 / 255")]
    public const float NightBoost = 1.4f * 30f / 255f;

    /// <summary>Night vision's post gain: saturate(4 n L).</summary>
    [Provenance(ProvenanceKind.Decoded, "docs/RENDERING.md vision modes: mode 34 post saturate(4 n L)")]
    public const float NightGain = 4f;

    /// <summary>Thermal vision fades with view depth: 1 - viewZ / 5000.</summary>
    [Provenance(ProvenanceKind.Decoded, "docs/RENDERING.md vision modes: thermal Z pass (0, heat, 1 - viewZ / 5000)")]
    public const float ThermalRange = 5000f;

    /// <summary>A smoke cloud's effect radius (SSmokeEmitter.RadiusEffect) and life.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/equipment/smoke-grenade.md: RadiusEffect 300, LifeSpan 10")]
    public const float SmokeRadius = 300f;

    // ---- hearing (spec/native/make-noise.md, spec/native/audio.md) ----------------------------------------

    /// <summary>MakeNoise's repeat window, distance (squared) and loudness ratio per instigator slot.</summary>
    [Provenance(ProvenanceKind.Decoded, "CheckNoiseHearing 0x109f1a50: 0.2 s, 2500 uu^2, 0.9")]
    public const float NoiseRepeatSeconds = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "CheckNoiseHearing 0x109f1a50: 2500 uu^2")]
    public const float NoiseRepeatDistanceSquared = 2500f;

    [Provenance(ProvenanceKind.Decoded, "CheckNoiseHearing 0x109f1a50: 0.9 x Loudness")]
    public const float NoiseRepeatRatio = 0.9f;

    [Provenance(ProvenanceKind.Decoded, "CheckNoiseHearing 0x109f1a50: slot reuse after 0.18 s")]
    public const float NoiseSlotSeconds = 0.18f;

    /// <summary>Pawn.HearingThreshold default: the bots' hearing radius of a noise of loudness 1.</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.u Pawn defaults HearingThreshold 2800")]
    public const float HearingThreshold = 2800f;

    /// <summary>A PlaySound radius of 0 reaches hearers as 80.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/audio.md rule 4: execPlaySound, ClientHearSound")]
    public const float SoundDefaultRadius = 80f;

    /// <summary>The listener roll-off: gain R / (R + 0.5 (d - R)) beyond the radius.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/audio.md rule 11: Default.ini Rolloff 0.5")]
    public const float SoundRolloff = 0.5f;

    // ---- movement facts the planner uses -------------------------------------------------------------------

    /// <summary>Spy and merc fall damage starts (Fell_MinHeightForDmg).</summary>
    [Provenance(ProvenanceKind.Decoded, "SPawnAttaque defaults Fell_MinHeightForDmg 600")]
    public const float SpyFallDamageHeight = 600f;

    [Provenance(ProvenanceKind.Decoded, "SPAWNDEFENSE defaults Fell_MinHeightForDmg 450")]
    public const float MercFallDamageHeight = 450f;

    /// <summary>
    /// Longest drop a spy plans: a little over its fall damage height (full damage at Fell_MaxHeightForDmg 1500), so a
    /// 700 uu drop costs a few points. SteeE's spies leave their roof this way.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SPawnAttaque defaults Fell_MinHeightForDmg 600, Fell_MaxHeightForDmg 1500")]
    public const float SpyMaxDrop = 700f;

    /// <summary>Crouched collision heights (spy 50, merc 68) and the standing one (87.5); radius 35.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBasePawn defaults CrouchHeight 50, CollisionHeight 87.5, CollisionRadius 35; SPAWNDEFENSE CrouchHeight 68")]
    public const float SpyCrouchHeight = 50f;

    [Provenance(ProvenanceKind.Decoded, "SPAWNDEFENSE defaults CrouchHeight 68")]
    public const float MercCrouchHeight = 68f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn defaults CollisionHeight 87.5")]
    public const float StandingHeight = 87.5f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn defaults CollisionRadius 35")]
    public const float PawnRadius = 35f;

    /// <summary>The ledge hang spot is 117 uu under the lip and 39 uu out from it.</summary>
    [Provenance(ProvenanceKind.Measured, "docs/spikes/R4-traversal.md spy-ledge-grab: hang at lip - 39 horizontally, LG z - 117")]
    public const float LedgeHangDrop = 117f;

    /// <summary>Lip heights above the floor a standing jump catches (container A: 263.5, caught at a 53.4 rise; JumpZ 610 rises 124).</summary>
    [Provenance(ProvenanceKind.Estimated, "from the hang spot (lip - 117), the standing location (floor + 89) and the 124 uu jump rise")]
    public const float LedgeJumpMin = 215f;

    [Provenance(ProvenanceKind.Estimated, "from the hang spot (lip - 117), the standing location (floor + 89) and the 124 uu jump rise")]
    public const float LedgeJumpMax = 320f;

    /// <summary>A ledge grab needs 24.5 uu from either end of the LG.</summary>
    [Provenance(ProvenanceKind.Decoded, "docs/PHYSICS.md CheckArmsZone: Ledge 24.5 end margin")]
    public const float LedgeEndMargin = 24.5f;

    /// <summary>Speeds (uu/s) the planner turns distances into time with.</summary>
    [Provenance(ProvenanceKind.Measured, "docs/spikes/R4-traversal.md: crouched 221.4, pipe 145, zip 423.3; run 385 (RunningSpeed)")]
    public const float CrouchSpeed = 221.4f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn defaults RunningSpeed 385")]
    public const float RunSpeed = 385f;

    [Provenance(ProvenanceKind.Measured, "docs/spikes/R4-traversal.md spy-pipe-climb-up: 145.0 uu/s")]
    public const float PipeClimbSpeed = 145f;

    [Provenance(ProvenanceKind.Measured, "docs/spikes/R4-traversal.md spy-zip-line-13: 423.3 uu/s")]
    public const float ZipSpeed = 423.3f;

    /// <summary>The top of a pipe exit: 177.9 up and 91 across from the hold.</summary>
    [Provenance(ProvenanceKind.Measured, "docs/spikes/R4-traversal.md spy-pipe-climb-up: s_TopPipe +177.9 up, 91 across")]
    public const float PipeTopAcross = 91f;

    // ---- bot tuning (not values of the original) -------------------------------------------------------------

    /// <summary>Spacing of the navigation grid.</summary>
    public const float NavSpacing = 32f;

    /// <summary>Eye height above the pawn's centre (standing and crouched).</summary>
    public const float EyeHeightStanding = 60f;

    public const float EyeHeightCrouched = 30f;

    /// <summary>Seconds between two full decisions of a bot (difficulty scales it).</summary>
    public const float DecisionSeconds = 0.25f;

    /// <summary>Seconds between two sight checks of one target.</summary>
    public const float SightSeconds = 0.1f;

    /// <summary>A pawn slower than this (uu/s) counts as still for motion vision.</summary>
    public const float MotionSpeed = 60f;

    /// <summary>Range at which a merc engages.</summary>
    public const float MercEngageRange = 2500f;

    /// <summary>Seconds of a rifle burst and the pause after it.</summary>
    public const float BurstSeconds = 0.3f;

    public const float BurstPause = 0.3f;

    /// <summary>
    /// Furthest spy a merc bot charges (uu). The charge runs 1.0 s from 400 uu/s, gaining 750 uu/s each second up to
    /// 770 (melee-elbow-charge-berserk.md rules 16 and 18), about 650 uu on open floor; the bot keeps a margin for the
    /// spy stepping away.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "charge distance from the decoded ramp (400 to 770 uu/s, +750/s, 1.0 s) less a margin")]
    public const float MercChargeReach = 550f;

    /// <summary>Nearest spy a merc bot charges (uu): nearer, the Use key without forward (spin or elbow) does better.</summary>
    public const float MercChargeMin = 140f;

    /// <summary>
    /// Largest view error (degrees) at which a merc bot starts a charge: the charge heads along the view and homes
    /// only within the 0.8 cone (37 degrees) of the aim, so the bot lines up first.
    /// </summary>
    public const float MercChargeAimDegrees = 12f;

    /// <summary>A merc bot's elbow reach (uu): the game's 150 less a margin for the spy moving off.</summary>
    public const float MercElbowReach = 130f;

    /// <summary>Seconds between a merc bot's looks at a close-quarters chance (each look takes it by MeleeUse).</summary>
    public const float MeleeRollSeconds = 1f;

    /// <summary>A team mate this near (uu) keeps a merc bot from Use at close quarters: the spin hits team mates too.</summary>
    public const float MercSpinClearance = 220f;

    /// <summary>A merc holds fire when a teammate stands this close to the line of fire.</summary>
    public const float FriendlyClearance = 60f;

    /// <summary>Seconds a navigation link that failed is left out of plans.</summary>
    public const float BadLinkSeconds = 60f;

    /// <summary>A walker about to drop waits up to this long for a teammate under the landing spot to move on.</summary>
    public const float DropYieldSeconds = 4f;

    /// <summary>How near the landing spot a teammate makes a dropping walker wait (two collision radii and the drift).</summary>
    public const float DropYieldRadius = 160f;

    /// <summary>
    /// A pawn counts as in a walker's way when the gap between the two cylinders along the walk is under this (uu): the
    /// walker steps around him instead of pushing into him.
    /// </summary>
    public const float PawnAheadGap = 40f;

    /// <summary>How far to the side of the walk line a pawn's cylinder may reach and still not be in the way (uu).</summary>
    public const float PawnPassMargin = 12f;

    /// <summary>
    /// A walker blocked by a pawn steps around him for at most this long before the usual stuck handling (a wiggle,
    /// then a new plan) takes over.
    /// </summary>
    public const float PawnSidestepSeconds = 3f;

    /// <summary>The wiggle that frees a stuck walker leaves out its jump with a pawn this near (uu between cylinders).</summary>
    public const float PawnNoJumpGap = 90f;

    /// <summary>Seconds from a goggle key to the clip's notify that starts or ends the mode (not measured).</summary>
    public const float GoggleClipSeconds = 0.5f;
}
