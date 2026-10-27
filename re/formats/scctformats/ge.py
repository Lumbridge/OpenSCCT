"""Traversal GEs ("geometric events") stored in a map's Level export.

A GE is not an actor. The level owns a GE partition (in memory at ULevel+0x39e8) that holds every
ledge grab, pipe, zip line, ladder, fence, pole and hand-over-hand bar of the map, a flat index
list and a k-d search tree over them. The Level export serializes it after the usual level fields.

Level export layout, package version 300 (all fields little-endian, "compact" = FCompactIndex,
"objref" = compact object reference, "name" = SCCT paired name reference):

    name        None                  (empty tagged property list)
    i32         ActorsNum, i32 ActorsMax, ActorsNum x objref
    FURL        Protocol, Host, Map, Portal (FString), compact nOp + nOp FString, i32 Port, i32 Valid
    i32 hasA,   objref ModelA if hasA (level+0x144)
    i32 hasB,   objref ModelB if hasB (level+0x14c); the loaded Model is A when present, else B
    f32         ApproxTime
    objref      FirstDeleted
    16 x objref TextBlocks
    TMap        TravelInfo: compact n, n x (FString key, FString value)      (version > 62)
    GE partition                                                           (version > 123)

GE partition:

    i32         PartitionVersion (4 in every shipped map)
    f32 x 3     BoundsMin, f32 x 3 BoundsMax (exact box of the first two points of every GE)
    compact n   then n GE records
    compact m   then m x i32: GE indices referenced by the tree leaves
    tree        pre-order nodes (see below)

GE record:

    i32 Kind    POLE 1, FENCE 2, HOH 4, LG 8, LADDER 16, PIPE 32, ZL 64
    i32 Flags   bit 0x2 is set by the editor when it adds a GE (its selection flag); 0 otherwise
    i32 Platform  (version >= 170) 0 common
    u8  ID        (version >= 174) 0 = always active, else active while ID & GEActivationMask
    FVector x 2 (POLE, FENCE, HOH, LG, ZL) or x 3 (LADDER, PIPE)

Tree node: i32 type. 1 = empty child (nothing follows). 2, 4, 8 = split on X, Y, Z: f32 split value,
then the two children. 16 = leaf: i32 first, i32 end, a half-open range into the index list.

Sources: the editor's GE serializer (ChaosTheory_Editor.exe 0x113059b0), GE array 0x11306f60,
partition 0x11308370, tree nodes 0x113092e0, ULevel::Serialize 0x111293d0..0x11129704, checked
against all 83 cooked maps (every Level export is consumed to its last byte).
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Tuple

from ._env import Package, PackageError, Reader, game_root, out_dir

KINDS: Dict[int, Tuple[str, int]] = {
    1: ("POLE", 2), 2: ("FENCE", 2), 4: ("HOH", 2), 8: ("LG", 2), 16: ("LADDER", 3), 32: ("PIPE", 3), 64: ("ZL", 2),
}
KIND_CODES = {name: code for code, (name, _) in KINDS.items()}
GE_FLAG_EDITOR_ADDED = 0x2

NODE_EMPTY = 1
NODE_LEAF = 16
NODE_AXES = {2: "X", 4: "Y", 8: "Z"}

VER_GE_PLATFORM = 0xAA
VER_GE_ID = 0xAE
VER_TRAVELINFO = 0x3E
VER_GE_PARTITION = 0x7B
PARTITION_VERSION = 4


class GEFormatError(PackageError):
    pass


@dataclass
class GE:
    index: int
    code: int
    flags: int
    platform: int
    id: int
    points: List[Tuple[float, float, float]]

    @property
    def kind(self) -> str:
        return KINDS[self.code][0]

    def active(self, mask: int) -> bool:
        """Present in game: no ID, or an ID sharing a bit with LevelInfo.GEActivationMask."""
        return self.id == 0 or (self.id & mask) != 0

    def to_json(self, mask: Optional[int] = None) -> dict:
        d = {"index": self.index, "kind": self.kind, "code": self.code, "id": self.id, "platform": self.platform,
             "flags": self.flags, "points": [list(p) for p in self.points]}
        if mask is not None:
            d["active_at_start"] = self.active(mask)
        return d


@dataclass
class Node:
    type: int
    split: Optional[float] = None
    left: Optional["Node"] = None
    right: Optional["Node"] = None
    first: int = 0
    end: int = 0

    @property
    def axis(self) -> Optional[str]:
        return NODE_AXES.get(self.type)


@dataclass
class GEPartition:
    version: int
    bounds_min: Tuple[float, float, float]
    bounds_max: Tuple[float, float, float]
    ges: List[GE]
    indices: List[int]
    tree: Optional[Node]

    def counts(self) -> Dict[str, int]:
        c = Counter(g.kind for g in self.ges)
        return {name: c.get(name, 0) for name, _ in KINDS.values()}

    def tree_stats(self) -> Dict[str, int]:
        c: Counter = Counter()
        depth = 0
        stack = [(self.tree, 1)]
        while stack:
            n, d = stack.pop()
            if n is None:
                c["empty"] += 1
                continue
            depth = max(depth, d)
            if n.type == NODE_LEAF:
                c["leaf"] += 1
            else:
                c["split_" + (n.axis or "?")] += 1
                stack.append((n.left, d + 1))
                stack.append((n.right, d + 1))
        out = dict(sorted(c.items()))
        out["depth"] = depth
        return out


@dataclass
class LevelHeader:
    actors_num: int
    actors_max: int
    actors: List[int]
    url: dict
    model_a: int
    model_b: int
    approx_time: float
    first_deleted: int
    text_blocks: List[int]
    travel: List[Tuple[str, str]] = field(default_factory=list)


# ---- reading ---------------------------------------------------------------------------------

def read_ge(r: Reader, index: int, version: int = 300) -> GE:
    code = r.i32()
    if code not in KINDS:
        raise GEFormatError(f"GE {index}: unknown kind {code} at {r.p - 4}")
    flags = r.i32()
    platform = r.i32() if version >= VER_GE_PLATFORM else 0
    gid = r.u8() if version >= VER_GE_ID else 0
    pts = [tuple(r.vec3()) for _ in range(KINDS[code][1])]
    return GE(index, code, flags, platform, gid, pts)


def read_tree(r: Reader, index_count: int) -> Optional[Node]:
    """Pre-order k-d tree. Iterative so a deep tree cannot hit the recursion limit."""
    root_holder: List[Optional[Node]] = [None]
    # Each pending slot is (parent node or None, which child): filled in pre-order.
    pending: List[Tuple[Optional[Node], str]] = [(None, "root")]
    while pending:
        parent, side = pending.pop()
        t = r.i32()
        if t == NODE_EMPTY:
            node = None
        elif t == NODE_LEAF:
            first, end = r.i32(), r.i32()
            if not (0 <= first <= end <= index_count):
                raise GEFormatError(f"tree leaf range {first}..{end} outside the index list ({index_count})")
            node = Node(t, first=first, end=end)
        elif t in NODE_AXES:
            node = Node(t, split=r.f32())
            pending.append((node, "right"))
            pending.append((node, "left"))
        else:
            raise GEFormatError(f"unknown tree node type {t} at {r.p - 4}")
        if parent is None:
            root_holder[0] = node
        elif side == "left":
            parent.left = node
        else:
            parent.right = node
    return root_holder[0]


def read_partition(r: Reader, version: int = 300) -> GEPartition:
    pver = r.i32()
    if pver < PARTITION_VERSION:
        # The editor loads older partitions with another routine (0x11307220) and rebuilds the tree.
        raise GEFormatError(f"GE partition version {pver} is older than {PARTITION_VERSION}: not supported")
    bmin = tuple(r.vec3())
    bmax = tuple(r.vec3())
    n = r.compact()
    if n < 0 or n * 37 > r.remaining() + 37:
        raise GEFormatError(f"implausible GE count {n}")
    ges = [read_ge(r, i, version) for i in range(n)]
    m = r.compact()
    if m < 0 or m * 4 > r.remaining():
        raise GEFormatError(f"implausible GE index count {m}")
    indices = [r.i32() for _ in range(m)]
    bad = [i for i in indices if not 0 <= i < n]
    if bad:
        raise GEFormatError(f"{len(bad)} tree indices outside the GE list, e.g. {bad[0]}")
    tree = read_tree(r, m)
    return GEPartition(pver, bmin, bmax, ges, indices, tree)


def read_level(r: Reader, version: int = 300) -> Tuple[LevelHeader, Optional[GEPartition]]:
    """Read a Level export from the start of its data (Level objects carry no state frame)."""
    if r.name() != "None":
        raise GEFormatError("Level export has tagged properties: not expected")
    num, mx = r.i32(), r.i32()
    if num < 0 or num > r.remaining():
        raise GEFormatError(f"implausible actor count {num}")
    actors = [r.objref() for _ in range(num)]
    url = {"protocol": r.fstring(), "host": r.fstring(), "map": r.fstring(), "portal": r.fstring()}
    nop = r.compact()
    url["op"] = [r.fstring() for _ in range(nop)]
    url["port"] = r.i32()
    url["valid"] = r.i32()
    model_a = r.objref() if r.i32() else 0
    model_b = r.objref() if r.i32() else 0
    approx_time = r.f32()
    first_deleted = r.objref()
    text_blocks = [r.objref() for _ in range(16)]
    travel = []
    if version > VER_TRAVELINFO:
        for _ in range(r.compact()):
            travel.append((r.fstring(), r.fstring()))
    hdr = LevelHeader(num, mx, actors, url, model_a, model_b, approx_time, first_deleted, text_blocks, travel)
    part = read_partition(r, version) if version > VER_GE_PARTITION else None
    if r.remaining() != 0:
        raise GEFormatError(f"{r.remaining()} bytes left after the GE partition")
    return hdr, part


# ---- checks ----------------------------------------------------------------------------------

def check_tree(part: GEPartition) -> Dict[str, int]:
    """Consistency of the search tree with the GE list: every GE reachable from a leaf, every leaf
    GE inside its split half-spaces (with a 1 uu tolerance, measured on any of its points), and
    the bounds box containing the first two points of every GE. Returns counts; 'violations' should be 0."""
    axis_i = {"X": 0, "Y": 1, "Z": 2}
    seen = set()
    violations = 0
    leaves = 0
    stack: List[Tuple[Optional[Node], List[Tuple[int, float, bool]]]] = [(part.tree, [])]
    while stack:
        n, cons = stack.pop()
        if n is None:
            continue
        if n.type == NODE_LEAF:
            leaves += 1
            for gi in part.indices[n.first:n.end]:
                seen.add(gi)
                g = part.ges[gi]
                for ax, s, is_left in cons:
                    lo = min(p[ax] for p in g.points)
                    hi = max(p[ax] for p in g.points)
                    if is_left and lo > s + 1.0:
                        violations += 1
                    if not is_left and hi < s - 1.0:
                        violations += 1
            continue
        a = axis_i[n.axis]
        stack.append((n.left, cons + [(a, n.split, True)]))
        stack.append((n.right, cons + [(a, n.split, False)]))
    outside = 0
    for g in part.ges:
        for p in g.points[:2]:   # the stored box covers the first two points only (not a pipe's side point)
            if any(p[i] < part.bounds_min[i] - 0.01 or p[i] > part.bounds_max[i] + 0.01 for i in range(3)):
                outside += 1
    return {"leaves": leaves, "ges_in_tree": len(seen), "ges": len(part.ges), "violations": violations,
            "points_outside_bounds": outside}


# ---- map level helpers -----------------------------------------------------------------------

def _actor_props(pkg: Package, e):
    from scctpkg.properties import read_properties, skip_state_frame
    r = pkg.export_reader(e)
    skip_state_frame(r, e.flags)
    return read_properties(r)


def activation_mask(pkg: Package, default: Optional[int] = None) -> Tuple[Optional[int], bool]:
    """LevelInfo.GEActivationMask: (value, stored). A map stores it only when it differs from the
    class default, so an absent value takes `default`."""
    for e in pkg.exports_of_class("LevelInfo"):
        if e.size <= 0:
            continue
        props = _actor_props(pkg, e)
        if "GEActivationMask" in props:
            return int(props["GEActivationMask"]), True
        return default, False
    return default, False


def ge_triggers(pkg: Package) -> List[dict]:
    """SGETrigger actors (they flip GEActivationMask bits when triggered)."""
    out = []
    for e in pkg.exports:
        if e.class_name != "SGETrigger" or e.size <= 0:
            continue
        try:
            p = _actor_props(pkg, e)
        except Exception as ex:  # keep going: a bad actor must not hide the GE list
            out.append({"name": e.name, "error": str(ex)})
            continue
        out.append({"name": e.name, "tag": str(p.get("Tag", "")), "ge_mask": p.get("GEMask"),
                    "toggle": p.get("bToggle"), "location": list(p["Location"]) if "Location" in p else None})
    return out


def level_export(pkg: Package):
    lv = [e for e in pkg.exports if e.class_name == "Level" and e.size > 0]
    if len(lv) != 1:
        raise GEFormatError(f"{pkg.path.name}: {len(lv)} Level exports")
    return lv[0]


def decode_package(pkg: Package, mask_default: Optional[int] = None) -> dict:
    lv = level_export(pkg)
    hdr, part = read_level(pkg.export_reader(lv), pkg.version)
    mask, stored = activation_mask(pkg, mask_default)
    out = {
        "map": pkg.path.stem,
        "package_version": pkg.version,
        "level_export": lv.name,
        "actors": hdr.actors_num,
        "url_map": hdr.url["map"],
        "model_a": pkg.object_name(hdr.model_a) if hdr.model_a else None,
        "model_b": pkg.object_name(hdr.model_b) if hdr.model_b else None,
        "activation_mask": mask,
        "activation_mask_stored": stored,
        "ge_triggers": ge_triggers(pkg),
    }
    if part is None:
        out["ges"] = []
        return out
    out.update({
        "partition_version": part.version,
        "bounds": [list(part.bounds_min), list(part.bounds_max)],
        "counts": part.counts(),
        "ids": dict(sorted(Counter(str(g.id) for g in part.ges).items())),
        "tree": part.tree_stats(),
        "tree_check": check_tree(part),
        "index_count": len(part.indices),
        "ges": [g.to_json(mask) for g in part.ges],
    })
    return out


# ---- writing (synthetic data, round trips) ---------------------------------------------------

def write_ge(w, g: GE, version: int = 300) -> None:
    w.i32(g.code).i32(g.flags)
    if version >= VER_GE_PLATFORM:
        w.i32(g.platform)
    if version >= VER_GE_ID:
        w.u8(g.id)
    for p in g.points:
        w.vec3(p)


def write_tree(w, n: Optional[Node]) -> None:
    stack = [n]
    while stack:
        n = stack.pop()
        if n is None:
            w.i32(NODE_EMPTY)
        elif n.type == NODE_LEAF:
            w.i32(NODE_LEAF).i32(n.first).i32(n.end)
        else:
            w.i32(n.type).f32(n.split)
            stack.append(n.right)
            stack.append(n.left)


def write_partition(w, part: GEPartition, version: int = 300) -> None:
    w.i32(part.version).vec3(part.bounds_min).vec3(part.bounds_max)
    w.compact(len(part.ges))
    for g in part.ges:
        write_ge(w, g, version)
    w.compact(len(part.indices))
    for i in part.indices:
        w.i32(i)
    write_tree(w, part.tree)


# ---- command line ----------------------------------------------------------------------------

def _map_paths(names: List[str], sub: str) -> List[Path]:
    root = game_root() / "Packages" / sub
    if names == ["all"]:
        return sorted(root.glob("*.sdc"))
    out = []
    for n in names:
        p = Path(n)
        out.append(p if p.suffix.lower() == ".sdc" and p.exists() else root / f"{n}.sdc")
    return out


def _default_mask() -> Optional[int]:
    """LevelInfo.GEActivationMask class default from the class-defaults output, if it exists."""
    try:
        from .classdefaults import load_output
        d = load_output()
        if d:
            v = d.get("Engine.LevelInfo", {}).get("props", {}).get("GEActivationMask")
            return int(v) if v is not None else 0
    except Exception:
        pass
    return None


def census_table(rows: List[dict]) -> str:
    kinds = [name for name, _ in KINDS.values()]
    lines = ["| Map | GEs | " + " | ".join(kinds) + " | IDs != 0 | Mask |",
             "|---|---:|" + "---:|" * len(kinds) + "---:|---:|"]
    tot = Counter()
    for d in rows:
        c = d.get("counts", {k: 0 for k in kinds})
        nid = sum(1 for g in d["ges"] if g["id"])
        tot.update(c)
        tot["_all"] += len(d["ges"])
        tot["_id"] += nid
        mask = d["activation_mask"]
        mask_s = "" if mask is None else (str(mask) + ("" if d["activation_mask_stored"] else " (default)"))
        lines.append(f"| {d['map']} | {len(d['ges'])} | " + " | ".join(str(c.get(k, 0)) for k in kinds)
                     + f" | {nid} | {mask_s} |")
    lines.append(f"| **all** | {tot['_all']} | " + " | ".join(str(tot[k]) for k in kinds) + f" | {tot['_id']} | |")
    return "\n".join(lines)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="scctformats.ge", description=__doc__.split("\n")[0])
    ap.add_argument("maps", nargs="+", help="map names, .sdc paths, or 'all'")
    ap.add_argument("--sub", default="Maps", help="Packages subfolder (Maps or MapsEd)")
    ap.add_argument("--out", help="output folder (default local/formats/ge, or ge_<sub> for other folders)")
    ap.add_argument("--census", action="store_true", help="print a Markdown census table")
    ap.add_argument("--compare", help="other Packages subfolder to compare GE lists with (e.g. MapsEd)")
    args = ap.parse_args(argv)
    out = Path(args.out) if args.out else out_dir("ge" if args.sub == "Maps" else f"ge_{args.sub}")
    out.mkdir(parents=True, exist_ok=True)
    mask_default = _default_mask()
    rows, failed = [], []
    for path in _map_paths(args.maps, args.sub):
        try:
            pkg = Package.load(path)
            d = decode_package(pkg, mask_default)
        except Exception as ex:
            failed.append((path.stem, str(ex)))
            print(f"{path.stem}: FAILED {ex}", file=sys.stderr)
            continue
        d["source"] = f"Packages/{args.sub}/{path.name}"
        (out / f"{path.stem}.json").write_text(json.dumps(d, indent=1), encoding="utf-8")
        rows.append(d)
        tc = d.get("tree_check", {})
        print(f"{d['map']}: {len(d['ges'])} GEs {d.get('counts', {})} mask {d['activation_mask']} "
              f"tree {tc.get('ges_in_tree')}/{tc.get('ges')} violations {tc.get('violations')}")
        if args.compare:
            other = game_root() / "Packages" / args.compare / path.name
            if other.exists():
                try:
                    o = decode_package(Package.load(other), mask_default)
                    same = [g["points"] for g in o["ges"]] == [g["points"] for g in d["ges"]] and \
                        [(g["code"], g["id"]) for g in o["ges"]] == [(g["code"], g["id"]) for g in d["ges"]]
                    print(f"  vs {args.compare}: {len(o['ges'])} GEs, identical={same}")
                    d["compare"] = {"folder": args.compare, "ges": len(o["ges"]), "identical": same}
                except Exception as ex:
                    print(f"  vs {args.compare}: FAILED {ex}")
                    d["compare"] = {"folder": args.compare, "error": str(ex)}
    if args.census:
        print(census_table(rows))
    summary = {"decoded": len(rows), "failed": failed,
               "compare": {d["map"]: d["compare"] for d in rows if "compare" in d}}
    (out / "_summary.json").write_text(json.dumps(summary, indent=1), encoding="utf-8")
    print(f"{len(rows)} decoded, {len(failed)} failed -> {out}")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
