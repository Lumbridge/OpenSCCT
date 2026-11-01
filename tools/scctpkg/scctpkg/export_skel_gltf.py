"""Export SCCT skeletal meshes to glTF 2.0 with skins (joints + weights).

    python -m scctpkg.export_skel_gltf "<game dir>" out/_chars

Writes out/_chars/<Package>_<Mesh>.gltf (+ .bin) with a skeleton node tree,
inverse bind matrices, JOINTS_0/WEIGHTS_0 and one primitive per material.
Textures: the meshes reference their thermal textures by default, so a skin
map picks the normal-vision textures from SPersoTextures.utx.

Coordinate handling matches export_gltf: UE (x, y, z) -> glTF (x, z, y)/100,
original winding (Interchange reverses it for the reflection).
"""

from __future__ import annotations

import json
import struct
import sys
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import numpy as np

from .export_gltf import GltfWriter, UE_TO_GLTF_SCALE, _to_gltf, _dir_to_gltf, _safe
from .materials import MaterialResolver
from .package import Package
from .packageset import PackageSet
from .skeletalmesh import SkeletalMesh, read_skeletal_mesh
from .meshanim import MeshAnimation, read_mesh_animation
from .texture import read_texture

# Normal-vision skin textures per mesh material slot (the mesh defaults are thermal maps).
SKIN_MAP: Dict[str, List[str]] = {
    "DEF_01": ["SPersoTextures.DEF_01_A_Body", "SPersoTextures.DEF_01_A_Face", "SPersoTextures.DEF_01_A_Body"],
    "ATT_01": ["SPersoTextures.Shadw_agent", "SPersoTextures.Shadw_agent_2"],
    "DEF_01_PS2": ["SPersoTextures.DEF_01_A_Body", "SPersoTextures.DEF_01_A_Face", "SPersoTextures.DEF_01_A_Body"],
    "ATT_01_PS2": ["SPersoTextures.Shadw_agent", "SPersoTextures.Shadw_agent_2"],
}


# Extra layers of the normal-vision pawn shaders (SPersoTextures): self-illumination through a mask (the spy's goggle
# lenses and suit lights, the merc's helmet lamp and visor) and the specularity mask of their wet (rain) variants.
# Per mesh, per material slot: (SelfIllumination, SelfIlluminationMask, SpecularityMask), texture keys or None.
LAYER_MAP: Dict[str, List[Tuple[Optional[str], Optional[str], Optional[str]]]] = {
    "ATT_01": [("SPersoTextures.Shadw_agent", "SPersoTextures.Shadw_agent_SI", "SPersoTextures.ATT_spec_mask"),
               (None, None, "SPersoTextures.ATT_2_wet")],
    "DEF_01": [("SPersoTextures.DEF_01_A_Body", "SPersoTextures.DEF_01_Body_SI", "SPersoTextures.DEF_01_A_Body_envmap"),
               ("SPersoTextures.DEF_01_A_Face", "SPersoTextures.DEF_01_A_Face_SI", None),
               ("SPersoTextures.DEF_01_A_Body", "SPersoTextures.DEF_01_Body_SI", "SPersoTextures.DEF_01_A_Body_envmap")],
}
# Merc face: the Diffuse is MaterialSequence def_seq_cligne_oeil = DEF_01_A_Face 2.5 s, DEF_01_A_Face_2 (eyes shut)
# 0.15 s, DEF_01_A_Face 5 s, DEF_01_A_Face_2 0.1 s (7.75 s loop). Per mesh, per slot: the eyes-shut frame.
BLINK_MAP: Dict[str, Dict[int, str]] = {"DEF_01": {1: "SPersoTextures.DEF_01_A_Face_2"}}
BLINK_MAP["DEF_01_PS2"] = BLINK_MAP["DEF_01"]
# Runtime skin swaps (SBase script sets the pawn's Skins): a phosphorus-tagged pawn wears SI_*_phospho (self-illumination
# phospho_pawn_1 through its own alpha) and a pawn hit by sticky residue wears Stickess_* (self-illumination pan_sticky:
# TexPanner over shadwagent_sticky_1, PanDirection yaw 16384 = +V, PanRate 0.5). The client overlays these glows.
OVERLAY_TEXTURES = {"phospho": "SPersoTextures.phospho_pawn_1", "sticky": "SPersoTextures.shadwagent_sticky_1"}
LAYER_MAP["ATT_01_PS2"] = LAYER_MAP["ATT_01"]
LAYER_MAP["DEF_01_PS2"] = LAYER_MAP["DEF_01"]


def _layer_png(pset: PackageSet, resolver: MaterialResolver, key: Optional[str], tex_dir: Path) -> Optional[str]:
    if not key:
        return None
    pkg_name, tex_name = key.split(".", 1)
    owner = pset.get(pkg_name)
    te = owner.find_export(tex_name, "Texture") if owner is not None else None
    tex = resolver.texture_for(owner, te) if te is not None else None
    if tex is None or tex.image is None:
        return None
    tex_dir.mkdir(parents=True, exist_ok=True)
    fname = _safe(key) + ".png"
    if not (tex_dir / fname).exists():
        tex.image.save(tex_dir / fname)
    return f"textures/{fname}"


def _quat_ue_to_gltf(q: Tuple[float, float, float, float]) -> List[float]:
    """UE2 bone quaternion (x, y, z, w) in UE axes -> glTF axes (x, z, y) with the reflection."""
    x, y, z, w = q
    # Reflection swapping y/z inverts handedness: negate w to keep a proper rotation.
    return [x, z, y, -w]


def _key_quat(q, is_root: bool) -> List[float]:
    """Animation key quaternion -> glTF (x, z, y, -w), conjugated for non-root bones like the bind pose."""
    x, y, z, w = _quat_ue_to_gltf(q)
    n = (x * x + y * y + z * z + w * w) ** 0.5 or 1.0
    x, y, z, w = x / n, y / n, z / n, w / n
    if not is_root:
        x, y, z = -x, -y, -z
    return [float(x), float(y), float(z), float(w)]


def _add_animations(writer: GltfWriter, mesh: SkeletalMesh, anim: MeshAnimation, node_base: int) -> List[dict]:
    """One glTF animation per sequence; channels for every animated bone found by name."""
    bone_index = {b.name.lower(): i for i, b in enumerate(mesh.bones)}
    animations = []
    for seq, move in zip(anim.seqs, anim.moves):
        if not seq.name or seq.num_frames <= 0:
            continue
        rate = seq.rate if seq.rate > 0 else 15.0
        samplers, channels = [], []
        for ti, track in enumerate(move.tracks):
            if ti >= len(move.bone_indices):
                break
            anim_bone = anim.bone_names[move.bone_indices[ti]] if move.bone_indices[ti] < len(anim.bone_names) else None
            if anim_bone is None:
                continue
            bi = bone_index.get(anim_bone.lower())
            if bi is None:
                continue
            is_root = bi == 0
            nq = len(track.quats)
            if nq:
                times = (track.times[:nq].astype(np.float32) if len(track.times) >= nq else np.arange(nq, dtype=np.float32)) / rate
                rot = np.array([_key_quat(q, is_root) for q in track.quats], dtype=np.float32)
                t_acc = writer.add_vec_accessor(times.reshape(-1, 1), 1, "SCALAR", target=None, minmax=True)
                r_acc = writer.add_vec_accessor(rot, 4, "VEC4", target=None)
                samplers.append({"input": t_acc, "output": r_acc, "interpolation": "LINEAR"})
                channels.append({"sampler": len(samplers) - 1, "target": {"node": node_base + bi, "path": "rotation"}})
            npos = len(track.positions)
            if npos:
                times = (track.times[:npos].astype(np.float32) if len(track.times) >= npos else np.arange(npos, dtype=np.float32)) / rate
                pos = np.stack([track.positions[:, 0], track.positions[:, 2], track.positions[:, 1]], axis=1) * UE_TO_GLTF_SCALE
                t_acc = writer.add_vec_accessor(times.reshape(-1, 1), 1, "SCALAR", target=None, minmax=True)
                p_acc = writer.add_vec_accessor(pos.astype(np.float32), 3, "VEC3", target=None)
                samplers.append({"input": t_acc, "output": p_acc, "interpolation": "LINEAR"})
                channels.append({"sampler": len(samplers) - 1, "target": {"node": node_base + bi, "path": "translation"}})
        if channels:
            animations.append({"name": seq.name, "samplers": samplers, "channels": channels})
    return animations


def _bone_local_matrices(mesh: SkeletalMesh) -> List[np.ndarray]:
    """4x4 local transforms (glTF space) per bone."""
    mats = []
    for i, b in enumerate(mesh.bones):
        q = np.array(_quat_ue_to_gltf(b.rotation), dtype=np.float64)
        x, y, z, w = q
        n = np.linalg.norm(q)
        if n > 0:
            x, y, z, w = q / n
        rot = np.array([
            [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
        ])
        # UE2 stores the parent-relative rotation as the inverse for non-root bones.
        if i != 0:
            rot = rot.T
        pos = np.array([b.position[0], b.position[2], b.position[1]]) * UE_TO_GLTF_SCALE
        m = np.eye(4)
        m[:3, :3] = rot
        m[:3, 3] = pos
        mats.append(m)
    return mats


def _global_matrices(mesh: SkeletalMesh, local: List[np.ndarray]) -> List[np.ndarray]:
    out: List[Optional[np.ndarray]] = [None] * len(mesh.bones)
    for i, b in enumerate(mesh.bones):
        if i == 0 or b.parent == i:
            out[i] = local[i]
        else:
            out[i] = out[b.parent] @ local[i]
    return out  # type: ignore


def _normal_vision_texture(pset: PackageSet, resolver: MaterialResolver, key: str):
    """Skeletal meshes default to their thermal maps (the map's actors override them with Skins at runtime):
    swap 'X_heat' / 'heat_X' for the same package's normal-vision 'X' when it exists."""
    if "heat" not in key.lower() or "." not in key:
        return None, None
    pkg_name, tex_name = key.split(".", 1)
    low = tex_name.lower()
    cands = []
    for suf in ("_heat_cold", "_heat", "_hot"):
        if low.endswith(suf):
            cands.append(low[: -len(suf)])
    if low.startswith("heat_"):
        cands.append(low[5:])
    owner = pset.get(pkg_name)
    if owner is None:
        return None, None
    by_name = {e.name.lower(): e for e in owner.exports if e.class_name == "Texture"}
    for c in cands:
        e = by_name.get(c)
        if e is not None:
            tex = resolver.texture_for(owner, e)
            if tex is not None and tex.image is not None:
                return tex, f"{pkg_name}.{e.name}"
    return None, None


def export_skeletal(pkg: Package, mesh: SkeletalMesh, pset: PackageSet, out_dir: Path, tex_dir: Path) -> Path:
    writer = GltfWriter()
    resolver = MaterialResolver(pset)
    tex_uri: Dict[str, Optional[str]] = {}

    # --- materials
    n_slots = max(int(mesh.face_material.max()) + 1 if len(mesh.face_material) else 1, len(mesh.material_refs))
    skins = SKIN_MAP.get(mesh.name, [])
    mat_indices = []
    for slot in range(n_slots):
        key = None
        tex = None
        if slot < len(skins):
            pkg_name, tex_name = skins[slot].split(".")
            owner = pset.get(pkg_name)
            if owner is not None:
                te = owner.find_export(tex_name, "Texture")
                if te is not None:
                    tex = resolver.texture_for(owner, te)
                    key = skins[slot]
        res = None
        if tex is None and slot < len(mesh.material_refs) and mesh.material_refs[slot]:
            res = resolver.resolve(pkg, mesh.material_refs[slot])
            tex = res.texture
            key = res.key
            ntex, nkey = _normal_vision_texture(pset, resolver, key or "")
            if ntex is not None:
                tex, key = ntex, nkey
        uri = None
        if tex is not None and tex.image is not None and key:
            tex_dir.mkdir(parents=True, exist_ok=True)
            fname = _safe(key) + ".png"
            if not (tex_dir / fname).exists():
                tex.image.save(tex_dir / fname)
            uri = f"textures/{fname}"
        # Keep masked / two-sided / unlit from the resolved material (tree leaves, grilles, lamps).
        mat_indices.append(writer.material(key or f"{mesh.name}.slot{slot}", res, uri))
        layers = LAYER_MAP.get(mesh.name, [])
        if slot < len(layers):
            si, mask, spec = (_layer_png(pset, resolver, k, tex_dir) for k in layers[slot])
            extras = {k: v for k, v in (("scct_self_illum", si), ("scct_self_illum_mask", mask), ("scct_spec_mask", spec)) if v}
            blink = _layer_png(pset, resolver, BLINK_MAP.get(mesh.name, {}).get(slot), tex_dir)
            if blink:
                extras["scct_blink"] = blink
            if extras:
                m = writer.materials[mat_indices[-1]]
                m["extras"] = dict(m.get("extras", {}), **extras, scct_slot=slot)

    # --- geometry: one vertex per wedge
    nw = len(mesh.wedges)
    pos = mesh.points[mesh.wedges[:, 0].astype(np.int32)]
    uv = mesh.wedges[:, 1:3].astype(np.float32)
    # normals: per-face accumulation. UE2's front faces wind the other way from the right-hand rule: the packages' own
    # static mesh normals are opposite to (p1 - p0) x (p2 - p0) on every face (SGameplayObjects checked), so that cross
    # product pointed inward and the UE2 pawn lighting lit the side facing away from each light (OrphD: spy lit from
    # the front where the game lights its back).
    tri = mesh.faces
    if mesh.normals is not None:
        normals = mesh.normals.copy()
    else:
        normals = np.zeros((nw, 3), np.float32)
        p0, p1, p2 = pos[tri[:, 0]], pos[tri[:, 1]], pos[tri[:, 2]]
        fn = np.cross(p2 - p0, p1 - p0)
        for k in range(3):
            np.add.at(normals, tri[:, k], fn)
    lens = np.linalg.norm(normals, axis=1, keepdims=True)
    lens[lens == 0] = 1
    normals /= lens

    # --- skin weights per point -> per wedge (up to 4)
    npts = len(mesh.points)
    joints = np.zeros((npts, 4), np.uint16)
    weights = np.zeros((npts, 4), np.float32)
    fill = np.zeros(npts, np.int32)
    order = np.argsort(-mesh.influences[:, 0]) if len(mesh.influences) else []
    for idx in order:
        w, p, b = mesh.influences[idx]
        p = int(p)
        b = int(b)
        if fill[p] < 4:
            joints[p, fill[p]] = b
            weights[p, fill[p]] = w
            fill[p] += 1
    wsum = weights.sum(axis=1, keepdims=True)
    wsum[wsum == 0] = 1
    weights /= wsum
    wjoints = joints[mesh.wedges[:, 0].astype(np.int32)]
    wweights = weights[mesh.wedges[:, 0].astype(np.int32)]

    # --- skeleton nodes
    local = _bone_local_matrices(mesh)
    glob = _global_matrices(mesh, local)
    node_base = len(writer.nodes)
    for i, b in enumerate(mesh.bones):
        m = local[i]
        # decompose: translation + rotation (no scale)
        t = m[:3, 3].tolist()
        R = m[:3, :3]
        tr = np.trace(R)
        if tr > 0:
            s = np.sqrt(tr + 1.0) * 2
            qw = 0.25 * s
            qx = (R[2, 1] - R[1, 2]) / s
            qy = (R[0, 2] - R[2, 0]) / s
            qz = (R[1, 0] - R[0, 1]) / s
        else:
            i_max = int(np.argmax([R[0, 0], R[1, 1], R[2, 2]]))
            if i_max == 0:
                s = np.sqrt(1.0 + R[0, 0] - R[1, 1] - R[2, 2]) * 2
                qw = (R[2, 1] - R[1, 2]) / s
                qx = 0.25 * s
                qy = (R[0, 1] + R[1, 0]) / s
                qz = (R[0, 2] + R[2, 0]) / s
            elif i_max == 1:
                s = np.sqrt(1.0 + R[1, 1] - R[0, 0] - R[2, 2]) * 2
                qw = (R[0, 2] - R[2, 0]) / s
                qx = (R[0, 1] + R[1, 0]) / s
                qy = 0.25 * s
                qz = (R[1, 2] + R[2, 1]) / s
            else:
                s = np.sqrt(1.0 + R[2, 2] - R[0, 0] - R[1, 1]) * 2
                qw = (R[1, 0] - R[0, 1]) / s
                qx = (R[0, 2] + R[2, 0]) / s
                qy = (R[1, 2] + R[2, 1]) / s
                qz = 0.25 * s
        node = {"name": b.name, "translation": t, "rotation": [float(qx), float(qy), float(qz), float(qw)]}
        writer.nodes.append(node)
    for i, b in enumerate(mesh.bones):
        if i != 0 and b.parent != i:
            writer.nodes[node_base + b.parent].setdefault("children", []).append(node_base + i)

    # --- inverse bind matrices (column-major)
    ibm = np.zeros((len(mesh.bones), 16), np.float32)
    for i in range(len(mesh.bones)):
        inv = np.linalg.inv(glob[i])
        ibm[i] = inv.T.reshape(16)
    ibm_acc = writer.add_vec_accessor(ibm, 16, "MAT4", target=None)
    skin_index = 0
    writer_skins = [{"joints": [node_base + i for i in range(len(mesh.bones))], "inverseBindMatrices": ibm_acc, "skeleton": node_base}]

    # --- primitives per material
    prims = []
    for slot in range(n_slots):
        sel = np.nonzero(mesh.face_material == slot)[0]
        if len(sel) == 0:
            continue
        idx = tri[sel].reshape(-1).astype(np.uint32)
        prims.append((idx, mat_indices[slot]))
    attrs_pos = writer.add_vec_accessor(_to_gltf(pos), 3, "VEC3", minmax=True)
    attrs_nrm = writer.add_vec_accessor(_dir_to_gltf(normals), 3, "VEC3")
    attrs_uv = writer.add_vec_accessor(uv, 2, "VEC2")
    # joints as unsigned short, weights float
    jview = writer._add_view(np.ascontiguousarray(wjoints, dtype=np.uint16).tobytes(), 34962)
    writer.accessors.append({"bufferView": jview, "componentType": 5123, "count": int(nw), "type": "VEC4"})
    attrs_j = len(writer.accessors) - 1
    attrs_w = writer.add_vec_accessor(wweights, 4, "VEC4")
    gprims = []
    for idx, mat in prims:
        gprims.append({"attributes": {"POSITION": attrs_pos, "NORMAL": attrs_nrm, "TEXCOORD_0": attrs_uv,
                                      "JOINTS_0": attrs_j, "WEIGHTS_0": attrs_w},
                       "indices": writer.add_index_accessor(idx), "material": mat, "mode": 4})
    writer.meshes.append({"name": mesh.name, "primitives": gprims})
    mesh_node = {"name": mesh.name, "mesh": len(writer.meshes) - 1, "skin": skin_index}
    writer.nodes.append(mesh_node)

    # --- animations from the mesh's default MeshAnimation (same package)
    animations = []
    if mesh.animation_ref > 0:
        ae = pkg.exports[mesh.animation_ref - 1]
        try:
            anim = read_mesh_animation(pkg, ae)
            animations = _add_animations(writer, mesh, anim, node_base)
        except Exception as ex:
            print(f"  animation {ae.name}: {ex}")

    out_dir.mkdir(parents=True, exist_ok=True)
    path = out_dir / f"{_safe(pkg.path.stem + '_' + mesh.name)}.gltf"
    # write with skins and only root nodes in the scene
    bin_path = path.with_suffix(".bin")
    bin_path.write_bytes(bytes(writer.bin))
    doc = {
        "asset": {"version": "2.0", "generator": "scctpkg-skel"},
        "scene": 0,
        "scenes": [{"nodes": [node_base, len(writer.nodes) - 1]}],
        "nodes": writer.nodes,
        "meshes": writer.meshes,
        "skins": writer_skins,
        "materials": writer.materials,
        "accessors": writer.accessors,
        "bufferViews": writer.buffer_views,
        "buffers": [{"uri": bin_path.name, "byteLength": len(writer.bin)}],
        "samplers": writer.samplers,
    }
    if mesh.name in LAYER_MAP:
        overlays = {k: _layer_png(pset, resolver, v, tex_dir) for k, v in OVERLAY_TEXTURES.items()}
        doc["asset"]["extras"] = {"scct_overlays": {k: v for k, v in overlays.items() if v}}
    if writer.images:
        doc["images"] = writer.images
        doc["textures"] = writer.textures
    if animations:
        doc["animations"] = animations
    path.write_text(json.dumps(doc), encoding="utf-8")
    return path


def main(argv=None) -> int:
    argv = argv if argv is not None else sys.argv[1:]
    if len(argv) < 2:
        print(__doc__)
        return 2
    game = Path(argv[0])
    out_dir = Path(argv[1])
    only = set(argv[2].split(",")) if len(argv) > 2 else None
    pset = PackageSet([game / "Packages"])
    done = 0
    for f in sorted((game / "Packages" / "Animations").glob("*.ukx")):
        pkg = Package.load(f)
        for e in pkg.exports_of_class("SkeletalMesh"):
            if e.size <= 0 or (only and e.name not in only):
                continue
            try:
                mesh = read_skeletal_mesh(pkg, e)
                path = export_skeletal(pkg, mesh, pset, out_dir, out_dir / "textures")
                import json as _json
                n_anim = len(_json.load(open(path, encoding="utf-8")).get("animations", []))
                print(f"{path.name}: {len(mesh.bones)} bones, {len(mesh.wedges)} verts, {len(mesh.faces)} tris, {n_anim} animations")
                done += 1
            except Exception as ex:
                print(f"{f.name}.{e.name}: {ex}")
    print(f"exported {done} skeletal meshes to {out_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
