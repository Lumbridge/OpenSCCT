# SCCT-Repack rendering experiments — 14 November 2026

Implemented against OpenSCCT parent `15efc973edcab11889df72a1181a21b4128881e5`
in branch `enhancement/g16-bloom-filtering`. Source reference:
[SCCT-Repack](https://github.com/dwojtasik/SCCT-Repack), commit
`b1bc8488e3626e6d5b52ee7b963355e1bff6912f`.

## Result and defaults

- Decode Versus G16 textures as opaque little-endian 16-bit luminance. This
  restores `PCF09_TXT.Projecteur.Proj_egouts`, including the wall diagram in
  CpusS. Projectors also retain their original 16-bit luminance in an optional
  raw texture, rather than quantizing their neutral background to eight bits.
  Eligible static G16 projections use clipped receiver geometry by default.
  Versus format 12 remains P4; the single-player DUDV16 interpretation
  cannot be copied to it.
- Offer **Stable bloom** in Display options. A progressive 13-tap prefilter
  feeds the existing normal-view bright pass in both renderers. The existing
  bloom threshold, gain and level weights remain. The option defaults off
  because the extra pass has a measurable cost on UHD 770. Karis weighting is
  available for experiments through `SVM_BLOOM_FILTER=karis`; it is not the
  user option's mode.
- Offer **High quality shadow filtering** in Display options, using a
  centre-weighted 3x3 tent filter instead of the existing eight-sample ring.
  **Smooth shadow edges** must also be enabled. This defaults off; shadow
  resolution stays at its existing setting. This is an OpenSCCT implementation
  inspired by the repack's quality settings, not a port of single-player shadow
  rendering.
- Provide an optional, local mesh import. Thirty replacements pass compatibility
  checks; 29 distinct assets are used by Prison (240 library mesh variants) and
  three shared assets by MoonD (eight variants). Eighty-four candidates are
  rejected, including incompatible templates and unchanged topology. This is
  a limited set of matching props, not the whole single-player remaster.

The shader adaptation retains the repack's MIT notice in
`packaging/licenses/SCCT-Repack-LICENSE.txt`. Mesh attribution travels with each
converted map; model licences are recorded individually by the source pack.
See [the importer instructions](../assets/README.md#scct-repack-mesh-overrides).

## Measurement

Release export, Godot 4.7.2, Vulkan, native 1920x1080, uncapped and VSync off,
no HUD/local character, no bots. Shadow quality 3 and smooth edges enabled.
Runs render off screen without focusing a game window. The runner rejects
engine/shader errors and flags competing renderer processes. Accepted runs
reported no contention. Main captures use two seconds of warm-up and four
seconds of measurement per view; final mesh close-ups use two seconds of
measurement. These short, local samples do not establish multiplayer or combat
performance, sustained thermals, load-time gains, or performance on other GPUs.

Bloom uses an eight-degree sinusoidal yaw sweep, with the requested camera
verified every rendered frame. AquaE and ClubE use ABBA order, two baseline and
two filtered runs. Numbers below average those runs. ClubE Standard has wider
run variation, so its apparent gains are not evidence of an optimization.

| UHD 770, bloom only | Original FPS, spy / merc | Filtered FPS, spy / merc |
|---|---:|---:|
| Standard, AquaE | 65.7 / 46.7 | 63.6 / 45.4 |
| Standard, ClubE | 63.6 / 78.3 | 64.5 / 76.9 |
| Fast, AquaE | 244.4 / 115.1 | 220.3 / 109.1 |
| Fast, ClubE | 275.8 / 310.4 | 247.3 / 271.1 |

The AquaE Standard cost is about 3%; the Fast cost spans about 5–13%. A separate
RTX 4070 SUPER Standard ClubE pair measured 703.0 / 1171.1 FPS before and
689.6 / 1138.6 after (roughly 2–3% lower). That is one pair, not an ABBA series.
Fast's reported main-viewport GPU timing excludes its custom frame work, so FPS
is the relevant total throughput measurement here.

| UHD 770, AquaE shadow filter only | Existing ring FPS, spy / merc | Tent FPS, spy / merc |
|---|---:|---:|
| Standard | 68.2 / 47.4 | 67.8 / 48.4 |
| Fast | 252.6 / 115.0 | 252.4 / 114.8 |

The tent filter's cost was within run variation in these views. Raising shadow
quality from 3 to 6 doubles the tile height from 768 to 1536 pixels and measured
46.2 / 33.0 FPS in Standard and 222.0 / 94.7 in Fast: about 12–32% slower than
quality 3. Resolution defaults therefore remain unchanged.

| UHD 770, Prison mesh close-up | Standard original → replacement FPS | Fast original → replacement FPS |
|---|---:|---:|
| Propane | 106.2 → 105.5 | 245.0 → 243.9 |
| Bench | 106.0 → 108.2 | 326.4 → 326.7 |
| Bath | 175.4 → 168.9 | 323.5 → 324.6 |
| Fridge | 73.9 → 71.6 | 207.0 → 204.9 |

Mesh close-ups are roughly unchanged in Fast and up to about 4% slower in
Standard. The paired content was built with the same converter and reference
install, with overrides disabled/enabled. Final numbers use the corrected
barycentric light transfer. Earlier nearest-vertex lighting experiments under
`local/bench/quality/mesh-*-after` are superseded.

A separate parent-versus-current comparison, using the same original AquaE
content and both new settings off, measured 67.4 / 47.4 → 66.4 / 46.8 FPS in
Standard and 253.3 / 114.9 → 252.3 / 115.2 in Fast. No material default/off
overhead from the optional filters was established by these AquaE samples.

Restoring the missing CpusS projector has a real rendering cost. Controlled
content from the parent converter (without G16) and current converter (with
G16) was measured using the same current executable and both optional filters
off. Standard uses ABBA order; Fast uses one before/after pair. Audio is muted
by the benchmark runner. The simulation files are byte-identical.

| UHD 770, CpusS G16 restoration | Standard missing → restored FPS | Fast missing → restored FPS |
|---|---:|---:|
| Projector view 0 | 152.5 → 131.8 | 296.2 → 305.4 |
| Projector view 1 | 154.7 → 133.2 | 301.8 → 307.7 |
| Wall diagram close-up (view 2) | 165.3 → 123.8 | 355.0 → 266.1 |
| Projector view 3 | 172.0 → 142.3 | 337.3 → 331.4 |

The Standard cost is about 14–25% in these nearby views. The close-up costs
about 25% in Fast as well; other Fast views vary from slightly faster to 2%
slower. Standard adds two volume draw calls, with about 1–2 ms extra main-view
GPU time. This restores previously absent rendering and exposes a projector
performance concern; the G16 decoding itself is done during content conversion.
These measurements describe the initial full-projector implementation. The
optimized implementation and final measurements below supersede that cost.

## Optimized G16 projector

The static CpusS projector now uses current receiver triangles clipped to its
frustum, with perspective UVs and the original modulation/fog rules. Standard
avoids its scene-depth reconstruction/copy; Fast avoids redrawing whole world
batches. Other world shaders are unchanged. Close views additionally omit the
near-neutral texture area, with a filtering halo. The original G16 samples are
retained as normalized floats: the native background is 32767/65535, whereas the
PNG preview had reduced it to 127/255. No texture detail or render resolution
was reduced.

The final close-up comparison uses one running client per renderer, the same
content/camera/settings, and disabled/full/optimized/optimized/full/disabled
order. Each switch has six seconds of warm-up and six seconds of measurement,
two windows per mode. GPU is UHD 770, Vulkan, native 1920x1080, no bots, HUD or
local body, optional bloom and tent filtering off. Both runs passed camera,
engine-error and process-contention checks; their DLL/PCK fingerprints match.
The disabled control hides the projection in the same loaded scene. These are
local scene measurements, not combat or multiplayer benchmarks.

| Renderer | Disabled FPS | Full projector FPS | Optimized FPS | Cost vs disabled |
|---|---:|---:|---:|---:|
| Standard | 162.8 | 122.8 | 158.7 | 2.5% |
| Fast | 755.3 | 455.3 | 694.9 | 8.0% |

The optimized close-up is 29.2% faster than the full
projector in Standard and 52.6% faster in Fast. Absolute
FPS varied between launches, so separate-process exploratory runs are not used
for this comparison. The wider camera sweep was also checked; its water is
animated, so whole-image differences there are not a projector parity metric.
Fast GPU timings exposed by the main Godot viewport exclude its own frame;
Fast comparisons therefore use total frame time/FPS.

The final close-up RGB differences against the full projector are:

| Renderer | Mean absolute error (0-255) | 99th percentile | Channels differing by >4 |
|---|---:|---:|---:|
| Standard | 0.02134 | 1 | 0.02414% |
| Fast | 0.00038 | 0 | 0.00005% |

The diagram remains intact in both. Sparse coverage uses the actual internal resolution and is disabled when any
live view would minify the texture, or when fog/active responsive smoke needs
the neutral draw. Those conditions use the complete clipped mesh. The initial
static-only implementation still switched to the original pass when a moving
receiver entered the frustum. The moving-receiver optimization below supersedes
that behavior for native players and opaque/masked objects. Fixed
masked/animated/displaced receivers, UV modifiers, other blends and multiple
projectors retain the original eligibility guards.

The exported `surface-projector` GPU check passed on both RTX 4070 SUPER and
UHD 770: eleven native-G16 pixel pairs cover fullbright/BSP/vertex light,
moved/scaled smooth geometry, backfaces, fog and vision modes. It also checks
reduced-resolution coverage, active/inactive smoke, and moving/new/hidden/
removed/unsupported receiver handling. The Fast translation explicitly keeps
the manual vertex transform and depth bias. Material-sequence and shadow-cookie
GPU checks pass. CpusS simulation/collision files remain byte-identical to the
controlled baseline; the precise projector payload adds about 0.13 MB to its
render content.

Final raw records: `local/bench/g16-verified/Standard-1` and
`local/bench/g16-verified/Fast-1`; analysis: `local/g16-final-analysis.json`.
The wider Standard sweep is in `local/bench/g16-live/Standard-1`.
Screenshots: `local/review/g16-optimized-final-Standard.png` and
`local/review/g16-optimized-final-Fast.png`. Scripts/cameras:
`local/run-g16-verified.ps1`, `local/cameras/CpusS-compare.json` and
`local/cameras/CpusS-wall-compare.json`. Use only verified records for the final
implementation.

## Moving G16 receivers

Native opaque/masked players and moving objects now sample the precise G16
projection in their existing material draw. The world position comes from the
deformed/skinned vertex, so movement and poses do not require rebuilding
projector geometry. Only affected material variants include this code. Shared
fixed/moving materials get a receiver-local copy with synchronized live
uniforms; the original animation clock continues to own those values.
Leaving the volume, hiding/removing a receiver, changing its material, or
changing maps restores the original binding.

Cloaks retain their existing screen-copy/refraction behavior. Thermal, EMF and
shadow passes omit the marking. Fast now includes eligible actor receivers;
its old volume implementation only projected onto world batches. Unknown
opaque/custom shaders retain the original compatibility pass. The static
projector eligibility restrictions above still apply.

The CpusS close-up was repeated with a posed/animated native spy, three moving
rigid props, a half-cloaked spy, and a spy plus props. Each row uses one client
and disabled/full/optimized/optimized/full/disabled order, three seconds of
warm-up and six seconds of measurement per case. Motion repeats on a six-second
cycle; the final 0.15 seconds settle at the same pose for screenshots.
GPU: UHD 770, Vulkan, native 1920x1080, shadow quality 3, optional bloom/tent
filters off. These are rendering fixtures, not combat/network benchmarks.

| Receiver scenario | Renderer | Disabled FPS | Full pass FPS | Optimized FPS | Optimized cost vs disabled |
|---|---|---:|---:|---:|---:|
| Animated player | Standard | 127.4 | 101.2 | 124.2 | 2.5% |
| Animated player | Fast | 340.2 | 264.0 | 319.9 | 6.0% |
| Moving props | Standard | 130.7 | 103.2 | 126.8 | 3.0% |
| Moving props | Fast | 341.2 | 270.5 | 317.8 | 6.9% |
| Half-cloaked player | Standard | 117.4 | 94.7 | 115.6 | 1.5% |
| Half-cloaked player | Fast | 349.1 | 267.1 | 329.7 | 5.6% |
| Player and props | Standard | 128.7 | 102.1 | 124.3 | 3.4% |
| Player and props | Fast | 333.0 | 267.9 | 311.5 | 6.4% |

The old pass costs 19.4–23.5% in these same fixtures. The optimized path is
16.3–23.5% faster than that pass, with no fallback in any optimized case.
Counters confirm affected moving materials are present in player/prop cases;
the cloak case correctly keeps its cloak material. FPS is the throughput
metric: Fast viewport timings exclude its custom frame, and some integrated
Vulkan timestamp samples were implausible.

Two existing game clients from other tasks remained active on the RTX 4070
SUPER. Their command lines/adapters were checked and only their exact PIDs
(62260 and 63160) were excluded by the contention checker. Other render/export
processes still invalidate a run. All accepted runs passed camera, engine-error
and contention checks under that declared condition. This was not an idle-machine
test. Absolute FPS differs from earlier static measurements; compare modes
within each row. The object rows were repeated after removing unnecessary
hidden-player animation from that fixture. Each run retains its own DLL/PCK
fingerprint; the rendering implementation is the same across these runs.

Settled Standard screenshots differ from the full pass by 0.020–0.026 mean
absolute RGB levels on a 0–255 scale (99th percentile: 1). Fast differences are
below 0.0004 for the individual player/prop/cloak fixtures. Its mixed fixture
has mean 0.054, with 0.1663% of channels differing by more than 4; these changes
are confined to the prop at the right edge, where the new actor projection
adds coverage absent from the old Fast pass. The diagram remains intact.

The expanded `surface-projector` rendered check passes on RTX 4070 SUPER and
UHD 770. Its 23 volume/optimized pixel pairs include a visibly rendered, posed
and skinned spy with the black projector band crossing its body, moving smooth
geometry, half/full cloaks, masked geometry, live uniform changes, fog and
vision modes. The masked checker fixture excludes a two-pixel band at image
discontinuities because the old depth-reconstructed normal has cut-out edge
artifacts; 89.48% of its pixels remain compared on UHD 770. Other pairs compare
the whole image. Lifecycle checks verify receiver-local material restoration
and unknown-opaque compatibility fallback/recovery. The final export passed
`surface-projector`, `material-sequence` and `spot-shadow-cookies` on UHD 770;
build/export and `git diff --check` passed.

The separate existing `cloak-render` smoke still fails its thermal visibility
threshold with 3946 contrast pixels. An exported, unmodified parent produces
the identical failure and pixel count. The new projector-specific cloak/vision
comparisons pass; this is not a clean result for the entire older cloak suite.

Accepted records: `local/moving-verified-accepted.json`, pointing to
`local/bench/moving-verified/`. Analysis: `local/moving-projector-analysis.json`.
Scripts: `local/run-moving-verified.ps1`, `local/run-moving-objects-final.ps1`
and `local/analyze-moving.py`. Visuals: `local/review/g16-moving-Standard.png`,
`local/review/g16-moving-Fast.png`, and
`local/review/g16-moving-player-parity.png`. Final smoke results:
`local/test-results/moving-final-verified.json` and the referenced logs.

## Aqua pawn shadow and high-resolution vision pyramid

The Aqua comparison exposed an existing missing path, separate from the new
spot-shadow filter. `SpyShadowLighting` already calculated the own-spy shadow
darkness for camera timing, but no bitmap was drawn. `SpyShadowProjector` now
renders the real posed/skinned spy into a private 128x128 alpha texture and
projects it onto nearby opaque world receivers. Root tracking, 300-uu light
distance/range and 600-uu cull distance follow the retained original
`SetupOldSchoolShadow`/`ShadowProjector` scripts. Light direction is now ported
from the original weighted light-position calculation, verified in native
function 0x10a363d0; evidence is retained in
`local/reference/actor-shadow-lighting-native.json`.

Only a small, clipped receiver region is drawn, cached until its bounds or
nearby rigid geometry changes. It uses the existing multiplied receiver stage
in Fast and the equivalent material in Standard. It does not reactivate the
full G16 projector. Cloak, zero strength, hidden/dead pawns, thermal/EMF and
distance culling suppress capture. Opaque receiver coverage is implemented;
masked/deformed receiver support and exact legacy bitmap-filter matching
remain limitations. The current silhouette uses a nine-sample tent filter.
The supplied original photo and the rendered spawn views do not have identical
cameras, so this is not a claim of pixel-exact original shadow parity.

The repack's high-resolution pyramid had not been included in the earlier
build. **High resolution vision effects** (`high_quality_vision`, default off)
now doubles the two coarse thermal/motion/EMF levels, adapting its
`remaster/pyramid.inc` Size=512 setting. At 1080p the levels grow from
512/256 square to 1024/512; EMF's coarse halo levels grow too. Colours, gains,
grain, level mixing and fine halo levels stay the same. Normal bloom and the
existing night-vision path are unchanged by this setting.

Standard's high-resolution frame input uses hardware bilinear filtering when
the frame texture supports sampling. This avoids the manual reference's
sixteen image loads per output pixel. The `vision-pyramid` GPU check compares
both paths using checker edges, ramps, fractional coordinates and borders:
maximum error 0.001953, mean 0.000063 in 0–1 values (about 0.50/0.016 on a
0–255 scale). The manual path remains the compatibility fallback.

Final local 1920x1080 UHD 770 samples use matching off/on/on/off cameras and
one running client per row. Other RTX game/editor work remained active, and
UnrealEditor was explicitly excluded by name for these final rendering runs.
Their error/camera/contention checks pass under that declared condition;
these are short comparisons under shared system load, not idle-system or
multiplayer results. Initial unexcluded runs collided with other tasks and
are discarded. Each run retains its own build fingerprint. Fast's rendering
implementation is unchanged by the later Standard-only sampler optimization.

| Renderer | Change | Off FPS | On FPS | Measured FPS cost |
|---|---|---:|---:|---:|
| Standard | Pawn shadow | 63.4 | 61.5 | 3.0% |
| Fast | Pawn shadow | 203.0 | 188.3 | 7.3% |
| Standard | High-resolution thermal | 62.9 | 64.0 | -1.7% |
| Standard | High-resolution motion | 55.0 | 55.8 | -1.6% |
| Standard | High-resolution EMF | 54.5 | 53.2 | 2.4% |
| Fast | High-resolution thermal | 187.6 | 168.5 | 10.2% |
| Fast | High-resolution motion | 173.1 | 158.2 | 8.6% |
| Fast | High-resolution EMF | 151.3 | 130.5 | 13.8% |

Small negative costs in Standard do not establish a performance improvement:
the faster input sampling mostly offsets the added pixels in these views.
An earlier Standard experiment with manual input filtering cost about 43% in
thermal; it is superseded by the sampler path. The optional pyramid is enabled
by `local/Run-Review.ps1` for manual review, without saving over user settings.

Both renderers completed all sixteen Aqua off/on cases. The final export also
passes `vision-pyramid`, `spy-shadow`, `surface-projector`, `material-sequence`
and `spot-shadow-cookies` on UHD 770. The spy-shadow check verifies actual
silhouette changes between standing/crouching, moving-root coverage and the
native suppression cases. Two focused actor-shadow direction/reset/cloak
logic tests pass. Build/export and `git diff --check` pass.

Final raw records: `local/bench/pawn-vision-sampler/Standard` and
`local/bench/pawn-vision-final/Fast`; analysis:
`local/pawn-vision-analysis.json`. Earlier Standard manual-filter records are
kept under `local/bench/pawn-vision-final/Standard`. Matched before/after views:
`local/review/aqua-pawn-shadow-before-after.png` and
`local/review/aqua-vision-pyramid-{thermal,motion,emf}.png`. Final GPU logs:
`local/test-results/pawn-vision-ship-*.{out,err}.log`.

## Visual and correctness checks

Screenshots and derived data stay in ignored local folders:

- `local/review/g16-projector.png`: the missing CpusS wall diagram is restored.
- `local/review/mesh-propane.png`: the propane silhouette is visibly rounder.
- `local/review/mesh-bench.png`: original baked brightness is retained after
  fixing light transfer. Bath and fridge screenshots are limited views and do
  not establish every replacement's visual quality.
- `local/review/shadow-overview.png`, `shadow-detail.png`: the tent comparison
  shows subtle edge changes; the baseline already has soft filtering.
- `local/review/bloom-overview.png`: no obvious halo or brightness regression
  in the inspected view. The motion sweeps rendered successfully, but no
  quantitative reduction in temporal shimmer has been established.

Prison and MoonD rebuild and load. Their simulation files are byte-identical to
controlled baseline builds. Original material slots and transformed UVs are
retained; baked vertex light is sampled from the closest original triangle.
Lightmapped meshes requiring a rebake are explicitly skipped. Prison render
data grows from 127.75 to 133.02 MB; MoonD from 463.99 to 464.39 MB. The exported
game was also checked in vision modes 34, 36, 37 and 38 with the filter settings
on. All requested cameras matched and all runs passed the runner's error check.
An additional run enabled both settings through the settings interface without
environment overrides and confirmed the 13-tap/tent modes were active.

`dotnet build game/OpenScctGame.slnx -c ExportRelease` passed with no warnings or
errors. Focused texture, mesh, synthetic-source, opacity, cache and CLI tests:
28 passed, one reference-install-dependent test skipped. The vendored scctpkg
patch synchronization check and `git diff --check` passed. The exported
`spot-shadow-cookies` smoke passed. The full `options` smoke fails at 800x600 on
both this branch and the unmodified parent, with the same controls exceeding
the window width; it is an existing layout issue, not a clean smoke result.

## Reproduce

Build/export as described in the [benchmark guide](README.md). Camera files and
raw results for this session are local:

- `local/cameras/{AquaE,ClubE,CpusS,PrisD-detail,MoonD}.json`
- `local/bench/quality/bloom-*`, `shadow-*`, `mesh-*-before`
- `local/bench/final/mesh-*-after`, `parent-*`, `current-*`, `vision-*`,
  `settings-enabled`, `moond-import`, `g16-*`
- `local/run-quality.ps1` and `local/run-final.ps1`

For example, from the repository root:

```powershell
$env:SVM_BENCHMARK_WARMUP = '2'
$env:SVM_BENCHMARK_SWEEP_DEGREES = '8'
$env:SVM_BLOOM_FILTER = '13tap' # use '0' for the baseline
$env:SVM_SHADOW_FILTER = 'disc' # use 'tent' for the shadow comparison
tools/benchmark/Invoke-OpenScct.ps1 -Exe local/build/OpenSCCT.exe -Cameras local/cameras/AquaE.json -Out local/bench/repeat -Content .content -Map AquaE -Seconds 4 -Profiling -GpuIndex 1 -Settings @('renderer=Standard','shadow_quality=3')
```

Environment overrides take priority over the user settings. Remove them to test
the Display checkboxes. `SVM_BENCHMARK_SWEEP_DEGREES` defaults to zero; it is
clamped to 0–45 degrees and uses a four-second sweep period. Use the same
content, adapter, view, renderer and process isolation for each pair.
