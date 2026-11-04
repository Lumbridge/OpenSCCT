"""RE+ conveyor belts lifted into native Svm.Conveyor actors (spec/additions/conveyors.md, "Converted RE+ belts").

RE+ 2.7 compiles four classes into a map that has belts (ConveyorBelt, ConveyorMotor, ConveyorItem, ConveyorRoller;
RE+ Reloaded.Editor/ConveyorModel.h). Only ConveyorBelt is placed: each machine's copy spawns the motor, rollers and
items at run time. A belt's pivot is its start on the riding surface, its rotation the run's direction and slope
(vector(Rotation)), and Length, Width and Speed size and drive it. NextBelt links belts into a chain; the chain's
first belt (no belt leads onto it, or in a closed loop the one whose name sorts first) runs the items of the whole
chain from its own ItemMeshes, ItemSpacing, FadeDistance and ItemScale, and items shrink away only at the chain's two
ends. Every belt carries walking pawns standing on it (feet within its length and Width / 2 + radius / 2 across) at its
own Speed, by Pawn.Move: up 18, along, down. Nothing replicates and nothing switches a belt.

The conversion keeps all of that in one or more Svm.Conveyor actors per chain:

- A chain is cut into runs of belts that join end to start (within JOIN_GAP) and share Speed, Width and
  bCarryPlayers; each run is one conveyor whose path goes through its belts' starts and ends (sharp corners: RE+
  curves are already chords). Runs of one chain hand items on through Next, so items only appear and vanish at the
  chain's ends, as in RE+.
- Look is CL_Driver: the map's own meshes are what is seen and stood on. A belt that RE+ draws (its ConveyorFrame mesh,
  not hidden) stays in the render and the collision as a static mesh; only its spinning rollers are not drawn.
- Every run takes its chain's item settings, so the items start spread along the whole chain.
- The conveyors carry no dropped objects (RE+ moved only pawns), and their Tag is their own id (RE+ belts take no
  events).
- AerTreadScroll (AeroD's map script that pans the travelator and escalator treads with a TexPanner in Skins[Slot])
  becomes ScrollTargets of the conveyor under each tagged mesh, scrolling only that slot's material, at the rate the
  panner had (UnitsPerTile = -Speed / PanRate: OpenSCCT slides the texture by -phase / UnitsPerTile, a TexPanner by
  +PanRate x time).

The belts and the tread script are marked lifted (not written); their runtime classes too, should a map place one.
"""

from __future__ import annotations

import math
import zlib
from dataclasses import dataclass, field, replace
from typing import Any, Dict, List, Optional, Set, Tuple

from ..sources.typedprops import Name, Ref
from .source import ActorInfo, MapSource

CONVEYOR_CLASS = "Svm.Conveyor"
BELT_CLASS = "ConveyorBelt"
RUNTIME_CLASSES = ("ConveyorMotor", "ConveyorItem", "ConveyorRoller")
TREAD_SCROLL_CLASS = "AerTreadScroll"

# EConveyorLook.CL_Driver: nothing built, the map's own meshes are the look and the floor.
LOOK_DRIVER = 1
# Belts whose start lies this close to the end of the belt before them continue one path (RE+ Join treats a gap
# under 1 uu as none; the editor's chains are exact, AeroD's rounded to 0.01).
JOIN_GAP = 1.0
# RE+ chains: Seg[MaxChain] (16 in class version 1, 64 from version 2); read from the map's own class when it can be.
DEFAULT_CHAIN = {1: 16}
LATER_CHAIN = 64
# RE+ items turn by Rand(2001) - 1000 rotator units; Svm.Conveyor by [0, ItemYawJitter).
ITEM_YAW_JITTER = 2001
# AerTreadScroll pans along PanDirection (Pitch 0, Yaw 16384): the texture's V.
TREAD_AXIS = 1
# A tagged tread mesh scrolls with the conveyor whose path passes nearest its pivot, within this distance.
TREAD_REACH = 512.0
ROT = 2.0 * math.pi / 65536.0


@dataclass
class Belt:
    info: ActorInfo
    start: Tuple[float, float, float]
    end: Tuple[float, float, float]
    length: float
    width: float
    speed: float
    carry: bool
    next_ref: int


@dataclass
class ConveyorLift:
    """What the belts became: the Svm.Conveyor actors (gameplay.json entries), the mesh actors whose textures they
    scroll (written as gameplay render instances so the client can move their materials), the item meshes (library
    nodes the client builds lit as actors) and stats."""
    actors: List[dict] = field(default_factory=list)
    scroll_targets: Set[str] = field(default_factory=set)
    item_meshes: List[Ref] = field(default_factory=list)
    stats: dict = field(default_factory=dict)


def _bare(cls: str) -> str:
    return cls.rsplit(".", 1)[-1]


def _own(src: MapSource, names: Tuple[str, ...]) -> List[ActorInfo]:
    return sorted((a for a in src.actors if a.category == "gameplay" and _bare(a.cls) in names
                   and src.db.is_package_class(a.cls, src.pkg.path.stem)), key=lambda a: a.position)


def _value(src: MapSource, info: ActorInfo, name: str, default: Any = None) -> Any:
    """A stored property, else the class default, by name ignoring case (a map's own class can store `speed`)."""
    low = name.lower()
    for k, v in info.props.items():
        if k.lower() == low:
            return default if isinstance(v, dict) and "_unknown_type" in v else v
    for k, v in (src.db.localized.get(src.db.find(info.cls) or "", {}) or {}).items():
        if k.lower() == low:
            return default if isinstance(v, dict) and "_unknown_type" in v else v
    return default


def _num(v: Any, default: float) -> float:
    return float(v) if isinstance(v, (int, float)) and not isinstance(v, bool) else default


def _elements(value: Any) -> Dict[int, Any]:
    if isinstance(value, dict):
        return {int(k): v for k, v in value.items() if isinstance(k, int)}
    if isinstance(value, list):
        return {i: v for i, v in enumerate(value) if v is not None}
    return {}


def _vec(v: Any) -> Tuple[float, float, float]:
    v = v if isinstance(v, dict) else {}
    return (float(v.get("X", 0.0) or 0.0), float(v.get("Y", 0.0) or 0.0), float(v.get("Z", 0.0) or 0.0))


def _direction(rot: Any) -> Tuple[float, float, float]:
    """UnrealScript vector(Rotation): the X axis of the rotator."""
    rot = rot if isinstance(rot, dict) else {}
    pitch = int(rot.get("Pitch", 0) or 0) * ROT
    yaw = int(rot.get("Yaw", 0) or 0) * ROT
    return (math.cos(pitch) * math.cos(yaw), math.cos(pitch) * math.sin(yaw), math.sin(pitch))


def _add(a, b, s=1.0):
    return (a[0] + b[0] * s, a[1] + b[1] * s, a[2] + b[2] * s)


def _dist(a, b) -> float:
    return math.dist(a, b)


def _chain_cap(src: MapSource, cls: str) -> int:
    """How many belts one chain runs items over: the map's own class's Seg array, else by its Version."""
    prop = src.db.prop(cls, "Seg")
    dim = prop.data.get("array_dim", 0) if prop is not None else 0
    if dim > 0:
        return int(dim)
    return DEFAULT_CHAIN.get(int(src.db.default(cls, "Version", 2) or 2), LATER_CHAIN)


def _belt(src: MapSource, info: ActorInfo) -> Belt:
    start = _vec(_value(src, info, "Location"))
    length = _num(_value(src, info, "Length"), 600.0)
    end = _add(start, _direction(_value(src, info, "Rotation")), length)
    nxt = _value(src, info, "NextBelt")
    return Belt(info, start, end, length, _num(_value(src, info, "Width"), 120.0), _num(_value(src, info, "Speed"), 150.0),
                bool(_value(src, info, "bCarryPlayers", True)),
                nxt.ref if isinstance(nxt, Ref) and nxt.ref > 0 and src.own(nxt) else 0)


# ---- chains ---------------------------------------------------------------------------------------

def _chains(src: MapSource, belts: Dict[int, Belt]) -> List[Tuple[List[Belt], bool, bool]]:
    """[(belts in order, closed loop, runs items)]: RE+'s chains (ConveyorBelt.Leads and BuildChain), then the belts no
    chain reached (past a chain's cap), as chains without items. Each belt is in one chain only: where two chains
    reach the same belt (a merge), the first one keeps it and the other ends there."""
    fed = {b.next_ref for b in belts.values() if b.next_ref in belts}
    taken: Set[int] = set()
    out: List[Tuple[List[Belt], bool, bool]] = []

    def walk(first: Belt, cap: int) -> Tuple[List[Belt], bool]:
        seq: List[Belt] = []
        seen: Set[int] = set()
        b: Optional[Belt] = first
        while b is not None:
            if seq and b is first:
                return seq, True
            # RE+ would run a chain that leads into a loop round it again up to the cap; each belt is one conveyor.
            if len(seq) >= cap or b.info.ref in taken or b.info.ref in seen:
                break
            seen.add(b.info.ref)
            seq.append(b)
            b = belts.get(b.next_ref)
        return seq, False

    def loop_leader(b: Belt) -> bool:
        # In a closed loop the belt whose name sorts first leads (string comparison, as the script's).
        names, cur, n = [b.info.name], belts.get(b.next_ref), 0
        while cur is not None and cur is not b and n < 4096:
            names.append(cur.info.name)
            cur, n = belts.get(cur.next_ref), n + 1
        return cur is b and min(names) == b.info.name

    for b in belts.values():
        if b.info.ref in fed and not loop_leader(b):
            continue
        seq, loop = walk(b, _chain_cap(src, b.info.cls))
        taken.update(x.info.ref for x in seq)
        out.append((seq, loop, True))
    # Belts past a chain's cap, or in a loop no belt leads: they still carry riders.
    for b in belts.values():
        if b.info.ref in taken:
            continue
        seq, loop = walk(b, len(belts))
        taken.update(x.info.ref for x in seq)
        out.append((seq, loop, False))
    return out


def _runs(seq: List[Belt], loop: bool) -> List[Tuple[List[Belt], bool]]:
    """A chain as [(belts, carries the chain's items)], cut where the next belt does not start at this one's end or
    carries differently. Items come onto a belt from the end of the one before at the nearest point of its centre line
    (RE+ ConveyorBelt.Join: Entry along it); a belt they come onto past its start is cut there, and the part before
    carries riders only."""
    runs: List[Tuple[List[Belt], bool]] = []
    for s, b in enumerate(seq):
        before = seq[s - 1] if s > 0 else seq[-1] if loop and len(seq) > 1 else None
        if before is not None and b.length > 0:
            x = tuple((b.end[k] - b.start[k]) / b.length for k in range(3))
            entry = max(0.0, min(sum((before.end[k] - b.start[k]) * x[k] for k in range(3)), max(b.length - 1.0, 0.0)))
            if entry > JOIN_GAP:
                cut = _add(b.start, x, entry)
                runs.append(([replace(b, end=cut, length=entry)], False))
                b = replace(b, start=cut, length=b.length - entry)
        last = runs[-1][0][-1] if runs and runs[-1][1] else None
        if s > 0 and last is not None and _dist(last.end, b.start) <= JOIN_GAP and abs(last.speed - b.speed) < 1e-3                 and abs(last.width - b.width) < 0.5 and last.carry == b.carry:
            runs[-1][0].append(b)
        else:
            runs.append(([b], True))
    return runs


# ---- tread scroll -----------------------------------------------------------------------------------

def _segment_distance(p, a, b) -> float:
    ab = (b[0] - a[0], b[1] - a[1], b[2] - a[2])
    ap = (p[0] - a[0], p[1] - a[1], p[2] - a[2])
    den = ab[0] ** 2 + ab[1] ** 2 + ab[2] ** 2
    t = 0.0 if den <= 0 else max(0.0, min(1.0, (ap[0] * ab[0] + ap[1] * ab[1] + ap[2] * ab[2]) / den))
    return _dist(p, _add(a, ab, t))


def _slot_material(src: MapSource, info: ActorInfo, slot: int) -> Optional[str]:
    """The material the mesh actor draws in a slot: its Skins entry, else its mesh's section material."""
    skin = _elements(_value(src, info, "Skins")).get(slot)
    if isinstance(skin, Ref) and skin.ref != 0 and skin.path:
        return skin.path
    sm = _value(src, info, "StaticMesh")
    if not isinstance(sm, Ref) or sm.ref == 0:
        return None
    from scctpkg.staticmesh import read_static_mesh  # noqa: PLC0415

    pkg, e = src.ref_export(sm)
    if pkg is None or e is None:
        return None
    try:
        sections = read_static_mesh(pkg, e).sections
    except Exception as ex:  # noqa: BLE001 - a mesh that does not read leaves the whole mesh scrolling
        src.notes.add("tread-scroll-mesh", f"{info.name}: {ex}")
        return None
    if slot >= len(sections) or sections[slot].material_ref == 0:
        return None
    return src.db.cd.object_path(pkg, sections[slot].material_ref)


def _tread_targets(src: MapSource, conveyors: List[Tuple[dict, List[Tuple[float, float, float]], float]],
                   lift: ConveyorLift) -> int:
    """AerTreadScroll actors as ScrollTargets; returns the targets added. `conveyors` holds (actor json, world path,
    speed)."""
    added = 0
    for ts in _own(src, (TREAD_SCROLL_CLASS,)):
        ts.category, ts.reason = "lifted", "RE+ map script panning conveyor treads (Svm.Conveyor ScrollTargets)"
        slot = int(_num(_value(src, ts, "Slot"), 0))
        tread = _value(src, ts, "Tread")
        runs = _elements(_value(src, ts, "Runs"))
        rates = _elements(_value(src, ts, "Rates"))
        treads = _elements(_value(src, ts, "Treads"))
        for i, tag in sorted(runs.items()):
            if not tag:
                continue
            rate = _num(rates.get(i), 0.0)
            texture = treads.get(i) if isinstance(treads.get(i), Ref) and treads[i].ref != 0 else tread
            meshes = [a for a in src.actors if a.category in ("render", "gameplay")
                      and str(a.props.get("Tag", "")).lower() == str(tag).lower() and a.props.get("StaticMesh") is not None]
            if rate == 0.0 or not meshes:
                src.notes.add("tread-scroll-run", f"{ts.name}.Runs[{i}]={tag}: no rate or no tagged mesh")
                continue
            for m in meshes:
                at = _vec(m.props.get("Location"))
                best = min(((min(_segment_distance(at, path[k], path[k + 1]) for k in range(len(path) - 1)), actor, speed)
                            for actor, path, speed in conveyors if len(path) > 1), default=None, key=lambda t: t[0])
                if best is None or best[0] > TREAD_REACH:
                    src.notes.add("tread-scroll-run", f"{ts.name}.Runs[{i}]: no conveyor within {TREAD_REACH:g} of {m.name}")
                    continue
                _, actor, speed = best
                target: Dict[str, Any] = {"Target": m.name, "UnitsPerTile": round(-speed / rate, 4), "Axis": TREAD_AXIS}
                material = _slot_material(src, m, slot)
                if material:
                    target["Material"] = {"$asset": material}
                    if isinstance(texture, Ref) and texture.path and texture.path.lower() != material.lower():
                        src.notes.add("tread-scroll-material",
                                      f"{m.name} slot {slot} draws {material}; the script panned {texture.path} there")
                actor["props"].setdefault("ScrollTargets", []).append(target)
                actor["src"].setdefault("liftedFrom", []).append(ts.name)
                lift.scroll_targets.add(m.name)
                added += 1
    return added


# ---- the conveyors ------------------------------------------------------------------------------------

def _rel(p, origin) -> dict:
    return {"X": round(p[0] - origin[0], 3), "Y": round(p[1] - origin[1], 3), "Z": round(p[2] - origin[2], 3)}


def build_conveyors(src: MapSource) -> ConveyorLift:
    """Svm.Conveyor actors for the map's own RE+ belts; marks the belts, their runtime classes and AerTreadScroll
    lifted."""
    lift = ConveyorLift()
    infos = _own(src, (BELT_CLASS,))
    for a in _own(src, RUNTIME_CLASSES):
        a.category, a.reason = "lifted", "RE+ conveyor part (spawned by ConveyorBelt at run time)"
    if not infos:
        if _own(src, (TREAD_SCROLL_CLASS,)):
            src.notes.add("tread-scroll-run", "AerTreadScroll without conveyor belts: kept as a map script")
        return lift
    belts = {i.ref: _belt(src, i) for i in infos}
    for i in infos:
        i.category, i.reason = "lifted", "RE+ conveyor belt (Svm.Conveyor)"
    used = {a.name.lower() for a in src.actors}
    placed: List[Tuple[dict, List[Tuple[float, float, float]], float]] = []
    item_paths: Dict[str, Ref] = {}
    by_first: Dict[int, dict] = {}
    links: List[Tuple[dict, int]] = []
    chains = _chains(src, belts)
    for seq, loop, leads in chains:
        head = seq[0].info
        meshes: List[Ref] = []
        if leads and bool(_value(src, head, "bShowItems", True)):
            meshes = [m for _, m in sorted(_elements(_value(src, head, "ItemMeshes")).items())
                      if isinstance(m, Ref) and m.ref != 0 and m.path and not m.is_class]
        spacing = _num(_value(src, head, "ItemSpacing"), 140.0)
        runs = _runs(seq, loop)
        carrying = [k for k, (_, items) in enumerate(runs) if items]
        whole_loop = loop and len(runs) == 1
        for k, (run, items) in enumerate(runs):
            # A belt's part before the point items come onto it is named for the belt's start.
            name = f"Conveyor_{run[0].info.name}" + ("" if items else "_Start")
            while name.lower() in used:
                name += "_"
            used.add(name.lower())
            points = [run[0].start] + [b.end for b in run]
            if whole_loop and _dist(points[-1], points[0]) <= JOIN_GAP:
                points.pop()
            origin = points[0]
            props: Dict[str, Any] = {
                "Location": {"X": origin[0], "Y": origin[1], "Z": origin[2]},
                "Path": [{"Loc": _rel(p, origin)} for p in points],
                "Width": run[0].width,
                "Speed": run[0].speed,
                "bCarryPlayers": run[0].carry,
                "bCarryObjects": False,
                "Look": LOOK_DRIVER,
                "Tag": name,
            }
            if whole_loop:
                props["bLoop"] = True
            if items and meshes and spacing > 0:
                props["ItemMeshes"] = [{"$asset": m.path} for m in meshes]
                props["ItemSpacing"] = spacing
                props["ItemScale"] = _num(_value(src, head, "ItemScale"), 1.0)
                props["ItemFade"] = _num(_value(src, head, "FadeDistance"), 60.0)
                props["ItemSeed"] = zlib.crc32(head.name.encode("utf-8")) & 0x7FFFFFFF
                props["ItemYawJitter"] = ITEM_YAW_JITTER
                for m in meshes:
                    item_paths.setdefault(m.path.lower(), m)
            actor = {"id": name, "class": CONVEYOR_CLASS, "props": props,
                     "src": {"export": run[0].info.ref, "liftedFrom": list(dict.fromkeys(b.info.name for b in run))}}
            lift.actors.append(actor)
            placed.append((actor, points, run[0].speed))
            if not items:
                continue
            by_first.setdefault(run[0].info.ref, actor)
            # Items go on to the chain's next run, round a loop of several runs, or onto the belt the chain ends on
            # (a merge into another chain).
            later = [j for j in carrying if j > k]
            if later:
                links.append((actor, runs[later[0]][0][0].info.ref))
            elif loop and not whole_loop and carrying[0] != k:
                links.append((actor, runs[carrying[0]][0][0].info.ref))
            elif run[-1].next_ref in belts and len(seq) < _chain_cap(src, head.cls):
                links.append((actor, run[-1].next_ref))
    for actor, ref in links:
        target = by_first.get(ref)
        if target is None:
            # A merge into the middle of another chain's run: hand on to the run holding that belt.
            target = next((a for a in by_first.values() if belts[ref].info.name in a["src"]["liftedFrom"]), None)
        if target is not None and target is not actor:
            actor["props"]["Next"] = {"$actor": target["id"]}
    scrolls = _tread_targets(src, placed, lift)
    lift.item_meshes = list(item_paths.values())
    lift.stats = {"belts": len(infos), "chains": len(chains), "conveyors": len(lift.actors),
                  "withItems": sum(1 for a in lift.actors if "ItemMeshes" in a["props"]), "scrollTargets": scrolls}
    return lift
