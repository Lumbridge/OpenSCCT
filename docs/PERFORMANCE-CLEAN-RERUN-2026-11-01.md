# Clean performance rerun — 1 November 2026

The rerun confirms a useful gain in the tested scenes. With the corrected input driver, uncapped moving-camera combat averages **1.330 → 1.213 ms per frame: 8.76% less frame time, or 9.60% more throughput**. Balanced stationary rounds show **19.5% and 20.2% lower frame time** in Spy and Merc HUD views, **5.0–6.2%** in AquaE views, and approximately flat results in SteeE environment-only views.

Controlled visual and pose comparisons passed, and no confirmed visual degradation was found in the inspected gameplay captures. This is bounded validation, **not a certification of every visual or animation-cadence case**. Native strict camera assertions still fail: detailed analysis identifies tiny integer-rotation residuals and a short sub-unit synthetic-input burst. They remain recorded rather than being reclassified as passes.

All six final combat timing runs passed without a detected competing renderer/build/test workload. Earlier combat series exposed excessive synthetic-input finalization in the test driver; they remain diagnostic history and are not the primary normal-gameplay pacing result. No production code was changed during this rerun.

## Final combat measurements

Each row is one fresh baseline/optimized pair: five seconds warmup and 60 measured seconds, Merc on SteeE, zero bots, 1920×1080 Vulkan, turning and strafing while firing and reloading. No screenshot readbacks or external profiler run during these windows. Graphics settings match; no quality or animation-rate setting was reduced.

| Frame cap | Mean frame ms, before → after | p99 ms, before → after | p99.9 ms, before → after | Worst frame ms, before → after |
|---|---:|---:|---:|---:|
| Uncapped | 1.3296 → 1.2131 | 1.702 → 1.650 | 3.543 → 4.422 | 7.543 → 7.593 |
| 60 FPS | 16.6654 → 16.6653 | 17.117 → 17.102 | 22.449 → 22.683 | 23.145 → 23.630 |
| 144 FPS | 6.9437 → 6.9437 | 7.364 → 7.252 | 12.640 → 12.435 | 13.062 → 14.403 |

Uncapped throughput is **752.11 → 824.34 FPS**. Capped means remain at the requested rates. The slightly longer maxima prevent claiming that every pacing statistic improved.

| Pacing observation | Baseline → optimized |
|---|---:|
| Uncapped frames above 16.667 ms | 0 → 0 |
| Cap 60 frames above 20 ms | 8 → 5 |
| Cap 60 frames above 33.333 ms | 0 → 0 |
| Cap 144 frames above 110% of budget (7.639 ms) | 27 → 17 |
| Cap 144 frames above 2× budget (13.889 ms) | 0 → 1 |
| Cap 144 frames above 16.667 ms | 0 → 0 |

The single optimized cap 144 frame above twice budget coincides with GC. Uncapped allocation falls **31.85 → 15.83 MiB/s**, collection count **107 → 53**, and total recorded GC pause **207.73 → 184.33 ms** over the minute. The earlier large synthetic-input pause pattern does not recur.

**The deeper tail remains worse in the uncapped sample:** p99.9 is **3.543 → 4.422 ms**. Mean recorded collection pause is **1.941 → 3.478 ms**, with medians **1.901 → 3.454 ms**. Collections remain fewer but longer after correcting the driver. Mean throughput and p99 improve, while this deeper tail and the maxima do not. No claim of uniformly better pacing is justified.

All six final runs observed **84 client and replicated shots, 12 completed reloads, 168 muzzle meshes, 84 casings and 84 impact marks**, with a peak of two world lights. Every sent synthetic input event was disposed. Movement, camera-turn and combat assertions remain enabled. Independent wall-clock input delivery means trajectories, tracer counts and interpolation phases need not be identical.

Observed action cadence also matches at the tested resolution: median within-burst shot intervals are **200.058 → 200.074 ms** uncapped, **199.991 → 200.045 ms** at 60 and **201.369 → 201.346 ms** at 144. Each calculation uses 60 intervals after excluding the first shot of each burst. Median reload durations remain about 2,000 ms in both variants (largest paired median difference 0.325 ms). These are live state observations with frame/tick quantization, not an assertion of identical timestamps.

A minute per variant/cap is a bounded sample, not a statistical guarantee about rare hitches or arbitrary matches. Measurements are application frame intervals, not display scanout or end-to-end latency. Complete budgets, phases, GC, allocation and action timing are in `final-analysis.json/.md` and `v3-combat-gc-analysis.json/.md` under the artifact root below.

## Stationary throughput

Ten viewpoints were measured twice per variant, A/B then B/A: 40 individual view measurements, each ten seconds after three seconds warmup. Role views use 1920×1080; map-camera views use 1280×720. Paired runs receive equal weight.

| Workload | Mean frame time, before → after | Reduction |
|---|---:|---:|
| Spy HUD view | 1.0838 → 0.8723 ms | 19.52% |
| Merc HUD view | 1.2478 → 0.9953 ms | 20.24% |
| AquaE, four directions | Per-view table in detailed analysis | 5.05–6.20% |
| SteeE, four environment views | Per-view table in detailed analysis | −1.27% to +2.47% |

The slightly slower SteeE views have paired ranges crossing zero. Role-view draw-call and primitive counts match. These gains belong to the combined implementation; they are not isolated attribution to each optimization. Zero-bot scenes do not establish multiplayer scaling gains.

## Visual and animation evidence

- **40,144 exact cached/uncached pose comparisons** passed across all 772 available clips in 11 animated assets plus the known static M160 attachment. Coverage includes repeated poses, 32 continuous samples per clip, signed/zero scale, material/vision changes, and attachment/reparent/reentry handling. First-person rifle/SMG/shotgun owners and all five sidearms were included. This validates sampled pose output, not every live transition.
- **126 exact particle-image comparisons** passed with identical moving cameras, seven styles, normal/thermal/EMF vision and growth/shrink/empty/reappearance cases. **1,024 exact actor-light results** matched the full scan, using 23,531 indexed candidates versus 151,040 full-scan candidates.
- HUD full/regional-copy comparisons were pixel-identical at 640×480,1920×1080 and 3440×1440, with a changing backdrop and transformed fallback. Live checks passed **417 exact image comparisons per role** over 420 submitted frames. Both roles moved and turned; Merc fired four shots and reloaded. Both comparison canvases retain draw commands, so this particular fixture isolates bounded screen copies. Separate ownership, text-layout, invalidation and asset-replacement assertions cover retained HUD work;1,024 warmed quads allocate zero managed bytes.
- Repaired native combat/FX checks passed in both exports: ammunition, muzzle flashes, held fire, rate-of-fire changes, reload, scope/zoom, frag throwing, casings, marks, sparks, mine placement, grenade trail, explosion, smoke and world lights. Original wall-facing pilot setup failures remain preserved; the repair only fixes a fixture attempt to interact with a plain wall.
- The initial review raw-compared 66 PNGs in 33 pairs and visually inspected 24 representative images. The zoomed-scope pair is byte-identical across all 1920×1080 RGB pixels. The six v3 fire/reload/movement images were also inspected: expected arms, rifle, HUD and FX are present. Both v3 quality runs passed with 26 shots, four reloads, 52 muzzle meshes and **1,894 sent/disposed events each**, using `FramePostDraw` for screenshots. Fire/reload scoreboard glyph masks match exactly.

No confirmed visual regression was found. One earlier moving/firing pair has different rifle illumination; both have a live muzzle flash but differ in tick, interpolation fraction and the existing lighting-refresh phase. Both rifles are lit in the v3 movement pair. Separate wall-clock screenshots cannot establish whole-scene identity. Older FX screenshot labels sometimes capture the same GPU frame, establishing effect presence rather than exact effect age. The preview tool also omitted HUD text present in byte-identical raw PNGs; direct glyph/pixel checks ruled out that apparent regression.

See `visual-review.md`, `visual-review-v3-evidence.json` and image manifests for exact coverage. Live combat/FX images cover Merc on SteeE; other maps, Spy effects and arbitrary transitions are not comprehensively certified. Quality-run timings are excluded.

## Strict motion assertions and their interpretation

Short frame-rate checks passed in both variants at 60 and 144 FPS. The longer strict motion diagnostic does **not** have an overall pass. Original camera-network setup became stuck in both builds; fresh core-only runs excluded those phases while retaining look, mouse-latency, zoom, scope and viewmodel assertions.

| Final v3 diagnostic | Baseline | Optimized |
|---|---|---|
| Uncapped steady turn | 0 held frames /488 | 7 held /627; native failure |
| Uncapped full 48-unit impulse completion | One sample completes 12 frames /16.6 ms later; native failure | All six immediate |
| Matched cap 400 steady turn | 0 held /379 | 0 held /377 |
| Matched cap 400 full 48-unit completion | Two late samples, up to 7 frames /17.6 ms; native failure | One late sample, 6 frames /15.0 ms; native failure |
| Matched cap 400 zoom-in/out | 0 held frames | 0 held frames |
| Matched cap 400 scope animation in/out | 99 clip frames each, 0 held | 99 clip frames each, 0 held |

**Every flagged late impulse already drew 47 of 48 expected rotation units in its delivery frame (97.92%).** The final one-unit residual, approximately **0.005493°**, arrived a simulation tick later. The reported 15–18 ms values describe exact completion of that tiny remainder, not a delay to the entire motion. Native FAIL statuses remain unchanged.

The optimized uncapped case has seven consecutive held frames during a 0.05–0.06 pixel input burst, each below one angular unit; none shares a GC frame. This is consistent with integer-rotation quantization of the synthetic input. The baseline has no such burst in its smaller uncapped sample, and neither variant holds during the matched 400 turn. These observations explain the threshold behavior more precisely but do not prove every possible real-input cadence case.

At cap 400, actual rates are about 402/400 FPS and sight variation is 1.40/1.41 canvas units. Earlier cap 60 and 144 core runs also failed strict completion assertions; both cap 600 runs passed but the baseline reached only about 491 FPS, so that was not matched-rate evidence. Tests were never retried merely to obtain a pass, and thresholds were not relaxed.

## Test-driver correction and profiling

V1/v2 showed fewer but longer optimized GC pauses, including uncapped frames above 16.667 ms and cap 60 frames above 33.333 ms. Separate matched-cap 60 EventPipe traces found about 92% of finalized objects were synthetic input events. Held-key resolution scanned bindings and constructed unused native validation events every rendered frame; reduced ordinary allocation allowed larger batches to accumulate between collections.

V3 changes only two common diagnostic files in isolated snapshots: held keys are submitted on transitions, and owned/validation input events are disposed. It preserves the input-delivery path, timeline and assertions. Both rendered quality checks passed before timing; all six final timing runs pass with sent/disposed counters matching. Production input, rendering, animation and performance code were not changed.

Trace diagnosis has a material limitation: parsers encounter EOF near shutdown. Only the decoded `[0,58)`-second prefix is analyzed, with `parseSuccess=false` preserved and no reported lost events in the prefix. No full successful decode, retaining-path analysis or production leak finding is claimed. Profile-inflated timings are excluded. Exact commands/hashes and the corrected-run follow-up are in `profiling-v2/gc-cap60-20261101-115632/profiling-analysis.md/.json`.

## Comparison, isolation and provenance

Artifact root: `C:/Users/ryans/source/repos/SVM-Rewrite/local/performance-clean-2026-11-01-1030/`.

The optimized source was frozen at 10:31:48 UTC from HEAD `a12d374bef8ed2ec37c869391dd06c30f3466c88` plus existing local changes. The control reverses only the eight performance improvements: HUD reuse, bounded copies, retained spark buffers, identical submission suppression, bounded normal-play frame history, actor searches, collision candidates and spatial lights. Its inverse patch covers 16 modified production files and three added runtime files, with three control-only smoke-compilation adjustments. Current combat/network fixes remain common. `control-diff-notes.md`, source manifests and build identities document the comparison.

Hardware: Core i9-14900K, RTX 4070 SUPER, Windows, Godot 4.7.2, .NET 10.0.13. VSync off, resolution scale 1, shadow quality 3, edge smoothing on, post quality 1, MSAA/SMAA off and filtering 4 are common. Game windows render off-screen without taking input focus.

The runner requires 45 quiet seconds initially and after interference, then samples known game/renderer/build/test/server processes every second. Contaminated attempts are rejected and retained; only owned processes are stopped. Accepted timings have no detected competing workload. Background OS/browser/GPU activity is not claimed absent. All 4,042 shared/AquaE/SteeE content files match before/after every completed stage; executable/package/managed-library bytes are also checked around attempts. Readbacks, profiling and offline analysis stay outside throughput windows.

| Export series | Baseline OpenScctGame.dll SHA256 | Optimized SHA256 |
|---|---|---|
| V1: static/initial checks | `6BC615FC2EFE2A56694BB6BE32A182B7EB6308E01E0B50929F3306F270AAB366` | `127A07A9F7C6841EB4A06D150C43372DA75331B3FF7BD090CC561DDE09DE6175` |
| V2: expanded/repaired quality | `8BF9CAA18FC6314CCC8CBA68AA57614905A5C77B408B3650F0FE9510C8E93363` | `0A7C7369C15CD2FF4FACAE1B88778CD3DF88293CAA2A115F5988EA972A0D202B` |
| V3: final combat | `57E0549D818521BFBE448873654CCA43ED07298C4BA8F0815C2A69394B08AF1D` | `836453C75188A0AAE72930380FF1E89AE8C6BAAA0AEF9B18F99AD908B94AE9C4` |

V2/V3 modify diagnostic fixtures only. Both final variants passed Debug and ExportRelease builds/import/export with zero build warnings/errors. A stale initial incremental export was detected and replaced before measurement; fresh RID/export DLL hashes are checked. Preparation failures, retries and commands remain in `report-worklog.md` and build/export evidence.

The ten selected state files contain **75 attempts: 52 accepted, 23 excluded (four contaminated and 19 native failures)**. Older interrupted batches and duplicate directories are not pooled into averages. Selected files are `state-all.json`, `state-static.json`, `state-quality.json`, `state-combatquality-v2.json`, `state-coremotion-v2.json`, `state-combat-v2.json`, `state-coremotion-matched400.json`, `state-combatquality-v3.json`, `state-coremotion-v3.json` and `state-combat-v3.json`; their hashes are in the detailed analysis.

## Commands and other checks

From `C:/Users/ryans/source/repos/SVM-Rewrite`, retained scripts record the setup and runs:

```powershell
pwsh -NoProfile -File local/performance-clean-2026-11-01-1030/run-clean-study.ps1 -Stage All -Execute -CombatSeconds 60 -QuietWaitSeconds 600
pwsh -NoProfile -File local/performance-clean-2026-11-01-1030/run-v2.ps1
pwsh -NoProfile -File local/performance-clean-2026-11-01-1030/run-final-checks.ps1
python local/performance-clean-2026-11-01-1030/prepare-v3.py
pwsh -NoProfile -File local/performance-clean-2026-11-01-1030/build-v3.ps1
pwsh -NoProfile -File local/performance-clean-2026-11-01-1030/run-v3.ps1
```

`build-v3.ps1` runs `dotnet build game/OpenScctGame.slnx -c Debug --nologo` and `dotnet build game/OpenScctGame.slnx -c ExportRelease --nologo` in each isolated tree, followed by pinned-engine bounded headless import and `--export-release "Windows Desktop"`. `run-v3.ps1` requires both image-quality runs and disposal counters to pass, then runs fresh core-motion processes at 0/400 and combat at 0/60/144. Exact attempt arguments/environment, preflight, timestamps, logs, CSVs, results and hashes accompany every capture.

103 core tests, 33 focused collision/query/primitive tests, three spatial-light tests and three frame-history tests passed. The broad physics suite passed 187/188: its synthetic 1,000-box-sweep timing assertion exceeded a fixed 20 ms threshold at 22.05 ms, then 20.66 ms on an isolated rerun. Failures remain recorded. A Microsoft.Testing.Platform handshake initially executed zero tests and is not counted; built xUnit executables were invoked directly. Exact filters/commands are retained in the worklog.

Concurrent unrelated input/controller/UI edits after the source freeze were preserved and are outside these exports. A final read-only audit confirms **all 19 performance-owned production files still match the frozen optimized-v3 source**; see `visual-review-v3-main-drift.json`. Cleanup evidence records normal runner exit, restored v3 overrides matching their original hash, and no remaining game renderer. No production edits, commits, publication or deployment occurred in this rerun.

The remaining limits are broader live-play visual coverage and exact camera/input threshold behavior across rates. This evidence supports the measured gains and sampled visual/pose equivalence; it does not promise exhaustive equality of every frame, input path or animation transition.

Detailed evidence: [measurement analysis](C:/Users/ryans/source/repos/SVM-Rewrite/local/performance-clean-2026-11-01-1030/final-analysis.md), [GC and action cadence](C:/Users/ryans/source/repos/SVM-Rewrite/local/performance-clean-2026-11-01-1030/v3-combat-gc-analysis.md), and [visual review](C:/Users/ryans/source/repos/SVM-Rewrite/local/performance-clean-2026-11-01-1030/visual-review.md).
