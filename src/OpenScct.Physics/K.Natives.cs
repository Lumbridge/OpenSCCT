using OpenScct.Core;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Physics;

public static partial class K
{
    /// <summary>Trace flag masks the physics natives pass (spec/native/collision-traces.md, "Masks used").</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/collision-traces.md masks, read at each caller")]
    public static class Flags
    {
        /// <summary>MoveActor with bCollideActors: pawns, volumes, others (0x19).</summary>
        public const ETraceFlags MoveActors = (ETraceFlags)0x19;

        /// <summary>MoveActor with bCollideActors and bIgnorePawns (0x18).</summary>
        public const ETraceFlags MoveActorsNoPawns = (ETraceFlags)0x18;

        /// <summary>MoveActor with bCollideWorld: movers, level, level geometry (0x86).</summary>
        public const ETraceFlags MoveWorld = (ETraceFlags)0x86;

        /// <summary>Walking and falling wall pre-probes (no pawns).</summary>
        public const ETraceFlags WallProbe = (ETraceFlags)0xd6;

        /// <summary>Floor probes.</summary>
        public const ETraceFlags Floor = (ETraceFlags)0xdf;

        /// <summary>Clearance tests (stop at the first hit).</summary>
        public const ETraceFlags Clearance = (ETraceFlags)0x2df;

        /// <summary>Clearance tests that see pawns and movers only (no BSP, no static meshes).</summary>
        public const ETraceFlags PawnsAndMovers = (ETraceFlags)0x243;

        /// <summary>Landing probe (bCanLand, processLanded).</summary>
        public const ETraceFlags Landing = (ETraceFlags)0x297;

        /// <summary>Falling narrow-top side probes.</summary>
        public const ETraceFlags SideProbe = (ETraceFlags)0x97;

        /// <summary>Drop-attack line of sight.</summary>
        public const ETraceFlags DropAttackSight = (ETraceFlags)0x29f;

        /// <summary>Hand-over-hand legs probe (pawns only).</summary>
        public const ETraceFlags HohLegs = (ETraceFlags)0x201;

        /// <summary>CheckFence probes (material requested).</summary>
        public const ETraceFlags Fence = (ETraceFlags)0x1086;

        /// <summary>CheckGEForWallJump wall probes.</summary>
        public const ETraceFlags WallJumpGE = (ETraceFlags)0x10df;

        /// <summary>Barrier test (0x286 with a second GE).</summary>
        public const ETraceFlags Barrier = (ETraceFlags)0x287;

        /// <summary>Clearance tests of the co-op natives: world, stop at the first hit, without pawns (0x286).</summary>
        public const ETraceFlags ClearanceNoPawns = (ETraceFlags)0x286;

        /// <summary>Clearance tests of the co-op natives with pawns (0x287).</summary>
        public const ETraceFlags ClearanceWithPawns = (ETraceFlags)0x287;

        /// <summary>CheckWallJump wall probes (editor 0x111e4ca0).</summary>
        [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 11: flags 0x1084")]
        public const ETraceFlags SplitJumpProbe = (ETraceFlags)0x1084;

        /// <summary>Encroachment and point checks against actors.</summary>
        public const ETraceFlags Encroach = (ETraceFlags)0x9f;
    }

    /// <summary>How far a hit is pulled back along the trace, per primitive (spec/native/collision-primitives.md).</summary>
    [Provenance(ProvenanceKind.Decoded, "UPrimitive::LineCheck 0x110c2400, UModel::LineCheck 0x111943e0, UStaticMesh::LineCheck 0x111b7c70")]
    public static class Pullback
    {
        /// <summary>Cylinder hits: Time - 0.001.</summary>
        public const float Cylinder = 0.001f;

        /// <summary>BSP line hits: half a unit along the trace.</summary>
        public const float BspLine = 0.5f;

        /// <summary>BSP box hits: 0.1 / |D| when that is at most 0.01, else 0.01 (and 0.01 / |D| below 1 uu).</summary>
        public const float BspBox = 0.1f;

        public const float BspBoxCap = 0.01f;

        /// <summary>Static mesh hits: 1 / |D| when that is at most 0.1, else 0.1 (and 0.1 / |D| below 1 uu).</summary>
        public const float Mesh = 1f;

        public const float MeshCap = 0.1f;

        /// <summary>Cylinder start-inside test: radial c below 1 and an inward rate below -0.1.</summary>
        public const float CylinderInsideC = 1f;

        public const float CylinderInward = -0.1f;

        /// <summary>Static mesh traces push tiny direction components to this before the reciprocals.</summary>
        public const float MeshDirectionEpsilon = 1e-6f;
    }

    /// <summary>The zero-extent BSP line walk of UModel::LineCheck (spec/native/ANSWERS-moveset.md W1).</summary>
    [Provenance(ProvenanceKind.Decoded, "UModel::LineCheck editor 0x111943e0, line walk 0x111925a0")]
    public static class Bsp
    {
        /// <summary>A point within this of a node plane counts as on its front side (both ends) or back side.</summary>
        public const float PlaneSlop = 0.001f;

        /// <summary>Node flags that make a node pass the solidity on (NF_NotCsg 0x01, NF_IsNew 0x20).</summary>
        public const ushort NotCsgFlags = 0x21;

        /// <summary>The level model's RootOutside: the tree's root region is solid (a subtractive level).</summary>
        [Provenance(ProvenanceKind.Estimated, "UE2 levels are subtractive; svmap does not carry the field, and on the converted SteeE every path to a leaf crosses a CSG node first, so 0 and 1 give the same hits (BspLineTests)")]
        public const bool LevelRootOutside = false;
    }

    /// <summary>The static mesh box sweep: a vertical cylinder on separating axes (spec/native/ANSWERS-fidelity.md, F1).</summary>
    [Provenance(ProvenanceKind.Decoded, "UStaticMesh::LineCheck triangle test editor 0x111b5d80, axis test 0x111b5ae0, cylinder support 0x111b4bc0")]
    public static class MeshSweep
    {
        /// <summary>An axis shorter than this (squared) has no reach.</summary>
        public const float MinAxisSquared = 0.001f;

        /// <summary>An axis with |Z| at least this reaches the cylinder's half height only.</summary>
        public const float VerticalAxis = 0.99999f;

        /// <summary>Edges and corner directions whose XY length squared is at most this give no axes.</summary>
        public const float MinEdgeXYSquared = 0.001f;

        /// <summary>A corner's axis is used when it points away from both edges (dot products below this).</summary>
        public const float CornerOutside = 0.001f;

        /// <summary>Approach rates within this of zero count as parallel.</summary>
        public const float ParallelRate = 1e-5f;
    }

    /// <summary>performPhysics, startNewPhysics, setPhysics (spec/native/physics-dispatch.md).</summary>
    [Provenance(ProvenanceKind.Decoded, "startNewPhysics editor 0x112157c0, game 0x10acaaf0 (.rdata 0x1149017c / 0x10c11198)")]
    public static class Dispatch
    {
        /// <summary>startNewPhysics returns at once below this step.</summary>
        public const float MinStep = 0.0003f;

        /// <summary>startNewPhysics returns at once when Iterations is above this (an eighth call returns).</summary>
        public const int MaxIterations = 7;

        /// <summary>AvgPhysicsTime = New * deltaTime + (1 - New) * AvgPhysicsTime.</summary>
        public const float AvgPhysicsTimeNew = 0.2f;

        public const float AvgPhysicsTimeOld = 0.8f;
    }

    /// <summary>physWalking, MycalcVelocity, stepUp, CheckForLedges (spec/native/physics-walking.md).</summary>
    [Provenance(ProvenanceKind.Decoded, "physWalking editor 0x112025e0 / game 0x10ac6b40, MycalcVelocity 0x111fc2d0, stepUp 0x1117d2a0")]
    public static class Walk
    {
        /// <summary>Braking decay sub-step.</summary>
        public const float BrakeSubStep = 0.03f;

        /// <summary>Braking stops below this squared speed.</summary>
        public const float BrakeStopSpeedSq = 100f;

        /// <summary>Floor probe depth (Down = 36 G).</summary>
        [Provenance(ProvenanceKind.Decoded, "game .rdata 0x10c10f58")]
        public const float FloorProbe = 36f;

        /// <summary>Pre-probe side offset, radius shrink and "counts" threshold (a .data value never written).</summary>
        [Provenance(ProvenanceKind.Decoded, "editor 0x11626868, game 0x10c437dc")]
        public const float ProbeOffset = 0.2f;

        /// <summary>Walls nearly parallel to the move within this skip the pre-probe.</summary>
        public const float ParallelWall = 0.0001f;

        /// <summary>A move with every component below this is no move.</summary>
        public const float NearlyZero = 0.0001f;

        /// <summary>Moves with a squared length below this are cut (kept in the carry).</summary>
        public const float TinyMoveSq = 0.009f;

        /// <summary>The after-step slide is tried when its length is above this.</summary>
        public const float TinySlide = 0.009f;

        /// <summary>Floor gap band: lift to <see cref="FloorLiftTo"/> below <see cref="FloorLiftBelow"/>, re-drop above <see cref="FloorRedropAbove"/>.</summary>
        public const float FloorLiftBelow = 1.39f;

        public const float FloorLiftTo = 1.405f;

        public const float FloorRedropAbove = 1.42f;

        /// <summary>Depth of the floor check after a step-up (1.42 + 0.1).</summary>
        public const float AfterStepFloorCheck = 1.52f;

        /// <summary>A lost floor within this is re-tested with the narrow box.</summary>
        public const float NarrowLedgeDistance = 32f;

        /// <summary>stepUp takes another step when Time |Delta|^2 is above this.</summary>
        public const float SecondStepMinSq = 144f;

        /// <summary>Pushable decorations need Normal . Dir below this.</summary>
        public const float PushDot = -0.9f;

        /// <summary>CheckForLedges: the front reach is R, or R times this with bAvoidLedges.</summary>
        public const float AvoidLedgesReach = 0.75f;

        /// <summary>CheckForLedges: depth margin below the cylinder.</summary>
        public const float LedgeDepthMargin = 4f;

        /// <summary>CheckForLedges: depth of the box floor test.</summary>
        public const float LedgeBoxDepth = 38f;

        /// <summary>CheckForLedges: the box is (R / n, R / n, H).</summary>
        public const float LedgeBoxRadiusDivisor = 4f;

        /// <summary>physWalkingSimulated, PhysWalkingWithChild: nothing new beyond the walking constants.</summary>
        public const float SteepSlideStep = MAXSTEPHEIGHT;
    }

    /// <summary>physFalling, bCanLand, processLanded (spec/native/physics-falling.md).</summary>
    [Provenance(ProvenanceKind.Decoded, "physFalling editor 0x11213090 / game 0x10ad3dc0, bCanLand 0x11210450, processLanded 0x11210660")]
    public static class Fall
    {
        /// <summary>Air control boost below this horizontal speed.</summary>
        public const float AirControlBoostSpeed = 10f;

        /// <summary>AirControl below this gives no air acceleration.</summary>
        public const float AirControlMin = 0.05f;

        /// <summary>Horizontal air speed cap (417^2 = 173889 is also in the binary).</summary>
        public const float AirSpeedCap = 417f;

        /// <summary>The falling wall pre-probe box is H minus this tall.</summary>
        public const float WallProbeHeightShrink = 10f;

        /// <summary>Drop-attack homing: best cosine to start with.</summary>
        public const float HomingCos = 0.92f;

        /// <summary>Homing needs the target this far below.</summary>
        public const float HomingHeight = 180f;

        /// <summary>Homing needs a horizontal velocity component above this.</summary>
        public const float HomingMinSpeed = 40f;

        /// <summary>The homing direction is lowered by this before normalising.</summary>
        public const float HomingDown = 3f;

        /// <summary>Line of sight end above the target.</summary>
        public const float HomingSightRaise = 100f;

        /// <summary>Homing pull: offset clamped to this length.</summary>
        public const float HomingPull = 200f;

        /// <summary>Homing gain k = min(Cap, Scale (|v| + Add)).</summary>
        public const float HomingGainScale = 0.04f;

        public const float HomingGainAdd = 100f;

        public const float HomingGainCap = 20f;

        /// <summary>Drop trigger: within this many H of the target.</summary>
        public const float TriggerReachH = 2.1f;

        /// <summary>Drop trigger: Fwd . offset above this.</summary>
        public const float TriggerFacing = 0.7f;

        /// <summary>Drop trigger: Z - Other.Z below this many H.</summary>
        public const float TriggerAboveH = 0.5f;

        /// <summary>Drop trigger: (Z - H) - Other.Z + Other.H above this (physFalling); physFallingInRootMotion uses 50.</summary>
        public const float TriggerOverlap = -100f;

        public const float TriggerOverlapRootMotion = 50f;

        /// <summary>physFallingInRootMotion: -100 &lt;= Z - Other.Z.</summary>
        public const float TriggerAboveRootMotion = -100f;

        /// <summary>Drop trigger: Velocity.Z below this.</summary>
        public const float TriggerSpeedZ = -800f;

        /// <summary>Landing probe box scale of (R, R, H).</summary>
        public const float LandingBoxScale = 0.9f;

        /// <summary>Landing probe depth: Scale H + Add.</summary>
        public const float LandingDepthScaleH = 0.2f;

        public const float LandingDepthAdd = 8f;

        /// <summary>Narrow-top side probes: reach Scale R.</summary>
        public const float SideProbeReachR = 0.2f;

        /// <summary>Narrow-top side probes: a surface steeper than this (Normal.Z below) is slid off.</summary>
        public const float SideProbeSlopeZ = 0.3f;

        /// <summary>Narrow-top side probes: push Scale R along the surface normal.</summary>
        public const float SideProbePushR = 0.1f;

        /// <summary>Narrow-top side probes: a push blocked below this Time lands.</summary>
        public const float SideProbeBlockedTime = 0.1f;

        /// <summary>Landing velocity rewrite needs Time above this and tick Time above <see cref="VelocityRewriteMinTime"/>.</summary>
        public const float VelocityRewriteMinFraction = 0.1f;

        public const float VelocityRewriteMinTime = 0.003f;

        /// <summary>processLanded's FindSpot box scale of (R, R, H).</summary>
        public const float FindSpotScale = 1.1f;

        /// <summary>Root-motion falling: hits with Normal.Z above this land, others are walls.</summary>
        public const float RootMotionLandZ = 0.707f;
    }

    /// <summary>ASBasePawn::MyDoJump (spec/native/ANSWERS-fidelity.md, F2).</summary>
    [Provenance(ProvenanceKind.Decoded, "ASBasePawn::MyDoJump editor 0x111f9cf0, game 0x10adff00 (exec 0x10ae00b0)")]
    public static class Jump
    {
        /// <summary>Letting go of a fence, pipe or ladder (or a second call during SA_LEDGEFALL): Velocity = this x the facing.</summary>
        public const float PushOffFacing = -200f;

        /// <summary>The push-off's upward part, added to Velocity.Z.</summary>
        public const float PushOffUp = 50f;

        /// <summary>The second jump in the air (nJumpNumber 1): Velocity = this x vWallJumpDir ...</summary>
        public const float WallJumpSpeed = 250f;

        /// <summary>... with Velocity.Z set to this.</summary>
        public const float WallJumpZ = 700f;

        /// <summary>SA_LEDGEFALL: MyDoJump while it plays pushes off like a fence.</summary>
        public const int SA_LEDGEFALL = 119;
    }

    /// <summary>Root motion, repositioning and Vandamme (spec/native/physics-rootmotion-repositioning.md).</summary>
    [Provenance(ProvenanceKind.Decoded, "physRootMotion 0x112128c0, physRepositionning 0x111fc7a0, physVandamme 0x111fb380 (editor)")]
    public static class RootMotion
    {
        /// <summary>physRootMotion runs once per game time step (guard).</summary>
        public const float TimeGuard = 0.0001f;

        /// <summary>Repositioning ignores a height difference below this.</summary>
        public const float ReposDeadBand = 5f;

        /// <summary>Snap tolerance of the straight-line moves.</summary>
        public const float SnapTolerance = 0.1f;

        /// <summary>Yaw turn per call of repositioning, Vandamme, ledge and zip (not scaled by dt).</summary>
        public const int TurnRate = 3000;

        /// <summary>physVandamme speed (a literal, not fPhysReposLinearSpeed).</summary>
        public const float VandammeSpeed = 87f;

        /// <summary>Repositioning arrives within this distance (with the yaw equal).</summary>
        public const float ArrivalDistance = 1f;

        /// <summary>The root-motion action the server re-tests with TestTopLedge.</summary>
        public const int ServerTestedAction = 113;

        /// <summary>Lock bit that also applies the root rotation.</summary>
        public const int LockRotationBit = 2;
    }

    /// <summary>The GE traversal modes (spec/native/physics-traversal.md).</summary>
    [Provenance(ProvenanceKind.Decoded, "physOnPipe 0x1120a470, physOnPipeH 0x112092c0, physOnLadder 0x1120b5d0, physLinear 0x11208af0, physOnVerticalPlane 0x11208670, physLedge 0x11204870 (editor)")]
    public static class Traversal
    {
        /// <summary>Hold-and-snap speed of every GE mode.</summary>
        public const float SnapSpeed = 500f;

        /// <summary>A snap move is taken whole within this.</summary>
        public const float SnapTolerance = 0.1f;

        /// <summary>Points of a new GE differ when an X or Y differs by more than this.</summary>
        public const float SwapTolerance = 1f;

        /// <summary>Yaw per second of the pipe hold (16384 dt per call).</summary>
        public const float PipeYawRate = 16384f;

        /// <summary>Yaw per call of the hand-over-hand and ladder holds.</summary>
        public const int HoldTurnRate = 1000;

        /// <summary>Input threshold that switches pipe and ladder to the slide or the rung stop.</summary>
        public const float InputThreshold = 10f;

        /// <summary>Pipe slide: Velocity.Z is kept in [SlideMinZ, 0].</summary>
        public const float PipeSlideMinZ = -450f;

        /// <summary>Pipe slide: TopSynchro when (HOH zone Z + Location.Z) - pipe bottom is below this.</summary>
        public const float PipeBottom = 210f;

        /// <summary>Pipe swap: the flip test compares |P.Z - Location.Z - this|.</summary>
        public const float PipeSwapHeight = 20f;

        /// <summary>Arms zone of the pipe climbing GE searches (up) and of the climb down.</summary>
        public static readonly FVector PipeClimbZone = new(40f, 0f, 90.3f);

        public static readonly FVector PipeClimbDownZone = new(40f, 0f, -40.3f);

        /// <summary>Pipe to HOH (down): HOH within dz above this and horizontal distance^2 below <see cref="PipeHohDistSq"/>.</summary>
        public const float PipeHohDownMinDz = -210f;

        public const float PipeHohDistSq = 600f;

        /// <summary>Pipe to HOH (down): the clearance boxes go down this, then forward <see cref="PipeHohForward"/>.</summary>
        public const float PipeHohDown = 207.2f;

        public const float PipeHohForward = 97.9f;

        /// <summary>Pipe to HOH (up): dz below this, the climb adds dz + <see cref="PipeHohUpAdd"/>.</summary>
        public const float PipeHohUpMaxDz = -6f;

        public const float PipeHohUpAdd = 6f;

        /// <summary>Pipe to zip: dz below this, distance^2 below <see cref="PipeZipDistSq"/>, the climb removes dz - <see cref="PipeZipOffset"/>.</summary>
        public const float PipeZipMaxDz = 10f;

        public const float PipeZipDistSq = 400f;

        public const float PipeZipOffset = 5.63f;

        /// <summary>Pipe top to ledge: the LG closest point within horizontal distance^2 and height of the top.</summary>
        public const float PipeTopLedgeDistSq = 1000f;

        public const float PipeTopLedgeDz = 2f;

        /// <summary>TestTopPipe segments: right, up (standing / crouched), right again, forward (standing / crouched).</summary>
        public const float TopPipeRight = 30f;

        public const float TopPipeUp = 191.5f;

        public const float TopPipeUpCrouched = 156.9f;

        public const float TopPipeRight2 = 15f;

        public const float TopPipeForward = 79.4f;

        public const float TopPipeForwardCrouched = 92.7f;

        /// <summary>Hand over hand: stop this far from an end (legs-over pose: <see cref="HohEndLegsOver"/>).</summary>
        public const float HohEnd = 27f;

        public const float HohEndLegsOver = 95f;

        /// <summary>Hand over hand: input below this (and above 0) is the legs-over pose.</summary>
        public const float HohLegsOverInput = 120f;

        /// <summary>Hand over hand: a ZL end within this joins the zip line.</summary>
        public const float HohZipJoin = 3f;

        /// <summary>Hand over hand: a perpendicular HOH within this horizontal distance^2 (31^2) chains.</summary>
        public const float HohOrthoDistSq = 961f;

        /// <summary>Hand over hand legs probe: 140 ahead, re-tested at 70.</summary>
        public const float HohLegsProbe = 140f;

        public const float HohLegsProbeNear = 70f;

        /// <summary>Zone configuration of a pawn hanging legs over (pose 3).</summary>
        public const byte HohLegsOverZoneConfig = 3;

        /// <summary>Hand over hand to pipe: reach zone.X - 34 (pipe end below) or 48 - zone.X.</summary>
        public const float HohPipeBelowReach = 34f;

        public const float HohPipeAboveReach = 48f;

        /// <summary>Hand over hand to pipe: clearance 86.35 forward then 205 up.</summary>
        public const float HohPipeForward = 86.35f;

        public const float HohPipeUp = 205f;

        /// <summary>Ladder rungs: spacing, offset above the bottom, snap speed, dead band.</summary>
        public const float RungSpacing = 64f;

        public const float RungOffset = 28f;

        public const float RungSnapSpeed = 100f;

        public const float RungDeadBand = 2f;

        /// <summary>Ladder hand height above the hold target, and the top margin.</summary>
        public const float LadderHandHeight = 2f;

        /// <summary>Ladder slide: below this input the speed is at least <see cref="LadderSlideFast"/>; floor <see cref="LadderSlideMin"/>; slowing caps at <see cref="LadderSlideSlow"/>.</summary>
        public const float LadderSlideSplit = -524.5f;

        public const float LadderSlideFast = -200f;

        public const float LadderSlideMin = -700f;

        public const float LadderSlideSlow = -64f;

        /// <summary>The ladder top needs Acceleration.Z above this.</summary>
        public const float LadderTopInput = 0.1f;

        /// <summary>Zip line: speed above GroundSpeed eases by this factor per second.</summary>
        public const float ZipEase = 0.03f;

        /// <summary>Fence: hits steeper than this (Normal.Z above) land.</summary>
        public const float FenceLandZ = 0.707f;

        /// <summary>Fence: the pawn is held R + this off the face.</summary>
        public const float FenceGap = 2f;

        /// <summary>Ledge: the hang target is this far out from the ledge.</summary>
        public const float LedgeOut = 39f;

        /// <summary>Ledge hang heights: WAIST, BARRIER (with its 10 band) and FEET.</summary>
        public const float LedgeWaistDown = 20f;

        public const float LedgeBarrierDown = 10f;

        public const float LedgeFeetUp = 50.5f;

        /// <summary>Ledge side test point: this far behind the pawn.</summary>
        public const float LedgeBehind = 100f;

        /// <summary>Ledge shimmy stops this far from an end.</summary>
        public const float LedgeEndStop = 36f;

        /// <summary>Ledge head room box trace height.</summary>
        public const float LedgeHeadRoom = 50f;

        /// <summary>Ledge corner search zone (Y signed by the input side), end distance, shared-end tolerance, room.</summary>
        public static readonly FVector LedgeCornerZone = new(37f, 35f, 90.3f);

        public const float LedgeCornerEnd = 37f;

        public const float LedgeSharedEnd = 2f;

        public const float LedgeCollinear = 0.001f;

        public const float LedgePerpendicular = 0.1f;

        public const float LedgeCornerRoom = 75f;

        /// <summary>Ledge to pipe: forward, vertical and side windows.</summary>
        public const float LedgePipeForward = 46f;

        public const float LedgePipeDz = 2f;

        public const float LedgePipeSide = 16f;

        /// <summary>Ledge: hits with Normal.Z above this land, others are walls.</summary>
        public const float LedgeLandZ = 0.707f;
    }

    /// <summary>GE search and the zone tests (spec/native/physics-ge-queries.md).</summary>
    [Provenance(ProvenanceKind.Decoded, "FindArmsGE 0x11204510, Check*Arms / Check*Feet, TestTopLedge 0x1120f610, bTestTopLadder 0x111e7c60, CheckFence 0x11207de0 (editor)")]
    public static class GE
    {
        /// <summary>Two-point GEs connect when some ends are within this on every axis.</summary>
        public const float ConnectTolerance = 2f;

        /// <summary>Two GEs are equal when every point is within this on every axis.</summary>
        public const float EqualTolerance = 0.1f;

        /// <summary>Grab facing: Fwd . N below this.</summary>
        public const float GrabFacing = -0.7f;

        /// <summary>Ledge hang: end margin, out, down, height window, distance^2 and fastest fall.</summary>
        public const float LedgeEndMargin = 24.5f;

        public const float LedgeHangOut = 39f;

        public const float LedgeHangDown = 117f;

        public const float LedgeWindowLow = -5f;

        public const float LedgeWindowHigh = 35f;

        public const float LedgeDistSq = 830f;

        public const float LedgeMinVelocityZ = -1270f;

        /// <summary>Ledge tests: the hang offset off the wall is R + this.</summary>
        public const float WallGap = 2f;

        /// <summary>Waist ledge: height, window, distance^2, velocity window, clearances.</summary>
        public const float WaistDown = 20f;

        public const float WaistWindowLow = -18f;

        public const float WaistWindowHigh = 22f;

        public const float WaistDistSq = 225f;

        public const float WaistMinVelocityZ = -300f;

        public const float WaistMaxVelocityZ = 600f;

        public const float WaistUp = 114.5f;

        public const float WaistOver = 56f;

        public const float WaistUpCrouched = 109.2f;

        public const float WaistOverCrouched = 77f;

        /// <summary>Forward speed (pawn space) must be above this for the waist, feet and barrier tests.</summary>
        public const float MinForwardSpeed = -1f;

        /// <summary>Auto feet ledge: the ledge 40 to 64 above the feet, forward speed, distance factor, top margin, over.</summary>
        public const float AutoFeetLow = 40f;

        public const float AutoFeetHigh = 64f;

        public const float AutoFeetMinSpeed = 300f;

        public const float AutoFeetDistFactor = 0.35f;

        public const float AutoFeetTopMargin = 1f;

        public const float FeetOver = 112.4f;

        /// <summary>Feet ledge: top offset, window, velocities, distance^2, merc floor probe and clearance up.</summary>
        public const float FeetTop = 50.5f;

        public const float FeetWindow = 15f;

        public const float FeetMinVelocityZ = -1f;

        public const float FeetMaxFallZ = -500f;

        public const float FeetDistSq = 1600f;

        public const float FeetMercFloorProbe = 51f;

        public const float FeetUp = 39f;

        /// <summary>Hand over hand grab: end margin, height margin, velocity window, distance^2.</summary>
        public const float HohEndMargin = 35f;

        public const float HohHeightMargin = 40f;

        public const float HohMaxVelocityZ = 600f;

        public const float HohDistSq = 1000f;

        /// <summary>Ladder grab: spot offset, spot radius, facings.</summary>
        public const float LadderSpot = 35f;

        public const float LadderSpotRadius = 20f;

        public const float LadderFacing = 0.7f;

        public const float LadderFacingAir = 0.3f;

        /// <summary>Pipe grab: pipe above and pawn above the bottom, spot offset and radius.</summary>
        public const float PipeMinAbove = 85f;

        public const float PipeSpot = 35f;

        public const float PipeSpotRadius = 20f;

        public const float PipeHandsAbove = 2f;

        /// <summary>Zip grab velocity window.</summary>
        public const float ZipMinVelocityZ = -1270f;

        public const float ZipMaxVelocityZ = 254f;

        /// <summary>Fence top: box (35, 35, 20), up, over, down.</summary>
        public static readonly FVector FenceTopBox = new(35f, 35f, 20f);

        public const float FenceTopUp = 127.5f;

        public const float FenceTopOver = 75f;

        public const float FenceTopDown = 198.25f;

        /// <summary>Ladder top from above: facing, height margin, spot, lift, boxes, out, down.</summary>
        public const float LadderTopFacing = 0.9f;

        public const float LadderTopAbove = 24f;

        public const float LadderTopSpot = 72f;

        public const float LadderTopLift = 16f;

        public const float LadderTopBoxZ = 71.5f;

        public const float LadderTopDown = 192f;

        /// <summary>Pipe top from above: stick limit, facing, spot gap, lift, boxes and the two down-the-pipe offsets.</summary>
        public const float PipeTopStickSq = 4000f * 4000f;

        public const float PipeTopFacing = 0.7f;

        public const float PipeTopGap = 15f;

        public const float PipeTopLift = 2f;

        public const float PipeTopNarrowBox = 26f;

        public static readonly FVector PipeTopRight = new(96.3f, 38.67f, -181.7f);

        public static readonly FVector PipeTopLeft = new(97.3f, -39.17f, -186.15f);

        /// <summary>TestTopLedge: stand up / forward, crouch up / forward, vault rise / reach / drop / box Z.</summary>
        public const float TopStandUp = 210.8f;

        public const float TopStandForward = 57.8f;

        public const float TopCrouchUp = 206.1f;

        public const float TopCrouchForward = 75.7f;

        public const float TopVaultRise = 117f + 45f;

        /// <summary>The vault rise over the ledge-grab arms zone height (TopVaultRise with the default zone).</summary>
        public const float TopVaultRiseAboveArms = 45f;

        public const float TopVaultForward = 95f;

        public const float TopVaultDrop = 78f;

        public const float TopVaultBoxZ = 44f;

        /// <summary>TestTopLedge barrier above: zone, height window, side points, top.</summary>
        public static readonly FVector TopBarrierZone = new(37f, 0f, 210f);

        public const float TopBarrierLow = 90f;

        public const float TopBarrierHigh = 102f;

        public const float TopBarrierLeft = 20f;

        public const float TopBarrierRight = 75f;

        public const float TopBarrierForward = 95f;

        public const float TopBarrierTop = 173f;

        // ---- co-op natives (spec/native/ANSWERS-moveset.md M1 to M6) ----

        /// <summary>AreaIsSafeToInteract: the spot before the mate and the floor probe under it.</summary>
        public const float CoopSafeSpot = 70f;

        public const float CoopSafeFloor = 15f;

        /// <summary>AreaIsSafeToInteract trace flags.</summary>
        public const ETraceFlags CoopSafeFlags = (ETraceFlags)0x80086;

        /// <summary>CheckIfCanInteractWithMate, boost helper: Z window over the mate's radius, facing dots.</summary>
        public const float CoopBoostZOverRadius = 15f;

        public const float CoopBoostFacing = 0.75f;

        /// <summary>CheckIfCanInteractWithMate, hanging helper: the point ahead, Z window, reach, facing dots.</summary>
        public const float CoopHoistAhead = 40f;

        public const float CoopHoistZLow = 237.36f;

        public const float CoopHoistZHigh = 356.04f;

        public const float CoopHoistReachSquared = 90000f;

        public const float CoopHoistFacing = 0.2f;

        /// <summary>The climber's box height scale and the spot under a hanging helper (back along X, down along Z).</summary>
        public const float CoopHoistBoxHeightScale = 0.85f;

        public const float CoopHoistBack = 130.7f;

        public const float CoopHoistDown = 296.7f;

        /// <summary>CheckIfCanStartSHL: the search zone height over the feet and its radius.</summary>
        public const float ShlSearchHeight = 512f;

        public const float ShlSearchRadius = 150f;

        /// <summary>The short-ladder spot: ledge flat within 15 (squared), 502 to 512 above the feet.</summary>
        public const float ShlFlatSquared = 225f;

        public const float ShlLedgeLow = 502f;

        public const float ShlLedgeHigh = 512f;

        /// <summary>The short-ladder spot: across and along limits of the unscaled products.</summary>
        public const float ShlAcross = 250f;

        public const float ShlAlong = 18086f;

        /// <summary>The short-ladder spot: a ledge shorter than this uses its middle; the spot is R + 10 out, 510 under.</summary>
        public const float ShlShortLedge = 192f;

        public const float ShlSpotOut = 10f;

        public const float ShlSpotDown = 510f;

        /// <summary>The short-ladder room test flags (ignoring pawns, and the default).</summary>
        public const ETraceFlags ShlRoomIgnorePawns = (ETraceFlags)0x284;

        /// <summary>FindHoistClimbType: floor probe, room behind, column, quick barrier zone and radius, hang point.</summary>
        public const float HoistFloorFrom = 83f;

        public const float HoistFloorTo = 97f;

        public const float HoistFloorBoxZ = 5f;

        public const float HoistRoomBehind = 126f;

        public const float HoistColumnTop = 456f;

        public const float HoistColumnBottom = 10f;

        public const float HoistColumnTime = 0.96f;

        public static readonly FVector HoistQuickZone = new(37f, 0f, 210f);

        public const float HoistQuickRadius = 40f;

        public const float HoistHangIn = 8f;

        /// <summary>FindHoistClimbType wall probes: toward the wall, the three heights, the box.</summary>
        public const float HoistWallReach = 75f;

        public const float HoistWallLow = 69f;

        public const float HoistWallMid = 230f;

        public const float HoistWallHigh = 345f;

        public static readonly FVector HoistWallBox = new(35f, 35f, 15f);

        /// <summary>CheckIfBarrier: the barrier search zone above the default top (512 - defH + 96) and radius.</summary>
        public const float BarrierSearchAbove = 96f;

        public const float BarrierSearchRadius = 150f;

        /// <summary>bTestTopLadder: spy up / forward, merc up / forward, uncrouch box Z, below and above.</summary>
        public const float LadderExitUpSpy = 192f;

        public const float LadderExitForwardSpy = 73.9f;

        public const float LadderExitUpMerc = 196f;

        public const float LadderExitForwardMerc = 68.8f;

        public const float LadderUncrouchBoxZ = 8f;

        public const float LadderUncrouchBelow = 20f;

        public const float LadderUncrouchAbove = 79.5f;

        /// <summary>CheckFence: reach R + 20, corners R + 10 and H - 15, normal tolerance.</summary>
        public const float FenceReach = 20f;

        public const float FenceCornerSide = 10f;

        public const float FenceCornerHeight = 15f;

        public const float FenceNormalTolerance = 0.01f;

        /// <summary>CheckBarrier: box Z, beyond, aside, above, down, window and distance^2.</summary>
        public const float BarrierBoxZ = 22.5f;

        public const float BarrierBeyond = 140f;

        public const float BarrierAside = 20f;

        public const float BarrierAbove = 23.5f;

        public const float BarrierDown = 85.2f;

        public const float BarrierLow = -5f;

        public const float BarrierHigh = 40f;

        public const float BarrierDistSq = 900f;

        /// <summary>CheckWallJump: probe length, start height (153 - 0.4 H), H factor, near distance R + 3, windows.</summary>
        public const float WallJumpProbe = 200f;

        public const float WallJumpStart = 153f;

        public const float WallJumpHeightH = 0.4f;

        public const float WallJumpNear = 3f;

        public const float WallJumpFirstMin = 182f;

        public const float WallJumpFirstMax = 200f;

        public const float WallJumpSecondMin = 180f;

        public const float WallJumpSecondMax = 198f;

        /// <summary>CheckGEForWallJump: facing, first-hit window, second probe height and side, consistency, ledge search.</summary>
        public const float GEWallFacing = -0.94f;

        public const float GEWallMin = 105f;

        public const float GEWallHigh = 160f;

        public const float GEWallSide = 35f;

        public const float GEWallConsistency = 10f;

        public const float GEWallConsistencySide = 12.75f;

        public const float GEWallLedgeCos = 0.342f;

        public const float GEWallLedgeAbove = 87.5f;

        public static readonly FVector GEWallLedgeBox = new(60f, 60f, 300f);

        public const float GEWallNoLedgeZ = 200f;

        /// <summary>CheckWallJump's direction picker: probes from R - 1 inside the cylinder, 18 long.</summary>
        [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 11: picker 0x111e4df0, R - 1 and 18")]
        public const float WallJumpPickInset = 1f;

        [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 11: picker reach 18")]
        public const float WallJumpPickReach = 18f;

        /// <summary>CheckGEForWallJump without a controller: the default fDistanceForContextualJump.</summary>
        [Provenance(ProvenanceKind.Decoded, "SPlayerController default fDistanceForContextualJump 500")]
        public const float GEWallDefaultReach = 500f;

        [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 12: side probes 72.5 up")]
        public const float GEWallMidHeight = 72.5f;

        [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 12: room box to d1 - 50")]
        public const float GEWallRoomShort = 50f;

        [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 12: floor boxes (35, 35, 60)")]
        public const float GEWallFloorBoxZ = 60f;

        [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 12: floor box at d1 - 55")]
        public const float GEWallFloorNear = 55f;

        [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 12: floor box at d1 - 130 when d1 > 130")]
        public const float GEWallFloorMid = 130f;

        [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 12: floor box at d1 - 210 when d1 > 210")]
        public const float GEWallFloorFar = 210f;

        [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 12: the floor boxes drop 50")]
        public const float GEWallFloorDrop = 50f;

        [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 12: LG search box +-30 in X and Y")]
        public const float GEWallLedgeSearch = 30f;

        [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 12: the climb box starts at d1 - 40")]
        public const float GEWallClimbBack = 40f;

        [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 12: up to the ledge when it is at least 100 above")]
        public const float GEWallClimbMin = 100f;
    }
}
