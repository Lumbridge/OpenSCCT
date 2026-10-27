"""UStaticMesh reader (SCCT Versus / UE2.5 layout, package versions 172-300).

Serialized after the tagged properties:

    FBox   BoundingBox   (Min f32x3, Max f32x3, u8 IsValid)   25 bytes
    FSphere BoundingSphere (f32x4)                             16 bytes
    compact nSections; each 14 bytes:
        i32 f4, u16 FirstIndex, u16 FirstVertex, u16 LastVertex, u16 fE, u16 NumFaces
    VertexStream : compact n; n x (f32x3 Position, f32x3 Normal); i32 Revision
    ColorStream  : compact n; n x u32 BGRA; i32 Revision
    AlphaStream  : compact n; n x u32 BGRA; i32 Revision
    UVStreams    : compact nStreams; each: compact n; n x (f32 U, f32 V); i32 CoordIndex; i32 Revision
    IndexBuffer  : compact n; n x u16; i32 Revision
    WireframeIndexBuffer : compact n; n x u16; i32 Revision
    (collision, raw triangles, internal version: not needed)

Section i draws NumFaces triangles from IndexBuffer[FirstIndex:] with
Materials[i].Material (an object ref resolved by the caller).
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from typing import List, Optional, Tuple

import numpy as np

from .package import Export, Package, PackageError, Reader
from .properties import read_properties, skip_state_frame


@dataclass
class Section:
    first_index: int
    first_vertex: int
    last_vertex: int
    num_faces: int
    material_ref: int = 0        # object ref into the mesh package
    enable_collision: bool = True


@dataclass
class StaticMesh:
    name: str
    positions: np.ndarray        # (n, 3) float32
    normals: np.ndarray          # (n, 3) float32
    uvs: List[np.ndarray]        # each (n, 2) float32
    colors: Optional[np.ndarray] # (n, 4) uint8 RGBA or None
    indices: np.ndarray          # (m,) uint16/uint32 triangle list
    sections: List[Section]
    bbox_min: Tuple[float, float, float]
    bbox_max: Tuple[float, float, float]
    props: dict = field(default_factory=dict)

    @property
    def triangle_count(self) -> int:
        return len(self.indices) // 3


def _read_stream_u32(r: Reader) -> Optional[np.ndarray]:
    n = r.compact()
    if n < 0 or n * 4 > r.remaining():
        raise PackageError(f"bad colour stream count {n}")
    raw = r.raw(n * 4)
    r.i32()  # revision
    if n == 0:
        return None
    bgra = np.frombuffer(raw, dtype=np.uint8).reshape(n, 4)
    return bgra[:, [2, 1, 0, 3]].copy()


def read_static_mesh(pkg: Package, e: Export) -> StaticMesh:
    r = pkg.export_reader(e)
    skip_state_frame(r, e.flags)
    props = read_properties(r)

    bmin = r.vec3()
    bmax = r.vec3()
    r.u8()          # IsValid
    r.raw(16)       # bounding sphere

    n_sections = r.compact()
    if n_sections < 0 or n_sections > 4096:
        raise PackageError(f"{e.name}: bad section count {n_sections}")
    sections: List[Section] = []
    for _ in range(n_sections):
        r.i32()
        first_index = r.u16()
        first_vertex = r.u16()
        last_vertex = r.u16()
        r.u16()
        num_faces = r.u16()
        sections.append(Section(first_index, first_vertex, last_vertex, num_faces))

    n = r.compact()
    if n < 0 or n * 24 > r.remaining():
        raise PackageError(f"{e.name}: bad vertex count {n}")
    vraw = np.frombuffer(r.raw(n * 24), dtype=np.float32).reshape(n, 6) if n else np.zeros((0, 6), np.float32)
    r.i32()
    positions = np.ascontiguousarray(vraw[:, 0:3])
    normals = np.ascontiguousarray(vraw[:, 3:6])

    colors = _read_stream_u32(r)
    _alpha = _read_stream_u32(r)

    n_uv = r.compact()
    if n_uv < 0 or n_uv > 8:
        raise PackageError(f"{e.name}: bad uv stream count {n_uv}")
    uvs: List[np.ndarray] = []
    for _ in range(n_uv):
        m = r.compact()
        if m < 0 or m * 8 > r.remaining():
            raise PackageError(f"{e.name}: bad uv count {m}")
        uv = np.frombuffer(r.raw(m * 8), dtype=np.float32).reshape(m, 2) if m else np.zeros((0, 2), np.float32)
        r.i32()  # coord index
        r.i32()  # revision
        uvs.append(uv.copy())

    ni = r.compact()
    if ni < 0 or ni * 2 > r.remaining():
        raise PackageError(f"{e.name}: bad index count {ni}")
    indices = np.frombuffer(r.raw(ni * 2), dtype=np.uint16).astype(np.uint32) if ni else np.zeros((0,), np.uint32)
    r.i32()

    # Materials array from the tagged properties: list of {Material, EnableCollision}
    mats = props.get("Materials")
    if isinstance(mats, list):
        for i, m in enumerate(mats):
            if i < len(sections) and isinstance(m, dict):
                sections[i].material_ref = int(m.get("Material", 0) or 0)
                sections[i].enable_collision = bool(m.get("EnableCollision", True))
    elif isinstance(mats, dict) and "_raw" in mats:
        pass  # opaque; caller keeps default material

    return StaticMesh(
        name=e.name,
        positions=positions,
        normals=normals,
        uvs=uvs,
        colors=colors,
        indices=indices,
        sections=sections,
        bbox_min=tuple(bmin),
        bbox_max=tuple(bmax),
        props=props,
    )


def iter_static_meshes(pkg: Package):
    for e in pkg.exports_of_class("StaticMesh"):
        if e.size > 0:
            yield e
