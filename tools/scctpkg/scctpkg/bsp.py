"""UModel (BSP) reader for SCCT maps, editor and cooked copies, and polygon extraction.

UModel::Serialize (stock editor 0x110D20E0), after the tagged properties (which are just None):

    FBox(25) FSphere(16)
    Vectors: compact n; n x f32x3
    Points:  compact n; n x f32x3
    Nodes:   compact n; each:
        f32x4 Plane; u64 ZoneMask; u8 NodeFlags;
        compact iVertPool, iSurf, iBack, iFront, iPlane, iCollisionBound, iRenderBound;
        FSphere x2 (32 bytes; the second is unused); u8 iZone0, u8 iZone1, u8 NumVertices;
        i32 iLeaf0, i32 iLeaf1, i32 iSection, i32 iFirstVertex, i32 iLightMap
    Surfs:   compact n; each:
        compact Material; u32 PolyFlags; compact pBase, vNormal, vTextureU, vTextureV;
        f32x4 Plane; f32 LightMapScalePSX2; f32 LightMapScaleXBOX (the PC scale); then
        editor copy: FPoly (the source brush face in world space), objref Actor, compact iBrushPoly
        cooked copy: FPoly only when PolyFlags & 0x0C000000 (portal, mirror)      [see read_fpoly]
    Verts:   compact n; each: compact pVertex, compact iSide
    then the tail read by read_model_tail.

Editor or cooked: nothing in the file records it. The editor saves its MapsEd copy with save target 0 and the cooked
Maps copy with target 2, and the surface, lightmap and StaticMeshInstance serializers branch on that target. Every Model
export parses in exactly one of the two layouts (checked on all 293,389 of the 572 distinct original map packages), so
detect_layout parses one in both.

Polygon extraction: for each node with NumVertices >= 3 the polygon is
Points[Verts[iVertPool + k].pVertex]; NodeFlags & 0x21 marks non-structural
(NF_NotCsg | NF_NotVisBlocking) nodes; winding is flipped when it disagrees
with Vectors[surf.vNormal]. Texture coordinates: for a point P,
U = dot(P - Points[pBase], Vectors[vTextureU]) / texW, V likewise.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from typing import List, Optional, Tuple

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

EDITOR = "editor"
COOKED = "cooked"
FPOLY_MAX_VERTICES = 16     # the editor's limit; every FPoly of the original maps has 3 to 16


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
    isection: int = -1
    ifirstvertex: int = -1
    ilightmap: int = -1


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
class BspSectionRecord:
    vertices: np.ndarray        # (n, 10) float32: pos xyz, normal xyz, u, v, lu, lv
    lightmap_texture: int       # iLightMapTexture (-1 none)


@dataclass
class LightMapTextureRecord:
    ids: List[int]              # the lightmaps (node iLightMap) packed into it
    data: bytes                 # the first lazy byte array (the second is empty in every map)
    format: int                 # ETextureFormat, 5 = RGBA8 (stored B, G, R, A)
    width: int
    height: int


@dataclass
class ModelTail:
    sections: List[BspSectionRecord]
    textures: List[LightMapTextureRecord]


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
    """Skip an FPoly (0x110CBDF0): compact NumVertices, Base, Normal, TextureU, TextureV, the vertices, u32 PolyFlags,
    objref Actor, objref Material, name ItemName, compact iLink, compact iBrushPoly, f32 LightMapScale PSX2 and XBOX."""
    n = r.compact()
    if n < 0 or n > FPOLY_MAX_VERTICES:
        raise PackageError(f"bad FPoly vertex count {n}")
    r.raw(12 * 4)            # Base, Normal, TextureU, TextureV
    r.raw(12 * n)            # vertices
    r.u32()                  # PolyFlags
    r.compact()              # Actor
    r.compact()              # Material
    r.name()                 # ItemName
    r.compact()              # iLink
    r.compact()              # iBrushPoly
    r.f32()                  # LightMapScale PSX2
    r.f32()                  # LightMapScale XBOX


def skip_surf_tail(r: Reader, polyflags: int, editor: bool) -> None:
    """What follows a surface's fixed fields (FBspSurf 0x110CD830): in an editor copy every surface keeps its source
    brush face, an FPoly in world space, and the link to it (objref Actor, compact iBrushPoly); a cooked copy keeps the
    FPoly of portal and mirror surfaces only."""
    if editor:
        read_fpoly(r)
        r.compact()          # Actor (the Brush)
        r.compact()          # iBrushPoly
    elif polyflags & (PF_PORTAL | PF_MIRRORED):
        read_fpoly(r)


def _read_vec_array(r: Reader, what: str) -> np.ndarray:
    n = r.compact()
    if n < 0 or n * 12 > r.remaining():
        raise PackageError(f"bad {what} count {n}")
    if n == 0:
        return np.zeros((0, 3), np.float32)
    return np.frombuffer(r.raw(n * 12), dtype=np.float32).reshape(n, 3).copy()


def _count(r: Reader, min_bytes: int, what: str) -> int:
    n = r.compact()
    if n < 0 or n * min_bytes > r.remaining():
        raise PackageError(f"bad {what} count {n}")
    return n


def read_model(pkg: Package, e: Export) -> BspModel:
    return read_model_geometry(pkg, e, pkg.layout == EDITOR)[0]


def read_model_geometry(pkg: Package, e: Export, editor: bool) -> Tuple[BspModel, Reader]:
    """The model up to and including Verts, and the reader positioned after them (at read_model_tail's start)."""
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
        _l0, _l1, isection, ifirst, ilightmap = struct.unpack("<5i", r.raw(20))
        nodes.append(Node(plane, flags, ivertpool, isurf, iback, ifront, iplane, numverts, (z0, z1),
                          isection, ifirst, ilightmap))

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
        r.f32()
        lms = r.f32()
        skip_surf_tail(r, polyflags, editor)
        surfs.append(Surf(material, polyflags, pbase, vnormal, vtexu, vtexv, lms))

    n = r.compact()
    if n < 0 or n * 2 > r.remaining():
        raise PackageError(f"bad vert count {n}")
    verts = np.zeros((n, 2), np.int32)
    for i in range(n):
        verts[i, 0] = r.compact()
        verts[i, 1] = r.compact()
    return BspModel(e.name, vectors, points, nodes, surfs, verts, tuple(bmin), tuple(bmax)), r


def read_model_tail(r: Reader, editor: bool) -> ModelTail:
    """The rest of a model after Verts, to its last byte:

        i32 NumSharedSides, i32 NumZones (0..64), NumZones x {objref ZoneActor, u64 Connectivity, f32 LastRenderTime},
        objref Polys, Bounds (compact n, n x FBox), LeafHulls (compact n, n x i32),
        Leaves (compact n, n x {compact iZone, iPermeating, iVolumetric}), Lights (compact n, n x objref),
        i32 RootOutside, i32 Linked,
        Sections (compact n, each: compact m, m x FBspVertex (40 bytes: f32x3 Position, f32x3 Normal, f32 U, V,
            LightU, LightV), i32, objref Material, i32, u32 PolyFlags, i32 iLightMapTexture),
        editor copy only: LightMaps (compact n, each: 7 x compact, FMatrix, 3 x FVector,
            compact k, k x {objref Light, compact b, b bytes, 7 x i32}, objref, i32),
        LightMapTextures (compact n, each: objref, compact m, m x i32 lightmap ids, u64, i32,
            2 x {i32, compact b, b bytes}, u8 Format, i32 Width, i32 Height, i32)
    """
    r.i32()                  # NumSharedSides
    zones = r.i32()
    if not 0 <= zones <= 64:
        raise PackageError(f"bad zone count {zones}")
    for _ in range(zones):
        r.compact()
        r.raw(12)
    r.compact()              # Polys
    r.raw(25 * _count(r, 25, "bounds"))
    r.raw(4 * _count(r, 4, "leaf hulls"))
    for _ in range(_count(r, 3, "leaves")):
        r.compact()
        r.compact()
        r.compact()
    for _ in range(_count(r, 1, "lights")):
        r.compact()
    root_outside, linked = r.i32(), r.i32()
    if root_outside not in (0, 1) or linked not in (0, 1):
        raise PackageError(f"bad RootOutside/Linked {root_outside}/{linked}")
    sections: List[BspSectionRecord] = []
    for _ in range(_count(r, 18, "sections")):
        nv = _count(r, 40, "section vertices")
        verts = np.frombuffer(r.raw(40 * nv), dtype=np.float32).reshape(nv, 10).copy()
        r.i32()
        r.compact()          # Material
        r.i32()
        r.u32()              # PolyFlags
        sections.append(BspSectionRecord(verts, r.i32()))
    if editor:
        for _ in range(_count(r, 100, "lightmaps")):
            for _ in range(7):
                r.compact()
            r.raw(64 + 36)   # WorldToLightMap, 3 x FVector
            for _ in range(_count(r, 30, "lightmap lights")):
                r.compact()
                r.raw(_count(r, 1, "light visibility bits"))
                r.raw(28)
            r.compact()
            r.i32()
    textures: List[LightMapTextureRecord] = []
    for _ in range(_count(r, 20, "lightmap textures")):
        r.compact()
        m = _count(r, 4, "lightmap ids")
        ids = list(struct.unpack(f"<{m}i", r.raw(4 * m)))
        r.raw(12)            # u64, i32
        r.i32()
        data = r.raw(_count(r, 1, "lightmap data"))
        r.i32()
        r.raw(_count(r, 1, "lightmap data"))
        fmt = r.u8()
        width, height = r.i32(), r.i32()
        r.i32()
        textures.append(LightMapTextureRecord(ids, data, fmt, width, height))
    return ModelTail(sections, textures)


def detect_layout(pkg: Package) -> str:
    """EDITOR or COOKED: parse the smallest Model export in both layouts; exactly one ends on the export's last byte.
    A package without a Model export has no BSP, so its layout does not matter (COOKED)."""
    models = [e for e in pkg.exports if e.class_name == "Model" and e.size > 0]
    if not models:
        return COOKED
    e = min(models, key=lambda m: m.size)
    fits = [layout for layout in (EDITOR, COOKED) if _ends_exactly(pkg, e, layout == EDITOR)]
    if len(fits) != 1:
        raise PackageError(f"{pkg.path.name}: Model {e.name} parses in {len(fits)} layouts, not one")
    return fits[0]


def _ends_exactly(pkg: Package, e: Export, editor: bool) -> bool:
    try:
        _model, r = read_model_geometry(pkg, e, editor)
        read_model_tail(r, editor)
    except (PackageError, IndexError, struct.error, ValueError, OverflowError):
        return False
    return r.remaining() == 0


def level_models(pkg: Package) -> Tuple[int, int]:
    """(ModelPSX2, ModelXBOX) object references of the map's Level export, 0 when absent. ULevel::Serialize
    0x11129320: tagged properties, i32 ActorsNum, i32 ActorsMax, the actors, FURL (4 FString, compact n + n FString
    options, i32 Port, i32 Valid), then i32 bHasModelPSX2 [objref], i32 bHasModelXBOX [objref]."""
    levels = [e for e in pkg.exports if e.class_name == "Level" and e.size > 0]
    if not levels:
        return 0, 0
    r = pkg.export_reader(levels[0])
    read_properties(r)
    num = r.i32()
    r.i32()
    if num < 0 or num > r.remaining():
        raise PackageError(f"bad Level actor count {num}")
    for _ in range(num):
        r.compact()
    for _ in range(4):
        r.fstring()
    for _ in range(r.compact()):
        r.fstring()
    r.i32()
    r.i32()
    psx2 = r.compact() if r.i32() else 0
    xbox = r.compact() if r.i32() else 0
    return psx2, xbox


def world_model(pkg: Package) -> Optional[Export]:
    """The world BSP the PC game uses: ModelXBOX, else ModelPSX2 (the stock editor loads them in that order). Cooked
    copies hold only ModelXBOX; some editor copies also hold a PS2 world, not always the smaller one."""
    psx2, xbox = level_models(pkg)
    ref = xbox or psx2
    return pkg.exports[ref - 1] if ref > 0 else None


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
