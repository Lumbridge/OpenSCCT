# `sim/movers.json` (SVMap 1.2, optional)

Each mover's own time per leg and easing per key. The server and every client read it at load
([mover-legs.md](../../additions/mover-legs.md) has the run-time rules); a map without the file moves every mover with
the game's timing (one `MoveTime` for every leg, one `MoverGlideType`).

```json
{
  "format": "svm.movers/1",
  "movers": [
    {"actor": "Mover40", "keys": [{"smooth": true}, {"time": 4, "smooth": false}, {"time": 0.75}]},
    {"actor": "Mover57", "keys": [{}, {"time": 1.5}]}
  ]
}
```

## `movers`

One entry per mover, in any order; a mover is listed at most once.

- `actor`: the id of an actor in `sim/gameplay.json` whose class is `Engine.Mover` or a subclass. Ids match ignoring
  ASCII case.
- `keys`: up to 8 objects, `keys[n]` for key `n`. A key the list does not reach keeps the mover's own timing, and so
  does an empty object. Each may hold:
  - `time` (number, 0.005 to 3600): the seconds of the leg between key `n - 1` and key `n`, run either way. Key 0 has
    no leg before it and takes no `time`. Without it the leg takes the mover's `MoveTime`.
  - `smooth` (`true` or `false`): whether the mover eases in and out at this key. Without it the mover's
    `MoverGlideType` decides (`MV_GlideByTime` is smooth).

Writers leave out trailing empty keys and movers with nothing set. The file holds no copy of the movers' own
`MoveTime` or `MoverGlideType`; those stay in `sim/gameplay.json`.

## Validation

Schema: [schemas/movers.schema.json](schemas/movers.schema.json) (the format id, at most 8 keys, no `time` on key 0,
the time range and a boolean `smooth`). The validator also checks that every `actor` is a mover of the map and is
listed once. A mover is `Engine.Mover` or a subclass, by the super classes in `sim/classes.json`, else by the game's
stock class tree (`SBase.SLift`, `SBase.SLiftDoor`, `Engine.MoverRotating`): a map built against the shared class
pack need not carry the table. Keys past the mover's `NumKeys` are allowed (they are never reached).

## Converted maps

The original game and RE+ have no per-key timing, so converted maps never have this file. A map with it cannot be
exported to `.sdc` as authored: an exporter keeps each mover's own `MoveTime` and glide and says so.
