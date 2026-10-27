"""Minimal deterministic glTF 2.0 binary (.glb) writer and reader, plus the UE to glTF space mapping."""

from __future__ import annotations

import json
import struct

import numpy as np

GLB_MAGIC = 0x46546C67
CHUNK_JSON = 0x4E4F534A
CHUNK_BIN = 0x004E4942
FLOAT = 5126
UINT32 = 5125
ARRAY_BUFFER = 34962
ELEMENT_ARRAY_BUFFER = 34963


def ue_points_to_gltf(points: np.ndarray) -> np.ndarray:
    """UE (x, y, z) uu -> glTF (x, z, y) metres."""
    p = np.asarray(points, dtype=np.float32)
    return np.stack([p[:, 0], p[:, 2], p[:, 1]], axis=1) / np.float32(100.0)


def ue_dirs_to_gltf(dirs: np.ndarray) -> np.ndarray:
    d = np.asarray(dirs, dtype=np.float32)
    return np.stack([d[:, 0], d[:, 2], d[:, 1]], axis=1).astype(np.float32)


def ue_tris_to_gltf(tris: np.ndarray) -> np.ndarray:
    """The Y/Z swap mirrors the space, so (a, b, c) becomes (a, c, b) to keep the front face."""
    t = np.asarray(tris, dtype=np.uint32)
    return t[:, [0, 2, 1]]


class GlbBuilder:
    def __init__(self, generator: str):
        self.gltf: dict = {
            "asset": {"version": "2.0", "generator": generator},
            "scene": 0,
            "scenes": [{"nodes": []}],
            "nodes": [],
            "meshes": [],
            "materials": [],
            "accessors": [],
            "bufferViews": [],
            "buffers": [{"byteLength": 0}],
        }
        self.bin = bytearray()

    def _view(self, data: bytes, target: int) -> int:
        while len(self.bin) % 4:
            self.bin.append(0)
        self.gltf["bufferViews"].append({"buffer": 0, "byteOffset": len(self.bin), "byteLength": len(data),
                                         "target": target})
        self.bin += data
        return len(self.gltf["bufferViews"]) - 1

    def _accessor(self, array: np.ndarray, kind: str, component: int, target: int, bounds: bool = False) -> int:
        view = self._view(array.tobytes(), target)
        acc = {"bufferView": view, "componentType": component, "count": int(array.shape[0]), "type": kind}
        if bounds:
            acc["min"] = [float(v) for v in array.min(axis=0)]
            acc["max"] = [float(v) for v in array.max(axis=0)]
        self.gltf["accessors"].append(acc)
        return len(self.gltf["accessors"]) - 1

    def material(self, name: str, base_color=(1.0, 1.0, 1.0, 1.0), double_sided: bool = False) -> int:
        self.gltf["materials"].append({
            "name": name,
            "pbrMetallicRoughness": {"baseColorFactor": [float(c) for c in base_color], "metallicFactor": 0.0,
                                     "roughnessFactor": 1.0},
            "doubleSided": double_sided,
        })
        return len(self.gltf["materials"]) - 1

    def mesh(self, name: str, positions: np.ndarray, normals: np.ndarray, colors: np.ndarray, indices: np.ndarray,
             material: int) -> int:
        """Arrays already in glTF space: positions/normals (N, 3) float32, colors (N, 4) float32, indices (M, 3)."""
        pos = self._accessor(np.ascontiguousarray(positions, dtype=np.float32), "VEC3", FLOAT, ARRAY_BUFFER, True)
        nrm = self._accessor(np.ascontiguousarray(normals, dtype=np.float32), "VEC3", FLOAT, ARRAY_BUFFER)
        col = self._accessor(np.ascontiguousarray(colors, dtype=np.float32), "VEC4", FLOAT, ARRAY_BUFFER)
        idx = self._accessor(np.ascontiguousarray(indices, dtype=np.uint32).reshape(-1), "SCALAR", UINT32,
                             ELEMENT_ARRAY_BUFFER)
        self.gltf["meshes"].append({"name": name, "primitives": [{
            "attributes": {"POSITION": pos, "NORMAL": nrm, "COLOR_0": col}, "indices": idx, "material": material,
            "mode": 4}]})
        return len(self.gltf["meshes"]) - 1

    def node(self, name: str, mesh: int | None = None, children: list[int] | None = None,
             extras: dict | None = None, root: bool = False) -> int:
        n: dict = {"name": name}
        if mesh is not None:
            n["mesh"] = mesh
        if children:
            n["children"] = children
        if extras:
            n["extras"] = extras
        self.gltf["nodes"].append(n)
        index = len(self.gltf["nodes"]) - 1
        if root:
            self.gltf["scenes"][0]["nodes"].append(index)
        return index

    def to_bytes(self) -> bytes:
        while len(self.bin) % 4:
            self.bin.append(0)
        self.gltf["buffers"][0]["byteLength"] = len(self.bin)
        js = json.dumps(self.gltf, sort_keys=True, separators=(",", ":")).encode("utf-8")
        js += b" " * ((4 - len(js) % 4) % 4)
        total = 12 + 8 + len(js) + 8 + len(self.bin)
        out = struct.pack("<III", GLB_MAGIC, 2, total)
        out += struct.pack("<II", len(js), CHUNK_JSON) + js
        out += struct.pack("<II", len(self.bin), CHUNK_BIN) + bytes(self.bin)
        return out


def read_glb(data: bytes) -> tuple[dict, bytes]:
    """(json, bin) of a .glb; raises ValueError on a malformed container."""
    if len(data) < 20:
        raise ValueError("glb too small")
    magic, version, total = struct.unpack_from("<III", data, 0)
    if magic != GLB_MAGIC or version != 2 or total != len(data):
        raise ValueError("bad glb header")
    jlen, jtype = struct.unpack_from("<II", data, 12)
    if jtype != CHUNK_JSON or jlen % 4:
        raise ValueError("bad glb JSON chunk")
    js = json.loads(data[20:20 + jlen].decode("utf-8"))
    pos = 20 + jlen
    binary = b""
    if pos < total:
        blen, btype = struct.unpack_from("<II", data, pos)
        if btype != CHUNK_BIN:
            raise ValueError("bad glb BIN chunk")
        binary = data[pos + 8:pos + 8 + blen]
    return js, binary


def accessor_array(gltf: dict, binary: bytes, index: int) -> np.ndarray:
    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    dtype = {FLOAT: np.float32, UINT32: np.uint32}[acc["componentType"]]
    width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[acc["type"]]
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    arr = np.frombuffer(binary, dtype=dtype, count=acc["count"] * width, offset=start)
    return arr.reshape(acc["count"], width) if width > 1 else arr


def pack_glb(gltf: dict, binary: bytes) -> bytes:
    """A glTF document and its one binary buffer as a .glb (the buffer becomes the BIN chunk, no uri)."""
    doc = dict(gltf)
    data = bytes(binary) + b"\0" * ((4 - len(binary) % 4) % 4)
    if data:
        doc["buffers"] = [{"byteLength": len(data)}]
    else:
        doc.pop("buffers", None)
    js = json.dumps(doc, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode("utf-8")
    js += b" " * ((4 - len(js) % 4) % 4)
    total = 12 + 8 + len(js) + (8 + len(data) if data else 0)
    out = struct.pack("<III", GLB_MAGIC, 2, total) + struct.pack("<II", len(js), CHUNK_JSON) + js
    if data:
        out += struct.pack("<II", len(data), CHUNK_BIN) + data
    return out
