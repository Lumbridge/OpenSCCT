"""One original map to a complete SVMap 1.1 folder."""

from __future__ import annotations

import time
from pathlib import Path
from typing import Optional

from .. import canonical
from ..svmap import LATEST_VERSION, write_svmap
from ..writers.collision import KINDS, read_collision
from .collision import CollisionBuilder
from .gameplay import build_class_table, build_gameplay
from .rain import build_rain
from .render import RenderBuilder
from .source import MapSource
from .traversal import build_traversal

GENERATOR = "svmbuild.maps"
GENERATOR_VERSION = 1


def build_map(game: Path, name: str, out_dir: Path, db, *, map_sha256: str, classes_pack: Optional[str] = None,
              render: bool = True, hasher=None, ctx=None) -> dict:
    """Write out_dir (the SVMap folder of `name`) and return the build stats (with 'discovered' for the cache)."""
    timings = {}
    t0 = time.perf_counter()
    src = MapSource(Path(game), name, db)
    timings["open"] = time.perf_counter() - t0

    t = time.perf_counter()
    gameplay, ui, gstats = build_gameplay(src)
    class_table = build_class_table(src)
    timings["gameplay"] = time.perf_counter() - t

    t = time.perf_counter()
    cb = CollisionBuilder(src)
    collision = cb.build()
    timings["collision"] = time.perf_counter() - t

    t = time.perf_counter()
    traversal, tstats = build_traversal(src, gameplay["levelInfo"]["props"]["GEActivationMask"])
    timings["traversal"] = time.perf_counter() - t

    render_files = {}
    rstats = {}
    if render:
        t = time.perf_counter()
        zone_index = {}
        for i, z in enumerate(cb.d.zones):
            nm = cb.d.name_of(z.actor)
            if nm:
                zone_index[nm] = i
        rb = RenderBuilder(src, Path(out_dir).parent / ".work" / name, {a["id"] for a in gameplay["actors"]} |
                           {gameplay["levelInfo"]["id"]})
        render_files = rb.build(zone_index, ui)
        rstats = rb.stats
        timings["render"] = time.perf_counter() - t

    title = str(src.level_info.props.get("Title") or ui.get("MapName") or name)
    source = {"kind": "converted", "generator": GENERATOR, "generatorVersion": GENERATOR_VERSION,
              "original": {"file": f"Packages/Maps/{src.path.name}", "sha256": map_sha256}}
    requires = [{"pack": "classes", "sha256": classes_pack}] if classes_pack else []
    files = {"sim/classes.json": canonical.dump_file(class_table)}
    rain = build_rain(src)
    if rain["volumes"]:
        files["sim/rain.json"] = canonical.dump_file(rain)
    if ui:
        files["ui/mapinfo.json"] = canonical.dump_file(ui, ascii_keys=False)
    t = time.perf_counter()
    manifest = write_svmap(out_dir, map_id=name, title=title, source=source, gameplay=gameplay, traversal=traversal,
                           collision=collision, render=render_files, requires=requires, files=files,
                           format_version=LATEST_VERSION)
    timings["write"] = time.perf_counter() - t
    timings["total"] = time.perf_counter() - t0

    d = read_collision(collision)
    by_kind: dict = {}
    for c in d.colliders:
        by_kind[KINDS[c.kind]] = by_kind.get(KINDS[c.kind], 0) + 1
    sizes = {}
    for rel, meta in manifest["files"].items():
        group = rel.split("/", 1)[0]
        sizes[group] = sizes.get(group, 0) + meta["size"]
    stats = {
        "map": name,
        "title": title,
        "actors": gstats,
        "collision": {"bytes": len(collision), "colliders": len(d.colliders), "byKind": by_kind,
                      "triangles": len(d.tris), "vertices": len(d.verts), "bspNodes": len(d.bsp),
                      "hulls": len(d.hulls), "cylinders": len(d.cylinders), "zones": len(d.zones),
                      "materials": len(d.materials), "detail": cb.stats},
        "traversal": tstats,
        "render": rstats,
        "sizes": sizes,
        "files": len(manifest["files"]),
        "simHash": manifest["hashes"]["sim"],
        "notes": dict(sorted(src.notes.counts.items())),
        "noteSamples": src.notes.items[:40],
        "timings": {k: round(v, 3) for k, v in timings.items()},
    }
    if ctx is not None:
        stats["discovered"] = src.discovered(ctx)
    return stats
