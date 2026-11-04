# Conveyors

OpenSCCT addition. `Svm.Conveyor` is a map actor for belts, travelators and escalators. It carries walking pawns and
dropped objects along a path, and map events start, stop and reverse it. It replaces RE+'s `ConveyorBelt`,
`ConveyorMotor`, `ConveyorRoller` and `ConveyorItem` classes and the AeroD `AerTreadScroll` script with one actor
that the movement physics knows. The SVMap data is in [SVMap 1.2: conveyors](../svmap/1.2/conveyors.md).

Code: `src/OpenScct.Physics/Conveyors` (path maths, the level's conveyor field),
`src/OpenScct.Physics/Movement/PhysicsDriver.Conveyor.cs` (the carry), `src/OpenScct.Game/Svm` (the actor, its
metadata and the shared look values), `game/Scripts/Views/ConveyorViews.cs` (drawing).

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
| `ScrollTargets` | empty | `{Target, UnitsPerTile, Axis}`: a placed mesh actor (by id) whose texture moves with the belt. `UnitsPerTile` is the uu one texture repeat covers along the direction of travel, `Axis` 0 for U and 1 for V. |
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

So a start, stop or reversal costs one correction, the first acknowledgement after the change, rather than one per
acknowledgement until the moves predicted with the old state age out.

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
- Escalator steps are 40 uu along the path each. A step's tread is level at the higher of its two ends and its riser
  stands at the lower end, so the slope runs through the steps' inner corners. Pawns collide with the slope, not the
  steps; a pawn on a slope stands `radius x slope` above it, so its feet stay within a few units of the treads. The
  shape does not move; the texture slides one repeat per step. More than 20 uu rise per step (over 30 degrees) is a
  map check warning.
- `CW_Auto` walls open where another conveyor's open end meets this one's side: over the joining conveyor's width
  (lengthened for a slanted join) plus the wall's thickness either side, with the cut wall ends closed.
- Textures: the belt and steps use `BeltMaterial` (U across, one repeat per 128 uu), the drums too, and the frame
  `FrameMaterial`; with none, the editor's default material. On both drums U runs the way the belt moves round them
  (over the top and down at the far end, up from the bottom at the start), so one scroll turns both with the belt.

Clients slide the moving parts' textures by the phase at the displayed server time, including every `ScrollTargets`
mesh. Each node gets its own copies of its materials and of their later passes (the vision passes), and every texture
layer (diffuse, opacity, specular, self-illumination and sequences) slides. Items are drawn one node each and lit as
actors (they take the actor light slots like any moving prop). Item `i` of `n = max(1, floor(L / ItemSpacing))` sits
at `i x L / n + jitter(i) + phase` along the path (wrapped). It faces the direction of travel and tilts with the
belt's climb, turns about the belt's up by a seeded amount, and is raised so its mesh's lowest point rests on the
belt (RE+ kept per-mesh `ItemOffsets` for this). Its mesh is picked from the seed. On an open path it scales from 0
to `ItemScale` over `ItemFade` at both ends. Items are drawn only: they do not collide, unlike RE+'s item actors. The
same function places the editor's preview.

## Constants

| Name | Value | Note |
|---|---|---|
| `ConveyorK.CarryStep` | 18 | RE+ `Carry` lift |
| `ConveyorK.EdgeOverhang` | 0.5 | of the body's radius, as RE+ |
| `ConveyorK.RideBelow` | 8 | design value |
| `ConveyorK.RideAbove` | 12 | design value, plus radius x slope |
| `ConveyorK.ObjectFloorProbe` | 8 | design value |

All values are OpenSCCT design values (estimated), not decoded from the original.

## Differences from RE+

- One actor per conveyor instead of a chain of belts, corner pieces and chord-approximated curves.
- Pawns are carried by the shared walking physics, not by `Pawn.Move` calls on each machine.
- Items, rollers and treads come from the replicated phase, so every client shows the same belt. RE+ placed items with
  each machine's own random numbers.
- Events start, stop and reverse conveyors; RE+ belts could not be switched.
- Items do not collide with pawns or soft bodies.
- Not built yet: bots do not plan with conveyor velocity, footsteps do not use `SURFACE_Conveyor`, conveyors have no
  loop sound, soft-body curtains do not part around items, items are not handed on from one conveyor to the next
  (each conveyor's items shrink away at its open ends), and escalator steps do not move (their texture slides).
