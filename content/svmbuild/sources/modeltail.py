"""Full read of a UModel (world BSP or brush) for collision: the node fields and the zone table that
scctpkg.bsp.read_model skips.

Same layout as scctpkg/bsp.py and scctpkg/bsplight.py document (cooked SCCT maps, package version 300):

    tagged properties (None), FBox (25), FSphere (16)
    Vectors, Points            compact n, n x f32[3]
    Nodes                      compact n, each: f32[4] Plane, u64 ZoneMask, u8 NodeFlags, compact iVertPool, iSurf,
                               iBack, iFront, iPlane (next coplanar), iCollisionBound, iRenderBound, 2 x FSphere (32),
                               u8 iZone[0] (back), u8 iZone[1] (front), u8 NumVertices, i32 iLeaf[2], i32 iSection,
                               i32 iFirstVertex, i32 iLightMap
    Surfs                      compact n, each: compact Material, u32 PolyFlags, compact pBase, vNormal, vTextureU,
                               vTextureV, f32[4], f32 LightMapScale, f32, then an FPoly when PolyFlags & 0x0C000000
    Verts                      compact n, each: compact pVertex, compact iSide
    i32 NumSharedSides, i32 NumZones, NumZones x {compact ZoneActor, u64 Connectivity, u32}
    compact Polys, Bounds (compact n, n x FBox), LeafHulls (compact n, n x i32), then data not read here.

The 12-byte zone record was checked on SteeE, OrphD and Entry: with it the next field is the Polys export.

LeafHulls (spec/svmap/1.1/collision.md, BSPN; checked on all 83 shipped maps): a node with iCollisionBound >= 0 owns the
run of i32 that starts there: the node indices whose planes bound the convex solid region behind the node, each with
bit 0x40000000 set when the plane faces the other way, then INDEX_NONE (-1), then the region's bounding box as six
f32 (min x y z, max x y z; 262144 where the region is unbounded). Such a node lists itself and almost always has no
back child (48 brush nodes of the Rive maps are the exception); its polygon lies on or behind every oriented plane.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from typing import List, Optional, Tuple

import numpy as np

from .env import import_formats

PF_INVISIBLE = 0x00000001
PF_NOT_SOLID = 0x00000008
PF_SEMISOLID = 0x00000020
PF_PORTAL = 0x04000000
LEAF_HULL_FLIPPED = 0x40000000
LEAF_HULL_NODE_MASK = 0x3FFFFFFF


@dataclass
class ModelNode:
    plane: Tuple[float, float, float, float]
    zone_mask: int
    flags: int
    vert_pool: int
    surf: int
    back: int
    front: int
    coplanar: int
    collision_bound: int
    render_bound: int
    zone_back: int
    zone_front: int
    num_verts: int
    leaf: Tuple[int, int]


@dataclass
class ModelSurf:
    material: int
    poly_flags: int
    base: int
    normal: int
    texture_u: int
    texture_v: int


@dataclass
class ModelZone:
    actor: int
    connectivity: int
    extra: int


@dataclass
class Model:
    name: str
    vectors: np.ndarray
    points: np.ndarray
    nodes: List[ModelNode]
    surfs: List[ModelSurf]
    verts: np.ndarray                       # (n, 2) pVertex, iSide
    shared_sides: int = 0
    zones: List[ModelZone] = field(default_factory=list)
    polys: int = 0
    bounds: Optional[np.ndarray] = None     # (n, 6) min, max
    leaf_hulls: Optional[np.ndarray] = None

    def polygon(self, node: ModelNode) -> np.ndarray:
        """Point indices of a node's polygon (empty when it has none)."""
        if node.num_verts < 3:
            return np.zeros((0,), np.int64)
        pool = self.verts[node.vert_pool:node.vert_pool + node.num_verts, 0].astype(np.int64)
        if len(pool) < 3 or pool.min() < 0 or pool.max() >= len(self.points):
            return np.zeros((0,), np.int64)
        return pool


@dataclass
class LeafHull:
    """The convex solid region behind a node: planes pointing out of it (UE2 plane, so inside is dot(n, p) < w)."""
    planes: List[Tuple[float, float, float, float]]
    nodes: List[int]                         # node index of each plane
    flipped: List[bool]
    box_min: Tuple[float, float, float]
    box_max: Tuple[float, float, float]


def leaf_hull(model: Model, start: int) -> Optional[LeafHull]:
    """Decode the LeafHulls run of a node's iCollisionBound, or None when it does not fit the layout."""
    lh = model.leaf_hulls
    if lh is None or not 0 <= start < len(lh):
        return None
    planes, nodes, flipped = [], [], []
    i = start
    while i < len(lh) and int(lh[i]) != -1:
        ref = int(lh[i])
        if ref < 0 or ref & ~(LEAF_HULL_FLIPPED | LEAF_HULL_NODE_MASK):
            return None
        node = ref & LEAF_HULL_NODE_MASK
        if node >= len(model.nodes):
            return None
        p = tuple(float(x) for x in model.nodes[node].plane)
        flip = bool(ref & LEAF_HULL_FLIPPED)
        planes.append(tuple(-x for x in p) if flip else p)
        nodes.append(node)
        flipped.append(flip)
        i += 1
    if not planes or i + 7 > len(lh):
        return None
    box = lh[i + 1:i + 7].view(np.float32)
    if not np.all(np.isfinite(box)) or np.any(box[:3] > box[3:]):
        return None
    return LeafHull(planes, nodes, flipped, tuple(float(x) for x in box[:3]), tuple(float(x) for x in box[3:]))


def _vec_array(r, what: str) -> np.ndarray:
    n = r.compact()
    if n < 0 or n * 12 > r.remaining():
        raise ValueError(f"bad {what} count {n}")
    if n == 0:
        return np.zeros((0, 3), np.float32)
    return np.frombuffer(r.raw(n * 12), dtype=np.float32).reshape(n, 3).copy()


def read_model(pkg, export) -> Model:
    import_formats()
    from scctpkg.bsp import read_fpoly  # noqa: PLC0415
    from scctpkg.properties import read_properties, skip_state_frame  # noqa: PLC0415

    r = pkg.export_reader(export)
    skip_state_frame(r, export.flags)
    read_properties(r)
    r.raw(25 + 16)
    vectors = _vec_array(r, "vectors")
    points = _vec_array(r, "points")
    n = r.compact()
    if n < 0 or n * 40 > r.remaining():
        raise ValueError(f"bad node count {n}")
    nodes: List[ModelNode] = []
    for _ in range(n):
        plane = struct.unpack("<4f", r.raw(16))
        zone_mask = struct.unpack("<Q", r.raw(8))[0]
        flags = r.u8()
        pool, surf, back, front, coplanar, cbound, rbound = (r.compact() for _ in range(7))
        r.raw(32)
        z0, z1, nv = r.u8(), r.u8(), r.u8()
        l0, l1, _sec, _first, _lm = struct.unpack("<5i", r.raw(20))
        nodes.append(ModelNode(plane, zone_mask, flags, pool, surf, back, front, coplanar, cbound, rbound, z0, z1, nv,
                               (l0, l1)))
    n = r.compact()
    if n < 0 or n * 20 > r.remaining():
        raise ValueError(f"bad surf count {n}")
    surfs: List[ModelSurf] = []
    for _ in range(n):
        material = r.compact()
        pf = r.u32()
        base, normal, tu, tv = (r.compact() for _ in range(4))
        r.raw(16)
        r.f32()
        r.f32()
        if pf & 0x0C000000:
            read_fpoly(r)
        surfs.append(ModelSurf(material, pf, base, normal, tu, tv))
    n = r.compact()
    if n < 0 or n * 2 > r.remaining():
        raise ValueError(f"bad vert count {n}")
    verts = np.zeros((n, 2), np.int64)
    for i in range(n):
        verts[i, 0] = r.compact()
        verts[i, 1] = r.compact()
    model = Model(export.name, vectors, points, nodes, surfs, verts)
    try:
        model.shared_sides = r.i32()
        nz = r.i32()
        if not 0 <= nz <= 256:
            raise ValueError(f"bad zone count {nz}")
        for _ in range(nz):
            actor = r.compact()
            conn = struct.unpack("<Q", r.raw(8))[0]
            extra = r.u32()
            model.zones.append(ModelZone(actor, conn, extra))
        model.polys = r.compact()
        nb = r.compact()
        if nb < 0 or nb * 25 > r.remaining():
            raise ValueError(f"bad bounds count {nb}")
        b = np.zeros((nb, 6), np.float32)
        for i in range(nb):
            b[i] = struct.unpack("<6f", r.raw(24))
            r.u8()
        model.bounds = b
        nh = r.compact()
        if nh < 0 or nh * 4 > r.remaining():
            raise ValueError(f"bad leaf hull count {nh}")
        model.leaf_hulls = np.frombuffer(r.raw(nh * 4), dtype=np.int32).copy() if nh else np.zeros((0,), np.int32)
    except (ValueError, IndexError, struct.error) as e:
        # Brush models of some maps end earlier; the geometry above is all collision needs from them.
        model.zones = model.zones or []
        model.tail_error = str(e)  # type: ignore[attr-defined]
    return model
