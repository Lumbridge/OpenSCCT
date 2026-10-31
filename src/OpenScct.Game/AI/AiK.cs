using OpenScct.Game.Lighting;
using OpenScct.Provenance;

namespace OpenScct.Game.AI;

/// <summary>
/// Numbers of the bots. The original has no bots: values marked Decoded or Measured come from the original game (the
/// rules the bots perceive and move by); the others are tuning choices of the bots, documented in docs/AI.md.
/// </summary>
public static class AiK
{
    // ---- the pawn lighting rule: OpenScct.Game.Lighting.ActorLightRules, shared with the renderer -------------

    public const int MaxActorLights = ActorLightRules.MaxActorLights;

    public const float PointLightGain = ActorLightRules.PointLightGain;

    public const float SunLightGain = ActorLightRules.SunLightGain;

    public const float HsvFullBrightness = ActorLightRules.HsvFullBrightness;

    public const float LightRadiusUnits = ActorLightRules.LightRadiusUnits;

    public const float SpotPhiScale = ActorLightRules.SpotPhiScale;

    public const float LightCap = ActorLightRules.LightCap;

    public const int LE_Sunlight = ActorLightRules.LE_Sunlight;

    public const int LE_ESpotShadow = ActorLightRules.LE_ESpotShadow;

    public const int LE_StaticSpot = ActorLightRules.LE_StaticSpot;

    public const int LE_Spotlight = ActorLightRules.LE_Spotlight;

    /// <summary>
    /// The merc torch's range (EchelonRange) and spot size at that range (SpotWidth and SpotHeight, half extents: the
    /// lit patch is 800 x 800 uu at the range, docs/RENDERING.md#spot-shadow-lights).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "spec/equipment/merc-flashlight.md: EchelonRange 1500, SpotWidth / SpotHeight 400")]
    public const float TorchRange = 1500f;

    [Provenance(ProvenanceKind.Decoded, "spec/equipment/merc-flashlight.md: SpotWidth 400")]
    public const float TorchSpotSize = 400f;

    /// <summary>The torch's NearPlaneRatio (its apex sits 3.76 uu behind the light).</summary>
    [Provenance(ProvenanceKind.Decoded, "SBase.SGadTorche defaults NearPlaneRatio 0.0025")]
    public const float TorchNearPlaneRatio = 0.0025f;

    /// <summary>The torch light: brightness 304, hue 21, saturation 255.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/equipment/merc-flashlight.md: LightBrightness 304, LightHue 21, LightSaturation 255")]
    public const float TorchBrightness = 304f;

    [Provenance(ProvenanceKind.Decoded, "spec/equipment/merc-flashlight.md: LightHue 21")]
    public const byte TorchHue = 21;

    [Provenance(ProvenanceKind.Decoded, "spec/equipment/merc-flashlight.md: LightSaturation 255")]
    public const byte TorchSaturation = 255;

    /// <summary>The spot shadow light's gain (the Echelon light pass is not decoded; the renderer uses 2).</summary>
    public const float TorchGain = ActorLightRules.SpotShadowGain;

    /// <summary>Night vision's light boost on lit surfaces: 1.4 x 30 / 255.</summary>
    [Provenance(ProvenanceKind.Measured, "docs/RENDERING.md vision modes: mode 34 boost 1.4 x 30 / 255")]
    public const float NightBoost = 1.4f * 30f / 255f;

    /// <summary>Night vision's post gain: saturate(4 n L).</summary>
    [Provenance(ProvenanceKind.Decoded, "docs/RENDERING.md vision modes: mode 34 post saturate(4 n L)")]
    public const float NightGain = 4f;

    /// <summary>Thermal vision fades with view depth: 1 - viewZ / 5000.</summary>
    [Provenance(ProvenanceKind.Decoded, "docs/RENDERING.md vision modes: thermal Z pass (0, heat, 1 - viewZ / 5000)")]
    public const float ThermalRange = 5000f;

    /// <summary>Body detail retained by motion vision's blurred normal scene (bot perception tuning).</summary>
    public const float MotionSightFactor = 0.65f;

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

    /// <summary>Ladder climb speed (uu/s), both ways.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/movement/ladder.md: SpeedForLadder 126")]
    public const float LadderSpeed = 126f;

    /// <summary>A ladder is caught from a spot this far out from its line on the climber's side.</summary>
    [Provenance(ProvenanceKind.Decoded, "PhysicsDriver.GE CheckLadderArms: K.GE.LadderSpot 35")]
    public const float LadderGrabOut = 35f;

    /// <summary>Getting on a ladder from the top starts from this far back from its line.</summary>
    [Provenance(ProvenanceKind.Decoded, "PhysicsDriver.GE CheckLadderFeet: K.GE.LadderTopSpot 72")]
    public const float LadderTopBack = 72f;

    /// <summary>Hand over hand speed (uu/s).</summary>
    [Provenance(ProvenanceKind.Measured, "spec/movement/pipe-horizontal.md: 105 uu/s measured for a request of 126")]
    public const float HohSpeed = 105f;

    /// <summary>The hand over hand move stops this far from the end of the bar.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/movement/pipe-horizontal.md physOnPipeH: stops 27 from an end")]
    public const float HohEndStop = 27f;

    /// <summary>
    /// Bar heights over the floor a standing jump catches: the HOH arm test wants the bar under 168 above the pawn's
    /// centre while it rises (centre at floor + 87.5, JumpZ 610 rises 124), and the bar over a standing pawn's head.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "CheckHOHArms (arms zone 128, height margin 40), standing centre 87.5, 124 uu jump rise")]
    public const float HohJumpMin = 190f;

    [Provenance(ProvenanceKind.Estimated, "CheckHOHArms (arms zone 128, height margin 40), standing centre 87.5, 124 uu jump rise")]
    public const float HohJumpMax = 360f;

    /// <summary>Where the jump to a bar is made: this far in from its end (the grab wants 35 from either end).</summary>
    public const float HohGrabIn = 80f;

    /// <summary>The walk-in before a jump to a bar, along the bar (a standing spy's body faces where he walks, and the move along the bar follows the body).</summary>
    public const float HohRunIn = 64f;

    /// <summary>Fence climb speed (uu/s), up, down and sideways.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/movement/fence.md OnFence: newAccel 120")]
    public const float FenceSpeed = 120f;

    /// <summary>Fence tops (over the floor on either side) the links take.</summary>
    public const float FenceMinHeight = 120f;

    public const float FenceMaxHeight = 900f;

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

    /// <summary>Seconds the walk edges across a shut way (a closed door, a place a walker stuck at twice) are left out of plans.</summary>
    public const float BlockedEdgeSeconds = 60f;

    /// <summary>A walker stuck again within this distance (uu) and <see cref="StuckRepeatSeconds"/> of its last stuck place takes the way ahead as shut.</summary>
    public const float StuckRepeatRadius = 128f;

    /// <summary>See <see cref="StuckRepeatRadius"/>.</summary>
    public const float StuckRepeatSeconds = 20f;

    /// <summary>How far ahead (uu) a stuck walker looks for what shuts its way.</summary>
    public const float StuckProbe = 160f;

    /// <summary>Half the width (uu) of the line left out across a closed door (a double glass door is about 256 wide).</summary>
    public const float DoorBlockHalfWidth = 160f;

    /// <summary>Half the width (uu) of the line left out across a place a walker stuck at twice with no door in front.</summary>
    public const float StuckBlockHalfWidth = 64f;

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
