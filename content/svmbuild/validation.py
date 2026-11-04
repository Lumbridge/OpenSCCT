"""Validation of an SVMap folder (1.0 to 1.2): JSON Schemas, references, derived indexes, collision.bin, the 1.2
match, mover timing and outfit files, and hashes.

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


# OpenSCCT's own classes that a class table need not list: a reader without an entry uses the class's own defaults
# (spec/svmap/1.2/conveyors.md).
CONVEYOR_CLASS = "Svm.Conveyor"
NATIVE_CLASSES = frozenset({CONVEYOR_CLASS})


def class_table_errors(table, gameplay: dict) -> list[str]:
    if not isinstance(table, dict) or not isinstance(table.get("classes"), dict):
        return ["sim/classes.json: no classes object"]
    classes = table["classes"]
    errs = []
    for a in all_actors(gameplay):
        c = classes.get(a["class"])
        if c is None and a["class"] in NATIVE_CLASSES:
            continue
        if c is None or "defaults" not in c:
            errs.append(f"sim/classes.json: no defaults for {a['class']} ({a['id']})")
    for path, c in classes.items():
        sup = c.get("super")
        if sup and sup not in classes:
            errs.append(f"sim/classes.json: {path} names super class {sup}, which is missing")
    return errs[:50]


MATCH_FORMAT = "svm.match/1"
OUTFITS_FORMAT = "svm.outfits/1"
MOST_PLAYERS = 24
TEAM_SIZES = 12
MOVER_KEYS = 8  # Engine.Mover KeyPos[8]


def _mover_mode_ok(mode) -> bool:
    return mode in ("normal", "hidden") or (isinstance(mode, dict) and set(mode) == {"key"} and
                                            isinstance(mode["key"], int) and 0 <= mode["key"] < MOVER_KEYS)


def match_errors(match, gameplay: dict, classes) -> list[str]:
    """sim/match.json (spec/svmap/1.2/match.md): rules name written actors once each, ranges run 0 (any) to 24 with
    min <= max, key positions only on movers, no rules on conveyors, win targets name missions and list 12 team
    sizes."""
    where = "sim/match.json"
    if not isinstance(match, dict) or match.get("format") != MATCH_FORMAT:
        return [f"{where}: format is not {MATCH_FORMAT}"]
    actors = {a["id"].lower(): a for a in all_actors(gameplay) if isinstance(a, dict) and isinstance(a.get("id"), str)}
    table = classes.get("classes", {}) if isinstance(classes, dict) else {}

    def is_a(cls: str, base: str) -> bool:
        for _ in range(64):
            if not cls:
                return False
            if cls.lower() == base.lower():
                return True
            cls = (table.get(cls) or {}).get("super") or ""
        return False

    errs = []
    ruled = set()
    for i, r in enumerate(match.get("rules", [])):
        at = f"{where}: rules[{i}]"
        actor = r.get("actor") if isinstance(r, dict) else None
        if not isinstance(actor, str) or not actor:
            errs.append(f"{at}: no actor")
            continue
        if actor.lower() in ruled:
            errs.append(f"{at}: {actor} has a second rule")
        ruled.add(actor.lower())
        players = r.get("players")
        if not (isinstance(players, list) and len(players) == 2 and
                all(isinstance(n, int) and 0 <= n <= MOST_PLAYERS for n in players)):
            errs.append(f"{at}: players must be [min, max], each 0 (any) to {MOST_PLAYERS}")
        elif players[0] and players[1] and players[0] > players[1]:
            errs.append(f"{at}: min {players[0]} is more than max {players[1]}")
        for key in ("outside", "inside"):
            if key in r and not _mover_mode_ok(r[key]):
                errs.append(f"{at}: {key} must be normal, hidden or a key")
        if r.get("outside") == "normal":
            errs.append(f"{at}: outside its range an actor cannot work as normal")
        keyed = isinstance(r.get("outside"), dict) or isinstance(r.get("inside"), dict)
        target = actors.get(actor.lower())
        if keyed and target is not None and not is_a(target.get("class", ""), "Engine.Mover"):
            errs.append(f"{at}: key positions apply to movers only ({actor})")
        if target is not None and target.get("class") == CONVEYOR_CLASS:
            errs.append(f"{at}: {actor} is a conveyor; a player-count rule cannot take its belt out of the round")
    for i, w in enumerate(match.get("win", [])):
        at = f"{where}: win[{i}]"
        mission = w.get("mission") if isinstance(w, dict) else None
        target = actors.get(mission.lower()) if isinstance(mission, str) else None
        if target is None or not is_a(target.get("class", ""), "SBase.SMission"):
            errs.append(f"{at}: {mission} is not a mission of the map")
        sizes = w.get("bySize") if isinstance(w, dict) else None
        if not (isinstance(sizes, list) and len(sizes) == TEAM_SIZES and
                all(isinstance(n, int) and n >= 0 for n in sizes)):
            errs.append(f"{at}: bySize must list {TEAM_SIZES} counts (1v1 to 12v12), 0 keeping the map's own")
    return errs


MOVERS_FORMAT = "svm.movers/1"
MIN_LEG = 0.005  # Engine.Mover.InterpolateTo's shortest leg
LONGEST_LEG = 3600.0


def movers_errors(movers, gameplay: dict, classes) -> list[str]:
    """sim/movers.json (spec/svmap/1.2/movers.md): each entry names a written mover once, lists at most 8 keys, gives
    no time to key 0, and times run from the shortest leg to an hour."""
    where = "sim/movers.json"
    if not isinstance(movers, dict) or movers.get("format") != MOVERS_FORMAT:
        return [f"{where}: format is not {MOVERS_FORMAT}"]
    actors = {a["id"].lower(): a for a in all_actors(gameplay) if isinstance(a, dict) and isinstance(a.get("id"), str)}
    table = classes.get("classes", {}) if isinstance(classes, dict) else {}

    def is_mover(cls: str) -> bool:
        for _ in range(64):
            if not cls:
                return False
            if cls.lower() == "engine.mover":
                return True
            cls = (table.get(cls) or {}).get("super") or ""
        return False

    errs = []
    seen = set()
    for i, m in enumerate(movers.get("movers", [])):
        at = f"{where}: movers[{i}]"
        actor = m.get("actor") if isinstance(m, dict) else None
        if not isinstance(actor, str) or not actor:
            errs.append(f"{at}: no actor")
            continue
        if actor.lower() in seen:
            errs.append(f"{at}: {actor} is listed twice")
        seen.add(actor.lower())
        target = actors.get(actor.lower())
        if target is None or not is_mover(target.get("class", "")):
            errs.append(f"{at}: {actor} is not a mover of the map")
        keys = m.get("keys")
        if not isinstance(keys, list) or len(keys) > MOVER_KEYS:
            errs.append(f"{at}: keys must list at most {MOVER_KEYS} keys")
            continue
        for k, key in enumerate(keys):
            if not isinstance(key, dict) or set(key) - {"time", "smooth"}:
                errs.append(f"{at}: keys[{k}] holds only time and smooth")
                continue
            if "time" in key:
                t = key["time"]
                if k == 0:
                    errs.append(f"{at}: keys[0] has no leg before it, so it takes no time")
                elif isinstance(t, bool) or not isinstance(t, (int, float)) or not MIN_LEG <= t <= LONGEST_LEG:
                    errs.append(f"{at}: keys[{k}].time must be {MIN_LEG} to {LONGEST_LEG} seconds")
            if "smooth" in key and not isinstance(key["smooth"], bool):
                errs.append(f"{at}: keys[{k}].smooth must be true or false")
    return errs


def outfits_errors(root: Path, outfits) -> list[str]:
    """render/outfits.json (spec/svmap/1.2/outfits.md): known teams, slots and policies, and the textures and models
    it names exist under render/."""
    where = "render/outfits.json"
    if not isinstance(outfits, dict) or outfits.get("format") != OUTFITS_FORMAT:
        return [f"{where}: format is not {OUTFITS_FORMAT}"]
    errs = []
    for team in ("spy", "merc"):
        t = outfits.get(team)
        if not isinstance(t, dict):
            continue
        if t.get("cosmetics", "map") not in ("map", "player", "none"):
            errs.append(f"{where}: {team}.cosmetics must be map, player or none")
        for slot, value in (t.get("slots") or {}).items():
            tex = value.get("texture") if isinstance(value, dict) else None
            if slot not in ("body", "head"):
                errs.append(f"{where}: {team}.slots.{slot} is not body or head")
            elif not isinstance(tex, str) or not (root / "render" / tex).is_file():
                errs.append(f"{where}: {team}.slots.{slot} texture {tex} is not a file under render/")
        model = t.get("model")
        if isinstance(model, dict):
            glb, stock = model.get("glb"), model.get("stock")
            if (glb is None) == (stock is None):
                errs.append(f"{where}: {team}.model names either a glb or a stock mesh")
            elif glb is not None and not (root / "render" / glb).is_file():
                errs.append(f"{where}: {team}.model glb {glb} is not a file under render/")
    return errs

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
    match = load("sim/match.json", required=False)
    if match is not None and gameplay is not None:
        errs += match_errors(match, gameplay, table)
    movers = load("sim/movers.json", required=False)
    if movers is not None and gameplay is not None:
        errs += movers_errors(movers, gameplay, table)
    outfits = load("render/outfits.json", required=False)
    if outfits is not None:
        errs += outfits_errors(root, outfits)
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
