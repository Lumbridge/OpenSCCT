# Architecture

## Goal

Reproduce Splinter Cell: Chaos Theory Versus behaviour exactly, in a codebase that is easier to
maintain and extend than the original UE2.5 game. Behaviour is proven by differential traces against
the real game, not by feel.

## Tracks

Work runs on three parallel tracks.

- **R, reverse engineering.** Binary hashes and reflection dumps (R0), a headless Ghidra project
  with idempotent scripts that name natives, classes, vtables and globals, using the editor build's
  guard strings and BSim to carry names to the game (R1), an UnrealScript decompiler (R2), the
  coverage inventory (R3), differential testing against the original (R4) and behaviour specs (R5,
  R6). Outputs that come from the game stay local; see `re/PROVENANCE.md`.
- **B, engine, gameplay and netcode.** The C# simulation, the SBase port, the UE2 network model,
  hosting and the Godot presentation.
- **C, content, SVMap and rendering.** The `svmbuild` pipeline that converts the original install,
  the SVMap format, the map tools plugin and the renderer.

Milestones: M0 foundations, M1 RE infrastructure, M2 SteeE content and the Faithful renderer,
M3 offline gameplay, M4 netcode core and combat, M5 services, M6 the SteeE Story 2v2 online
vertical slice, M7 SteeE parity and the Standard playlist, M8 full parity (all 86 maps, every mode).

## Projects

| Project | Role |
|---|---|
| `OpenScct.Provenance` | attributes that link C# to the original (`[Uc]`, `[UcNative]`, `[PortOf]`, `[Provenance]`) |
| `OpenScct.Core` | UE2 runtime: Name, FVector, FRotator, SvmMath, Actor, Pawn, Controller, PlayerController, Inventory, Mover, triggers, ZoneInfo, LevelInfo, GameInfo, GRI, PRI, Level (ordered tick, deferred spawn and destroy, tag index), states with serializable latent frames, timers, anim channels, SimEvents, seeded RNG streams |
| `OpenScct.Physics` | `ICollisionWorld` (LineCheck, extent sweeps, PointCheck), BSP, hull, per-poly and BVH backends, physics modes 1 to 16 including pipe, zip, fence, ledge, ladder and hand-over-hand, GE queries |
| `OpenScct.Game` | the SBase port: `SBase/<Class>.cs`, `<Class>.<state>.cs`, `Generated/{Defaults,Replication,Dispatch}` |
| `OpenScct.Net` | the UE2 network model (see `docs/NETCODE.md`) |
| `OpenScct.Content` | SVMap reader (`sim/` only), class-defaults pack, manifests with SHA-256, UE basis |
| `OpenScct.Server`, `OpenScct.Server.Cli` | dedicated server for win-x64 and linux-x64, admin console, the online session (see `docs/ONLINE.md`) |
| `OpenScct.Master` | master server on a VPS: HTTP server list, UDP NAT punch introducer and relay (see `docs/ONLINE.md`) |
| `game/` | Godot project: boot, menus, server browser, lobby, game and results scenes, rendering, views, input, cameras, HUD, audio, UI, the map tools plugin |
| `content/svmbuild` | Python pipeline: classes, textures, meshes, maps, characters, audio, UI, validate, cook |

Planned and not yet in the solution: `OpenScct.Bots` (headless bot clients), `OpenScct.Generators` (Roslyn generators for state dispatch,
replication, defaults and the net class registry), `OpenScct.Analyzers` (no Godot in the sim, banned
APIs, `[Uc]` required in `OpenScct.Game`, no mutable statics).

### Dependency rules

```
OpenScct.Provenance <- OpenScct.Core <- OpenScct.Physics <- OpenScct.Game
                  OpenScct.Core <- OpenScct.Net
                  OpenScct.Core <- OpenScct.Content
OpenScct.Server     -> Core, Physics, Game, Net, Content (never Godot)
game/          -> OpenScct.Server, so a player can host
```

- The simulation libraries (Core, Physics, Game, Net, Content) never reference Godot. `build/Sim.props`
  fails the build if one does, and turns banned-API use (RS0030) into an error.
- Net and Content depend on Core only, not on each other or on Game.
- Nothing in `src/` depends on `game/`.

## Simulation and presentation

- The simulation is pure C#. It runs a fixed 60 Hz tick, with the UE2 sub-step rule inside physics.
- Views read `RenderState` and `SimEvents`. They never write simulation state.
- A listen server is the dedicated server code on its own thread, with the local player connected
  through the loopback transport. There is one code path for hosting.
- The server reads only the `sim/` part of an SVMap. Rendering, audio and UI data are client-only.
- Godot never drives gameplay. `Ue2PoseApplier` poses a Skeleton3D from the core anim sampler (no
  AnimationTree), and the camera port lives in the core.

## UE conventions in the simulation

- Unreal units (uu), left-handed, Z up.
- Rotators in 65536 units per turn, float32 everywhere the original used float.
- Original enums, names and values: for example `EPhysics` 0 None, 1 Walking, 2 Falling ... 16 Flying.
- Traversal is data: GE records from the level (pole, fence, hand-over-hand, ledge, ladder, pipe,
  zip line), gated by `LevelInfo.GEActivationMask`.
- One conversion point to Godot space, `UeSpace`: UE (x, y, z) becomes Godot (x, z, y) / 100,
  checked by a handedness test. Nothing else converts coordinates.

## Content flow

```
game install --svmbuild--> .content/ (SVMap per map + shared packs) --cook--> PCKs for releases
                                 |
                                 +-- sim/ --> OpenScct.Content --> server and client simulation
```

Converted content is built from the user's own install and never committed. SVMap 1.0 is specified
in `spec/svmap/1.0`.

## Verification

- `dotnet test`: unit, property and integration tests (virtual clock and SimLink), zero allocations
  per tick.
- Determinism: replay hashes from win-x64 and linux-x64 must match (planned CI job).
- Godot headless smoke test in CI (`tools/ci/godot-smoke.ps1`).
- Differential traces against the original, a collision oracle, golden renders and coverage gates.
