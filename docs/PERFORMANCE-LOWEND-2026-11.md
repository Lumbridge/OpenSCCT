# Low-end performance — November 2026

Work on branch `perf/low-end` towards running OpenSCCT as well as the original on integrated graphics.

**Reference:** Reloaded (RE+) at its default settings on this PC's Intel UHD 770 at native resolution (2560×1440). It holds its 240 FPS cap on SteeE and averages 226 FPS on AquaE (1080p captures; its 1% low there is 59 FPS). It loads a map in 0.3–0.4 s (editor load timings).

**Target:** at least 240 FPS mean and 1% low at 2560×1440 on the UHD 770 with Reloaded's default look, and a cold map load as fast as Reloaded's.

## How it is measured

- `tools/benchmark/Invoke-OpenScct.ps1` runs an exported build against a recorded camera:
  - `-GpuIndex 1` selects the UHD 770 (the RTX 4070 SUPER drives the displays).
  - `-Settings key=value` overrides game settings for one run without saving them.
  - `-EngineArgs` passes engine options such as `--gpu-profile`.
  - `-ProjectOverrides section/key=value` writes project settings into the run's `override.cfg`.
  - The runner waits for, and records, any other game or renderer that is running.
- `summary.json` holds:
  - the GPU time of the main view and of every extra viewport (sky, spot shadows, flare faces)
  - memory: VRAM, private bytes, GC
  - pipeline compilations
- `--gpu-profile` prints Godot's per-stage GPU times. Stages with the same name in different viewports are summed.
- Every play load prints `SVM_LOAD <phase> ms= total= pipelines= texmem= ws=` lines (`Diagnostics/LoadTimer.cs`).

## Results so far (UHD 770, 2560×1440, gameplay cameras)

| Build | SteeE FPS | AquaE FPS | SteeE GPU ms | AquaE GPU ms | Load (s) |
|---|---:|---:|---:|---:|---:|
| `main` (base) | 23.3 | 14.1 | 39.2 | 66.9 | 20.0 |
| Phase 1: load fixes | 23.5 | 14.2 | 39.0 | 66.6 | 8.8 |
| Phase 2: shader and viewport cuts | 42.4 | 25.1 | 20.1 | 36.0 | 6.4–7.5 |
| Plain depth pre-pass | 56.1 | 32.2 | 14.3 | 27.2 | 6.8–7.7 |
| Sky zone in the main view | 66.8 | 32.2 | 11.4 | 27.1 | |
| Light reach, portal-fog switch and uniform fog rows | 77.2 | 34.6 | 9.4 | 25.0 | |
| Solid sky at default render priority | 86.9 | 34.7 | 8.0 | 24.9 | |
| No cosmetic code on map materials, normal-vision twins | 93.7 | 36.3 | 7.1 | 23.6 | 8.7–14.7 (first run after shader changes) |

Load times include one-off pipeline compiles on the first run after a shader change (up to 3.4 s, in `warmup.frame1`).

### Phase 1: cold load

- **The 7.4 s regression from commit `4d0419a4`.** It came from `PreloadSidearms`, which built 13 skeletal templates on every load. Now only the templates the match needs are preloaded.
- **Shared caches across the process:**
  - character textures, with decoded images freed after upload
  - shaders (one `Shader` per generated code) and compute SPIR-V
  - static collision, built once per map instead of three or four times
- **Faster loads:**
  - The bot navigation cache is found from the manifest before any scratch world loads.
  - The server tick loop uses a high-resolution waitable timer instead of spinning.

### Phase 2: GPU work removed with the same image

- Responsive smoke is compiled into shaders only when the match uses it. Before, its raymarch sat in every world, particle and effect shader and roughly halved shading speed on Intel.
- Sky zones fogged solid (AquaE, GhosS, OrphD) are drawn once into a 16×16 target and kept.
- **Spot lights:** each slot has a bounding sphere, with a bit mask of the slots that are on. Live shadow passes are skipped when nothing but static world is in the light's view.
- **Portal fog:** per-candidate partner lists, and fog mode 0 when no tile has a candidate.
- Coronas copy only their screen rectangles, not the whole colour and depth buffers.
- **Load:**
  - The effects library is read on a worker thread.
  - Map documents are read in parallel, meshes decoded in parallel, and cell buckets merged in parallel.

### Plain depth pre-pass

Godot's Forward+ depth pre-pass ran our whole world shader a second time. Every world material:

- wrote the stencil
- wrote `POSITION`
- carried the thermal/EMF cut-out `discard`s

Any of these stops Godot from using its shared depth-only shader (`uses_shared_shadow_material` in `scene_shader_forward_clustered.h`).

The fix:

- Merged map geometry whose actors are fully visible in thermal and EMF now gets one-sided opaque materials compiled without those (`SVM_PLAIN_DEPTH`, `MaterialCompiler.GetPlain`). The cut-outs cannot fire there, so the pixels are the same.
- Stencil marks are written only on the 14 of 85 maps whose projectors read them.

Pre-pass time: SteeE 5.5 → 1.7 ms, AquaE 10.5 → 2.3 ms.

Turning the pre-pass off entirely is slower: SteeE 42 → 33 FPS, because overdraw then runs the full shader.

## Where the time goes now

Godot GPU profile after the plain-depth change, in ms:

| Stage | SteeE | AquaE |
|---|---:|---:|
| Depth pre-pass | 1.7 | 2.3 |
| Opaque pass | 8.3 | 15.8 |
| Transparent pass | 0.2 | 1.8 |
| Compositor effects (display post, glow, portal fog) | 1.7 | 3.1 + 0.8 |
| Tonemap | 1.6 | 1.9 |
| Sky viewport (part of the above) | 3.4 | 0 |

To find the opaque-pass cost of each world-shader feature, a scratch build stubbed each one out. Columns are SteeE / AquaE in ms:

| Feature | Saved when removed | Of which: runtime work | Of which: code in the shader |
|---|---:|---:|---:|
| Spot lights | 2.7 / 7.3 | 0.7 / 2.6 | 2.0 / 4.7 |
| Dynamic lights | 1.6 / 3.9 | 1.1 / 3.5 | 0.5 / 0.5 |
| Surface fog | 1.0 / 5.1 | — | — |
| Flare lights | 0.3 / 0.3 | — | — |
| All four | 5.0 / 13.4 | | |

"Runtime work" was measured by compiling the code in but never running it.

The spot code costs more by being in the shader than by running. Intel shades fewer pixels at once when a shader holds more registers. The soft-shadow loop is not the cause: unrolled or not, and with or without edge smoothing, the time is the same.

## Sky zone, light reach, fog and vision twins

All exact: golden-camera shots on SteeE, MounE, PolaE and AquaE match the earlier build within run-to-run noise (animated
signs, rain, panning clouds).

- **Sky zone in the main view.** On maps whose sky zone is not fogged solid, the main camera draws the sky meshes itself
  (SVM_SKY_ZONE: eye at the SkyZoneInfo, depth squeezed behind the world). The second full-resolution render is gone.
  Solid sky keeps Godot's default render priority: at the lowest priority it drew first in the depth pre-pass, before
  anything could hide it (pre-pass 2.6 → 1.2 ms on SteeE).
- **Light reach.** Merged cells swap between full materials and twins without the spot code (SVM_NO_SPOT) or without
  both spot and dynamic light code (SVM_NO_DYN) where no such light can reach their world box. On SteeE 239 of 267
  meshes drop the spot code. On AquaE four large spot spheres reach nearly everything, so it gains little there.
- **Portal fog.** Compiled only on maps that use it (SVM_PORTAL_FOG); the main view reads its per-frame fog rows from
  global uniforms instead of a chain of dependent texture fetches per pixel (AquaE opaque 15.8 → 13.9 ms).
- **Cosmetics and vision.** Only character and skin compilers carry the camo/tint/sheen code (SVM_COSMETIC); merged
  cells draw normal-vision twins without the thermal and EMF code outside those modes (SVM_NORMAL_VISION). Warm-up
  frames for the twins draw under a black loading cover.
- **Bug fixed on the way:** the light-beam shader had not compiled since 0.8.105 (svm_spots redefined by the smoke
  include), so no map drew light beams.

## The ceiling of Godot's renderer

Scratch builds on the same cameras, UHD 770 at 2560×1440:

| Run | SteeE FPS | AquaE FPS | SteeE GPU ms | AquaE GPU ms |
|---|---:|---:|---:|---:|
| Current build (Forward+) | 86.9 | 34.7 | 8.0 | 24.9 |
| Every world material texture × lightmap only | 115.6 | 63.9 | 5.1 | 11.9 |
| Same, no compositor effects | 144.4 | 78.9 | 3.5 | 8.9 |
| Mobile renderer, current materials | 79.6 | 27.9 | 9.1 | 32.1 |
| Mobile renderer, texture × lightmap, no effects | 130.3 | 96.3 | 4.3 | 6.8 |

With shading as simple as the original's, Godot's Forward+ frame still costs about 3.5 ms of GPU on SteeE: depth
pre-pass 1.1, opaque 1.1, screen copy 1.0, depth copy 0.5, tonemap 0.85, and the frame takes 6.9 ms in all. AquaE adds
four spot-shadow SubViewport renders (3.8 ms, each with its own tonemap and copies; kept live by gameplay objects on
the World layer) and 1.8 ms of rain. The original draws straight into an 8-bit back buffer with none of these passes,
so matching it needs our own 3D renderer on Godot's RenderingDevice, with Godot keeping UI, audio and input.

## Next

- **Sky zone in the main pass.** Draw the sky zone in the main view instead of a second full-resolution render (3.4 ms on SteeE; 67 of 86 maps have a sky zone).
- **Light reach variants.** Choose per map mesh, on the CPU each frame:
  - A mesh that no spot light's bounding sphere reaches gets a material compiled without the spot code. It adds exactly nothing there, so the pixels are the same.
  - The same applies to dynamic lights.
- **Portal fog:** reduce its per-pixel cost on fogged maps.
- **Display post:** fuse the fog pass into the glow bright pass, saving one full-screen read and write.
- **Integrated-graphics profile (Phase 3):** OpenSCCT-only extras off, and FSR or texture detail as options.
- **Load:** the remaining time is pipeline warm-up, outfits, effects and map merging, about 6–7 s against the original's 0.3–0.4 s. Closing that gap needs pre-baked map data and GPU-ready (BC) textures.
