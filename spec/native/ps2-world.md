# PS2 world collision and authored beam colours

These rules apply to the converted PS2 maps. The source is the shipped
`SC3_ON.ELF` (SHA-256
`86ca75c3f3f07a345fa97970a5cc3a158ed2cfc8f68ca0e0eaa5e3efb3592113`),
with the original LIN object payloads supplying actor properties and mesh data.
They do not change the PC map defaults.

## World geometry trace

The PS2 Actor virtual function at `0x178EC0` is the trace predicate. Its world
geometry branch at `0x179040..0x179058` tests bit 7 of Actor byte `+0x30E` and
returns `TraceFlags & 0x80`. It does not apply the additional PC pawn /
`bBlockPlayers` test in that branch.

Mount Hospital actor export 579, source name 868680, is the staircase beneath
the Rooms 200 / Lift signs. Its original `bBlockPlayers` and `bBlockActors` are
false. The source mesh (map asset table 3, export 94, name 865676) contains
158 render vertices, 84 collision triangles and a separate 14-vertex,
13-triangle hull. No stair geometry was missing. Applying the PC predicate
made a pawn answer the floor below the steps instead of this existing mesh.

The PS2 converter writes `LevelInfo.bPS2WorldGeometryTrace = true` into
`sim/gameplay.json`. It is part of the simulation hash. `CollisionWorld.Build`
reads that explicit marker; its default is false. Only the world mesh
`MeshPoly` / `MeshHull` pawn predicate changes. Actor collision enables,
extent masks, GPOs, attached collision, movers and volumes retain their own
gates. Original actor flags and collision bytes are preserved.

## Cooked vertex colours

The source hospital beam meshes 861781, 865733 and 865623 explicitly use
vertex colour. Their CPU colour arrays are empty, but their cooked GS packets
are not. Each has 176 CPU vertices, a 7,984-byte section packet and a
2,080-byte colour packet.

The section's VIF `UNPACK V4_32` batches at destination `0x8006` contain XYZ
and the original CPU vertex index in W. Their XYZ values match the indexed
CPU positions exactly. The corresponding `UNPACK V4_16` batches at
`0x8008` hold RGBA. Each colour batch is padded to eight entries; padding can
contain unrelated bytes and is not a vertex. Repeated CPU indices must have
identical colours and every CPU vertex must be covered.

All three meshes contain 132 RGBA `(0,0,0,0)` rim vertices and 44
`(128,128,128,128)` vertices. Preserve those byte values: GS texture modulation
uses 128 as unity, and the PC vertex colour path already restores its 2x
term. Normalizing 128 to 255 would apply the factor twice. PC native mesh
colour arrays use BGRA memory order; glTF uses RGBA. Existing nonempty CPU
colour and alpha arrays remain authoritative. Unknown layouts are not
inferred from padding or vertex order.

The reusable source decoder validates the batch pairing, exact index and
position identity, full coverage and repeated-index agreement. The release
caller enables it only for source code `MHO05`, table `map:3`, exports
18, 19 and 20, and requires their original `UseVertexColor` flag. If one of
those meshes has neither a CPU colour stream nor a validated GS stream, the
writer fails instead of silently creating white authored colours.

An independent layout audit also recovered 21 other authored streams. They
are outside this release policy and retain their previous serialized bytes,
including the old neutral fallback. Decoder capability does not mean those
additional visual changes have been approved or tested for release.

## Per-instance beam lighting

The mesh's base packet is a fallback, not the final colour stream for every
actor. Hospital's seven beam actors have seven distinct, nonempty
`StaticMeshInstance` colour arrays, each with 176 entries. Their brightest RGB
channels range from 41 to 67; the base packet's nonblack vertices are 128.
Using the base packet for these actors made source-black interior layers
bright and caused the remaining saturated patch.

The shipped PS2 path establishes the precedence:

- Actor PostLoad `0x174FEC..0x174FFC` passes its mesh and lighting instance to
  the packet builder. Actor byte `+0x312`, bit 7 is `bUnlit`; the source Actor
  and StaticMeshActor defaults and all seven beam actors leave it false.
- The normal engine draw at `0x1EBB68..0x1EBBB4` passes Actor `+0x258`, the
  lighting instance, for every nonempty section. The wrapper at
  `0x331C40..0x331CBC` passes that instance to `0x32FE30`.
- `0x325228..0x325278` selects the nonempty instance colour array before
  considering the mesh's `UseVertexColor` fallback. CPU vertex indices from
  position W select the corresponding instance colours at
  `0x3258DC..0x325944`. Packet emission `0x325B34..0x325B6C` preserves RGB
  and converts alpha to `(A + 1) >> 1`.
- Section submission `0x330380..0x3303C4` uses the instance packet when the
  instance exists. It does not multiply that packet by the base packet.

The source actors/instances are 330/756, 329/755, 331/378, 332/251, 333/250,
334/249 and 347/222. Serialized FColor bytes are RGBA. Each stream is mapped
by the original CPU vertex index and verified against the cooked packet's
positions. All base-black rim vertices remain RGB zero. Distinct instances
must retain separate colour variants even when they share a mesh.

The converter's explicit authored-colour override accepts an actor name,
exact mesh key, ordered vertex-data fingerprint and RGBA bytes. It validates
the binding and count before choosing the override ahead of the PC base-colour
path. The variant cache includes the colour hash. It retains the authored
overlay path, so PC AmbientGlow folding and dynamic lighting do not alter the
recovered stream. With no override, PC conversion is unchanged.

This correction changes the seven beam instances' render colours. It does not
change their geometry, material gain, glow settings, collision or objectives.
The native evidence is a static decode of the shipped ELF and source data,
not a captured original GS frame or a claim of pixel-exact PS2 postprocessing.

## PS2 fixed additive state

The material is `P2_862546.UnknownGlobalPS2_862552`: a two-sided Shader with
OutputBlending 5, Diffuse through TexPanner 862554 to Texture 862553. There is
no opacity or self illumination input, and the texture is not an alpha
texture. Its converted pixels are dark gray, not white.

The shipped native chain is:

- Shader OutputBlending byte `+0x64`, parser `0x326850`, jump-table index 5
  to `0x326888`: set material flag 4.
- Main view `0x1F7C34..0x1F7C5C`: render-map 5 selects draw context 0 via the
  context vtable slot `+0x68` (`0x33C400`, stored at `0x33C4CC`).
- Normal context 0 bypasses the special context branches. With flag 4 and
  without self illumination / opacity flags, `0x3336C8..0x3336F4` writes
  `0x0000005000000068` to GS ALPHA register `0x42`.

That state is `(Cs - 0) * (80 / 128) + Cd`, a fixed additive contribution of
0.625. Texture alpha is not the blend coefficient. Other material and view
branches write different states, so this is not a universal PS2 gain.

`render/materials.json` can carry the explicit byte `ps2_gs_alpha_fix` in a
material's flags. The runtime uses FIX / 128 for that additive material.
Absent metadata retains PC behaviour. The PS2 source profile currently emits
80 only for this proved material identity and validates the complete source
chain before doing so. It does not use the separate PC hospital material's
measured 0.1 workaround.

Both Standard and Fast use the same material template and uniform. Fast
translates the shader and packs its material values; blended surfaces retain
the template's blend and fog rules.

## Validation and limits

The focused collision tests cover PC defaults, both world mesh kinds,
unchanged source flags, non-world GPO / attached controls, collision enables,
and explicit marker / simulation hash selection. The hospital smoke uses
normal local forward and backward input after fixture positioning and checks
the authority and client walking up and down the actual staircase. Fixture
placement does not alter collision or gameplay flags.

The staged content changes only the LevelInfo marker in all fourteen maps,
and three authored colour buffers plus the single material blend flag in
Mount Hospital. Positions, normals, UVs, indices, lightmaps, mesh instances,
source actor flags, collision, audio and UI remain unchanged.

## Reproduction boundary

Byte reproduction of this content update uses the frozen published baseline,
not a fresh full GLB export:

1. `ps2-world-issues/stage_world_profile.py` copies the frozen fourteen-map
   baseline and writes only the explicit LevelInfo marker and canonical hashes.
2. `mho-world/source_mesh_probe.py` recovers the source CPU vertices and GS
   packets from the original decoded MHO LIN stream.
3. `mho-world/stage_beam_colors.py` validates source vertex identity and patches
   only the three existing glTF `COLOR_0` accessors, preserving all other GLB
   bytes and existing names.
4. `ps2_port/ps2_render_profile.py` validates the proved material chain, writes
   its FIX byte, and recomputes the canonical manifest hashes.

Those paths refer to the conversion source bundle. The caller allowlist is
`ps2_bsp/ps2_gs_release_policy.py`; the reusable decoder is
`ps2_bsp/ps2_gs_colors.py`.

A fresh full GLB export is not claimed byte-identical. It generates authored
variant names from the recovered colour MD5 (`__vc7cb90dba`), whereas the
bounded patch intentionally preserves the frozen `__vc46406b8d` identities.
The resulting source colours and geometry can be compared semantically, but
that is a different check from reproducing these staged bytes.

Source recovery and native blend decoding establish these specific rules.
They are not a full PS2 image-parity claim or an all-map gameplay audit.
Matched rendered hospital views remove the broad opaque white rectangle;
the original stacked beam geometry still produces a bright central hotspot.
