# Authored and animated materials

Available in OpenSCCT 0.8.120 with Editor 0.2.34. The editor's built-in Help tutorials cover texture import, animation, lighting and shine, depth/collision, emissive baking and original-game export. See the [release notes](releases/0.8.120.md) for validation boundaries.

Authored materials explicitly select the shared version 1 material path. Stock materials retain the existing renderer. The editor and game share `OpenScct.Content.Materials.AuthoredMaterial`, `AuthoredMaterialBinding` and `svm_authored.gdshaderinc`.

`render/materials.json` keeps its existing entries and flags and adds `flags.authored`. The definition contains named texture bindings, channel/inversion/colour-space choices, surface factors, mapping, depth and scene-clock animation. `flags.texture` remains a basic colour/opacity approximation for older readers. Unknown versions or invalid definitions are rejected when loading an authored material.

Supported surface channels are colour, normal/height bump, roughness/gloss, metallic, specular, AO, opacity, emission and detail colour/normal. UV mapping supports tiling, offset, rotation and parallax; triplanar colour/normal mapping is available separately. Clearcoat and anisotropy are analytic approximations; translucent refraction is screen-space. Supplied reflection environments use equirectangular images.

Atlases and numbered sequences support FPS, loop, ping pong, phase, UV scrolling and rotation. Surface maps share one layout; reflection environments remain static. Power-of-two frame dimensions have independent per-frame mip chains. Other frame sizes use base-level bilinear sampling to avoid frame bleed. Normal mips are renormalized, colour mips are generated in linear space, and non-interlaced 16-bit data PNGs retain source precision through float texture loading.

The editor compiler produces deterministic tessellated/displaced geometry and optional collision from a static height map. Patch and material/UV/normal seams are pinned and faded to avoid cracks. Render LODs retain full-resolution collision. Physical relief replaces flat collision. Animated or triplanar build-time displacement is rejected. Per-face and per-mesh-section collision overrides remain in the editable project.

Baked world lightmaps carry `authored_direction` textures. Authored static meshes carry `COLOR_1` as encoded world-space dominant light direction plus coherence; runtime geometry maps that stream to CUSTOM2, leaving vision CUSTOM0 and Echelon CUSTOM1 intact. Irradiance remains in the existing lightmap or vertex colours. Both editor preview and game use the directional data for normal response. This is one dominant lobe, not a full directional lighting field.

Emission can optionally contribute to the static bake, including static-mesh emitters and receivers with occlusion. Textures are averaged into deterministic area-weighted emitters, capped at 256 samples per mesh section. Character illumination remains controlled by placed lights. Material emission does not create heat or EMF activity. Authored shading enters the existing display, fog, shadow and vision conversion pipeline.

Run `--svm-smoke=authored-material` with a rendered Godot window. It checks 23 material variants, visible normal and convention changes, grazing parallax, animation and frame mips, finish/emission changes, static-mesh directional normal response and generated LOD data. The editor repository's material and network check tools cover import, compilation, optional collision, both teams and native SDC export. Broader stock-map visual comparisons and minimum-hardware acceptance are not established by these synthetic checks.
