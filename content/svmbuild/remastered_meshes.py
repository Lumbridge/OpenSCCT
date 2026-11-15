"""Optional, local render-geometry overrides. Collision and gameplay exports keep their source geometry."""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path

import numpy as np

from .paths import REPO_ROOT


def manifest_path() -> Path | None:
    value = os.environ.get("SVM_MESH_OVERRIDES", "").strip()
    if not value:
        return None
    path = Path(value).resolve()
    path.relative_to(REPO_ROOT.resolve())  # cache inputs use the repo: scheme
    return path


def inputs() -> list[Path]:
    path = manifest_path()
    if path is None:
        return []
    doc = json.loads(path.read_text(encoding="utf-8"))
    if doc.get("format") != "svm.mesh-overrides/1":
        raise ValueError("Unknown mesh override manifest format")
    out = [path]
    if doc.get("credits"):
        credit = (path.parent / doc["credits"]).resolve()
        credit.relative_to(path.parent)
        out.append(credit)
    for entry in doc["meshes"].values():
        asset = (path.parent / entry["file"]).resolve()
        asset.relative_to(path.parent)
        out.append(asset)
    return out


def credits() -> bytes:
    path = manifest_path()
    doc = json.loads(path.read_text(encoding="utf-8"))
    file = (path.parent / doc["credits"]).resolve()
    file.relative_to(path.parent)
    prefix = f"Source: {doc['source']}\nChanges: {doc['changes']}\n\n".encode("utf-8")
    return prefix + file.read_bytes()


def _array(doc: dict, binary: bytes, index: int) -> np.ndarray:
    acc = doc["accessors"][index]
    view = doc["bufferViews"][acc["bufferView"]]
    dtype = {5121: "u1", 5123: "<u2", 5125: "<u4", 5126: "<f4"}[acc["componentType"]]
    width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[acc["type"]]
    offset = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    return np.ndarray((acc["count"], width), dtype=dtype, buffer=binary, offset=offset,
                      strides=(view.get("byteStride", np.dtype(dtype).itemsize * width), np.dtype(dtype).itemsize)).copy()


def _colours(positions: np.ndarray, old: np.ndarray, values: np.ndarray, indices: np.ndarray) -> np.ndarray:
    """Sample the closest original surface, retaining its barycentric lighting instead of mixing nearby faces."""
    result = np.empty((len(positions), values.shape[1]), np.float32)
    triangles = indices.reshape(-1, 3)
    a, b, c = (old[triangles[:, i]] for i in range(3))
    ab, ac = b - a, c - a
    d00, d01, d11 = (np.sum(x * y, axis=1) for x, y in ((ab, ab), (ab, ac), (ac, ac)))
    determinant = d00 * d11 - d01 * d01
    safe = np.where(np.abs(determinant) > 1e-20, determinant, 1)
    triangle_values = values[triangles]
    for start in range(0, len(positions), 64):
        points = positions[start:start + 64, None, :]
        ap = points - a
        d20, d21 = np.sum(ap * ab, axis=2), np.sum(ap * ac, axis=2)
        u = (d11 * d20 - d01 * d21) / safe
        v = (d00 * d21 - d01 * d20) / safe
        weights = np.stack([1 - u - v, u, v], axis=2)
        projected = a + ab * u[:, :, None] + ac * v[:, :, None]
        best_distance = np.sum((points - projected) ** 2, axis=2)
        inside = (weights.min(axis=2) >= 0) & (np.abs(determinant) > 1e-20)
        best_distance = np.where(inside, best_distance, np.inf)
        # Outside a triangle, its closest point lies on one of its three segments.
        for first, second in ((0, 1), (1, 2), (2, 0)):
            p0, p1 = old[triangles[:, first]], old[triangles[:, second]]
            edge = p1 - p0
            t = np.clip(np.sum((points - p0) * edge, axis=2) / np.maximum(np.sum(edge * edge, axis=1), 1e-20), 0, 1)
            nearest = p0 + edge * t[:, :, None]
            distance = np.sum((points - nearest) ** 2, axis=2)
            take = distance < best_distance
            edge_weights = np.zeros_like(weights)
            edge_weights[:, :, first], edge_weights[:, :, second] = 1 - t, t
            weights = np.where(take[:, :, None], edge_weights, weights)
            best_distance = np.minimum(best_distance, distance)
        nearest_triangle = np.argmin(best_distance, axis=1)
        barycentric = weights[np.arange(len(points)), nearest_triangle]
        result[start:start + 64] = np.sum(triangle_values[nearest_triangle] * barycentric[:, :, None], axis=1)
    if np.issubdtype(values.dtype, np.integer):
        return np.rint(result).clip(0, np.iinfo(values.dtype).max).astype(values.dtype)
    return result.astype(values.dtype)


def apply(doc: dict, binary: bytes) -> tuple[bytes, dict]:
    path = manifest_path()
    if path is None:
        return binary, {"meshes": 0, "assets": []}
    manifest = json.loads(path.read_text(encoding="utf-8"))
    if manifest.get("format") != "svm.mesh-overrides/1":
        raise ValueError("Unknown mesh override manifest format")
    data = bytearray(binary)
    seen = set()
    used = set()
    skipped = []
    cache = {}

    def append(arr: np.ndarray, kind: str, component: int, normalized=False) -> int:
        data.extend(bytes((-len(data)) % 4))
        raw = np.ascontiguousarray(arr).tobytes()
        view = len(doc["bufferViews"])
        doc["bufferViews"].append({"buffer": 0, "byteOffset": len(data), "byteLength": len(raw)})
        data.extend(raw)
        acc = {"bufferView": view, "componentType": component, "count": len(arr), "type": kind}
        if normalized:
            acc["normalized"] = True
        if kind == "VEC3":
            acc.update(min=arr.min(axis=0).tolist(), max=arr.max(axis=0).tolist())
        index = len(doc["accessors"])
        doc["accessors"].append(acc)
        return index

    for node in doc["nodes"]:
        asset = node.get("extras", {}).get("svmap", {}).get("asset")
        entry = manifest["meshes"].get(asset)
        index = node.get("mesh")
        if entry is None or index is None or index in seen:
            continue
        seen.add(index)
        mesh = doc["meshes"][index]
        if any("TEXCOORD_1" in p["attributes"] for p in mesh["primitives"]):
            skipped.append({"asset": asset, "reason": "requires a lightmap rebake"})
            continue
        if asset not in cache:
            file = (path.parent / entry["file"]).resolve()
            file.relative_to(path.parent)
            raw = file.read_bytes()
            if hashlib.sha256(raw).hexdigest() != entry["sha256"]:
                raise ValueError(f"Mesh override hash mismatch: {asset}")
            with np.load(file, allow_pickle=False) as arrays:
                cache[asset] = {k: arrays[k] for k in arrays.files}
        arrays = cache[asset]
        positions, normals, uv = (arrays[k] for k in ("positions", "normals", "uv"))
        if positions.ndim != 2 or positions.shape[1] != 3 or normals.shape != positions.shape or uv.shape != (len(positions), 2):
            raise ValueError(f"Invalid mesh override arrays: {asset}")
        if not all(np.isfinite(v).all() for v in (positions, normals, uv)):
            raise ValueError(f"Non-finite mesh override: {asset}")
        for primitive in mesh["primitives"]:
            section = primitive.get("extras", {}).get("scctSection")
            if section is None or f"indices_{section}" not in arrays:
                raise ValueError(f"Mesh override section mismatch: {asset}")
        for primitive in mesh["primitives"]:
            attributes = primitive["attributes"]
            old_positions = _array(doc, binary, attributes["POSITION"])
            old_uv = _array(doc, binary, attributes["TEXCOORD_0"])
            template_uv = arrays["template_uv"]
            if template_uv.shape != old_uv.shape:
                raise ValueError(f"Mesh override UV template mismatch: {asset}")
            mapped_uv = uv
            if not np.allclose(template_uv, old_uv, atol=1e-5):
                # The original material can pan, scale or rotate UVs. Keep its affine mapping.
                basis = np.column_stack([template_uv, np.ones(len(template_uv))])
                transform = np.linalg.lstsq(basis, old_uv, rcond=None)[0]
                if not np.allclose(basis @ transform, old_uv, atol=1e-4):
                    raise ValueError(f"Mesh override needs a non-affine UV mapping: {asset}")
                mapped_uv = np.column_stack([uv, np.ones(len(uv))]) @ transform
            old_colour = _array(doc, binary, attributes["COLOR_0"]) if "COLOR_0" in attributes else None
            colour_info = doc["accessors"][attributes["COLOR_0"]] if old_colour is not None else None
            indices = arrays[f"indices_{primitive['extras']['scctSection']}"]
            if indices.size == 0 or indices.size % 3 or indices.min() < 0 or indices.max() >= len(positions):
                raise ValueError(f"Invalid mesh override indices: {asset}")
            primitive["attributes"] = {
                "POSITION": append(positions.astype("<f4"), "VEC3", 5126),
                "NORMAL": append(normals.astype("<f4"), "VEC3", 5126),
                "TEXCOORD_0": append(mapped_uv.astype("<f4"), "VEC2", 5126),
            }
            if old_colour is not None:
                old_indices = _array(doc, binary, primitive["indices"]).reshape(-1)
                colours = _colours(positions, old_positions, old_colour, old_indices)
                primitive["attributes"]["COLOR_0"] = append(colours, "VEC4", colour_info["componentType"], colour_info.get("normalized", False))
            primitive["indices"] = append(indices.astype("<u4").reshape(-1), "SCALAR", 5125)
        used.add(asset)
    return bytes(data), {"meshes": len(seen) - len(skipped), "assets": sorted(used), "skipped": skipped,
                         "source": manifest.get("source"), "credits": manifest.get("credits")}
