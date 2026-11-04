"""Classes a map defines itself, and the RE+ families among them lifted into SVMap 1.2 data.

A map package can carry its own UnrealScript classes (RE+ compiles its tools' actors into MyLevel; private map
builders added scripts of their own). source.py registers them from the package's Class and *Property exports, so
their actors decode with full props like any other. Most stay that way: written to sim/gameplay.json as actors of a
class marked `"mapScript": true` in sim/classes.json, which OpenSCCT keeps as inert actors.

Two RE+ families become native data instead, and their actors are not written:

- SMatchSizeRules (v3) and its older names SPlayerCountRules (v2) and SObjectivePlayers (v1): player-count rules on
  objectives and movers, and win targets by team size, to sim/match.json (spec/svmap/1.2/match.md).
- SCharacterOutfits (v7) and its older names: the teams' textures and replacement models, to render/outfits.json
  with the textures in render/textures/ and the models in render/chars/<Map>/ (spec/svmap/1.2/outfits.md).
  ReloadedBotController, the do-nothing controller RE+'s Play Level bots used, is dropped with them.
"""

from __future__ import annotations

import shutil
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from ..sources.typedprops import Ref
from .source import ActorInfo, MapSource

# Newest first: when a map has several, the first found wins (RE+ migrated older actors but ShipD kept two).
MATCH_RULE_CLASSES = ("SMatchSizeRules", "SPlayerCountRules", "SObjectivePlayers")
OUTFIT_CLASSES = ("SCharacterOutfits", "SCharacterSkins", "ReloadedCharacterSkins5", "ReloadedCharacterSkins4",
                  "ReloadedCharacterSkins3", "ReloadedCharacterSkins2", "ReloadedCharacterSkins")
BOT_CONTROLLER = "ReloadedBotController"

# SMatchSizeRules: 8 team sizes per mission row (1v1..8v8); SVMap keeps 12 (OpenSCCT lobbies go to 12v12).
RE_TEAM_SIZES = 8
TEAM_SIZES = 12

# The slots RE+ dresses (CharacterSkinsModel.h Slots): team, slot name in outfits.json, property.
OUTFIT_SLOTS = (("spy", "body", "SpyBody"), ("spy", "head", "SpyHead"),
                ("merc", "body", "MercBody"), ("merc", "head", "MercHead"))
OUTFIT_MODELS = (("spy", "SpyModel", "SpyGoggleOffset"), ("merc", "MercModel", "MercGoggleOffset"))
GOGGLE_BONE = "B Head"


def _bare(cls: str) -> str:
    return cls.rsplit(".", 1)[-1]


def _own_actors(src: MapSource, names: Tuple[str, ...]) -> List[ActorInfo]:
    """Actors of the map's own classes with these names, newest class first, then level order."""
    found = [a for a in src.actors if a.category == "gameplay" and src.db.is_package_class(a.cls, src.pkg.path.stem)
             and _bare(a.cls) in names]
    return sorted(found, key=lambda a: (names.index(_bare(a.cls)), a.position))


def _elements(value: Any) -> Dict[int, Any]:
    """A decoded static array ({index: value}) or list as {index: value}."""
    if isinstance(value, dict):
        return {int(k): v for k, v in value.items() if isinstance(k, int)}
    if isinstance(value, list):
        return {i: v for i, v in enumerate(value) if v is not None}
    return {}


def _actor_id(src: MapSource, ref: Any) -> Optional[str]:
    """The id of a placed actor a stored reference names (any category but editor and skipped), or None."""
    if not isinstance(ref, Ref) or ref.ref <= 0 or not src.own(ref):
        return None
    info = src.by_ref.get(ref.ref)
    if info is None or info.category in ("editor", "skipped"):
        return None
    return info.name


def _lift(src: MapSource, chosen: ActorInfo, others: List[ActorInfo], what: str) -> None:
    chosen.category, chosen.reason = "lifted", what
    for a in others:
        a.category, a.reason = "lifted", f"{what} (superseded by {chosen.name})"
        src.notes.add("map-class-duplicate", f"{a.name} {a.cls}: {chosen.name} wins")


# ---- player-count rules -----------------------------------------------------------------------------------

def build_match(src: MapSource) -> Tuple[Optional[dict], dict]:
    """(sim/match.json document or None, stats). Marks the rules actors as lifted."""
    actors = _own_actors(src, MATCH_RULE_CLASSES)
    if not actors:
        return None, {}
    chosen = actors[0]
    _lift(src, chosen, actors[1:], "player-count rules (sim/match.json)")
    props = chosen.props
    subjects = _elements(props.get("Subject") if "Subject" in props else props.get("Objective"))
    lows, highs, opens = (_elements(props.get(k)) for k in ("MinPlayers", "MaxPlayers", "OpenInRange"))
    rules = []
    for i in sorted(subjects):
        target = _actor_id(src, subjects[i])
        if target is None:
            if isinstance(subjects[i], Ref) and subjects[i].ref != 0:
                src.notes.add("match-rule-subject", f"{chosen.name}.Subject[{i}] -> {subjects[i].path}")
            continue
        rule: Dict[str, Any] = {"actor": target, "players": [int(lows.get(i, 0) or 0), int(highs.get(i, 0) or 0)]}
        info = next(a for a in src.actors if a.name == target)
        if src.db.is_a(info.cls, "Engine.Mover"):
            # Outside the range RE+ closes (key 0) and locks; with OpenInRange it opens (key 1) and locks inside.
            rule["outside"] = {"key": 0}
            if int(opens.get(i, 0) or 0):
                rule["inside"] = {"key": 1}
        rules.append(rule)
    win = []
    missions = _elements(props.get("WinMission"))
    targets = _elements(props.get("WinTarget"))
    for j in sorted(missions):
        mission = _actor_id(src, missions[j])
        if mission is None:
            continue
        by_size = [int(targets.get(j * RE_TEAM_SIZES + s, 0) or 0) for s in range(RE_TEAM_SIZES)]
        by_size += [0] * (TEAM_SIZES - RE_TEAM_SIZES)
        if any(by_size):
            win.append({"mission": mission, "bySize": by_size})
    doc = {"format": "svm.match/1", "rules": rules, "win": win}
    stats = {"from": chosen.name, "class": chosen.cls, "rules": len(rules), "win": len(win),
             "playLevelPlayers": int(props.get("PlayLevelPlayers", 0) or 0)}
    return doc, stats


# ---- outfits ------------------------------------------------------------------------------------------------

def _outfit_values(info: ActorInfo) -> dict:
    keys = [p for _, _, p in OUTFIT_SLOTS] + [m for _, m, _ in OUTFIT_MODELS] + [g for _, _, g in OUTFIT_MODELS]
    out = {}
    for k in keys:
        v = info.props.get(k)
        out[k] = v.path if isinstance(v, Ref) else v
    return out


def _texture_png(src: MapSource, ref: Ref, tex_dir: Path, uris: dict) -> Optional[str]:
    """Writes the diffuse texture a slot's material resolves to and returns its file name under render/textures/."""
    from scctpkg import export_gltf  # noqa: PLC0415
    from scctpkg.materials import MaterialResolver  # noqa: PLC0415

    pkg, export = src.ref_export(ref)
    if pkg is None or export is None:
        return None
    res = MaterialResolver(src.pset).resolve(pkg, export.index + 1)
    if res.texture is None or res.texture.image is None:
        return None
    return export_gltf._ensure_texture(res, tex_dir, uris)


def build_outfits(src: MapSource, db, work: Path, write: bool = True) -> Tuple[Dict[str, bytes], dict]:
    """(render/-relative files: outfits.json, textures/*.png, chars/<Map>/..., stats). Marks the outfit actors and
    their bot controllers as lifted, also when `write` is off (a sim-only build), so sim/ is the same either way."""
    actors = _own_actors(src, OUTFIT_CLASSES)
    if not actors:
        return {}, {}
    chosen = actors[0]
    _lift(src, chosen, actors[1:], "character outfits (render/outfits.json)")
    for a in _own_actors(src, (BOT_CONTROLLER,)):
        a.category, a.reason = "lifted", "RE+ Play Level bot controller"
    mine = _outfit_values(chosen)
    for a in actors[1:]:
        if _outfit_values(a) != mine:
            src.notes.add("outfit-actors-differ", f"{a.name} differs from {chosen.name}; {chosen.name} is used")
    if not write:
        return {}, {"from": chosen.name, "class": chosen.cls}

    work = Path(work)
    if work.exists():
        shutil.rmtree(work)
    tex_dir = work / "textures"
    files: Dict[str, bytes] = {}
    uris: dict = {}
    doc: Dict[str, Any] = {"format": "svm.outfits/1"}
    teams: Dict[str, dict] = {}
    for team, slot, prop in OUTFIT_SLOTS:
        ref = chosen.props.get(prop)
        if not isinstance(ref, Ref) or ref.ref == 0:
            continue
        uri = _texture_png(src, ref, tex_dir, uris)
        if uri is None:
            src.notes.add("outfit-texture", f"{chosen.name}.{prop} -> {ref.path}")
            continue
        teams.setdefault(team, {}).setdefault("slots", {})[slot] = {"texture": uri, "source": ref.path}
    models: Dict[str, List[str]] = {}
    for team, prop, goggles in OUTFIT_MODELS:
        ref = chosen.props.get(prop)
        if not isinstance(ref, Ref) or ref.ref == 0:
            continue
        pkg, export = src.ref_export(ref)
        if pkg is None or export is None or export.class_name != "SkeletalMesh":
            src.notes.add("outfit-model", f"{chosen.name}.{prop} -> {ref.path}")
            continue
        offset = chosen.props.get(goggles) or {}
        model: Dict[str, Any] = {"goggles": {"bone": GOGGLE_BONE,
                                             "offset": [float(offset.get(k, 0.0)) for k in ("X", "Y", "Z")]}}
        if pkg.path.stem.lower() == src.pkg.path.stem.lower():
            model["glb"] = f"chars/{src.name}/{export.name}.glb"
            models.setdefault(src.name, []).append(export.name)
        else:
            # A mesh of a game package (RE+ offers the other team's stock body): a reference, nothing copied.
            model["stock"] = ref.path
        teams.setdefault(team, {})["model"] = model
    for team in teams.values():
        team["cosmetics"] = "map"
    doc.update(dict(sorted(teams.items())))

    if models:
        from ..charconv import export_package  # noqa: PLC0415

        for name, meshes in models.items():
            st = export_package(src.pkg, src.pset, sorted(meshes), work / "chars" / name, db, src.notes,
                                work / "tmp", name)
            for f in st["failed"]:
                src.notes.add("outfit-model-failed", f)
    for p in sorted(work.rglob("*")):
        if p.is_file() and "tmp" not in p.relative_to(work).parts:
            files[p.relative_to(work).as_posix()] = p.read_bytes()
    shutil.rmtree(work, ignore_errors=True)
    from .. import canonical  # noqa: PLC0415

    files["outfits.json"] = canonical.dump_file(doc)
    stats = {"from": chosen.name, "class": chosen.cls, "teams": sorted(teams),
             "textures": sum(1 for k in files if k.startswith("textures/")),
             "models": sorted(f for f in files if f.endswith(".glb"))}
    return files, stats


# ---- every map class --------------------------------------------------------------------------------------

def map_script_actors(src: MapSource) -> List[ActorInfo]:
    """The written actors of classes the map defines itself (kept as inert map scripts)."""
    return [a for a in src.actors if a.category == "gameplay" and src.db.is_package_class(a.cls, src.pkg.path.stem)]
