"""RE+ conveyor belts lifted into native Svm.Conveyor actors (spec/additions/conveyors.md, "Converted RE+ belts").

RE+ 2.7 compiles four classes into a map that has belts (ConveyorBelt, ConveyorMotor, ConveyorItem, ConveyorRoller;
RE+ Reloaded.Editor/ConveyorModel.h). Only ConveyorBelt is placed: each machine's copy spawns the motor, rollers and
items at run time. A belt's pivot is its start on the riding surface, its rotation the run's direction and slope
(vector(Rotation)), and Length, Width and Speed size and drive it. NextBelt links belts into a chain; the chain's
first belt (no belt leads onto it, or in a closed loop the one whose name sorts first) runs the items of the whole
chain from its own ItemMeshes, ItemSpacing, FadeDistance and ItemScale, and items shrink away only at the chain's two
ends. Items cross from the end of one belt to the point of the next belt's centre line nearest it (Join), at the
next belt's speed. Every belt carries walking pawns standing on it (feet within its length and Width / 2 + radius / 2
across) at its own Speed, by Pawn.Move: up 18, along, down. Nothing replicates and nothing switches a belt.

The conversion keeps all of that in one or more Svm.Conveyor actors per chain:

- A chain is cut into runs of belts that join end to start (within JOIN_GAP) and share Speed, Width and
  bCarryPlayers; each run is one conveyor whose path goes through its belts' starts and ends (sharp corners: RE+
  curves are already chords). Where items cross a gap between two belts, a conveyor of its own carries them over it
  (no riders: RE+ carries a pawn there by the belt under it). Runs of one chain hand items on through Next, so items
  only appear and vanish at the chain's ends, as in RE+.
- Look is CL_Driver: the map's own meshes are what is seen and stood on. A belt that RE+ draws (its ConveyorFrame mesh,
  not hidden) stays in the render and the collision as a static mesh; only its spinning rollers are not drawn.
- Every run takes its chain's item settings, so the items start spread along the whole chain.
- The conveyors carry no dropped objects (RE+ moved only pawns), and their Tag is their own id (RE+ belts take no
  events).
- AerTreadScroll (AeroD's map script that pans the travelator and escalator treads with a TexPanner in Skins[Slot])
  becomes ScrollTargets of the conveyor under each tagged mesh, scrolling only that slot's material, at the rate the
  panner had (UnitsPerTile = -Speed / PanRate: OpenSCCT slides the texture by -phase / UnitsPerTile, a TexPanner by
  +PanRate x time). The script stays a map script of its own in the editor's handoff.

The belts and the tread script are marked lifted (not written); their runtime classes too, should a map place one.
"""

from __future__ import annotations

import math
import struct
import zlib
from dataclasses import dataclass, field, replace
from typing import Any, Dict, List, Optional, Set, Tuple

from ..sources.typedprops import Ref
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
VERSION_ONE_CHAIN = 16
LATER_CHAIN = 64
# RE+ items turn by Rand(2001) - 1000 rotator units; Svm.Conveyor by [0, ItemYawJitter).
ITEM_YAW_JITTER = 2001
# AerTreadScroll pans along PanDirection (Pitch 0, Yaw 16384): the texture's V.
TREAD_AXIS = 1
# A tagged tread mesh scrolls with the conveyor whose path passes nearest its pivot, within this distance.
TREAD_REACH = 512.0
ROT = 2.0 * math.pi / 65536.0

Vec = Tuple[float, float, float]


@dataclass
class Belt:
    info: ActorInfo
    start: Vec
    end: Vec
    length: float
    width: float
    speed: float
    carry: bool
    next_ref: int


@dataclass
class Run:
    """Belts (or parts of belts) one conveyor stands for. `kind` is "belt" (items and riders), "start" (the part of a
    belt before the point items come onto it: riders only) or "join" (items crossing a gap onto the next belt: no
    riders)."""
    belts: List[Belt]
    kind: str

    @property
    def items(self) -> bool:
        return self.kind != "start"


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


def _num(v: Any, default: float) -> float:
    return float(v) if isinstance(v, (int, float)) and not isinstance(v, bool) else default


def _elements(value: Any) -> Dict[int, Any]:
    if isinstance(value, dict):
        return {int(k): v for k, v in value.items() if isinstance(k, int)}
    if isinstance(value, list):
        return {i: v for i, v in enumerate(value) if v is not None}
    return {}


def _vec(v: Any) -> Vec:
    v = v if isinstance(v, dict) else {}
    return (float(v.get("X", 0.0) or 0.0), float(v.get("Y", 0.0) or 0.0), float(v.get("Z", 0.0) or 0.0))


def _direction(rot: Any) -> Vec:
    """UnrealScript vector(Rotation): the X axis of the rotator."""
    rot = rot if isinstance(rot, dict) else {}
    pitch = int(rot.get("Pitch", 0) or 0) * ROT
    yaw = int(rot.get("Yaw", 0) or 0) * ROT
    return (math.cos(pitch) * math.cos(yaw), math.cos(pitch) * math.sin(yaw), math.sin(pitch))


def _add(a: Vec, b: Vec, s: float = 1.0) -> Vec:
    return (a[0] + b[0] * s, a[1] + b[1] * s, a[2] + b[2] * s)


def _dist(a: Vec, b: Vec) -> float:
    return math.dist(a, b)


class _Values:
    """Property values of the map's own RE+ actors: stored, else the class default, by name ignoring case (a map's own
    class can store `speed`). A property its class declarations do not cover (an editor copy whose class is older than
    its belts) keeps only its tag type and bytes: numbers and object references are read from those, and either way
    the conversion notes it once per property."""

    def __init__(self, src: MapSource) -> None:
        self.src = src
        self._noted: Set[str] = set()

    def get(self, info: ActorInfo, name: str, default: Any = None) -> Any:
        low = name.lower()
        for k, v in info.props.items():
            if k.lower() == low:
                return self._decoded(info.name, k, v, default)
        for k, v in (self.src.db.localized.get(self.src.db.find(info.cls) or "", {}) or {}).items():
            if k.lower() == low:
                return self._decoded(_bare(info.cls), k, v, default)
        return default

    def num(self, info: ActorInfo, name: str, default: float) -> float:
        return _num(self.get(info, name), default)

    def class_num(self, cls: str, name: str, default: float) -> float:
        for k, v in (self.src.db.localized.get(self.src.db.find(cls) or "", {}) or {}).items():
            if k.lower() == name.lower():
                return _num(self._decoded(_bare(cls), k, v, None), default)
        return default

    def _decoded(self, owner: str, name: str, v: Any, default: Any) -> Any:
        if isinstance(v, dict) and "_unknown_type" in v:
            out = self._raw(owner, name, v)
            return default if out is None else out
        if isinstance(v, dict) and v and all(isinstance(k, int) for k in v):
            return {k: self._decoded(owner, f"{name}[{k}]", x, None) for k, x in v.items()}
        return v

    def _raw(self, owner: str, name: str, v: dict) -> Any:
        from scctpkg.package import read_compact  # noqa: PLC0415

        kind = str(v.get("_unknown_type"))
        try:
            raw = bytes.fromhex(str(v.get("_raw", "")))
        except ValueError:
            raw = b""
        out: Any = None
        if kind == "FloatProperty" and len(raw) == 4:
            out = struct.unpack("<f", raw)[0]
        elif kind == "IntProperty" and len(raw) == 4:
            out = struct.unpack("<i", raw)[0]
        elif kind == "ByteProperty" and len(raw) == 1:
            out = raw[0]
        elif kind == "ObjectProperty" and raw:
            try:
                ref, end = read_compact(raw, 0)
            except IndexError:
                ref, end = 0, -1
            if end == len(raw):
                path = self.src.db.universe.path(self.src.pkg, ref) or ""
                out = Ref(ref, path, False, self.src.pkg.path.stem)
        # Once per property: every belt of the map has the same class.
        key = name.split("[", 1)[0].lower()
        if key not in self._noted:
            self._noted.add(key)
            what = "read from its tag's bytes" if out is not None else "the default is used"
            self.src.notes.add("conveyor-property", f"{owner}.{name}: not declared by its class ({kind} tag); {what}")
        return out


def _chain_cap(values: _Values, cls: str) -> Tuple[int, int]:
    """(how many belts one chain runs items over, class version): the map's own class's Seg array, else by its
    Version."""
    version = int(values.class_num(cls, "Version", 2))
    prop = values.src.db.prop(cls, "Seg")
    dim = prop.data.get("array_dim", 0) if prop is not None else 0
    if dim > 0:
        return int(dim), version
    return (VERSION_ONE_CHAIN if version <= 1 else LATER_CHAIN), version


def _belt(values: _Values, info: ActorInfo) -> Belt:
    start = _vec(values.get(info, "Location"))
    length = values.num(info, "Length", 600.0)
    end = _add(start, _direction(values.get(info, "Rotation")), length)
    nxt = values.get(info, "NextBelt")
    own_next = nxt.ref if isinstance(nxt, Ref) and nxt.ref > 0 and values.src.own(nxt) else 0
    return Belt(info, start, end, length, values.num(info, "Width", 120.0), values.num(info, "Speed", 150.0),
                bool(values.get(info, "bCarryPlayers", True)), own_next)


# ---- chains ---------------------------------------------------------------------------------------

@dataclass
class Chain:
    belts: List[Belt]
    loop: bool
    leads: bool       # RE+ runs items over it (its first belt leads)
    cap: int


def _chains(values: _Values, belts: Dict[int, Belt]) -> List[Chain]:
    """RE+'s chains (ConveyorBelt.Leads and BuildChain), then the belts no chain reached (past a chain's cap, or in a
    loop no belt leads), as chains without items. Each belt is in one chain only: closed loops first, so a belt leading
    into a loop ends where the loop starts; where two chains reach the same belt (a merge), the first one keeps it and
    the other ends there. RE+ runs a chain on round a loop or past a merge (up to its cap); one belt is one conveyor
    here, so the later chain's items stop there."""
    fed = {b.next_ref for b in belts.values() if b.next_ref in belts}
    taken: Set[int] = set()
    out: List[Chain] = []

    def leads(b: Belt, segs: int) -> bool:
        # Leads(): no belt feeds it, or it is the first name round a loop of at most segs + 1 belts.
        if b.info.ref not in fed:
            return True
        first, cur, n = b, belts.get(b.next_ref), 0
        while cur is not None and cur is not b and n < segs:
            if cur.info.name < first.info.name:
                first = cur
            cur, n = belts.get(cur.next_ref), n + 1
        return cur is b and first is b

    def walk(first: Belt, segs: int, version: int) -> Tuple[List[Belt], bool]:
        # BuildChain(): version 1 tests the cap before the way back to the first belt, so a loop of exactly segs belts
        # is no loop; later versions test the loop first.
        seq: List[Belt] = []
        b: Optional[Belt] = first
        while b is not None:
            if version <= 1 and len(seq) >= segs:
                break
            if seq and b is first:
                return seq, True
            if len(seq) >= segs or b.info.ref in taken or any(x is b for x in seq):
                break
            seq.append(b)
            b = belts.get(b.next_ref)
        return seq, False

    def add(first: Belt, segs: int, version: int, items: bool) -> None:
        seq, loop = walk(first, segs, version)
        if seq:
            taken.update(x.info.ref for x in seq)
            out.append(Chain(seq, loop, items, segs))

    caps = {b.info.ref: _chain_cap(values, b.info.cls) for b in belts.values()}
    for b in belts.values():
        if b.info.ref in fed and leads(b, caps[b.info.ref][0]):
            add(b, *caps[b.info.ref], True)
    for b in belts.values():
        if b.info.ref not in fed:
            add(b, *caps[b.info.ref], True)
    # What no chain reached still carries riders: first from where a chain stopped, then loops no belt leads, each
    # from its first name.
    rest = [b for b in belts.values() if b.info.ref not in taken]
    feeders: Dict[int, List[Belt]] = {}
    for b in belts.values():
        feeders.setdefault(b.next_ref, []).append(b)
    for b in rest:
        if b.info.ref not in taken and all(f.info.ref in taken for f in feeders.get(b.info.ref, [])):
            add(b, len(belts), 2, False)
    for b in sorted(rest, key=lambda x: x.info.name):
        if b.info.ref not in taken:
            add(b, len(belts), 2, False)
    return out


def _runs(seq: List[Belt], loop: bool) -> List[Run]:
    """A chain as runs, cut where the next belt does not start at this one's end or carries differently. Items come
    onto a belt from the end of the one before at the nearest point of its centre line (RE+ ConveyorBelt.Join: Entry
    along it, To on it, Gap from the end before): a belt they come onto past its start is cut there, its part before
    carrying riders only, and a gap of 1 uu or more is a join run of its own."""
    runs: List[Run] = []
    for s, b in enumerate(seq):
        before = seq[s - 1] if s > 0 else seq[-1] if loop and len(seq) > 1 else None
        if before is not None and b.length > 0:
            x = tuple((b.end[k] - b.start[k]) / b.length for k in range(3))
            reach = sum((before.end[k] - b.start[k]) * x[k] for k in range(3))
            entry = max(0.0, min(reach, max(b.length - 1.0, 0.0)))
            to = _add(b.start, x, entry)
            if entry > JOIN_GAP:
                runs.append(Run([replace(b, end=to, length=entry)], "start"))
                b = replace(b, start=to, length=b.length - entry)
            gap = _dist(before.end, to)
            if gap >= JOIN_GAP:
                # Items slide straight across at the speed of the belt they come onto.
                runs.append(Run([replace(b, start=before.end, end=to, length=gap, carry=False)], "join"))
        last = runs[-1].belts[-1] if runs and runs[-1].items else None
        if s > 0 and last is not None and _dist(last.end, b.start) <= JOIN_GAP and abs(last.speed - b.speed) < 1e-3 \
                and abs(last.width - b.width) < 0.5 and last.carry == b.carry:
            runs[-1].belts.append(b)
        else:
            runs.append(Run([b], "belt"))
    return runs


# ---- tread scroll -----------------------------------------------------------------------------------

def _segment_distance(p: Vec, a: Vec, b: Vec) -> float:
    ab = (b[0] - a[0], b[1] - a[1], b[2] - a[2])
    ap = (p[0] - a[0], p[1] - a[1], p[2] - a[2])
    den = ab[0] ** 2 + ab[1] ** 2 + ab[2] ** 2
    t = 0.0 if den <= 0 else max(0.0, min(1.0, (ap[0] * ab[0] + ap[1] * ab[1] + ap[2] * ab[2]) / den))
    return _dist(p, _add(a, ab, t))


def _path_distance(p: Vec, path: List[Vec]) -> float:
    return min(_segment_distance(p, path[k], path[k + 1]) for k in range(len(path) - 1))


def _slot_material(values: _Values, info: ActorInfo, slot: int) -> Optional[str]:
    """The material the mesh actor draws in a slot: its Skins entry, else its mesh's section material."""
    src = values.src
    skin = _elements(values.get(info, "Skins")).get(slot)
    if isinstance(skin, Ref) and skin.ref != 0 and skin.path:
        return skin.path
    sm = values.get(info, "StaticMesh")
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


def _tread_targets(values: _Values, conveyors: List[Tuple[dict, List[Vec], float]], lift: ConveyorLift) -> int:
    """AerTreadScroll actors as ScrollTargets; returns the targets added. `conveyors` holds (actor json, world path,
    speed) of the conveyors riders stand on."""
    src = values.src
    added = 0
    for ts in _own(src, (TREAD_SCROLL_CLASS,)):
        ts.category, ts.reason = "lifted", "RE+ map script panning conveyor treads (Svm.Conveyor ScrollTargets)"
        slot = int(values.num(ts, "Slot", 0))
        tread = values.get(ts, "Tread")
        runs = _elements(values.get(ts, "Runs"))
        rates = _elements(values.get(ts, "Rates"))
        treads = _elements(values.get(ts, "Treads"))
        for i, tag in sorted(runs.items()):
            if not tag:
                continue
            rate = _num(rates.get(i), 0.0)
            texture = treads.get(i) if isinstance(treads.get(i), Ref) and treads[i].ref != 0 else tread
            meshes = [a for a in src.actors if a.category in ("render", "gameplay")
                      and str(a.props.get("Tag", "")).lower() == str(tag).lower()
                      and a.props.get("StaticMesh") is not None]
            if rate == 0.0 or not meshes:
                src.notes.add("tread-scroll-run", f"{ts.name}.Runs[{i}]={tag}: no rate or no tagged mesh")
                continue
            for m in meshes:
                at = _vec(m.props.get("Location"))
                best = min(((_path_distance(at, path), actor, speed) for actor, path, speed in conveyors
                            if len(path) > 1), default=None, key=lambda t: t[0])
                if best is None or best[0] > TREAD_REACH:
                    src.notes.add("tread-scroll-run",
                                  f"{ts.name}.Runs[{i}]: no conveyor within {TREAD_REACH:g} of {m.name}")
                    continue
                _, actor, speed = best
                target: Dict[str, Any] = {"Target": m.name, "UnitsPerTile": round(-speed / rate, 4), "Axis": TREAD_AXIS}
                material = _slot_material(values, m, slot)
                if material:
                    target["Material"] = {"$asset": material}
                    if isinstance(texture, Ref) and texture.path and texture.path.lower() != material.lower():
                        src.notes.add("tread-scroll-material",
                                      f"{m.name} slot {slot} draws {material}; the script panned {texture.path} there")
                actor["props"].setdefault("ScrollTargets", []).append(target)
                lift.scroll_targets.add(m.name)
                added += 1
    return added


# ---- the conveyors ------------------------------------------------------------------------------------

def _rel(p: Vec, origin: Vec) -> dict:
    return {"X": round(p[0] - origin[0], 3), "Y": round(p[1] - origin[1], 3), "Z": round(p[2] - origin[2], 3)}


def build_conveyors(src: MapSource) -> ConveyorLift:
    """Svm.Conveyor actors for the map's own RE+ belts; marks the belts, their runtime classes and AerTreadScroll
    lifted."""
    lift = ConveyorLift()
    values = _Values(src)
    infos = _own(src, (BELT_CLASS,))
    for a in _own(src, RUNTIME_CLASSES):
        a.category, a.reason = "lifted", "RE+ conveyor part (spawned by ConveyorBelt at run time)"
    if not infos:
        if _own(src, (TREAD_SCROLL_CLASS,)):
            src.notes.add("tread-scroll-run", "AerTreadScroll without conveyor belts: kept as a map script")
        return lift
    belts = {i.ref: _belt(values, i) for i in infos}
    for i in infos:
        i.category, i.reason = "lifted", "RE+ conveyor belt (Svm.Conveyor)"
        if not bool(values.get(i, "bHidden", False)) and bool(values.get(i, "bShowRollers", True)):
            src.notes.add("conveyor-rollers", f"{i.name}: its frame is kept; the rollers RE+ spins on it are not drawn")
    used = {a.name.lower() for a in src.actors}
    riders: List[Tuple[dict, List[Vec], float]] = []
    item_paths: Dict[str, Ref] = {}
    by_belt: Dict[int, dict] = {}
    merges: List[Tuple[dict, int]] = []
    chains = _chains(values, belts)
    in_loop = {b.info.ref for c in chains if c.loop for b in c.belts}
    for chain in chains:
        head = chain.belts[0].info
        meshes: List[Ref] = []
        if chain.leads and bool(values.get(head, "bShowItems", True)):
            meshes = [m for _, m in sorted(_elements(values.get(head, "ItemMeshes")).items())
                      if isinstance(m, Ref) and m.ref != 0 and m.path and not m.is_class]
        spacing = values.num(head, "ItemSpacing", 140.0)
        runs = _runs(chain.belts, chain.loop)
        carrying = [k for k, r in enumerate(runs) if r.items]
        whole_loop = chain.loop and len(runs) == 1
        made: List[dict] = []
        for run in runs:
            first = run.belts[0]
            name = f"Conveyor_{first.info.name}" + {"belt": "", "start": "_Start", "join": "_Join"}[run.kind]
            while name.lower() in used:
                name += "_"
            used.add(name.lower())
            points = [first.start] + [b.end for b in run.belts]
            if whole_loop and _dist(points[-1], points[0]) <= JOIN_GAP:
                points.pop()
            origin = points[0]
            props: Dict[str, Any] = {
                "Location": {"X": origin[0], "Y": origin[1], "Z": origin[2]},
                "Path": [{"Loc": _rel(p, origin)} for p in points],
                "Width": first.width,
                "Speed": first.speed,
                "bCarryPlayers": first.carry,
                "bCarryObjects": False,
                "Look": LOOK_DRIVER,
                "Tag": name,
            }
            if whole_loop:
                props["bLoop"] = True
            if run.items and meshes and spacing > 0:
                props["ItemMeshes"] = [{"$asset": m.path} for m in meshes]
                props["ItemSpacing"] = spacing
                props["ItemScale"] = values.num(head, "ItemScale", 1.0)
                props["ItemFade"] = values.num(head, "FadeDistance", 60.0)
                # Each conveyor lays out its own items: one seed per conveyor, or every part of a chain repeats.
                props["ItemSeed"] = zlib.crc32(name.encode("utf-8")) & 0x7FFFFFFF
                props["ItemYawJitter"] = ITEM_YAW_JITTER
                for m in meshes:
                    item_paths.setdefault(m.path.lower(), m)
            lifted_from = list(dict.fromkeys(b.info.name for b in run.belts))
            actor = {"id": name, "class": CONVEYOR_CLASS, "props": props,
                     "src": {"export": first.info.ref, "liftedFrom": lifted_from}}
            lift.actors.append(actor)
            made.append(actor)
            if run.kind != "join":
                riders.append((actor, points, first.speed))
            if run.kind == "belt":
                by_belt.setdefault(first.info.ref, actor)
        # Items go on to the chain's next run, round a loop of several runs, or onto the belt the chain ends on (a
        # merge into another chain) while the chain is under its cap. Not into a closed loop: its items never leave
        # it, so the belt leading in would back up for good once the loop is full. RE+ runs no items past a chain's
        # cap, so the belts no chain reached hand nothing on.
        for n, k in enumerate(carrying if chain.leads else []):
            if n + 1 < len(carrying):
                made[k]["props"]["Next"] = {"$actor": made[carrying[n + 1]]["id"]}
            elif chain.loop and not whole_loop and carrying[0] != k:
                made[k]["props"]["Next"] = {"$actor": made[carrying[0]]["id"]}
            elif not chain.loop and len(chain.belts) < chain.cap:
                onto = runs[k].belts[-1].next_ref
                if onto in belts and onto not in in_loop:
                    merges.append((made[k], onto))
    for actor, ref in merges:
        # The run that belt starts, else (a merge into the middle of another chain's run) the run holding it.
        name = belts[ref].info.name
        target = by_belt.get(ref) or next((a for a in by_belt.values() if name in a["src"]["liftedFrom"]), None)
        if target is not None and target is not actor:
            actor["props"]["Next"] = {"$actor": target["id"]}
    scrolls = _tread_targets(values, riders, lift)
    lift.item_meshes = list(item_paths.values())
    lift.stats = {"belts": len(infos), "chains": len(chains), "conveyors": len(lift.actors),
                  "joins": sum(1 for a in lift.actors if a["id"].endswith("_Join")),
                  "withItems": sum(1 for a in lift.actors if "ItemMeshes" in a["props"]), "scrollTargets": scrolls}
    return lift
