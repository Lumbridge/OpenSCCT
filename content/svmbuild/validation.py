"""Validation of an SVMap folder (1.0 or 1.1): JSON Schemas, references, derived indexes, collision.bin and hashes.

The schemas are taken from spec/svmap/1.<minor>/schemas for the minor version each file declares.
"""

from __future__ import annotations

import json
from functools import lru_cache
from pathlib import Path

from . import canonical
from .paths import SPEC_ROOT
from .svmap import (GE_POINTS, actor_refs, all_actors, derive_event_edges, derive_mission_tree, derive_tag_index,
                    ge_active, minor_of)
from .writers.collision import NONE, CollisionError, read_collision
from .writers.glb import read_glb

SCHEMA_FILES = {
    "common": "common.schema.json",
    "manifest": "manifest.schema.json",
    "gameplay": "gameplay.schema.json",
    "traversal": "traversal.schema.json",
}
SPEC_DIR = SPEC_ROOT.parent          # spec/svmap
SCHEMA_ROOT = SPEC_ROOT / "schemas"  # 1.0, kept for callers that name it


def schema_root_for(version: str | None) -> Path:
    minor = minor_of(version or "")
    root = SPEC_DIR / f"1.{minor}" / "schemas"
    return root if root.is_dir() else SCHEMA_ROOT


@lru_cache(maxsize=None)
def validators(schema_root: str = str(SCHEMA_ROOT)) -> dict:
    from jsonschema import Draft202012Validator
    from referencing import Registry, Resource

    root = Path(schema_root)
    docs = {k: json.loads((root / f).read_text(encoding="utf-8")) for k, f in SCHEMA_FILES.items()}
    registry = Registry().with_resources(
        (doc["$id"], Resource.from_contents(doc)) for doc in docs.values())
    out = {}
    for k, doc in docs.items():
        Draft202012Validator.check_schema(doc)
        out[k] = Draft202012Validator(doc, registry=registry)
    return out


def schema_errors(kind: str, instance) -> list[str]:
    version = instance.get("formatVersion") if isinstance(instance, dict) else None
    v = validators(str(schema_root_for(version)))[kind]
    errs = sorted(v.iter_errors(instance), key=lambda e: list(e.absolute_path))
    return [f"{kind}: {'/'.join(str(p) for p in e.absolute_path) or '<root>'}: {e.message}" for e in errs[:50]]


def gameplay_errors(g: dict) -> list[str]:
    errs = []
    actors = all_actors(g)
    seen: dict[str, str] = {}
    for a in actors:
        low = a["id"].lower()
        if low in seen:
            errs.append(f"gameplay: duplicate actor id {a['id']} (also {seen[low]})")
        seen[low] = a["id"]
    ids = {a["id"] for a in actors}
    for a in actors:
        for ref in actor_refs(a["props"]):
            if ref not in ids:
                errs.append(f"gameplay: {a['id']} refers to unknown actor {ref}")
    if g.get("tagIndex") != derive_tag_index(g):
        errs.append("gameplay: tagIndex does not match the actors' Tag properties")
    if g.get("eventEdges") != derive_event_edges(g):
        errs.append("gameplay: eventEdges do not match the actors' event properties")
    if g.get("missionTree") != derive_mission_tree(g):
        errs.append("gameplay: missionTree does not match the SMission and SObjective properties")
    return errs[:200]


def traversal_errors(t: dict, gameplay: dict | None = None) -> list[str]:
    errs = []
    seen = set()
    mask = t.get("activationMask")
    if mask is not None and gameplay is not None:
        li = gameplay.get("levelInfo", {}).get("props", {}).get("GEActivationMask")
        if li != mask:
            errs.append(f"traversal: activationMask {mask} differs from LevelInfo.GEActivationMask {li}")
    for ge in t["ges"]:
        if ge["id"].lower() in seen:
            errs.append(f"traversal: duplicate GE id {ge['id']}")
        seen.add(ge["id"].lower())
        if len(ge["points"]) != GE_POINTS[ge["kind"]]:
            errs.append(f"traversal: {ge['id']} {ge['kind']} needs {GE_POINTS[ge['kind']]} points")
        if "activeAtStart" in ge and mask is not None and ge["activeAtStart"] != ge_active(ge["mask"], mask):
            errs.append(f"traversal: {ge['id']} activeAtStart does not follow its mask")
    return errs


def collision_errors(data: bytes, actor_ids: set[str]) -> list[str]:
    try:
        d = read_collision(data, strict=True)
    except CollisionError as e:
        return [f"collision: {e}"]
    errs = []
    named = [(f"collider {i} actor", c.actor) for i, c in enumerate(d.colliders)]
    named += [(f"collider {i} parent", c.parent) for i, c in enumerate(d.colliders)]
    named += [(f"cylinder {i}", c.actor) for i, c in enumerate(d.cylinders)]
    named += [(f"zone {i}", z.actor) for i, z in enumerate(d.zones)]
    for what, idx in named:
        if idx != NONE and d.strings[idx] not in actor_ids:
            errs.append(f"collision: {what} names {d.strings[idx]!r}, which is not an actor id")
    return errs[:200]


def class_table_errors(table, gameplay: dict) -> list[str]:
    if not isinstance(table, dict) or not isinstance(table.get("classes"), dict):
        return ["sim/classes.json: no classes object"]
    classes = table["classes"]
    errs = []
    for a in all_actors(gameplay):
        c = classes.get(a["class"])
        if c is None or "defaults" not in c:
            errs.append(f"sim/classes.json: no defaults for {a['class']} ({a['id']})")
    for path, c in classes.items():
        sup = c.get("super")
        if sup and sup not in classes:
            errs.append(f"sim/classes.json: {path} names super class {sup}, which is missing")
    return errs[:50]


def render_errors(root: Path) -> list[str]:
    errs = []
    rdir = root / "render"
    if not rdir.is_dir():
        return errs
    for p in sorted(rdir.glob("*.glb")):
        try:
            js, binary = read_glb(p.read_bytes())
        except ValueError as e:
            errs.append(f"render/{p.name}: {e}")
            continue
        for img in js.get("images", []):
            uri = img.get("uri")
            if uri and not (rdir / uri).is_file():
                errs.append(f"render/{p.name}: image {uri} is missing")
        names = {n.get("name") for n in js.get("nodes", [])}
        inst = (js.get("extras") or {}).get("svmap", {}).get("instances", [])
        for i in inst:
            if i.get("mesh") not in names:
                errs.append(f"render/{p.name}: instance {i.get('actor')} names mesh {i.get('mesh')}, which is not a node")
                break
        for b in js.get("buffers", []):
            if "uri" in b:
                errs.append(f"render/{p.name}: buffer with an external uri")
    for p in sorted(rdir.glob("*.json")):
        try:
            canonical.loads(p.read_bytes())
        except ValueError as e:
            errs.append(f"render/{p.name}: {e}")
    return errs[:50]


def validate_svmap(root: Path) -> list[str]:
    """All problems found in the SVMap folder at root (empty when valid)."""
    root = Path(root)
    errs: list[str] = []

    def load(rel: str, required: bool = True):
        p = root / rel
        if not p.exists():
            if required:
                errs.append(f"missing {rel}")
            return None
        try:
            return canonical.loads(p.read_bytes())
        except ValueError as e:
            errs.append(f"{rel}: {e}")
            return None

    manifest = load("manifest.json")
    gameplay = load("sim/gameplay.json")
    traversal = load("sim/traversal.json")
    if manifest is not None:
        errs += schema_errors("manifest", manifest)
    if gameplay is not None:
        se = schema_errors("gameplay", gameplay)
        errs += se
        if not se:
            errs += gameplay_errors(gameplay)
    if traversal is not None:
        se = schema_errors("traversal", traversal)
        errs += se
        if not se:
            errs += traversal_errors(traversal, gameplay)
    coll = root / "sim" / "collision.bin"
    if not coll.exists():
        errs.append("missing sim/collision.bin")
    elif gameplay is not None and isinstance(gameplay.get("actors"), list):
        ids = {a.get("id") for a in all_actors(gameplay) if isinstance(a, dict)}
        errs += collision_errors(coll.read_bytes(), ids)
    table = load("sim/classes.json", required=False)
    if table is not None and gameplay is not None:
        errs += class_table_errors(table, gameplay)
    errs += render_errors(root)
    if manifest is not None and gameplay is not None and traversal is not None:
        mid = manifest.get("map")
        if gameplay.get("map") != mid or traversal.get("map") != mid:
            errs.append("map id differs between manifest.json, gameplay.json and traversal.json")
        versions = {manifest.get("formatVersion"), gameplay.get("formatVersion"), traversal.get("formatVersion")}
        if len({minor_of(v or "") for v in versions}) != 1:
            errs.append(f"formatVersion differs between the files: {sorted(str(v) for v in versions)}")
    if manifest is not None:
        try:
            files, hashes = canonical.hash_map(root)
        except ValueError as e:
            errs.append(str(e))
        else:
            if manifest.get("files") != files:
                errs.append("manifest files do not match the folder contents")
            if manifest.get("hashes") != hashes:
                errs.append("manifest hashes do not match the folder contents")
    return errs
