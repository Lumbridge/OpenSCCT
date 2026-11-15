"""Stage matching SCCT-Repack remaster meshes as local render overrides.

Reads the downloaded dynamic-pc.umd and an unmodified Versus install. Needs a
separate SCCT-Repack checkout (--repack); its MIT reader is never installed into
the game. The manifest and derived geometry belong under ignored local/.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import sys

import numpy as np

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "scctpkg"))
from scctpkg.package import Package
from scctpkg.staticmesh import read_static_mesh


def _signature(positions, uv):
    points = np.rint(np.asarray(positions) * 1000).astype(np.int64)
    texels = np.rint(np.asarray(uv) * 2048).astype(np.int64)
    return {tuple(row) for row in np.concatenate([points, texels], axis=1)}


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--archive", type=Path, required=True)
    ap.add_argument("--repack", type=Path, required=True)
    ap.add_argument("--game", type=Path, required=True)
    ap.add_argument("--credits", type=Path, required=True)
    ap.add_argument("--out", type=Path, required=True)
    args = ap.parse_args()
    args.out.resolve().relative_to((REPO / "local").resolve())
    sys.path.insert(0, str(args.repack.resolve()))
    import scct
    from scct.umd import _read_archive
    from scct.package import parse_package
    from scct.mesh import parse_static_mesh, section_triangles, unpack_vec

    Path(scct.__file__).resolve().relative_to(args.repack.resolve())
    args.out.mkdir(parents=True, exist_ok=True)
    # Retain the pack's per-model author, source URL, licence and description of changes.
    (args.out / "Remastered_CC.txt").write_bytes(args.credits.read_bytes())
    ours = {}
    for path in sorted((args.game / "Packages" / "StaticMeshes").glob("*.usx")):
        pkg = Package.load(path)
        for exp in pkg.exports_of_class("StaticMesh"):
            ours.setdefault(exp.name.lower(), []).append((pkg, exp))
    meshes, skipped = {}, []
    rotation = np.asarray([-1, -1, 1], np.float32)
    with args.archive.open("rb") as stream:
        archive = _read_archive(stream, args.archive.stat().st_size)
        for entry in archive.entries:
            if not entry.name.lower().endswith(".usx"):
                continue
            stream.seek(entry.offset)
            raw = stream.read(entry.size)
            pkg = parse_package(raw)
            for exp in pkg.exports:
                if exp.class_name != "StaticMesh" or exp.object_name.lower() not in ours:
                    continue
                source = parse_static_mesh(pkg.names, raw[exp.serial_offset:exp.serial_offset + exp.serial_size])
                tail = source.parsed_tail
                if tail is None:
                    continue
                positions = np.asarray(source.verts, np.float32) * rotation
                raw_uv = np.asarray(source.uvs, dtype=np.uint16).view(np.int16)
                uv = raw_uv.astype(np.float32) / 2048.0
                # Versus copies of these props are rotated 180 degrees around Z.
                # Require the remaster's retained template to match original position+UV pairs;
                # names and bounding boxes alone are insufficient evidence of compatibility.
                signature = _signature(positions, uv)
                for old_pkg, old_exp in ours[exp.object_name.lower()]:
                    asset = old_exp.full_name
                    if asset in meshes:
                        continue
                    old = read_static_mesh(old_pkg, old_exp)
                    old_signature = _signature(old.positions, old.uvs[0]) if old.uvs else set()
                    matched = len(old_signature & signature) / max(1, len(old_signature))
                    if matched < 0.98 or len(tail.sections) != len(old.sections):
                        skipped.append({"asset": asset, "reason": "template or material slots differ", "templateMatch": matched})
                        continue
                    triangles = [np.asarray(section_triangles(source.indices, s), np.uint32).reshape(-1) for s in tail.sections]
                    if not all(t.size for t in triangles):
                        continue
                    used = np.unique(np.concatenate(triangles))
                    if sum(len(t) // 3 for t in triangles) == old.triangle_count and len(used) == len(old.positions):
                        skipped.append({"asset": asset, "reason": "unchanged topology"})
                        continue
                    remap = np.zeros(len(positions), np.uint32)
                    remap[used] = np.arange(len(used), dtype=np.uint32)
                    pos = positions[used]
                    # Exclude replacements whose visual envelope is incompatible with the existing collision.
                    span = np.asarray(old.bbox_max) - old.bbox_min
                    tolerance = np.maximum(span * 0.15, 2.0)
                    if np.any(pos.min(axis=0) < np.asarray(old.bbox_min) - tolerance) or np.any(pos.max(axis=0) > np.asarray(old.bbox_max) + tolerance):
                        skipped.append({"asset": asset, "reason": "replacement exceeds original collision envelope"})
                        continue
                    normals = np.asarray([unpack_vec(source.extras[int(i)], 2) for i in used], np.float32) * rotation
                    normals /= np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-12)
                    arrays = {"positions": pos[:, [0, 2, 1]] * 0.01, "normals": normals[:, [0, 2, 1]], "uv": uv[used],
                              "template_uv": old.uvs[0]}
                    arrays.update({f"indices_{i}": remap[t] for i, t in enumerate(triangles)})
                    filename = asset.replace(".", "_") + ".npz"
                    np.savez_compressed(args.out / filename, **arrays)
                    meshes[asset] = {"file": filename, "sha256": hashlib.sha256((args.out / filename).read_bytes()).hexdigest(),
                                     "sourcePackage": entry.name, "sourceMesh": exp.object_name,
                                     "templateMatch": matched, "vertices": len(used), "triangles": sum(len(t) // 3 for t in triangles),
                                     "originalVertices": len(old.positions), "originalTriangles": old.triangle_count}
    manifest = {"format": "svm.mesh-overrides/1", "source": "https://github.com/dwojtasik/SCCT-Repack",
                "credits": "Remastered_CC.txt", "changes": "Rotated to Versus coordinates; original materials and collision retained; baked light transferred during map conversion.",
                "meshes": meshes, "skipped": skipped}
    (args.out / "enabled.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"accepted": len(meshes), "skipped": len(skipped), "triangles": sum(m["triangles"] for m in meshes.values()),
                      "manifest": str(args.out / "enabled.json")}, indent=2))


if __name__ == "__main__":
    main()
