# Performance investigation — 1 November 2026

The best first targets are HUD allocation and redraw work, HUD screen-copy bandwidth, and retained spark geometry. These remove repeated work while keeping the same image, animation cadence and gameplay. There are also smaller, straightforward opportunities in redundant skeleton/material submissions and match-long diagnostic storage.

This is an investigation, not a performance patch. Findings describe the working tree inspected on 1 November, based on commit `a12d374` with existing uncommitted changes. No current FPS improvement or visual equivalence has been demonstrated. No game code, settings or content were changed.

## Evidence and its limits

The [31 October benchmark report](../tools/benchmark/RESULTS-2026-10-31.md) already documents substantial improvements: static shadow caching, unchanged light upload suppression, conservative skeletal bounds, retained sprite/cloth buffers, particle upload culling and unchanged audio/material updates. Those should not be proposed again as new work.

The recorded SteeE gameplay captures include the HUD and character at 1920×1080:

| Capture | Average FPS | 1% low FPS | Managed allocation rate | Cumulative GC pause over approximately 10 seconds |
|---|---:|---:|---:|---:|
| Spy | 883 | 409 | 27.76 MB/s | 17.19 ms |
| Merc | 787 | 522 | 41.27 MB/s | 19.76 ms |

Sources: `local/fps/verified-spy-1080/summary.json` and `local/fps/verified-merc-1080/summary.json`. These are historical, stationary-view measurements on the recorded i9-14900K/RTX 4070 SUPER system. Their allocation rates reflect very high, uncapped frame rates, not a fixed allocation rate at 60/144 FPS. They do not prove that garbage collection is the main bottleneck. The environment-only recordings use different resolution/views and cannot isolate the HUD's FPS cost.

The earlier `local/fps/cpu-game-report.txt` also points toward HUD command generation and native property calls. It predates some optimizations, and its inclusive sample counts are neither current timings nor percentages of an entire frame. Some other historical report filenames identify waiting worker threads, so their names alone do not establish what was profiled.

Multiple game and test processes were active during this investigation. No new throughput runs were made under that contention, and no existing process was interrupted. No builds, test suites or smokes were run for this documentation-only investigation.

## Recommended implementation order

### 1. Reuse HUD primitives and unchanged text layout

**Observation:** [HudPage.Draw](../game/Scripts/Hud/HudPage.cs) rebuilds its draw list. [MagmaRenderer.DrawImage](../game/Scripts/Hud/Magma/MagmaRenderer.cs) allocates point, UV and colour arrays and a draw item for each image; clipped polygons allocate additional arrays. [MagmaCanvas.Submit](../game/Scripts/Hud/Magma/MagmaCanvas.cs) repopulates existing batches and queues every active batch for redraw. Its text path repeatedly splits, wraps and measures unchanged strings. HUD markers also create primitives in [HudMarkers](../game/Scripts/Hud/HudMarkers.cs).

**Method:** retain primitive storage; cache text layout using all layout inputs; retain canvas commands until their inputs change. The existing canvas-node and list-row pools are useful but do not eliminate this per-frame primitive work. Start with storage reuse and text layout before introducing broader invalidation logic.

**Expected benefit:** less managed allocation, text processing and managed/native draw-command traffic. This is the strongest source-backed candidate for improving gameplay frame consistency; its FPS gain is unmeasured.

**Visual requirements:** continue every fade, animation, timer, marker and aim update at the existing cadence. Invalidate layout for text, font, dimensions, scaling, spacing and alignment changes. Preserve masks, blend order and clipping. Submitted buffers must remain valid until Godot consumes them; retained commands must not alias scratch storage that gets overwritten.

### 2. Copy only the HUD screen region a blend run reads

**Observation:** [MagmaCanvas.Batch](../game/Scripts/Hud/Magma/MagmaCanvas.cs) creates full-viewport `BackBufferCopy` nodes for Dodge blend runs. The associated shader reads the current pixel's `SCREEN_UV` with nearest filtering and no neighbouring samples.

**Method:** use a conservative, pixel-rounded rectangle enclosing each run's drawn image polygons, retaining the copy immediately before that run. Keep the full-viewport fallback where bounds cannot be established.

**Expected benefit:** lower GPU copy bandwidth, particularly at higher resolutions. The saving depends on the number and coverage of active blend runs; it is not yet measured.

**Visual requirements:** preserve run order, overlapping blends, transformed coordinates and screen-edge coverage. Keep these copies current even when the HUD geometry itself is unchanged: the scene underneath still moves. Godot supports rectangle copies, but reads outside the copied region are undefined. See the official [BackBufferCopy reference](https://docs.godotengine.org/en/stable/classes/class_backbuffercopy.html) and [screen-reading rules](https://docs.godotengine.org/en/stable/tutorials/shaders/screen-reading_shaders.html).

### 3. Retain spark mesh buffers

**Observation:** [ParticleSystemView.FillLines](../game/Scripts/Render/Particles/ParticleSystemView.cs) clears its mesh surfaces and allocates vertex, UV and colour arrays on each draw. The sprite path already has a retained [QuadBuffer](../game/Scripts/Render/Particles/QuadBuffer.cs).

**Method:** give spark lines equivalent capacity-managed storage and update existing vertex/attribute regions; rebuild only when capacity or format requires it. Godot exposes these update operations in [ArrayMesh](https://docs.godotengine.org/en/stable/classes/class_arraymesh.html).

**Expected benefit:** less allocation, mesh construction and GPU-resource churn while sparks are active. Benefit is effect-dependent.

**Visual requirements:** preserve every segment, endpoint, UV, colour, lifetime and simulation update. When the active count shrinks, unused capacity must not draw stale lines. Maintain conservative bounds and validate empty-to-active transitions.

### 4. Suppress identical native state submissions

Three localized opportunities remain:

- [ChannelPoseApplier.Apply](../game/Scripts/Views/ChannelPoseApplier.cs) submits every final bone pose every rendered frame. The simpler [Ue2PoseApplier](../game/Scripts/Render/Characters/Ue2PoseApplier.cs) already skips unchanged components. Extend exact last-submitted-value comparisons to the channel applier while continuing sampling, blending, interpolation and tween bookkeeping. Moving bones still update every frame; no approximate epsilon or reduced animation rate is needed.
- [SimPawnView.SetShadowOnly](../game/Scripts/Views/SimPawnView.cs) recursively walks the local pawn hierarchy every frame to discover new attachments. Register new geometry as it is created and apply layer changes on mode transitions. Preserve body shadows and newly equipped weapons, gadgets and cosmetics.
- [FirstPersonView.Draw](../game/Scripts/Views/FirstPersonView.cs) calls [SkeletalModel.SetVision](../game/Scripts/Render/Characters/SkeletalModel.cs) each frame, which unconditionally writes mesh shader parameters. Cache the exact value, as the third-person path already does, and initialize new/replaced meshes correctly.

**Expected benefit:** fewer managed/native calls, especially for constant bones and unchanged vision state. These are smaller opportunities; whether comparisons pay off depends on how much state actually remains unchanged. Reset caches on replacement or any external pose/state writer.

### 5. Bound ordinary gameplay's frame-history storage

**Observation:** [PlayFrameStats](../game/Scripts/Play/PlayRoot.cs) appends three doubles to growing lists every rendered frame for the lifetime of the play scene. Normal play calls this unconditionally. The sample payload alone is 24 bytes/frame: approximately 12.4 MB/hour at 144 FPS or 51.8 MB/hour at 600 FPS, before spare list capacity and temporary storage during growth.

**Method:** keep last-frame values, counters and aggregate statistics in ordinary gameplay; retain raw histories only for explicit diagnostics, or use a bounded history with clearly defined percentile semantics. Preserve the existing full-history diagnostic summary when that mode is requested.

**Expected benefit:** bounded memory and fewer large allocation/copy events during long sessions. This is a long-session reliability and hitch-prevention improvement, not a claim of a large average-FPS gain. It has no rendered-image dependency.

## Additional candidates after profiling

| Candidate | Concrete evidence | Constraint |
|---|---|---|
| Separate animated-alpha shadow surfaces from fixed surfaces | [MapBuilder.AddCellMesh](../game/Scripts/Render/MapBuilder.cs) marks a whole multi-surface cell as dynamic-shadow geometry if any surface has animated masked alpha. Current content has eligible geometry in ClubE, TermD and ClarD; inspection found no qualifying animated masked materials in AquaE, SteeE or FactD. | Map-dependent: this does not explain the existing Aquarium/Steel Squat results. Quantify mixed cells and added draw calls first. Preserve geometry/materials, animated masks and cache invalidation. |
| Reuse computed particle bounds | [ParticleSystemView](../game/Scripts/Render/Particles/ParticleSystemView.cs) calculates exact quad bounds for culling, then [QuadBuffer](../game/Scripts/Render/Particles/QuadBuffer.cs) calculates them again during upload. | Pass the same AABB through; retain the earlier conservative check that avoids generating invisible sprite geometry. Likely a small saving. |
| Reuse actor-light selection output | [ActorLighting.Select](../game/Scripts/Render/ActorLighting.cs) allocates an `ActorLightSet` plus arrays on each selection. | Give each consumer retained output storage; avoid sharing mutable results across consumers. Preserve current light selection and modulation. |
| Select only the required top four lights | Both `ActorLighting` and [AiLightField](../src/OpenScct.Game/AI/AiLightField.cs) sort all eligible lights before using four. | Resolve equal-score behaviour first: the renderer's `List.Sort` is unstable despite its comment about map-order ties. A new selection algorithm can change visible light choice. Preserve sunlight priority and contribution order. |
| Avoid provably unnecessary AI torch rays | [AiWorld](../src/OpenScct.Game/AI/AiWorld.cs) raycasts before calculating whether a torch can contribute to the target point. | Evaluate the identical geometric contribution first and raycast every potentially nonzero contribution. Keep sight frequency and body samples unchanged. |
| Typed actor candidate lists | [Level.AllActors/DynamicActors](../src/OpenScct.Core/Level.cs) and [ActorIterator](../src/OpenScct.Core/ActorIterator.cs) scan the complete actor list for repeated typed queries. | Preserve iteration order, subclass matching, spawn/destroy visibility and live filtering. Existing iterators already avoid allocation. |
| Dynamic collision broadphase | [CollisionWorld.Query](../src/OpenScct.Physics/Collision/CollisionWorld.Query.cs) and [CollisionWorld.Actors](../src/OpenScct.Physics/Collision/CollisionWorld.Actors.cs) scan dynamic colliders/cylinders despite having a static-geometry BVH. | Only pursue if combat/bot traces dominate. Bounds must follow movement within the same tick, teleports, crouch and prediction/rewind. Retain exact collision tests and hit ordering. |

Conservative per-cell light candidate lists could also reduce the all-light fragment loop in `svm_world.gdshaderinc`. This is a larger renderer change: exclude only lights guaranteed to contribute zero, retain order and account for moving/spawned lights. It belongs after full GPU profiling.

## How to establish a benefit without a visual regression

1. Capture an isolated release baseline from the actual working tree, recording binary/content hashes and settings. Use the existing [benchmark runner](../tools/benchmark/README.md). Collect throughput with profiling disabled and diagnose separately, since instrumentation adds overhead.
2. Measure visible spy and merc gameplay, Aquarium and Steel Squat, then moving-camera/combat/bot cases. Include reloads, animated materials, smoke/sparks, moving shadows, different vision modes, changing HUD text and overlapping HUD blends. Existing stationary environment shots alone are insufficient.
3. Record median/p99/worst frame times, 1% lows, allocations, GC pauses and CPU/GPU costs. Keep resolution, textures, filtering, shadow resolution, effects, animation cadence and simulation/network rates fixed. Add a longer session for the frame-history change.
4. Address instrumentation gaps: [FpsBenchmark](../game/Scripts/Diagnostics/FpsBenchmark.cs) measures only the main viewport's render CPU/GPU timings when profiling is enabled; shadow and sky work need separate coverage. `LastSimMs` does not measure the complete threaded server tick. `LastViewMs` excludes independently processed HUD work. Zero GPU values with profiling disabled mean “not measured,” not “free.”
5. Compare matched images at identical simulation/animation times, plus motion sequences for interpolation, flicker and popping. Use reproducible particle seeds where possible. A higher average FPS does not compensate for worse frame-time tails or an image/motion regression.

Cold first-use stutter is a separate measurement from warmed throughput. Godot already performs automatic pipeline precompilation when it sees the required resources; first correlate hitches with compilation counters before adding resource warm-up. The project already samples those counters in `PlayHitchSmoke`. See Godot's [pipeline compilation guidance](https://docs.godotengine.org/en/stable/tutorials/performance/pipeline_compilations.html). This investigation did not run that smoke.

Keep resolution, effects and animation quality intact. The previous report records rejected experiments in occlusion, batch sizing, animation-key caching, worker settings and server GC, including a 90 ms worst-frame result for server GC. Those are not established wins to reapply. Start with one narrowly scoped change from the ranked list and measure its effect before combining changes.
