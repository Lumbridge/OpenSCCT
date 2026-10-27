# Collision primitives: actor cylinders, BSP, static meshes

Units:
- eng:UPrimitive::LineCheck
- eng:UPrimitive::PointCheck
- eng:UModel::LineCheck
- eng:UModel::PointCheck
- eng:UStaticMesh::LineCheck
- eng:UStaticMesh::PointCheck

## Purpose

What a single actor or the level answers when the hash or the level asks "does this sweep hit you": a cylinder for
most actors (pawns included), the BSP with its leaf hulls for the level and brushes, and a collision BSP of
triangles for static meshes.

## Data

- Cylinder: Location, CollisionRadius R, CollisionHeight H. The sweep box has half extent E = (Ex, Ey, Ez).
- Static mesh collision (docs/spikes/C2-staticmesh-collision.md): a per-poly tree (planes +0x104, triangles
  +0x10c, nodes +0x114, vertices from the render stream) and a hull tree (vertices +0x128, planes +0x140,
  triangles +0x148, nodes +0x150). Node children are (triangle, coplanar, back, front).
- UseSimpleBoxCollision and UseSimpleLineCollision are stored on the mesh but not read by any collision code of
  this build (only the editor import and its StaticMesh command write them).

## Rules

### UPrimitive::LineCheck (cylinder), returns 0 on a hit

1. Hit.Time = 1. No owner: no hit.
2. Reject when Start and End are both beyond the same face of the box (R + Ex, R + Ey, H + Ez) around Location.
3. Cap crossings: top plane `Ztop = Location.Z + H + Ez`, bottom `Zbot = Location.Z - H - Ez`. A segment going down
   through Ztop gives t_in = max(0, crossing) with normal (0, 0, 1); going up through Zbot gives t_in with normal
   (0, 0, -1); leaving through a cap shortens t_out (min).
4. Side: with r = R + Ex, solve `|S.XY + t D.XY - C.XY|^2 = r^2` (a = |D.XY|^2, b = 2 D.XY . (S - C).XY,
   c = |(S - C).XY|^2 - r^2). Starting inside (c < 1) with Start between the caps: a hit only when moving inward
   (`D.XY . (S - C).XY < -0.1`), at Time 0, normal = horizontal SafeNormal(S - C), Material = Skins[0].
   Otherwise with a real root and a >= 1e-8: t_in = max(t_in, entering root) with normal = horizontal unit from C
   to the entry point; t_out = min(t_out, leaving root); no hit when t_out <= t_in. With a < 1e-8 and c > 0: no hit.
5. Hit: Time = clamp(t_in - 0.001, 0, 1), Location = Start + D Time, Actor = owner, Primitive = None.

### UPrimitive::PointCheck (cylinder), returns 0 on overlap

Overlap when `|Location.Z - P.Z| < H + Ez` and `|Location.XY - P.XY|^2 < (R + Ex)^2`. Normal = SafeNormal(P -
Location). Hit location: for a steep normal (`|N.Z| > 0.5`) the point above or below the cylinder, else the point
on the side (Location minus Ex along the horizontal normal) at P's height.

### UModel::LineCheck (BSP)

1. No nodes: return the model's RootOutside value.
2. Box sweep (non-zero extent): the UE2 box-versus-BSP sweep with each leaf's convex hull (the box is expanded by
   the hull planes, bevels included), starting with Time 2. On a hit: Time -= f, `f = min(0.1 / |D|, 0.01)` when
   `0.01 / |D| <= 0.01`, else `0.01 / |D|`; clamp to [0, 1]; Location from Time. Returns hit when Time != 1.
3. Line (zero extent): walk the BSP for the first solid crossing; Time = the projection of the crossing point on
   the segment, minus `0.5 / |D|` (pulled back half a unit), clamped; Item = the node found; Normal = that node's
   plane normal; Material = the surface material only with Flags & 0x1000. For a brush model owner the normal is
   rotated into world space (normalised unless the owner's draw scale is 1).

### UModel::PointCheck

Zero extent: descend from the root by the point's side of each node plane, tracking solid or empty from the node
flags; the result says outside or inside, Item = the leaf. Non-zero extent: box against the leaf hulls (UE2 box
point check).

### UStaticMesh::LineCheck

1. Owner with bUseCylinderCollision: the cylinder check above.
2. Otherwise choose the tree: the hull tree when the mesh has one (its node count is non-zero) and Flags lacks 0x100
   (ShadowCast); else the per-poly tree (asserted non-empty). This holds for zero and non-zero extents alike, so
   pawn movement, bullets and clearance tests all use the hull when a mesh has one.
3. The segment is taken into mesh space (owner's world-to-local transform). Tiny direction components are pushed to
   plus or minus 1e-6 before the reciprocals. Zero extent walks the tree testing the segment against each node's
   triangle (front or back child first by the start side, both when the segment spans the plane; a per-query
   mailbox keeps a triangle from being tested twice): a hit inside the triangle edges with `0 <= t < 1` and t below
   the best keeps the triangle plane normal, Item = the triangle, the material slot and the mesh material.
   Non-zero extent uses the box variant of the same walk. The material (game zero-extent walk 0x10a48090): slot =
   the triangle's material word (+6, the same word in hull and per-poly triangles); slot 0xffff gives None; else the
   owner's GetSkin(slot) (AActor 0x1098efa0: Skins[slot] when slot < Skins.Num(), else None), else the mesh's
   Materials[slot].Material, else the engine's default material. The material object is used as it is (no
   FallbackMaterial, no Modifier or Shader unwrapping). The result's +0x30 is the slot. In the shipped meshes no
   triangle of either set has slot 0xffff or a slot past the mesh's material list (all 4961 meshes checked).
4. On a hit: Actor = owner, Primitive = the mesh, Normal normalised in world space, then the pull-back
   `Time -= f`, `f = min(1 / |D|, 0.1)` when `0.1 / |D| <= 0.1`, else `0.1 / |D|`; clamp to [0, 1]; Location from
   Time.

### UStaticMesh::PointCheck

bUseCylinderCollision: the cylinder point check. Otherwise a cylinder point check of the extent against the
collision tree (FCylinderPointCheck over the hull tree slot of the mesh).

## States

None.

## Constants

| Name | Value | Provenance |
|---|---|---|
| cylinder inside test | c < 1, inward dot < -0.1 | decoded |
| cylinder pull-back | 0.001 of Time | decoded |
| BSP box pull-back | 0.01 / |D| or 0.1 / |D| capped 0.01 | decoded |
| BSP line pull-back | 0.5 / |D| | decoded |
| mesh pull-back | 0.1 / |D| or 1 / |D| capped 0.1 | decoded |
| mesh direction epsilon | 1e-6 | decoded |

## Network

None.

## Edge cases

- Most stock hulls are open shells (C2 spike); the hull tree is still a triangle tree here, so the port should
  treat hull collision as triangles, never as convex solids.
- A pawn starting inside another pawn's cylinder can still move away (only inward motion is blocked).

## Evidence

- Editor: UPrimitive::LineCheck 0x110c2400, UPrimitive::PointCheck 0x110c2200, UModel::LineCheck 0x111943e0,
  UModel::PointCheck 0x11193980, UStaticMesh::LineCheck 0x111b7c70 (tree selector 0x111b47a0, line walk
  0x111b6c50, box walk 0x111b7590), UStaticMesh::PointCheck 0x111b80c0.
- Game: UPrimitive::LineCheck 0x10a0f320, UModel::LineCheck 0x10a42f30; UStaticMesh::LineCheck 0x10a49ee0 or
  0x10a49b60 (fingerprint tie).

## Open questions

1. The BSP box sweep internals (editor 0x11193c60, 0x111921f0) are taken as UE2-standard; the drop grid and squeeze
   tests of M3 should confirm them.
2. The non-zero extent mesh walk (0x111b7590) is only outlined.
