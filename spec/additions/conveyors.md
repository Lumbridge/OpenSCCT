# Conveyors

OpenSCCT addition. `Svm.Conveyor` is a map actor for belts, travelators and escalators. It carries walking pawns and
dropped objects along a path, and map events start, stop and reverse it. It replaces RE+'s `ConveyorBelt`,
`ConveyorMotor`, `ConveyorRoller` and `ConveyorItem` classes and the AeroD `AerTreadScroll` script with one actor
that the movement physics knows. The SVMap data is in [SVMap 1.2: conveyors](../svmap/1.2/conveyors.md).

Code: `src/OpenScct.Physics/Conveyors` (path maths, the level's conveyor field),
`src/OpenScct.Physics/Movement/PhysicsDriver.Conveyor.cs` (the carry), `src/OpenScct.Game/Svm` (the actor, its
metadata, the shared look values, the moving steps `ConveyorSteps` and the item flow `ConveyorItemFlow`),
`src/OpenScct.Game/AI/NavConveyors.cs` (bots), `game/Scripts/Views/ConveyorViews.cs` (drawing),
`game/Scripts/Audio/AudioWorld.cs` (the hum).

## Properties

| Property | Default | Meaning |
|---|---|---|
| `Path` | empty | Points on the riding surface, relative to the actor's location and rotation: `{Loc, Radius}`. `Radius` 0 is a sharp corner, more rounds the corner with an arc of that radius. |
| `bLoop` | false | The path closes from its last point back to its first. |
| `Width` | 120 | Width of the carried strip, centred on the path. |
| `Speed` | 150 | uu a second along the path. Negative runs backwards. |
| `bOn` | true | Running. |
| `bCarryPlayers` | true | Carries walking pawns (players and bots). |
| `bCarryObjects` | true | Carries dropped disks and data drives (server). |
| `Look` | `CL_Frame` | `CL_Frame` (belt, side walls, end drums), `CL_Driver` (nothing built; the map's own meshes are the look and the floor), `CL_Treadmill` (flat belt, low edges), `CL_Escalator` (steps, balustrades). |
| `Walls` | `CW_Auto` | Which sides have a wall: `CW_Auto` (both, open where another conveyor's end meets this one's side), `CW_Both`, `CW_Left`, `CW_Right`, `CW_None`. |
| `TriggerAction` | `CT_Toggle` | What an event does: `CT_Toggle`, `CT_TurnOn`, `CT_TurnOff`, `CT_Reverse`. |
| `ItemMeshes` | empty | Static meshes riding the belt; each item picks one from the seed. |
| `ItemSpacing`, `ItemScale`, `ItemFade`, `ItemSeed`, `ItemYawJitter` | 160, 1, 48, 0, 65536 | Item spacing along the path, size, fade length at the ends of an open path, seed and largest random turn (rotator units). |
| `Next` | none | The conveyor items go on to from the end they leave by ([items](#items)). |
| `AmbientSound`, `SoundRadius`, `SoundVolume` | `Amb_Krauser.Ambiances.Krau_machine1`, 32, 96 | The hum while the belt runs ([sound](#sound)); `None` silences it. A driver does not play the default. |
| `ScrollTargets` | empty | `{Target, UnitsPerTile, Axis, Material}`: a placed mesh actor (by id) whose texture moves with the belt. `UnitsPerTile` is the uu one texture repeat covers along the direction of travel (negative where the texture's axis runs against it), `Axis` 0 for U and 1 for V, and `Material`, when set, the only material of the mesh that moves. |
| `BeltMaterial`, `FrameMaterial` | none | Materials the map builder uses for the generated look. |

`PhaseAtChange`, `ChangeTick` and `SpeedBeforeChange` are runtime state (below); maps leave them at 0.

## Path

The path is built in world space when the conveyor begins play:

1. Each stored point is rotated by the actor's rotation and added to its location. Points closer than 0.1 uu (flat)
   to the previous one are dropped. A loop drops a last point equal to the first.
2. Straight runs join the points. A rounded corner (radius above 0, not the end of an open path) replaces the corner
   with an arc tangent to both runs, fitted in the horizontal plane. The arc may use at most half of each run, so a
   too-large radius shrinks.
3. Height changes linearly along each run and arc. The path's length, and every distance along it, is measured on the
   surface (with the climb).

A point projects onto the path in the horizontal plane: the nearest point of each run or arc, clamped to its ends,
except that an open path ends square at its first and last points. The projection gives the distance along the path,
the side distance, the surface height and the direction of travel (the run's or arc's tangent, tilted by its climb).

- Only runs and arcs whose surface is in the rider's height band (below) count, so a path that passes over itself, or
  a conveyor on another floor, is not mistaken for the one underfoot.
- Distances within 0.01 uu are a tie. On a tie, a piece the point lies beyond (clamped to its end) loses to one it
  does not. Past a sharp corner, on its outside, both runs clamp to the corner point, so the run that starts there
  wins and the body turns the corner instead of being pushed into the outer wall.

## Who rides

A body rides the conveyor when all of these hold (`ConveyorField.FindCarrier`):

- the conveyor is running (`bOn` and `Speed` not 0) and carries that kind of body;
- the body's feet (location minus collision height) project onto the path within `Width / 2 + radius / 2`
  (RE+'s overhang), counting only places where the feet are no more than 8 uu below the surface and no more than
  `12 + radius x slope` above it; `slope` is the climb per flat uu at that point (a cylinder on a slope rests on its
  edge).

Where conveyors overlap, the one whose centre line is nearest carries the body; on a tie, the one that began play
first. Joints between conveyors therefore hand riders on without a link between them.

Unlike RE+'s box test, a pawn that is jumping, falling, hanging or traversing never rides, and a pawn standing on a
crate above the belt is out of the height band.

## Carrying pawns

At the end of `physWalking`, when the pawn is still `PHYS_Walking` and has walked its time, a carrying conveyor moves
it by `velocity x deltaTime`, where `velocity` is the direction of travel times the speed:

1. `MoveActor` up by `CarryStep` (18 uu, RE+'s value);
2. `MoveActor` along the carry; if it hits something, one slide along the wall's horizontal plane for the rest;
3. `MoveActor` down by the height actually gained.

The carry is not added to `Velocity` (a mover's carry is not either), so walking speed, animation and friction are
the pawn's own. The pawn never turns. The walking physics runs on the server and in the owning client's prediction
and replay, so both move a rider the same way. A level with no conveyor has no conveyor field and skips the step, so
stock maps behave exactly as before.

## Carrying objects

Every server tick, the conveyor that began play first walks the level's actors once and moves each dropped `SFlag`
or `SDisk` that a running conveyor with `bCarryObjects` carries (same rules as above, nearest centre line wins). An
object counts as dropped when it is visible, `PHYS_None`, colliding with the world, not based on a pawn and away from
home: its X or Y differs from its `InitialLocation`, the test `SDisk.Touch` uses. A disk still where the map placed it
is never carried. The move is the same up, along, down step. When no floor is within 8 uu below the object afterwards,
it starts falling (`PHYS_Falling`) and lands by its own rules. These objects replicate their location, so clients see
them move.

## Events

`Trigger` applies `TriggerAction`. Every change of `bOn` or `Speed` first stores the distance the belt has travelled,
`PhaseAtChange`, the server tick, `ChangeTick`, and the surface speed until then, `SpeedBeforeChange`. With
`v = bOn ? Speed : 0` for `t >= ChangeTick` and `SpeedBeforeChange` before it, the phase at server tick `t` is
`PhaseAtChange + v x (t - ChangeTick) x Sim.DeltaTime`: continuous across the last change, also for a client that
draws a little in the past. A new round travels to the map again, so every round starts as the map placed it.

## Network

The actor is a `bNoDelete`, always relevant map actor replicated to clients as a simulated proxy. `bOn`, `Speed`,
`PhaseAtChange`, `ChangeTick` and `SpeedBeforeChange` replicate when they change (a runtime class extension of the
Actor layout, so the protocol hash changes).

Client prediction: each recorded movement tick's collision inputs include every conveyor's `bOn` and `Speed` as the
client saw them. A correction carries the server tick it was sent at (the acknowledgement's tick field). Replay
estimates the server tick that applies each replayed move as that tick plus the move's local ticks after the
acknowledged one, and stages, per conveyor:

- the replicated (live) state for a move applied at or after the conveyor's `ChangeTick`: the server used the new
  state for it, even if the client predicted it before it heard of the change;
- the recorded state for an earlier move.

A correction also carries the server's revision at the location it sends: the sum of the `ChangeTick` of every
conveyor within 512 uu of that location (`ConveyorK.RevisionReach`: past its strip's edges, its ends, above or below
its surface), which grows with each change of those conveyors. The client works out the same sum at the same
location. When its sum is lower, the property update with a change near the pawn has not arrived (it was lost and is
being resent, or is still on its way), and replaying would predict the old way again. The client then takes the
correction as a plain acknowledgement and waits; a correction taken earlier in the same receive still applies. It
waits at most 32 corrections in a row (`MoveK.MaxDeferredCorrections`; any plain acknowledgement or prediction reset
starts the count again), so a change that never arrives cannot stop corrections. Changes of conveyors away from the
pawn do not hold back its corrections (a hit, a knockback).

The burst this replaces was traced on profile D: a reversal at server tick 479 reached the client at server tick
505, about one round trip after its packet was lost and resent, and every acknowledgement in between carried a
correction that replaying with the old belt state could not satisfy (9 on that reversal).

So a start, stop or reversal costs one correction, the first acknowledgement after the client has the change, rather
than one per acknowledgement until the moves predicted with the old state age out. Checked with a host and a client
over the simulated links, one scripted run per profile: one correction per change on profiles A and D (D: 250 ms and
5% loss, where a lost property update used to cost 9 corrections on one reversal), including an escalator stopped and
started again under a rider.

`SetRunning` called twice in one server tick keeps the phase and the speed from before the first call.

## Look

The map builder (the OpenSCCT editor) builds each look into `render/world.glb` and its collision into
`sim/collision.bin`, lit like the rest of the map. The moving parts are separate gameplay instances named after the
conveyor's id:

| Instance | Look | Texture |
|---|---|---|
| `<id>.Belt` | frame, treadmill | V runs along the path, one repeat every 128 uu |
| `<id>.Steps` | escalator | V runs along the path, one repeat per 40 uu step |
| `<id>.Rollers` | frame | the half drums (16 uu radius) at an open path's ends; U runs once round a drum |

The rest is the static `<id>.Frame`: a body 32 uu deep under the riding surface, and on each side `Walls` names a
wall 8 uu thick, 24 uu high on a frame, 4 uu (edges) on a treadmill and 96 uu (balustrades) on an escalator. Left
and right are as seen facing the direction of travel (right is +Y when travelling along +X). An open path's ends are
closed by the drums on a frame, else by a flat end. `ConveyorLayout` holds these values for the builder and the game.

- The belt and the body follow the path: straight runs, arcs cut every 15 degrees, at most 64 uu between
  cross-sections (lighting is per vertex), widened at a sharp corner so both runs keep their width.
- Escalator steps are 40 uu along the path each ([moving steps](#moving-steps)). A step's tread is level at the
  higher of its two ends and its riser stands at the lower end, so the slope runs through the steps' inner corners.
  The map builder bakes the steps at phase 0. More than 20 uu rise per step (over 30 degrees) is a map check warning.
- `CW_Auto` walls open where another conveyor's open end meets this one's side: over the joining conveyor's width
  (lengthened for a slanted join) plus the wall's thickness either side, with the cut wall ends closed.
- Textures: the belt and steps use `BeltMaterial` (U across, one repeat per 128 uu), the drums too, and the frame
  `FrameMaterial`; with none, the editor's default material. On both drums U runs the way the belt moves round them
  (over the top and down at the far end, up from the bottom at the start), so one scroll turns both with the belt.

Clients slide the belt's and drums' textures by the phase at the displayed server time, including every
`ScrollTargets` mesh (only its surfaces drawn with the target's `Material`, when it names one). Each node gets its own
copies of its materials and of their later passes (the vision passes),
and every texture layer (diffuse, opacity, specular, self-illumination and sequences) slides. The editor's textured
views show the same motion while its conveyor tool runs the belts.

## Moving steps

An escalator's steps travel with the belt (`ConveyorSteps`). At phase `p` the step boundaries are at
`(p mod 40) + 40k` along the path; each step between two boundaries is built as above (tread level at its higher
end, riser at its lower end, V running 0 to 1 over the step so its texture travels with it). Past the path's ends a
step is cut off and held level at the landing. Clients rebuild the `<id>.Steps` node's mesh every frame from the
phase at the displayed server time. The baked light stays put: each new vertex takes the light the baked steps had
at the same place along the path, on the same side and facing the same way (tread or riser): the mean of the baked
vertices in each 8 uu bin, blended between the centres of the two bins either side. Its alpha keeps the zone the
baked vertices there had (the most common one in the bin, never blended), and the steps' Echelon code is copied, so
zone-limited, dynamic and spot lights, flares and the vision ambient light the moving steps as they lit the baked
ones. Steps the map builder left actor-lit (no baked colours) stay actor-lit.

Pawns collide with the slope through the steps' inner corners, which lies under the steps wherever they are, so the
physics and the prediction do not depend on the phase. A walking pawn's mesh is drawn with its feet on the tread
under it: `TreadAt(along, phase) - feet height`, eased at 20 per second so the feet ride each step up and settle at
the next. The drawn location only; the pawn's location, collision and camera are unchanged.

## Items

`ConveyorItemFlow` runs the items of every conveyor in a level, one server tick at a time, from the conveyors'
phases only. Every machine (and the editor's preview) that sees the same belt history gets the same items.

- **Late changes.** The flow saves its state every 30 ticks and keeps the last 8 (4 s). When a belt's replicated
  state changes and its `ChangeTick` is before the tick the flow has reached (the update came late, or was lost and
  resent), the flow goes back to the newest saved state at or before that tick and steps again, so it ends where a
  machine that had the change in time is. A change older than the saved states goes back to the oldest one.
- **Joining late.** A flow first drawn more than 20 s (`WarmUpTicks`) into a round starts 20 s before then, with the
  items spread as at tick 0, instead of stepping the whole round in one frame. Queues settle well within that, but
  where items queued or were handed on before it, they can differ from what machines that ran the whole round show.

- **Start.** A conveyor with `ItemMeshes` and `ItemSpacing` above 0 starts with `n = max(1, floor(L / ItemSpacing))`
  items: item `i` at `i x L / n + jitter(i)` along the path (wrapped on a loop, clamped on an open path), `jitter` a
  seeded quarter of the spacing either way. Items closer than one item length to the one before are dropped.
- **Length.** Each item takes up its meshes' widest flat extent times `ItemScale` along the belt (32 uu when no
  mesh is known; the editor's preview measures the same meshes once they have loaded). Two items' centres stay at least the mean of their lengths apart.
- **Moving.** Each tick a conveyor's items move by its phase change, front first; an item stops behind the one ahead
  (on a loop the front item is held by the rearmost, a lap ahead). An item is never pushed back. So a queue builds
  behind a stopped belt or a busy join.
- **Handing on.** An open conveyor whose `Next` names another conveyor hands its items on where the end they leave
  by meets that one: its end running forwards, its start running backwards. An end meets the other conveyor when it
  projects onto the other's path within its half width plus 32 uu, and within 32 uu above or below its surface; an
  end that does not meet it removes its items as with no `Next`. An item that runs off the end goes on there when there is room (no item closer than the gap) and the other
  conveyor would not carry it straight back off its own end; otherwise it waits at the end. When the end meets the
  other's start (within 16 uu), the two run as one line: run backwards, the other hands the items back (unless its
  own `Next` takes its start's items elsewhere). An end with nothing to hand on to removes the item.
- **New items.** An open end that nothing feeds (no other conveyor hands items on within 16 uu of it) brings in a
  new item each time the belt has moved `ItemSpacing` plus
  the next item's jitter (at least one item length), unless the queue reaches the end.
- **Drawing.** An item faces the direction of travel and tilts with the belt's climb, turns about the belt's up by a
  seeded amount, and is raised so its mesh's lowest point rests on the belt (RE+ kept per-mesh `ItemOffsets` for
  this). Its mesh, turn, size and seed come from the conveyor it started on. It scales from 0 to `ItemScale` over
  `ItemFade` near an end where items appear or vanish the way the belt runs; ends that hand items on or are handed
  them do not shrink them. Clients draw each item between where it was a tick before and where it is now, one node
  each, lit as actors (they take the actor light slots like any moving prop).

Items are drawn only: they collide with each other on the belt but not with pawns, objects or soft bodies, unlike
RE+'s item actors.

## Sound

A running conveyor (`bOn` and `Speed` not 0) plays its `AmbientSound` as an ambient sound from the point of its path
nearest the listener, so a long belt sounds from where the listener is beside it. Range, volume, pitch and
occlusion follow the ambient sound rules (audible within `100 x SoundRadius`). The default is a steady 4.1 s machine
loop from the shared sounds. A stopped belt is silent. A driver (`CL_Driver`) does not play the default: its meshes
are the map's own machinery, which has its own sounds; it plays a sound its map names. The default was chosen by
name and length from the shared sounds, not by listening.

The map builder gives the belt and the escalator slope the `SURFACE_Conveyor` surface type unless their material
names one, so footsteps and other surface sounds pick the bank's conveyor entry (the default entry when a bank has
none).

## Bots

Bots plan and walk with the belts (`NavConveyors`). The navigation graph knows nothing of conveyors, because they
switch and reverse during a round; the planner and the follower read the belts' state when they plan and steer.

- **Planning.** Each walking edge whose middle a running conveyor carries costs its length times
  `speed / headway`, where `headway` is the speed along the edge of walking at run speed (385) with the belt under it,
  steering so the belt's sideways push is cancelled: `along + sqrt(speed^2 - across^2)`. With the belt this is below
  1; against it above 1; a belt that leaves no headway (faster than the bot runs, against it) costs 100 times the
  length. Edges that use a link (ladders and the like) are not scaled.
- **Steering.** On a carrying conveyor the follower aims so that its walk plus the belt heads for the next waypoint;
  when the belt is too fast across the way, it walks straight against the belt's sideways part.

## Constants

| Name | Value | Note |
|---|---|---|
| `ConveyorK.CarryStep` | 18 | RE+ `Carry` lift |
| `ConveyorK.EdgeOverhang` | 0.5 | of the body's radius, as RE+ |
| `ConveyorK.RideBelow` | 8 | design value |
| `ConveyorK.RideAbove` | 12 | design value, plus radius x slope |
| `ConveyorK.ObjectFloorProbe` | 8 | design value |

All values are OpenSCCT design values (estimated), not decoded from the original.

## Converted RE+ belts

The map converter (svmbuild `mapconv/conveyors.py`) turns the RE+ belts a map defines itself (`ConveyorBelt`, the
class RE+ 2.7 compiles into the map; RE+ `Reloaded.Editor/ConveyorModel.h`) into conveyors, and does not write the
belts. What it keeps from RE+'s script:

- **Geometry.** A belt's pivot is its start on the riding surface and `vector(Rotation)` its direction and slope; its
  end is `Location + vector(Rotation) x Length`. Belts linked by `NextBelt` form a chain. RE+'s chain leader is the
  belt nothing links onto (in a closed loop, the belt whose name sorts first), and a chain runs over at most the
  class's `Seg` array (16 belts in class version 1, AeroD's; 64 from version 2).
- **Runs.** A chain is cut into runs of belts that join end to start (within 1 uu) and share `Speed`, `Width` and
  `bCarryPlayers`. Each run is one conveyor through its belts' starts and ends, with sharp corners (RE+ curves are
  already chords). Items come onto a belt from the end of the one before at the nearest point of its centre line
  (RE+ `Join`, `Entry`); where that is past the belt's start, the belt is cut there and the part before is a
  conveyor of its own that carries riders but no items (`<id>_Start`). Runs hand items on with `Next`. A loop that is
  one run is a `bLoop` path.
- **Carrying.** `Width`, `Speed` and `bCarryPlayers` per run. `bCarryObjects` is false: RE+ moved only pawns.
- **Items.** Every run that carries the chain's items takes its leader's `ItemMeshes` (the slots set, in order),
  `ItemSpacing`, `ItemScale` and `FadeDistance` (as `ItemFade`), when its `bShowItems` is set. `ItemSeed` is a
  CRC-32 of the leader's name, `ItemYawJitter` 2001 (RE+ turned items by `Rand(2001) - 1000`). The library carries
  the item meshes and the client builds them lit as actors.
- **Look.** `CL_Driver`: what RE+ draws stays as the map has it. A hidden belt (AeroD's drivers) draws nothing; a
  drawn belt's `ConveyorFrame` mesh stays a static mesh of the world, in the render and the collision.
- **Events.** None: the conveyor's `Tag` is its own id, since RE+ belts take no events.
- **Treads.** AeroD's `AerTreadScroll` map script set a `TexPanner` (`PanDirection` Yaw 16384, its `Rates[i]`) over a
  tread texture into `Skins[Slot]` of the meshes tagged `Runs[i]`. Each tagged mesh becomes a scroll target of the
  conveyor whose path passes nearest its pivot (within 512 uu): `Axis` 1, `Material` the slot's material, and
  `UnitsPerTile = -Speed / Rates[i]`, so the tread moves as the panner moved it (a panner adds
  `PanRate x time` to the texture coordinates; a scroll subtracts `phase / UnitsPerTile`). The script is not written.
- **Runtime classes.** `ConveyorMotor`, `ConveyorItem` and `ConveyorRoller` are spawned by belts at run time; a map
  that places one has it dropped too.

`src.liftedFrom` names the RE+ actors each conveyor stands for ([SVMap 1.2: conveyors](../svmap/1.2/conveyors.md)).
Not kept: RE+'s side push into a belt's centre (`Push`: riders overlapping its channels move inwards), the rollers a
drawn belt spins, items' centring on their mesh bounds (`ItemOffsets`; OpenSCCT only rests them on their lowest
point), the riders' height band (RE+ carries feet from 16 below to 34 above the surface) and RE+'s carrying of
pawns that stand on a belt only by its box (see below).

## Differences from RE+

- One actor per conveyor instead of a chain of belts, corner pieces and chord-approximated curves.
- Pawns are carried by the shared walking physics, not by `Pawn.Move` calls on each machine.
- Items, rollers and steps come from the replicated phase, so every client shows the same belt. RE+ placed items with
  each machine's own random numbers.
- Events start, stop and reverse conveyors; RE+ belts could not be switched.
- Items queue behind each other and pass from one conveyor to the one its `Next` names; RE+ items stayed on their own
  chain. They do not collide with pawns or soft bodies.
- Escalator steps move, but pawns walk on the slope under them and only their drawn feet follow the treads.
- Not built yet: soft-body curtains do not part around items.
