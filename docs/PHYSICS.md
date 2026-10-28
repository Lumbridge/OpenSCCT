# Physics (OpenScct.Physics)

`OpenScct.Physics` is the collision world and the engine physics of the simulation: the line and box traces the
natives and the script use, every pawn physics mode of SCCT (walking, falling, root motion, repositioning,
Vandamme and the GE traversal modes), the GE zone natives, movers with encroachment, and MoveActor. It is pure C#,
Godot-free and deterministic (the banned-API analyzer applies).

The rules follow the native code decoded in `spec/native/physics-*.md` and `collision-*.md` (physics v2). Every
port carries `[UcNative]`, `[PortOf]` and `[Provenance]`; a rule stays `Estimated` only where the spec leaves it
open, and the attribute says what is guessed. "Still estimated" below lists them.

## Pawn movement API

What the SBasePawn and SPlayerController movement port calls and implements. These names and signatures are
stable.

### What the pawn port implements

- `SBasePawn : ISBasePawnPhysics` (OpenScct.Physics.Movement): explicit members returning references to its own fields
  (`ref FVector ISBasePawnPhysics.ZipP1 => ref ZipP1;`), the class defaults (`DefaultCollisionRadius`,
  `DefaultCollisionHeight`, `DefaultPrePivot`, `DefaultHealth`), `UnCrouch(bClientSimulation)` and
  `RootMotionMesh` (`IRootMotionMesh`: lock mode and root delta, from the animation port). Enum and actor fields
  are get/set (`TypeLedge` as a byte, `m_AttackTarget` as a Pawn). `tests/OpenScct.Physics.Tests/TestSupport.cs`
  (`TestSBasePawn`) is a complete example.
- `SPlayerController : ISPlayerControllerPhysics` (extends `IPhysicsEvents`): `ref fZMaxFalling`,
  `ref fLastTimeOnAMover`, `bTestBackFeetGrab`, `fDistanceForContextualJump`, and the events the natives raise,
  each named `event<UcName>`: MyFalling, MyNotifyLanded, NotifyHitWall, NotifyBump, FellOutOfWorld, DropAttack
  (from `IPhysicsEvents`); CanBeDropAttacked, StickyBlast, MayFall, TopSynchro, NoGE, Ledge, WaistLedge,
  AutoFeetLedge, FeetLedge, Pipe, TopPipe, TopPipeCrouch, NarrowLadder, TopLadder, HandOverHand, OrthoPipeH,
  ZipLine, ChangeLedge, FenceTop, ClientStopRM. `TestController` in the same file is an example.
- Fields the natives use that already live on the generated Core classes are read there directly: Pawn.Floor,
  bCanWalkOffLedges, bNoVelocityUpdate, bSimulateGravity, bDirectHitWall, bAvoidLedges, DesiredSpeed, OldZ,
  OldAcceleration, AvgPhysicsTime, vLocationBeforeRM, rRotBeforeRM, CrouchHeight, CrouchRadius, bIsCrouched,
  bIsWalking, bCanJump, Health; Actor.PrePivot, bJustTeleported, bUseRootMotionAdditionnalPhysics, bCollideWorld,
  Mass, bIsAMover; Controller.MinHitWall, MoveTimer; LevelInfo.TimeSeconds, fDeathPawnHeight, GEActivationMask.

The script decides the physics mode through its actions: the natives never switch Walking to Falling or Falling
to Walking themselves for an SBasePawn. MyFalling's handler plays the fall action (`setPhysics(PHYS_Falling)`) and
MyNotifyLanded's handler the landing (`setPhysics(PHYS_Walking)`); the native then carries on with the rest of the
frame in the new mode (landing) or stops (MyFalling is raised with LeftTime 0). A controller without
`IPhysicsEvents` gets the stock UE2 switch instead (Falling and Landed events, the mode set by the native).

The script also sets the inputs every frame, and the port must too: the natives write Acceleration and GroundSpeed
(walking sets GroundSpeed to 0 with no input; landing sets it to the landing speed; the zip ease writes
Acceleration; the air clamp is undone at the end of physFalling). Events are raised every frame their condition
holds (TopPipe at a pipe top, TopSynchro while a hold is settled), as in the original; the script's state change
is what stops them.

### What the pawn port calls

`PhysicsDriver` (get it with `PhysicsDriver.Of(actor)`, the level's driver):

| Call | Native |
|---|---|
| `setPhysics(actor, mode, NewFloor = null, NewFloorV = default)` | ASBasePawn::setPhysics (ExecuteAction's mode changes) |
| `startNewPhysics(actor, dt, Iterations)` | the mode switch |
| `MyDoJump(pawn)` | physics part of SBasePawn.MyDoJump |
| `MycalcVelocity(pawn, AccelDir, dt, maxSpeed, friction, bFluid, bBrake)` (static) | ASBasePawn::MycalcVelocity |
| `MoveActor(actor, Delta, NewRotation, out Hit)` and the full form with `bTest, bIgnorePawns, bIgnoreBases, bNoFail` | ULevel::MoveActor |
| `FarMoveActor(actor, Dest, bTest = false, bNoCheck = false, bAttachedMove = false)` | ULevel::FarMoveActor |
| `CheckEncroachment(actor, Location, Rotation, bTouchNotify)` | ULevel::CheckEncroachment |
| `MovePawn(pawn, vDelta)` | SBasePawn.MovePawn |
| `CanUnCrouch(pawn)` | SBasePawn.CanUnCrouch (standing cylinder fits) |
| `SetZipSpeed(pawn)`, `UnZipSpeed(pawn, bTest)`, `CanUnZipSpeed(pawn)` | zip speed pose |
| `EndRootMotion(pawn)`, `bServerTestRootMotion(pawn, Action, vMove, Yaw)`, `EndPhysRepositionning(pawn)` | root motion and repositioning ends |
| `CheckArmsZone(pawn)`, `CheckFeetZone(pawn)`, `CheckBackFeetZone(pawn)` | zone natives (raise the GE events) |
| `FindArmsGE(pawn, out Best, out Second)` (and with a zone offset and radius) | ASBasePawn::FindArmsGE |
| `TestTopLedge(pawn, bCrouchOnly)`, `bTestTopLadder(pawn, out iCanUncrouch)`, `TestTopPipe(pawn, dz, bCrouch)` | top-exit room tests |
| `CheckFence(pawn, out Normal, out Wall, Pos)`, `CheckBarrier(pawn)` | fence and barrier tests |
| `CheckWallJump(pawn, X, Y, Z, out HitNormal)`, `CheckGEForWallJump(pawn, X, Y, Z, out GEZPosition)` | wall jump tests |
| `ZMaxFalling(pawn)` | the fall apex (the controller's fZMaxFalling, or the driver's copy) |
| `processHitWall(actor, HitNormal, HitActor)`, `FindBase(actor)`, `FindSpot(actor, ref Location)` | engine helpers |
| `ArmsZonePoint(pawn, offset)`, `FeetZonePoint(pawn, offset)`, `GEClosestPoint`, `GEConnected`, `GEEquals` (static) | GE geometry |

Every physics mode of the dispatch table runs inside `performPhysics`; the pawn port only sets the mode and the
inputs (Acceleration, GroundSpeed and the GE fields) and answers the events. `SetModeHandler(mode, handler)` still
replaces a mode for tests and experiments.

## Setup

```csharp
SvMap map = SvMap.Load(path);
var world = new World(matchSeed, ENetMode.NM_DedicatedServer);
MapLoadResult loaded = new MapLoader().Load(world, map);
PhysicsWorld physics = PhysicsWorld.Attach(map, loaded);   // binds the level, sets Level.PhysicsDriver

physics.Collision    // CollisionWorld : ICollisionWorld
physics.Driver       // PhysicsDriver : IActorPhysics (with the map's GE index)
physics.GEs          // GEIndex over sim/traversal.json
physics.Traversal    // GEQueries: zone searches by kind, ledge clearance, hang spot
```

`CollisionWorld.Build(CollisionData)` and `BindLevel(level, resolveActor)` can be used on their own, and
`CollisionDataBuilder` makes collision data by hand (boxes, ramps, triangle meshes, box hulls, zones) for tests,
tools and native maps. `new PhysicsDriver(collision, ges)` makes a driver without a map.

Dependencies: OpenScct.Physics references OpenScct.Core and OpenScct.Content (for `CollisionData` and `TraversalDoc`).

## The collision world

Built from `sim/collision.bin` (spec/svmap/1.1/collision.md):

- **Static set.** Every collider that never moves (world BSP, brushes, static mesh triangles and hulls) is baked
  to world space with its transform (`((local - PrePivot) * Scale) * M(Rotation) + Location`, mirrored scales flip
  the winding) and put in one BVH for triangles and one for hulls.
- **Dynamic colliders.** Movers, glass, gameplay objects and attached collision keep their local geometry and own
  BVH and follow their actor's Location and Rotation (rewritten and refitted before a query when they differ).
  `SetColliderTransform`, `SetColliderEnabled` and `SetActorCollidersEnabled` switch them.
- **Geometry rule.** Hulls when a collider has any (convex brushes: solid, as BSP is), else its triangles (the
  world BSP and every static mesh set, simple collision included: the hull tree is a triangle tree).
- **Static mesh sets.** A static mesh has a per-poly collider (meshPoly) and, when it has simple collision, a
  simple one (meshHull) right after it with the same owner and transform. Traces use the simple set and only
  `TRACE_ShadowCast` traces use the per-poly set (UStaticMesh::LineCheck). Movers, glass and gameplay objects use one
  kind for both sets, so `PairMeshSets` reads the 1.1 `CSRC` chunk (kept in `UnknownChunks`): a perPoly row followed
  by a simple row of the same source, asset, variants, owner and transform is a pair. A map without CSRC keeps both
  sets of those actors colliding. Terminal `mode_neutralisation`: per-poly reaches X 33.2, the two simple boxes 28.2,
  so only the simple set lets a 35 uu pawn reach the 64 uu use spot.
- **Actor cylinders.** Colliding actors without colliders collide as cylinders (CollisionRadius, CollisionHeight)
  with the trace flags of their CYLS record or their live fields (blockZero/NonZeroExtent, blockCamera).
- **Live flags.** A collider uses its map flags while its owner keeps the flags it had at bind time; once script
  changes them (SetCollision) the owner's live fields apply. Cylinders follow the same rule.

The BVHs use a binned surface-area split (leaf size 4); node bounds are widened by 0.05 uu at query time. A query
never allocates. The world is not thread-safe.

## Queries

`ICollisionWorld` (UE2 semantics; `Extent` zero traces a line, non-zero sweeps an axis-aligned box of those half
sizes, pawns use `(CollisionRadius, CollisionRadius, CollisionHeight)`):

| Query | UE2 | Result |
|---|---|---|
| `LineCheck(out Hit, Source, End, Start, Extent, TraceFlags)` | `ULevel::SingleLineCheck` (inverted: true means hit) | first hit |
| `MultiLineCheck(Hits, Source, End, Start, Extent, TraceFlags)` | `ULevel::MultiLineCheck` | first contact per collider and per actor, sorted by pulled-back time (so a mesh level with BSP wins), nothing past the first world BSP hit |
| `PointCheck(out Hit, Source, Location, Extent, TraceFlags)` | `ULevel::SinglePointCheck` | true when the box overlaps something |
| `FastTrace(End, Start)` | `Actor.FastTrace` | true when a line sees through the world |
| `GetZoneIndex`, `GetZoneInfo` | `UModel::PointRegion` | zone by BSP walk, else by zone bounds |

### Pull-backs (decoded)

A hit's Time is the contact time pulled back along the trace by the primitive's rule, clamped to [0, 1]:

| Primitive | Pull-back |
|---|---|
| actor cylinder | Time - 0.001 |
| BSP (world, brushes), line | half a unit: Time - 0.5 / \|D\| |
| BSP, box | Time - min(0.1 / \|D\|, 0.01) (0.1 uu for traces of 10 uu or more, 1% below; 0.01 / \|D\| under 1 uu) |
| static mesh (and movers, glass, gameplay objects, attached), line or box | Time - min(1 / \|D\|, 0.1) (1 uu for traces of 10 uu or more, 10% below; 0.1 / \|D\| under 1 uu) |

A walking pawn therefore rests 1.49 to 1.52 uu above a BSP floor (the floor band below plus 0.1) and 2.39 to 2.42
above a static mesh floor.

### Lines against the world BSP and static meshes (decoded)

A zero-extent trace does not use the world BSP's triangles: it walks the level's BSP tree (UModel::LineCheck, line
walk editor 0x111925a0). The segment is split at each node plane it crosses (a point within 0.001 of a plane
counts as front when both ends are) and followed front half first; a CSG node (one with a polygon and NodeFlags
without NF_NotCsg 0x01 or NF_IsNew 0x20) makes its front empty and its back solid, other nodes pass the state on.
The hit is where the line enters the first solid leaf, on the last plane crossed; its node is the first of that
plane's coplanar chain whose polygon holds the point (0x11192a30), whose plane gives the normal. Polygon winding
plays no part, so the converted SteeE lane floor near (-492, -2032), where some polygons are wound downward, is hit
like the rest. A line that starts in solid hits at once (ULevel passes no ExtraNodeFlags, so the 0x10 skip never
applies). Brush models still use their triangles (front faces only) for lines.

A zero-extent trace against a static mesh tests both faces of each collision triangle (the line walk 0x111b6c50
has no facing test): the segment must cross the triangle plane by 0.001 at each end, the crossing lie inside the
edges and `0 <= t < 1`; the normal is the triangle's own (outward) normal whichever side the line came from. So a
fence quad (MounE FENCE 225's chain fence, a two-triangle simple collision set) answers CheckFence from both sides.
`CollisionWorld.BspLine.cs`, `StaticMeshSweep.LineTriangle`; tests `BspLineTests`.

### Cylinders (decoded)

A sweep against an actor cylinder widens it by the box: radius R + Ex, half height H + Ez. Entering through a cap
gives the cap normal, through the side the horizontal normal at the entry point. A sweep that starts inside the
radius and between the caps is blocked only when it moves inward (rate below -0.1), at time 0; moving out is free.
Point checks: |dz| < H + Ez and |dxy|^2 < (R + Ex)^2.

### Static mesh sweeps and point checks (decoded)

A box sweep against a static mesh (any collider but world BSP and brushes) moves a vertical cylinder of radius
Extent.X and half height Extent.Z, in world space, on these separating axes: the triangle's bounds on X, Y and Z on
the start's side, the normal both ways, edge x Z, edge x X and Y x edge for each edge that is not vertical (each
flipped away from the third corner), and a horizontal corner-to-start axis when it points outside both edges.
The reach along a unit axis n is H when |n.Z| >= 0.99999, else |n.Z| H + R |n.XY|. A start that already overlaps
is not blocked. So a box's corner passes a mesh corner that the cylinder clears (the fall-ledge bricks).
`StaticMeshSweep`, from editor 0x111b5d80, 0x111b5ae0, 0x111b4bc0.

A point check against a static mesh works in the mesh's frame: the world box becomes its local bounds there, whose
X and Z half sizes are the radius and half height. A triangle is dropped when the point lies outside its bounds
grown by the local box (the original culls by its tree's node boxes; the triangle's own bounds stand in), then it
overlaps when the point is within the reach of its plane and no edge or corner axis separates them
(FCylinderPointCheck 0x111b6520, walk 0x111b6680, test 0x111b4d60).

`OverlapMoveShape` checks the shape the movement collides with (the world cylinder) for tests and the soak; the
mesh point check's local box is wider on a yawed mesh and is not a penetration test.

BSP and brush sweeps are an exact separating-axis sweep of the box against each triangle; a box that starts on or
in a triangle's Minkowski region is blocked only when it moves deeper through the nearest face (estimated).
A box contact of less than `K.BoxContactSkin` (0.001 uu) is no contact: a box hit on a BSP or brush triangle or a
hull counts only when the box shrunk by the skin hits as well (the hit keeps the full box's time and normal, so rest
heights and pull-backs are unchanged), and a box point check overlaps them only with the shrunk box (estimated, like
the sweep itself). Without it a box exactly on a face, as the server's copy of a pawn lands when a big move snaps it
to the whole-unit network location against a wall on the unit grid, was stopped by geometry behind the face and by
its own wall: the walking and falling pre-probes' side boxes (R - 0.2 moved 0.2 sideways) reach exactly the radius,
so the second probe met the wall the pawn stood on and stopped it dead (SteeE, the wall at x = -1664 by the spawn).
Tests: `TouchingContactTests`; with the skin at 0 four of its seven fail. The DiffTest report is unchanged by it.

### Trace flags (decoded)

| Bit | Name | Effect |
|---|---|---|
| 0x1 | TRACE_Pawns | pawns |
| 0x2 | TRACE_Movers | movers |
| 0x4 | TRACE_Level | the world BSP |
| 0x8 | TRACE_Volumes | volumes |
| 0x10 | TRACE_Others | glass, gameplay objects, attached collision, other cylinders |
| 0x20 | TRACE_OnlyProjActor | others only when bProjTarget, or blocking actors and players |
| 0x40 | TRACE_Blocking | only what blocks the source |
| 0x80 | TRACE_LevelGeometry | static meshes, blocking brushes; a pawn source sees a static mesh only with blockPlayers |
| 0x100 | TRACE_ShadowCast | bShadowCast actors; static meshes use the per-poly set |
| 0x200 | TRACE_StopAtFirstHit | stop at the first hit |
| 0x1000 | TRACE_Material | the surface material (always reported) |
| 0x8000 | TRACE_AcceptProjectors | bAcceptsProjectors actors |
| 0x10000 | TRACE_Camera | needs blockCamera |
| 0x0200_0000 | TRACE_Bullet | SVM channel: passes bulletGoesThrough (the original uses a pass-through word on the hit) |

Then, for every candidate: not the source, an owner of the source or the ignored actor; colliding; blocking this
extent (blockZeroExtent / blockNonZeroExtent); bOnlyAffectPawns on the source limits hits to pawns, on the target
excludes non-pawn sources. With TRACE_Blocking: players by blockPlayers, other actors by blockActors (both ways
for two cylinders), brushes and movers only sources with bCollideWorld.

The masks the natives pass are in `K.Flags`: MoveActor `(bCollideActors ? (bIgnorePawns ? 0x18 : 0x19) : 0) |
(bCollideWorld ? 0x86 : 0)` (plus the blocking pick), wall pre-probes 0xd6, floor probes 0xdf, clearance 0x2df,
pawns-and-movers clearance 0x243 (no BSP, no static meshes), landing 0x297, side probes 0x97, drop-attack sight
0x29f, legs-over probe 0x201, CheckFence 0x1086, CheckGEForWallJump 0x10df, barrier 0x287, encroachment 0x9f.

## MoveActor, FarMoveActor, encroachment (decoded)

- **MoveActor(actor, Delta, NewRotation, out Hit, bTest, bIgnorePawns, bIgnoreBases, bNoFail).** A zero move only
  turns the actor; an actor colliding with nothing just moves. Otherwise the cylinder box sweeps with the MoveActor
  mask to the first blocking hit that is not a base relation (an actor based on the mover is always skipped, the
  mover's own base with bIgnoreBases) and the move is cut there (unless bNoFail). Non-pawn attached actors move
  along; based pawns keep their own physics. Unless bTest: the other actor is bumped first, then the mover (not for
  world geometry or base relations), and touches are updated: a touch begins for an actor overlapped at the end or
  whose cylinder the move entered (the cylinder test of the sweeps, not a box round it), then ends for every touched
  actor no longer overlapped (`StuckTouchTests`). Returns Hit.Time > 0.
- **Encroachers** (actors with mesh or brush colliders, the static-mesh movers): the new pose is checked with
  CheckEncroachment first and an encroached pose is refused without moving.
- **CheckEncroachment(actor, Location, Rotation, bTouchNotify)** (true = refused): every colliding cylinder actor
  the actor would overlap and that it blocks is shoved by the displacement plus 1.5 times the offset the rotation
  gives its position (only an encroacher with bTryToMoveEncroachedActor; the shove is a moveSmooth); a shove that
  leaves it overlapping is undone. What still overlaps goes to EncroachingOn (`IEncroacher`, or Mover's state
  dispatch; Actor answers false) and true refuses. An accepted check ends stale touches and sends EncroachedBy to
  every overlapped blocker. Crushing is script (the mover's encroach type, the pawn's EncroachedBy).
- **FarMoveActor(actor, Dest, bTest, bNoCheck, bAttachedMove).** Unless bNoCheck, FindSpot may nudge the
  destination (an actor colliding with the world, or bCollideWhenPlacing off clients) and failure refuses; unless
  bTest the destination is checked for encroachment. A good move sets bJustTeleported (not for a test), leaves the
  base unless bAttachedMove and moves attached actors. The location is set even for a test; the caller restores it.

Also: `TraceMove` (the sweep without moving), `SetLocation` (script teleport, refused when the box would overlap
something blocking), `FindSpot` (up first, then out in eight directions, 4 uu steps up to 40), `SetCollision` and
`SetCollisionSize` (update touches; turning collision off ends them), `MovePawn`, `CanUnCrouch`.

## Physics dispatch (decoded)

`performPhysics` (the Level's `IActorPhysics`): for pawns OldZ, the mode switch, bSimulateGravity = walking or
falling (after the move), Controller.MoveTimer -= dt, OldAcceleration and AvgPhysicsTime = 0.2 dt + 0.8 old. Then
the KillZ fallback (FellOutOfWorld once when LevelInfo.fDeathPawnHeight is 0 and the pawn passes below the world).

`startNewPhysics(actor, dt, Iterations)` returns below 0.0003 s or above 7 iterations. Pawns: Walking and Falling
get the iteration count; RootMotion, OnPipe, Linear, OnVerticalPlane, Ledge, Vandamme, Repositionnal, OnLadder and
OnPipeH start at zero and do one move per call. FollowParent and Flying are not decoded and do nothing. Non-pawn
actors keep the stock modes: Falling, Projectile, MovingBrush.

`setPhysics`: the same mode changes nothing (RootMotion again still sets bActionsSendByServerMove); leaving
Repositionnal on the authority runs EndPhysRepositionning; entering Falling sets fZMaxFalling = Location.Z;
leaving RootMotion unlocks the root; Walking, None and Rotating take NewFloor as the base (or FindBase), other
modes leave it; None and Rotating zero Velocity and Acceleration; then bActionsSendByServerMove = true and
bKeepLocalVandammePos = false.

## Walking (decoded)

`physWalking` (ASBasePawn): no Controller, no walking (proxies use `physWalkingSimulated` from root motion). The
mover time and the authority's drop attack on a pawn base come first (StickyBlast for a sticky-skinned base,
UnCrouch(true) before attacking from a crouch). Then:

- **MycalcVelocity**: friction 8 (hard-coded); with no Acceleration GroundSpeed becomes 0 and the speed cap stops
  the pawn at once (the braking decay, 2 V f in 0.03 s steps with a stop below 10 uu/s, matters only for callers
  that keep GroundSpeed); with input the velocity turns toward it keeping its speed, then `V += Acceleration dt`
  (AccelRate is not used) and the cap is GroundSpeed (times DesiredSpeed when not human controlled). From rest
  with input 3000 the speed grows 50 uu/s per tick to GroundSpeed.
- **Wall pre-probe**: two thin boxes (R - 0.2) 0.2 uu either side of the move, lifted 18 and spanning from 36 above
  the feet to the head (flags 0xd6, no pawns); a wall within the next move (+0.2) bumps, raises processHitWall and
  slides both the velocity and the move along it; a second wall stops the move. Steps below 36 are left to stepUp.
- **Sub-steps** of 0.05 s (a longer remainder is halved), at most 8 iterations: the ledge stop (only with an
  attack target), the move (moves below 0.009 squared are carried), stepUp on a hit with the 1.52 uu floor check
  (undo to the frame start and slide when it finds no walkable floor), a fall started by an event.
- **stepUp**: a near-vertical (|N.Z| < 0.08) or walkable hit lifts 34 and moves on; another near-vertical hit
  with Time |Delta|^2 > 144 takes a second, shortened step; else processHitWall and the slide along the wall and
  its crease (TwoWallAdjust); the pawn always ends moved back down 34. With the floor gap a 35 step is climbed and a
  36 step is not (measured: 32 climbs, 36 does not).
- **Floor probe**: box down 36 (0xdf). A walkable floor (N.Z >= 0.7) or a surface the move does not go into: drop
  to it when it is a new base or more than 1.42 below, lift to 1.405 when less than 1.39. A steep surface the move
  goes into: slide down along it. A walkable floor ends the step.
- **Floor lost**: MayFall once; a pawn that cannot jump (or cannot walk off ledges while walking or crouched) is
  put back at the frame start, stopped, MoveTimer -1; a floor within 32 is re-tested with the narrow box (R/2,
  0x2df) and walking goes on when it hits; else the velocity is rewritten from the move and the controlling side's
  controller gets MyFalling(0) (the script plays the fall).
- **Velocity** is the move made over the frame (plus the carry), Z zero.

`processHitWall`: walls only (no actor, a pawn: nothing). Without a controller or with bDirectHitWall: HitWall.
Repositioning asks NotifyHitWall first. Otherwise only with input: walking compares horizontal directions (the
caller keeps the horizontal normal), and a hit with velocity . normal at most Controller.MinHitWall (-0.98 for
SPlayerController, -1 for Controller) that NotifyHitWall does not take moves the pawn down a step (unless
falling) and raises HitWall. `CheckForLedges` (ledge stop): a front probe, a floor probe below the front point with
the slope allowance, a narrow box floor test, then a sidestep either way, else stop.

## Falling (decoded)

`physFalling` (ASBasePawn): FellOutOfWorld below LevelInfo.fDeathPawnHeight (controlling side). Air control clamps
Acceleration.XY to AirControl x AccelRate (4096; a boost below 10 uu/s; none under AirControl 0.05). The wall
pre-probe (unlifted, H - 10 high) slides off the first wall and stops on a second. The authority's drop-attack scan
(last actor to first; other team; controller lets it): homing picks the target best lined up with the fall
(cosine 0.92, 180 below, horizontal speed over 40, line of sight to 100 above it), the trigger (when the fall
would not kill: within 2.1 H, in front by 0.7, below 0.5 H above, -100 overlap, Velocity.Z below -800, in sight)
raises StickyBlast or DropAttack. Sub-steps (0.05 s, 8 iterations):

- `V += 0.5 (Acceleration + (0, 0, -1500)) tick` (fixed gravity; zone gravity is not read), the homing pull
  (0.5 V + 0.5 k P, P clamped to 200, k = min(20, 0.04 (|target V| + 100))), the 417 uu/s horizontal cap, the
  move (XY below 0.009 squared carried).
- A wall: processHitWall, slide, TwoWallAdjust with a remembered wall, land on a floor met by the slide or in a
  V-shaped trough (processLanded, remaining 0).
- A floor (N.Z >= 0.7): velocity rewritten from the move, GroundSpeed = the horizontal speed, then **bCanLand**: a
  box 0.9 (R, R, H) 0.2 H + 8 below (0x297) must hit; the controlling side's controller gets
  MyNotifyLanded(normal, remaining, actor), a walking pawn's Acceleration becomes unit length and the rest of the
  frame runs (unless RootMotion). The remaining time is stored before the bCanLand test, so a floor hit that does
  not land carries on with it. On the server for a remote player nothing is called and the pawn stays falling
  on the floor until the client's landing action arrives. When the probe misses, the narrow-top side probes push
  the pawn off a steep edge or land it (processLanded).
- After a step: `Velocity = displacement / tick`, `Velocity.Z = 2 Velocity.Z - Vz0` (exact for constant gravity:
  JumpZ 610 peaks at 124.0, 495 at 81.7), the 2500 uu/s cap.
- At the end fZMaxFalling is raised to Location.Z and Acceleration restored.

`processLanded` (wall-slide landings): FindSpot with 1.1 times the cylinder when the probe misses, else Floor,
MyNotifyLanded (no role test) and the rest of the frame.

**A pawn inside a wall.** Walking and falling assume the pawn starts clear of the world. A box that starts across a
wall's triangles is held by the triangulation's seams (the start-inside rule below meets an edge axis of a face's
triangle with the move going into it): the walking floor probe hits at time 0 and lifts the pawn 1.405 uu a frame
until the floor is lost, and then every falling move is blocked at time 0, so the pawn hangs in `PHYS_Falling`.
The stalled bot matches reached this only through a client correction's unchecked teleport into a wall (now the
decoded checked `FarMoveActor` of MovePawn, docs/NETCODE.md "Stalled-frame bot matches"; tests
`StuckInWallTests`). The original's BSP box check clips the box against the solid leaf hulls (FBoxLineCheck::ClipTo,
editor 0x111922b0, the leaf test 0x11193c60): a plane the move goes into gives the entry `(D0 - PushOut) / (D0 -
D1)`, taken as 0 while the box centre is still in front of the plane, and the leaf blocks when the latest entry is
above -1; a face's own plane is a leaf plane, its triangulation's edges are not. Porting that needs the leaf hulls
(open question 2); a per-triangle version (only the face plane) frees such a pawn but also holds a probe box that
starts across a face and moves into it, which the bot nav builder's floor sampling relied on not happening.

## Root motion, repositioning, Vandamme (decoded)

- `physRootMotion`: once per game time step (0.0001 s guard) the mesh's root delta moves the pawn with collision
  off, or through the additional physics (None: a vertical then a horizontal collision move; Walking: physWalking
  with the delta as a velocity, physWalkingSimulated without a controller; Falling: physFallingInRootMotion; OnPipe
  and the GE modes). Lock bit 2 adds the root rotation. Velocity is the displacement over the elapsed game time
  (not while bJustTeleported, which is true on a fresh actor until walking or falling clears it).
- `physFallingInRootMotion`: the drop-attack trigger with the root-motion bounds, the two moves, NotifyHitWall
  (N.Z up to 0.707) or MyNotifyLanded, the apex.
- `EndRootMotion`: vSpeedAfterRM, the final yaw, and the placement at vLocationBeforeRM + vMoveAfterRM (or back
  with bReverseRM) without collision. `bServerTestRootMotion` refuses the pull-up (113) without room to stand
  (ClientStopRM).
- `physRepositionning`: a straight line to vVandammePos at fPhysReposLinearSpeed (87; Z ignored within 5 or with
  bUseAdditionnalPhysicsForRepos), turning to vWallJumpDir by 3000 per call; through the world, or walking with
  collision; TopSynchro within 1 uu with the yaw reached. `EndPhysRepositionning` snaps to the target on the
  authority unless bNoReposAtEnd.
- `physVandamme`: to vVandammePos at 87 uu/s facing the forward axis with its vWallJumpDir part removed (3000 per
  call); Velocity = displacement x dt as in the original.

## Traversal modes (decoded)

Each holds the pawn on its GE (ZipP1, ZipP2, m_geoNormal) with a 500 uu/s snap, turns it, moves it by the
script's Acceleration convention and raises the chain events (spec/native/physics-traversal.md). Velocity is the
frame's displacement over dt, measured from after the hold move in OnPipe, OnPipeH, OnLadder and Linear and from
before it in Ledge and OnVerticalPlane; Linear faces ZipP1 - ZipP2 flattened and keeps pitch and roll. physOnPipe
does not touch bCanStop: the pawn's climb step (UpdateMovementAnimation, editor 0x11200140) sets it on the climb
clip's step frames (spec/native/ANSWERS-fidelity.md).

| Mode | Input | Rules |
|---|---|---|
| OnPipe (6) | Acceleration.Z climbs (Y sideways); \|X\| > 10 slides with Velocity.Z in [-450, 0] | hold through the world, yaw 16384 per second; bottom TopSynchro; HOH and zip junctions; top: TopPipe, TopPipeCrouch (TestTopPipe), else Ledge |
| OnPipeH (14) | Acceleration.X along the bar | hold (TopSynchro, NoGE), end stops 27 (95 legs over, input below 120), zip join within 3, legs-over pawn probe 140 / 70, OrthoPipeH, Pipe |
| OnLadder (13) | Z climbs; \|Y\| > 10 stops on the nearest 64 uu rung at 100 uu/s; \|X\| > 10 slides (-200 / -700, slowing -64 with TopSynchro per rung) | horizontal hold facing the ladder (1000 per call); TopLadder |
| Linear (7, zip) | Acceleration = v m_geoNormal, GroundSpeed | hold (drop attack on a pawn, NoGE), no uphill motion, ease 0.03 (GroundSpeed - s) above GroundSpeed, MycalcVelocity below, NotifyHitWall at the end |
| OnVerticalPlane (8, fence) | Acceleration | moves only while CheckFence at the target is 1, held R + 2 off the face; FenceTop stops the climb; MyNotifyLanded on a floor |
| Ledge (9) | 80 straf along the right axis | hold by TypeLedge (NORMAL 39 out and 117 down, WAIST 20 down, BARRIER 10, FEET 50.5 up), TopSynchro, NoGE; shimmy with the 36 stop and the 50 uu head room; NoGE off the ledge; corners (ChangeLedge 0 to 3, outside corners need 75 + 75), merges, ledge to pipe (TopPipe) |

`SetZipSpeed` / `UnZipSpeed` / `CanUnZipSpeed`: the (CrouchRadius, ZipHeight 45) cylinder keeping the top
(Location up by H - 45, PrePivot down by it, ZoneConfig 2), and back (a FarMoveActor test for CanUnZipSpeed).

## GE natives (decoded)

- **Elements**: Distance to the segment P1-P2; ClosestPoint directions (LG, FENCE, HOH horizontal P1 - P2;
  LADDER, PIPE toward the climber P3 - P1; ZL downhill; POLE up); Connected within 2 uu per axis (never for
  three-point kinds); Equals within 0.1.
- **Zone points**: arms `Location + Rotation (offset + PrePivot - Default.PrePivot)`, feet the same with the yaw,
  back feet mirrored in X; the standing-equivalent height is `Location.Z - ZCDecal[ZoneConfig * 4]`.
- **FindArmsGE**: the nearest GE closer than m_CurrentArmsRadius (on a pipe or a bar the nearest of that kind
  first); the second: PIPE best with HOH, ZL or LG (last wins), HOH best with ZL or PIPE (first), LG best with PIPE
  (first), or the same kind connected (last wins). Candidates come in GE list order (ties only can differ from
  the stored k-d tree order). Inactive GEs (ID not 0 and not in GEActivationMask) never take part.
- **CheckArmsZone**: ZipP from the best GE, then the kind's test: LG (AutoFeetLedge while walking; else
  WaistLedge with standing then crouched room, then Ledge: 24.5 end margin, Velocity.Z above -1270, the -5 / +35
  window, distance^2 830, the two (35, 35, 87.5) 0x2df clearance traces to the hang spot and 50 above it), HOH,
  LADDER (NarrowLadder from either side), PIPE (Pipe with the hold spot and facing), ZL (ZipLine). FENCE and POLE
  raise nothing here.
- **CheckFeetZone**: LG FeetLedge, LADDER TopLadder (from the top, vVandammePos), PIPE TopPipe (from the top).
  **CheckBackFeetZone**: an LG behind the feet fills ZipP and vBackFeetNormal.
- **TestTopLedge** 1 stand (up 210.8, forward 57.8), 2 crouch (206.1, 75.7), 3 vault over the top, 4 barrier above
  the ledge, 0 none. **bTestTopLadder** (spy 192 / 73.9, merc 196 / 68.8, iCanUncrouch). **CheckFence** 0 / 1 / 2
  from two opposite and four corner probes on one plane and the material's climbable flag. **CheckBarrier**,
  **CheckWallJump**, **CheckGEForWallJump** as in the spec, with the estimates below.

## Constants

`K` holds every number with its evidence: the top level (MAXSTEPHEIGHT 34, MINFLOORZ 0.7, gravity -1500,
friction 8, terminal 2500, sub-step 0.05 and 8 iterations, braking 2, step wall 0.08, the ledge clearance
values, the measured GE team filters) and one nested class per native: `K.Flags` (trace masks), `K.Pullback`,
`K.Dispatch`, `K.Walk`, `K.Fall`, `K.RootMotion`, `K.Traversal`, `K.GE`. A nested class's `[Provenance]` covers its
constants unless one carries its own.

## Still estimated

| Rule | Why |
|---|---|
| pawn physicsRotation, PendingTouch.PostTouch in performPhysics | not decoded (the script sets Rotation itself) |
| actor physicsRotation render gate | ported (`PhysicsDriver.Rotation.cs`, game 0x10a2b130 / 0x10a2bb20 / fixedTurn 0x10a2b210): a non-pawn with a RotationRate turns unless bInterpolating; the original skips a PHYS_Rotating actor not rendered in the last second, which the sim has no time for, so it always turns |
| FollowParent (12), Flying (16) | not decoded |
| FindBase (8 uu down), FindSpot search, moveSmooth | not decoded (UE2 lineage) |
| TwoWallAdjust | assumed standard UE2 (walking open question 2) |
| AActor::IsBlockedBy and IsOverlapping details, the MoveActor touch list | used but not decoded |
| movers' and volumes' ShouldTrace (TRACE_Movers / TRACE_Volumes) | only AActor and APawn ShouldTrace are decoded |
| BSP triangle start-inside rule, BSP box sweep at edges | the non-zero-extent BSP walk is only outlined (static meshes are decoded); lines use the decoded tree walk; the leaf clip is decoded (A pawn inside a wall) but needs the leaf hulls |
| `K.BoxContactSkin` (a box contact under 0.001 uu is none) | the non-zero-extent BSP walk is not decoded; the original's tolerance for a box exactly on a face is unknown |
| brush models' zero-extent lines | front-face triangles; the native walks each brush's own BSP (RootOutside 1) |
| the level model's RootOutside | svmap does not carry it; 0 assumed, and on SteeE 0 and 1 give the same hits |
| the static mesh point check's node boxes | the original's quantized tree is not converted; the triangle's own bounds stand in |
| tie order of GE candidates | list order instead of the k-d tree order |
| the root-motion lock bit 2 as the value 2; root motion switches collision by the flags | wording of the spec |
| StickyBlast(Base) while falling; a DropAttack ends the falling scan | as the spec reads; not stated |
| pipe swap orientation, pipe junction dz reference, ledge-to-pipe windows, OrthoPipeH side | traversal open questions |
| CheckPipeFeet segments, TestTopLedge result 4 boxes, CheckBarrier height reference and start, CheckWallJump helpers, CheckGEForWallJump room boxes | ge-queries open questions 2 to 4 |
| CheckLadderFeet flags of the 87.5 segments | not given |
| rung dead band (speed below 2), CheckForLedges sidestep order | wording of the spec |
| MovePawn, CanUnCrouch, SetLocation | contracts inferred from script use (MyDoJump is decoded: spec/native/ANSWERS-fidelity.md F2) |
| non-pawn falling and projectile, physMovingBrush | stock actor physics (the mover spec is estimated) |
| KillZ fallback, the encroach push margin K.TraceSkin | no SCCT counterpart |
| static mesh set pairing for movers, glass, gameplay objects | needs CSRC in the C# reader |

## Spec notes

- physics-walking.md section 3 gives the walking pre-probe extent as H + 18; the decompiled sign (editor
  0x112025e0) is H + Down.Z / 2 = H - 18 with the lift of 18, so the box spans from 36 above the feet to the head
  and steps are left to stepUp. The port uses H - 18 (with H + 18 no step could be climbed).
- The floor band leaves the pawn's height above a floor between two values (see Pull-backs); goldens of the
  original should show 1.49 to 1.52 uu above BSP floors.

## Performance

Measured on the development machine (Release):

- 1,000 pawn box sweeps on a synthetic 135,000 triangle, 400 hull map: about 16 ms (target 20 ms).
- SteeE (`.content/svmap/SteeE`, local only): 122,241 triangles; 1,000 pawn sweeps in about 2 ms; 8 pawns walking
  at random from the PlayerStarts for 20 s stay out of geometry (checked with `OverlapMoveShape`, the cylinder the
  moves collide with against meshes).
- Steady-state ticks allocate nothing (tested with pawns, touches, bumps and a lift).

## Open questions

1. Which bits SCCT's own fire, camera and vision traces set (combat and camera specs); the bullet pass-through.
2. The original LeafHulls of the BSP (SVMap BSPN is provisional): world BSP collides by triangles.
3. A trace of the floor height and the walking ramp in the running game to confirm the band and the pre-probe.
4. The spec open questions listed under "Still estimated".
