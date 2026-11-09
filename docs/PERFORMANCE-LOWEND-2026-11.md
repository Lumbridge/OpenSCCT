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

## A renderer of our own (RenderingDevice prototype, 2026-11-09)

To find out whether our own renderer on Godot's RenderingDevice would beat Forward+, we built a prototype: `perf/rd-proto`, `game/Scripts/Render/RdProto/`, switched on with `SVM_RD_PROTO=1`. It draws only the static map cells, as texture × (lightmap + vertex light). That is the same shading as the "trivial" Forward+ ceiling run. Godot's 3D is turned off; the image is shown under the HUD through a `Texture2DRD`.

| UHD 770, 2560×1440 | SteeE | AquaE |
|---|---|---|
| Forward+, trivial shading, no post | 144.4 FPS | 78.9 FPS |
| Prototype, same shading | 196–215 FPS | 203 FPS |
| Prototype, GPU time of the world pass | 0.94–1.2 ms | 0.98 ms |
| Prototype, frame time | 4.65–5.1 ms | 4.9 ms |
| Prototype at 640×360 | | 1049 FPS (0.95 ms) |

- The picture is correct: winding, orientation and depth all match Forward+.
- Recording the draw list costs 0.03–0.06 ms of CPU.
- Main-thread game work is small: simulation 0.17 ms, views 0.33 ms.
- With RenderingDevice, the world pass is several times cheaper than Forward+ for the same shading. Forward+'s fixed passes (pre-pass, tonemap, copies) are gone.

## The presentation floor on this PC

The prototype's frame (4.9 ms) is much longer than its timed GPU work (about 1.6 ms, world plus canvas). The Intel 3D engine is still about 94% busy (Windows GPU engine counters). So the rest is untimed GPU work, and it scales with the window's pixel count.

The cause is how this PC is wired: **both monitors are connected to the RTX 4070**. Every frame the UHD 770 renders has to be copied across to the NVIDIA card before it can be shown, and that copy runs on the Intel GPU. Measured with a blank window (one clear per frame, nothing else), off-screen, 2560×1440:

| Blank window, 2560×1440 | FPS | ms per frame |
|---|---|---|
| Native D3D11, flip model with tearing, UHD 770 | 430 | 2.32 |
| Native D3D11, legacy blit model, UHD 770 | 317 | 3.15 |
| Godot 4.7.2 Vulkan, empty canvas, UHD 770 | 348 | 2.88 |
| Godot 4.7.2 Vulkan, one full-screen rectangle, UHD 770 | 298 | 3.36 |
| Godot 4.7.2 D3D12, one full-screen rectangle, UHD 770 | 280 | 3.57 |
| Native D3D11 at 640×360, UHD 770 | 4654 | 0.21 |
| Native D3D11, RTX 4070 (the display GPU) | 17 910 | 0.06 |

- **Any program** on this PC pays about 2.3 ms per 1440p frame just to get an Intel-rendered frame onto the screen. 240 FPS is 4.17 ms, so that leaves 1.85 ms for everything else.
- **Godot adds about 1 ms more**: the canvas pass into the root viewport's own target, then a full-screen copy into the swapchain. `viewport_set_render_direct_to_screen` changes nothing on Forward+/Vulkan. That leaves 0.8 ms for the game on stock Godot.
- **On a real low-end machine** (a laptop, or a desktop with the monitor in the motherboard's port), the integrated GPU drives the screen itself and this copy does not exist. At 640×360, where the copy is small, the prototype runs at over 1000 FPS.
- Reloaded holds 240 because `fps_spy` and `fps_merc` default to 240 in `SCCT_Versus.config`. That is a frame cap, not its ceiling. We have not yet measured Reloaded uncapped on the UHD 770.

## Plan: a RenderingDevice renderer

Forward+ cannot reach the target: even its trivial-shading ceiling on AquaE is 79 FPS. The prototype shows that our own renderer can. The full plan input (inventories of every world, character, effect and frame feature, plus the port order) is summarised here.

- **Shape.**
  - New code goes in `game/Scripts/Render/Rd/`, grown from the prototype.
  - Godot keeps the canvas (HUD, menus), audio, input and game code.
  - View classes draw through an `IRenderScene` facade with a Godot backend (today's nodes) and an RD backend. Forward+ stays the fallback and screenshot reference until the new renderer reaches parity.
- **Shaders.** `svm_world.gdshaderinc` and its includes are split into a shared core that compiles both as Godot shader language and as GLSL 450. The same variant matrix stays; cheap toggles (no spot, no dynamic lights, see-through cut, portal fog) become specialisation constants. Precompiled SPIR-V and pipelines are cached on disk.
- **Frame.**
  - Uploads, compute pre-skin, then the spot-shadow atlas as depth-only tiles. Static tiles are copied in instead of redrawn.
  - The main pass: sky, then opaque front to back, then masked.
  - Depth and colour copies only when a projector, corona, cloak or refraction needs them.
  - Then transparents, first-person view models, and the existing post compute shaders.
- **Order.**
  - Static world in normal vision first.
  - Then lights, shadows and fog.
  - Then movers and gameplay objects, then characters, then effects and vision modes.
  - Each step is benchmarked and screenshot-compared against Forward+ on the golden cameras.
- **Presentation.** To get under Godot's extra 1 ms we need an engine change: draw the HUD canvas straight into our target, then present that target directly. This is a small patch to Godot's viewport and present code, so it means building our own Godot. We will decide once the renderer's own cost is known.
