"""BSP render sections and baked lightmaps of cooked SCCT maps.

Layout after the Verts array parsed by ``bsp.read_model`` (reverse-engineered on this
build; see docs/LIGHTMAPS.md):

    i32 NumSharedSides, i32 NumZones, Zones[NumZones] {compact ZoneActor, u32 Connectivity,
    u32 Visibility, f32 LastRenderTime}, compact Polys, Bounds (compact n; n x FBox 25 bytes),
    LeafHulls (compact n; n x i32), Leaves (...), Lights (...), u32 RootOutside, u32 Linked,
    Sections: compact n; each {compact NumVertices; NumVertices x FBspVertex (40 bytes:
        f32x3 Position, f32x3 Normal, f32 U, V, f32 LightU, LightV); trailer (17..19 bytes:
        u32 Node, compact, u32, u32 PolyFlags, u8, i32 iLightMapTexture)}
    LightMapTextures: compact n; each {u16 tag, compact m, m x u32 lightmap ids (ascending),
        u32 tag, u32, u32 M, u32 0, TArray<u8> Data (W*H*4 BGRA), u32 0, u8, u8 Format (5 = RGBA8),
        u32 W, u32 H, u32 M2}

Node tails (the last 20 bytes of FBspNode) are i32 iLeaf[2], i32 iSection, i32 iFirstVertex,
i32 iLightMap. A node's lightmap id maps to a texture through the id lists, and the node's
polygon vertices are Sections[iSection].Vertices[iFirstVertex : iFirstVertex + NumVertices]
(same order as the node's vertex pool), whose LightU/LightV address that texture directly.

The Leaves/Lights records are not decoded; the section table is located by scanning for its
count followed by a plausible vertex record, and the texture records are anchored on their
trailer signature and walked backwards over the ascending id list.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from typing import Dict, List, Optional

import numpy as np

from .package import Export, Package, PackageError, read_compact
from .properties import read_properties, skip_state_frame
from .bsp import read_fpoly

TRAILER_SIG = bytes([0, 0, 0, 0, 0, 5])   # u32 0, u8 0, u8 TEXF_RGBA8
POWS = (32, 64, 128, 256, 512, 1024, 2048, 4096)


@dataclass
class BspSection:
    vertices: np.ndarray        # (n, 10) float32: pos xyz, normal xyz, u, v, lu, lv
    lightmap_texture: int       # i32 from the trailer (-1 none)


@dataclass
class LightMapTexture:
    ids: List[int]
    width: int
    height: int
    data: bytes                 # BGRA8


@dataclass
class BspLighting:
    node_section: np.ndarray     # (nodes,) i32
    node_first_vertex: np.ndarray
    node_lightmap: np.ndarray    # lightmap id per node (-1 none)
    sections: List[BspSection]
    textures: List[LightMapTexture]
    id_to_texture: Dict[int, int] = field(default_factory=dict)

    def texture_for_node(self, node_index: int) -> int:
        lm = int(self.node_lightmap[node_index])
        return self.id_to_texture.get(lm, -1) if lm >= 0 else -1

    def node_vertices(self, node_index: int, num_vertices: int) -> Optional[np.ndarray]:
        s = int(self.node_section[node_index])
        f = int(self.node_first_vertex[node_index])
        if s < 0 or s >= len(self.sections):
            return None
        v = self.sections[s].vertices
        if f < 0 or f + num_vertices > len(v):
            return None
        return v[f:f + num_vertices]


def _plausible_run(d: bytes, q: int, nv: int, k: int = 6) -> bool:
    """Several consecutive plausible vertices: one alone matches random data too often (StatD section 38)."""
    return all(_plausible_vertex(d, q + 40 * i) for i in range(min(nv, k)))


def _plausible_vertex(d: bytes, q: int) -> bool:
    if q + 40 > len(d):
        return False
    v = struct.unpack_from("<10f", d, q)
    if not all(abs(t) < 200000.0 for t in v[:3]):
        return False
    n2 = v[3] * v[3] + v[4] * v[4] + v[5] * v[5]
    return abs(n2 - 1.0) < 0.05


def read_bsp_lighting(pkg: Package, e: Export) -> BspLighting:
    r = pkg.export_reader(e)
    d = r.d
    skip_state_frame(r, e.flags)
    read_properties(r)
    r.vec3()
    r.vec3()
    r.u8()
    r.raw(16)
    for _ in range(2):
        n = r.compact()
        r.raw(n * 12)
    n = r.compact()
    node_section = np.zeros(n, np.int32)
    node_first = np.zeros(n, np.int32)
    node_lm = np.zeros(n, np.int32)
    node_nv = np.zeros(n, np.int32)
    for i in range(n):
        r.raw(16)
        r.i64()
        r.u8()
        for _ in range(7):
            r.compact()
        r.raw(32)
        r.u8()
        r.u8()
        node_nv[i] = r.u8()
        _l0, _l1, isec, ifv, ilm = struct.unpack("<5i", r.raw(20))
        node_section[i], node_first[i], node_lm[i] = isec, ifv, ilm
    nsurf = r.compact()
    for _ in range(nsurf):
        r.compact()
        pf = r.u32()
        for _ in range(4):
            r.compact()
        r.raw(16)
        r.f32()
        r.f32()
        if pf & 0x0C000000:
            read_fpoly(r)
    n = r.compact()
    for _ in range(n):
        r.compact()
        r.compact()
    r.i32()                      # NumSharedSides
    nz = r.i32()
    for _ in range(nz):
        r.compact()
        r.raw(12)
    r.compact()                  # Polys
    n = r.compact()
    r.raw(n * 25)                # Bounds
    n = r.compact()
    r.raw(n * 4)                 # LeafHulls
    leaves_start = r.p

    # Locate the section table: count == max(iSection)+1 followed by a plausible vertex stream.
    nsec_expected = int(node_section.max()) + 1 if len(node_section) else 0
    need: Dict[int, int] = {}
    for i in range(len(node_nv)):
        if node_nv[i] >= 3 and node_section[i] >= 0:
            need[int(node_section[i])] = max(need.get(int(node_section[i]), 0), int(node_first[i]) + int(node_nv[i]))
    start = None
    for q in range(leaves_start, min(len(d) - 48, leaves_start + 4000000)):   # ClarD: table 66 KB past the leaves
        try:
            cnt, q2 = read_compact(d, q)
            if cnt != nsec_expected:
                continue
            nv, q3 = read_compact(d, q2)
        except Exception:
            continue
        if nv >= need.get(0, 1) and nv < 200000 and _plausible_run(d, q3, nv):
            start = q2
            break
    if start is None:
        raise PackageError("BSP section table not found")

    sections: List[BspSection] = []
    p = start
    for s in range(nsec_expected):
        nv, p2 = read_compact(d, p)
        if nv < 0 or p2 + nv * 40 > len(d):
            raise PackageError(f"bad section {s} vertex count {nv}")
        verts = np.frombuffer(d[p2:p2 + nv * 40], dtype=np.float32).reshape(nv, 10).copy()
        q = p2 + nv * 40
        lm_tex = -1
        if s + 1 < nsec_expected:
            found = None
            for off in range(12, 96):
                try:
                    v, pp = read_compact(d, q + off)
                except Exception:
                    continue
                if v >= need.get(s + 1, 1) and v < 200000 and _plausible_run(d, pp, v):
                    found = off
                    break
            if found is None:
                raise PackageError(f"section {s}: next section not found")
            lm_tex = struct.unpack_from("<i", d, q + found - 4)[0]
            p = q + found
        else:
            p = q
        sections.append(BspSection(verts, lm_tex))

    # Lightmap textures, anchored on their trailer signature.
    textures: List[LightMapTexture] = []
    id_to_tex: Dict[int, int] = {}
    records = []
    q = p
    while True:
        k = d.find(TRAILER_SIG, q)
        if k < 0 or k + 18 > len(d):
            break
        w, h, _m2 = struct.unpack_from("<III", d, k + 6)
        if w in POWS and h in POWS and k - w * h * 4 >= p:
            records.append((k, w, h))
            q = k + 18
        else:
            q = k + 1
    for t, (k, w, h) in enumerate(records):
        size = w * h * 4
        blob = k - size
        data = d[blob:blob + size]
        c = blob - 1
        while c > blob - 6 and read_compact(d, c)[0] != size:
            c -= 1
        if read_compact(d, c)[0] != size:
            raise PackageError(f"lightmap texture {t}: size compact not found")
        pos = c - 16
        ids: List[int] = []
        prev = None
        while pos - 4 >= p:
            v = struct.unpack_from("<I", d, pos - 4)[0]
            if v > 100000 or (prev is not None and v >= prev):
                break
            ids.append(v)
            prev = v
            pos -= 4
        ids.reverse()
        # The compact count precedes the list; the backwards walk may have swallowed the tag/count bytes
        # as a bogus first id, so shrink from the front until the count matches.
        while ids:
            matched = False
            for clen in (1, 2, 3):
                cpos = pos - clen
                if cpos < p:
                    continue
                try:
                    v, np_ = read_compact(d, cpos)
                except Exception:
                    continue
                if np_ == pos and v == len(ids):
                    matched = True
                    break
            if matched:
                break
            ids.pop(0)
            pos += 4
        if not ids:
            raise PackageError(f"lightmap texture {t}: id list not found")
        textures.append(LightMapTexture(ids, w, h, data))
        for i in ids:
            id_to_tex[i] = t
    return BspLighting(node_section, node_first, node_lm, sections, textures, id_to_tex)


def lightmap_image(tex: LightMapTexture):
    """BGRA8 lightmap as a PIL RGB image (UE2 lightmaps are half-intensity: the material scales them)."""
    from PIL import Image
    arr = np.frombuffer(tex.data, dtype=np.uint8).reshape(tex.height, tex.width, 4)
    rgb = arr[:, :, [2, 1, 0]]
    return Image.fromarray(np.ascontiguousarray(rgb), "RGB")
