# FPS benchmarks

Capture the original's rendered cameras, then replay those cameras through a release export's normal listen-server
play path. Runs are sequential on the same machine, with a real renderer, uncapped FPS, vsync off, native resolution,
4x anisotropic filtering, no MSAA/SMAA, shadow quality 3 and smooth shadow edges. Player settings are isolated.
Both processes opt out of Windows background execution-speed throttling. Vulkan is the OpenSCCT default;
`-Driver d3d12` selects the other Windows backend without changing the player's configuration.
No game data or derived images belong in Git; results, camera records, binary hashes and raw frame CSVs go in `local/`.
The [31 October results](RESULTS-2026-10-31.md) include the original comparison and remaining gaps.

The original needs the existing RE+ automation tools and a complete, idle Play Level sandbox. The verified Reloaded
Core build is checked by SHA-256, its loaded path and relocated cap-selector instructions before `--uncap` writes
only the sandbox child's two cap values to 10,000. Executable code is unchanged. Other builds can use `--cap 240`,
which measures capped player performance rather than uncapped engine throughput. All edited sandbox configuration
files are restored when the capture exits. The original stays on its hidden desktop; OpenSCCT renders off screen
without taking focus or the pointer.

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
The comparison's `--require-parity` requires both average FPS and 1% low parity in every recorded view. These are
background runs of a Reloaded editor Play Level and a release export; they do not establish retail foreground
performance, moving-camera behavior, combat, network load or performance on another GPU.

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
