# SVMap 1.2: conveyors

SVMap 1.2 is a minor version of [1.1](../1.1/README.md): conveyors are optional data that a 1.1 reader loads as an
unported actor (it draws and collides with the built look, but nothing moves). The behaviour is in
[conveyors](../../additions/conveyors.md).

## `sim/gameplay.json`

A conveyor is an actor of class `Svm.Conveyor`, written like any other actor: `id`, `class`, `props` with only what
differs from the class defaults, typed as in 1.1. Example:

```json
{
  "id": "Conveyor0",
  "class": "Svm.Conveyor",
  "props": {
    "Location": {"X": 0, "Y": 0, "Z": 64},
    "Path": [
      {"Loc": {"X": 0, "Y": 0, "Z": 0}},
      {"Loc": {"X": 512, "Y": 0, "Z": 0}, "Radius": 128},
      {"Loc": {"X": 512, "Y": 512, "Z": 64}}
    ],
    "Speed": 200,
    "ItemMeshes": [{"$asset": "Gen_Obj_US.Cardboard.Box01"}],
    "Tag": "Belt1"
  }
}
```

- `Path` is a dynamic array of `ConveyorPoint` structs `{Loc, Radius}`; `ScrollTargets` of `ConveyorScroll` structs
  `{Target, UnitsPerTile, Axis}`, where `Target` is the id of a placed mesh actor (a string, since mesh actors are
  render-only).
- Enums are stored as numbers: `Look` (0 frame, 1 driver, 2 treadmill, 3 escalator), `Walls` (0 both, 1 left, 2 right,
  3 none, 4 auto; 4 is the default), `TriggerAction` (0 toggle, 1 on, 2 off, 3 reverse).
- `Tag` receives events as for any actor (event edges list the conveyor as a receiver).
- A conveyor takes no player-count rule (`sim/match.json`): its built look and riding surface are world geometry, so a
  rule could not take it out of a round. Gate what switches it instead ([match.md](match.md)).

The gameplay schema ([schemas/gameplay.schema.json](schemas/gameplay.schema.json), `conveyorProps`) types these props on
any actor of class `Svm.Conveyor`; its other props are typed as for any actor. Conveyors make the map 1.2: the
validator rejects them in a map that declares 1.1.

## `sim/classes.json`

The table need not list `Svm.Conveyor`. A reader that finds no entry for it uses the class's own defaults (the props
above and [conveyors](../../additions/conveyors.md)); a map that does list it gives `Engine.Actor` as its super.

## `sim/collision.bin`

The built look's collision is ordinary world collision without an actor id: the belt surface (frame, treadmill), the
steps' slope (escalator), and the frame's walls, edges and balustrades. A driver conveyor adds none; the map's own
meshes are its floor.

## `render/world.glb`

- The static look (frame body and walls, treadmill edges, escalator balustrades) is a `Library` mesh of the map's own
  package with an `instances` entry like any static mesh (actor `<id>.Frame`, `gameplay` false).
- The moving parts are `Library` meshes with `instances` entries that have `gameplay` true, so the client keeps them as
  separate nodes: actor `<id>.Belt`, `<id>.Steps` or `<id>.Rollers`, class `Conveyor`. Their texture coordinates follow
  the layout in [conveyors: look](../../additions/conveyors.md#look).
- Item meshes (`ItemMeshes`) are `Library` nodes the map's instances may not use; the client builds them for actors.
- Each `ScrollTargets` mesh actor's instance has `gameplay` true, so it is a separate node whose material the client
  can move.

Lighting is baked into these meshes' vertex colours like other static meshes.

## Readers

- A 1.1 reader keeps `Svm.Conveyor` as an unported actor; the built look draws and collides, nothing moves.
- A 1.2 reader creates the conveyor, carries riders and moves the look.
