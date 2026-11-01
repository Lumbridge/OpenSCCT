"""USkeletalMesh reader for SCCT Versus (.ukx), reverse-engineered on this build.

Layout (verified on SPerso.ukx DEF_01/ATT_01 and the map animation packages):

    tagged properties (None)
    FBox(25) FSphere(16)
    i32 Version (4), i32 VertexCount
    TArray<u32> Verts (empty), TArray<obj> Textures
    FVector MeshScale, FVector MeshOrigin, FRotator RotOrigin
    TArray<u16> (per-face values), TArray(empty), TArray(empty),
    TArray<FMeshMaterial {u32 PolyFlags, i32 TextureIndex}>, 6 LOD floats, padding
    compact NumBones; FMeshBone[]: name, u32 flags, quat(4f), pos(3f), f32 length,
        size(3f), i32 NumChildren, i32 ParentIndex
    obj Animation, i32 SkeletalDepth, five empty arrays, SCCT-specific tables ...
    ... (LOD/section tables we do not need) ...
    compact NumPoints == VertexCount; FVector[]           <- located by search
    i32 0; compact NumWedges; {u16 iVertex, f32 U, f32 V}[]
    i32 0; compact NumFaces; {u16 w0, w1, w2, u8 Material, u8 Flags, i32 Smoothing}[]
    i32 0; compact NumInfluences; {f32 Weight, u16 Point, u16 Bone}[]

The skeleton is found by scanning for a bone-count compact followed by a
decodable chain of bones; the vertex block by the VertexCount compact followed
by plausible float3 data. Both scans are cheap (single export).
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from typing import List, Optional, Tuple

import numpy as np

from .package import Export, Package, PackageError, Reader, read_compact
from .properties import read_properties, skip_state_frame


@dataclass
class Bone:
    name: str
    parent: int
    rotation: Tuple[float, float, float, float]   # quaternion x, y, z, w (UE2 convention)
    position: Tuple[float, float, float]
    length: float
    num_children: int


@dataclass
class SkeletalMesh:
    name: str
    bones: List[Bone]
    points: np.ndarray                 # (n, 3)
    wedges: np.ndarray                 # (m, 3) float: iVertex, U, V
    faces: np.ndarray                  # (k, 3) int wedge indices
    face_material: np.ndarray          # (k,) int
    influences: np.ndarray             # (j, 3): weight, point, bone
    material_refs: List[int]           # Textures array object refs
    mesh_scale: Tuple[float, float, float]
    mesh_origin: Tuple[float, float, float]
    rot_origin: Tuple[int, int, int]
    animation_ref: int = 0
    props: dict = field(default_factory=dict)
    normals: Optional[np.ndarray] = None  # per wedge, from the cooked render LOD when requested


def _encode_compact(n: int) -> bytes:
    out = bytearray([n & 63])
    n >>= 6
    if n:
        out[0] |= 64
    while n:
        b = n & 127
        n >>= 7
        out.append(b | (128 if n else 0))
    return bytes(out)


def _try_bone(d: bytes, p: int, pkg: Package, base: int) -> Optional[Tuple[Bone, int]]:
    r = Reader(d, p, pkg, base=base)
    try:
        name = r.name()
        r.u32()
        q = struct.unpack("<4f", r.raw(16))
        pos = r.vec3()
        length = r.f32()
        r.vec3()
        nch = r.i32()
        par = r.i32()
    except Exception:
        return None
    s = sum(x * x for x in q)
    if not (0.95 < s < 1.05) or not (0 <= nch < 512) or not (-1 <= par < 1024):
        return None
    return Bone(name, par, q, pos, length, nch), r.p


def _find_skeleton(d: bytes, pkg: Package, base: int, start_hint: int) -> Tuple[int, List[Bone], int]:
    """Return (count offset, bones, end) for the first decodable bone chain."""
    best = None
    limit = len(d)
    p = start_hint
    while p < limit:
        n, q = read_compact(d, p)
        if 1 <= n <= 512:
            bones = []
            pos = q
            while len(bones) < n:
                b = _try_bone(d, pos, pkg, base)
                if not b:
                    break
                bones.append(b[0])
                pos = b[1]
            if len(bones) == n and n >= 1:
                return p, bones, pos
        p += 1
    raise PackageError("skeleton not found")


def _points_candidates(d: bytes, count: int, start: int):
    """Offsets of plausible Points arrays (compact count == VertexCount, finite float3 data, then i32 0)."""
    enc = _encode_compact(count)
    p = d.find(enc, start)
    while p >= 0:
        q = p + len(enc)
        if q + count * 12 <= len(d):
            arr = np.frombuffer(d, dtype=np.float32, count=count * 3, offset=q).reshape(count, 3)
            ok = np.all(np.isfinite(arr), axis=1) & (np.abs(arr).max(axis=1) < 200000)
            # A few garbage points (unreferenced, or cooked leftovers) must not hide an otherwise valid array.
            if ok.mean() > 0.97 and np.abs(arr[ok]).max(initial=0) > 0.5:
                tail = q + count * 12
                if tail + 6 <= len(d) and struct.unpack_from("<i", d, tail)[0] == 0:
                    yield p
        p = d.find(enc, p + 1)


def _find_points(d: bytes, count: int, start: int) -> int:
    for p in _points_candidates(d, count, start):
        return p
    raise PackageError("points array not found")


def _read_geometry(d: bytes, pts_off: int, pkg: Package, base: int, name: str, nbones: int):
    """Points | i32 0 | Wedges | i32 0 | Faces | i32 0 | Influences, validated end to end."""
    rr = Reader(d, pts_off, pkg, base=base)
    npts = rr.compact()
    points = np.frombuffer(rr.raw(npts * 12), dtype=np.float32).reshape(npts, 3).copy()
    bad = ~(np.all(np.isfinite(points), axis=1) & (np.abs(points).max(axis=1) < 200000))
    rr.i32()
    nw = rr.compact()
    if nw <= 0 or nw > 200000 or rr.p + nw * 10 > len(d):
        raise PackageError(f"{name}: bad wedge count {nw}")
    wraw = rr.raw(nw * 10)
    wedges = np.array([struct.unpack_from("<Hff", wraw, i * 10) for i in range(nw)], dtype=np.float32)
    if rr.i32() != 0:
        raise PackageError(f"{name}: no separator after wedges")
    nf = rr.compact()
    if nf <= 0 or nf > 200000 or rr.p + nf * 12 > len(d):
        raise PackageError(f"{name}: bad face count {nf}")
    fraw = rr.raw(nf * 12)
    faces = np.zeros((nf, 3), np.int32)
    fmat = np.zeros((nf,), np.int32)
    for i in range(nf):
        w0, w1, w2, mat, _flags, _smooth = struct.unpack_from("<3HBBi", fraw, i * 12)
        faces[i] = (w0, w1, w2)
        fmat[i] = mat
    if rr.i32() != 0:
        raise PackageError(f"{name}: no separator after faces")
    ni = rr.compact()
    if ni < 0 or ni > 400000 or rr.p + ni * 8 > len(d):
        raise PackageError(f"{name}: bad influence count {ni}")
    iraw = rr.raw(ni * 8)
    infl = np.array([struct.unpack_from("<fHH", iraw, i * 8) for i in range(ni)], dtype=np.float32).reshape(-1, 3)
    if wedges[:, 0].max(initial=0) >= npts:
        raise PackageError(f"{name}: wedge vertex index out of range")
    if bad.any():
        used = np.zeros(npts, bool)
        used[wedges[:, 0].astype(np.int64)] = True
        if (bad & used).sum() > 0.01 * npts:
            raise PackageError(f"{name}: {int((bad & used).sum())} referenced points are garbage")
        # Collapse the stray points onto a referenced neighbour so bounds and skinning stay sane.
        good = np.nonzero(~bad)[0]
        for i in np.nonzero(bad)[0]:
            points[i] = points[good[np.argmin(np.abs(good - i))]] if len(good) else 0.0
    if faces.max(initial=0) >= nw:
        raise PackageError(f"{name}: face wedge index out of range")
    if ni and infl[:, 2].max() >= nbones:
        raise PackageError(f"{name}: influence bone index out of range")
    return points, wedges, faces, fmat, infl


def _read_lod_stream(d: bytes, p: int, name: str, nbones: int, *, include_normals: bool = False):
    """LOD model 0, for meshes whose base Wedges/Faces are empty (geometry only in the render LODs).

    Layout right after the skeleton tables (verified on MAL ventilateur_mesh and the sqt13 crane/wall):

        compact N; u32 SkinningStream[N]      compiled skinning program, one record per render vertex:
            new vertex:  influence dwords {flags:4 | weight:16 | bone*6:12} (flag 0x1 = another influence
                         follows; flag 0x8 on the first dword = also keep the skinned point in the next cache
                         slot), then f32 U, f32 V
            duplicate:   0xF0000000 | cache slot * 6, f32 U, f32 V (a cached point with a different UV)
            0xFFFFFFFF   end
        compact M; FSkinPoint {FVector, u32 packed normal}[M]   mesh space, in "new vertex" order
        i32 NumDynWedges (== records)
        compact S; FSkelMeshSection[S] (18 B: u16 Material, MinStream, MinWedge, MaxWedge, NumStream, Bone, fE,
            FirstFace, NumFaces)    smooth sections
        compact R; FSkelMeshSection[R]   rigid sections (MinStream/NumStream = range in RigidIndices, one Bone)
        compact I; u16 SmoothIndices[I]; i32 revision
        compact J; u16 RigidIndices[J]; i32 revision
        i32, i32, i32; compact K; {FVector pos, FVector normal, f32 U, V}[K]   rigid vertices, mesh space
    """
    n, q = read_compact(d, p)
    if n < 4 or q + 4 * n > len(d):
        raise PackageError(f"{name}: no skinning stream")
    words = struct.unpack_from(f"<{n}I", d, q)
    if words[-1] != 0xFFFFFFFF:
        raise PackageError(f"{name}: skinning stream not terminated")
    uv = np.frombuffer(d, dtype=np.float32, count=n, offset=q)
    recs = []          # (point index, u, v)
    infl = []          # (weight, point, bone)
    npoint = 0
    cache = []         # skinned points kept for duplicates (verified against MIS_ANM's base-geometry copy)
    i = 0
    while words[i] != 0xFFFFFFFF:
        c = words[i]
        if c >> 28 == 0xF:
            ref = (c & 0x0FFFFFFF)
            if ref % 6 or ref // 6 >= len(cache) or i + 2 >= n:
                raise PackageError(f"{name}: bad duplicate record")
            recs.append((cache[ref // 6], uv[i + 1], uv[i + 2]))
            i += 3
            continue
        if c >> 31:
            cache.append(npoint)
        group = []
        while True:
            c = words[i]
            if (c & 0xFFF) % 6 or (c & 0xFFF) // 6 >= nbones:
                raise PackageError(f"{name}: bad influence record")
            group.append((((c >> 12) & 0xFFFF) / 65535.0, (c & 0xFFF) // 6))
            i += 1
            if not (c >> 28) & 1:
                break
        total = sum(w for w, _ in group) or 1.0
        for w, b in group:
            infl.append((w / total, npoint, b))
        if i + 1 >= n:
            raise PackageError(f"{name}: truncated skinning stream")
        recs.append((npoint, uv[i], uv[i + 1]))
        npoint += 1
        i += 2
    r = Reader(d, q + 4 * n, None)
    m = r.compact()
    if m != npoint or r.p + m * 16 > len(d):
        raise PackageError(f"{name}: {m} skin points for {npoint} skinned vertices")
    points = np.array([struct.unpack_from("<3f", d, r.p + k * 16) for k in range(m)], dtype=np.float32).reshape(-1, 3)
    # UE2 FSkinPoint packs XYZ into unsigned 10-bit channels, with 511 representing zero.
    packed = np.array([struct.unpack_from("<I", d, r.p + k * 16 + 12)[0] for k in range(m)], dtype=np.uint32)
    point_normals = np.column_stack([(packed >> shift) & 1023 for shift in (0, 10, 20)]).astype(np.float32) / 511.0 - 1.0
    normals = [point_normals[np.array(recs, dtype=np.float32).reshape(-1, 3)[:, 0].astype(np.int32)]]
    r.p += m * 16
    if r.i32() != len(recs):
        raise PackageError(f"{name}: NumDynWedges mismatch")
    sections = []
    for _ in range(r.compact()):
        sections.append(struct.unpack_from("<9H", d, r.p))
        r.p += 18
    rigid = []
    for _ in range(r.compact()):
        rigid.append(struct.unpack_from("<9H", d, r.p))
        r.p += 18
    ni = r.compact()
    idx = np.frombuffer(d, dtype=np.uint16, count=ni, offset=r.p).astype(np.int32).reshape(-1, 3)
    r.p += ni * 2
    r.i32()
    if len(idx) != sum(s_[8] for s_ in sections) or (len(idx) and idx.max() >= len(recs)):
        raise PackageError(f"{name}: index buffer does not match the sections")
    fmat = np.zeros((len(idx),), np.int32)
    for s_ in sections:
        fmat[s_[7]:s_[7] + s_[8]] = s_[0]
    points = [points]
    wedges = [np.array(recs, dtype=np.float32).reshape(-1, 3)]
    faces = [idx]
    fmats = [fmat]
    if rigid:
        nj = r.compact()
        ridx = np.frombuffer(d, dtype=np.uint16, count=nj, offset=r.p).astype(np.int32)
        r.p += nj * 2
        r.i32()
        r.raw(12)
        nk = r.compact()
        if r.p + nk * 32 > len(d):
            raise PackageError(f"{name}: truncated rigid vertex stream")
        verts = np.frombuffer(d, dtype=np.float32, count=nk * 8, offset=r.p).reshape(nk, 8)
        if nj != sum(s_[4] for s_ in rigid) or (nj and ridx.max() >= nk):
            raise PackageError(f"{name}: rigid index buffer does not match the sections")
        base_p, base_w = npoint, len(recs)
        for s_ in rigid:
            mat, first, count, bone = s_[0], s_[1], s_[4], s_[5]
            if bone >= nbones:
                raise PackageError(f"{name}: rigid section bone {bone} out of range")
            tri = ridx[first:first + count]
            used, local = np.unique(tri, return_inverse=True)
            # Each section gets its own copy of its vertices: one bone per copy, no cross-section conflicts.
            points.append(verts[used, :3].copy())
            normals.append(verts[used, 3:6].copy())
            wedges.append(np.column_stack([np.arange(base_p, base_p + len(used)), verts[used, 6], verts[used, 7]]).astype(np.float32))
            faces.append((local + base_w).reshape(-1, 3).astype(np.int32))
            fmats.append(np.full((count // 3,), mat, np.int32))
            infl.extend((1.0, base_p + k, bone) for k in range(len(used)))
            base_p += len(used)
            base_w += len(used)
    geometry = (np.concatenate(points), np.concatenate(wedges), np.concatenate(faces), np.concatenate(fmats),
                np.array(infl, dtype=np.float32).reshape(-1, 3))
    return (*geometry, np.concatenate(normals)) if include_normals else geometry


def read_skeletal_mesh(pkg: Package, e: Export, *, prefer_render_lod: bool = False) -> SkeletalMesh:
    d = pkg.data[e.offset:e.offset + e.size]
    r = Reader(d, 0, pkg, base=e.offset)
    skip_state_frame(r, e.flags)
    props = read_properties(r)
    r.raw(25)
    r.raw(16)
    version = r.i32()
    vertex_count = r.i32()
    nv = r.compact()
    r.raw(nv * 4)
    nt = r.compact()
    textures = [r.compact() for _ in range(nt)]
    mesh_scale = r.vec3()
    mesh_origin = r.vec3()
    rot_origin = r.rot()
    if pkg.version <= 174:
        n = r.compact()
        r.raw(n * 2)

    count_off, bones, skel_end = _find_skeleton(d, pkg, e.offset, r.p)
    rr = Reader(d, skel_end, pkg, base=e.offset)
    animation_ref = rr.compact()

    geometry = None
    last_error: Optional[Exception] = None
    for pts_off in _points_candidates(d, vertex_count, skel_end):
        try:
            geometry = _read_geometry(d, pts_off, pkg, e.offset, e.name, len(bones))
            break
        except (PackageError, struct.error, ValueError) as ex:
            last_error = ex
    normals = None
    if geometry is None or prefer_render_lod:
        # Decode render LOD 0; its skinning stream starts a few bytes after the skeleton.
        for p in range(skel_end, min(skel_end + 64, len(d))):
            try:
                *lod_geometry, normals = _read_lod_stream(d, p, e.name, len(bones), include_normals=True)
                geometry = tuple(lod_geometry)
                if not prefer_render_lod:
                    normals = None
                break
            except (PackageError, struct.error, ValueError, IndexError):
                continue
    if geometry is None:
        raise last_error or PackageError("points array not found")
    points, wedges, faces, fmat, infl = geometry

    return SkeletalMesh(
        name=e.name, bones=bones, points=points, wedges=wedges, faces=faces, face_material=fmat,
        influences=infl, material_refs=textures, mesh_scale=tuple(mesh_scale), mesh_origin=tuple(mesh_origin),
        rot_origin=tuple(rot_origin), animation_ref=animation_ref, props=props, normals=normals,
    )
