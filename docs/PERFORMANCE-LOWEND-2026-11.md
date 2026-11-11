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
- Every play load prints `SVM_LOAD <phase> ms= total= pipelines= texmem= ws=` lines (`Diagnostics/LoadTimer.cs`). `SVM_LOAD sum <what> ms= calls=` lines before a phase line total work done in many small calls during it.

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
- With RenderingDevice, the world pass costs about what Forward+'s opaque pass does for the same shading (1.0–1.2 ms against 1.1–1.4 ms). The frame is shorter because Forward+'s fixed passes (pre-pass, tonemap, copies) are gone, and because nothing else is drawn: see "The world drawn inside Godot's frame" below.

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
- Reloaded holds 240 because `fps_spy` and `fps_merc` default to 240 in `SCCT_Versus.config`. That is a frame cap, not its ceiling. It has not been measured at 2560×1440 on the UHD 770 (see "The target" below).

## On the iGPU's own display (display 3, 1920×1080)

An HDMI cable from the motherboard now feeds a display nobody watches (physical x 4480; `SVM_BACKGROUND_AT=4480,0`), so the UHD 770 presents its own frames with no copy to the RTX 4070. Build p8 (`perf/low-end` at `41ce28be`), gameplay cameras:

| p8, 1080p on display 3 | SteeE FPS | SteeE GPU ms | AquaE FPS | AquaE GPU ms |
|---|---:|---:|---:|---:|
| Vulkan | 125.1 | 4.61 | 48.7 | 12.71 + 3.99 (shadow viewports) |
| D3D12 | 128.0 | 5.66 | crash | |

- Vulkan still loses about 3.4 ms a frame outside the timed GPU work (7.99 ms frame, 4.61 ms GPU), so the cost is in Intel's Vulkan presentation, not only in the cross-adapter copy. Blank-window runs earlier that day put a Godot D3D12 frame on this display at about 0.94 ms and a native D3D11 one at 0.43–0.92 ms (scratch tools, not re-run since).
- D3D12 presents fast but draws slower: the opaque pass takes 3.98 ms against 1.93 ms on Vulkan, so SteeE gains 3 FPS.
- D3D12 crashes on AquaE in `ShadowDepthCapture._RenderCallback` (compute dispatch). Not fixed.

## The world drawn inside Godot's frame (2026-11-09, `perf/rd-world`)

The first prototype (`SVM_RD_PROTO=1`) turns Godot's 3D off, so its 200 FPS is the cost of a trivially shaded world and nothing else: no characters, objects, effects, shadows or post. `SVM_RD_PROTO=frame` measures the other way round. Godot's frame stays whole, and only the merged map cells and the sky zone are drawn by our code, with the same trivial shading (texture × (lightmap + vertex light)):

- A compositor effect on the main camera runs between Godot's depth pre-pass and its opaque pass (`RdProtoFrameEffect`, `EFFECT_CALLBACK_TYPE_PRE_OPAQUE`).
- It draws into Godot's own colour and depth buffers with the projection and view of Godot's scene data (`RenderSceneData.GetCamProjection`, which already holds the y flip and reverse Z), so both renderers share one depth buffer.
- The environment's background mode is Keep, so Godot's opaque pass loads our colour instead of clearing it (`render_forward_clustered.cpp`, `load_color`). We clear the colour ourselves.
- The cells leave the main camera's cull mask (`StaticWorld`, and `Sky` when the main view draws the sky zone). The shadow cameras still draw them. Cells with an animated shadow material stay with Godot.
- Drawing after the opaque pass instead was slower than Forward+ (82.7 FPS on SteeE): nothing hid Godot's sky zone, so it was shaded across the whole screen in both of Godot's passes.

The picture is right: Godot's characters, objects, effects and the HUD sit in the world drawn by our pass, hidden by it where they should be.

UHD 770, 2560×1440, off-screen, gameplay cameras with the player's character and HUD, one build (`build-world2`), GPU ms from `--gpu-profile`:

| | SteeE Forward+ | SteeE in-frame | SteeE replaced | AquaE Forward+ | AquaE in-frame | AquaE replaced |
|---|---:|---:|---:|---:|---:|---:|
| FPS (1% low) | 93.4 (80.1) | 114.8 (95.1) | 197.3 (150.0) | 36.3 (33.5) | 60.2 (53.1) | 204.1 (160.3) |
| Frame ms | 10.71 | 8.71 | 5.07 | 27.53 | 16.60 | 4.90 |
| GPU ms, main view | 7.13 | 5.31 | | 19.86 | 9.09 | |
| GPU ms, spot shadow viewports | 0 | 0 | | 3.71 | 3.72 | |
| **Stages, summed over every viewport** | | | | | | |
| Our world pass | | 1.59 | 1.21 | | 1.30 | 0.99 |
| Godot depth pre-pass | 1.10 | 0.36 | | 2.09 | 1.51 | |
| Godot opaque pass | 2.98 | 0.31 | | 12.58 | 1.09 | |
| Shadow depth capture | 0 | 0 | | 0.79 | 0.79 | |
| Depth copy | 0 | 0 | | 0.47 | 0.47 | |
| Transparent pass (rain on AquaE) | 0.11 | 0 | | 1.78 | 1.76 | |
| Our post (display pass, glow) | 1.71 | 1.76 | | 3.02 | 3.03 | |
| Tonemap | 0.84 | 0.85 | | 1.94 | 1.94 | |

"Replaced" is `SVM_RD_PROTO=1`: it draws none of the rows below its world pass. On AquaE the four shadow viewports' own pre-pass, opaque pass, capture and tonemap are inside the stage rows (3.7 ms in all); without them the main view's pre-pass and opaque pass come to about 0.8 ms in the in-frame run, as on SteeE (0.67 ms). In the Forward+ and in-frame runs 3.4–4.0 ms of every frame lies outside the timed GPU work: presentation on this PC, plus CPU.

What the table says:

1. **The 200 FPS of the replacement prototype is not a forecast.** On AquaE, 11.5 of the in-frame run's 12.8 ms of GPU time is work the replacement prototype does not do at all, and a finished renderer of any kind has to do all of it.
2. **Staying inside Godot's frame costs about 1.3 ms at 1440p**: the tonemap pass (0.85 ms) and the wider colour buffer under our pass (1.59 against 1.21 ms). That is all a full replacement of Godot's 3D can still win over the in-frame world pass, in exchange for rewriting characters, particles, effects, vision modes, post and the capture tools.
3. **Most of the time is in work that is ours either way.** The world's real shading costs 1.8 ms more than the trivial one on SteeE and 10.8 ms more on AquaE (Forward+'s pre-pass and opaque pass against the in-frame run's). Then the post chain (1.7–3.0 ms), the spot shadow viewports (3.7 ms), rain (1.8 ms) and presentation. Porting `svm_world` to GLSL with the same variant matrix would carry its cost over unchanged.

## The world pass (`SVM_RD_WORLD=1`, 2026-11-09)

Step 1 of the plan above, built: the merged map and the sky zone's solid surfaces are drawn by our own pass inside Godot's frame, with the template's real lighting, shadows lookups and fog. It is off unless `SVM_RD_WORLD=1` is set. Code: `game/Scripts/Render/Rd/`.

UHD 770, 2560×1440, off-screen, build `build-w14`:

| View | Forward+ FPS (1% low) | World pass FPS (1% low) | Forward+ GPU ms | World pass GPU ms |
|---|---:|---:|---:|---:|
| SteeE gameplay camera (player, HUD) | 93.8 (80.6) | 107.9 (93.0) | 7.10 | 5.59 |
| SteeE spy spawn | 100.9 (83.6) | 119.4 (104.8) | 6.85 | 5.28 |
| SteeE shop | 69.6 (61.2) | 76.2 (69.1) | 11.23 | 9.99 |
| SteeE courtyard | 75.5 (63.1) | 100.5 (84.5) | 10.12 | 6.84 |
| SteeE crane | 81.9 (72.5) | 109.2 (91.3) | 9.07 | 6.10 |
| AquaE gameplay camera (player, HUD) | 36.4 (33.4) | 51.8 (46.7) | 23.52 | 15.17 |
| AquaE spawn | 37.7 (33.9) | 56.0 (49.6) | 23.32 | 14.71 |
| AquaE spawn, MSAA on | 31.2 | 43.5 | 28.89 | 19.81 |

The world itself (Godot's depth pre-pass and opaque pass before, our pass plus what Godot still draws after): SteeE 4.07 → 2.53 ms, AquaE 14.65 → 6.29 ms, of which our pass is 1.85 and 3.69 ms. **The gate of 3 ms on AquaE is missed by 0.7 ms**; portal fog is 1.4 ms of it (below).

**Same pixels.** With both renderers held at 30 FPS (so time-driven content is in step), the five views match Forward+ to within one 8-bit level, apart from single pixels on edges where a surface of ours meets one of Godot's (at most 0.03 % of a frame differ by more than 2 levels). Uncapped runs differ in the rain and the animated signs only because they are captured at different times. With MSAA 1.2 % of pixels differ by a level or two; not looked into.

### How it draws

- **The template itself.** `GdShaderTranslator` turns each material's Godot shader code (the defines and `svm_world.gdshaderinc` with its includes) into GLSL 450: the preprocessor conditionals are resolved, global parameters become one std140 block and combined samplers, material uniforms a second block, instance uniforms their defaults, varyings stage inputs and outputs, and `vertex()` and `fragment()` run inside a `main()` that supplies Godot's built-ins. There is one source for both renderers, so a change to the template reaches both. Engine free; a shader it cannot carry leaves that surface with Godot.
- **Global parameters.** `ShaderGlobals` wraps `GlobalShaderParameterSet`/`Add` (Godot gives the values back only in the editor) and gives every parameter a fixed place in the block. The pass reads the values at draw time, after every pre-draw handler has run. This relies on Godot's default thread model.
- **Which surfaces.** Solid and masked buckets of the merged cells whose normal-vision twin translates. Blended surfaces, authored materials, material sequences and masked materials with animated shadows stay with Godot (SteeE 34 of 324 buckets, AquaE 16 of 385). Taken buckets keep a Godot mesh on layers of their own (`StaticWorldRd`, `SkyRd`): the shadow cameras draw it, and the main view draws it whenever the pass sits out.
- **When it sits out.** Thermal and EMF, responsive smoke compiled in, maps with an outdoor sky, and after any failure of the pass. Godot then draws everything, as before.
- **Geometry as Godot packs it.** Normals octahedral in 16 bits, colours cut to bytes, so vertex data is bit for bit what Godot's own meshes hold.
- **Frame.** One draw list: our own depth pre-pass for the solid world, then colour front to back. A clip position computed by one expression in both (`invariant`) lets the colour pass find its own depth.

### What makes it faster than Forward+ with the same shading

A shader that holds more code shades slower on this GPU whether or not the code runs: with the spot code compiled in but never run, AquaE's world pass cost 3.2 ms more. So every draw gets the smallest shader that draws the same pixels, chosen each frame on the CPU:

| Step | AquaE world pass ms | SteeE world pass ms |
|---|---:|---:|
| Template as it is, no depth pre-pass | 15.63 | 3.80 |
| Own depth pre-pass | 10.40 | 2.44 |
| Spot slots tested by their frustum and own zone, not their sphere | 8.88 | 2.27 |
| Draws cut into 8 m blocks | 4.90 | 1.86 |
| Dynamic lights tested per row (radius, cone, own zone), rows handed to the draw as a mask | 3.62 | 1.78 |
| No light state, no spot, neither; with or without portal fog | 3.69 | 1.85 |

- MapBuilder's cells are 4096 uu (78 m) wide, so one material's draw touched most lights of the map. 8 m blocks (`SVM_RD_WORLD_BLOCK`) keep the costly variants to surfaces a light can reach. 4 m blocks draw the world in 3.14 ms on AquaE but cost more CPU; frame rate is the same.
- Tried and removed, no gain: variants without the flare term (0.1 ms), without the window pane filter (0), and portal fog without its crossings chosen per draw from the fog tiles (0: a block near the camera covers too many tiles).
- **Shaders at load.** Each material's shader in its three light states, with and without portal fog, is compiled in the first frame: 35 shaders on SteeE, 61 on AquaE. The first load of a map on a machine takes 0.4–6.5 s for this (12 s with MSAA); the SPIR-V is cached in the user folder (`rd_shaders`), and later loads take 0.15–0.3 s. Nothing compiles during play.

### Open

- **Portal fog, 1.4 ms on AquaE.** Its cost is its code's presence in every draw. Forcing the plain variant on every draw gives 2.5 ms (wrong picture). Choosing it per screen tile needs a stencil mask and each draw recorded twice.
- **Not exercised:** thermal and EMF switching, responsive smoke rounds, window resize, leaving a map, other maps. Only SteeE and AquaE captures were run, in normal vision.
- **The rest of the frame is unchanged:** spot shadow viewports (3.7 ms on AquaE), the post chain (1.7–3.0 ms), rain (1.8 ms), presentation.
- Measurement switches: `SVM_RD_WORLD_STATS=1`, `SVM_RD_WORLD_OUT`/`_IN` (force features out or in), `SVM_RD_WORLD_TIGHT=0`, `SVM_RD_WORLD_DYNBITS=0`, `SVM_RD_WORLD_PREPASS=0`, `SVM_RD_WORLD_CACHE=0`, `SVM_RD_WORLD_LEAVE=name,…`, `SVM_RD_DUMP=folder`.

## The shadow pass (`SVM_RD_SHADOWS=1`, 2026-11-09)

Step 2 of the plan, built: the moving casters of a spot shadow light are drawn by our own code instead of a Godot SubViewport per light per frame. Off unless `SVM_RD_SHADOWS=1` is set; it works with or without the world pass. Code: `game/Scripts/Render/Rd/RdShadowPass.cs`, called from `SpotShadowLight.RefreshShadowPass`.

UHD 770, 2560×1440, off-screen, gameplay cameras, build `build-s9` (another game was running on the RTX 4070 during these runs; the earlier figures reproduce within 1 FPS):

| | SteeE FPS (1% low) | AquaE FPS (1% low) | AquaE GPU ms | of which shadows |
|---|---:|---:|---:|---:|
| Forward+, shadow viewports | 90.9 (75.4) | 36.0 (31.8) | 23.80 | 3.76 |
| Forward+, our shadow pass | 90.8 (74.8) | 40.6 (37.0) | 20.18 | 0.57 |
| World pass, shadow viewports | 106.7 (88.6) | 51.3 (44.7) | 15.37 | 3.75 |
| World pass, our shadow pass | 106.0 (90.0) | 61.4 (54.8) | 11.70 | 0.57 |

SteeE's camera has no live shadow pass, so nothing changes there.

- **Why the viewports cost so much.** Four of AquaE's lights redrew every frame because something that is not static world sat in their frusta: the player, sharks, cloth, objective props. Each redraw was a whole Godot frame at up to 1707×1024: pre-pass, colour pass, depth copy and tonemap, about 0.95 ms a light, half of it for a colour image nobody reads.
- **What ours does.** A light whose static world is already cached gets its tile as a copy of the cached tile with the casters drawn over it. Each caster writes its distance along the light's axis and the nearest wins (minimum blending), which is the value the viewport pass and its depth copy produce. Skinned meshes are skinned in the vertex shader from the skeleton's pose; cloth is drawn from the arrays its simulation rewrites. Only the boxes the casters cover now, or covered at the last write, are copied from the cache.
- **Checked against Godot texel by texel.** `SVM_RD_SHADOWS=verify` keeps the viewport passes and, every 150 frames, draws the same tiles of the same frame with our pass into a scratch texture, reads both back and compares them. On AquaE, with the player, sharks, cloth and props in the frusta: two tiles identical to 2 mm, the other two with 10 to 38 texels of 1.5 million apart by more than the shadow bias, all on silhouette edges.
- **Still tracked without drawing.** A caster whose mesh never changes (MapBuilder's gameplay meshes, `SpotShadowLight.MarkRigid`) no longer forces any redraw while it keeps its place: the tile is kept until it moves, hides or takes another mesh. This also holds without `SVM_RD_SHADOWS` (`SVM_SHADOW_RIGID=0` turns it off).
- **Falls back per light, per frame.** A light keeps its viewport pass while its static cache is not written (a light that moves every frame, so the merc's torch), and whenever its frustum holds something our pass cannot draw as Godot would: a node that is not a MeshInstance3D, a mesh rewritten in place that does not hand over its shape (`MarkMutable` without `SetDynamic`: the escalator steps), eight bone weights, a material outside the Faithful template, a cut-out that animates or fades. `SVM_SHADOW_STATS=1` names the caster that keeps a pass live.

### Open

- The torch and the flares' cube shadows still use viewports.
- 0.57 ms is mostly the copies: two of the four tiles have casters close to the light whose boxes cover half the tile or more.
- Not exercised: cloaked spies and other material swaps on casters, broken glass, maps other than AquaE and SteeE, leaving a map.

## The post chain (2026-11-09)

Step 3 of the plan. On by default for every renderer path; `SVM_POST_LEAN=0` runs the chain as before. Code: `Post/DisplaySpaceEffect` (`Normal`, `Bright`), `Shaders/svm_display_normal.glsl`, `Shaders/svm_glow_bright.glsl`.

UHD 770, 2560×1440, off-screen, gameplay cameras, world pass and shadow pass on (builds `build-p1` before, `build-p4` after):

| | SteeE before | SteeE after | AquaE before | AquaE after |
|---|---:|---:|---:|---:|
| FPS | 106.7 | 121.2 | 61.1 | 68.7 |
| GPU ms | 5.72 | 4.52 | 11.76 | 9.98 |
| Final display pass | 1.77 | 0.54 | 1.83 | 0.71 |
| Glow: bright pass | none | none | 0.88 | 0.31 |
| Glow: four blurs | none | none | 0.24 | 0.24 |
| Glow: composite | none | none | 0.18 | 0.19 |
| Godot's tonemap pass | 0.86 | 0.85 | 0.84 | 0.85 |

- **The final pass was paying for code it never ran.** `svm_display_post.glsl` holds fallback fog, portal fog, the gate, the glow and five vision modes behind branches, and binds ten resources. A normal frame runs the gate, the glow add and the sRGB decode. Those three now have a shader of their own with two bindings and push constants; the full shader still runs for vision modes, fallback fog and the warm-up. Same rule as the world shaders: on this GPU a shader costs by what it holds.
- **The bright pass read the frame four times a texel.** It filtered four image loads by hand into a 1024² target. The normal view now reads the frame through a bilinear sampler, one fetch a texel. The sampler's weights have 8 fractional bits, so values can differ in their last bits; EMF's mask pass is unchanged.
- **Parity.** Both renderers capped at 30 FPS, lean against `SVM_POST_LEAN=0`: SteeE's crane, shop and spy spawn views identical in every pixel; the courtyard view (0.06% of pixels) and AquaE (2.1%) differ by as much as two runs of the unchanged chain differ from each other (0.06% and 2.7%: signs and rain).
- **Left.** Godot's tonemap pass (0.85 ms) is a copy of the frame into the viewport's texture with a linear curve; our decode cannot be folded into it without changing the engine. The four blurs and the composite are 0.43 ms together.

## Load time (2026-11-09)

Time from starting a load to the first frame the player controls (`SVM_LOAD done`), UHD 770, 2560×1440, world pass and shadow pass on, Godot's shader cache and pipeline cache warm:

| | AquaE | SteeE |
|---|---:|---:|
| Before | 8.6 s | 7.8 s |
| Character files stripped for the importer | 8.0 s | 6.9 s |
| Shaders made ahead on worker threads (second load of the map onwards) | 5.4 s | 5.3 s |

- **Godot decoded every animation clip of a character file it was not asked to make.** The stock spy's file is 14.6 MB with 339 clips in 26,442 channels and 52,892 accessors; the importer decodes every key while it parses, although `CreateAnimations` is off and the clips are read by `AuthoredAnimationTracks`. `SkeletalModel.ForImport` hands the importer the file without its `animations` and without the accessors and buffer views after the last one a mesh or skin uses (the converter writes geometry first). Import of the spy 554 → 54 ms, of the mercenary 203 → 19 ms.
- **Making a shader costs 20 ms and happened on the main thread, one material at a time.** `shader.Code = ...` only stores the code. Godot makes the server's shader when its RID is first asked for, which was inside `new ShaderMaterial { Shader = ... }` for the first material of each shader: the code is parsed and its compiled variants read from the shader cache, on the calling thread. AquaE asks for about 180 shaders: 3.5 s of its load, spread over the cell materials (1.8 s), instances, objects, effects and outfits. `ResponsiveSmokeShaders.Prewarm` now reads the list of shader codes the map's last load asked for (`load_shaders/<map>.json` in the user folder, written at the end of every load that asked for other shaders) and makes them on worker threads from the moment the load starts; a material that needs one before the workers reach it waits for that one. The workers finish in 1.8–2.3 s, overlapping the session start and the map build. `SVM_SHADER_PREWARM=0` turns it off.
  - The first load of a map after an install has no list and loads as before.
  - The list holds the two shader flags of that load (responsive smoke, portal fog) and sets them early. A match whose rules differ sets its own afterwards and makes its shaders where they are used, as before.
  - Godot serialises most of a shader's making behind one lock, so the workers together are 1.5 times as fast as one thread. The gain is that the main thread does other work meanwhile.
- **`SVM_LOAD sum` lines.** `LoadTimer.Add` totals work done in many small calls (shader code, shaders made by materials, textures); each phase line is preceded by the sums gathered during it. `build.cells` is now three lines: `build.cell-materials`, `build.cell-geometry`, `build.cells`.

### Where the 5.4 s of AquaE goes now

| Phase | ms | What it is |
|---|---:|---|
| `session.*` | 2000 | The host's world, the map's actors and the client's world (1.5 s without the shader workers beside it) |
| `warmup.frame1` to `warmup.finish` | 1000 | First frames: 269 pipelines made, the world pass's own shaders |
| `play.outfits` | 450–850 | Two character models; the 9.9 MB JSON of the spy's file is still parsed by us (100 ms) and its clips built |
| `build.*` | 600 | Map build with its shaders ready |
| `first-control` | 330 | Frames until the pawn is controlled |
| `play.view`, `play.hud`, `play.spy-first-person` | 450 | |

### Open

- The original loads a map in 0.3–0.4 s. Nothing here gets within an order of magnitude; the session start and the first frames are the next two seconds to look at.
- The Godot caches on this PC are large: the shader cache is 2.1 GB (7,844 files in the newest folder) and the Intel pipeline cache 704 MB, read whole at start. Not measured here: what start-up pays for them, and what a player's first run without them costs.
- Clips could be stored outside the character files' JSON (a binary block read on demand), which removes most of `play.outfits`.

## Where AquaE's frame goes against the original's (2026-11-10)

The original draws the AquaE spawn view at 2560×1440 on the UHD 770 in 6.3 ms a frame (159 FPS, "The target" below). Ours took 14.6 ms. The frame taken apart with measurement switches (`SVM_RD_WORLD_TEST=only|clear|depth|nosky`, `SVM_RD_WORLD_MASK=<hex layers Godot's main view keeps>`, `SVM_PERF_HIDE=<words in node paths>`, `SVM_PERF_SURVEY=1`), world pass, shadow pass and lean post on, GPU 92% busy throughout:

| Part | Frame ms | Notes |
|---|---:|---|
| Godot's frame with nothing in it, our post chain and shadow pass | 6.98 | 2.87 timed (tonemap 0.87, final display pass 0.64, glow 0.65, shadow pass 0.57, depth clear 0.25) and 4.1 untimed (the copy to the display's GPU, 2.3; Godot's own target and swapchain copy) |
| World pass without portal fog | +2.75 | 2.25 timed: 0.53 depth pre-pass, 1.7 colour |
| Portal fog in the world's shaders | +1.5 | |
| Map projectors | +1.5 | fixed, below |
| Objects and characters (World layer) | +0.95 | Godot's pre-pass and opaque pass |
| Depth copy, other effects | +0.9 | the depth copy (0.48) is for the projectors' and coronas' depth reads |
| Rain sheets | +0.1 | |
| Cloth water (soft bodies) | +0.3 | hidden: 14.7 → 14.4 ms; 4 draws, 448 triangles; its simulation is part of 0.5 ms of CPU |

- **An empty Godot frame is slower than the original's whole frame.** 6.98 ms against 6.3 ms. Nothing drawn inside Godot's 3D frame on this PC can match the original on this view; what is left after every cut is the tonemap copy, a 16-bit float target read back by two full-screen passes, and Godot's presentation.
- **A full-screen pass costs 0.3–0.9 ms here whatever it computes.** The world's colour pass with no fog is 1.7 ms for the whole screen; a pass that only blends a constant over it costs about as much as the glow or the final display pass.
- **Map projectors, fixed (1.77 → 0.37 ms of the transparent pass, 68.6 → 76.2 FPS).** A projector drew the back faces of its box with no depth test, so a box hidden behind a wall still ran its shader on every pixel it covered: AquaE's five rain-ripple boxes covered the screen from inside the spawn room. Each box is now two surfaces: a camera outside the box (by more than 1 m) draws the faces turned towards it with the depth test on, so pixels whose surface lies in front of the box are rejected before the shader runs; a camera inside draws the far faces as before. The choice is made in the vertex shader from the box's planes, per camera. Capped at 30 FPS the picture differs from the old one by 0.65% of pixels, less than two runs of the same build differ (rain).
- **Portal fog, three attempts, no gain kept.** Its cost in the surfaces' shaders is 1.4 ms. (1) One fog draw over the screen after the world, reading depth: 2.2 ms, worse (the camera's own zone is fogged, so every pixel is covered). (2) The same split by screen tile, a ramp-only shader where no portal candidate lies (83% of tiles) and the whole function elsewhere: 0.81 + 0.62 ms, equal to the surfaces' own. (3) The ramp back in the surfaces behind one tile lookup, the fog draw on candidate tiles only: 0.66 + 0.61 ms, 0.16 ms better, not worth its code. Reverted. Fog on this GPU costs by the pixels it touches; computing it per vertex is the form that would be cheap, and it would not match the per-pixel portal crossings.

## The original's frame, traced (2026-11-10)

What the original draws in one frame of the AquaE spawn view at 2560×1440 on the UHD 770, and what each part costs. Recorded by a tracing build of the sandbox's Direct3D 9 proxy (`D:\OpenSCCT\rd-notes\d3d9trace`, `SCCT_TRACE=1`: it counts draws between render target changes, clears, copies and blend switches, and ends each stretch with a GPU timestamp query; every 300th frame is written to the proxy's log). Eight traced frames agree within 0.2 ms.

| Part | GPU ms | What it is |
|---|---:|---|
| Scene | 2.7–2.8 | 109–134 draws, 11,700 triangles, into one 2560×1440 A8R8G8B8 target |
| Player shadow texture | 0.05 | 128×128 target: 6 untextured draws, then 9 blended ones |
| Glow | 0.53 | bright pass 512×512 (0.08), eight blur passes 512×256 down to 32×32 (0.08 together), composite 512×512 (0.05), full-screen pass onto the back buffer (0.32) |
| HUD | 0.11 | 17 draws on the back buffer |
| Frame copy and present | 2.3 | the proxy's own copy of the frame (0.33) and Present |
| **Frame** | **5.8–6.3** | 159 FPS measured |

- **The scene has no pixel shaders and no vertex shaders.** Every scene draw is fixed-function: one texture stage on 48 of the 55 draws of the first stretch, two or three on the rest, fog switched on as a render state. The only pixel shaders in the frame are the four of the glow chain (ps_1_1 and one ps_1_4, 116 to 164 bytes).
- **One pass, no depth pre-pass, 8 bits a channel.** Solid draws first, then blended ones, straight into the scene target.
- **Filling the screen costs it about 0.9 ms.** Two draws with two textures (94 triangles, the room's large surfaces) take 0.89 ms; 55 single-texture draws take 0.44 ms.
- **Its present costs about 2 ms on this PC**, hidden desktop or not. So the earlier comparison stands as measured: 6.3 ms against ours with both paying a copy to the display's GPU.

Ours on the same view (13.1 ms): world 3.7, objects and characters 0.95, effects and depth copy 0.9, shadows 0.57 (scene 6.1 against 2.8); tonemap, final pass and glow 2.3 (against 0.53); presentation and untimed copies 4.1 (against 2.3).

### The states it draws with

The same trace with each draw's fixed-function state (`STATE` lines of `D:\OpenSCCT\rd-notes\d3d9trace\aquaE-state.log`: vertex format, depth, alpha test, blend factors, fog, lighting, and the colour and alpha operation of every texture stage with its texture's format). The AquaE spawn frame uses about forty distinct states; these carry nearly all of it:

| Surface | Draws (triangles) | Vertex | Stages | Result |
|---|---|---|---|---|
| Static mesh, baked vertex light | 21 (1645) + 5 (595) + others | position, normal, colour, one UV; lighting off | 0: texture MODULATE2X vertex colour | texture × colour × 2 |
| BSP, lightmapped | 8 (171) | position, normal, two UVs; lighting off | 0: texture; 1: lightmap (A8R8G8B8) MODULATE2X, second UV | texture × lightmap × 2 |
| Actor-lit mesh (characters, props) | 13 + 8 + 8 and more | position, normal, one UV; **Direct3D lighting on**, ambient set per draw | 0: texture MODULATE2X lit colour | texture × per-vertex light × 2 |
| Self-illuminated (mask) | several | as its base | 0: as base; 1: keeps colour, takes the mask's alpha; 2: BLENDCURRENTALPHA with the glow texture | lit colour blended to the glow texture by the mask |
| Environment-mapped | 1 (5) | BSP | 0, 1: as BSP; 2: MODULATEALPHA_ADDCOLOR, camera-space reflection coordinates | adds the env texture through alpha |
| Masked | many | any | alpha test on, reference 127 | cut at half alpha |
| **Projector pass** (the player's shadow, map projectors, lights) | 1 (200) + 6 (643) + 1 (13) | the receiving meshes drawn again | 0: the projector's 128×128 render target, projected camera-space coordinates; 1: BLENDTEXTUREALPHA towards texture factor 0.5 with a DXT3 texture; 2: the same with a volume or cube texture | blended DESTCOLOR, SRCCOLOR: frame × value × 2, where 0.5 leaves the frame as it is |
| Translucent, additive | 9 | with colour | one or two stages | SRCALPHA/INVSRCALPHA, or ONE/ONE |

- **Fog is a render state on every scene draw:** table fog, linear, with no vertex fog mode. The hardware fogs each pixel by its depth; there is no fog code anywhere.
- **Characters and props are lit per vertex** by Direct3D's own lights, with an ambient colour set for each draw.
- **A light or shadow that falls on the world is a second draw of the surfaces it reaches,** multiplied into the frame, not a term in every surface's shader. Its 128×128 texture is drawn first: six untextured draws of the casters (2,618 triangles), then nine blended quads.
- **Nothing in this frame is a shadow map lookup or a fog integral.** Our per-pixel spot shadow slots and portal fog either stand for something this view does not use, or for Reloaded features switched off at the capture's settings; which one is the next thing to settle before they are carried into a new renderer.

### What follows for a renderer of our own

- **Target and passes as the original's:** one 8-bit scene target in display values, solid then blended, no pre-pass, the glow chain at its sizes, one full-screen pass to the screen that also adds the glow. No tonemap copy, no float target, no separate decode pass.
- **Shaders as small as its texture stages.** A surface is its diffuse texture times its lightmap or vertex colour, with fog from the vertex. The Faithful template computes per pixel what the original gets from two or three texture stages and a fog state; the frame budget is the original's 0.9 ms a screen.
- **To decode next (REA):** the texture stage and fog state the game sets for each kind of surface (BSP with lightmap, static mesh, actor, the three-stage draws), so each maps to one small shader; how its dynamic spot lights reach a surface (no shader in the scene, so stages or extra blended draws); what Reloaded's enhanced zone fog adds when it is on (none of it showed in this frame).

## Own frame, first stage (`SVM_RD_FRAME=1`, 2026-11-10, `perf/own-frame`)

The world pass's batches, culling and draw order, drawn the original's way: into an RGBA8 target of our own with a 32-bit depth buffer, one pass, no pre-pass, each surface a shader the size of the original's texture stages (texture × vertex colour × 2, texture × lightmap × scale, or the texture alone; masked ones cut at half alpha). Godot draws no 3D in the main viewport (`Viewport.Disable3D`); the target is a full-screen `TextureRect` under the HUD. Code: `Rd/RdWorldGpu.RenderOwn`, `Rd/RdWorldRenderer.PreDrawOwn`. Off unless the variable is set.

AquaE spawn view, 2560×1440, UHD 770, build `build-o2`:

| | Frame ms | FPS | World GPU ms |
|---|---:|---:|---:|
| In Godot's frame (world pass, shadow pass, lean post) | 13.1 | 76 | 3.7 |
| Own frame, world only | 6.04 | 166 | 1.01 |
| The original, whole frame | 6.3 | 159 | 2.8 for its whole scene |

- The room and the view through the door draw as before to the eye; not compared pixel by pixel yet.
- **Not drawn yet:** characters, gameplay objects, blended surfaces, projector passes (shadows, lights, the rain ripples), rain, emitters, coronas, fog, glow, vision modes. The 6.04 ms is the floor these are added to; the original spends about 1.8 ms on the same things and 0.53 ms on glow.
- The shadow pass still runs and is not read (0.58 ms of the 6.04).
- 3.5 ms of the frame is outside the timed work: the copy to the display's GPU, Godot's canvas pass (0.68 ms timed, most of it showing our target) and its swapchain copy. The original's present costs about 2.3 ms with the proxy's copy.

### Second stage: gameplay meshes (`Rd/RdObjects.cs`)

The rigid meshes under the map's `Gameplay` node with solid or masked template materials are drawn after the world in the same pass: texture × a light worked out at each vertex (the baked vertex colour, or the actor's ambient and four lights by the template's `svm_actor_light`), as the original lights its actors per vertex. Each surface's geometry is uploaded once; each frame hands over a model matrix and light block per visible object.

| Own frame, 2560×1440, UHD 770, build `build-o3` | Frame ms | FPS | Scene GPU ms |
|---|---:|---:|---:|
| AquaE spawn, world and objects | 5.76 | 174 | 1.00 |
| SteeE spawn, world and objects | 5.50 | 182 | 1.10 |

- **The picture is brighter than the current renderer's** (SteeE: 40% of pixels more than 2 levels apart; AquaE 73%). The current renderer's last pass applies the glow composite's gate to the frame, and the original does the same in its one full-screen pass; the own frame has no final pass yet. Nothing can be compared level for level until it has.
- Not drawn: skinned meshes (characters, animated props), meshes rewritten in place (cloth), blended surfaces, and everything listed under the first stage except objects.

### Third stage: the end of the frame and fog (`Rd/RdFramePost.cs`)

- **The frame ends as the original's does.** Bright pass at 512×512, eight blur passes from 512×256 down to 32×32, the weighted sum back at 512×512, all on 8-bit targets, then one full-screen pass that gates the scene, adds the glow and writes the target the canvas shows. Same arithmetic as the compute chain of the Godot path. AquaE: glow 0.19 ms, final pass 0.74 ms; SteeE (no glow): final pass 0.46 ms.
- **Fog as the original sets it.** A trace with the fog state (`aquaE-fog.log`) shows one linear fog for every scene draw of the AquaE frame: colour (27, 33, 32), start 100, end 3500, with the colour swapped to black on additive draws and to mid grey on the multiplied ones. No draw has a fog of its own and nothing is evaluated per portal. The own frame applies the camera zone's ramp to every world and object pixel (two lines in each shader); the scene still costs 1.0 ms.
- **Picture against the Godot path** (uncapped runs, so rain and signs differ too): SteeE 1.2% of pixels more than 2 levels apart, AquaE 3.0% (it was 65% before the fog). What is left is what is not drawn yet: the character, the projector passes, rain and other blended surfaces.
- Frame times of this build were taken while other work ran on the machine and are not quoted; the stage times are as before.

### Fourth stage: characters

Skinned meshes (the pawns under PlayRoot's `Pawns` node) are drawn by the same object pass: four bone influences a vertex, the bone matrices of each visible skeleton handed over every frame and blended in the vertex shader, the light worked out at the posed vertex. Meshes on layers the main view does not draw (a body hidden from its own eyes) are left out.

| Own frame, 2560×1440, UHD 770, build `build-o6` | Frame ms | FPS | Scene GPU ms | Pixels more than 2 levels from the Godot path |
|---|---:|---:|---:|---:|
| AquaE spawn | 6.7–7.0 | 143–150 | 1.04 | 0.6% |
| SteeE spawn | 5.9–6.6 | 151–169 | 0.94 | 0.6% |

- The character is posed and lit as in the Godot path to the eye. Frame times vary because other work ran on the machine (23% CPU load); the stage times do not.
- AquaE's frame still holds 0.57 ms of shadow pass nothing reads, and lacks the projector passes, rain and other blended surfaces.
- Not handled on characters: cloak, tints and other cosmetics, goggle glow, normal maps, first-person arms.

### Fifth stage: lights, projectors, fallback

| Own frame, AquaE spawn, 2560×1440, UHD 770 | Scene GPU ms | Pixels more than 2 levels from the Godot path |
|---|---:|---:|
| World, objects, characters (stage shaders only) | 1.04 | 0.6% |
| With the template's lighting where a light reaches | 2.01 | 0.7% |
| With the projector passes (build `build-o9`) | 2.61 | 0.36% |
| The original's whole scene | 2.8 | |

- **Spot and dynamic lights.** A world draw that a spot slot or a dynamic light reaches (the reach tests of the world pass) is drawn with the translated template and its light terms, in the same pass and target; every other draw keeps its stage shader. The template's fog globals are overridden for the pass so it applies the frame's one ramp. On this view that is 253 of 676 draws and 1.0 ms for almost no visible change: the reach tests are conservative. The original pays for a light only where it draws the light's pass.
- **Projectors** (`Rd/RdProjectors.cs`). Each projector in view draws again the world batches its frustum box reaches (box against its six planes), depth test EQUAL, with svm_projector's projection on the surface's own position, blended as its kind says. 0.6 ms on AquaE (five rain-ripple projectors); the first version, testing by bounding box only, was 0.86 ms. Objects and characters receive no projection yet.
- **Other vision modes.** When the view is not the normal one, or the round has responsive smoke, the main viewport draws 3D again and Godot's renderer takes the frame whole. Written, not exercised.
- Frame times were not usable in these runs (other work on the machine: the same build read 98 to 150 FPS); the stage times were stable.

**Not drawn in the own frame yet:** blended world surfaces (glass, water, grates), rain sheets, emitters, coronas, light beams, lasers, the first-person view model, cloth, cloaked or tinted characters, spot and projector light on objects and characters, maps with an outdoor sky. Until these exist the mode is a measurement of where the renderer can get to, not something to play with.

### Sixth stage: blended surfaces and coronas

- **Blended world surfaces.** With `SVM_RD_FRAME=1` the world geometry takes translucent, additive and multiplied buckets too (SteeE: all 324 buckets, none left to Godot). They are drawn after the solid scene, far to near, with the translated template and its own blend factors, testing depth and writing none. Blended sky-zone surfaces keep a stage shader with the material's blend.
- **Coronas** (`Rd/RdCoronas.cs`). The quads the Coronas node shows are drawn after the scene, instanced by texture, blended ONE, INVSRCCOLOR as the original; the visibility rule of svm_corona.gdshaderinc is tested once a corona, in the vertex shader, against the frame's depth.

Four SteeE views at a 30 FPS cap, own frame (build `build-o12`) against the Godot path, pixels more than 2 levels apart: spy spawn 0.03%, shop 0.55%, courtyard 0.83%, crane 1.76% (4.2% before the coronas).

**Seen missing in the crane view:** the moon, the crane itself (a skinned map prop outside the nodes the object pass walks), one lit window. Not looked into yet.

**Still not drawn:** rain sheets, emitters, light beams, lasers, the first-person view model, cloth, cloaked or tinted characters, spot and projector light on objects and characters, skinned map props, maps with an outdoor sky.

### Seventh stage: every actor, rain, particles

- **Actors.** The object pass walks the whole play scene (cached; walked again when a node enters or leaves the tree) and draws every mesh on the actors' and the first-person layers with a solid or masked template material: gameplay objects, animated props (SteeE's crane), characters, the first-person model. The merged world's own nodes and the cloth are left out. Gathering costs 0.25–0.37 ms of CPU a frame. A cloaked body is not drawn at all yet.
- **Draw order.** Solid world and sky, actors, projector passes, then everything blended over them: the world's blended surfaces far to near, then the sheets.
- **Sheets** (`Rd/RdSheets.cs`). One pass for simple blended triangles with a texture and a colour: the rain as RainVolumes builds it, and the sprite particles as ParticleSystemView fills them, in the seven particle draw styles with their blend factors, cell blending, alpha test and fog rules. The frame's vertices are gathered on the main thread and handed over whole.
- Cut-outs whose shadow animates are taken by the world geometry in this mode too.

Own frame (build `build-o16`) against the Godot path at a 30 FPS cap, pixels more than 2 levels apart: SteeE spy spawn 0.02%, shop 0.55%, courtyard 0.82%, crane 0.82%; AquaE's eight reference views 0.04% to 6.6% (far-180 6.6%, spy-180 4.4%, merc-0 2.6%, spy-90 2.3%, the rest under 2%). The worst two look the same to the eye side by side: the differences are level shifts on lit floors and a grate, not missing things. Not yet tracked down.

**Still not drawn:** mesh particles (MultiMesh emitters), light beams, lasers, cloth, cloaked bodies and character cosmetics, spot and projector light on actors, the moon on SteeE. Maps with an outdoor sky leave the mode off and draw with Godot.

### Eighth stage: blended actors, and the fog checked against the original

- **Blended actor surfaces.** Translucent and additive template materials on actors are drawn after the solid actors, blended by the texture's alpha, writing no depth. Multiplied ones are not drawn yet.
- **Lasers need nothing in the normal view:** svm_laser draws only in thermal and EMF, which go back to Godot's renderer.
- **The largest difference from the Godot path is the Godot path's error.** In AquaE's far-180 view the aquarium window is where the two differ (6.9% of the frame). The original was traced and captured on that pose (`aquaE-far180.log`, `runs/orig-trace-far180`): all 46 of its draws use the camera zone's fog (100 to 3500, colour 27, 33, 32), although the zones behind the window have other ranges in the map's data. Mean colour of the window:

| | R | G | B |
|---|---:|---:|---:|
| Original (today's capture, and the stored golden) | 21.4 | 87.7 | 202.2 |
| Own frame | 21.6 | 89.4 | 203.5 |
| Godot path (portal fog) | 5.8 | 57.9 | 171.4 |

  The own frame's one ramp is what the original draws; the Godot path's fog through portals darkens the water by 30 levels. Whether Reloaded has a setting that turns per-zone fog on is not known; at the sandbox's settings it does not.
- **Both of ours are darker than the original on that view's dark floor** (mean 6.0, 5.2, 4.5 against 11.5, 9.4, 7.5). The own frame and the Godot path agree with each other there, so it is not from this work: the gate of the final pass, or the floor's light. Not looked into.

Own frame (build `build-o17`) against the Godot path, 30 FPS cap, pixels more than 2 levels apart: SteeE's eight reference views 0.00% to 1.64%; AquaE's 0.04% to 6.9% (the window above).

### Ninth stage: the scene completed

Everything the normal view draws on the maps of the original is now drawn by the own frame.

- **Spot shadow lights on actors.** A mesh whose box a spot slot's sphere reaches is drawn by a second set of the small actor programs that adds `svm_spot_slot` at each pixel, for the slots that reach it (the mercenary's torch on a spy, a door or a crate). The others keep the programs without that code.
- **Light beams** (`RdSheets.CollectBeams`): the slice meshes of `MapLightBeams`, stretched on the CPU each frame and drawn with their own small shader (cut by the light's shadow map, tinted by its cookie).
- **Layered materials take the whole template.** The small shaders hold one texture and one light. A material with more (self-illumination, an animated or combined texture, a flipbook, an environment, an opacity layer, cosmetics, an authored material) is drawn by the translated template instead, on the world (`RdMaterial.Layered`) and on actors. For actors the translator has a second mode (`GdShaderTranslator.Translate(..., actor)`): RdObjects' vertices, the object's model matrix or the vertex's blend of bones, and the instance uniforms (`svm_ambient`, the four lights, `svm_tint`, `svm_glow_hue`) read from the object's block. `SVM_NORMAL_VISION` is defined for these, so they hold no thermal or EMF code. Multiplied and darkening blends go this way too. The values of an actor material are read again in turn (two materials a frame; every frame for a conveyor's scroll and a cloak's glass value).
- **Cloth** (`RdObjects.SetDynamic`): the arrays `SoftBodyViews` rewrites its mesh from are uploaded again when they changed and the cloth is in view.
- **Mesh particles** (`RdSheets.CollectMeshes`): the emitter's mesh placed on the CPU for each particle, as a sheet draw.
- **Cloaked bodies**: drawn last, after a copy of the frame (`TextureCopy`, only in frames with a cloaked body in view), which the template's cloak code reads as `svm_behind`.
- **No work at first sight.** When a mesh node enters the scene (the map's load, a character spawning) its surfaces are packed and its template shaders and pipelines are made, both light states. The sheet and beam shaders are made in the first frame.
- **The hand-over to Godot's renderer was exercised** with `SVM_RD_FRAME_FLIP=0.7` (Godot's renderer takes every other 0.7 s): SteeE's and AquaE's reference views, some captured on each side of a switch, show no error, no black frame and no difference from the unswitched runs beyond rain and blinking lamps, which differ between any two runs.

Against the original's own captures (`local/golden/allmaps/game`, mean absolute difference over 32 views of FactE, RiveE, AquaE and SteeE, levels of 255): the Godot path 3.28, the own frame 3.16. The own frame is nearer on FactE's windows (the glow around them: 66, 65, 65 at one point of the original, 67, 66, 66 in the own frame, 22, 21, 21 in the Godot path) and on AquaE's aquarium window; it is further by 0.3 to 0.6 on two RiveE views with additive holograms, where the 8-bit target clamps a sum the Godot path's float target carries on.

Not drawn by the own frame: maps with an outdoor sky (OpenSCCT's own; none of the original's maps) and every vision mode but the normal one. Both go to Godot's renderer whole.

### Tenth stage: every map compared, on by default

The own frame is the options' **Renderer: Fast (beta)** (Performance page, `renderer` in the settings; Standard, Godot's renderer, is the default until Fast has been played). `SVM_RD_FRAME=1` or `0` overrides the setting, as the benchmark scripts do. Maps with an outdoor sky and every vision mode but the normal one still go to Godot's renderer.

**All maps.** `own-sweep.sh` captured the eight golden views of one map of each family (34 maps, 268 views) with the Godot path and with the own frame, and both were measured against the original's own captures (`local/golden/allmaps/game`; `svm/` there holds ours). Mean absolute difference from the original, levels of 255: Godot path 2.84, own frame 2.86 before the last three fixes below, which each removed the largest remaining differences (MuseD spy-180 from +9.7 to -4.4 against the Godot path, SlauS from +5 to 0, DeftD from +5.6 to 0). What the sweep found and what was done:

- **Zone ambient** (`svm_zone_amb`) was missing from the small world shaders: outdoor zones (OrphD, MissD, SancD's church) were dark. It is read at the vertex from the zone in the colour's alpha.
- **Mirrored instances** (a model matrix with a negative determinant) drew their back faces: SancD's ammunition lockers stood open. They take pipelines with the other front face.
- **The sky zone**: its props and light sprites are drawn (a sprite is turned to the camera by its model matrix); its surfaces all go through the template, which gives them the sky's own fog, and they read the frame's real fog values from a second copy of the global block; `OwnSkyDepth` is 2^-50, the template's own scale.
- **Blended order**: the sky's blended layers, then the world's blended surfaces and the blended actors together, far to near (PolaD: window glass over an additive sky layer). Every blended actor surface takes the template.
- **Light beams were also drawn as props** (the beam shader's comment names the template file; the test now looks for the include line).
- **Map surfaces the world pass leaves** (a sequence, an authored material, no entry) are drawn by the object pass, with the lightmap's UV in the rigid vertex. No map of the sweep has any; unexercised.
- **Cloth** kept still: its on-screen notifier never fires without Godot's 3D view. A cloth the own frame has in view counts as on screen.
- **A fog rule tried and dropped**: MissD's spy views have no fog by our zone lookup while the original (traced, `missD-spy0.log`) fogs the frame 300 to 4000. Taking a neighbouring zone's fog hazed SlauS and MuseD and was not what made MissD right; the zone found for that eye is the open question, in both of our renderers.

Differences that are not faults: rain, flickering and blinking lights and animated screens differ between any two runs; glow around saturated lights is nearer the original in the own frame (FactD, MissD, AquaE far views); additive sums past white clamp in the 8-bit target (RiveD's holograms, +0.3 to +0.6).

**Speed** (quiet machine, UHD 770, 2560 x 1440, uncapped, gameplay spawn views, build `build-o44`):

| | Godot path (`j1`) | Own frame | Original |
|---|---:|---:|---:|
| SteeE FPS (1% low) | 123 (102) | 188 (141) | 144 (138) |
| AquaE FPS (1% low) | 76 (68) | 119 (99) | 159 (130) |
| AquaE GPU ms | 8.98 | 4.96 | 4.0 (6.3 with its present) |

At 1920 x 1080 (build `build-o45`): SteeE 301 FPS (1% low 205), AquaE 179 FPS (1% low 140); the original holds its 240 cap on both there. A 20 s round with six bots on each map ran without an error.

AquaE's own frame: scene 3.07 ms, glow 0.19, the canvas item that shows it (with the gate and the glow, the former final pass) 0.85, shadow pass 0.56. The frame is 8.40 ms, so 3.4 ms lie outside GPU work on this machine (the copy to the other adapter's monitors and Godot's present); on a display driven by the integrated GPU itself that part is about 1 ms.

Open: the frame's time outside GPU work (Godot's canvas and swapchain copies); the shadow pass redrawing tiles with a moving caster every frame; a smaller shader for world draws a light reaches; cloaked bodies, mesh particles and leftover map surfaces have not been seen in a capture; Ryan's laptop.

### Eleventh stage: an option, and made safe to ship

**Renderer: Standard or Fast (beta)** (Options, Performance; `renderer` in the settings, Standard by default). A map takes the choice when it is built (`RdWorldRenderer.LatchOwnFrame`), so a change waits for the next map. Standard is Godot's renderer with none of our own passes: the world pass in Godot's frame (`SVM_RD_WORLD`) and the shadow pass (`SVM_RD_SHADOWS`) were on by default for a day and now follow Fast; each variable set to 1 or 0 still forces its pass, and `SVM_RD_FRAME=1` or `0` forces the renderer, for measurements.

**The device lost on the NVIDIA card.** The smokes run on the RTX 4070 SUPER crashed now and then just after a map's load (`VK_ERROR_DEVICE_LOST`, then an access violation in `nvoglv64.dll`; `nvlddmkm` event 153 in the system log): three times in some sixty runs, never twice at the same place on demand, never on the UHD 770. Standard toured all 87 maps without it. The Vulkan validation layer (`--gpu-validation`; a copy of `VkLayer_khronos_validation.dll` found on the machine, named by a manifest in `VK_LAYER_PATH`) reported one error, four times in two map loads: `VUID-VkImageViewCreateInfo-image-01762`. Godot's texture storage makes an sRGB view of every 8-bit texture a `Texture2DRD` wraps, and the frame's targets were made for one format only, so that view was invalid. They are now made with both formats shareable (`RdWorldGpu.Shown`), and all three modes validate with no error. The layer's GPU-assisted checks crash the game themselves and were not used. The passes carry breadcrumb marks (`RdWorldGpu.Breadcrumb` plus 1 to 6), so a lost device names the pass it was in.

**Both renderers draw behind the loading cover.** The own frame used to switch Godot's 3D off as soon as the map was built, so the warm frames (`GameplayFxWarmup`, `LightReach.Warm`) drew nothing: the load waited five seconds for the view's post effects (`Gameplay post warmup did not draw before loading timeout`) and Godot's pipelines for the vision modes were first made when a player switched to one. While `PlayRoot`'s cover is up (`RdWorldRenderer.Loading`) Godot now draws as it does in Standard and the own frame draws too, into its hidden target.

**Nothing compiled in play.** A frame of the own pass that makes a shader or a pipeline after its thirtieth, or takes over 12 ms, is named in the log (`SVM_RD_FRAME late work`, with the shader's name and its defines). What the map tour and a round with bots named, and what was done:

- Cloak materials are warmed with the others (the copy of the frame they read exists from the first frame), and a rigid material's mirrored pipelines too.
- The projector and corona shaders and every map material's small shader are made in the first frame, not when the first one comes into view.
- A material given to a node after it was first met (an objective's marker, a switched screen) was made ready in the frame it came into view. `RdObjects.Gather` now looks at 24 nodes a frame again, in turn, in view or not.
- A material first met in play whose shader this machine has never compiled cost 169 ms on the NVIDIA card (two shaders, four pipelines: the driver's own compile). After the thirtieth frame an actor shader is compiled from source on a worker thread and its pipelines are made there too (`RenderingDevice` allows both, as Godot's own compiler does); the surface is drawn from the frame they are ready. `SVM_RD_WORLD_TEST=defer` sends every actor shader that way, as a check.
- Godot's renderer made 14 pipelines at the first switch to night vision (`gameplay-first-use`): the player's body and weapon enter the scene after the cover lifts and Godot had never drawn them. Godot draws under the own frame's picture for 30 more frames after a load (`SVM_RD_FRAME_BOTH`). The smoke then reads as it does in Standard (one pipeline at the first thermal view, in both).

**Reads kept inside their buffers.** A device lost on the NVIDIA card is most often a read past a buffer, which the validation layer does not see. A packed vertex holds bone indices no higher than 255 (`RdObjects.MaxBone`) and both palette buffers (the frame's and the shadow pass's) keep that much room past their last matrix. GLSL's `length()` of a runtime array is no use for this: Godot's shader reflection answers `OpArrayLength is not supported yet`.

## Twelfth stage: the first load of a map (2026-11-10, `perf/fast-load-times`)

A player hosting AquaT on an Intel UHD 630 laptop waited 70 s for the first load on Fast: 21 s in the world pass's warm-up and 43 s in the first warm-up frame as a whole. Nearly all of it is the graphics driver compiling pipelines it has never seen, one after another on the render thread. Later loads read the driver's and Godot's caches.

Measured cold on the UHD 770 (`tools` outside the repository: an empty user folder through `APPDATA`, and Intel's own per-program cache file under `LocalLow\Intel\ShaderCache` moved aside; without the second a "cold" run is warm). AquaT, three bots, editor build:

| Step | Before | After |
|---|---|---|
| World pass warm-up (73 shaders, 73 pipelines) | 14.3 s | 2.0 s |
| Objects and characters (96 fixed pipelines and the templates of the load) | 5.4 s | 0.8 s |
| First warm-up frame as a whole | 22.3 s | 5.7 s |
| Load to first control, Fast | 32.3 s | 15.3 s |
| Load to first control, Standard | 12.5 s | 12.5 s |
| First-person weapons with pistols on (`play.sidearms`) | 3.0 s | 1.1 s |

- **Pipelines of a load are made together.** `RdWorldGpu.Warm`, the fixed object programs and the object queue of the first frames collect the pipelines they need and make them on every core but one (`InParallel`, `MakePipelines`); the render thread waits behind the loading cover. The driver compiles on any thread, as it does for Godot's own pipelines. `SVM_RD_WARM_THREADS=0` goes back to one after another.
- **Shaders of a load are compiled together.** `Precompile` compiles the GLSL this machine has never compiled on the same threads before the variants are made (0.9 s to 0.25 s here; the SPIR-V cache makes it nothing on later loads).
- **Model files are read on worker threads.** `SkeletalModel.Prefetch` reads a file, parses it and has Godot make its scene on a worker; `Load` takes the result and builds materials on the main thread as before. `FirstPersonView.Preload` asks for the mercenary's weapons and the five pistols (about 10 MB each) this way. `SVM_MODEL_THREADS=0` turns it off.
- **`SVM_RD_FRAME first frame`** prints where a load's first Fast frame went, and the warm-up line now splits compile, shaders and pipelines.
- `--svm-sidearms` gives a command-line round its pistols, so their load can be measured.

Left: Godot's own 210 pipelines of the first frame (2.7 s here, the same on Standard), the map's Godot shaders on a machine's first load of a map (`build.cell-materials`, 1.7 s here and 5.5 s on the laptop; the list `ResponsiveSmokeShaders.Prewarm` reads exists only from the second load), and the 2.3 s between the cover lifting and first control on a cold machine.

## Thirteenth stage: frame rate on Fast (2026-11-11, `perf/fast-fps`)

Where the Fast frame went on the UHD 770 at 2560 x 1440 (spawn views, uncapped, `--gpu-profile`), and what each cut
bought. The scene was taken apart first with the measurement switches (`SVM_RD_WORLD_TEST=unlit|noprojectors`,
`SVM_RD_WORLD_OUT`, `SVM_RD_SHADOWS=0`):

| AquaE spawn, GPU ms | Before | After |
|---|---:|---:|
| Scene (`RD Own Frame`) | 3.04 | 2.60 |
| Shadow pass | 0.53 | 0.31 |
| Glow | 0.21 | 0.20 |
| Canvas item that shows the frame (gate and glow) | 0.91 | 0.87 |
| World draws | 728 | 364 |
| Recording the draws (CPU) | 0.31 | 0.20 |
| **Frame ms, FPS (1% low)** | **8.70, 115 (79)** | **7.65, 131 (111)** |

SteeE spawn: 5.95 ms, 168 FPS (1% low 86) to 5.39 ms, 186 FPS (1% low 135).

- **The dynamic lights were the cost of a lit draw, not the spots.** Of the scene's 3.04 ms, 0.89 was the template's
  light code on the 254 draws a light reaches; with the dynamic code forced out it was 0.04. AquaE has six dynamic
  rows and each lit draw is reached by one (266 rows over 253 draws), but a pixel walked the whole row list, with a
  lookup of the light grid for each. Under `SVM_RD_WORLD` `svm_dynamic` now walks the bits the draw names
  (`svm_draw_dyn_bits`, `findLSB`) and touches the grid only on a map with more than 32 rows. Scene 3.04 to 2.67 ms.
  What is left of the lit draws (about 0.5 ms) is one light evaluated at every pixel of them.
- **Shadow tiles with a moving caster are written every other frame at 100 FPS and over** (`SpotShadowLight`,
  `SVM_SHADOW_HALF=0` turns it off). Four spot lights hold the player's character in AquaE's spawn room, so four
  tiles were redrawn every frame; half of them are now, in turn, and each shadow is still drawn fifty times a second
  or more, a frame behind its caster at most. Under 100 FPS nothing changes. 0.53 to 0.31 ms.
- **World batches of zones the camera cannot see are left out** (`Rd/RdZoneVisibility`, `SVM_RD_WORLD_ZONES=0` turns it
  off). The walk goes from the camera's zone through the world model's zone portals (`fogPortals` of the lighting
  file: every portal of the map), narrowing the part of the screen left at each one, as the original walks its zones.
  It starts from the zones a quarter of a metre around the eye too, so a camera pressed against a wall cannot lose
  its own room. A batch's zones are looked up from where its triangles are (`RdWorldRenderer.BatchZones`, on a worker
  thread after the map is built; until it is done everything in the frustum is drawn): a mesh carries the zone of its
  actor alone in its vertices, and one that reaches into the next room must be drawn from there. A camera in zone 0
  and a map without portals cull nothing. AquaE: 4 zones seen, 364 of 728 draws left out, scene 2.73 to 2.60 ms,
  recording 0.29 to 0.20 ms. The depth test already rejected their pixels; what is saved is their vertices and
  their draws.
- **A burning flare now lights the world on Fast.** The small world shaders hold no flare code, so a flare lit only
  the draws a spot or dynamic light already reached. A draw a lit flare's sphere reaches keeps the template's light
  code (`RdWorldRenderer.FlareReaching`). Flares on characters and objects are still missing on Fast: their small
  programs hold no flare code either.

Measured and left alone:

- **Texture filtering.** Trilinear, 4x and 16x anisotropic: scene 2.72, 2.70 and 2.82 ms. Not a cost on this GPU.
- **A separate render thread** (`rendering/driver/threads/thread_model=2`): slower (118 FPS against 125, 1% low 65
  against 97) and it logged errors. Not used.
- **The canvas item that shows the frame** cannot be dropped by drawing straight into the viewport: the glow gate
  scales every pixel by its own finished brightness, on every map, so the finished frame has to be read once.
- **Maps with an outdoor sky** still go to Godot's renderer. None of the 86 installed maps has one (the hub and
  editor-made maps do).
- **A round with responsive smoke** still goes to Godot's renderer whole.

### Automatic render scale

Options, Display, Image quality: **Automatic render scale** (`auto_scale_fps`: off, or hold 30 to 144 FPS).
`Options/DynamicRenderScale` reads the frame rate over half-second windows and lowers the root viewport's 3D scale a
5% step at a time (two when far under) while the rate is under 94% of the target, down to 50%; it raises it again
when the pixel count says there is room, or after the rate has held for four seconds (a raise that does not hold is
taken back and the next one waits twice as long, up to a minute). The target is never more than the frame cap or,
with V-Sync, the screen's refresh rate. A step down that gains under 2% is the processor's limit, not the GPU's: the
scale goes back up and holds for fifteen seconds. Render scale is the upper bound. Fast resamples its frame in the
item that shows it, so a lower scale costs no extra pass there; Standard takes the same viewport scale. Not measured
in play yet.

### Shader lists a build carries

A load makes its map's shaders on worker threads from the list its last load saved (`load_shaders/<map>.json`, "Load
time" above), so the first load of a map on a machine had none: `build.cell-materials` was 1.7 s on the UHD 770 and
5.5 s on a UHD 630 laptop. The build now carries a list for each map (`res://LoadShaders/<map>.json.gz`, read when
the user folder has none; `ResponsiveSmokeShaders.BundledList`). `tools/ci/bake-shader-lists.ps1` writes them: one
load of each map by an exported game with `SVM_SHADER_LIST_OUT`, first-run settings in an empty user folder, window
off screen; the export is then made again so it holds them. A list names shader source, which changes when the
template changes, so the lists belong to the build and are baked for each release; a stale or differently set list
only names shaders the load does not ask for. Maps that are not in the content folder at bake time (custom maps)
load as before until their second load. The driver's own pipeline compile, most of a cold first load, is not
touched by this.

## Fourteenth stage: the frame's time outside the GPU (2026-11-11, night, `perf/fast-fps`, local only)

Measured after the 1080p monitor was moved to the motherboard's output, so the UHD 770 drives the display the window
is on (`SVM_BACKGROUND_AT=2560,0`). Placing the window there or off screen made no difference any more (AquaE 1080p
214 against 212 FPS): the copy to the other adapter that earlier sections charge to this PC is gone in this layout.

### More of the scene

| AquaE spawn, 2560 x 1440, GPU ms | Thirteenth stage | Now |
|---|---:|---:|
| Scene | 2.60 | 2.22 |
| Frame ms, FPS (1% low) | 7.65, 131 (111) | 7.22, 138 (110) |

- **A projector's redraws are cut to its screen rectangle** (`RdWorldRenderer.TargetRect`, `SVM_RD_WORLD_TEST=noscissor`
  off). A receiving batch is an 8 m block and was drawn whole; the projection falls only inside the projector's box.
  Scene 2.58 to 2.22 ms on AquaE (five rain-ripple projectors now cost 0.20 ms).
- **Reaching spot slots keep their dynamic flag** (`frame.SpotBits`, bits 16 to 31). The world draws named only the
  low sixteen bits, so the template's mask lost which slots are dynamic and a surface that ignores static spot lights
  (Echelon bit 0) took no torch light either. A fault of the world pass since it was written, not of this night.
- **Flares light characters and objects drawn by the small programs** (`RdObjects.Spot`: `flare_light`, the draw's
  kind holds the count). Written, compiled at load, never seen with a flare lit.
- **Tried and backed out** (`D:\OpenSCCT
d-builds\lit-experiments.patch`): a stage shader with only the spot and
  dynamic light terms for lit draws, and the dynamic lights of static meshes at the vertices. 127 of AquaE's 133 lit
  draws took the small shader and the scene did not move (2.22 ms either way): with the light walk forced empty the
  small shader still cost 0.33 ms over the unlit stage, and the light itself 0.24 ms. The cost of a lit draw is
  being lit at each pixel at all, not the template's size, and only 26 of the draws are static meshes.

### The processor's share

AquaE at three sizes on the Intel-driven display (`RD ...` rows are the timed GPU passes):

| | Frame ms | Timed GPU ms | Rest |
|---|---:|---:|---:|
| 1920 x 1080 | 4.68 | 2.34 | 2.34 |
| 1920 x 1080, render scale 50% | 3.76 | 1.46 | 2.30 |
| 640 x 360 | 2.63 | 0.92 | 1.71 |

About 1.7 ms of every frame does not depend on the picture's size. `SVM_RD_WORLD_STATS=1` now prints where it
goes: `turnMs` (from the end of the engine's draw phase to the start of the next: the game's turn) and `drawMs` (the
draw phase), `preDrawMs`, `gatherMs`, and the bytes allocated a frame.

- **The game's turn and the GPU's frame do not overlap on Vulkan on this GPU.** AquaE 1080p: turn 1.5 ms, draw
  phase 3.2 ms, frame 4.7 ms; at 360p 1.0 + 1.3. The draw phase lasts as long as the GPU takes, whatever
  `frame_queue_size` (2 or 3) and `swapchain_image_count` (2, 3 or 4) are, and with a separate render thread too
  (`thread_model=2`: same 212 FPS). So a millisecond of processor time costs a millisecond of frame, as a
  millisecond of GPU time does. Mailbox presentation is not offered by the driver.
- **On Direct3D 12 they overlap.** SteeE 1080p, same build (`SVM_ALLOW_D3D12=1`, `--rendering-driver d3d12`):
  436 FPS (2.29 ms; turn 0.57, draw 1.69) against 301 FPS (3.32 ms) on Vulkan, although the timed GPU passes are
  slower there (scene 1.21 against 0.78 ms). **AquaE hung on Direct3D 12 after its load**, and the hang left the
  Intel driver unusable: every later process that enumerates Vulkan devices (the game on either GPU, `vulkaninfo`)
  stops at start and cannot be ended, until the driver is restarted. Nothing after that point was measured.
  Do not run Direct3D 12 on this machine unattended again. What AquaE draws that SteeE does not: the shadow pass
  (its tile copies), the glow chain, projectors, lit draws, rain, cloth.
- **Fewer engine reads a frame** (`RdObjects.Gather`): a mesh node out of view costs its layers and its transform
  (its mesh's box is kept from its last full look; 355 nodes on AquaE); a skeleton's bone poses are read once a
  frame for the object pass and the shadow pass together (`RdSkeletonPoses`); an actor's ambient and four lights
  come from `ActorLighting`'s own record of what it wrote (thirteen reads a node fewer).
- **Fewer allocations a frame**: 78 KB to 27 KB on AquaE (the bone matrices were a new array a bone; the sheet
  vertices and coronas likewise). The collector ran 14 times in an 8 s run before, 3 ms a pause.
- AquaE 640 x 360 (the processor's limit): 2.84 to 2.39-2.65 ms between runs (352 to 380-418 FPS); runs of the same
  build differ by 5 to 10% at this size.

Not verified: none of this stage's changes has been through the all-maps picture comparison (the Vulkan driver
hung before it could run), and the zone culling of the thirteenth stage was compared on six maps only (46 views,
no missing geometry). The shader lists have not been baked.

Checked on 2026-11-11 (morning, after a restart): the all-maps comparison of the Fast renderer before this work
against this branch's head (33 map families, 8 views each, 30 FPS cap, on the NVIDIA card) shows no missing geometry;
the views that differ (MuseD's haze, GhosS's screen, MissD's turning machine, rain) differ as much between two runs
of one build. The shader lists are baked (85 maps, 293 KB together) and on main with the rest.

Direct3D 12 on the UHD 770, looked into further (`SVM_ALLOW_D3D12=1`, each run under a watchdog):

- AquaE runs on Direct3D 12 on the NVIDIA card (321 FPS at 1080p): the fault is the Intel driver's.
- With Godot's renderer (`SVM_RD_FRAME=0`), and with Fast stripped of its optional passes, the game dies on
  `Cannot create uniform set because there's not enough room in the RESOURCES descriptor heap`, then
  `0x887a0005` (device removed) at the next buffer it makes: the default
  `rendering/rendering_device/d3d12/max_resource_descriptors` is too small for this map.
- With that heap raised to 524288 nothing is reported, and Fast hangs after the load as before, taking the Intel
  driver with it a second time (a restart again). So the heap is one fault and the hang another.

Each further step on the Intel GPU costs a restart of the machine when it goes wrong, and a path that can leave a
player's graphics driver unusable cannot ship on what is known. Not pursued further without a decision.

The same overlap may be had on Vulkan: with a separate render thread (`thread_model=2`) the render thread's frame
was 4.1 ms of a 4.7 ms frame, because this pass's gathering (`PreDrawOwn`, 0.6 to 0.9 ms) and recording run on it
before the wait for the GPU. Moved to the main thread's turn, only the recording and the engine's own drawing
would stand in front of the GPU's time. Not tried.

Open, in order of what they are worth: a Direct3D 12 path for Intel that does not hang (the overlap is worth 30 to
45% of the frame, more than everything else found); failing that, the processor's turn (a sampled profile through
the runtime's EventPipe is set up in `D:\OpenSCCT
d-builds\prof`, not yet taken); the engine's own time in the
draw phase.

## The target

### The original at 2560×1440 on the UHD 770 (measured 2026-11-10)

Reloaded editor Play Level in the automation sandbox, hidden desktop, gameplay view (HUD and character), same spawn cameras as ours, `tools/benchmark/capture_original.py --cap 240 --width 2560 --height 1440`. The Windows GPU counters showed the play process at 99% 3D use on the Intel adapter, so both figures are below the cap and are what the GPU can do.

| | Original FPS (1% low) | Frame ms | OpenSCCT, world + shadow pass + lean post (1% low) | Frame ms |
|---|---:|---:|---:|---:|
| SteeE spy spawn | 144.3 (137.9) | 6.9 | 121–123 (85–98) | 8.2 |
| AquaE spy spawn, turned 180° | 159.1 (129.7) | 6.3 | 69 (57–58) | 14.5 |

- **The bar is 144 and 159 FPS on these two cameras, not 240.** The 1080p captures sat on the cap; at 1440p the original is GPU-bound too.
- SteeE is 1.3 ms a frame away. AquaE is 8.2 ms away: our frame is 2.3 times the original's.
- The play process is the image `System\SCCT Versus` (no extension) in the sandbox, not `SCCT_Versus.exe`: the Windows per-app GPU preference must name that path, or the capture runs on the Nvidia card (239.9 FPS on both maps, which says nothing).
- **At 1920×1080** the original holds its 240 cap on both cameras (1% low 237) with the Intel GPU 84% busy on AquaE and 94% on SteeE, so its ceiling there is a little above the cap. OpenSCCT on the same cameras (build `build-l7`, profiling on): SteeE 197 FPS (1% low 143, GPU 2.68 ms), AquaE 106 FPS (1% low 88, GPU 6.13 ms); without the world pass 147 and 71.
- `--uncap` refuses in this sandbox (its Reloaded.Core is not the verified build). Not needed at this resolution.
- An OpenSCCT Editor window was open on the Nvidia card during the captures (7% of that GPU).


- 240 FPS at 1440p on this PC leaves 0.8 ms for the whole game on stock Godot (presentation floor above). No renderer fits a lit world, shadows, characters, effects and post in that.
- The original has not been measured where that target sits. Its captures are at 1080p under its 240 cap: 239.5 FPS with a 1% low of 203 on SteeE, with the Intel 3D engine about 95% busy (`tools/benchmark/RESULTS-2026-11-08.md` on `feature/low-end-renderer`), and 226 FPS with a 1% low of 59 on AquaE. A GPU that busy at 1080p is unlikely to hold 240 at 1.78× the pixels. Reloaded's cap setting stops at 240, so the measurement that settles it is the original at 2560×1440 on the UHD 770.
- Until then the bar is: at least the original's mean and 1% low on the same camera, resolution and display path. The reported low-end machine is an i5-1135G7 (Iris Xe) laptop, which presents on its own panel.

## Plan (revised 2026-11-09): our own world renderer, inside Godot's frame

Building our own renderer on RenderingDevice stays the direction for the map: Godot's scene renderer cannot draw a UE2 world cheaply (pre-pass, one uber-shader per pixel, a SubViewport per shadow). What changes is the scope and the order.

- **Scope.** Our code draws the static world: cells, sky zone, later their shadow depth. Godot keeps characters, gameplay objects, particles, effects, vision post, HUD, menus, hub and the capture tools. No `IRenderScene` facade, no replacement for `Skeleton3D`, particles or the post chain. Replacing the rest of Godot's 3D stays possible later and reuses all of this; it is worth about 1.3 ms at 1440p, so it waits for numbers that say it is needed.
- **Fallback is per cell and per frame.** A cell the world pass cannot draw exactly stays on Godot's layer (as cells with animated shadows do now), and thermal, EMF or any unsupported state turns the pass off for that frame. Each map can ship as soon as it matches, compared against Forward+ in the same process.
- **Lighting is designed for cost, not translated.** The original lights a surface with what reaches that surface. The world pass gets per-draw light lists (which spot slots, which dynamic lights, which fog) chosen on the CPU, with pipelines specialised by count, so a pixel that no spot reaches runs no spot code. Vertex fog where the original's fog is per vertex.
- **Order: the hard case first.**
  1. AquaE with its real lighting, shadows lookups and fog in the world pass. Gate: world pass at or under 3 ms at 1440p on the UHD 770 with a picture matching Forward+. If the lighting cannot be made that cheap, no renderer reaches the target and the lighting model is the work. **Built (see "The world pass"): 3.7 ms with the same pixels, 0.7 ms over the gate; portal fog is the part left.**
  2. Spot shadow depth from our own depth-only passes over the same buffers, replacing the SubViewports (3.7 ms on AquaE). Godot draws only the moving casters. **Built (see "The shadow pass"): 3.75 → 0.57 ms on AquaE; our pass draws the moving casters too.**
  3. The post chain: one fused full-screen pass where there are two or three now (1.7–3.0 ms plus the tonemap). **Built (see "The post chain"): 1.8 → 0.5–0.7 ms for the final pass, 0.88 → 0.31 ms for the glow's bright pass; the tonemap copy stays.**
  4. Per-map parity and release behind a setting, Forward+ as the fallback.
  5. Then decide on replacing the rest of Godot's 3D, and on a Godot build with direct presentation, from the numbers at that point.
- **Driver.** Intel pays about 3.4 ms a frame to present through Vulkan and draws slower on D3D12. That choice is worth more than several of the items above on real Intel machines and needs its own measurement on the in-frame build.
