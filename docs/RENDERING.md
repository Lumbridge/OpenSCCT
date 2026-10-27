# Rendering (Faithful profile)

The Godot client draws a converted SVMap the way the original does: the same display values in the same pixels.
This page holds the rules the renderer follows, in our own words, with the evidence for each number. The code is in
`game/Scripts/Render/` (map building, materials, lights, post, vision, emitters), `game/Scripts/Views/` (pawn views),
`game/Shaders/` and `tools/golden/` (golden captures and scores). Constants live in `RenderK` (`RenderK.cs`,
`RenderK.Vision.cs`) with their `[Provenance]`. Game addresses are of the game DLL (`System/SCCT Versus`, base
0x10900000) unless the editor is named.

The renderer is client only. It reads `render/` of a map folder (spec/svmap/1.1), `sim/collision.bin` for zone
lookups, `sim/gameplay.json` and `sim/classes.json` for gameplay actors, and the shared packs (`.content/shared`),
and never writes simulation state.

## Render pack

The content pipeline's `renderpack` step (`content/svmbuild/steps/renderpack.py`) writes `.content/shared/render/`,
read by `Data/RenderPack` once per folder:

| File | Content |
|---|---|
| `textures/` | mip 0 of the textures the renderer draws with: the thermal table `Engine.heat_and_attenuation_2d` (32 x 32), the grain `Engine.Noise0_gauss` (128 x 128), the backdrop `Engine.sonar_background` (8 x 16), the EMF environment `Echelon.EMFInvDarkBlue`, the torch cookie `Echelon.flashlight`, `Echelon.spot_bw`, the attenuation ramps, the torch corona `sfx.def_flare`, plus every heat and EMF texture of the vision modifiers |
| `vision.json` | every HeatTextureModifier, SpecialVisionTextureModifier and EMFTextureModifier of the texture packages (548), by object path, with its heat texture and type, EMF texture and invert flag after class defaults (SpecialVisionTextureModifier: bUseTextureAsHeat, HEATTEX_Heat, bInvertEMFTexture), chains merged; also by the base material under the chain, for character skins |
| `mips.json` | the stored mip levels of every Texture of the texture, mesh and map packages (5075; 4289 store one level) |
| `gameplay/` | `SGameplayObjects.usx` (class-default meshes of gameplay actors) as `gameplay.glb` with a map-style `materials.json` |
| `render.json` | the texture table and the gameplay mesh table (object path to glTF node) |

Without the pack the client still draws, with a seeded stand-in grain, no thermal table and no gameplay objects.

## Map builder

`MapBuilder.Build(folder)` returns a `RenderMap` (root node, statistics, warnings, gameplay meshes by actor id).

- `world.glb` is read directly (`Gltf/GlbDocument`), not through Godot's importer, so nothing is converted twice.
  Geometry stays in render space, which is Godot space; instance transforms are UE values and go through `UeSpace`
  once: `world = ((local - PrePivot) x Scale) x Rotation + Location`.
- glTF triangles are counter-clockwise, Godot front faces clockwise: each triangle's last two indices swap, unless the
  instance transform mirrors. The handedness check compares the Godot winding of every BSP triangle with its stored
  normal (SteeE: 3693 of 3706 agree; the rest are degenerate or two-sided slivers). The smoke test fails below 99 %.
- Static geometry (BSP and every non-gameplay static mesh with baked colours) is merged per material and per square
  cell of 4096 uu (`RenderOptions.CellSize`): one `MeshInstance3D` per cell, one surface per material and light
  source. SteeE: 9 cells, 318 surfaces, 112,598 triangles. Merged surfaces carry each instance's vision values per
  vertex (`CUSTOM0`, see [Vision modes](#vision-modes)).
- Gameplay meshes (movers, SGamePlayObject, switches, help paths) and meshes without baked colours keep a node of
  their own, named by actor id, so views can move, hide and relight them (`RenderMap.Gameplay`). Hidden ones are
  built with `Visible = false`. Their vision values are instance uniforms.
- Sky zone geometry (BSP nodes with `sky: true`, triangles inside a `skyZone` ZONE record's bounds, instances flagged
  `skyZone`) goes on the sky layer, drawn only by the sky view.
- Skeletal props (`SAnimatedMesh`, DrawType DT_Mesh with a mesh from the shared packs) are placed by
  `AnimatedProps.Build` at the first frame of their first sequence and lit for their mesh's bounding sphere (the
  crane, at DrawScale 10, reaches 2440 uu from its centre). Skinned meshes cull with a 64 m margin, since the pose
  moves vertices away from the rest box. In play, `DeviceViews` draws every SAnimatedMesh (all 54 placed meshes of
  the map anim packages are exported by the chars step, `charconv.MAP_PROP_SETS`) and the skeletal fish: a prop
  shows its actor's animation channel (backwards and looping included, the client's proxy follows the server's
  SimAnim), a fish loops the AnimsFish sequence its script chose and is drawn between ticks as it swims. Static-mesh
  fish are gameplay nodes that follow their actor (spec/devices/fish.md).
- `RenderMap.Actors` holds `sim/gameplay.json` over `sim/classes.json`; `MtvSlope` and `MtvOffset` come from its
  LevelInfo.
- `RenderMap.SwapMeshes`: the meshes gameplay objects change to at run time (SGamePlayObject `TriggeredMesh` and
  `DamagedMeshes[].StaticMesh`: broken extinguishers, lamps switched off, the SteeE crane wall after the blast). The
  converter adds them to `world.glb`'s library (`mapconv/render.py` `_swap_meshes`, scctpkg patch 0002); the builder
  turns every library mesh no instance uses into an actor-lit mesh, and `DeviceViews` shows it when the actor's
  `StaticMesh` changes (before, 683 of the 779 swap references had no render mesh and the node hid). Maps converted
  before this change keep hiding the node until their render step is rebuilt.
- Map emitters fire when the event graph triggers them: the simulation raises the `ReplicateTrigger` notify of the
  Emitter actor (relayed to clients), and `PlayRoot` triggers the particle system of that map id
  (`Emitters.TriggerActor`).

### Gameplay objects

`GameplayObjects.Build` draws gameplay actors with DT_StaticMesh and no mesh of their own in `world.glb` (the
converter writes only meshes an actor stores) from the pack's gameplay meshes: SteeE's first-aid cabinets, window
panes, extinguishers, ammo stocks, camera globes and objective props (67 actors). Placement is the actor's Location,
Rotation, DrawScale x DrawScale3D and PrePivot; light is actor lighting with AmbientGlow, or none with bUnlit.
Mode-dependent props (SDisk, SFlag, SFlagDropZone, SComputerObjectiveTrigger) are built hidden;
`GameplayObjects.ShowMode(map, "Story")` shows what Story starts with (each SFlag as its disk HardDisk_T plus the rack
it spawns, each SFlagDropZone as its case plus the lid it spawns at Location + (0, 0, 10) - 10 X,
spec/objectives/disk-flag.md rules 1 and 4) and `"DiskHunt"` the dog tags. The golden captures are Story games.
Skins a gameplay actor swaps in at run time that the gameplay meshes do not carry are built by `GameplaySkins` and
put over the node by `MeshFollower.DrawSkin` (Skins[0], one skin for every slot): only the unarmed bomb spot's ghost
(`SGameplayObjectsTex.Object.FX_pose_bomb_heat`, fullbright two-sided alpha blend, two frames at 3 fps,
spec/objectives/bomb-target.md rule 14). Any other skin draws the mesh's own materials.

## Display space

The original computes every colour in display (gamma) space and shows the framebuffer as it is: its D3D gamma ramp
is the identity at the default settings. The Faithful pipeline does the same:

1. Materials write display values (textures are sampled raw, no sRGB decode; lightmaps and vertex colours are
   multiplied in display space). Blending therefore happens in display space too, as in the original.
2. `Post/DisplaySpaceEffect`, a `CompositorEffect` after the transparent pass, runs `svm_display_post.glsl` in three
   stages: distance fog; the glow composite of the normal view (gate, plus the glow from `svm_glow.glsl` when the
   level's threshold is below 255); the vision pass of RendMap and the exact sRGB decode. Thermal, motion and EMF run
   the blur pyramid (`svm_vision_blur.glsl`) before the vision pass.
3. The environment passes colours through: Linear tonemapper, exposure 1, no glow, AO, SSR, SDFGI, fog, adjustments
   or auto exposure; no MSAA, TAA or debanding. Godot's output encode is the exact sRGB curve, so the screen shows
   the display value again. Godot linearizes the environment's background colour, so a clear colour is handed over
   sRGB-encoded.

`Post/DisplayCheck` draws 16 known values (0 to 255) through the whole chain and reads them back: every golden run
starts with it, and it must be exact within one step. It reads back exact (maximum error 0).

**Glow.** The normal composite (0x10966010, while PlayerController.DoGlow & 3, default 1) runs the glow driver
0x10967740 in mode 0 and then writes `frame x saturate(16 (dot(frame, (0.311, 0.524, 0.165)) - 0.03)) + glow`
(ps_1_1 0x10c4ea78). The gate crushes near-black and runs at every threshold. The glow, all in 8-bit targets:

1. Bright pass (ps 0x10c4e898) of the whole frame into 512 x 512 (0x30e68): `L = dot(frame, (0.3, 0.59, 0.11))`,
   `T = GlowThreshold / 255` (render/lighting.json `look.glow_threshold`, class default 224); where `L > T` it writes
   rgb `saturate(8 L)` and alpha `saturate(8 (L - T))`, else 0 (c5 = (0, 0, 0, T), so the bright parts go white).
2. Four runs of the vision blur (0x10967d70: a vertical then a horizontal 4-tap pass, each halving one side) give
   256, 128, 64 and 32 texels square (targets 0x30e70, 0x30e78, 0x30e80, 0x30e88; sizes at the default detail).
3. Composite (ps 0x10c4ed00) back into 0x30e68: `0.4 L1 + 0.6 L2 + 0.7 L3 + 0.9 L4`, bilinear.
4. The final composite samples it bilinear over the whole screen.

At threshold 255 nothing passes (L is at most 1), so the passes are skipped (SteeE and four other maps). 79 maps glow
(65 at 224; AquaE 250, PolaE 230, ClubD 227). Only the normal view (and binoculars over it) glows: the night pass
(0x10963090) reads the scene before any composite and runs the driver in mode 1 with its own tint (not ported, see open
issues); thermal, motion and EMF have their own passes. With FlashColor.A set the driver runs mode 2 (ps 0x10c4e920,
the frame lerped to FlashColor first) and the composite adds the flash colour (ps 0x10c4eb10); no script sets it, so
mode 2 is not ported. The original asks for texture filter 5 (gaussian cubic) for the bright pass's 2x reduction; we
sample bilinear.

**Distance fog.** Linear table fog on view depth (D3D W fog: FOGTABLEMODE linear when the device has table and W fog,
0x1095d640), in display space, over what was drawn; where nothing was drawn the clear colour stays. The scene fog setup
(0x10a15fb8) picks it each frame:
- Thermal: no fog. EMF: start 1500, end 2000 uu, colour 0xffffda85 (its clear colour).
- Otherwise the camera zone's `bDistanceFog`, `DistanceFogColor`, `DistanceFogStart`, `DistanceFogEnd`, blended on a
  zone change over the new zone's `DistanceFogBlendTime` (class default 1 s; 41 zones set 0) with PlayerController's
  LastZone, LastViewOrigin, TimeSinceLastFogChange and LastDistanceFog*: a zone without fog blends towards start 32764,
  end 32765 in the last colour; the first zone, a jump of more than 100 uu and a change between two fogless zones
  take the new fog at once; a change while a blend runs starts from where it got to. Colours lerp as bytes, truncated.
  Fog is on while the zone has it or a blend runs.
- The sky zone's own scene takes its zone's fog unblended (AquaE's SkyZoneInfo fogs 0..16 uu, so its sky is the fog
  colour).
- `bClearToFogColor` with `bDistanceFog` (42 zones; 16 more set it without fog, which does nothing): the view clears to
  the zone's `DistanceFogColor` (0x10a3e515; not in thermal or EMF). Both the main and the sky camera clear to it, so it
  shows where neither drew.

Zones store only fields that differ from the class defaults, so the converter writes colour (128, 128, 128), start
3000, end 8000 and blend time 1 when a zone leaves them out (ClubD's ZoneInfo1141 fogs and clears to grey; MissD's
outdoor zones end at 8000).

## Materials

`MaterialCompiler` builds one `ShaderMaterial` per (material, light source) from the template
`svm_world.gdshaderinc`, with defines picked from `materials.json`:

| Group | Values |
|---|---|
| light | `LIGHT_LIGHTMAP` (BSP, UV2), `LIGHT_VERTEX` (baked colours), `LIGHT_ACTOR` (actor lighting), none (fullbright) |
| blend | opaque; masked (alpha test 0.5); translucent (SRCALPHA, INVSRCALPHA); additive (ONE, ONE, also standing in for the screen blend ONE, INVSRCCOLOR); modulate (dest x src x 2); darken (dest x (1 - src)) |
| extras | `SELF_ILLUM` (+ `_MASK`): lit diffuse lerped to SelfIllumination by the mask alpha; `ENV_COLOR`: the env map x SpecularityMask alpha, added (`ENV_TEX`: the env map's own 2D texture looked up by the camera-space reflection `0.25 R.xy + 0.5` like the EMF scene; without it, cube maps, its mean colour); `UV_ANIM`: panner, friendly panner, oscillator (pan and stretch), constant rotator; `OPACITY_LAYER`, `SPEC_LAYER`, `COMBINE`, `FLIPBOOK`: below |

**Layers.** UE2 moves each input of a Shader or Combiner by its own modifier chain. The exporter writes an input
apart when its modifiers differ from the Diffuse's, and the template evaluates it on its own, starting from the mesh's
own UVs (`uv_matrix`, the transform baked into the UVs, is undone first): `OPACITY_LAYER` (an Opacity panning at its
own rate, ClubD's `CLU_Water_01_SH`: alpha from that texture's alpha, or its luminance when the alpha is flat, as the
old bake chose), `SPEC_LAYER` (the SpecularityMask). Otherwise Opacity stays baked into the diffuse's alpha.
`COMBINE` evaluates a Combiner whose inputs animate (AquaE's sea `Aquarium_TXT.osc`: two panners at different rates
under an oscillator; 98 materials in all): Material1, Material2 and the Mask each through their own modifiers after
the Combiner's own, then CombineOperation (Material1, Material2, multiply, add, subtract, `lerp(M2, M1, mask.a)`,
`M1 + M2 x mask.a`, mask colour), x Modulate2X / 4X, and AlphaOperation (mask alpha, multiply, add, Material1's,
Material2's); InvertMask inverts the mask alpha; no Mask means Material1. Static Combiners stay a bake. `FLIPBOOK`
plays an animated texture (Texture.AnimNext chain: AquaE caustics `SC01` 16 frames, ClubD's pool caustic projectors,
AquaE's rain projectors) from an atlas, one frame per 1 / MaxFrameRate s (MinFrameRate when unset, else 30 fps
[estimate: the game then steps a frame per rendered frame]). The game's MaterialSequence screens still show their
first item.

Two-sided materials disable culling. Unlit materials without self-illumination draw fullbright. Shaders are shared
per variant (SteeE: 20 variants, 196 materials). Animated UVs follow the global `svm_time` (Level.TimeSeconds),
which golden renders set to the captured time. Every material also carries its vision modifier's heat and EMF textures
(`heat_mode`, `emf_mode`; SteeE: 19 materials), found by its source path for map materials, by the material name
for gameplay pack meshes and by the base under the modifier chain for character skins (`VisionLookup`).

**Mip levels.** A texture the game stores with one level is drawn without mipmaps; the others keep a generated
chain. Most world textures store one level (SQU13_txt: all of SteeE's walls), so the original samples them at full
detail at any distance, which is why distant brick and tiles read sharp and grainy in the captures. The material
graph (`materials.json` graph) leads from a material input (Diffuse, SelfIllumination, SelfIlluminationMask,
SpecularityMask) to its texture, and `mips.json` gives the count; unknown textures (combined, derived) keep mipmaps.
On the SteeE goldens this alone raised median SSIM from 0.970 to 0.991 and dark-room from 0.995 to 0.9999 (a
-0.5 LOD bias with mipmaps reached 0.984, -1.5 0.980). Colour textures are filtered anisotropically (Godot's 4x
default; the sandbox's Reloaded config sets `filter_level` 4). `--svm-mip-bias=` keeps a LOD bias switch for
calibration.

## Baked light

- **BSP:** `display = min(1, tex x min(2, lightmap x 2 + dynamic))`. The lightmaps are stored at half intensity
  and modulated 2x (decoded). A sweep of the factor on the twelve SteeE goldens: 2.0 gives median SSIM 0.966,
  2.25 gives 0.952, 2.5 gives 0.928; the ~2.5 measured with the UE5 client does not hold here.
- **Static meshes:** `display = min(1, tex x min(2, 2 x VC + glow + dynamic))`, VC the instance's baked colour and
  glow `min(1, 2 x AmbientGlow / 255)` (measured on BankD). The vertex colour stores half the term so it stays exact
  in 8 bits.
- `dynamic` is the sum of the dynamic world lights, the spot shadow light and the night vision boost.

## Dynamic lights

Lights the original keeps out of the baked light (bDynamicLight, triggered STriggerLights, pulsing lights) still
light the world (`WorldLights`): `bDynamicLight` and `bApplyToStaticLighting`, not LE_ESpotShadow, brightness above
0. They live in a float texture (one row per light: position and radius, colour, spot cone, own zone), a global
shader parameter, and are summed per pixel into the baked term.

- Colour, gain and radius are the actor model's (below). The falloff over the world is `1 - (d / R)^2`: in a sweep
  (gain, radius, falloff) on the SteeE shop and yard views it matched far better than the actor falloff (shop SSIM
  0.70 to 0.81, luma ratio 0.54 to 1.00). Measured, not decoded.
- Light types (FDynamicLight::Update, game 0x10a1de70, evaluated once a frame; `WorldLights.Modulation`, shared by
  the world term, actor lighting and coronas): pulse `0.6 + 0.39 sin(a)`, subtle pulse `0.9 + 0.09 sin(a)` with
  `a = t x 35 x 2 pi / max(1, LightPeriod) + (LightPhase << 8)` radians; blink off while the integer part of
  `t x 35 x 65536 / (LightPeriod + 1) + (LightPhase << 8)` is odd (it reads as noise); flicker a fresh random value
  each frame, 0 below 0.5; strobe one switch for all strobe lights, toggled whenever the scene time changes.
- Animated lights that are baked (bApplyToStaticLighting without bDynamicLight: 13, pulse and subtle pulse, all also
  in-game lights) stay steady on the world, as the baked light is; they animate on actors.
- `bAffectOwnZoneOnly` lights light only surfaces of their zone. Each triangle carries its zone in the vertex colour
  alpha: the zone of a point 2, 8 or 32 uu in front of it through the collision BSP; mesh faces that land in solid
  take the actor's zone. A vertex shared by triangles of two zones is written once per zone, so no triangle blends
  zones. Without the rule SteeE's storage view reads 3.4x too bright.
- `WorldLights.SetTune` (`--svm-dyn-tune=gain,radius,falloff,ownzone`) overrides the calibration for sweeps.

## Spot shadow lights

`SpotShadowLight` draws an LE_ESpotShadow light (Echelon spot: the merc torch, searchlights, window and sun
projectors) with its own shadow pass. The frustum is the decoded one (editor 0x111a5480): its apex sits
`p x EchelonRange / (1 - p)` behind the light (p = NearPlaneRatio), the near plane at the light (p x SpotWidth by
p x SpotHeight) and the far plane SpotWidth x SpotHeight, EchelonRange further on; geometry between the apex and the
light never shadows. From p 0.999 the rays are taken as parallel (an orthographic box; FactD's Light17985 has 1.0).
A camera at the apex renders the world into the light's tile of a shared 2048 x 2048 R32F atlas (tiles 1024 x 512,
the map 512 high and as wide as SpotWidth / SpotHeight allows), kept as linear distance along the axis by
`svm_spot_depth.glsl`. The template has `SpotShadowSlots` (8) slots, one float-texture row each (`svm_spots`); it adds,
per pixel, to the light stage of every lit surface (lightmapped, vertex lit and actor lit) each light's colour x 2 x
the cookie at the projected point x the attenuation ramp at d / EchelonRange (d from the light) x the shadow term,
with N.L (towards the apex) weighted by the normal influence. Own-zone lights light only world surfaces of their zone.
Cookies share a texture array (`svm_spot_cookies`, each resampled to 256 x 256; layer 0 white).

- Map LE_ESpotShadow lights (`MapSpotLights`, 364 over all maps) are not in the baked light: the bridge found FactD's
  lightmap uniform under the window patches the game draws with bar shadows (fix_plan I8), so every one that is on
  is drawn, whatever its bApplyToStaticLighting. The six nearest the camera (`SpotShadowMapLights`, estimated) take
  slots; each follows its switch and light type. Cookie: EchelonLightMaterial.ProjectedLightingTexture
  (`echelon_texture_uri`), attenuation its AttenuationType (`echelon_attenuation`, written by the exporter since this
  change; linear, the class default, until the map is converted again), normal influence EchelonNormalInfluence
  (default 1). Lights without a material get no cookie and linear attenuation (estimated).
- Shadow lookups: one filtered lookup (four depth comparisons blended bilinearly, as hardware shadow filtering);
  with `SpotShadowSoftRadius` > 0 (1.5 texels, estimated) eight such lookups on a disc, as the sandbox's Reloaded
  `shadow_edge_smoothing` does. The stock game uses the single filtered lookup.

- `SpotShadowShape.Torch` is SGadTorche's (spec/equipment/merc-flashlight.md): range 1500, 400 x 400, near ratio
  0.0025, brightness 304, hue 21, saturation 255 (white), cookie `Echelon.flashlight` (Echelon.Torche's
  ProjectedLightingTexture), linear attenuation (`Engine.linear_attenuation`), normal influence 0.
- The 2x gain is estimated (the Echelon light pass is not decoded); colour is FGetHSV x brightness / 255 as for
  other lights. `Place(location, rotation)` moves it; views put the torch on the merc's `Laser` bone (NativeTickTorche is not decoded; the UE5 client found
  that bone). `On` follows LightType.
- The sandbox's Reloaded config smooths shadow edges with eight blended lookups and scales shadow maps
  (`shadow_edge_smoothing`, `shadow_quality` 8); the port uses one hard lookup for now.

## Actor lighting

Pawns, skeletal props and meshes without baked colours (`ActorLighting`), decoded from the editor build's actor
lighting:

`display = tex x min(2, A + sum k C S(d / R) max(0, N.L))` over the four strongest lights (Actor.MaxLights), with
`S(x) = 1 - 3x^2 + 2x^3`, `k = 2` for point lights and `1.75` for suns (no falloff), `C = FGetHSV(hue, saturation,
255) x brightness / 255` (0.8213 at V 255), `R = 25 (LightRadius + 1)`, `A` = zone ambient plus AmbientGlow / 255.
Lights are ranked by `brightness x (1 - d^2 / (R + r)^2)`, suns first; spots score 0 outside their cone and use
`phi = 1.15 acos((1 - LightCone / 256)^2)`, `theta = 0.05 phi`. Only `bApplyToInGameLighting` lights that are on
take part, and own-zone lights only for actors in their zone. The light type scales each chosen light's colour (as
for the world lights); ranking uses LightBrightness alone. LE_QuadraticNonIncidence (20) uses the D3D attenuation
`1 / (8 d^2 / R^2)` (with half the point gain, as the 2 of the point fit is not there); LE_PointAmbient (21) also
adds its attenuated colour whatever the normal (its D3D ambient). LE_Cylinder and LE_NonIncidence stay plain points
(not decoded). The set is chosen on the CPU and handed over as
instance uniforms; the shader evaluates it per vertex. The spot shadow light and the night boost are added per pixel.

## Coronas

Lights with `bCorona` draw `Skins[0]` as a camera-facing additive quad at the light (`Coronas`, `svm_corona.gdshader`):
`s = clamp(0.001 x distance, 0.01, 10)`, half size `clamp(s x DrawScale x Fade x 512 x DrawScale3D, Min, Max)` uu,
roll `s x CoronaRotation x 16384 + CoronaRotationOffset`, colour `corona colour x light type x 0.5 x Fade x 2`, no
depth test. The light type factor (pulse, flicker...) is estimated: the corona is taken to follow the light's
FDynamicLight colour.

- The doubling (`CoronaStageGain`, measured): the halo of SteeE's crane lamp reads 1.9 to 2.1 times the undoubled
  halo at every radius, as from a Modulate2X stage on the halved colour.
- Visibility: in radius (`25 (LightRadius + 1)` when set) or the cone test for oriented coronas, a trace against the
  world BSP only, stopping 16 uu short of the light, and a depth test at the light's centre that tolerates
  `max(16 uu, 8 % of the distance)` so the lamp head around a light does not hide it (yard-north's crane lamp sits
  100 uu behind its housing at 2398 uu; lounge's coronas behind floors stay hidden at 8 % and 15 %). A full collision
  trace hides coronas the original shows (blocking volumes, cables); BSP alone lets a corona shine through mesh floors.
- Fade moves at 6 x ScaleGlow per second; a camera cut settles it at once. Thermal and EMF draw no coronas.
- The sky backdrop has the lowest render priority, so a corona or a particle over the sky is never overdrawn by it
  (the crane lamp's corona was).

## Sky zone

When `lighting.json` has a sky zone, `FaithfulView` renders it in a SubViewport from the SkyZoneInfo's location with
the main camera's rotation and projection, through its own display-space pass, and a full-screen quad on the far
plane copies that picture (raw display values) wherever the world drew nothing. The sky camera draws only the sky
layer; the backdrop quad is on a layer the sky camera leaves out.

The sky zone's own walls are fake-backdrop surfaces (SteeE's star box: 8 polygons of `SQU13_txt.Tsky06`, plus the
floor); the exporter now keeps fake-backdrop polygons of the sky zone (`scctpkg` `extract_polygons(...,
include_fake_backdrop=True)` for the sky node only), while the fake-backdrop windows of other zones stay out. Thermal
and EMF draw no sky zone: their frames keep the clear colour there (measured: the EMF sky reads exactly the clear
colour through the EMF pass, the thermal sky the table's far, cold end).

## View

`FaithfulView.SetUeCamera(location, rotation, FovAngle)`:

- The camera basis is UE right, up and backward through `UeSpace` (Godot cameras look down -Z).
- FovAngle is horizontal over a 4:3 picture; wider pictures add width (Hor+), so the vertical angle is
  `2 atan(tan(FovAngle / 2) x 3 / 4)` whatever the aspect (measured on the 1280x720 captures).
- The projection is shifted half a pixel down and right: phase correlation of every capture against our renders gave
  (0.5, 0.5) px, D3D's pixel centre convention. The shift alone raised SteeE's median SSIM from 0.922 to 0.966.
- Near 2 uu, far 65536 uu.
- `RendMap` picks the vision mode (below). `BinocularsOn` and `BinocularsOverlay` are the binocular hook: the HUD's
  page texture drawn at 4:3 in the middle of the view (a CanvasLayer) while SBasePawn.bBinocularsOn is set; mode 50
  only flags the overlay, the renderer keeps drawing the mode under it.

## Drawing between ticks

The simulation runs at 60 Hz; the screen runs at whatever rate the frame cap and vertical sync allow (144 Hz or more).
The original ran its controller and its view once per rendered frame, so everything the player sees moves every frame
and a mouse move is in the frame it arrives in. The rewrite matches that on the screen:

| What | How it is drawn | Code |
|---|---|---|
| Camera location, rotation, FOV (all views: walking, scope, binoculars, sticky cam, camera network) | the calc views of the last two client ticks blended by the clock's fraction into the next tick (alpha); a move over 512 uu in a tick is a cut | `Cameras/PlayCamera.cs`, `Cameras/ViewBlend.cs` |
| Mouse look | while the view is the pawn's own first-person view turned by the controller's plain mouse model (merc `UpdateRotationAttaquant_Mouse`, spy `UpdateCamRotation` without a target snap), the drawn rotation adds the rest of the last tick's input turn, `(1 - alpha) x turn`, and the turn of the counts not yet simulated, both from the decoded formulas. When the tick takes the counts the drawn rotation does not move. A tick whose turn differs from the prediction twice in a row, once on a state change, or not at all (the roll keeps its view), turns this off until a tick matches again (the scope's chasing view, the limited views). The sticky cam and camera network views (`s_UseObject.UpdateRotation`: a tick turns only past the 150 dead zone, then the cam's limits clamp) get the same: their view is the cam actor, which takes the controller's angles a tick later while turning, so that whole tick is drawn ahead too, and during a steady turn the counts of the tick so far are drawn without waiting for the dead zone. Without it the drawn turn speed beat by 1.5x with two or three frames of mouse a tick (144 Hz) | `LookAhead` in `Cameras/ViewBlend.cs`, `PlayInput.PendingMouse` |
| First-person arms and rifle | one-shot clips: the time left is sampled after every tick and blended by alpha (a clip that started at the last tick is drawn from its start); loops advance by the frame time | `Views/ViewModelPose.cs`, `Views/OneShotClock.cs` |
| Scope sights | the aim point (1000 uu along MyDesiredRotation from the eye) is sampled after every tick and blended by alpha, then projected through the drawn camera | `HudBinocularAim` in `Hud/HudBinoculars.cs`, `HudLayer.DrawnAimLocation` |
| Spy's following view (third person) | the same look-ahead while the camera is SPlayerCam `s_Following` and its rotation is the controller's (the spy's `UpdateCamRotation`); the solver puts the camera its distance behind the target along the controller's rotation (spec/native/camera.md rule 3.11), so the location turned ahead moves round the target by `m_prevDistance x (forward(blended) - forward(blended + look))` and the spy stays where the next tick puts him. Without it the spy's turn beat by 1.5x at 144 and 165 fps | `LookAhead.FollowView`, `OrbitDistance`; `PlayCamera.Apply` |
| Scene time (animated materials, light types, map emitters, the HUD's screen effect fades and page time) | the level time of the last two ticks blended by alpha (`DrawnTime`), so it moves every frame as the original's per-frame level time; before, it moved only at the ticks and held still in 58 % of the frames at 144 fps and 75 % at 240 | `Play/DrawnTime.cs`, `RenderMap.SetTime`, `HudLayer.DrawnTime` |
| Map emitters | each frame steps every system by the frame's own time (the engine ticks emitters once per rendered frame); a stretch over 1/60 s runs in 60 Hz steps and then the rest | `Particles/Emitters.SetTime` |
| Pawns, devices, effects | see docs/ANIMATION.md (SimPawnView interpolates location and channels) | |

The zoom curves stay the original's: the scope's 0.4 s step (FOV held 0.25 s, then `target + (previous - target) x
time x 5`, merc-scope.md rule 4) and its kick run per tick in the simulation, and the blend draws them between ticks.
The scope's view chases the aim point at rate 5 per second (rule 6): that lag is the original's and is kept.

`--svm-smoke=smooth` checks it through the player's path (KeyPilot mouse and keys, the launchers' threads) at each
frame cap of `SVM_SMOOTH_CAPS` (default `0,144,60`, 0 uncapped with vertical sync off): a steady turn moves the
camera every frame, a steady turn of a network camera (zoomed, turned to one end of its yaw window first) moves at
an even speed (90th over 10th percentile of the per-frame speed at most 1.3), a single mouse move is drawn in full in
the frame it reaches the game, a zoom's FOV and the scope
clip move every frame, and the sights have no tick-rate saw tooth. It lists frames over 20 ms with the simulation and
view costs and the garbage collections in them; `SVM_SMOOTH_CSV` writes the per-frame curves. The logic tests
`SmoothViewTests` run the same rules against the play session.

Measured on SteeE at 1280 x 720 before the change: a mouse move reached the screen 17 to 28 ms (2 to 6 frames)
after it arrived at 144 fps and uncapped, 16.7 ms at 60; the scope clip held still in 60 to 75 percent of the frames
at 144 fps and above; the sights jumped by up to 5 canvas units a frame. After: 0 frames, no still frames, under 1.5.
Frame pacing: every frame over 20 ms in play was a generation 1 garbage collection (10 to 20 ms pauses, about one
every 1 to 3 s at 144 fps); the process allocates about 20 MB/s at 144 fps, most of it in the client tick, then the
views and the HUD.

## Frame pacing

The frame rate is the player's (Options, Display; `settings.json`). Reloaded caps each side: `fps_spy` in game as a
spy, `fps_merc` otherwise (merc, menus, lobby), 30 to 240 (spec/native/reloaded-features-from-code.md rule 15). Two
values are additions Ryan asked for (2026-10-25): **Monitor** (-1, the default), the refresh rate of the screen the
window is on, and **Unlimited** (0). Reloaded's first run has 120; a version 1 settings file with 120 and 120 (the old
defaults, never chosen) follows the monitor now. Vertical sync (`vsync`) is off by default as the original's render
device (`Default.ini` `[D3DDrv.D3DRenderDevice] UseVSync=False`), whose frames Reloaded's own limiter paces.

| Setting | `Engine.max_fps` | Vertical sync |
|---|---|---|
| Monitor, vsync off | the screen's rate, rounded (59.94 Hz gives 60) | off |
| a rate, vsync off | that rate | off |
| Unlimited, vsync off | 0 | off |
| vsync on, cap at or above the screen's rate (or Monitor, Unlimited) | 0: the swap paces the frames (a sleep limiter at the swap's own rate misses a refresh now and then) | on |
| vsync on, cap under the screen's rate | the cap | on |

`Options/DisplayFrameCap.cs` holds the rules (Godot-free), `SettingsApplier.ApplyPacing` puts vsync and the cap into
effect (the options screen's Save, and at start whether or not a file exists), the play scene's `OptionsRuntime`
chooses the side's cap every frame, and `DisplayFramePacer` (under the root window) follows the window: every frame it
checks the window's screen, every second it reads that screen's refresh rate again, and on a change it applies the cap
again (`SVM_DISPLAY screen N at R Hz`). Godot's own limiter holds a cap to a frame time standard
deviation of 0.01 to 0.07 ms (measured below), so Reloaded's limiter (absolute deadlines, then sleep and spin: rules
16 and 17) is not ported. The latency modes (`low_latency_mode`) and the frame-rate readout's 1 % lows are not done yet.

`--svm-smoke=frame-rate` (`Smoke/FrameRateSmoke.cs`) checks it through the player's path: the spy plays SteeE as the
launchers run it, and for each case of `SVM_FRAMERATE_CASES` (default `monitor,60,144,165,240,unlimited,monitor-vsync,
monitor-move`) the settings are saved and applied as the options screen does, KeyPilot walks the spy and then turns
the mouse steadily, and every frame the drawn camera, the spy's drawn pawn, the map's scene time, its emitters' time
and the HUD's effect time are read after every view has drawn (`FrameStartProbe` notes when each frame began). It
fails when a cap is not met (unless the machine draws fewer frames uncapped, or vsync runs faster than the screen),
when one of those holds still between moving frames, when the steady turn's per-frame speed spreads over 1.3x on
evenly paced frames (else its step jitter is checked), or when a time's step jitter
(`Smoke/FrameMotion.cs`: each frame's step against the step its length asks for, as a share of the mean step) is
over 0.35. `monitor-move` moves the window to the next screen and expects the cap to follow (with one screen it is
only noted). A hitch (over three median frames) and the two frames after it are left out of the motion checks, as is
the screenshot read back after each case. `SVM_FRAMERATE_CSV` writes the per-frame values. The logic tests `FramePacingTests` check the rules, the
drawn time and the spy's following view at 60, 144, 165 and 240 fps.

Measured (October 2026, 1280 x 720 window on a 480 Hz screen, and moved to a 144 Hz one; before -> after this change;
frame time median and standard deviation from the walks; the spy's steady turn: per-frame speed p90/p10; the scene
time: frames it held still and its step jitter):

| Case | fps | frame | spy turn spread | scene time still | scene time jitter |
|---|---|---|---|---|---|
| 60 | 60.0 | 16.67 ms, sd 0.01 | 1.00 -> 1.00 | 0 / 133 -> 0 / 143 | 0.36 -> 0.001 |
| 144 | 144.0 | 6.94 ms, sd 0.04 | 1.51 -> 1.02 | 199 / 340 -> 0 / 345 | 1.17 -> 0.001 |
| 165 | 165.0 | 6.06 ms, sd 0.04 | 1.51 -> 1.02 | 247 / 389 -> 0 / 396 | 1.30 -> 0.002 |
| 240 | 240.0 | 4.17 ms, sd 0.03 | 1.01 -> 1.03 | 426 / 567 -> 0 / 576 | 1.72 -> 0.002 |
| monitor (480 Hz) | 440 to 480 | 2.1 to 2.3 ms, sd 0.6 | 1.33 -> 1.28 | 845 / 988 -> 0 / 1068 | 2.41 -> 0.006 |
| unlimited | 450 to 540 | 1.9 to 2.2 ms, sd 0.25 | 1.27 -> 1.37 | 1094 / 1238 -> 0 / 1042 | 2.77 -> 0.003 |
| monitor, vsync | 370 to 490 | 2.1 to 2.7 ms, sd 0.3 | 1.12 -> 1.22 | 967 / 1109 -> 0 / 870 | 2.58 -> 0.002 |
| monitor, moved to the 144 Hz screen | 144.0 | 6.94 ms, sd 0.05 | 1.02 | 0 / 346 | 0.001 |

The spy's drawn pawn moved every frame at every rate before and after (step jitter 0.001 to 0.01 on a straight walk at
a cap). Above about 300 fps the frame times themselves spread by 1.25 to 1.35x (no cap paces them), so the turn's
speed spreads with them; its step jitter stays near 0.1 there. Godot's limiter holds a cap to within 0.1 ms. This
machine draws SteeE at 350 to 540 fps uncapped, so a 480 Hz monitor cap is not always reached there.

## Vision modes

`FaithfulView.RendMap` (PlayerController.RendMap: 5 normal, 34 night, 36 thermal, 37 motion, 38 EMF) sets the global
`svm_vision` every Faithful material reads, the clear colour, coronas, the sky and the vision-only nodes, and the
post pass draws the mode. All four modes use the grain: `Engine.Noise0_gauss` tiled one texel a pixel, turned by a
random angle in [0, 1) radian and moved by a random offset in [0, 1) each frame (thermal post 0x10965191: rand() /
32768 for each), bilinear. The random sequence is seeded, so renders repeat.

**Blur pyramid** (thermal, motion and EMF; their post passes 0x10964d30, 0x10963dc0 and 0x10965680 each run blur
run 0x10967d70 twice and thermal and EMF composite 0x10967e30). One pass draws its source into a smaller target with
a 4-tap filter along one axis, taps at +-1 and +-3 source texels from the target texel's centre, bilinear, weights
0.332 and 0.168 (ps_1_1 0x34e9b8). Run 1 draws the frame into 512 x 256 (vertical taps) then 256 x 256 (horizontal);
run 2 draws that into 256 x 128 then 128 x 128 (render targets 0x30e6c..0x30e78, default sizes from 0x1090ab98; a
high-detail switch doubles them above 800 pixels of width, not used). The composite (fixed function: texture factor
0x80 blends the levels, vertex alpha 0xC8 blends them over the frame): `h = lerp(frame, lerp(blur1, blur2, 128/255),
200/255)`.

| Mode | Scene | Post |
|---|---|---|
| 34 night | normal scene with a light boost: the pre-render step sets the render interface's byte +0xC to 30; EnableLighting puts it halved into the texture factor added to the lightmap stage, so the light term grows by `2 x 15 / 255`; actors and vertex-lit meshes take the same (measured); no glow gate | `saturate(4 n (L x (0.718, 1, 0.667) + glow))` (ps_1_1: `mad_x2` then `mul_x2` by the grain), L the display luminance (0.3, 0.59, 0.11); the glow term is not fed in yet |
| 36 thermal | the Z pass: `(0, heat, 1 - viewZ / 5000)`; heat is HeatIntensity, or `4 lum(heat texture) x HeatIntensity` (HEATTEX_Cool), or `lum + HeatIntensity` (HEATTEX_Heat) with a heat modifier; translucent surfaces blend `(0, 0, fade)` by alpha x HeatOpacity; HeatOpacity 0 leaves the actor out; additive, modulating and darkening surfaces are left out; clear black | `2 x table(h.g, h.b) x n` (ps_1_1 0x34e11c, texreg2gb), table `Engine.heat_and_attenuation_2d` (u heat, v fade), bilinear, clamped |
| 37 motion | normal scene | `lum = 2 s L(blur2 x n) + MTVOffset / 255`, `s = 1 + 3 MTVSlope / 255`; below 0.5765 a ramp from (0, 20, 40) to (180, 10, 10) by `1.7347 lum`, above it `(0.9, 0.05, 0.05) x (1 - lum)`; x sonar (constants 0x10963f9f..0x10964127, ps_1_1 0x34e278); blur2 is read while PlayerController.SoundWaveDistanceToCamera is 0 |
| 38 EMF | `EMFInvDarkBlue(0.25 R.xy + 0.5) x (1 - EMF glow)` with R the camera-space reflection, x the EMF texture (inverted with bInvertEMFTexture); EMF glow is EMFIntensity while bEMFIsOn; EMFOpacity 0 hides the actor, below 0.99 draws a 4 x 4 screen door; additive surfaces left out; clear 0xffffda85 | `2 x (1 - h) x n x sonar` (ps_1_1 0x34efe0) |

- Motion vision's clean view rectangles: each held lock box of the tracker (HudMotionTracking.CleanRects, the list
  ASMotionTrackingVision::Tick fills at 0x10ac0e30) is redrawn after the motion pass by ps_1_1 0x10c4e328 (loop at
  0x10964996..0x10964c7b over the viewport's list): `2 x (2 x (L(scene) (0.675, 0.75, 0.675) + (0.09, 0.1, 0.09))) x n
  x sonar`, each stage saturated, L of the sharp scene copy (RenderDevice +0x30e24, not the blurred level), the grain
  and the sonar of the motion pass at the same place. `FaithfulView.MotionCleanRects` hands at most eight to the post
  pass as parts of the picture; the box lies under the HUD's corner brackets, as in the captures.
- The sonar backdrop `Engine.sonar_background` (8 x 16) is tiled 80 x 60 times over the frame, bilinear (motion
  pass stage 2 coordinates 80.0 and 60.0; the EMF pass is taken to tile it the same).
- Per-actor values: merged geometry reads HeatIntensity, HeatOpacity, EMF glow and EMFOpacity per vertex
  (`CUSTOM0`, w + 2 marks it present) from the instance table's `vision` (SteeE: 742 instances, mostly HeatIntensity
  0.03 to 0.06 and EMFIntensity 0.07; 94 store HeatOpacity 0 and EMFOpacity 0); actors read the instance uniform
  `svm_vision_actor` (gameplay meshes from `sim/gameplay.json`; pawns HeatIntensity 1 by default; a spy without a
  drawn weapon has EMFOpacity 0, spec/equipment/vision-goggles.md rule 12, read live as 0 in the captures).
  `SimPawnView` writes the pawn's live values (`GameplayObjects.VisionOf`: HeatIntensity, HeatOpacity, EMFIntensity
  while bEMFIsOn, EMFOpacity; replicated on every change) when they change, so a spy shows in EMF only while EMF
  visible; `PawnAttachments` does the same for the spy's pistol from its own actor (1 while the spy is EMF visible,
  0.3 screen door otherwise) and for the merc's rifle dummy (Actor defaults: drawn, no glow).
- Laser beams (`Lasers`: SLaserBeam's LasDetectBeam stretched from start to end; the tripwires' and the mines'
  SLaserBeam_MineLaser, length fTailleFaisceau) are vision-only nodes: drawn only in 36 and 38 (bHideInNormalVision),
  with their own material: the beam's HeatIntensity in thermal (tripwire 1, mine beam 0.4), full EMF glow in EMF.
- Spawned gadget meshes (`EffectViews`) and the map's device nodes (`DeviceViews`) write their actor's live values
  (`GameplayObjects.VisionOf`) when they change, so a device whose bEMFIsOn goes off stops glowing; a map node keeps
  the map's HeatOpacity (not replicated).
- Particles draw the translucent thermal rule for alpha styles and nothing in EMF.
- Not yet: the night glow term and its pre-draws, the heat-source pass (HeatRadius actors; SteeE has none), the
  EMF actor pass details (the original's electronics glow as outlines with halos; ours
  fill), the remanence overlay (PlayerController.RemanenceBlendFactor).

## Cloak

The spy's camo suit is `Actor.GlassEffect` (0 opaque, 255 full glass), ramped and flickered on every machine by
`SPawnAttaque.TickNative` (spec/equipment/spy-energy.md rules 7 and 7a). The original draws an actor with GlassEffect
set in a deferred glass list, only in normal vision (RendMap 5; scene traversal 0x10a395d2, 0x10a39c3a, 0x10a3f1ff):
the pawn shaded normally into its own target, then textured with the frame drawn so far at its screen position plus a
refraction shift, blended by TFactor alpha = GlassEffect, then copied back where it passes the depth test (game
0x10a3bd47..0x10a3be15). Per pixel: `lerp(shaded, behind(uv + shift), GlassEffect / 255)`, no tint or texture. The
shift is `(-k Nx / 640, k Ny / 480)` with `k = 10 (1 - d / 1500)` (0 from 1500 uu), d the camera distance (glass
policy 0x109771f0); that it follows the vertex normal is inferred (the vertex shader is built at run time).

`SimPawnView` hands GlassEffect / 255 to `SkeletalModel.SetGlass` for the body and the attachments (the gun) while the
view's vision is 5. A cloaked model draws in the transparent pass (GeometryInstance3D.transparency 0.01, so the
screen copy holds the scene without it) with `svm_cloak.gdshader` as its material overlay: the screen copy at the
shifted pixel, alpha the glass amount. In night, thermal, motion and EMF vision the spy draws as uncloaked, as in the
original; the cloak also makes the spy EMF visible (TestAndSetEMFVisible) and hides it from motion tracking
(DesiredGlassEffect 255). The goggle glow is its own actor and is not glassed. The spy's own view has no screen effect
(the HUD energy gauge and the suit messages only).

Known differences: the pawn's surfaces in the transparent pass do not write depth, so while partly cloaked its own
parts can overlap in draw order; other translucent surfaces behind the spy are missing from the screen copy.

## Emitters

`Particles/Emitters` draws the map's placed Emitter actors (`render/fx.json`, each sub-emitter's values over the shared
class defaults) with the same particle code as the gameplay effects: `ParticleSystemSim` (AEmitter::Tick) over
`ParticleEmitterSim` (UParticleEmitter's spawning and update) and `ParticleSystemView` (sprite quads facing the
viewport's camera, spark lines, beam strips, `svm_particle.gdshaderinc`). The rules are decoded from the engine:
`spec/native/emitters.md`; the gameplay effects and their converted assets: `docs/EFFECTS.md`. `Trigger(tag)` is
Emitter.Trigger; `RenderMap.SetTime` steps every system at 60 Hz up to the scene time (going back restarts them).
SteeE: 57 sprite emitters (flames, flares and smoke always on; explosions, sparks and debris wait for their events).
Mesh emitters (Fact parts, ORP lamps and mannequins, Aquarium vase shards and fish, Missile bricks, Stee debris)
draw their static mesh from the map library: the converter adds each MeshEmitter's StaticMesh as a library node
flagged `particle` (render.py `_particle_meshes`), MapBuilder builds it actor-lit into `RenderMap.ParticleMeshes`,
and the view draws one MultiMesh per sub-emitter with the mesh's own materials (UseMeshBlendMode), lit once per
emitter by the actor lighting at the emitter (bUnlit off; an Emitter is bUnlit by class default and then draws at
full brightness). Maps converted before this have no such nodes and their mesh emitters still draw nothing (rerun
`maps`).

An emitter or light whose simulated actor rides a mover (AttachTag: ShadD spot1/spot2, ShadP DynamicSpotlights) or
hangs on an SAnimatedMesh bone (ActorsToAttach: Club, Orph, Miss, Stat) follows it (`Views/AttachedEffects`, from
`DeviceViews`): emitters through `Emitters.SetPlace`, lights through `RenderMap.MoveLight` (world term, actor lighting,
corona). A bHardAttach rider keeps its frame in its base's (HardRelMatrix, pitch and roll included); a soft rider is
where the simulation put it; a bone rider sits on the drawn bone (RelativeLocation and RelativeRotation, zero on these
maps). The render-only Engine.Light actors with AttachTag (PanaD/PanaS `elevator`) are bStatic and not bMovable by
class default and baked into the lightmaps: they stay put. The places are taken after the frame's emitters are
simulated, so a moving emitter is one frame behind.

## Rain volumes

`RainVolumes` draws the map's ERainVolumes (`render/fx.json` rain; ClarD, Orph*, Skys*, Stat*, Aqua*) as
AERainVolume::RenderRain does each frame (game 0x10a04d70; the volume's fields sit at 0x304..0x328 in reverse
declaration order: Falloff, FadeScale, DistanceToCamera, maxDistFromCamera, then m_weatherProperties). The world box is
(-X, -Y, 0)..(X, Y, Z) of m_RainSize x DrawScale x DrawScale3D from the volume's Location. Along X and along Y the
camera coordinate is clamped to [min - D/2, max + D/2] (D = m_DistanceToCamera); vertical sheets stand at D/2, D/2 + T,
... either side of it (T = m_RainThickness, 80 when 0), each spanning the box's other horizontal axis and its full
height, with grey vertex colour `round(255 x clamp((Falloff - d) / Falloff x FadeScale, 0, 1))` at distance d. A sheet
is drawn while it lies in [min, max) of its axis, its grey is not 0 and it is within m_maxDistFromCamera of the camera;
the walk stops at the first step that draws neither sheet. So only the nearest sheets show (OrphD: two each way at
75 uu, grey 0.125) and a negative FadeScale (Stat*) draws nothing. Texture coordinates repeat once per m_RainMatScale
uu from a per-sheet offset; the material (StandardRainMaterial: FB_Translucent, drawn additive) pans them.

Estimates: the per-sheet offsets (the game fills its 16-float table at run time), the TexFriendlyPanner as
`scaleU x u + PanU x PanRate x t` (GetMatrix is not decoded), the floor origin of the box (from OrphD's BSP), the
volume's rotation (left out; the shipped volumes turn at most a few degrees), the top-to-bottom tilt RenderRain gives
the sheets from a camera term (not decoded; sheets are vertical). Thermal and EMF leave the rain out (the volume's
HeatOpacity and EMFOpacity are 0), as they do additive surfaces.

## Characters

`SkeletalModel` loads a shared-pack skeletal mesh (spy `SPerso.ATT_01`, merc `SPerso.DEF_01`, props) with Godot's glTF
importer for the skin and skeleton, then frees the AnimationPlayer: `AnimSampler` plays one sequence by name at a
rate (looping or once, UE2 normalized frame) and `Ue2PoseApplier` writes the sampled bone positions, rotations and
scales into the `Skeleton3D`. Placement is UE2's: `Location + PrePivot`, the actor's yaw, then the mesh's RotOrigin
and scale. Materials are the actor-lit template, with the spy's masked self-illumination and the skins' heat and EMF
textures (`SPersoTextures` modifiers: Shadw_agent_Heat, DEF_01_Body_Heat... HEATTEX_Cool). `SetVision` writes a
pawn's vision values. `Views/PawnView` wraps one pawn for views. The core animation channels will replace the sampler.

`SetSlotSkin(slot, material)` puts a material over one material slot, as UE2's `Skins[slot]`: the mesh's materials
are its slots in order, and the skin's texture is the chars step's `textures/Package_Name.png` next to the model,
built on the slot's own material. The first-person view applies its weapon's Skins every frame: the merc rifle's
rate-of-fire screen is `Skins[3]`, which `SWeaponDefense.SetMode` swaps between SI_rof_single (the mesh's own),
SI_rof_burst and SI_rof_auto (the default), so the rifle reads AUTO, BURST or SINGLE as the HUD does. The chars step
exports those three (`SKIN_SWAPS` in `content/svmbuild/charconv.py`); `play-merc` checks the screen at each switch.

## Golden renders

| Step | Tool | Output (local only) |
|---|---|---|
| capture the original | `python -I -B tools/golden/capture_game.py [--rendmap 34\|36\|37\|38]` | `local/golden/game/<Map>/` or `local/golden/game-vision/<mode>/<Map>/`: `<pose>.png`, `cameras.json` |
| render the same views | `python -I -B tools/golden/render_svm.py golden [-- --svm-golden-in=... --svm-rendmap=36]` | `local/golden/svm/<Map>/<pose>.png`, `render.json` |
| score | `python -I -B tools/golden/score.py [--sbs] [--bin 4]` | `local/golden/score/<Map>/score.json`, side by sides |

- Poses are tracked in `tools/golden/poses/<Map>.json`. The capture runs one headless Play Level in the project
  sandbox under the play lock (the difftest recorder's `sandbox.py`), teleports the pawn and writes the controller
  view per pose (memory writes on the local player only, never Physics), hides the HUD with `HUD.bHideHUD`, waits
  until pawn and camera are still for 30 frames and saves the next presented frame (the RE+ headless frame share). It
  records `vCamLocation`, `rCamRotation`, `FovAngle`, `bBehindView`, the pawn's placement, zone, animation channels
  and vision values (HeatIntensity, HeatOpacity, EMFIntensity, EMFOpacity), and `TimeSeconds`.
- `--rendmap` puts the local view in a vision mode before every grab the way the goggles leave it
  (SGoggle.CurrentMode and PlayerController.RendMap set, bGogglesUp cleared) and plays the mode's side (spy for 34
  and 36, merc for 37 and 38). Without a goggle key press the motion tracker actor does not exist (no lock brackets;
  the HUD is hidden anyway). The merc plays in first person: golden renders hide the pawn when `behindView` is 0.
- `render_svm.py` runs the client with a GPU renderer on a desktop of its own (CreateDesktop, never shown), so no
  window appears. `--svm-smoke=golden` renders every recorded camera at the capture's size, with the recorded side's
  pawn at the recorded placement playing the recorded sequence and frame, and the Story objective props
  (`--svm-golden-mode=`). Calibration switches: `--svm-lightmap-scale=`, `--svm-glow-gate=0|1`, `--svm-dyn-tune=`,
  `--svm-mip-drop=`, `--svm-mip-bias=`, `--svm-rendmap=`, `--svm-night-boost=`, `--svm-pawn-heat=`,
  `--svm-golden-only=`, `--svm-golden-debug=coronas`.
- `score.py` reports per pose SSIM on display luma (Gaussian window, sigma 1.5), the luma ratio and the dark-corner
  difference, plus the plan's Faithful gates. `--bin 4` takes SSIM on 4 x 4 block means: vision frames carry a grain
  that changes every frame, which per-pixel SSIM would score as structure.

SteeE, twelve poses at 1280x720 (2026-10-23), before (the renderer at f970fed) and after this work:

| pose | SSIM before | luma before | SSIM after | luma after |
|---|---|---|---|---|
| spy-spawn | 0.9974 | 0.883 | 0.9998 | 0.997 |
| merc-spawn | 0.9768 | 1.023 | 0.9876 | 1.022 |
| basement | 0.9420 | 0.957 | 0.9952 | 1.037 |
| kitchen | 0.9813 | 0.979 | 0.9817 | 0.992 |
| lounge | 0.9739 | 0.983 | 0.9970 | 1.006 |
| shop | 0.8760 | 0.981 | 0.9079 | 0.983 |
| storage | 0.9991 | 0.998 | 0.9995 | 1.003 |
| dark-room | 0.9944 | 0.919 | 0.9999 | 0.994 |
| yard-east | 0.9401 | 0.979 | 0.9958 | 1.000 |
| courtyard | 0.9536 | 0.987 | 0.9876 | 1.003 |
| yard-north | 0.9586 | 0.935 | 0.9828 | 0.983 |
| crane | 0.9212 | 0.791 | 0.9793 | 0.933 |

Median SSIM 0.966 to 0.991, minimum 0.876 to 0.908. Luma failures (gate 0.95 to 1.05) went from four to one. The
causes found, in order of effect: spy-spawn's bright marks are the Story drop zone's suitcase screen (an objective
prop the converter did not export, now drawn from the pack); the crane view lacked the star sky (the converter dropped
the sky zone's fake-backdrop walls) and the crane lamp's corona (overdrawn by the sky backdrop, then too faint by half);
yard-north's corona sat 4.2 % behind its lamp housing (tolerance 4 % to 8 %); dark-room's deficit was texture
sharpness under the glow gate (single-level textures drawn with mipmaps). Lightmap gain, vertex colour scale and fog
were not the cause (unchanged). Remaining: crane 0.933, the crane prop (SAnimatedMesh at DrawScale 10) is dark here
and lit in the original (drawn fullbright it reads 1.17, with light radii scaled by DrawScale 1.12; the original's
rule for lighting scaled skeletal props is not decoded).

Vision modes, the same twelve views captured in each mode (SSIM on 4 x 4 block means):

| mode | median SSIM | min | median luma ratio | notes |
|---|---|---|---|---|
| 34 night | 0.948 | 0.871 | 1.01 | every gate passes (before: 0.818, 0.692; the gate crushed the pawns black and the boost was a fitted 1.4 x 30 / 255); no glow term |
| 36 thermal | 0.945 | 0.805 | 1.05 | the pawn reads hotter than the original (best fit at HeatIntensity 0.65 although it reads 1.0 live); crane 0.805 |
| 37 motion | 0.985 | 0.946 | 0.99 | every gate passes |
| 38 EMF | 0.873 | 0.810 | 0.98 | electronics glow as filled shapes, the original's as outlines with halos |

## Smoke tests

- `--svm-smoke=steee-render` (`--svm-render-map=` for another map): builds the map, checks triangles and the
  handedness test, poses the spy and the merc through the sampler (their thighs must move), and with a renderer draws
  the map from every player start and golden camera (draw calls must stay at most 600: SteeE max 436, median 384),
  checks the night vision pass tints the frame, then runs `Golden/RenderChecks` on the first camera: thermal leads in
  blue over green, EMF is blue, motion draws, the torch lights the frame, a laser beam shows in thermal and not in
  the normal view, and the always-on emitters have live particles. Headless it checks the build only.
- `--svm-smoke=golden`: the golden renders above, after the display-space readback check.

Both need `.content/` (converted content and the render pack), so CI's default set stays `boot` and `testbox`.

## Open issues

- The crane prop's lighting (above). The pawn's heat in thermal. The EMF actor pass (outline glow). The night glow
  term: the night pass runs the glow driver 0x10967740 in mode 1, whose bright pass (ps 0x10c4e830) writes the night
  tint (0.718, 1, 0.667) where `dot(scene, (0.2, 0.393, 0.0733)) > 0.5` (c5 = 0), blurs it through the four-level
  pyramid and composites it (ps 0x10c4ed00); the night pass blurs that once more (0x10967d70) and adds it as `glow`.
  Without DoGlow & 3 it adds a blurred copy of the scene instead. The three full-screen pre-draws of the night pass
  (texture factors 0x7f7f7f7f and 0 at depths 0.9974 and 0.9979) are not decoded.
- Not drawn yet: projectors (`fx.json` projectors are empty on SteeE), light beams, mirrors,
  BeamEmitter / SparkEmitter.
- Spot shadow lights: the Echelon light pass's gain (2), shadow buffer sizes (ShadowZBufferWidthType /
  HeightType), EchelonBias and the original's limit on lights drawn at once are not decoded; bDirectional and
  bIsPerspective are read nowhere yet.
- Some self-lit signs read darker than the original; the crane SAnimatedMesh is posed at its first frame only in the
  render-only views (AnimatedProps.Build; play animates it).
- The pawn's pose uses the base channel only (blend channels are not mixed), so pawns differ slightly in the goldens.
