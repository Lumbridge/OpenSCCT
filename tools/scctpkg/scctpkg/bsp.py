"""UModel (BSP) reader for cooked SCCT maps and polygon extraction.

Layout after the tagged properties (which are just None):

    FBox(25) FSphere(16)
    Vectors: compact n; n x f32x3
    Points:  compact n; n x f32x3
    Nodes:   compact n; each:
        f32x4 Plane; u64 ZoneMask; u8 NodeFlags;
        compact iVertPool, iSurf, iBack, iFront, iPlane, iCollisionBound, iRenderBound;
        FSphere x2 (32 bytes); u8 iZone0, u8 iZone1, u8 NumVertices;
        i32 iLeaf0, i32 iLeaf1, i32 iSection, i32 iFirstVertex, i32 iLightMap
    Surfs:   compact n; each:
        compact Material; u32 PolyFlags; compact pBase, vNormal, vTextureU, vTextureV;
        f32x4 Plane; f32 LightMapScale; f32 extra;
        if PolyFlags & 0x0C000000: embedded FPoly (cooked)      [see read_fpoly]
    Verts:   compact n; each: compact pVertex, compact iSide

Polygon extraction: for each node with NumVertices >= 3 the polygon is
Points[Verts[iVertPool + k].pVertex]; NodeFlags & 0x21 marks non-structural
(NF_NotCsg | NF_NotVisBlocking) nodes; winding is flipped when it disagrees
with Vectors[surf.vNormal]. Texture coordinates: for a point P,
U = dot(P - Points[pBase], Vectors[vTextureU]) / texW, V likewise.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Tuple

import numpy as np

from .package import Export, Package, PackageError, Reader
from .properties import read_properties, skip_state_frame

PF_INVISIBLE = 0x00000001
PF_TWO_SIDED = 0x00000100
PF_PORTAL = 0x04000000
PF_MIRRORED = 0x08000000
PF_FAKE_BACKDROP = 0x00000080
PF_NOT_SOLID = 0x00000008
PF_SEMISOLID = 0x00000020
PF_UNLIT = 0x00400000       # drawn fullbright (no lightmap): skylight/window glow panels, neon

NF_NOT_CSG = 0x01
NF_NOT_VIS_BLOCKING = 0x20


@dataclass
class Node:
    plane: Tuple[float, float, float, float]
    flags: int
    ivertpool: int
    isurf: int
    iback: int
    ifront: int
    iplane: int
    numverts: int
    izone: Tuple[int, int]


@dataclass
class Surf:
    material_ref: int
    polyflags: int
    pbase: int
    vnormal: int
    vtexu: int
    vtexv: int
    lightmap_scale: float


@dataclass
class BspModel:
    name: str
    vectors: np.ndarray
    points: np.ndarray
    nodes: List[Node]
    surfs: List[Surf]
    verts: np.ndarray          # (n, 2) int32: pVertex, iSide
    bbox_min: Tuple[float, float, float]
    bbox_max: Tuple[float, float, float]


@dataclass
class BspPolygon:
    surf_index: int
    node_index: int
    vertices: np.ndarray       # (k, 3)
    normal: np.ndarray         # (3,)
    material_ref: int
    polyflags: int
    base: np.ndarray
    texu: np.ndarray
    texv: np.ndarray


def read_fpoly(r: Reader) -> None:
    """Skip an embedded editor FPoly (cooked portal surfaces keep one)."""
    n = r.compact()
    if n < 0 or n > 512:
        raise PackageError(f"bad FPoly vertex count {n}")
    r.raw(12 * 4)            # Base, Normal, TextureU, TextureV
    r.raw(12 * n)            # vertices
    r.u32()                  # PolyFlags
    r.compact()              # Actor
    r.compact()              # Material
    r.name()                 # ItemName
    r.compact()              # iLink
    r.compact()              # iBrushPoly
    r.f32()                  # LightMapScale
    r.f32()                  # SCCT extra


def _read_vec_array(r: Reader, what: str) -> np.ndarray:
    n = r.compact()
    if n < 0 or n * 12 > r.remaining():
        raise PackageError(f"bad {what} count {n}")
    if n == 0:
        return np.zeros((0, 3), np.float32)
    return np.frombuffer(r.raw(n * 12), dtype=np.float32).reshape(n, 3).copy()


def read_model(pkg: Package, e: Export) -> BspModel:
    r = pkg.export_reader(e)
    skip_state_frame(r, e.flags)
    read_properties(r)
    bmin = r.vec3()
    bmax = r.vec3()
    r.u8()
    r.raw(16)
    vectors = _read_vec_array(r, "vectors")
    points = _read_vec_array(r, "points")

    n = r.compact()
    if n < 0 or n * 40 > r.remaining():
        raise PackageError(f"bad node count {n}")
    nodes: List[Node] = []
    for _ in range(n):
        plane = (r.f32(), r.f32(), r.f32(), r.f32())
        r.i64()
        flags = r.u8()
        ivertpool = r.compact()
        isurf = r.compact()
        iback = r.compact()
        ifront = r.compact()
        iplane = r.compact()
        r.compact()
        r.compact()
        r.raw(32)
        z0 = r.u8()
        z1 = r.u8()
        numverts = r.u8()
        r.raw(20)
        nodes.append(Node(plane, flags, ivertpool, isurf, iback, ifront, iplane, numverts, (z0, z1)))

    n = r.compact()
    if n < 0 or n * 20 > r.remaining():
        raise PackageError(f"bad surf count {n}")
    surfs: List[Surf] = []
    for _ in range(n):
        material = r.compact()
        polyflags = r.u32()
        pbase = r.compact()
        vnormal = r.compact()
        vtexu = r.compact()
        vtexv = r.compact()
        r.raw(16)
        lms = r.f32()
        r.f32()
        if polyflags & 0x0C000000:
            read_fpoly(r)
        surfs.append(Surf(material, polyflags, pbase, vnormal, vtexu, vtexv, lms))

    n = r.compact()
    if n < 0 or n * 2 > r.remaining():
        raise PackageError(f"bad vert count {n}")
    verts = np.zeros((n, 2), np.int32)
    for i in range(n):
        verts[i, 0] = r.compact()
        verts[i, 1] = r.compact()
    return BspModel(e.name, vectors, points, nodes, surfs, verts, tuple(bmin), tuple(bmax))


def point_zone(model: BspModel, p) -> int:
    """Zone number of a point, exactly like UE2's UModel::PointRegion: descend the BSP choosing the child on the
    side of each node's plane (front when PlaneDot >= 0) and take the last node's iZone[IsFront]."""
    if not model.nodes:
        return 0
    x, y, z = float(p[0]), float(p[1]), float(p[2])
    inode, parent, is_front = 0, 0, 0
    guard = 0
    while 0 <= inode < len(model.nodes) and guard < 100000:
        guard += 1
        n = model.nodes[inode]
        px, py, pz, pw = n.plane
        is_front = 1 if (px * x + py * y + pz * z - pw) >= 0.0 else 0
        parent = inode
        inode = n.ifront if is_front else n.iback
    return model.nodes[parent].izone[is_front]


def extract_polygons(model: BspModel, include_nonstructural: bool = True,
                     include_fake_backdrop: bool = False) -> List[BspPolygon]:
    polys: List[BspPolygon] = []
    npts = len(model.points)
    nvec = len(model.vectors)
    for ni, node in enumerate(model.nodes):
        if node.numverts < 3:
            continue
        if not include_nonstructural and (node.flags & (NF_NOT_CSG | NF_NOT_VIS_BLOCKING)):
            continue
        if node.isurf < 0 or node.isurf >= len(model.surfs):
            continue
        surf = model.surfs[node.isurf]
        if surf.polyflags & (PF_INVISIBLE | PF_PORTAL):
            continue
        if surf.polyflags & PF_FAKE_BACKDROP and not include_fake_backdrop:
            continue   # fake backdrop = skybox window in UE2, not a wall (the sky zone's own walls are asked for)
        pool = model.verts[node.ivertpool:node.ivertpool + node.numverts, 0]
        if len(pool) < 3 or pool.max() >= npts or pool.min() < 0:
            continue
        verts = model.points[pool]
        # drop consecutive duplicates
        keep = [0]
        for k in range(1, len(verts)):
            if not np.allclose(verts[k], verts[keep[-1]]):
                keep.append(k)
        if len(keep) > 2 and np.allclose(verts[keep[-1]], verts[keep[0]]):
            keep.pop()
        if len(keep) < 3:
            continue
        verts = verts[keep]
        normal = np.array(node.plane[:3], np.float32)
        if 0 <= surf.vnormal < nvec:
            normal = model.vectors[surf.vnormal]
        # Winding: match the static meshes' convention (their cross product opposes the stored normal in
        # UE space; the glTF axis swap plus Interchange's winding reversal makes that the front face).
        # BSP polygons were wound the other way, which back-face culled every floor and wall seen from
        # the inside.
        # Newell normal of the whole polygon (the first three vertices can be collinear).
        nx = np.sum((verts[:, 1] - np.roll(verts[:, 1], -1)) * (verts[:, 2] + np.roll(verts[:, 2], -1)))
        ny = np.sum((verts[:, 2] - np.roll(verts[:, 2], -1)) * (verts[:, 0] + np.roll(verts[:, 0], -1)))
        nz = np.sum((verts[:, 0] - np.roll(verts[:, 0], -1)) * (verts[:, 1] + np.roll(verts[:, 1], -1)))
        if nx * normal[0] + ny * normal[1] + nz * normal[2] > 0:
            verts = verts[::-1].copy()
        base = model.points[surf.pbase] if 0 <= surf.pbase < npts else np.zeros(3, np.float32)
        texu = model.vectors[surf.vtexu] if 0 <= surf.vtexu < nvec else np.array([1, 0, 0], np.float32)
        texv = model.vectors[surf.vtexv] if 0 <= surf.vtexv < nvec else np.array([0, 1, 0], np.float32)
        polys.append(BspPolygon(node.isurf, ni, verts, normal, surf.material_ref, surf.polyflags, base, texu, texv))
    return polys
