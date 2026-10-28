"""The skeletal meshes a map places (SAnimatedMesh props, DT_Mesh fish), written into the map's own render/chars/.

The shared chars step exports the map props of the anim packages the converting install has. A map played from a
downloaded pack meets a player's shared content built from another install, often the retail one, which lacks
the downloaded maps' anim packages (map17_anm for Polar Base, sqt13_anm for Steel Squall): without a copy of its own
the map's dish, wind sock and crane were not drawn. Every converted map therefore carries the skeletal meshes its
actors name, laid out as shared/chars (render/chars/<Package>/<Mesh>.glb, <Animation>.anim.json, textures/), and the
client looks there before the shared folder.
"""

from __future__ import annotations

import shutil
from pathlib import Path
from typing import Dict, List, Tuple

from .source import MapSource

DT_MESH = 2

# The character packages every install has and the shared chars always carry (spy, merc, weapons, goggle glow): a
# map placing one of their meshes (a weapon on a table, a posed agent) uses the shared copy.
CORE_PACKAGES = frozenset(("sperso", "sweaponfirst", "sweapon", "sgoggle"))


def placed_skeletal_meshes(src: MapSource) -> Dict[str, Tuple[object, List[str]]]:
    """{package name: (package, [mesh names])} of the skeletal meshes the map's gameplay actors draw (DrawType
    DT_Mesh and a Mesh that is a SkeletalMesh of a package without groups), the core character packages left out."""
    out: Dict[str, Tuple[object, List[str]]] = {}
    for a in src.of("gameplay"):
        mesh = src.value(a, "Mesh")
        if mesh is None or not getattr(mesh, "ref", 0) or src.value(a, "DrawType", 0) != DT_MESH:
            continue
        pkg, e = src.ref_export(mesh)
        if pkg is None or e is None or e.class_name != "SkeletalMesh":
            continue
        path = getattr(mesh, "path", "") or ""
        top, _, rest = path.partition(".")
        if not top or rest.lower() != e.name.lower():
            src.notes.add("skeletal-prop-path", f"{a.name}: {path}")
            continue
        if pkg.path.stem.lower() in CORE_PACKAGES:
            continue
        entry = out.setdefault(pkg.path.stem, (pkg, []))
        if e.name not in entry[1]:
            entry[1].append(e.name)
    return out


def build_skeletal_props(src: MapSource, db, work: Path, stats: dict) -> Dict[str, bytes]:
    """render/-relative files of the placed skeletal meshes: chars/<Package>/..."""
    from ..charconv import export_package  # noqa: PLC0415

    placed = placed_skeletal_meshes(src)
    files: Dict[str, bytes] = {}
    if not placed:
        return files
    work = Path(work)
    out = work / "chars"
    if work.exists():
        shutil.rmtree(work)
    meshes = 0
    failed: List[str] = []
    for name in sorted(placed, key=str.lower):
        pkg, wanted = placed[name]
        st = export_package(pkg, src.pset, sorted(wanted), out / name, db, src.notes, work / "tmp", name)
        meshes += len(st["meshes"])
        failed += st["failed"]
    for p in sorted(out.rglob("*")):
        if p.is_file():
            files["chars/" + p.relative_to(out).as_posix()] = p.read_bytes()
    shutil.rmtree(work, ignore_errors=True)
    stats["skeletalProps"] = {"packages": sorted(placed, key=str.lower), "meshes": meshes, "failed": failed}
    for f in failed:
        src.notes.add("skeletal-prop-failed", f)
    return files
