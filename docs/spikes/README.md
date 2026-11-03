# Historical investigations

These reports record early feasibility checks and measurements. Their results,
commands and open issues describe the checkout and original-game build used at the
time. Use [development](../DEVELOPMENT.md) and [testing](../TESTING.md) for current setup.

## Runtime and content

| Report | What it established |
|---|---|
| [Godot and .NET](S0-godot-net10.md) | Build, headless startup and export feasibility |
| [Class defaults](P1-class-defaults.md) | Decoding and comparing original class defaults |
| [Static mesh collision](C2-staticmesh-collision.md) | Mesh collision data and hull limitations |
| [Traversal records](C3-ge-list.md) | Decoding the level's GE records |
| [Map conversion](P4-maps.md) | Conversion and validation of the installed maps |

## Original-game evidence

| Report | What it measured |
|---|---|
| [Reflection](R0-reflection.md) | Editor and game layouts |
| [UnrealScript decompiler](R2-ucdecomp.md) | Bytecode round-trip checks |
| [Locomotion recordings](R4-difftest.md) | Repeatable movement traces |
| [Traversal recordings](R4-traversal.md) | Ledges, pipes, zip lines and related movement |
| [Remaining moves](R4-moveset.md) | Ladders, fences, jumps, steps and other moves |
| [Simulation comparison](R4-sim-vs-game.md) | Recorded behavior compared with the rewrite |
| [Combat recordings](R4-combat.md) | Damage, weapons and reactions with a two-pawn rig |

Current behavior references live in [spec](../../spec/README.md); see the
[documentation index](../README.md) for implementation guides and later reports.
