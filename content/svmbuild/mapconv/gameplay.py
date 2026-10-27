"""sim/gameplay.json and sim/classes.json of a converted map.

- Actors: Actors[0] is the LevelInfo; every "gameplay" actor (source.py) follows in Level.Actors order with its
  complete stored property bag (typed; actor references as {"$actor"}, everything else as {"$asset"} / {"$class"}),
  plus its effective Tag. `src.export` is the 1-based export index in the .sdc.
- mapSettings: SMapSettings of <Map>-i.utc, one entry per game mode (TimeLimit minutes, lives, RespawnTime seconds,
  ScoreLimit, Handicap, NumDisksMax, GameModeId).
- animations: for every skeletal mesh a gameplay actor uses (SAnimatedMesh props, fish), its default MeshAnimation's
  sequences with frame counts, rates and notifies (normalized time, function name, notify object with typed props).
  The server needs them: AnimNotify_Trigger notifies fire events (the SteeE crane).
- tagIndex, eventEdges, missionTree: derived (svmbuild.svmap, 1.1 rules).
- sim/classes.json: the effective class defaults of every class used by those actors, and the super chains, so a
  reader can rebuild the full state of an actor (defaults overlaid with its props) from sim/ alone.
"""

from __future__ import annotations

from typing import Dict, List, Optional, Tuple

from ..sources import animations as anim_reader
from ..sources.typedprops import Ref, bag_to_json, default_resolver
from ..svmap import LATEST_VERSION, bare_class, with_derived
from .source import ActorInfo, MapSource

MODE_NAMES = {"GM_Undefined": "Undefined", "GM_Extraction": "Extraction", "GM_Sabotage": "Sabotage",
              "GM_Neutralization": "Neutralization", "GM_DiskHunt": "DiskHunt", "GM_Multi": "Story",
              "GM_DeathMatch": "Deathmatch", "GM_MercDeathMatch": "MercDeathmatch"}
MODE_INT_FIELDS = ("TimeLimit", "ScoreLimit", "SpiesLives", "RespawnTime", "SentriesLives", "Handicap", "NumDisksMax",
                   "GameModeId")


def effective_tag(src: MapSource, info: ActorInfo) -> str:
    if "Tag" in info.props:
        return str(info.props["Tag"])
    d = src.db.default(info.cls, "Tag")
    return str(d) if d is not None else bare_class(info.cls)


def actor_json(src: MapSource, info: ActorInfo) -> dict:
    props = bag_to_json(info.props, src.resolve, src.notes, info.name)
    props["Tag"] = effective_tag(src, info)
    return {"id": info.name, "class": info.cls, "props": props, "src": {"export": info.ref}}


# ---- map settings (<Map>-i.utc) ----------------------------------------------------------------

def read_map_settings(src: MapSource) -> Tuple[dict, dict, Optional[str]]:
    """(mapSettings, ui text, path of the -i package or None)."""
    key = f"{src.name}-i".lower()
    pkg = src.pset.get(key)
    if pkg is None:
        src.notes.add("no-map-settings", f"{src.name}-i.utc not found")
        return {}, {}, None
    exports = [e for e in pkg.exports if e.class_name == "SMapSettings" and e.size > 0]
    if not exports:
        src.notes.add("no-map-settings", f"{src.name}-i.utc has no SMapSettings")
        return {}, {}, str(src.pset.files.get(key))
    from scctpkg.properties import skip_state_frame  # noqa: PLC0415

    e = exports[0]
    rec = src.db.record("SBase.SMapSettings")
    r = pkg.export_reader(e)
    skip_state_frame(r, e.flags)
    props = src.db.decoder.tagged(r, pkg, rec["_field"])
    from .localization import apply_text, read_text  # noqa: PLC0415

    apply_text(props, read_text(src.game, f"{src.name}-i").get(e.name.lower()), lambda k: src.db.prop("SBase.SMapSettings", k))
    enum = src.db.enum_values(".EGameMode")
    modes: Dict[str, dict] = {}
    for i, m in enumerate(props.get("Modes") or []):
        gid = int(m.get("GameModeId", 0)) if isinstance(m, dict) else 0
        label = enum[gid] if enum and 0 <= gid < len(enum) else f"GM_{gid}"
        name = MODE_NAMES.get(label, label.replace("GM_", "")) or f"Mode{i}"
        if name in modes:
            name = f"{name}{i}"
        entry = {k: int(m.get(k, 0)) for k in MODE_INT_FIELDS}
        modes[name] = entry
    ui = {k: str(v) for k, v in props.items() if isinstance(v, str)}
    strings = props.get("ModesStrings") or []
    ui_modes = {}
    for i, name in enumerate(modes):
        if i < len(strings) and isinstance(strings[i], dict):
            ui_modes[name] = {k: str(v) for k, v in strings[i].items() if isinstance(v, str)}
    ui["modes"] = ui_modes
    return modes, ui, str(src.pset.files.get(key))


# ---- animations ------------------------------------------------------------------------------------

def _skeletal_animation(src: MapSource, mesh_ref: Ref):
    """(mesh package, mesh export, animation export) of a SkeletalMesh reference, or None."""
    from scctpkg.skeletalmesh import read_skeletal_mesh  # noqa: PLC0415

    owner, e = src.ref_export(mesh_ref)
    if owner is None or e is None or e.class_name != "SkeletalMesh":
        return None
    try:
        mesh = read_skeletal_mesh(owner, e)
    except Exception as ex:  # noqa: BLE001
        src.notes.add("skeletal-mesh", f"{mesh_ref.path}: {ex}")
        return None
    if mesh.animation_ref <= 0:
        return None
    ae = owner.exports[mesh.animation_ref - 1]
    if ae.class_name != "MeshAnimation":
        return None
    return owner, e, ae


def notify_object(src: MapSource, pkg, ref: int) -> dict:
    """{"object", "class", "props"} of an AnimNotify object (typed props, references as $asset)."""
    cf = src.db.cd
    from scctpkg.properties import skip_state_frame  # noqa: PLC0415

    if ref == 0:
        return {}
    if ref < 0:
        return {"object": cf.object_path(pkg, ref), "class": "", "props": {}}
    e = pkg.exports[ref - 1]
    path = cf.export_path(pkg, e)
    cls = src.db.find(f"{e.class_package}.{e.class_name}") or f"{e.class_package}.{e.class_name}"
    rec = src.db.record(cls)
    props: dict = {}
    if rec is not None and e.size > 0:
        r = pkg.export_reader(e)
        skip_state_frame(r, e.flags)
        decoded = src.db.decoder.tagged(r, pkg, rec["_field"])
        props = bag_to_json(decoded, default_resolver, src.notes, path)
    return {"object": path, "class": cls, "props": props}


def animation_set(src: MapSource, pkg, anim_export) -> dict:
    a = anim_reader.read_animation(pkg, anim_export)
    seqs = {}
    for s in a.sequences:
        notifies = []
        for n in s.notifies:
            entry = {"time": float(n.time), "function": n.function}
            entry.update(notify_object(src, pkg, n.notify_ref))
            notifies.append(entry)
        seqs[s.name] = {"frames": int(s.num_frames), "rate": float(s.rate), "startFrame": int(s.start_frame),
                        "groups": list(s.groups), "notifies": notifies}
    return {"animation": src.db.cd.export_path(pkg, anim_export), "sequences": seqs}


def build_animations(src: MapSource, actors: List[ActorInfo]) -> dict:
    out: dict = {}
    seen = set()
    for info in actors:
        mesh = info.props.get("Mesh")
        if not isinstance(mesh, Ref) or mesh.ref == 0:
            continue
        resolved = src.resolve(mesh)
        if not resolved or "$asset" not in resolved:
            continue
        key = resolved["$asset"]
        if key in seen:
            continue
        seen.add(key)
        found = _skeletal_animation(src, mesh)
        if found is None:
            continue
        owner, _mesh_export, anim_export = found
        try:
            out[key] = animation_set(src, owner, anim_export)
        except Exception as ex:  # noqa: BLE001
            src.notes.add("animation", f"{key}: {ex}")
    return out


# ---- documents -------------------------------------------------------------------------------------

def build_gameplay(src: MapSource) -> Tuple[dict, dict, dict]:
    """(gameplay.json document, ui mapinfo, stats)."""
    level = src.level_info
    li = actor_json(src, level)
    mask = level.props.get("GEActivationMask")
    if mask is None:
        mask = src.db.default(level.cls, "GEActivationMask", 0) or 0
    li["props"]["GEActivationMask"] = int(mask)
    gameplay_actors = src.of("gameplay")
    actors = [actor_json(src, a) for a in gameplay_actors]
    settings, ui, settings_path = read_map_settings(src)
    doc = {
        "formatVersion": LATEST_VERSION,
        "map": src.name,
        "levelInfo": li,
        "mapSettings": settings,
        "actors": actors,
        "animations": build_animations(src, [level, *gameplay_actors]),
    }
    doc = with_derived(doc)
    by_class: Dict[str, int] = {}
    for a in [level, *gameplay_actors]:
        by_class[a.cls] = by_class.get(a.cls, 0) + 1
    cats: Dict[str, int] = {}
    for a in src.actors:
        cats[a.category] = cats.get(a.category, 0) + 1
    stats = {"actors": len(actors) + 1, "levelActors": len(src.actors), "categories": cats, "byClass": by_class,
             "tags": len(doc["tagIndex"]), "eventEdges": len(doc["eventEdges"]),
             "missions": len(doc["missionTree"]), "animationSets": len(doc["animations"]),
             "mapSettings": settings_path}
    return doc, ui, stats


def build_class_table(src: MapSource) -> dict:
    """Effective defaults of the classes the written actors use, with their super chains."""
    used = sorted({a.cls for a in src.actors if a.category in ("level", "gameplay")})
    classes: Dict[str, dict] = {}
    for cls in used:
        for i, c in enumerate(src.db.chain(cls)):
            entry = classes.setdefault(c, {"super": src.db.classes[c].get("super")})
            if i == 0:
                entry["defaults"] = src.db.defaults_json(c, notes=src.notes)
    return {"format": "svm.classes/1", "classes": classes}
