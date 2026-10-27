"""Skeletal meshes and their animation sets: GLB skins plus one animation file per MeshAnimation.

The GLB is scctpkg's skeletal glTF export (scctpkg/export_skel_gltf.py: skeleton, inverse bind matrices, joints and
weights, one primitive per material, a glTF animation per sequence of the mesh's default animation set), packed as
one binary file; its textures sit next to it as PNG.

The animation file (<Package>/<Animation>.anim.json, svm.anim/1) keeps what the simulation needs in UE2 terms:

    {"format": "svm.anim/1", "animation": "SPerso.<Anim>", "bones": [...],
     "sequences": {"<name>": {"startFrame", "frames", "rate", "groups", "lead",
                              "length": seconds a one-shot plays ((frames - 1) / rate),
                              "notifies": [{"time" (0..1), "seconds", "function", "object", "class", "props"}],
                              "rootTrack": {"rot", "pos", "time"}      the chunk's RootTrack (often empty),
                              "rootBone": {"bone", "rot", "pos", "time"} the track of the chunk's first bone}}}

Positions are uu, quaternions (x, y, z, w) as stored, times in frames.
"""

from __future__ import annotations

import json
import shutil
from pathlib import Path
from typing import Dict

import numpy as np

from . import canonical
from .sources import animations as anim_reader
from .sources.env import import_formats
from .sources.typedprops import bag_to_json, default_resolver
from .writers.glb import pack_glb

# (package under Packages/Animations, mesh names; empty = every mesh except the PS2 variants)
DEFAULT_SETS = (
    ("SPerso", ("ATT_01", "DEF_01")),          # spy, merc
    ("SWeaponFirst", ()),                      # first-person weapons
    ("sqt13_anm", ("sqt_grue_mesh", "sqt_mur_mesh")),   # SteeE crane and collapsing wall
    ("SGoggle", ("ATT01_beams", "DEF01_beams")),        # goggle glow (spec/equipment/goggle-glow.md)
)

# Sets of one map's props: skipped when the install does not have the package (the retail Versus has no SteeE).
OPTIONAL_SETS = frozenset({"sqt13_anm"})


def set_path(game: Path, package: str) -> Path:
    return Path(game) / "Packages" / "Animations" / f"{package}.ukx"

# Materials a class swaps into a mesh's Skins at run time, exported next to that package's meshes (textures/, named
# as the mesh materials are): the merc rifle's rate-of-fire screen (SWeaponDefense Skins[3], SetMode swaps
# SI_rof_single, SI_rof_burst and SI_rof_auto; spec/equipment/merc-rifle.md rule 16).
SKIN_SWAPS = {
    "SWeaponFirst": ("SWeaponTex.SI_rof_single", "SWeaponTex.SI_rof_burst", "SWeaponTex.SI_rof_auto"),
    # SGoggleBeam.ChangeSkin's skins (spec/equipment/goggle-glow.md rule 5).
    "SGoggle": ("SGoggleTextures.TRA_goggle_green", "SGoggleTextures.TRD_goggle_orange",
                "SGoggleTextures.TRD_goggle_motion", "SGoggleTextures.TRD_goggle_EMF"),
}


def export_skin_swaps(pset, keys, tex_dir: Path) -> list:
    """Writes the texture each material key draws (a Shader's resolved texture) as textures/<key>.png."""
    from scctpkg.export_gltf import _safe  # noqa: PLC0415
    from scctpkg.materials import MaterialResolver  # noqa: PLC0415

    resolver = MaterialResolver(pset)
    written = []
    for key in keys:
        pkg_name, name = key.split(".", 1)
        owner = pset.get(pkg_name)
        e = None
        if owner is not None:
            e = owner.find_export(name, "Shader") or owner.find_export(name, "Texture")
        if e is None:
            raise ValueError(f"skin swap material not found: {key}")
        res = resolver.resolve(owner, e.index + 1)
        if res.texture is None or res.texture.image is None:
            raise ValueError(f"skin swap material has no texture: {key}")
        tex_dir.mkdir(parents=True, exist_ok=True)
        res.texture.image.save(tex_dir / (_safe(key) + ".png"))
        written.append(key)
    return written


def _notify(db, pkg, ref: int, notes) -> dict:
    cd = db.cd
    from scctpkg.properties import skip_state_frame  # noqa: PLC0415

    if ref == 0:
        return {}
    if ref < 0:
        return {"object": cd.object_path(pkg, ref)}
    e = pkg.exports[ref - 1]
    path = cd.export_path(pkg, e)
    cls = db.find(f"{e.class_package}.{e.class_name}") or f"{e.class_package}.{e.class_name}"
    rec = db.record(cls)
    props = {}
    if rec is not None and e.size > 0:
        r = pkg.export_reader(e)
        skip_state_frame(r, e.flags)
        props = bag_to_json(db.decoder.tagged(r, pkg, rec["_field"]), default_resolver, notes, path)
    return {"object": path, "class": cls, "props": props}


def animation_document(db, pkg, export, notes) -> dict:
    a = anim_reader.read_animation(pkg, export)
    seqs = {}
    for i, s in enumerate(a.sequences):
        notifies = []
        for n in s.notifies:
            entry = {"time": float(n.time), "seconds": anim_reader.notify_seconds(n.time, s.num_frames, s.rate),
                     "function": n.function}
            entry.update(_notify(db, pkg, n.notify_ref, notes))
            notifies.append(entry)
        seq = {"startFrame": int(s.start_frame), "frames": int(s.num_frames), "rate": float(s.rate),
               "groups": list(s.groups), "lead": float(s.lead),
               "length": float(np.float32(np.float32(max(s.num_frames - 1, 0)) / np.float32(s.rate))) if s.rate > 0
               else 0.0, "notifies": notifies}
        if i < len(a.chunks):
            ch = a.chunks[i]
            seq["rootTrack"] = anim_reader.track_json(ch.root_track)
            if ch.tracks:
                k = ch.bone_indices.index(0) if 0 in ch.bone_indices else 0
                bone = a.bones[ch.bone_indices[k]] if k < len(ch.bone_indices) and ch.bone_indices[k] < len(a.bones) \
                    else None
                rb = anim_reader.track_json(ch.tracks[k])
                rb["bone"] = bone
                seq["rootBone"] = rb
            seq["chunkFrames"] = float(ch.track_time)
        seqs[s.name] = seq
    return {"format": "svm.anim/1", "animation": db.cd.export_path(pkg, export), "bones": list(a.bones),
            "sequences": seqs}


def export_set(game: Path, package: str, meshes, out_dir: Path, db, notes, work: Path) -> dict:
    import_formats()
    from scctpkg.export_skel_gltf import export_skeletal  # noqa: PLC0415
    from scctpkg.package import Package  # noqa: PLC0415
    from scctpkg.packageset import PackageSet  # noqa: PLC0415
    from scctpkg.skeletalmesh import read_skeletal_mesh  # noqa: PLC0415

    path = Path(game) / "Packages" / "Animations" / f"{package}.ukx"
    pkg = Package.load(path)
    pset = PackageSet([Path(game) / "Packages"])
    pset.register(pkg)
    wanted = [e for e in pkg.exports_of_class("SkeletalMesh") if e.size > 0 and
              ((e.name in meshes) if meshes else not e.name.upper().endswith("_PS2"))]
    dest = out_dir / package
    dest.mkdir(parents=True, exist_ok=True)
    stats: Dict[str, object] = {"meshes": [], "failed": [], "animations": []}
    anims_used = set()
    for e in wanted:
        try:
            mesh = read_skeletal_mesh(pkg, e)
            wdir = work / package
            if wdir.exists():
                shutil.rmtree(wdir)
            gpath = export_skeletal(pkg, mesh, pset, wdir, wdir / "textures")
            gltf = json.loads(gpath.read_text(encoding="utf-8"))
            binary = gpath.with_suffix(".bin").read_bytes()
            gltf["asset"]["generator"] = "svmbuild (scctpkg export_skel_gltf)"
            anim_name = None
            if mesh.animation_ref > 0:
                ae = pkg.exports[mesh.animation_ref - 1]
                if ae.class_name == "MeshAnimation":
                    anim_name = ae.name
                    anims_used.add(ae.index)
            gltf.setdefault("extras", {})["svmap"] = {
                "mesh": db.cd.export_path(pkg, e), "bones": len(mesh.bones),
                "animation": f"{anim_name}.anim.json" if anim_name else None,
                "meshScale": list(mesh.mesh_scale), "meshOrigin": list(mesh.mesh_origin),
                "rotOrigin": list(mesh.rot_origin)}
            (dest / f"{e.name}.glb").write_bytes(pack_glb(gltf, binary))
            tdir = wdir / "textures"
            if tdir.is_dir():
                (dest / "textures").mkdir(exist_ok=True)
                for t in sorted(tdir.iterdir()):
                    shutil.copyfile(t, dest / "textures" / t.name)
            stats["meshes"].append({"mesh": e.name, "bones": len(mesh.bones), "vertices": int(len(mesh.wedges)),
                                    "triangles": int(len(mesh.faces)),
                                    "glAnimations": len(gltf.get("animations", []))})
        except Exception as ex:  # noqa: BLE001 - one mesh must not stop the set
            stats["failed"].append(f"{e.name}: {type(ex).__name__}: {ex}")
    swaps = SKIN_SWAPS.get(package, ())
    if swaps:
        stats["skinSwaps"] = export_skin_swaps(pset, swaps, dest / "textures")
    for idx in sorted(anims_used):
        ae = pkg.exports[idx]
        try:
            doc = animation_document(db, pkg, ae, notes)
            (dest / f"{ae.name}.anim.json").write_bytes(canonical.dump_file(doc))
            n_notifies = sum(len(s["notifies"]) for s in doc["sequences"].values())
            stats["animations"].append({"animation": ae.name, "sequences": len(doc["sequences"]),
                                        "notifies": n_notifies})
        except Exception as ex:  # noqa: BLE001
            stats["failed"].append(f"{ae.name}: {type(ex).__name__}: {ex}")
    if work.exists():
        shutil.rmtree(work, ignore_errors=True)
    return stats


def export_all(game: Path, out_dir: Path, db, sets=DEFAULT_SETS) -> dict:
    from .sources.typedprops import Notes  # noqa: PLC0415

    notes = Notes()
    out = {}
    for package, meshes in sets:
        if package in OPTIONAL_SETS and not set_path(game, package).is_file():
            continue
        out[package] = export_set(game, package, meshes, out_dir, db, notes, out_dir / ".work")
    out["_notes"] = dict(notes.counts)
    return out
