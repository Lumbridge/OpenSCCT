# Architecture

OpenSCCT separates the game simulation from the Godot client. The same C# simulation
runs in a dedicated server, a locally hosted match and automated tests. Godot handles
input, rendering, audio and menus.

The project aims to reproduce Chaos Theory Versus behavior. Specifications and
comparisons with the original provide evidence; a passing build alone does not prove
parity. See [testing](TESTING.md) and the [coverage report](progress/COVERAGE.md).

New to the codebase? Start with [development setup](DEVELOPMENT.md). Browse all guides
in the [documentation index](README.md).

## Find the code

| Area | Project or folder | Responsibility |
|---|---|---|
| Simulation runtime | [OpenScct.Core](../src/OpenScct.Core/) | Actors, states, timers, animation channels, simulation events and seeded random numbers |
| Collision and movement | [OpenScct.Physics](../src/OpenScct.Physics/) | Traces, collision geometry, pawn physics and traversal queries |
| Gameplay | [OpenScct.Game](../src/OpenScct.Game/) | Original SBase classes, objectives, combat, equipment and bot behavior |
| Networking | [OpenScct.Net](../src/OpenScct.Net/) | Packets, replication, movement synchronization, discovery and voice |
| Content loading | [OpenScct.Content](../src/OpenScct.Content/) | SVMap simulation data, class defaults and content manifests |
| Map services | [OpenScct.Maps](../src/OpenScct.Maps/) | Map downloads, installation and content storage |
| Hosting | [OpenScct.Server](../src/OpenScct.Server/), [Server.Cli](../src/OpenScct.Server.Cli/) | Map catalog, shared server/session code and the dedicated server executable |
| Online services | [OpenScct.Master](../src/OpenScct.Master/) | Server directory, accounts, player website and connection services |
| Bot hosting and tests | [OpenScct.Bots](../src/OpenScct.Bots/) | Server bots, headless clients and automated matches |
| Launcher | [OpenScct.Launcher](../src/OpenScct.Launcher/), [Launcher.Core](../src/OpenScct.Launcher.Core/) | Installation, updates and content preparation |
| Game client | [game](../game/) | Godot scenes, views, cameras, HUD, audio and UI |
| Content conversion | [content/svmbuild](../content/svmbuild/) | Converts an original installation into maps and shared content packs |
| Evidence and code generation | [OpenScct.Provenance](../src/OpenScct.Provenance/), [CodeGen](CODEGEN.md), [re](../re/README.md) | Links ports to specifications and original behavior |

## Dependencies

```text
Provenance <- Core <- Physics <- Game
              ^
              +-- Net
              +-- Content

Server -> simulation, networking and content libraries
Godot client -> Server and presentation code
```

- The simulation libraries never reference Godot. [Sim.props](../build/Sim.props)
  enforces this and treats banned API use as a build error.
- Net and Content depend on Core, not on each other or Game.
- Projects in `src/` do not depend on `game/`.

## Simulation and presentation

The simulation runs at a fixed 60 Hz, with the original engine's sub-step rules inside
physics. Rendering reads simulation state and events; it does not own gameplay rules.
A listen server runs the shared server code on its own thread, with the local player
connected through loopback transport.

The server loads only the simulation portion of a map. Client code loads the rendering,
audio and UI data. Pawn animation comes from the simulation's animation channels;
Godot poses the skeleton from those channels rather than using an AnimationTree to
drive gameplay. See [animation](ANIMATION.md), [rendering](RENDERING.md) and
[networking](NETCODE.md).

## Coordinates and original names

Simulation code keeps the original conventions:

- Unreal units, left-handed coordinates and Z up.
- Rotators use 65,536 units per turn; original float values stay 32-bit.
- Classes, fields, states and enum values keep their original names.
- Traversal uses the map's GE records for ledges, ladders, pipes, fences and zip lines.

[UeSpace](../game/Scripts/Space/UeSpace.cs) converts simulation coordinates to Godot:
`(x, y, z)` becomes `(x, z, y) / 100`. Other code should use this shared conversion.
See [coding conventions](CONVENTIONS.md) for naming, provenance and determinism rules.

## Content flow

```text
Original installation -> svmbuild -> .content/ -> release content packs
                                      |
                                      +-- sim/ -> server and client simulation
                                      +-- render/, audio/, shared/ -> Godot client
```

Converted game content stays out of Git. The [provenance policy](../re/PROVENANCE.md)
defines what may be committed. See the [content pipeline](../content/README.md) and
the [SVMap 1.1 format](../spec/svmap/1.1/README.md).

## Verification

Unit and integration tests check the simulation and network behavior. Godot smoke
tests check selected client paths. Local differential tests compare recorded behavior
against the original game, and visual/audio checks cover results that automated tests
cannot establish.

Cross-platform replay-hash comparison remains a planned CI check. See
[testing and CI](TESTING.md) for the checks that actually run, their limits and local
commands.
