# FPS benchmarks

Record fixed camera views in the original game, then replay them through an OpenSCCT release export.
This compares rendering performance on the same machine. Runs are sequential on the same machine, with a real renderer, uncapped FPS, vsync off, native resolution,
4x anisotropic filtering, no MSAA/SMAA, shadow quality 3 and smooth shadow edges. Player settings are isolated.
Both processes opt out of Windows background execution-speed throttling. The benchmark runner defaults to Vulkan for
repeatability. Use `-Driver d3d12` to measure OpenSCCT's Windows default without changing the player's configuration.
Keep game data, derived images, camera records and raw frame CSVs in ignored `local/`.
The [31 October results](RESULTS-2026-10-31.md) include the original comparison and remaining gaps.
The [5 November hot-path results](RESULTS-2026-11-05.md) cover packet serialization, bot navigation allocations
and actor-light uploads, with exact output checks and impaired-network validation.

The original needs the existing RE+ automation tools and a complete, idle Play Level sandbox. The verified Reloaded
Core build is checked by SHA-256, its loaded path and relocated cap-selector instructions before `--uncap` writes
only the sandbox child's two cap values to 10,000. Executable code is unchanged. Other builds can use `--cap 240`,
which measures capped player performance rather than uncapped engine throughput. All edited sandbox configuration
files are restored when the capture exits. The original stays on its hidden desktop; OpenSCCT renders off screen
without taking focus or the pointer.

## Run a comparison

```powershell
dotnet build game/OpenScctGame.slnx -c ExportRelease
tools/godot/4.7.2-stable/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path game --export-release "Windows Desktop" ../local/fps/build/OpenSCCT.exe

python -I -B tools/benchmark/capture_original.py --sandbox F:\SCCT-Automation\headless\Sandbox --uncap --out local/fps/original-SteeE
pwsh -NoProfile -File tools/benchmark/Invoke-OpenScct.ps1 -Exe local/fps/build/OpenSCCT.exe -Cameras local/fps/original-SteeE/cameras.json -Out local/fps/openscct-SteeE
python tools/benchmark/compare.py local/fps/original-SteeE/summary.json local/fps/openscct-SteeE/summary.json --require-parity
```

For another map use `--map AquaE --spawn-views` on the original capture and `-Map AquaE` on the OpenSCCT run.
`--width` and `--height` select the original resolution; OpenSCCT reads it from the recorded cameras.
`--side merc` / `-Side merc` select the other side. The default SteeE poses are `spy-spawn,shop,courtyard,crane`;
`--only spy-spawn` selects one. Do not run another game, a build or a profiler during throughput captures.
`--spawn-current --gameplay --width 1920 --height 1080` captures the natural spawn view without teleporting or
turning; replay it with `-Gameplay`. The OpenSCCT result records the pawn's distance from the reference spawn.

Default captures hide both the HUD and the local character in both engines. Simulation, map actors, effects,
animation and sound still run. Invisible HUD draw-list work and invisible character posing are skipped. This
isolates rendering; it is not a combat or multiplayer benchmark. Add `--gameplay` and `-Gameplay` to include the
HUD and character (use the side's spawn pose so the character is in the same place). `-Bots 3` adds an OpenSCCT-only
stress capture; the original's editor Play Level has no equivalent bots, so keep that result separate.

Ten seconds per camera follow a three-second warm-up by default. The original settles its camera at 240 FPS before
raising the runtime cap for measurement. Its level clock deltas provide frame times; an independent Present counter
provides throughput and detects missed observations. Its engine-clock FPS can differ slightly from wall-clock FPS,
so comparisons use `presentFps`. OpenSCCT measures wall-clock frame intervals internally. “1% low” is 1,000 divided
by the mean of the slowest 1% of frame times, rounded up to at least one frame. OpenSCCT also records simulation,
view, allocations and GC pause. Add `-Profiling` for main-viewport render CPU/GPU timing (timestamp/readback overhead
can affect throughput); that GPU time excludes the auxiliary sky and shadow viewports. Screenshots and CSV writes happen outside the measured interval. The runner rejects logs
with engine or shader errors. Check screenshots and camera drift as well as the FPS table.
Every measured frame must match the requested camera position, rotation and FOV; mismatches now fail the run.
Results include the actual camera and distance from its reference. First-person records (`behindView:0`) are valid
camera requests too. Older builds incorrectly used that flag as camera validity and could benchmark a frozen spawn
view; reject those runs when their screenshots do not show the requested views.
The comparison's `--require-parity` requires both average FPS and 1% low parity in every recorded view. These are
background runs of a Reloaded editor Play Level and a release export; they do not establish retail foreground
performance, moving-camera behavior, combat, network load or performance on another GPU.

### GPU, settings and other processes

`-GpuIndex <n>` passes Godot's `--gpu-index <n>` to pick the adapter. The runner cannot list Godot's adapters, so
check the `gpu` name in `summary.json` to see which one ran. `runner.json` in the output folder records the
requested index, driver and settings.

`-Settings 'key=value', ...` overrides settings.json keys for that run only, through the game's repeatable
`--svm-setting=key=value` flag (keys as in the settings console, groups with a dot). Overrides are never saved: while
any are given, the game writes no settings file. A key it does not know logs an `ERROR:` line, which fails the run.

Before launch the runner waits up to `-QuietWaitSeconds` (default 120) until no other game, editor or renderer runs:
names starting `OpenSCCT`, `Godot`, `ClassicGame`, `SCCT`, `SplinterCell` or `UnrealEd`, or anything under
`F:\SCCT-Automation`. If one is still there it does not start. While the game runs it checks once a second; a
newcomer marks the run `"contaminated": true` in `runner.json`, lists it under `competitors` and prints a warning.
Discard contaminated runs. `-IgnoreCompetitors 'OpenSCCT.Launcher*'` skips names you know are idle.

Direct exported-game controls are `SVM_BENCHMARK=<cameras.json>`, `SVM_BENCHMARK_OUT=<folder>`,
`SVM_BENCHMARK_SECONDS`, `SVM_BENCHMARK_WARMUP`, `SVM_BENCHMARK_PROFILING=1`, and `SVM_BENCHMARK_GAMEPLAY=1`;
pass `--svm-play=<map>` after `--`.

## Original ledge-grab presentation

`capture_original_ledge.py` records the normal SteeE GE59/54 Jump → OnLedge → Use → pull → fall → death
sequence in the existing `SteeECombat` sandbox map. It places a standing spy below the ledge and spawns/places
the combat rig's merc above it; it does not write health, force an animation or select a death state. The original
runs hidden and muted at 60 FPS. The sandbox owns process cleanup, and the helper restores the saved editor/play
configuration. `--sandbox` and `--re-tools` override the existing automation defaults when needed.

```powershell
python -I -B tools/benchmark/capture_original_ledge.py --repo . --out local/original-ledge-capture

$env:SVM_DEATH_BONES_CAPTURES = (Resolve-Path local/original-ledge-capture/ledge-presentation.jsonl).Path
tools/ci/godot-smoke.ps1 -Tests death-bones -ExpectedFailures @()
Remove-Item Env:SVM_DEATH_BONES_CAPTURES
```

Choose a new output directory under the selected checkout's `local/`; the command refuses existing capture
directories. The JSONL preserves both players' channels, positions, health, root locks and rendered bone arrays.
The summary records input/state transition frames and screenshots. Jump must reach OnLedge within 180 sampled
frames, and death must occur within the 720-frame recording bound; after death, 180 further samples record the
settled pose. A failed fixture remains an incomplete capture with its error and cleanup report.

The merc is an original combat-rig dummy without a viewport/PlayerTick. Its ledge landing/death callbacks still
run normally. Bone samples are checked against the same simulation tick, not a strict post-Present boundary;
the hanging spy's camera can obscure the corpse in screenshots. The opt-in Godot replay separately requires
at least 120 alive `GrbdStLgRail` frames with translation lock and 120 dead `LandStXxFd2` frames without it,
with at least 99% of each group matching all bones within 1.5 uu. This checks the actual pose builder; live
network tests remain necessary to verify state delivery and the complete rendered transition.
