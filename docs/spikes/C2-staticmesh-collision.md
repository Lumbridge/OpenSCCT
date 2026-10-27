# C2: static mesh collision (the tail after the render buffers)

**Verdict: GO for decoding; one open question for physics.** Every static mesh export of the 62
shipped `.usx` packages (4,961 meshes, 138.4 MB of tail data) and the 10 meshes stored inside maps
decode to their last byte. Per-polygon collision matches the collision-enabled render triangles on
every mesh. The hull data is decoded completely, but most stock hulls are open shells, not closed
convex solids, so the physics port must treat hull collision as triangles (see Findings).

Date: 2026-10-23. Decoder: `re/formats/scctformats/smcollision.py`
(`python -B -m scctformats.smcollision all`, `... maps` for the meshes inside `.sdc` files).
Output: `local/formats/smcollision/<Package>.json` (per-mesh report) and `_summary.json`.

## Format

scctpkg reads a UStaticMesh up to its render index buffer. What follows, in package versions 172 to
300 (all the shipped ones: 172, 174, 175 and 300):

| # | Field | Encoding | Mesh member (editor) |
|---|---|---|---|
| 1 | wireframe index buffer | compact n, n x u16, i32 revision | `+0xdc` |
| 2 | per-poly planes | compact n, n x FPlane (X, Y, Z, W) | `+0x104` |
| 3 | per-poly triangles | compact n, n x (u16 v0, v1, v2, material); indices into the render vertex stream | `+0x10c` |
| 4 | per-poly BSP | compact n, n x node | `+0x114` |
| 5 | hull vertices | compact n, n x (FVector position, FVector normal), i32 revision | `+0x128`, revision `+0x13c` |
| 6 | hull planes | compact n, n x FPlane, one per hull triangle | `+0x140` |
| 7 | hull triangles | compact n, n x (u16 v0, v1, v2, material); indices into the hull vertices | `+0x148` |
| 8 | hull BSP | compact n, n x node | `+0x150` |
| 9 | hull source triangles | lazy array of FStaticMeshTriangle (the imported collision objects) | `+0x19c` |
| 10 | hull edge index buffer | compact n, n x u16, i32 revision (a line list the editor draws) | `+0x15c` |
| 11 | RawTriangles | lazy array of FStaticMeshTriangle (the render source) | `+0x184` |
| 12 | InternalVersion | i32 (8 in every mesh) | `+0x1b4` |
| 13 | unknown | i32, version >= 120 (0 in every mesh) | `+0x1b8` |

- BSP node: 4 compacts, then a u32 when the version is >= 157 and not 158. The compacts are
  **(triangle, coplanar, back, front)**, -1 for none (RE+ notes guessed front, back, coplanar; the
  geometry says otherwise). On every mesh the first triangle of each coplanar child lies in the
  node's plane, of each back child is not wholly in front and of each front child is not wholly
  behind. Over whole subtrees (120 meshes of SQU13 and FAC) every coplanar subtree is in the plane,
  and 0.1 to 0.2% of back-subtree and 0.5 to 0.8% of front-subtree triangles (hull and per-poly
  trees) sit wholly on the wrong side by more than 0.1 uu; triangles that span a plane go into both subtrees. The u32 holds two 16-bit fields; the
  low half is 0xfffe in almost every node.
  Children are int16 in memory, so a tree holds at most 32,767 nodes.
- Lazy array: i32 (before version 175 it is the absolute file offset just past the array; checked on
  all 1,139 meshes of the older packages), then compact n and n triangles inline.
- FStaticMeshTriangle: 3 x FVector, i32 NumUVs, NumUVs x 3 x (f32 U, f32 V), 3 x FColor (B, G, R, A),
  i32 MaterialIndex, u32 SmoothingMask. NumUVs is compared signed when loading: one mesh
  (`SM_UMP.panel_hacked`) saved garbage there (-987,154,217) in its hull source triangles, which the
  loader reads as no UV data.
- Plane convention: planes face outward; triangles are wound so that `(v1-v0) x (v2-v0)` points
  inward (opposite the stored normal): all but 1 of the 994,261 per-poly and hull triangles.
- The collision flags are script properties of StaticMesh, stored only when they differ from the
  class default. StaticMesh is an intrinsic class (no export in Engine.u), so the defaults come from
  the live class default object (game and editor dumps agree): `UseSimpleLineCollision` False,
  `UseSimpleBoxCollision` True, `UseVertexColor` False. Only one shipped mesh stores
  `UseSimpleBoxCollision` (False, on a mesh without hulls); 32 store `UseVertexColor` True.

Evidence: `UStaticMesh::Serialize` in ChaosTheory_Editor.exe (`0x110e8be0`), the 16-byte and
8-byte array serializers (`0x110e4b60`, `0x110e4c30`), BSP arrays and nodes (`0x110e6c00`,
`0x110e5a20`), the vertex stream (`0x110e5d80`), lazy arrays (`0x110e6d00`) and FStaticMeshTriangle
(`0x110e5280`). After loading, every hull triangle's material word is set to the first section that
has a material; that is not stored.

## Validation on SQU13_STM and FAC_STM

| Check | SQU13_STM (v174) | FAC_STM (v175) |
|---|---|---|
| meshes decoded to the last byte | 182 / 182 | 145 / 145 |
| per-poly triangles | 21,634 | 18,208 |
| collision-enabled render triangles found among them (0.01 uu, any vertex order) | 21,614 / 21,614 | 18,208 / 18,208 |
| per-poly triangles with no render triangle of the same shape | 0 | 0 |
| extra per-poly triangles that repeat a render triangle's corners (two-sided faces) | 20 (29 corner sets) | 0 (1) |
| stored plane agrees with its triangle (normal within 0.999, distance) | 21,634 / 21,634 | 18,208 / 18,208 |
| meshes with hulls | 158 | 120 |
| hull triangles, all reachable from the hull BSP | 2,760 | 6,694 |
| hull pieces (connected by shared corners) | 390 | 578 |
| closed pieces (every edge used twice) | 31 | 28 |
| pieces with tris == 2V - 4 | 30 | 28 |
| open pieces whose holes are planar and that satisfy 2V - 4 once capped | 263 | 357 |
| convex pieces (all corners behind all their planes) | 340 | 443 |
| BSP nodes at the 32,767 cap / meshes over the 5,512 triangle table | 0 / 0 | 0 / 0 |

`sqt_grue_coll01` (the SteeE crane collision): 1 drawn triangle and 1 per-poly triangle; a 28,599
byte tail with 235 hull vertices, 212 hull planes and triangles in 34 pieces, a 352-node hull BSP
holding all 212 triangles, 212 hull source triangles and an 832-entry hull edge list. None of its
pieces is closed and **none of its triangles faces up** (no +Z plane in the whole hull set): the
first piece is a 614 x 458 x 102 box with its top missing.

## Coverage, all 62 packages

| Measure | Value |
|---|---|
| meshes / decoded / failed | 4,961 / 4,961 / 0 |
| per-poly triangles | 904,169; every shape matches a collision-enabled render triangle (4,961 / 4,961 meshes); 4,426 repeated corner sets explain the 967 extra triangles |
| plane agreement | 904,169 / 904,169 |
| meshes with hulls | 2,881 (90,094 hull triangles, all in their BSP) |
| hull pieces | 8,811: 1,281 closed, 1,310 meet 2V - 4, 5,453 more meet it once their planar holes are capped, 7,406 convex |
| BSP side checks (coplanar, back, front) | no violation in any mesh |
| caps | no per-poly or hull BSP at 32,767 nodes, no mesh over 15,000 per-poly or 5,512 triangles |
| meshes inside maps (`... maps`) | 10 on 83 maps, all per-poly only, all match |

The task brief said 64 `.usx`; the reference install holds 62 (`Packages/StaticMeshes`, nothing elsewhere).

## Findings

1. **Hull collision is a triangle set, not a set of closed convex hulls.** Only 15% of stock hull
   pieces are closed. The missing faces point in every direction (on SQU13, FAC, CHU01 and Terminus:
   548 sideways, 231 down, 139 up, 286 non-planar holes), so they are faces the artists left out of
   the collision objects, not an import rule. The hull source triangles equal the hull triangles one
   for one, so nothing was dropped on import. Read as a solid BSP, the crane's open-top boxes turn
   into columns that are solid all the way up (a point 100 uu above the first box classifies as
   inside), which cannot be what the game does; the tree reads naturally as a triangle search
   structure. A port should collide boxes and lines against the hull triangles through this tree,
   not build solids from planes, until R1 shows otherwise.
2. The 2V - 4 rule from RE+ (malformed hulls let players fall through) applies to hulls that are
   meant to be closed; stock data cannot be validated against it. The convex check (all corners
   behind every plane of the piece) holds for 84% of pieces; the rest are concave groups of slabs
   sharing corners.
3. Per-poly collision keeps one triangle per raw triangle of a collision-enabled section, including
   two-sided duplicates that the render buffers merge.

## Verified facts and assumptions

- Verified: the layout (every byte of 4,971 meshes consumed; editor serializers read), BSP node
  field order and side rules, plane convention, per-poly to render correspondence, lazy array offsets,
  StaticMesh flag defaults (live CDO).
- Assumption: the game uses these arrays as loaded and does not rebuild them (RE+ found the same
  0x7fff compares in the game build). Not re-checked here.
- Assumption to test: which set a trace uses. `UStaticMesh::LineCheck` (`0x111b7c70`) reads the hull
  BSP count and branches on the trace extent; the UE2 meaning of the flags (zero-extent traces use
  the simple set when `UseSimpleLineCollision`, box traces when `UseSimpleBoxCollision`, falling back
  to per-poly when there are no hulls) is not yet confirmed in this build.

## Next steps

- P3 (meshes): write per-poly and hull triangles with their trees into SVMap `collision.bin`
  (VERT/TRIS/BSPN/HULL chunks) straight from `smcollision.read_mesh`.
- R1: decode `UStaticMesh::LineCheck` / `PointCheck` in the game build to settle which set each
  trace kind uses and what the node's u32 means; then run the collision oracle (RE+ live
  `trace_box`) on SteeE's crane and a few open-hull meshes.
