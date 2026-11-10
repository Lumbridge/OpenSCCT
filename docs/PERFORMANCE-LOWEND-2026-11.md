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
