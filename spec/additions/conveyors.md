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
| `Look` | `CL_Frame` | `CL_Frame` (belt, side walls, rollers), `CL_Driver` (nothing built; the map's own meshes are the look and the floor), `CL_Treadmill` (flat belt, low edges), `CL_Escalator` (steps, balustrades). |
| `Walls` | `CW_Both` | Which sides of a frame have a wall: `CW_Both`, `CW_Left`, `CW_Right`, `CW_None`. |
| `TriggerAction` | `CT_Toggle` | What an event does: `CT_Toggle`, `CT_TurnOn`, `CT_TurnOff`, `CT_Reverse`. |
| `ItemMeshes` | empty | Static meshes riding the belt; each item picks one from the seed. |
| `ItemSpacing`, `ItemScale`, `ItemFade`, `ItemSeed`, `ItemYawJitter` | 160, 1, 48, 0, 65536 | Item spacing along the path, size, fade length at the ends of an open path, seed and largest random turn (rotator units). |
| `ScrollTargets` | empty | `{Target, UnitsPerTile, Axis}`: a placed mesh actor (by id) whose texture moves with the belt. `UnitsPerTile` is the uu one texture repeat covers along the direction of travel, `Axis` 0 for U and 1 for V. |
| `BeltMaterial`, `FrameMaterial` | none | Materials the map builder uses for the generated look. |

`PhaseAtChange` and `ChangeTick` are runtime state (below); maps leave them at 0.

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

## Who rides

A body rides the conveyor when all of these hold (`ConveyorField.FindCarrier`):

- the conveyor is running (`bOn` and `Speed` not 0) and carries that kind of body;
- the body's feet (location minus collision height) project onto the path within `Width / 2 + radius / 2`
  (RE+'s overhang);
- the feet are no more than 8 uu below the surface and no more than `12 + radius x slope` above it, where `slope` is
  the climb per flat uu at that point (a cylinder on a slope rests on its edge).

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

On the server, each running conveyor with `bCarryObjects` moves, every tick, each resting `SFlag` or `SDisk` it
carries (same rules as above): visible, `PHYS_None`, colliding with the world and not based on a pawn. The move is the
same up, along, down step. When no floor is within 8 uu below the object afterwards, it starts falling
(`PHYS_Falling`) and lands by its own rules. These objects replicate their location, so clients see them move.

## Events

`Trigger` applies `TriggerAction`. Every change of `bOn` or `Speed` first stores the distance the belt has travelled,
`PhaseAtChange`, and the server tick, `ChangeTick`. The phase at server tick `t` is then
`PhaseAtChange + (bOn ? Speed : 0) x (t - ChangeTick) x Sim.DeltaTime`, continuous across changes. `Reset` (a new
round) restores the placed `bOn` and `Speed` the same way.

## Network

The actor is a `bNoDelete`, always relevant map actor replicated to clients as a simulated proxy. `bOn`, `Speed`,
`PhaseAtChange` and `ChangeTick` replicate when they change (a runtime class extension of the Actor layout, so the
protocol hash changes).

Client prediction: each recorded movement tick's collision inputs include every conveyor's `bOn` and `Speed`, and
replay stages them as it stages moving-base poses. A toggle inside the replay window replays with the state each tick
saw. As with movers, a change the server made before the client heard of it is corrected once the next
acknowledgement arrives.

## Look

The map builder (the OpenSCCT editor) builds each look into `render/world.glb` and its collision into
`sim/collision.bin`, lit like the rest of the map. The moving parts are separate gameplay instances named after the
conveyor's id:

| Instance | Look | Texture |
|---|---|---|
| `<id>.Belt` | frame, treadmill | V runs along the path, one repeat every 128 uu |
| `<id>.Steps` | escalator | V runs along the path, one repeat per 40 uu step |
| `<id>.Rollers` | frame | U runs once round each 6 uu roller, rollers every 24 uu |

The frame (walls 24 uu high and 8 thick, a body 32 uu deep), treadmill edges (4 uu) and escalator balustrades (96 uu)
are static. `ConveyorLayout` holds these values for the builder and the game.

Clients slide the moving parts' textures by the phase at the displayed server time (each node gets its own material
copies), including every `ScrollTargets` mesh. Items are drawn one node each and lit as actors (they take the actor
light slots like any moving prop). Item `i` of `n = max(1, floor(L / ItemSpacing))` sits at
`i x L / n + jitter(i) + phase` along the path (wrapped), with its yaw from the direction of travel plus a seeded turn,
and its mesh picked from the seed. On an open path it scales from 0 to `ItemScale` over `ItemFade` at both ends. The
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
- Not built yet: bots do not plan with conveyor velocity, footsteps do not use `SURFACE_Conveyor`, conveyors have no
  loop sound, and soft-body curtains do not part around items.
