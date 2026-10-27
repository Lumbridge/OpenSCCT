"""Static mesh collision: the data a UStaticMesh export keeps after its render buffers.

scctpkg's staticmesh reader stops after the render index buffer. A UStaticMesh export goes on
(package versions 172 to 300, all fields little-endian, "compact" = FCompactIndex):

    WireframeIndexBuffer   compact n, n x u16, i32 Revision
    per-polygon collision  (the triangles players and traces collide with when there is no hull)
      planes      compact n, n x FPlane (f32 X, Y, Z, W)          mesh+0x104
      triangles   compact n, n x (u16 v0, v1, v2, material)       mesh+0x10c, indices into the render vertex stream
      BSP         compact n, n x node                             mesh+0x114
    convex hulls (imported MCDCX collision)
      vertices    compact n, n x (FVector position, FVector normal), i32 Revision     mesh+0x128
      planes      compact n, n x FPlane                           mesh+0x140, one per hull triangle
      triangles   compact n, n x (u16 v0, v1, v2, material)       mesh+0x148, indices into the hull vertices
      BSP         compact n, n x node                             mesh+0x150
    hull source triangles   lazy array of FStaticMeshTriangle     mesh+0x19c
    hull edge index buffer  compact n, n x u16, i32 Revision      mesh+0x15c (line list for the editor view)
    RawTriangles            lazy array of FStaticMeshTriangle     mesh+0x184 (the render source triangles)
    i32 InternalVersion     mesh+0x1b4
    i32                     mesh+0x1b8 (version >= 120; 0 in every shipped mesh seen)

BSP node: 4 compacts (triangle, coplanar, back, front; -1 = none), then i32 (version >= 157 and != 158)
holding two more 16-bit fields (low half 0xfffe in almost every node). It is a triangle tree, not a solid
BSP: each node names one triangle, the coplanar chain holds triangles in its plane, and the back and front
subtrees hold the triangles behind and in front of it (a triangle that spans the plane goes into both).
Child indices are int16 in memory, so a tree holds at most 32767 nodes.

Lazy array: i32 (before version 175 the absolute file offset just past the array, then 0 or the same
kind of offset), compact n, n x FStaticMeshTriangle.

FStaticMeshTriangle (version >= 112): 3 x FVector, i32 NumUVs, NumUVs x (3 x (f32 U, f32 V)),
3 x FColor (4 bytes, B G R A), i32 MaterialIndex, u32 SmoothingMask. NumUVs is compared signed:
a negative value (uninitialized memory saved by the editor) means no UV data follows.

Source: UStaticMesh::Serialize in ChaosTheory_Editor.exe (0x110e8be0), element serializers
0x110e4b60 (16-byte arrays), 0x110e4c30 (8-byte arrays), 0x110e6c00 / 0x110e5a20 (BSP nodes),
0x110e5d80 (vertex stream), 0x110e6d00 (lazy arrays), 0x110e5280 (FStaticMeshTriangle).
Checked by consuming every static mesh export of the 62 shipped .usx packages to the last byte.
"""

from __future__ import annotations

import argparse
import json
import struct
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import numpy as np

from ._env import Package, PackageError, Reader, game_root, out_dir

BSP_NODE_CAP = 32767
PER_POLY_TRI_CAP = 15000
GAME_TRI_TABLE = 5512   # the game's static table of tested triangles (RE+ finding, crash past it)


class SMFormatError(PackageError):
    pass


@dataclass
class RawTriangle:
    verts: List[Tuple[float, float, float]]
    uvs: List[List[Tuple[float, float]]]
    colors: List[Tuple[int, int, int, int]]
    material: int
    smoothing: int


@dataclass
class Collision:
    """Everything after the render index buffer."""
    wire_indices: np.ndarray
    pp_planes: np.ndarray        # (n, 4) float32
    pp_tris: np.ndarray          # (n, 4) uint16: v0, v1, v2, material
    pp_bsp: np.ndarray           # (n, 5) int64: triangle, coplanar, back, front, extra (-1 when absent)
    hull_verts: np.ndarray       # (n, 6) float32: position, normal
    hull_revision: int
    hull_planes: np.ndarray
    hull_tris: np.ndarray
    hull_bsp: np.ndarray
    hull_source: List[RawTriangle]
    hull_wire_indices: np.ndarray
    raw_tris: List[RawTriangle]
    internal_version: int
    extra: Optional[int]
    lazy_heads: List[int] = field(default_factory=list)
    lazy_ends: List[int] = field(default_factory=list)


@dataclass
class Section:
    f0: int
    first_index: int
    first_vertex: int
    last_vertex: int
    fe: int
    num_faces: int


@dataclass
class MeshData:
    name: str
    version: int
    props: dict
    bbox: Tuple[Tuple[float, ...], Tuple[float, ...], int]
    sphere: Tuple[float, ...]
    sections: List[Section]
    positions: np.ndarray
    normals: np.ndarray
    indices: np.ndarray
    collision: Collision

    def render_tris(self, collision_only: bool = False, enable: Optional[List[bool]] = None) -> np.ndarray:
        out = []
        for i, s in enumerate(self.sections):
            if collision_only and enable is not None and i < len(enable) and not enable[i]:
                continue
            idx = self.indices[s.first_index:s.first_index + 3 * s.num_faces]
            out.append(idx.reshape(-1, 3))
        return np.concatenate(out) if out else np.zeros((0, 3), np.uint32)


# ---- primitive readers -----------------------------------------------------------------------

def _array(r: Reader, size: int, dtype, cols: int, what: str) -> np.ndarray:
    n = r.compact()
    if n < 0 or n * size > r.remaining():
        raise SMFormatError(f"bad {what} count {n} at {r.p}")
    if n == 0:
        return np.zeros((0, cols), dtype)
    return np.frombuffer(r.raw(n * size), dtype=dtype).reshape(n, cols).copy()


def _u16_buffer(r: Reader, what: str) -> np.ndarray:
    n = r.compact()
    if n < 0 or n * 2 > r.remaining():
        raise SMFormatError(f"bad {what} count {n}")
    v = np.frombuffer(r.raw(n * 2), dtype=np.uint16).copy() if n else np.zeros((0,), np.uint16)
    r.i32()  # revision
    return v


def read_bsp(r: Reader, version: int) -> np.ndarray:
    n = r.compact()
    if n < 0 or n * 4 > r.remaining():
        raise SMFormatError(f"bad BSP node count {n}")
    has_extra = version >= 0x9D and version != 0x9E
    out = np.empty((n, 5), np.int64)
    for i in range(n):
        out[i, 0] = r.compact()
        out[i, 1] = r.compact()
        out[i, 2] = r.compact()
        out[i, 3] = r.compact()
        out[i, 4] = r.u32() if has_extra else -1
    return out


def read_raw_triangle(r: Reader) -> RawTriangle:
    v = [tuple(r.vec3()) for _ in range(3)]
    nuv = r.i32()
    # The loader loops while i < NumUVs (signed), so a negative value (uninitialized memory saved by the
    # editor: SM_UMP.panel_hacked's hull source triangles) means no UV data.
    if nuv > 8:
        raise SMFormatError(f"bad NumUVs {nuv} at {r.p - 4}")
    uvs = []
    for _ in range(max(0, nuv)):
        f = struct.unpack("<6f", r.raw(24))
        uvs.append([(f[0], f[1]), (f[2], f[3]), (f[4], f[5])])
    cols = []
    for _ in range(3):
        b, g, rr, a = r.raw(4)
        cols.append((rr, g, b, a))
    mat = r.i32()
    sm = r.u32()
    return RawTriangle(v, uvs, cols, mat, sm)


def read_lazy_triangles(r: Reader, heads: List[int], ends: List[int]) -> List[RawTriangle]:
    heads.append(r.i32())
    n = r.compact()
    if n < 0 or n * 60 > r.remaining():
        raise SMFormatError(f"bad triangle count {n}")
    tris = [read_raw_triangle(r) for _ in range(n)]
    ends.append(r.base + r.p)
    return tris


def read_collision(r: Reader, version: int) -> Collision:
    if version < 0xA6:
        raise SMFormatError(f"package version {version}: collision layout before 166 not supported")
    wire = _u16_buffer(r, "wireframe index")
    pp_planes = _array(r, 16, np.float32, 4, "per-poly plane")
    pp_tris = _array(r, 8, np.uint16, 4, "per-poly triangle")
    pp_bsp = read_bsp(r, version)
    hull_verts = _array(r, 24, np.float32, 6, "hull vertex") if version >= 0x77 else np.zeros((0, 6), np.float32)
    hull_rev = r.i32() if version >= 0x77 else 0
    hull_planes = _array(r, 16, np.float32, 4, "hull plane")
    hull_tris = _array(r, 8, np.uint16, 4, "hull triangle")
    hull_bsp = read_bsp(r, version)
    heads: List[int] = []
    ends: List[int] = []
    hull_source = read_lazy_triangles(r, heads, ends)
    hull_wire = _u16_buffer(r, "hull edge index")
    raw = read_lazy_triangles(r, heads, ends)
    iv = r.i32()
    extra = r.i32() if version >= 0x78 else None
    if r.remaining():
        raise SMFormatError(f"{r.remaining()} bytes left after the collision data")
    return Collision(wire, pp_planes, pp_tris, pp_bsp, hull_verts, hull_rev, hull_planes, hull_tris, hull_bsp,
                     hull_source, hull_wire, raw, iv, extra, heads, ends)


def read_mesh(pkg: Package, e) -> MeshData:
    from scctpkg.properties import read_properties, skip_state_frame
    r = pkg.export_reader(e)
    skip_state_frame(r, e.flags)
    props = read_properties(r)
    bmin, bmax, valid = tuple(r.vec3()), tuple(r.vec3()), r.u8()
    sphere = struct.unpack("<4f", r.raw(16))
    ns = r.compact()
    if not 0 <= ns <= 4096:
        raise SMFormatError(f"bad section count {ns}")
    sections = [Section(*struct.unpack("<iHHHHH", r.raw(14))) for _ in range(ns)]
    v = _array(r, 24, np.float32, 6, "vertex")
    r.i32()
    for _ in range(2):          # colour and alpha streams
        n = r.compact()
        r.raw(4 * n)
        r.i32()
    nuv = r.compact()
    if not 0 <= nuv <= 8:
        raise SMFormatError(f"bad uv stream count {nuv}")
    for _ in range(nuv):
        n = r.compact()
        r.raw(8 * n)
        r.i32()
        r.i32()
    indices = _u16_buffer(r, "index").astype(np.uint32)
    col = read_collision(r, pkg.version)
    return MeshData(e.name, pkg.version, dict(props), (bmin, bmax, valid), sphere, sections,
                    np.ascontiguousarray(v[:, :3]), np.ascontiguousarray(v[:, 3:]), indices, col)


# ---- checks ----------------------------------------------------------------------------------

def _tri_keys(pos: np.ndarray, tris: np.ndarray, q: float) -> Counter:
    """Order-independent keys of triangles from quantized corner positions."""
    keys: Counter = Counter()
    p = np.round(pos[tris.reshape(-1)].reshape(-1, 3, 3) / q).astype(np.int64)
    for t in p:
        corners = sorted(map(tuple, t))
        keys[tuple(corners)] += 1
    return keys


def check_per_poly(m: MeshData, enable: List[bool], tol: float = 0.01) -> dict:
    """Per-poly collision triangles against the render triangles of collision-enabled sections."""
    c = m.collision
    nv = len(m.positions)
    out = {"pp_tris": int(len(c.pp_tris)), "pp_planes": int(len(c.pp_planes))}
    if len(c.pp_tris) == 0:
        return out
    idx = c.pp_tris[:, :3].astype(np.int64)
    if idx.max() >= nv:
        out["index_out_of_range"] = int((idx >= nv).any(axis=1).sum())
        return out
    rt = m.render_tris(True, enable)
    out["render_tris_collision_enabled"] = int(len(rt))
    out["render_tris_all"] = int(len(m.render_tris()))
    pp = _tri_keys(m.positions, idx, tol)
    rk = _tri_keys(m.positions, rt.astype(np.int64), tol) if len(rt) else Counter()
    matched = sum(min(n, rk.get(k, 0)) for k, n in pp.items())
    out["pp_matched_render"] = int(matched)
    out["render_not_in_pp"] = int(sum(rk.values()) - matched)
    # Raw triangles that share all three corners (two-sided faces) stay separate in the per-poly set but are
    # one triangle in the render buffers: count the corner sets with no render triangle at all.
    out["pp_shape_not_in_render"] = int(sum(1 for k in pp if k not in rk))
    out["pp_duplicate_corner_sets"] = int(sum(n - 1 for n in pp.values() if n > 1))
    # Plane agreement: stored plane vs the plane through the triangle's corners.
    p = m.positions[idx].astype(np.float64)
    n = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
    ln = np.linalg.norm(n, axis=1)
    ok = ln > 1e-9
    n[ok] /= ln[ok, None]
    pl = c.pp_planes.astype(np.float64)
    dots = np.einsum("ij,ij->i", n, pl[:, :3])
    dist = np.abs(np.einsum("ij,ij->i", p[:, 0], pl[:, :3]) - pl[:, 3])
    out["plane_normal_agree"] = int(((np.abs(dots) > 0.999) | ~ok).sum())
    out["plane_sign"] = "same" if np.median(dots[ok]) > 0 else "flipped" if ok.any() else "n/a"
    out["plane_dist_max"] = float(dist.max()) if len(dist) else 0.0
    return out


def _boundary_loops(edges: List[Tuple[int, int]]) -> List[List[int]]:
    adj: Dict[int, List[int]] = defaultdict(list)
    for u, w in edges:
        adj[u].append(w)
        adj[w].append(u)
    seen = set()
    loops = []
    for s in sorted(adj):
        if s in seen:
            continue
        loop, prev, cur = [s], None, s
        seen.add(s)
        while True:
            nxt = [x for x in adj[cur] if x != prev and x not in seen]
            if not nxt:
                break
            prev, cur = cur, nxt[0]
            seen.add(cur)
            loop.append(cur)
        loops.append(loop)
    return loops


def _planar(p: np.ndarray, tol: float) -> bool:
    if len(p) < 3:
        return False
    s = np.linalg.svd(p - p.mean(axis=0), compute_uv=False)
    return bool(s[-1] < tol)


def hulls_of(c: Collision, weld: float = 0.01) -> List[dict]:
    """Group hull triangles into hulls by shared (welded) corners and check each one."""
    nt = len(c.hull_tris)
    if nt == 0:
        return []
    pos = c.hull_verts[:, :3].astype(np.float64)
    key = np.round(pos / weld).astype(np.int64)
    wid: Dict[tuple, int] = {}
    vid = np.array([wid.setdefault(tuple(k), len(wid)) for k in key], np.int64)
    tris = vid[c.hull_tris[:, :3].astype(np.int64)]
    parent = list(range(len(wid)))

    def root(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x
    for a, b, cc in tris:
        ra, rb, rc = root(a), root(b), root(cc)
        parent[ra] = rb
        parent[rc] = rb
    groups: Dict[int, List[int]] = defaultdict(list)
    for i, t in enumerate(tris):
        groups[root(t[0])].append(i)
    wpos = np.zeros((len(wid), 3))
    wpos[vid] = pos
    in_bsp = set(int(x) for x in c.hull_bsp[:, 0]) if len(c.hull_bsp) else set()
    hulls = []
    for members in groups.values():
        t = tris[members]
        verts = np.unique(t.reshape(-1))
        V, T = len(verts), len(members)
        edges: Counter = Counter()
        directed: Counter = Counter()
        for a, b, cc in t:
            for u, w in ((a, b), (b, cc), (cc, a)):
                edges[(min(u, w), max(u, w))] += 1
                directed[(u, w)] += 1
        closed = all(n == 2 for n in edges.values())
        oriented = closed and all(directed[(w, u)] == 1 for (u, w) in directed)
        open_edges = [k for k, n in edges.items() if n == 1]
        loops = _boundary_loops(open_edges)
        planar_loops = sum(1 for L in loops if _planar(wpos[L], 0.05))
        cap_tris = sum(max(0, len(L) - 2) for L in loops)
        degenerate = 0
        # Convexity: every vertex of the hull on the inside of every triangle plane (stored planes).
        pl = c.hull_planes[members].astype(np.float64)
        vp = wpos[verts]
        d = vp @ pl[:, :3].T - pl[:, 3]
        for a, b, cc in t:
            if len({a, b, cc}) < 3:
                degenerate += 1
        lo, hi = vp.min(axis=0), vp.max(axis=0)
        span = float(np.max(hi - lo)) if V else 0.0
        eps = max(0.05, 1e-4 * span)
        outward = float(d.max()) if d.size else 0.0
        inward = float(d.min()) if d.size else 0.0
        # Planes may face out (all d <= 0) or in (all d >= 0); convex means one side only.
        convex = outward <= eps or inward >= -eps
        hulls.append({
            "tris": T, "verts": V, "euler_ok": T == 2 * V - 4, "closed": closed, "oriented": oriented,
            "open_edges": len(open_edges), "open_loops": [len(L) for L in loops], "planar_loops": planar_loops,
            "euler_ok_if_capped": bool(loops) and planar_loops == len(loops) and T + cap_tris == 2 * V - 4,
            "convex": convex, "plane_side": "inside<=0" if outward <= eps else ("inside>=0" if inward >= -eps else "mixed"),
            "degenerate": degenerate, "in_bsp": sum(1 for i in members if i in in_bsp),
            "lo": [round(float(x), 2) for x in lo], "hi": [round(float(x), 2) for x in hi],
        })
    hulls.sort(key=lambda h: h["lo"])
    return hulls


def check_planes(planes: np.ndarray, pos: np.ndarray, tris: np.ndarray) -> dict:
    if len(tris) == 0:
        return {}
    p = pos[tris[:, :3].astype(np.int64)].astype(np.float64)
    n = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
    ln = np.linalg.norm(n, axis=1)
    ok = ln > 1e-9
    n[ok] /= ln[ok, None]
    pl = planes.astype(np.float64)
    dots = np.einsum("ij,ij->i", n, pl[:, :3])
    dist = np.abs(np.einsum("ij,ij->i", p[:, 0], pl[:, :3]) - pl[:, 3])
    return {"normal_agree": int(((np.abs(dots) > 0.999) | ~ok).sum()), "of": int(len(tris)),
            "sign": "same" if ok.any() and np.median(dots[ok]) > 0 else "flipped",
            "dist_max": round(float(dist.max()), 4)}


def bsp_stats(bsp: np.ndarray, ntris: int) -> dict:
    if len(bsp) == 0:
        return {"nodes": 0}
    tri = bsp[:, 0]
    reach = set(int(x) for x in tri if 0 <= x < ntris)
    kids = bsp[:, 1:4]
    bad = int(((kids >= len(bsp)) | (kids < -1)).sum())
    extra = Counter(int(x) for x in bsp[:, 4])
    return {"nodes": int(len(bsp)), "capped": len(bsp) >= BSP_NODE_CAP, "tris_in_tree": len(reach),
            "tris": ntris, "bad_children": bad, "extra_values": dict(extra.most_common(4))}


def check_bsp_sides(bsp: np.ndarray, pos: np.ndarray, tris: np.ndarray, planes: np.ndarray, tol: float = 0.1) -> dict:
    """Each child's first triangle against the node's plane: the coplanar child lies in the plane, the back
    child is not wholly in front, the front child is not wholly behind. Returns violation counts."""
    if len(bsp) == 0:
        return {}
    P = pos.astype(np.float64)
    T = tris[:, :3].astype(np.int64)
    PL = planes.astype(np.float64)
    bad = Counter()
    for row in bsp:
        t = row[0]
        for k, name in ((1, "coplanar"), (2, "back"), (3, "front")):
            ch = row[k]
            if ch < 0 or ch >= len(bsp):
                continue
            d = P[T[bsp[ch][0]]] @ PL[t, :3] - PL[t, 3]
            if name == "coplanar" and np.abs(d).max() > tol:
                bad[name] += 1
            elif name == "back" and d.min() > tol:
                bad[name] += 1
            elif name == "front" and d.max() < -tol:
                bad[name] += 1
    return {"violations": dict(bad), "ok": not bad}


STATICMESH_FLAG_PROPS = ("UseSimpleLineCollision", "UseSimpleBoxCollision", "UseSimpleKarmaCollision",
                         "UseVertexColor", "UseSimpleRigidBodyCollision")
# Engine.StaticMesh has no script export (it is an intrinsic class), so its defaults are not in Engine.u.
# Values read from the live class default object of the game and of the editor (2026-10-23 reflection dumps):
STATICMESH_DEFAULTS = {"UseSimpleLineCollision": False, "UseSimpleBoxCollision": True, "UseVertexColor": False}


def mesh_report(pkg: Package, e, defaults: Optional[dict] = None) -> dict:
    m = read_mesh(pkg, e)
    c = m.collision
    mats = m.props.get("Materials")
    enable = []
    if isinstance(mats, list):
        for x in mats:
            enable.append(bool(x.get("EnableCollision", False)) if isinstance(x, dict) else False)
    hulls = hulls_of(c)
    flags = {}
    stored = []
    base = dict(STATICMESH_DEFAULTS)
    base.update(defaults or {})
    for k in STATICMESH_FLAG_PROPS:
        if k in m.props:
            flags[k] = bool(m.props[k])
            stored.append(k)
        elif k in base:
            flags[k] = bool(base[k])
    rep = {
        "name": e.name,
        "size": e.size,
        "render": {"verts": int(len(m.positions)), "tris": int(len(m.indices) // 3), "sections": len(m.sections),
                   "enable_collision": enable},
        "wire_indices": int(len(c.wire_indices)),
        "per_poly": check_per_poly(m, enable),
        "per_poly_bsp": bsp_stats(c.pp_bsp, len(c.pp_tris)),
        "hull": {"verts": int(len(c.hull_verts)), "tris": int(len(c.hull_tris)), "planes": int(len(c.hull_planes)),
                 "count": len(hulls),
                 "euler_ok": sum(1 for h in hulls if h["euler_ok"]),
                 "closed": sum(1 for h in hulls if h["closed"]),
                 "euler_ok_if_capped": sum(1 for h in hulls if h["euler_ok_if_capped"]),
                 "convex": sum(1 for h in hulls if h["convex"]),
                 "planes_check": check_planes(c.hull_planes, c.hull_verts[:, :3], c.hull_tris),
                 "source_tris": len(c.hull_source), "edge_indices": int(len(c.hull_wire_indices))},
        "hull_bsp": bsp_stats(c.hull_bsp, len(c.hull_tris)),
        "per_poly_bsp_sides": check_bsp_sides(c.pp_bsp, m.positions, c.pp_tris, c.pp_planes),
        "hull_bsp_sides": check_bsp_sides(c.hull_bsp, c.hull_verts[:, :3], c.hull_tris, c.hull_planes),
        "raw_tris": len(c.raw_tris),
        "internal_version": c.internal_version,
        "extra": c.extra,
        "flags": flags,
        "flags_stored": stored,
        "tail_bytes": None,
    }
    if hulls:
        rep["hulls"] = hulls if len(hulls) <= 64 else [h for h in hulls if not (h["euler_ok"] and h["convex"] and h["closed"])][:64]
    if pkg.version < 0xAF:
        # Old lazy arrays start with the file offset just past their data.
        rep["lazy_offsets_ok"] = c.lazy_heads == c.lazy_ends
    return rep


def tail_size(pkg: Package, e) -> int:
    """Bytes after the render index buffer (what scctpkg leaves undecoded)."""
    from scctpkg.properties import read_properties, skip_state_frame
    r = pkg.export_reader(e)
    skip_state_frame(r, e.flags)
    read_properties(r)
    r.raw(25 + 16)
    ns = r.compact()
    r.raw(14 * ns)
    n = r.compact(); r.raw(24 * n); r.i32()
    for _ in range(2):
        n = r.compact(); r.raw(4 * n); r.i32()
    for _ in range(r.compact()):
        n = r.compact(); r.raw(8 * n); r.i32(); r.i32()
    n = r.compact(); r.raw(2 * n); r.i32()
    return r.remaining()


# ---- command line ----------------------------------------------------------------------------

def _defaults() -> dict:
    try:
        from .classdefaults import load_output
        d = load_output() or {}
        return d.get("Engine.StaticMesh", {}).get("props", {})
    except Exception:
        return {}


def scan_package(path: Path, names: Optional[List[str]] = None, defaults: Optional[dict] = None) -> dict:
    pkg = Package.load(path)
    meshes, failed = [], []
    for e in pkg.exports_of_class("StaticMesh"):
        if e.size <= 0 or (names and e.name not in names):
            continue
        try:
            rep = mesh_report(pkg, e, defaults)
            rep["tail_bytes"] = tail_size(pkg, e)
            meshes.append(rep)
        except Exception as ex:
            failed.append({"name": e.name, "error": f"{type(ex).__name__}: {ex}"})
    return {"package": path.name, "version": pkg.version, "meshes": meshes, "failed": failed}


def summarize(pkgs: List[dict]) -> dict:
    s = Counter()
    for p in pkgs:
        s["packages"] += 1
        s["failed"] += len(p["failed"])
        for m in p["meshes"]:
            s["meshes"] += 1
            pp = m["per_poly"]
            h = m["hull"]
            s["with_hulls"] += 1 if h["tris"] else 0
            s["hulls"] += h["count"]
            s["hulls_euler_ok"] += h["euler_ok"]
            s["hulls_closed"] += h["closed"]
            s["hulls_convex"] += h["convex"]
            s["hulls_euler_ok_if_capped"] += h.get("euler_ok_if_capped", 0)
            s["with_per_poly"] += 1 if pp["pp_tris"] else 0
            s["pp_tris"] += pp["pp_tris"]
            s["pp_matched"] += pp.get("pp_matched_render", 0)
            s["pp_render_collision_tris"] += pp.get("render_tris_collision_enabled", 0)
            s["pp_render_not_in_pp"] += pp.get("render_not_in_pp", 0)
            s["pp_full_match"] += 1 if pp["pp_tris"] and pp.get("pp_matched_render") == pp["pp_tris"] \
                and pp.get("render_not_in_pp") == 0 else 0
            s["pp_shape_match"] += 1 if pp["pp_tris"] and pp.get("pp_shape_not_in_render") == 0 \
                and pp.get("render_not_in_pp") == 0 else 0
            s["pp_duplicate_corner_sets"] += pp.get("pp_duplicate_corner_sets", 0)
            s["pp_shape_not_in_render"] += pp.get("pp_shape_not_in_render", 0)
            s["pp_eq_raw_tris"] += 1 if pp["pp_tris"] == m["raw_tris"] else 0
            s["pp_plane_agree"] += pp.get("plane_normal_agree", 0)
            s["pp_bsp_capped"] += 1 if m["per_poly_bsp"].get("capped") else 0
            s["hull_bsp_capped"] += 1 if m["hull_bsp"].get("capped") else 0
            s["hull_tris_in_bsp"] += m["hull_bsp"].get("tris_in_tree", 0)
            s["hull_tris"] += h["tris"]
            s["over_game_tri_table"] += 1 if max(pp["pp_tris"], h["tris"]) > GAME_TRI_TABLE else 0
            if m.get("lazy_offsets_ok") is False:
                s["lazy_offset_mismatch"] += 1
            if m.get("lazy_offsets_ok") is True:
                s["lazy_offset_ok"] += 1
            for key in ("per_poly_bsp_sides", "hull_bsp_sides"):
                if m.get(key) and not m[key]["ok"]:
                    s[key + "_bad_meshes"] += 1
            s["tail_bytes"] += m.get("tail_bytes") or 0
            for k in m.get("flags_stored", []):
                s[f"stored_{k}={m['flags'][k]}"] += 1
            if m["flags"].get("UseSimpleBoxCollision") and h["tris"]:
                s["box_traces_use_hulls"] += 1
            if not m["flags"].get("UseSimpleLineCollision") and pp["pp_tris"]:
                s["line_traces_use_per_poly"] += 1
    return dict(s)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="scctformats.smcollision", description=__doc__.split("\n")[0])
    ap.add_argument("packages", nargs="+",
                    help="package names (StaticMeshes/<name>.usx), paths (.usx or .sdc), 'all' (every .usx) "
                         "or 'maps' (the static meshes stored inside every Packages/Maps/*.sdc)")
    ap.add_argument("--mesh", action="append", help="only these mesh names")
    ap.add_argument("--out", help="output folder (default local/formats/smcollision)")
    ap.add_argument("--print", action="store_true", help="print each mesh report")
    args = ap.parse_args(argv)
    root = game_root() / "Packages" / "StaticMeshes"
    if args.packages == ["all"]:
        paths = sorted(root.glob("*.usx"))
    elif args.packages == ["maps"]:
        paths = sorted((game_root() / "Packages" / "Maps").glob("*.sdc"))
    else:
        paths = [Path(p) if Path(p).suffix else root / f"{p}.usx" for p in args.packages]
    out = Path(args.out) if args.out else out_dir("smcollision_maps" if args.packages == ["maps"] else "smcollision")
    out.mkdir(parents=True, exist_ok=True)
    defaults = _defaults()
    results = []
    for p in paths:
        res = scan_package(p, args.mesh, defaults)
        results.append(res)
        (out / f"{p.stem}.json").write_text(json.dumps(res, indent=1), encoding="utf-8")
        s = summarize([res])
        print(f"{p.stem} (v{res['version']}): {s.get('meshes', 0)} meshes, {s.get('failed', 0)} failed, "
              f"hulls {s.get('hulls', 0)} (euler {s.get('hulls_euler_ok', 0)}, convex {s.get('hulls_convex', 0)}), "
              f"per-poly full match {s.get('pp_full_match', 0)}/{s.get('with_per_poly', 0)}")
        if args.print:
            for m in res["meshes"]:
                print(json.dumps(m, indent=1))
        for f in res["failed"][:5]:
            print(f"  FAILED {f['name']}: {f['error']}")
    total = summarize(results)
    if len(results) > 1:
        (out / "_summary.json").write_text(json.dumps(total, indent=1), encoding="utf-8")
    print(json.dumps(total, indent=1))
    return 1 if total.get("failed") else 0


if __name__ == "__main__":
    raise SystemExit(main())
