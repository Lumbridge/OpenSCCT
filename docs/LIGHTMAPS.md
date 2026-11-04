# How the original baked light

OpenSCCT ships the original lighting: the converter copies each map's cooked lightmap atlases and the static
meshes' baked vertex colours, and the renderer draws them as [Rendering, "Baked light"](RENDERING.md#baked-light)
describes. Nothing is relit. This page records how the original editor's Build Lighting made those values, for the
converter ([bsplight.py](../tools/scctpkg/scctpkg/bsplight.py), [level.py](../tools/scctpkg/scctpkg/level.py)) and
for any future relight of an edited or new map. Where the atlases, the per-leaf light lists and the surface scales
sit in a map package is in [map package layout](../spec/native/map-package.md).

Addresses are in the stock `ChaosTheory_Editor.exe` (image base 0x10e00000; its SHA-256 is the `editor` entry of
[re/binaries.lock.json](../re/binaries.lock.json)). **Decoded** means read from that binary. **Measured** means
compared with the stored atlases or vertex colours of the shipped maps.

**On this page:** [Texel value](#texel-value) · [Light terms](#light-terms) · [Shadows](#shadows) ·
[Shadow filter](#shadow-filter) · [Charts and atlases](#charts-and-atlases) · [Which lights bake](#which-lights-bake) ·
[The cook drops lights](#the-cook-drops-lights) · [Static mesh vertex light](#static-mesh-vertex-light) ·
[Relight check](#relight-check)

## Texel value

A lightmap texel is a saturating byte sum per channel (decoded, `FLightMap::GetTextureData` 0x111b1d10):

1. It starts at the zone ambient: `trunc8(255 x FGetHSV(AmbientHue, AmbientSaturation, AmbientBrightness) / 2)` of
   the surface's ZoneInfo, or of the LevelInfo when the surface has no zone.
2. Each light adds `min(255, 255 x I x 2C)`, the sum capped at 255 (0x111af570, 0x11194c70). `I` is the light's
   effect term below times its shadow (0 to 1). `C` is the light's colour:
   `FGetHSV(LightHue, LightSaturation, 255) x LightBrightness / 510 x` the LightType modulation at bake time
   `x LevelInfo.Brightness` (0x111af860).

The game draws twice the stored value (the lightmap stage is modulated 2x), so the displayed light is `2 x stored`.

`FGetHSV(H, S, V)` (0x11096ab0) is not linear in V. With `v = V x 1.4 / 255`, the brightness is
`b = clamp(0.7 v / (sqrt(v) + 0.01), 0, 1)`; the hue ramp is pulled toward white by `S / 255` and scaled by `b`.
0.8213 (`ActorLightRules.HsvFullBrightness`) is only its value at V 255. `level.py`'s `_hsv_color` implements it and matches the stored
`m_RefAmbientColor` of all 2814 zones of the shipped maps.

Measured zone ambients: PrisD AmbientBrightness 128 stores 73, TermD 200 stores 92, MissD
10 stores 20.

## Light terms

The effect term `I` (decoded, the per-light switch at 0x111b00c0, re-checked by disassembly) uses `x = d / R`, the
texel's distance over the light's radius. `R = max(radius, 1)` is read from the light's FDynamicLight (+0x34), whose
radius is `25 x (LightRadius + 1)` uu. "cos" below is the incidence, the cosine between the surface normal and the
direction to the light.

| LightEffect | Term | Where |
|---|---|---|
| default (point) | `S(x) x cos` | falloff table below |
| LE_StaticSpot (8), LE_Spotlight (12) | point term `x ((cos a - k) / (1 - k))^2` inside the cone, `k = 1 - LightCone / 256`, `a` the angle from the light's direction | compares at 0x111b133a and 0x111b1342, spot path at 0x111b1a2e |
| LE_Sunlight (19) | `max(0, -Dir . N)`; the absolute value on two-sided polygons (PolyFlags 0x100) | 0x111b0208 |
| LE_NonIncidence (13) | `max(1 - x, 0)` | 0x111b134a |
| LE_QuadraticNonIncidence (20) | `max(1 - x^2, 0)`, no incidence term | 0x111b147a |
| LE_Shell (14) | `1 - 10 abs(x - 0.9)` for x in [0.8, 1), else 0 | 0x111b15c0 |

- **Falloff table.** `S(x) = 1 - 3x^2 + 2x^3`. The bake reads it from a 4096-entry table at 0x11aab038, built at
  0x111af1d0: entry `i` holds `S(x) / x` for `x = sqrt((i + 1) / 4096)`, so it is indexed by squared distance,
  `round(4093 d^2 / R^2)`, with the index capped at 0x1000.
- **The spot cone is the old Unreal cone.** The bake's cone is `((cos a - k) / (1 - k))^2`. The run-time actor
  lights use the Direct3D cone instead (`phi = 1.15 acos(k^2)`, [Rendering](RENDERING.md#actor-lighting)), so a
  baked spot and the same spot lighting a pawn have different edges.
- **The sun has no extra gain** in the bake: its colour term is the same `2C` as every light.
- **LE_NonIncidence is linear.** The helper the switch calls (thunk 0x10e029c9, which jumps to 0x10e2e730) is a
  float max, not a square.

Measured: texels lit by a single light give actual / predicted 0.95 to 0.99 (SteeE, PolaD). A leave-one-out fit of
an LE_NonIncidence light on ScheU gives an error of 1.05 for `1 - x` against 1.77 for `(1 - x)^2`.

## Shadows

A light's shadow mask is a GPU depth render from the light, one bit per texel:

- Point and spot lights render a depth view from the light (`FPointLightMapSceneNode`, 0x1119d090); suns render an
  orthographic view along their direction (0x1119d810). No back faces are culled (decoded).
- Occluders are the world BSP and the static meshes with `bShadowCast`. Masked materials let light through.
- A sun texel is lit when the line along `-Dir` reaches the sky.

Two details were measured against the stored atlases, not decoded: the depth comparison has a slack of about
`1.2e-5 x d^2` uu, and the static mesh occluder rules (which meshes cast, and how masked materials pass light).

## Shadow filter

The one-bit mask is filtered before the light is added (decoded, 0x111afee0): a 3x3 kernel

```text
24 40 24
40 64 40
24 40 24
```

over 320, using lookup tables at 0x11aaf038, 0x11aaf03c and 0x11aaf040. Each row's sum is floored before the rows
are added, so a fully lit texel stores 254, not 255.

## Charts and atlases

Each BSP surface gets a chart (decoded, 0x111a0410 and 0x1119ebb0):

- Chart size per axis is `((extent / scale) + 9) & ~7` texels, with the scale doubled until the chart fits in 256.
  Charts have a one-texel border.
- The scale is the surface's `LightMapScaleXBOX`, which is the PC scale (see
  [map package layout](../spec/native/map-package.md#bsp-surfaces-and-polygons)).
- Charts are packed by a skyline packer into 512 x 512 atlases.
- `CompressLightmaps` (0x1119edf0) box-filters each RGBA8 atlas 2 x 2 down to 256 x 256. That filter crosses chart
  borders, so neighbouring charts bleed into each other.

Measured on the shipped maps: most store 512 x 512 atlases, so they were saved without `CompressLightmaps`. Every
atlas of ClarD, CineU, MounU, OffsD, PyroD, ShadD and VertU is 256 x 256, and DeftD and DeftS each have two 256 x 256
atlases among their 512 x 512 ones. What decides whether a map's atlases were compressed is not decoded.

## Which lights bake

- Lights with `bDynamicLight` are never baked, and LE_ESpotShadow (22) lights are skipped.
- A surface takes only the lights listed for its leaves (each leaf's `iPermeating` run in the model's light list,
  0x1119f880). A light that the visibility build did not list for a leaf does not reach it, even in range.

## The cook drops lights

The cook that writes `Maps/` from the editor copy in `MapsEd/` (0x10ee13b0, actor filter 0x10edd1e0) removes every
light except those kept by any of:

- LightType 0 (LT_None);
- `bApplyToInGameLighting`;
- `bDynamicLight`;
- LE_ESpotShadow (22);
- a heat light;
- a saved reference from another object.

The filter tests LightType before the brush and volume test, so an actor of any class with a kept LightType is
treated as a light ([map package layout](../spec/native/map-package.md#cooked-actor-filter)).

Measured over 100 MapsEd / Maps pairs: 21,483 of 34,407 lights dropped, with 0 mismatches against the rule
(SteeE keeps 118 of the 344 in its editor copy). A light kept only because something references it survives as an
orphan export outside `Level.Actors` (824 in 168 cooked files). Orphans carry neither `bApplyToInGameLighting` nor
`bDynamicLight`; see [Maps, converter notes](MAPS.md#converter-notes).

So a cooked map no longer holds most of the lights its lightmaps were baked from. A relight needs the editor copy.

## Static mesh vertex light

Static meshes store one baked colour per vertex (decoded, `UStaticMesh::Illuminate` 0x110e8190 and
`SampleIntensity` 0x111aed50). These were decoded once and not re-verified independently:

- One sample per vertex. No zone ambient is added (the game adds it at run time, see
  [Rendering](RENDERING.md#baked-light)).
- Point: `2 S(x) cos`. Sun: `2 max(0, -N . Dir)`. Spot: the point term times the old Unreal cone above.
  LE_Cylinder: `2 max(0, 1 - (dx^2 + dy^2) / R^2)`.
- LE_NonIncidence: `2 sqrt(1.02 - x)`; LE_QuadraticNonIncidence: `2 (1.02 - x^2)`; both only when the vertex faces
  the light.
- Shadows only when the actor has `bStaticLighting`: one line check from `vertex + 4n` to the light (to
  `vertex - 262144 x Dir` for suns), trace flags 0x197.
- The light and the actor must agree on `bSpecialLit`, and lights named in the actor's `IgnoredEchelonLights` are
  skipped.

## Relight check

Measured: a relight of shipped maps from their editor copies with the rules above against the stock atlases gives
SSIM 0.93 to 0.99 and a mean texel error of 0.4 to 2.5 (0 to 255 scale), for example SteeE 1.05, RiveD 1.01 and
OrphD 0.95.
