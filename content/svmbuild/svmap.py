"""SVMap model helpers: derived indexes of sim/gameplay.json and the map folder writer.

Two format minors are written: 1.0 (the synthetic TestBox) and 1.1 (converted maps). The derived indexes follow the
rules of the version a document declares (spec/svmap/1.0/README.md, spec/svmap/1.1/README.md).
"""

from __future__ import annotations

import re
import shutil
from pathlib import Path
from typing import Any, Iterator

import numpy as np

from . import canonical

FORMAT_VERSION = "1.0.0"          # what the TestBox generator writes
LATEST_VERSION = "1.1.0"          # what the map converter writes
GE_KINDS = {"POLE": 1, "FENCE": 2, "HOH": 4, "LG": 8, "LADDER": 16, "PIPE": 32, "ZL": 64}
GE_POINTS = {"POLE": 2, "FENCE": 2, "HOH": 2, "LG": 2, "LADDER": 3, "PIPE": 3, "ZL": 2}
# 1.0: the properties that name an event (Tag of the receivers).
EVENT_PROPS = ("Event",)
# 1.1: properties (at any depth) that hold actors whose events the owner accepts (sender filters).
ACCEPT_KEYS = ("DestroyOnEventFrom", "AlertOnEventFrom", "EventFromActor", "EventSender")
# 1.1: name-array properties that list the sequences an actor plays.
SEQUENCE_KEYS = ("Animations", "AnimsFish")


def minor_of(version: str) -> int:
    m = re.match(r"^1\.(\d+)\.\d+$", version or "")
    return int(m.group(1)) if m else 0


def bare_class(class_path: str) -> str:
    return class_path.rsplit(".", 1)[-1]


def all_actors(gameplay: dict) -> list[dict]:
    """LevelInfo first, then the placed actors, as in Level.Actors."""
    return [gameplay["levelInfo"], *gameplay["actors"]]


def actor_refs(value: Any) -> Iterator[str]:
    """Every {"$actor": id} inside a property value."""
    if isinstance(value, dict):
        if "$actor" in value and len(value) == 1:
            yield value["$actor"]
            return
        for v in value.values():
            yield from actor_refs(v)
    elif isinstance(value, list):
        for v in value:
            yield from actor_refs(v)


def _ref_list(value: Any) -> list[str]:
    if isinstance(value, list):
        return [v["$actor"] for v in value if isinstance(v, dict) and "$actor" in v]
    if isinstance(value, dict) and "$elements" in value:
        elements = value["$elements"]
        return [elements[k]["$actor"] for k in sorted(elements, key=int)
                if isinstance(elements[k], dict) and "$actor" in elements[k]]
    return []


def _is_name(value: Any) -> bool:
    return isinstance(value, str) and value != "" and value.lower() != "none"


def derive_tag_index(gameplay: dict) -> dict[str, list[str]]:
    index: dict[str, list[str]] = {}
    spelling: dict[str, str] = {}
    for a in all_actors(gameplay):
        tag = a["props"].get("Tag")
        if not _is_name(tag):
            continue
        key = spelling.setdefault(tag.lower(), tag)
        index.setdefault(key, []).append(a["id"])
    return index


def _derive_event_edges_10(gameplay: dict) -> list[dict]:
    tags = {k.lower(): v for k, v in derive_tag_index(gameplay).items()}
    edges = []
    for a in all_actors(gameplay):
        for prop in EVENT_PROPS:
            event = a["props"].get(prop)
            if _is_name(event):
                edges.append({"from": a["id"], "via": prop, "event": event, "to": list(tags.get(event.lower(), []))})
    return edges


def _is_event_key(key: str) -> bool:
    return key in ("Event", "Events") or (key.endswith("Event") and len(key) > 5)


def notify_seconds(t: float, frames: int, rate: float) -> float:
    """Seconds into a sequence of `frames` frames at `rate` at which a notify at normalized time t fires (float32)."""
    f32 = np.float32
    if rate <= 0:
        return 0.0
    return float(f32(f32(f32(t) * f32(frames)) / f32(rate)))


def _walk_events(owner: dict, value: Any, path: str, key: str, tags: dict, out: list) -> None:
    """Fire and accept edges inside one property value, in document order (object keys sorted)."""
    if isinstance(value, dict):
        if "$actor" in value or "$asset" in value or "$class" in value:
            if key in ACCEPT_KEYS and "$actor" in value:
                tag = owner["props"].get("Tag")
                out.append({"from": owner["id"], "via": path, "event": tag if _is_name(tag) else "None",
                            "to": [value["$actor"]], "kind": "accept"})
            return
        if "$elements" in value and len(value) == 1:
            for k in sorted(value["$elements"], key=int):
                _walk_events(owner, value["$elements"][k], f"{path}[{k}]", key, tags, out)
            return
        for k in sorted(value, key=lambda s: s.encode("ascii")):
            _walk_events(owner, value[k], f"{path}.{k}" if path else k, k, tags, out)
        return
    if isinstance(value, list):
        for i, v in enumerate(value):
            _walk_events(owner, v, f"{path}[{i}]", key, tags, out)
        return
    if isinstance(value, str) and _is_event_key(key) and _is_name(value):
        out.append({"from": owner["id"], "via": path, "event": value, "to": list(tags.get(value.lower(), []))})


def _derive_event_edges_11(gameplay: dict) -> list[dict]:
    tags = {k.lower(): v for k, v in derive_tag_index(gameplay).items()}
    anims = gameplay.get("animations", {})
    edges: list[dict] = []
    for a in all_actors(gameplay):
        props = a["props"]
        for k in sorted(props, key=lambda s: s.encode("ascii")):
            _walk_events(a, props[k], k, k, tags, edges)
        mesh = props.get("Mesh")
        if not (isinstance(mesh, dict) and mesh.get("$asset") in anims):
            continue
        sequences = anims[mesh["$asset"]].get("sequences", {})
        for sk in SEQUENCE_KEYS:
            names = props.get(sk)
            if not isinstance(names, list):
                continue
            for i, seq_name in enumerate(names):
                seq = sequences.get(seq_name) if isinstance(seq_name, str) else None
                if seq is None:
                    continue
                for n in seq.get("notifies", []):
                    event = n.get("props", {}).get("EventName")
                    if not _is_name(event):
                        continue
                    edges.append({"from": a["id"], "via": f"{sk}[{i}]", "event": event,
                                  "to": list(tags.get(event.lower(), [])), "kind": "notify", "seq": seq_name,
                                  "time": notify_seconds(n["time"], seq["frames"], seq["rate"]),
                                  "notify": n.get("object", "")})
    return edges


def derive_event_edges(gameplay: dict) -> list[dict]:
    if minor_of(gameplay.get("formatVersion", "")) >= 1:
        return _derive_event_edges_11(gameplay)
    return _derive_event_edges_10(gameplay)


def derive_mission_tree(gameplay: dict) -> list[dict]:
    by_id = {a["id"]: a for a in all_actors(gameplay)}
    v11 = minor_of(gameplay.get("formatVersion", "")) >= 1
    missions = []
    for a in gameplay["actors"]:
        if bare_class(a["class"]) != "SMission":
            continue
        props = a["props"]
        entry: dict = {"mission": a["id"], "objectives": []}
        if isinstance(props.get("GameMode"), int):
            entry["gameMode"] = props["GameMode"]
        if isinstance(props.get("MinimumObjectives"), int):
            entry["minimumObjectives"] = props["MinimumObjectives"]
        if v11:
            own = _ref_list(props.get("Triggers"))
            if own:
                entry["triggers"] = own
        for oid in _ref_list(props.get("Objectives")):
            obj = by_id.get(oid)
            triggers = _ref_list(obj["props"].get("Triggers")) if obj is not None else []
            entry["objectives"].append({"objective": oid, "triggers": triggers})
        missions.append(entry)
    return missions


def with_derived(gameplay: dict) -> dict:
    """gameplay with tagIndex, eventEdges and missionTree filled in from the actors."""
    out = dict(gameplay)
    out["tagIndex"] = derive_tag_index(gameplay)
    out["eventEdges"] = derive_event_edges(gameplay)
    out["missionTree"] = derive_mission_tree(gameplay)
    return out


def ge_active(mask: int, activation: int) -> bool:
    return mask == 0 or (mask & activation) != 0


def write_svmap(root: Path, *, map_id: str, title: str, source: dict, gameplay: dict, traversal: dict,
                collision: bytes, render: dict[str, bytes], requires: list | None = None,
                files: dict[str, bytes] | None = None, format_version: str = FORMAT_VERSION) -> dict:
    """Write a complete SVMap folder (replacing what is there) and return its manifest.

    render: files under render/ by relative path; files: any other files by path from the map root (sim/..., ui/...).
    """
    root = Path(root)
    if root.exists():
        for child in ("sim", "render", "audio", "ui", "manifest.json"):
            p = root / child
            if p.is_dir():
                shutil.rmtree(p)
            elif p.exists():
                p.unlink()
    (root / "sim").mkdir(parents=True, exist_ok=True)
    (root / "sim" / "gameplay.json").write_bytes(canonical.dump_file(gameplay))
    (root / "sim" / "traversal.json").write_bytes(canonical.dump_file(traversal))
    (root / "sim" / "collision.bin").write_bytes(collision)
    for rel, data in sorted(render.items()):
        p = root / "render" / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)
    for rel, data in sorted((files or {}).items()):
        p = root / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)
    files_, hashes = canonical.hash_map(root)
    manifest = {
        "format": "svmap",
        "formatVersion": format_version,
        "map": map_id,
        "title": title,
        "source": source,
        "files": files_,
        "hashes": hashes,
        "requires": requires or [],
    }
    (root / "manifest.json").write_bytes(canonical.dump_file(manifest))
    return manifest
