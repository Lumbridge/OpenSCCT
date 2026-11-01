# Overnight performance report — 1 November 2026

Completed at the 08:00 London cutoff. All five proposed improvements and the three larger CPU improvements are implemented in the working tree. No texture quality, resolution, draw distance, effects count, lighting budget or animation update frequency was reduced. Changes are uncommitted and undeployed; unrelated work was preserved.

**Measured result:** the two eligible stationary 1080p gameplay comparisons showed about **18% lower mean frame time**, with matching static HUD pixels. This is one before/after pair per role, not a general match-performance guarantee. The planned additional benchmark rounds could not start because other tests kept renderers active. A separate 30-minute run completed, but its timing assertions failed under heavy contention.

## Implemented changes

| Area | Final implementation |
|---|---|
| HUD command and text reuse | Reuses primitive/geometry storage, retains independently owned batch snapshots, caches up to 256 text layouts and redraws only exactly changed batches. Animation evaluation continues normally. |
| HUD screen copies | Copies conservative screen rectangles for compatible dodge-blend runs; transformed or unsuitable cases retain full viewport copies. |
| Particle buffers | Retains exact-size spark mesh buffers when counts match and avoids computing identical quad bounds twice. Particle simulation, geometry and shaders are unchanged. |
| Repeated presentation updates | Suppresses exactly identical bone and vision parameter submissions; maintains a geometry registry for shadow-layer changes and late attachments. Pose sampling and interpolation still run. |
| Frame statistics | Ordinary play keeps running totals rather than lifetime histories. Diagnostic callers can explicitly request exact history/percentiles. |
| Actor searches | Maintains lazy typed candidate lists while preserving actor order, subclasses, live flags, tags and insertion/removal behavior. |
| Collision candidates | Conservatively rejects distant candidates before expensive geometry updates, using live transforms rather than stale per-tick bounds; existing narrow-phase queries remain authoritative. |
| Spatial actor lighting | Uses conservative map-light candidates, then the original ordering, ranking and packing. Moved/spawned/unsafe cases retain conservative fallback behavior. This changes CPU actor-light selection, not world-light shaders. |

Main files are `MagmaRenderer.cs`, `MagmaCanvas.cs`, `HudPage.cs`, `HudMarkers.cs`, `ParticleSystemView.cs`, `QuadBuffer.cs`, new `LineBuffer.cs`, `ChannelPoseApplier.cs`, `SkeletalModel.cs`, `SimPawnView.cs`, new `PlayFrameStats.cs`, `PlayRoot.cs`, `Level.cs`, the `CollisionWorld` partials, `PrimitiveSet.cs`, `ActorLighting.cs` and new `LightInfluenceIndex.cs`. Detailed notes are in `local/performance-overnight/{hud,render,cpu}-notes.md`.

## Measured performance

Identical SteeE cameras/settings/content, 1920×1080, Vulkan, RTX 4070 SUPER, uncapped, zero bots, three-second warm-up and ten measured seconds per view. Known game/build/test/server processes were sampled during each attempt; these accepted pairs had no detected competing workload. This cannot exclude all unrelated system activity.

| Role | Mean frame time before → after | p95 before → after | FPS before → after | Allocation before → after |
|---|---:|---:|---:|---:|
| Spy | 1.0251 → 0.8423 ms (**17.8% lower**) | 1.1757 → 0.9589 ms | 975.5 → 1187.2 | 30.89 → 13.68 MB/s |
| Merc | 1.1941 → 0.9793 ms (**18.0% lower**) | 1.3311 → 1.1321 ms | 837.4 → 1021.2 | 44.60 → 26.14 MB/s |

Both results are the original second-round pairs, with optimized/reference run order reversed from the first round. Of 14 completed initial captures, seven were contended; only two complete pairs were eligible. The fresh revision-two queue waited from 04:13 until its safe-start cutoff at 07:49 and made zero attempts. There are no defensible additional environment-view or combat FPS results.

The final optimized source differs from the measured optimized source only by a new opt-in HUD fixture and its registry entries; all production source/configuration/content hashes match. The table describes that unchanged implementation, but remains limited to the two measured views. Allocation per 1,000 frames fell from 31.67 to 11.52 MB for spy and from 53.25 to 25.60 MB for merc.

Structural/synthetic evidence supports the larger CPU changes independently of FPS:

- 2,000 typed pawn queries over 10,017 actors: 29.342 ms full scan versus 0.588 ms indexed, with zero warmed indexed allocations.
- Differential collision queries avoided 9,617 of 10,042 eligible mesh refits while returning identical results. A deliberately distant-mover fixture took 0.744 ms versus 238.168 ms for eager transformation.
- Exact lighting comparisons sent 23,531 of 151,040 map candidates to the original scorer: **84.4% fewer candidate visits**.

These synthetic timings are not claimed as game-wide speedups.

## Visual, animation and correctness evidence

- Both solution builds and isolated release exports passed; final ExportRelease build had zero warnings/errors.
- **122 focused checks passed:** 40 core/index, 76 physics, three lighting and three frame-stat checks. The map-contact check initially lacked content, then passed after linking the existing content. The repository's `dotnet test` platform handshake executed zero tests; the built xUnit executables were used successfully instead.
- **1,440 HUD frame command fingerprints** matched the original across both roles and three resolutions. These are command comparisons, not 1,440 screenshot comparisons.
- Full versus regional screen-copy images matched exactly at 640×480, 1920×1080 and 3440×1440, including changing scenery and transformed-canvas fallback.
- **Nine static regions in actual spy/merc gameplay PNGs matched byte-for-byte**, covering location labels, objective counts, health, scoreboard, ammo and fire mode. Animated regions were not treated as time-aligned pixel evidence.
- **4,880 cached/uncached bone poses matched exactly** across 12 selected models and sampled clips, including first-person arms, all five sidearms, overlays, root locks, signed/zero scale, vision and attachment/layer lifecycle checks.
- **1,024 lighting outputs and 126 particle fixture images matched exactly**, across seven particle styles, three vision modes and six update states.
- Two million ordinary frame-stat additions retained no history and allocated zero warmed bytes. Reusing 1,024 HUD quads also allocated zero warmed bytes in its fixture.
- At final audit, **all 4,042 recorded content files still matched their original SHA-256 values**.

A preview-based missing-HUD diagnosis during the night was incorrect. Independent raw PNG comparisons and uniquely named previews disproved it. The temporary diagnostic removal of redraw suppression was reversed byte-for-byte; the original validated optimization remains. No HUD degradation was established by those captures.

This is strong targeted equivalence evidence, not exhaustive manual visual/gameplay certification.

## Thirty-minute observation and remaining limits

The separate three-bot SteeE observation completed **1,800 measured seconds**, 26,462 frames and 108,185 server ticks, without a crash, engine/script/shader error or timeout. It logged the same three map-render warnings seen elsewhere, including `Engine.DefaultTexture`.

Its native timing test **failed**: 25,678 frames exceeded 33.4 ms; median was 64.7 ms, p95 112.6 ms and maximum 351.6 ms. The 60/144 motion test also **failed its requested-rate checks**, observing 24.1/24.0 FPS. All 117 observation samples recorded competing workloads—14–34 processes per sample. These runs cannot establish clean frame pacing, comparative FPS or the cause of timing failures.

Across 114 samples within the measured play interval, private memory declined from 4,726,124,544 to 4,535,103,488 bytes (182.17 MiB lower), with no sustained upward trend apparent. Working-set volatility under system pressure is not interpreted as retained-memory growth. This does not prove leak freedom.

Still unverified: repeatable combat/moving-camera performance on a quiet machine, the planned environment-view comparisons and manual visual/gameplay approval. No quality settings were lowered to obtain the measurements.

A final checkout audit found later concurrent landing-state/draw-alpha instrumentation in `SimPawnView` and network smoke changes. The geometry registry and rendering/layer behavior are unchanged; the new landing fields are used by `InspectPose` diagnostics. They were preserved, but the overnight builds validate the frozen snapshot rather than every later checkout edit.

## Artifacts and reproduction

Everything is under `local/performance-overnight/` unless noted:

- `baseline-source`, `baseline-manifest.json`, `initial-status.txt`: original dirty-tree baseline, preserved.
- `reference-final-build.json`, `validation-final-build.json`, `validation-v2-build.json`: measured/final binary identities. Game DLL hashes: reference `1A72709C6D1884442740A2FA669F67166F8381E61B7F2FF88EC25D9F1F13B08C`; measured optimized `5E5ED3688B7D5C203AD964CD9B0CB17D592A7B70CBD2268E4C4F248885B45462`; final fixture-extended export `42BA500961E0718CB078E49E9A77E3388AB9BF20CD7351814BE741598A9E3057`.
- `original-study-analysis.json`, `captures/*-r2/{summary.json,build.json,contention.json,spawn-native.csv,spawn-native.png}`: accepted measurements and raw evidence.
- `hud-original-optimized-static-regions.json`, `hud-{reference,optimized}.json`, `hud-pixels/`, `presentation-all-models-clean.log`, `render-performance-parity.out.log`, `checks-*.log`: parity and correctness evidence.
- `followup-output/validation-build-v2/contended-observation/`: motion/soak logs, native verdicts and memory/process samples. `fallback-state.json` explicitly separates completion from passing assertions.
- `content-inputs.json`, `content-differences.json`: original content hashes and final zero-difference result.
- `overnight-work-log.md`: preserved chronological work log, including the corrected preview diagnosis.

Analysis commands: `python local/performance-overnight/summarize-study.py --captures captures` and `python local/performance-overnight/check-content.py`. Build commands were `dotnet build OpenSCCT.slnx`, `dotnet build game/OpenScctGame.slnx` and the latter with `-c ExportRelease`, in isolated source copies. The existing `tools/benchmark/Invoke-OpenScct.ps1` supplied matching camera/content inputs, `-Seconds 10` and `-Gameplay` for the two role views; full arguments are retained in the worker scripts and capture records.

All owned observation/capture workers have exited. No unrelated process was stopped. Overnight monitoring is finished; no deployment or commit was made.
