# Enhanced Beta 5 zone fog

Evidence: the B5 `Reloaded.Core.dll` embeds its shader source (local extraction
`local/b5-fixes/content/fog-shaders.txt`, binary string offsets 4412000–4449000).
Native resource setup is at `0x1016d240`; actor/property discovery is at `0x1017c650`.
This document describes the recovered behavior in our own words.

## Portal zones

Distance is radial distance from the view origin, in Unreal units. A fogged zone
contributes coverage for each ray segment inside it: the difference between the
larger of the segment endpoint and the zone's start, divided by end minus start.
Fog colors accumulate with the same coverage weights. Clamp total coverage to
0–1 for opacity; divide the weighted color sum by the unclamped coverage. Thus
splitting one room into equally fogged zones does not change its appearance.
For a zero or negative end-minus-start interval, native setup at `0x1016e630`
uses an inverse range of 1,000,000 instead of disabling the fog; this produces a
sharp boundary at the start distance.

A portal crossing ends the segment in the zone being left and starts one in the
zone being entered. Its plane orientation determines the order. A crossing must
be ahead of the camera, before the surface, and within all polygon edge planes.
Coincident crossings between the same directed pair count only once; the native
distance tolerance is 0.001 times the larger of crossing distance and 1.

The builder at `0x1017ca90` takes BSP polygons with different, nonzero side zones,
whether or not their surface carries the Portal flag. It accepts 3–64 source
vertices. Each inward edge plane is widened by 0.25 uu; edges whose normal length
is below 0.0001 are omitted. OpenSCCT keeps all usable edges instead of imposing
the native sixteen-edge packing limit. Its screen bounds include the widened
polygon, including the extended corners of acute polygons.

The camera zone uses the engine's current blended fog settings. Other zones use
their own settings. Thermal has no distance fog and EMF retains its dedicated
fog settings. Sky rendering has its own view and settings.

Our conversion preserves portal geometry separately from collision. Candidate
selection is conservative screen tiling and does not impose a portal-count cap.
This is an implementation choice, not a decoded B5 performance constant.

## Volume construction

The builder accepts the exact `SFogVolume` class with positive `FogDistance`.
Fog colour is the authored RGB byte value divided by 255. Extinction is
`3 / FogDistance`. `HeightFalloff` and `EdgeFade` retain their authored values.
When `bLimitToZone` is set, the actor's location selects its BSP zone; an invalid
zone makes the volume unrestricted. No supplied B5 map has a placed volume.

Brush BSP traversal produces convex inside leaves (`0x10179ec0`). Planes transform
by the inverse transpose and normalize in world space. The serialized Model's
`RootOutside` value is required to determine inside leaves. Traversal is bounded
at 8,192 entries and 4,096 nodes; packed pieces have at most 24 planes. The native
fallback (`0x1017a910`) samples a box near the actor's origin when a usable convex
brush cannot be recovered. The converter includes that fallback and reports it
in its conversion notes instead of silently discarding the shape.

The floor search (`0x1017c0d0`) samples XY cell centers at a spacing of the larger
of 16 uu and the bounds' largest horizontal extent divided by 48. It scans upward
from the lower bound plus 1 uu in 8 uu steps. The lowest sample both inside a
piece and in a valid world zone becomes the floor; no such sample rejects the
volume. This floor is shared by the volume's pieces.

An open face (`0x1017b390`) needs a strict majority of its sample points to reach
open world space outside the actor after moving 4 uu outward. Samples comprise
the face centroid and each vertex moved 60% of the way from that centroid. Open
planes are packed first. The conversion records culling bounds separately from
the original planes. With positive height falloff, the bounds may be clipped at
`floor + HeightFalloff * max(0, ceil(log2(diagonal * extinction * 1024)))`.
This is a culling optimization, not an extra plane in the fog's shape.

## Volume integration

A ray is clipped to the piece's outward planes. Density is one below its floor;
above it, density halves over every positive `HeightFalloff` distance. A zero or
negative falloff leaves density constant. The height-density integral is exact,
including rays crossing the floor and horizontal rays. An open face fades density
over `EdgeFade`; closed faces do not fade. At each end of a ray segment in the
fade band, the implementation multiplies the height integral by the mean of the
clamped nearest-face ramp, matching the recovered evaluator's approximation.

Multiply the integrated density by extinction to obtain optical depth. Volumes
restricted to a zone count only ray segments in that zone, using the same distinct
portal crossings as zone fog. Overlapping pieces add their optical depths and
weight their colours by depth. Volume opacity is `1 - exp(-depth)`. Zone and
volume transmittances multiply; their colours combine with their respective
opacity weights before applying the resulting opacity.

The texture-backed surface shader and the SSBO-backed compositor use the same
authored equations in `svm_fog_math.gdshaderinc`. Conversion rows live in local
`render/lighting.json` under `fogVolumes`: colour, extinction, floor, height
falloff, fade, optional zone, normalized planes, open-plane count and culling
bounds. Old content without this field retains its existing behavior.

## Guarded trail code

The binary contains shader code for pawn trails, guarded by `FOG_TRAILS`, but the
examined B5 render path does not enable it. All ten occurrences of that token are
conditional guards; the shader builder at `0x1016cc00` supplies `MAX_VISIBLE` and
`TRAIL_WORDS`, not `FOG_TRAILS`. The constant-buffer allocation at `0x1016d240` and
upload at `0x1016ea20` cover the headers and pieces without trail rows. This is
static evidence that trails are inactive in the shipped path, not a claim that
an original-runtime trail test was performed. OpenSCCT therefore does not invent
a trail lifecycle from this inactive shader code.

## Rendering boundary

Registered scene cameras supply their own fog data through reserved visibility
bits and a shared texture. Materials evaluate fog at their own fragment depth,
including transparent surfaces; the display compositor skips its fog stage for
those cameras. Separate main, sky and auxiliary cameras retain independent data.
Unregistered diagnostic cameras retain the compositor fallback, which samples
opaque depth and therefore retains its transparency limitation.

Synthetic reference tests cover extinction, floor crossing, horizontal falloff,
open and closed faces, rays parallel to a fade face, overlapping colours and zone
limits. The Vulkan fog-volume fixture checks six scenarios against numeric pixel
expectations through both compute and surface paths (twelve phases, plus 96
off-center comparisons). Fifteen pure C# fog tests and seven converter tests pass.
The multi-view surface fixture checks 36 samples with MSAA disabled and at 4x,
including transparent blends, reflection, cloak, retired slots and slot reuse.
Both surface and volume fixtures also pass in the exported Windows debug runtime,
including the resource-remapped shared shader include.
These synthetic checks do not substitute for visual comparison of an authored
fog-volume map in B5.

The retained native shader text includes a deterministic fog dither whose
amplitude is half an 8-bit display step. It appears in the role-16 world tail and
the compositor, but the inspected CPU/builder evidence does not establish the
world draw-role selection for every blend mode. The rewrite does not add this
dither. Tiny banding/quantization differences therefore remain an explicit
acceptance boundary; neither exact native frame identity nor original-runtime
volume acceptance is claimed.

## Performance check

The final local Vulkan check used the refreshed AquaE content (72 boundary
polygons), 22 cameras at 1280×720 on an RTX 4070 Super, and the existing night,
thermal, EMF, motion, torch, laser and emitter checks. All checks passed. Median
fog-frame construction was 0.079 ms (maximum 0.714 ms); it allocated a median
215,564 managed bytes when rebuilt. Camera/settings caching avoids rebuilding an
unchanged frame. Further frame-builder allocation reduction remains possible.

The shared texture grows to the required capacity and reuses bounded managed
scratch, clearing the full table and unused tail before upload. At the measured
80 KiB texture capacity, each upload allocated 376 managed bytes; median upload
time was 0.050 ms (maximum 0.170 ms), down from 0.866 ms in the preceding same-map
run that resized the image with candidate counts. Registered views also skip the
unused compositor SSBO upload. The main viewport's measured GPU time had a median
of 0.322 ms and a maximum of 1.010 ms; those are scene timings, not isolated fog
GPU cost or end-to-end frame-rate guarantees. Logs and numeric summaries remain
under ignored `local/b5-fixes/network/aquae-fog-performance-final*`.
