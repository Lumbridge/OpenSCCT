"""TestRange's two forms side by side: the SVMap tools/testmap/generate.py writes and the .sdc twin the editor built,
converted back by the content pipeline. Used by `twin.py diff`.

What is compared, with the tolerance that counts as the same:
- gameplay actors by id: class, Location (0.5 uu), Rotation (2 units), DrawScale3D (0.001);
- GEs by kind and points (0.5 uu), whatever their ids and order;
- the world: lines traced through each world BSP the way the engine walks it (UModel::LineCheck: the front of a CSG
  node is empty, its back solid), straight down on a 32 uu grid over the hall and level across it at six heights in
  both directions; the first hit (1 uu) and the surface type there;
- the zone of points on a grid, by the name of its ZoneInfo;
- the collision of the gameplay objects, glass and the grating: their colliders' bounds per actor (4 uu: the SVMap
  uses boxes round the meshes' collision, and the box of a pitched camera globe overshoots by 3.5);
- the lights: location (0.5), brightness, radius, zone.

Plain Python; reads only the two folders.
"""

from __future__ import annotations

import json
import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(REPO / "content"))
sys.path.insert(0, str(HERE))

from svmbuild.writers import collision as C  # noqa: E402

import layout as L  # noqa: E402

SLOP = 0.001
NOT_CSG = 0x21


class World:
    """One map's world BSP for line checks and zone lookups, as OpenScct.Physics walks it."""

    def __init__(self, folder: Path):
        self.d = C.read_collision((folder / "sim" / "collision.bin").read_bytes())
        self.world = next(c for c in self.d.colliders if C.KINDS[c.kind] == "worldBsp")
        self.root = self.world.bsp_start if self.world.bsp_count else -1
        self.nodes = self.d.bsp

    def _walk(self, prev, node, start, end, outside):
        while node >= 0:
            n = self.nodes[node]
            nx, ny, nz, w = n.plane
            ds = nx * start[0] + ny * start[1] + nz * start[2] - w
            de = nx * end[0] + ny * end[1] + nz * end[2] - w
            csg = n.tri_count > 0 and (n.node_flags & NOT_CSG) == 0
            if ds > -SLOP and de > -SLOP:
                outside = outside or csg
                node = n.front
                continue
            if ds < SLOP and de < SLOP:
                outside = outside and not csg
                node = n.back
                continue
            k = ds / (de - ds)
            middle = (start[0] + (start[0] - end[0]) * k, start[1] + (start[1] - end[1]) * k, start[2] + (start[2] - end[2]) * k)
            front = ds > 0
            near = (outside or csg) if front else (outside and not csg)
            free, at, hit = self._walk(prev, n.front if front else n.back, start, middle, near)
            if not free:
                return False, at, hit
            outside = (outside and not csg) if front else (outside or csg)
            prev = node
            node = n.back if front else n.front
            start = middle
        return outside, start, prev

    def line(self, start, end):
        """(distance along the line, surface type) of the first hit, or None."""
        if self.root < 0:
            return None
        free, at, node = self._walk(self.root, self.root, start, end, False)
        if free:
            return None
        dist = math.dist(start, at)
        return dist, self._surface(node, at)

    def _surface(self, node, p):
        n = node
        guard = 0
        while n >= 0 and guard <= len(self.nodes):
            rec = self.nodes[n]
            for t in range(rec.tri_start, rec.tri_start + rec.tri_count):
                a, b, c, _, m = self.d.tris[t]
                if _inside(rec.plane[:3], p, self.d.verts[a], self.d.verts[b], self.d.verts[c]):
                    return self.d.materials[m].surface_type
            n = rec.coplanar
            guard += 1
        rec = self.nodes[node]
        return self.d.materials[self.d.tris[rec.tri_start][4]].surface_type if rec.tri_count else None

    def zone(self, p) -> str | None:
        if self.root < 0:
            return None
        node, parent, front = self.root, -1, False
        while node >= 0:
            parent = node
            n = self.nodes[node]
            front = n.plane[0] * p[0] + n.plane[1] * p[1] + n.plane[2] * p[2] - n.plane[3] >= 0
            node = n.front if front else n.back
        z = self.nodes[parent].zone_front if front else self.nodes[parent].zone_back
        return self.d.name_of(self.d.zones[z].actor) if z < len(self.d.zones) else None

    def actor_bounds(self) -> dict[str, list]:
        """Per actor: the union of its colliders' bounds and their kinds."""
        out: dict[str, list] = {}
        for c in self.d.colliders:
            name = self.d.name_of(c.actor)
            kind = C.KINDS[c.kind]
            if kind == "worldBsp":
                continue
            key = name or f"<{kind} at {round(c.aabb_min[0])},{round(c.aabb_min[1])}>"
            e = out.setdefault(key, [list(c.aabb_min), list(c.aabb_max), set()])
            e[0] = [min(e[0][i], c.aabb_min[i]) for i in range(3)]
            e[1] = [max(e[1][i], c.aabb_max[i]) for i in range(3)]
            e[2].add(kind)
        return out


def _inside(normal, p, a, b, c):
    last = 0.0
    prev = c
    for v in (a, b, c):
        e = (v[0] - prev[0], v[1] - prev[1], v[2] - prev[2])
        side = (e[1] * normal[2] - e[2] * normal[1], e[2] * normal[0] - e[0] * normal[2], e[0] * normal[1] - e[1] * normal[0])
        s = side[0] * (p[0] - v[0]) + side[1] * (p[1] - v[1]) + side[2] * (p[2] - v[2])
        if (s < 0 and last > 0) or (s > 0 and last < 0):
            return False
        last = s
        prev = v
    return True


def _load(folder: Path, rel: str):
    return json.loads((folder / rel).read_text(encoding="utf-8"))


def _vec(v):
    return [v["X"], v["Y"], v["Z"]] if isinstance(v, dict) else list(v)


def _rot(v):
    return [v.get("Pitch", 0), v.get("Yaw", 0), v.get("Roll", 0)] if v else [0, 0, 0]


def _angle_diff(a, b):
    d = (a - b) % 65536
    return min(d, 65536 - d)


def compare(native: Path, twin: Path) -> dict:
    report: dict = {"native": str(native), "twin": str(twin), "problems": [], "summary": []}
    problems = report["problems"]

    # ---- actors ----
    ga, gb = _load(native, "sim/gameplay.json"), _load(twin, "sim/gameplay.json")
    tb = {a["id"]: a for a in gb["actors"]}
    worst = {"location": 0.0, "rotation": 0, "scale": 0.0}
    for a in ga["actors"]:
        b = tb.get(a["id"])
        if b is None:
            problems.append(f"actor {a['id']} ({a['class']}) is not in the twin")
            continue
        if a["class"] != b["class"]:
            problems.append(f"actor {a['id']}: class {a['class']} against {b['class']}")
        la, lb = _vec(a["props"]["Location"]), _vec(b["props"].get("Location", {"X": 0, "Y": 0, "Z": 0}))
        dl = math.dist(la, lb)
        worst["location"] = max(worst["location"], dl)
        if dl > 0.5:
            problems.append(f"actor {a['id']}: Location {la} against {lb}")
        ra, rb = _rot(a["props"].get("Rotation")), _rot(b["props"].get("Rotation"))
        dr = max(_angle_diff(x, y) for x, y in zip(ra, rb))
        worst["rotation"] = max(worst["rotation"], dr)
        if dr > 2:
            problems.append(f"actor {a['id']}: Rotation {ra} against {rb}")
        if "DrawScale3D" in a["props"]:
            sa, sb = _vec(a["props"]["DrawScale3D"]), _vec(b["props"].get("DrawScale3D", {"X": 1, "Y": 1, "Z": 1}))
            ds = max(abs(x - y) for x, y in zip(sa, sb))
            worst["scale"] = max(worst["scale"], ds)
            if ds > 1e-3:
                problems.append(f"actor {a['id']}: DrawScale3D {sa} against {sb}")
    extra = sorted(set(tb) - {a["id"] for a in ga["actors"]})
    report["actors"] = {"native": len(ga["actors"]), "twin": len(gb["actors"]), "onlyInTwin": extra, "worst": worst}
    if extra:
        problems.append(f"actors only in the twin: {extra}")

    # ---- GEs ----
    ta, tb2 = _load(native, "sim/traversal.json")["ges"], _load(twin, "sim/traversal.json")["ges"]
    unmatched = list(tb2)
    worst_ge = 0.0
    for g in ta:
        best, best_d = None, None
        for h in unmatched:
            if h["kind"] != g["kind"] or len(h["points"]) != len(g["points"]):
                continue
            d = max(math.dist(p, q) for p, q in zip(g["points"], h["points"]))
            if best_d is None or d < best_d:
                best, best_d = h, d
        if best is None or best_d > 0.5:
            problems.append(f"GE {g['id']} {g['kind']} {g['points']} has no twin (nearest off by {best_d})")
            continue
        worst_ge = max(worst_ge, best_d)
        unmatched.remove(best)
    if unmatched:
        problems.append(f"GEs only in the twin: {[(h['id'], h['kind'], h['points']) for h in unmatched]}")
    report["ges"] = {"native": len(ta), "twin": len(tb2), "worst": worst_ge}

    # ---- world: line probes and zones ----
    wa, wb = World(native), World(twin)
    (x0, y0, z0), (x1, y1, z1) = L.HALL_LO, L.HALL_HI
    rays = []
    step = 32.0
    x = x0 + step / 2
    while x < x1:
        y = y0 + step / 2
        while y < y1:
            rays.append(((x, y, z1 - 1.0), (x, y, z0 - 100.0)))
            y += step
        x += step
    for z in (16.0, 50.0, 100.0, 150.0, 200.0, 300.0):
        y = y0 + step / 2
        while y < y1:
            rays.append(((x0 + 1.0, y, z), (x1 + 100.0, y, z)))
            rays.append(((x1 - 1.0, y, z), (x0 - 100.0, y, z)))
            y += step
        x = x0 + step / 2
        while x < x1:
            rays.append(((x, y0 + 1.0, z), (x, y1 + 100.0, z)))
            rays.append(((x, y1 - 1.0, z), (x, y0 - 100.0, z)))
            x += step
    differ, surf, worst_hit, both_free = [], [], 0.0, 0
    for s, e in rays:
        if not (_free(wa, s) and _free(wb, s)):
            continue                                   # starts in solid in one of them (inside a block)
        ha, hb = wa.line(s, e), wb.line(s, e)
        if ha is None and hb is None:
            both_free += 1
            continue
        if (ha is None) != (hb is None) or abs(ha[0] - hb[0]) > 1.0:
            differ.append({"from": s, "to": e, "native": ha, "twin": hb})
            continue
        worst_hit = max(worst_hit, abs(ha[0] - hb[0]))
        if ha[1] != hb[1]:
            surf.append({"from": s, "to": e, "native": ha[1], "twin": hb[1]})
    report["world"] = {"rays": len(rays), "differ": len(differ), "surfaceDiffer": len(surf), "worstHit": worst_hit,
                       "noHitBoth": both_free, "samples": differ[:20], "surfaceSamples": surf[:20]}
    if differ:
        problems.append(f"{len(differ)} of {len(rays)} world lines hit differently, e.g. {differ[:3]}")
    if surf:
        problems.append(f"{len(surf)} world hits on another surface type, e.g. {surf[:3]}")
    zdiff = []
    zx = x0 + 64.0
    while zx < x1:
        zy = y0 + 64.0
        while zy < y1:
            p = (zx, zy, 50.0)
            if _free(wa, p) and _free(wb, p):
                za, zb = wa.zone(p), wb.zone(p)
                if za != zb:
                    zdiff.append({"at": p, "native": za, "twin": zb})
            zy += 128.0
        zx += 128.0
    report["zones"] = {"differ": len(zdiff), "samples": zdiff[:20]}
    if zdiff:
        problems.append(f"{len(zdiff)} points in another zone, e.g. {zdiff[:3]}")

    # ---- actor collision ----
    ba, bb = wa.actor_bounds(), wb.actor_bounds()
    rows = []
    for name, (lo, hi, kinds) in sorted(ba.items()):
        other = bb.get(name)
        if other is None and name.startswith("<"):
            # Unnamed (render-only) colliders: the nearest unnamed one of the twin.
            cands = [(k, v) for k, v in bb.items() if k.startswith("<")]
            if cands:
                k, other = min(cands, key=lambda kv: math.dist(kv[1][0], lo) + math.dist(kv[1][1], hi))
        if other is None:
            problems.append(f"collider of {name} ({sorted(kinds)}) is not in the twin")
            continue
        d = max(max(abs(lo[i] - other[0][i]), abs(hi[i] - other[1][i])) for i in range(3))
        rows.append({"actor": name, "kinds": sorted(kinds), "twinKinds": sorted(other[2]), "off": round(d, 2)})
        if d > 4.0:
            problems.append(f"collider of {name}: bounds {lo}..{hi} against {other[0]}..{other[1]}")
    report["actorCollision"] = rows

    # ---- lights ----
    la = {li["name"]: li for li in _load(native, "render/lighting.json")["lights"]}
    lb = {li["name"]: li for li in _load(twin, "render/lighting.json")["lights"]}
    for name, li in sorted(la.items()):
        o = lb.get(name)
        if o is None:
            problems.append(f"light {name} is not in the twin")
            continue
        if math.dist(li["location"], o["location"]) > 0.5 or abs(li["brightness"] - o["brightness"]) > 0.01 or \
                abs(li["radius"] - o["radius"]) > 0.01 or li["zone"] != o.get("zone"):
            problems.append(f"light {name}: {li['location']} {li['brightness']} {li['radius']} {li['zone']} against "
                            f"{o['location']} {o['brightness']} {o['radius']} {o.get('zone')}")
    report["lights"] = {"native": len(la), "twin": len(lb)}

    report["ok"] = not problems
    report["summary"] = [
        f"actors: {len(ga['actors'])} native, {len(gb['actors'])} twin, worst location {worst['location']:.3f} uu, "
        f"rotation {worst['rotation']}, scale {worst['scale']:.4f}",
        f"GEs: {len(ta)} native, {len(tb2)} twin, worst point {worst_ge:.3f} uu",
        f"world: {len(rays)} lines, {len(differ)} hit differently, {len(surf)} on another surface, worst hit "
        f"{worst_hit:.3f} uu",
        f"zones: {len(zdiff)} grid points differ",
        f"actor collision: {len(rows)} colliders, worst bounds {max((r['off'] for r in rows), default=0):.2f} uu",
        f"lights: {len(la)} native, {len(lb)} twin",
        "SAME within tolerance" if not problems else f"{len(problems)} difference(s)",
    ] + [f"  {p}" for p in problems[:30]]
    return report


def _free(world: World, p) -> bool:
    """True when p is in empty space of the world (a zero-length walk)."""
    if world.root < 0:
        return True
    node = world.root
    outside = False
    while node >= 0:
        n = world.nodes[node]
        d = n.plane[0] * p[0] + n.plane[1] * p[1] + n.plane[2] * p[2] - n.plane[3]
        csg = n.tri_count > 0 and (n.node_flags & NOT_CSG) == 0
        if d >= 0:
            outside = outside or csg
            node = n.front
        else:
            outside = outside and not csg
            node = n.back
    return outside
