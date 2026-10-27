# S0: Godot 4.7.2 .NET on net10.0

**Verdict: GO.** The game project and the simulation libraries target net10.0. The project builds,
runs headless from the editor binary and exports to a self-contained Windows exe that boots and
passes the smoke test. The net8.0 fallback is not needed.

Date: 2026-10-23.

## Question

Godot 4.7.2 ships its C# bindings (GodotSharp 4.7.2) built for net8.0. The plan pins the whole
repo to net10.0 (C# 14). Does a Godot project with `TargetFramework` net10.0 build, run in the
editor binary, export, and run as an exported exe? Gate from the plan: "exported exe boots".

## Setup

| Item | Version |
|---|---|
| OS | Windows 11 Pro 10.0.26200 |
| .NET SDK | 10.0.302 (global.json, rollForward latestFeature) |
| .NET runtimes present | 8.0.29, 9.0.18, 10.0.8, 10.0.10 |
| Godot | v4.7.2.stable.mono.official.ed1daf0bf, from `tools/Get-Godot.ps1` |
| Godot.NET.Sdk / GodotSharp / Godot.SourceGenerators | 4.7.2 from nuget.org (GodotSharp lib is `net8.0`) |
| Export templates | `Godot_v4.7.2-stable_mono_export_templates.tpz`, SHA-512 checked |
| Project | `game/OpenScctGame.csproj`, `Sdk="Godot.NET.Sdk/4.7.2"`, `TargetFramework` net10.0 |

The project inherits the root `Directory.Build.props` unchanged: C# 14, nullable, ImplicitUsings
disabled, TreatWarningsAsErrors, AnalysisLevel latest, InvariantGlobalization.

## Evidence

All commands run from the repo root. `$godot` is the console exe printed by `tools/Get-Godot.ps1`.

| Step | Command | Exit | Time | Result |
|---|---|---|---|---|
| 1 | `tools/Get-Godot.ps1 -ExportTemplates` | 0 | 0.1 s cached | editor and templates verified |
| 2 | `dotnet build game/OpenScctGame.slnx` | 0 | 1.5 s | 0 warnings, 0 errors |
| 3 | `$godot --headless --path game --build-solutions --quit` | 0 | 4.4 s | Godot builds the project itself |
| 4 | `$godot --headless --path game -- --svm-smoke=boot` | 0 | 0.6 s | `SVM_SMOKE boot OK runtime=10.0.10` |
| 5 | `$godot --headless --path game -- --svm-smoke=selftest-fail` | 1 | 0.5 s | failure path works |
| 6 | `$godot --headless --path game --export-release "Windows Desktop" ../local/build/OpenSCCT.exe` | 0 | 6.0 s | exe, console wrapper, data folder |
| 7 | `local/build/OpenSCCT.console.exe --headless -- --svm-smoke=boot` | 0 | 0.6 s | `SVM_SMOKE boot OK runtime=10.0.10` |
| 8 | `local/build/OpenSCCT.console.exe --headless -- --svm-smoke=selftest-fail` | 1 | 0.5 s | failure path works |
| 9 | `local/build/OpenSCCT.console.exe --headless -- --svm-smoke=nope` | 2 | 0.5 s | unknown test reported |
| 10 | `Start-Process local/build/OpenSCCT.exe -ArgumentList '--headless','--','--svm-smoke=boot' -Wait -PassThru` | 0 | | GUI exe gives the same exit codes |

What the export contains:

- `OpenSCCT.exe` (109 MB, pck embedded) and `OpenSCCT.console.exe` (console wrapper).
- `data_OpenScctGame_windows_x86_64/`: 198 files, 86 MB. It is a self-contained .NET 10.0.10 runtime
  (`coreclr.dll` 10.0.10, `OpenScctGame.runtimeconfig.json` with `tfm` net10.0 and
  `includedFrameworks` Microsoft.NETCore.App 10.0.10) plus OpenScctGame, OpenScct.Core, OpenScct.Content and
  OpenScct.Provenance.
- The exported `OpenScct.Core.dll` is byte-identical to `src/OpenScct.Core/bin/Release/net10.0/OpenScct.Core.dll`,
  so exports ship optimised libraries (see the configuration mapping below).

## Findings and gotchas

1. **No `game/Directory.Build.props` is needed.** Godot's source generators build cleanly under the
   repo's warnings-as-errors, nullable and no-implicit-usings settings. Central package management
   also works: the SDK's implicit `GodotSharp`, `GodotSharpEditor` and `Godot.SourceGenerators`
   references are marked `IsImplicitlyDefined`, so `Directory.Packages.props` needs no Godot entries.
2. **Export needs a solution file next to the project.** Without one, the export prints
   "no solution file was found at ...\OpenScctGame.sln" for every script, still **exits 0**, and writes an
   exe with no `data_OpenScctGame_windows_x86_64` folder. `game/OpenScctGame.slnx` satisfies it (4.7 reads
   `.slnx`). Do not keep a `.sln` next to it.
3. **Export exit codes are not trustworthy.** Treat an export as good only when the data folder
   exists and the exported exe passes the smoke test.
4. **Configuration mapping.** Godot builds `Debug`, `ExportDebug` and `ExportRelease`. The libraries
   only define Debug and Release, and an unknown configuration name builds them without
   optimisation. `OpenScctGame.csproj` sets `SetConfiguration` on its project references (ExportRelease
   maps to Release, everything else to Debug) and `OpenScctGame.slnx` carries the same mapping for
   solution builds. Checked by hash as above.
5. **A smoke run with no built assembly hangs forever.** The C# autoload cannot load, so nothing
   quits. `Scripts/App/smoke_watchdog.gd`, a small GDScript autoload with no .NET dependency, ends
   the run with exit 3 if the C# runner has not appeared after 15 s, and with exit 4 at the smoke
   timeout plus 30 s. CI should still wrap Godot in its own timeout.
6. **Build before exporting.** If `game/.godot/mono/temp/bin` is missing, the export logs "Failed to
   create an autoload ... is not compiling" while the editor loads, then publishes and succeeds.
   Running `dotnet build game/OpenScctGame.slnx` first keeps the log clean.
7. **`.uid` files.** Any headless editor run (`--build-solutions`, `--import`, `--export-*`) writes a
   `*.cs.uid` / `*.gd.uid` next to each script. Godot expects them in version control; they are
   committed with the scripts.
8. **Console wrapper.** `debug/export_console_wrapper=2` makes the wrapper for release exports too.
   It is the simplest way to get output and the exit code in CI. The GUI exe also returns the right
   exit code but PowerShell only waits for it with `Start-Process -Wait`.
9. `application/modify_resources=false` so the export does not need rcedit.

## Smoke protocol

`<exe> --headless -- --svm-smoke=<name> [--svm-smoke-timeout=<seconds>]`

| Exit | Meaning | Output line |
|---|---|---|
| 0 | passed | `SVM_SMOKE <name> OK [detail]` |
| 1 | failed or timed out (C# runner, default 120 s) | `SVM_SMOKE <name> FAIL <reason>` |
| 2 | unknown smoke test | `SVM_SMOKE <name> FAIL unknown smoke test (known: ...)` |
| 3 | C# runner never started (watchdog) | `SVM_SMOKE <name> FAIL C# smoke runner did not start ...` |
| 4 | hard time limit (watchdog) | `SVM_SMOKE <name> FAIL hard time limit ...` |

Tests today: `boot` (boot scene ready, simulation assemblies load) and `selftest-fail` (always
fails, proves the failure path). New tests subclass `SmokeTest` and register in `SmokeTests`.
`boot` will step a `OpenScct.Core` World for 600 ticks and check `TickCount` once that type exists.

## CI recipe (Windows job)

```powershell
$godot = & tools/Get-Godot.ps1 -ExportTemplates
dotnet build game/OpenScctGame.slnx
& $godot --headless --path game -- --svm-smoke=boot                 # expect exit 0
& $godot --headless --path game --export-release "Windows Desktop" ../local/build/OpenSCCT.exe
if (-not (Test-Path local/build/data_OpenScctGame_windows_x86_64)) { throw "export incomplete" }
& local/build/OpenSCCT.console.exe --headless -- --svm-smoke=boot    # expect exit 0
& local/build/OpenSCCT.console.exe --headless -- --svm-smoke=selftest-fail  # expect exit 1
```

When imported assets arrive, add `& $godot --headless --path game --import` before the export.

## Fallback (not needed)

Had net10.0 failed, the fallback was net8.0 for the game and every library the game references:

- `game/OpenScctGame.csproj`: `TargetFramework` net8.0.
- Libraries referenced by the game (OpenScct.Provenance, OpenScct.Core, OpenScct.Content, later OpenScct.Physics,
  OpenScct.Game, OpenScct.Net, OpenScct.Server): `TargetFrameworks` net10.0;net8.0. The root
  `Directory.Build.props` sets the singular `TargetFramework`, so those projects would clear it and
  set the plural form, or the root would switch to `TargetFrameworks` with an opt-out for tests.
- C# 14 compiles for net8.0, but features that need runtime or library support (some `params`
  collection types, newer BCL APIs) would need guarding, and the determinism hash check would have
  to cover both target frameworks.
- Tests and the dedicated server could stay net10.0 only.

## M0 client wiring (follow-up, 2026-10-23)

The game project now references OpenScct.Core, OpenScct.Content, OpenScct.Net and OpenScct.Server (OpenScct.Server brings OpenScct.Physics,
OpenScct.Game and LiteNetLib), still on net10.0. `game/OpenScctGame.slnx` lists all seven libraries with the same
ExportDebug / ExportRelease mapping. The verdict above stands.

What the client does now:

- `Scripts/Space/UeSpace.cs` is the only UE to Godot conversion: UE `(x, y, z)` uu becomes Godot `(x, z, y) / 100`
  metres, directions `(x, z, y)`, rotators become a Godot basis whose columns are the rotated UE X, Z and Y axes,
  and the full UE2 actor transform (PrePivot, DrawScale3D) is supported.
- `Scripts/Maps/MapLocator.cs` finds SVMap folders: `--svm-maps=dir` (repeatable), then `maps/` next to the exe,
  then (when not exported) the repo's `fixtures/` and `.content/maps/`.
- `Scripts/Maps/MapBuilder.cs` builds the world from `render/world.glb` at runtime (falling back to the
  collision triangles), instances `Library` meshes for actors whose `StaticMesh` names one, and places a marker per
  PlayerStart from the actors `MapLoader` created.
- `Scripts/Match/LocalMatch.cs` hosts a `ListenServer` (server thread) with the local player and bots over
  loopback. Clients tick on the main thread from the same clock as the server.
- `Scenes/Game.tscn` (`Scripts/Play/GameRoot.cs`) ties it together; `-- --svm-map=TestBox [--svm-bots=3]` starts
  there from Boot. Keyboard: W/S/A/D, mouse or Q/E to turn (click captures the mouse, Esc releases it).

### Evidence

| Step | Command | Exit | Time | Output |
|---|---|---|---|---|
| 1 | `dotnet build game/OpenScctGame.slnx` | 0 | 1.5 s | 0 warnings, 0 errors |
| 2 | `$godot --headless --path game -- --svm-smoke=boot` | 0 | 0.6 s | `SVM_SMOKE boot OK runtime=10.0.10 ticks=600` |
| 3 | `$godot --headless --path game -- --svm-smoke=testbox` | 0 | 10.6 s | `SVM_SMOKE testbox OK walkers=4 joinedAt=6 clientTicks=601 serverTicks=600 maxError=0.03uu lag=0 render=RenderGlb starts=4 instances=1` |
| 4 | `$godot --headless --path game -- --svm-smoke=selftest-fail` | 1 | 0.6 s | `SVM_SMOKE selftest-fail FAIL expected failure` |
| 5 | export (inside `tools/ci/godot-smoke.ps1 -Export`) | 0 | 7.6 s | data folder has OpenScctGame, Svm.*, LiteNetLib |
| 6 | `local/build/OpenSCCT.console.exe --headless -- --svm-smoke=testbox` | 0 | 10.6 s | same OK line as step 3 |
| 7 | `local/build/OpenSCCT.console.exe --headless -- --svm-smoke=boot` | 0 | 0.6 s | `SVM_SMOKE boot OK runtime=10.0.10 ticks=600` |
| 8 | `$godot --headless --path game --quit-after 400 -- --svm-map=TestBox` | 0 | | normal start path, no errors |

The exported `OpenScct.Server.dll` and `OpenScct.Net.dll` are byte-identical to their Release builds. The testbox smoke
was checked to fail when the axis mapping is broken (`UeSpace: UE +Y 100 uu must be Godot +Z 1 m`) and when a
walker is missing (`after 180 ticks: all joined True, local client sees 3 walkers, 3 drawn`).

What `testbox` checks:

1. `UeSpace`: axes, handedness (forward x up in Godot is the mapped UE right), yaw and pitch, proper rotations
   against the UE rotation matrix, the UE2 LocalToWorld transform, the triangle winding rule, round trip.
2. TestBox loads through `SvMap.Load` (sim hash checked) and `MapLoader` with no failed spawns.
3. The world comes from `world.glb`, and its bounds equal the world collider's bounds mapped through `UeSpace`
   within 0.1 mm. This also proves the pipeline's glTF space and `UeSpace` are the same mapping.
4. The `Library` node is not in the scene; `Mover0` is instanced at its location; 4 PlayerStart markers sit at
   their starts facing their yaw.
5. A listen server with the local player and 3 bots: all join and the local client sees 4 walkers within 180
   ticks; from then on every frame it sees 4, and each drawn walker sits exactly where its replicated actor is.
6. After 600 client ticks the server is stopped: 4 players, at least 570 server ticks, every server walker is on
   the local client (by NetGUID), applied at least 300 moves, and its newest replicated location is within 100 uu
   of the server's (measured: 0.03 uu, lag 0 ticks).

### More findings

1. **Runtime glTF loading works** headless and in the exported template (`GltfDocument.AppendFromFile` plus
   `GenerateScene`), with no import step, so maps can live outside the project and the pck.
2. **`GltfState.GetSceneNode(i)` is unsafe after `GenerateScene`.** For mesh nodes it returns the importer node
   that the mesh instance replaced, and the C# wrapper throws `InvalidCastException`. Use
   `GltfNode.GetSceneNodePath(state, false)` and look the node up in the generated scene.
3. **Winding.** Godot front faces are clockwise, glTF's counter-clockwise. A mesh built directly from mapped UE
   triangles keeps the UE order `(a, b, c)`; glTF files store `(a, c, b)` and the importer flips them back.
4. **Headless frame rate is uncapped**, so the client ticks run on time from the shared clock. Windowed runs are
   paced by vsync; `LocalMatch.Update` runs every tick that is due (up to 5 behind, then drops the backlog).
5. **Quick visual check without a screenshot tool:** `$godot --path game --write-movie <dir>/f.png
   --quit-after 240 -- --svm-map=TestBox` writes PNG frames (into `local/`, never a tracked path).

### Smoke protocol and CI, updated

- Tests: `boot` (boot scene, every simulation library loads, a `World` steps 600 ticks), `testbox` (above),
  `selftest-fail`.
- `tools/ci/godot-smoke.ps1` gets Godot, builds, imports, runs `boot` and `testbox` (must pass) and
  `selftest-fail` (must exit 1). With `-Export` it also exports, checks the data folder and the libraries in it,
  copies `fixtures/testbox` to `local/build/maps/testbox` and runs the same tests with the exported console exe.
  Every step is a JUnit test case in `local/test-results/godot-smoke.xml`; logs are in
  `local/test-results/godot-smoke/`. It exits with 1 on any failure and kills a Godot run that outlives the
  smoke timeout by 60 s. Works in PowerShell 7 and Windows PowerShell 5.1.
- `-Test` still works as an alias of `-Tests`, so `ci.yml`'s `-Test boot` runs only `boot`. Dropping the
  argument runs `testbox` too. `-Export` needs the export templates (a large download), so CI leaves it off for
  now.
