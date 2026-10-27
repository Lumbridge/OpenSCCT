"""Generate TestRange, the test map, as a native SVMap 1.1 folder, and its tracked stations manifest.

    python -B tools/testmap/generate.py                     .content/svmap/TestRange and fixtures/testrange/stations.json
    python -B tools/testmap/generate.py --out DIR           the map folder somewhere else (tests write a scratch copy)
    python -B tools/testmap/generate.py --check             fail when the map folder or the manifest is not what this writes

The layout is tools/testmap/layout.py. The folder has everything the server and the client read (spec/svmap/1.1):
sim/gameplay.json, sim/traversal.json, sim/collision.bin (the world as triangles, gameplay object hulls, glass,
interaction cylinders, zones), render/world.glb (the world as vertex-lit meshes with our own grid textures),
render/materials.json, render/lighting.json, ui/mapinfo.json, and sim/classes.json when the reference install is
there to read the class defaults from (the same table the converter writes for its twin).

The sim group is byte-identical on every run with the same class table; no clocks, no randomness.
"""

from __future__ import annotations

import argparse
import io
import json
import math
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(REPO / "content"))
sys.path.insert(0, str(HERE))

import numpy as np  # noqa: E402

import bsp as B  # noqa: E402
import layout as L  # noqa: E402
from svmbuild import canonical  # noqa: E402
from svmbuild.svmap import LATEST_VERSION, with_derived, write_svmap  # noqa: E402
from svmbuild.writers import collision as C  # noqa: E402
from svmbuild.writers.glb import pack_glb  # noqa: E402

GENERATOR = "tools.testmap"
GENERATOR_VERSION = 1
DEFAULT_OUT = REPO / ".content" / "svmap" / L.MAP_ID
MANIFEST = REPO / "fixtures" / "testrange" / "stations.json"

Vec3 = tuple[float, float, float]

WORLD_FLAGS = ["collideActors", "blockActors", "blockPlayers", "blockZeroExtent", "blockNonZeroExtent", "blockCamera"]
GPO_FLAGS = ["collideActors", "blockZeroExtent", "blockNonZeroExtent", "blockCamera"]
INFIRMARY_FLAGS = ["collideActors", "blockPlayers", "blockZeroExtent", "blockNonZeroExtent", "blockCamera"]

# Collision of the class-default meshes of the gameplay objects TestRange places, as boxes in the mesh's own space
# (before PrePivot and rotation): the bounds of each mesh's per-poly set (zero-extent traces) and of its simple set
# (box sweeps), as the converter writes them, measured from the converted twin (local/testrange, `twin.py convert`).
# Class: (collider kind, PrePivot, [(source set, trace use, lo, hi)], flags).
GPO_BOXES = {
    "SBase.SComputerObjectiveTrigger": ("gpo", (0.0, 0.0, 0.0), [
        ("perPoly", 1, (-30.29, -30.74, -0.08), (33.19, 30.74, 50.22)),
        ("simple", 2, (-30.29, -30.0, -0.08), (28.17, 30.0, 50.22))], GPO_FLAGS),
    "SBase.SFlag": ("gpo", (0.0, 0.0, 0.0), [
        ("perPoly", 1, (-25.0, -10.0, 0.0), (25.0, 10.0, 32.0)),
        ("simple", 2, (-25.0, -10.0, 0.0), (25.0, 10.0, 32.0))], GPO_FLAGS),
    "SBase.SFlagDropZone": ("gpo", (-10.0, 0.0, 0.0), [
        ("perPoly", 1, (-20.67, -25.67, 0.0), (23.59, 25.67, 10.0)),
        ("simple", 2, (-19.48, -25.08, 0.04), (20.01, 24.95, 9.95))], GPO_FLAGS),
    "SBase.SAmmoStock": ("mesh", (0.0, 0.0, 0.0), [
        ("perPoly", 1, (-64.55, -39.66, -0.25), (64.55, 39.66, 86.25)),
        ("simple", 2, (-64.23, -39.94, -0.25), (64.18, 39.03, 86.1))], WORLD_FLAGS),
    "SBase.SInfirmary": ("mesh", (0.0, 0.0, 0.0), [
        ("perPoly", 1, (-0.08, -19.55, -29.54), (21.05, 19.55, 29.54)),
        ("simple", 2, (-0.08, -19.53, -29.54), (19.92, 19.55, 29.54))], INFIRMARY_FLAGS),
    "SBase.SCamNetwork": ("mesh", (0.0, 0.0, 0.0), [
        ("perPoly", 1, (-18.07, -20.87, -20.87), (19.96, 20.87, 20.87)),
        ("simple", 2, (-18.04, -17.99, -18.75), (17.96, 18.01, 17.25))], WORLD_FLAGS),
}
# Interaction cylinders the converter writes for these classes: radius and half height from the actor's
# fInteractionRadius / fInteractionHeight, class <Class>Interaction.
INTERACTION_CLASSES = ("SBase.SAmmoStock", "SBase.SInfirmary", "SBase.SFlagDropZone")
# SVitre's class mesh (SGameplayObjects.Objects.F_vitreSolo01) is one quad in its X-Z plane, centred on the pivot,
# 124.02 x 106.66 (SteeE SVitre11159's glass collider, 132.70 x 159.98 at DrawScale3D (1.07, 1, 1.5)).
VITRE_PANE = (124.02, 106.66)
# Actor lighting (docs/RENDERING.md "Actor lighting"): C = FGetHSV(0, 255, 255) x brightness / 255, k = 2,
# R = 25 (LightRadius + 1), S(x) = 1 - 3x^2 + 2x^3. The bake below lights the world's vertices with it.
WHITE = 0.8213
SUBDIVIDE = 128.0
UV_TILE = 256.0             # one texture tile per 256 uu (2.56 m): the grid lines read as distances


@dataclass
class Face:
    corners: list[Vec3]      # convex polygon, (v1 - v0) x (v2 - v0) points out of the solid
    material: str


def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _outward(corners, faces):
    n = len(corners)
    centre = tuple(sum(c[i] for c in corners) / n for i in range(3))
    out = []
    for f in faces:
        v = [corners[i] for i in f]
        normal = _cross(_sub(v[1], v[0]), _sub(v[2], v[0]))
        mid = tuple(sum(p[i] for p in v) / len(v) for i in range(3))
        out.append(f if _dot(normal, _sub(mid, centre)) > 0 else tuple(reversed(f)))
    return out


def box_faces(lo, hi, material) -> list[Face]:
    x0, y0, z0 = lo
    x1, y1, z1 = hi
    c = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    faces = _outward(c, [(0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)])
    return [Face([c[i] for i in f], material) for f in faces]


def wedge_corners(lo, hi, up) -> list[Vec3]:
    """The six corners of a wedge filling lo..hi that rises toward `up` from lo.z to hi.z."""
    x0, y0, z0 = lo
    x1, y1, z1 = hi
    base = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)]
    high = {"+x": [(x1, y0, z1), (x1, y1, z1)], "-x": [(x0, y0, z1), (x0, y1, z1)],
            "+y": [(x0, y1, z1), (x1, y1, z1)], "-y": [(x0, y0, z1), (x1, y0, z1)]}[up]
    return base + high


def wedge_faces(lo, hi, up, material) -> list[Face]:
    c = wedge_corners(lo, hi, up)
    faces = [(0, 1, 2, 3)]
    # The high edge is corners 4, 5; the low edge is the base edge opposite it.
    base = {"+x": ((1, 2), (0, 3)), "-x": ((0, 3), (1, 2)), "+y": ((3, 2), (0, 1)), "-y": ((0, 1), (3, 2))}[up]
    (h0, h1), (l0, l1) = base
    faces.append((h0, h1, 5, 4))          # vertical back face under the high edge
    faces.append((l0, l1, 5, 4))          # the slope
    faces.append((h0, 4, l0))             # the two triangular sides
    faces.append((h1, 5, l1))
    faces = _outward(c, faces)
    return [Face([c[i] for i in f], material) for f in faces]


def _convex(faces: list[Face], index: int) -> B.Convex:
    return B.Convex([B.Poly(list(f.corners), B.plane_of(f.corners), f.material, index) for f in faces], index)


def world_volumes() -> tuple[list[B.Convex], list[B.Convex]]:
    """The carves (hall, pool) and the solids as convex volumes with outward faces."""
    carves = []
    for i, c in enumerate(L.CARVES):
        faces = box_faces(c.lo, c.hi, c.walls)
        for f in faces:
            nz = B.plane_of(f.corners).n[2]
            # A carve's floor is the face whose outward normal points down; its ceiling points up.
            f.material = c.floor if nz < -0.5 else c.ceiling if nz > 0.5 else c.walls
        carves.append(_convex(faces, i))
    solids = []
    for j, s in enumerate(L.SOLIDS):
        faces = wedge_faces(s.lo, s.hi, s.up, s.material) if s.up else box_faces(s.lo, s.hi, s.material)
        solids.append(_convex(faces, len(L.CARVES) + j))
    return carves, solids


def world_polys() -> list[B.Poly]:
    """The world's surface: every polygon between the empty space and solid, facing the empty side."""
    carves, solids = world_volumes()
    hall, pool = carves[0], carves[1]

    def cut_carve(poly: B.Poly, index: int) -> list[B.Poly]:
        if index == pool.index:
            # The pool's top is open to the hall.
            top = L.CARVES[pool.index].hi[2]
            return [] if poly.plane.n[2] < -0.5 and all(abs(v[2] - top) < 1e-6 for v in poly.verts) else [poly]
        if index == hall.index and poly.plane.n[2] > 0.5:
            return B._subtract([poly], pool, keep_same=False)
        return [poly]

    return B.boundary(carves, solids, cut_carve)


def world_faces() -> list[Face]:
    return [Face(list(p.verts), p.material) for p in world_polys()]


def _f(v) -> dict:
    return {"X": float(v[0]), "Y": float(v[1]), "Z": float(v[2])}


def _rot(yaw=0, pitch=0, roll=0) -> dict:
    return {"Pitch": int(pitch), "Roll": int(roll), "Yaw": int(yaw)}


def vitre_scale() -> Vec3:
    w, h = L.PANE_SIZE
    return (canonical.to_f32(w / VITRE_PANE[0]), 1.0, canonical.to_f32(h / VITRE_PANE[1]))


def build_gameplay() -> dict:
    actors = []
    for a in L.ACTORS:
        props = {"Location": _f(a.location)}
        if a.yaw or a.pitch:
            props["Rotation"] = _rot(a.yaw, a.pitch)
        props.update(a.props)
        if a.cls == "SBase.SVitre":
            props["DrawScale3D"] = _f(vitre_scale())
        actors.append({"id": a.id, "class": a.cls, "props": props})
    gameplay = {
        "formatVersion": LATEST_VERSION,
        "map": L.MAP_ID,
        "levelInfo": {"id": "LevelInfo0", "class": "Engine.LevelInfo", "props": dict(L.LEVEL_INFO)},
        "mapSettings": L.MAP_SETTINGS,
        "actors": actors,
    }
    return with_derived(gameplay)


def build_traversal() -> dict:
    ges = []
    for g in L.GES:
        ges.append({"id": g.id, "kind": g.kind, "points": [[float(c) for c in p] for p in g.points], "mask": 0,
                    "platform": 0, "src": {"kind": "synthetic", "note": g.note}, "activeAtStart": True})
    return {"formatVersion": LATEST_VERSION, "map": L.MAP_ID, "activationMask": L.LEVEL_INFO["GEActivationMask"],
            "ges": ges}


def _yaw_point(p: Vec3, yaw: int, pitch: int = 0) -> Vec3:
    """UE2 rotation of a local point by (pitch, yaw), as FRotationMatrix (roll 0)."""
    a = yaw * 2 * math.pi / 65536
    b = pitch * 2 * math.pi / 65536
    cy, sy, cp, sp = math.cos(a), math.sin(a), math.cos(b), math.sin(b)
    x, y, z = p
    # X axis (cp cy, cp sy, sp), Y axis (-sy, cy, 0), Z axis (-sp cy, -sp sy, cp)
    return (x * cp * cy - y * sy - z * sp * cy, x * cp * sy + y * cy - z * sp * sy, x * sp + z * cp)


def _box_corners(lo, hi) -> list[Vec3]:
    x0, y0, z0 = lo
    x1, y1, z1 = hi
    return [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]


def _box_planes(lo, hi):
    return [(-1.0, 0.0, 0.0, -lo[0]), (1.0, 0.0, 0.0, hi[0]), (0.0, -1.0, 0.0, -lo[1]), (0.0, 1.0, 0.0, hi[1]),
            (0.0, 0.0, -1.0, -lo[2]), (0.0, 0.0, 1.0, hi[2])]


def _aabb(points):
    xs, ys, zs = zip(*points)
    return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))


def build_collision(polys: list[B.Poly], gameplay: dict) -> bytes:
    d = C.CollisionData(sources=[], material_ext=[], cylinder_ext=[])
    mats = {}
    for key, m in L.MATERIALS.items():
        mats[key] = len(d.materials)
        d.materials.append(C.Material(m.surface, C.flag_bits(["climbable"] if m.climbable else [], C.MATERIAL_FLAGS),
                                      d.string(m.stock)))
        d.material_ext.append(C.MaterialExt(0, 0, m.occlusion))
    vert_index: dict[Vec3, int] = {}

    def add_poly(verts, material):
        idx = []
        for p in verts:
            key = tuple(canonical.to_f32(c) for c in p)
            if key not in vert_index:
                vert_index[key] = len(d.verts)
                d.verts.append(key)
            idx.append(vert_index[key])
        for i in range(1, len(idx) - 1):
            d.tris.append((idx[0], idx[i], idx[i + 1], 0, mats[material]))

    # The world BSP: zones split off first (non-CSG chains), then a solid-leaf tree over the surface polygons.
    zone_index = {z.id: i for i, z in enumerate(L.ZONES, start=1)}
    room = next(z for z in L.ZONES if z.id == "ZoneRoom")
    # The room's zone is split off on planes in the middle of its walls, roof and floor slab (where the twin's portals
    # stand), so no zone plane lies on a face: a line then always ends its walk on a face's own node.
    half = L.T / 2
    zlo = (room.lo[0] - half, room.lo[1] - half, room.lo[2] - half)
    zhi = (room.hi[0] + half, room.hi[1] + half, room.hi[2] + half)
    root = B.build(polys, [(zone_index[room.id], zlo, zhi)], zone_index["ZoneHall"])
    for plane, front, back, node_polys, csg, zf, zb in B.flatten(root):
        t0 = len(d.tris)
        for q in node_polys:
            add_poly(q.verts, q.material)
        d.bsp.append(C.BspNode((float(plane.n[0]), float(plane.n[1]), float(plane.n[2]), float(plane.d)), front, back, -1, -1,
                               t0, len(d.tris) - t0, 0 if csg else B.NOT_CSG, zf, zb))
    lo, hi = _aabb(d.verts)
    d.colliders.append(C.Collider(kind=C.KINDS.index("worldBsp"), flags=C.flag_bits(WORLD_FLAGS, C.COLLIDER_FLAGS),
                                  vert_start=0, vert_count=len(d.verts), tri_start=0, tri_count=len(d.tris),
                                  bsp_start=0, bsp_count=len(d.bsp), aabb_min=lo, aabb_max=hi))
    d.sources.append(C.ColliderSource(source=d.string(L.MAP_ID + ".World"), set=C.SOURCE_SETS.index("worldBsp")))
    world_lo, world_hi = lo, hi
    by_id = {a["id"]: a for a in gameplay["actors"]}

    def hull(kind, actor, flags, local_lo, local_hi, location, yaw, pitch=0, material="metal", bullet=False,
             pre_pivot=(0.0, 0.0, 0.0), source_set="simple", trace_use=3):
        corners = _box_corners(local_lo, local_hi)
        v0, p0, t0 = len(d.verts), len(d.planes), len(d.tris)
        d.verts.extend(corners)
        d.planes.extend(_box_planes(local_lo, local_hi))
        for f in _outward(corners, [(0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]):
            d.tris.append((v0 + f[0], v0 + f[1], v0 + f[2], len(d.colliders), mats[material]))
            d.tris.append((v0 + f[0], v0 + f[2], v0 + f[3], len(d.colliders), mats[material]))
        h = len(d.hulls)
        d.hulls.append(C.Hull(p0, 6, v0, 8, mats[material], local_lo, local_hi))
        world = [tuple(a + b for a, b in zip(_yaw_point(_sub(c, pre_pivot), yaw, pitch), location)) for c in corners]
        wlo, whi = _aabb(world)
        names = flags + (["bulletGoesThrough"] if bullet else [])
        d.colliders.append(C.Collider(kind=C.KINDS.index(kind), flags=C.flag_bits(names, C.COLLIDER_FLAGS),
                                      actor=d.string(actor) if actor else C.NONE, vert_start=v0, vert_count=8,
                                      tri_start=t0, tri_count=12, hull_start=h, hull_count=1, location=location,
                                      rotation=(pitch, yaw, 0), pre_pivot=pre_pivot, aabb_min=wlo, aabb_max=whi))
        d.sources.append(C.ColliderSource(source=d.string(actor or "TestRange.Grating"),
                                          set=C.SOURCE_SETS.index(source_set), trace_use=trace_use))

    for a in L.ACTORS:
        if a.cls in GPO_BOXES:
            kind, pivot, sets, flags = GPO_BOXES[a.cls]
            for source_set, use, blo, bhi in sets:
                k = kind if kind == "gpo" else ("meshPoly" if source_set == "perPoly" else "meshHull")
                hull(k, a.id, flags, blo, bhi, a.location, a.yaw, a.pitch, pre_pivot=pivot, source_set=source_set,
                     trace_use=use)
        if a.cls == "SBase.SVitre":
            sx, _, sz = vitre_scale()
            hw, hh = VITRE_PANE[0] / 2, VITRE_PANE[1] / 2
            quad = [(-hw, 0.0, -hh), (hw, 0.0, -hh), (hw, 0.0, hh), (-hw, 0.0, hh)]
            v0, t0 = len(d.verts), len(d.tris)
            d.verts.extend(quad)
            d.tris.append((v0, v0 + 1, v0 + 2, len(d.colliders), mats["wall"]))
            d.tris.append((v0, v0 + 2, v0 + 3, len(d.colliders), mats["wall"]))
            world = [tuple(c + l for c, l in zip(_yaw_point((q[0] * sx, 0.0, q[2] * sz), a.yaw), a.location)) for q in quad]
            wlo, whi = _aabb(world)
            d.colliders.append(C.Collider(kind=C.KINDS.index("glass"), flags=C.flag_bits(WORLD_FLAGS, C.COLLIDER_FLAGS),
                                          actor=d.string(a.id), vert_start=v0, vert_count=4, tri_start=t0, tri_count=2,
                                          location=a.location, rotation=(0, a.yaw, 0), scale=(sx, 1.0, sz),
                                          aabb_min=wlo, aabb_max=whi))
            d.sources.append(C.ColliderSource(source=d.string(a.id), asset=d.string("SGameplayObjects.Objects.F_vitreSolo01"),
                                              set=C.SOURCE_SETS.index("perPoly")))

    # The grating: a render-only mesh that blocks bodies and lets bullets through (bBulletGoTru).
    gx = (L.PANEL_X[0] + L.PANEL_X[1]) / 2
    gy0, gy1 = L.OPENINGS["grating"]
    hull("meshHull", None, WORLD_FLAGS, (-2.0, gy0 - (gy0 + gy1) / 2, L.OPEN_Z[0] - 128.0),
         (2.0, gy1 - (gy0 + gy1) / 2, L.OPEN_Z[1] - 128.0), (gx, (gy0 + gy1) / 2, 128.0), 0, bullet=True)

    for a in L.ACTORS:
        if a.cls in INTERACTION_CLASSES:
            p = by_id[a.id]["props"]
            d.cylinders.append(C.Cylinder(d.string(a.id), C.flag_bits(["collideActors"], C.COLLIDER_FLAGS), a.location,
                                          float(p["fInteractionRadius"]), float(p["fInteractionHeight"])))
            d.cylinder_ext.append(C.CylinderExt(C.CYLINDER_KINDS.index("interaction"), d.string(a.cls + "Interaction")))

    d.zones.append(C.Zone(d.string("LevelInfo0"), 0, world_lo, world_hi))
    for z in L.ZONES:
        d.zones.append(C.Zone(d.string(z.id), 0, z.lo, z.hi))
    return C.write_collision(d)


# ---- render ---------------------------------------------------------------------------------------------------

def zone_of(p: Vec3) -> str:
    for z in L.ZONES:
        if all(z.lo[i] <= p[i] <= z.hi[i] for i in range(3)):
            return z.id
    return "LevelInfo0"


def _smooth(x: float) -> float:
    return 0.0 if x >= 1.0 else 1.0 - 3.0 * x * x + 2.0 * x * x * x


def light_at(p: Vec3, n: Vec3) -> float:
    """The baked light of a world vertex: zone ambient plus every light of its zone, the actor lighting model."""
    probe = (p[0] + 2 * n[0], p[1] + 2 * n[1], p[2] + 2 * n[2])
    zone = zone_of(probe)
    total = next((z.ambient / 255.0 for z in L.ZONES if z.id == zone), 0.0)
    for li in L.LIGHTS:
        if li.zone != zone:
            continue
        dv = _sub(li.location, p)
        dist = math.sqrt(_dot(dv, dv))
        reach = 25.0 * (li.radius + 1.0)
        if dist >= reach or dist < 1e-6:
            continue
        nl = max(0.0, _dot(n, (dv[0] / dist, dv[1] / dist, dv[2] / dist)))
        total += 2.0 * WHITE * li.brightness / 255.0 * _smooth(dist / reach) * nl
    return min(2.0, total)


def _face_grid(f: Face):
    """Split a convex planar face into cells of at most SUBDIVIDE uu: quads by bilinear subdivision, triangles as a fan."""
    v = f.corners
    if len(v) == 4:
        a, b, c, d = v
        lu = max(math.dist(a, b), math.dist(d, c))
        lv = max(math.dist(a, d), math.dist(b, c))
        nu, nv = max(1, math.ceil(lu / SUBDIVIDE)), max(1, math.ceil(lv / SUBDIVIDE))

        def at(i, j):
            s, t = i / nu, j / nv
            top = tuple(a[k] + (b[k] - a[k]) * s for k in range(3))
            bot = tuple(d[k] + (c[k] - d[k]) * s for k in range(3))
            return tuple(top[k] + (bot[k] - top[k]) * t for k in range(3))
        for i in range(nu):
            for j in range(nv):
                yield [at(i, j), at(i + 1, j), at(i + 1, j + 1), at(i, j + 1)]
    else:
        yield v


def _uv(p: Vec3, n: Vec3) -> tuple[float, float]:
    ax = max(range(3), key=lambda k: abs(n[k]))
    if ax == 2:
        return p[0] / UV_TILE, p[1] / UV_TILE
    if ax == 0:
        return p[1] / UV_TILE, -p[2] / UV_TILE
    return p[0] / UV_TILE, -p[2] / UV_TILE


class Glb:
    """A small glTF writer: meshes with POSITION, NORMAL, TEXCOORD_0, COLOR_0 (unsigned bytes), textured materials."""

    def __init__(self):
        self.g = {"asset": {"version": "2.0", "generator": f"{GENERATOR} {GENERATOR_VERSION}"}, "scene": 0,
                  "scenes": [{"nodes": []}], "nodes": [], "meshes": [], "materials": [], "accessors": [],
                  "bufferViews": [], "images": [], "textures": [], "samplers": [
                      {"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}]}
        self.bin = bytearray()
        self.material_index: dict[str, int] = {}

    def _acc(self, arr: np.ndarray, kind: str, comp: int, target: int, bounds=False, normalized=False) -> int:
        while len(self.bin) % 4:
            self.bin.append(0)
        data = arr.tobytes()
        self.g["bufferViews"].append({"buffer": 0, "byteOffset": len(self.bin), "byteLength": len(data), "target": target})
        self.bin += data
        acc = {"bufferView": len(self.g["bufferViews"]) - 1, "componentType": comp, "count": int(arr.shape[0]), "type": kind}
        if bounds:
            acc["min"] = [float(x) for x in arr.min(axis=0)]
            acc["max"] = [float(x) for x in arr.max(axis=0)]
        if normalized:
            acc["normalized"] = True
        self.g["accessors"].append(acc)
        return len(self.g["accessors"]) - 1

    def material(self, name: str, uri: str) -> int:
        if name in self.material_index:
            return self.material_index[name]
        self.g["images"].append({"uri": uri})
        self.g["textures"].append({"sampler": 0, "source": len(self.g["images"]) - 1})
        self.g["materials"].append({"name": name, "pbrMetallicRoughness": {
            "baseColorTexture": {"index": len(self.g["textures"]) - 1}, "metallicFactor": 0.0, "roughnessFactor": 0.85}})
        self.material_index[name] = len(self.g["materials"]) - 1
        return self.material_index[name]

    def mesh(self, name: str, pos, nrm, uv, col, idx, material: int) -> int:
        """UE-space arrays: positions (uu), normals; converted to glTF space (x, z, y) / 100 with flipped winding."""
        p = np.asarray(pos, dtype=np.float32)
        n = np.asarray(nrm, dtype=np.float32)
        gp = (np.stack([p[:, 0], p[:, 2], p[:, 1]], axis=1) / np.float32(100.0)).astype(np.float32)
        gn = np.stack([n[:, 0], n[:, 2], n[:, 1]], axis=1).astype(np.float32)
        t = np.asarray(idx, dtype=np.uint32)[:, [0, 2, 1]].reshape(-1)
        attrs = {"POSITION": self._acc(gp, "VEC3", 5126, 34962, bounds=True),
                 "NORMAL": self._acc(gn, "VEC3", 5126, 34962),
                 "TEXCOORD_0": self._acc(np.asarray(uv, dtype=np.float32), "VEC2", 5126, 34962),
                 "COLOR_0": self._acc(np.asarray(col, dtype=np.uint8), "VEC4", 5121, 34962, normalized=True)}
        self.g["meshes"].append({"name": name, "primitives": [
            {"attributes": attrs, "indices": self._acc(t, "SCALAR", 5125, 34963), "material": material, "mode": 4}]})
        return len(self.g["meshes"]) - 1

    def node(self, name, mesh=None, children=None, extras=None, root=False) -> int:
        n = {"name": name}
        if mesh is not None:
            n["mesh"] = mesh
        if children:
            n["children"] = children
        if extras:
            n["extras"] = extras
        self.g["nodes"].append(n)
        if root:
            self.g["scenes"][0]["nodes"].append(len(self.g["nodes"]) - 1)
        return len(self.g["nodes"]) - 1

    def to_bytes(self, extras) -> bytes:
        self.g["extras"] = extras
        return pack_glb(self.g, bytes(self.bin))


def _mesh_arrays(faces: list[Face], lit=True):
    pos, nrm, uv, col, idx = [], [], [], [], []
    for f in faces:
        v = f.corners
        n = _cross(_sub(v[1], v[0]), _sub(v[2], v[0]))
        length = math.sqrt(_dot(n, n))
        n = (n[0] / length, n[1] / length, n[2] / length)
        for cell in _face_grid(f):
            base = len(pos)
            for p in cell:
                pos.append(p)
                nrm.append(n)
                uv.append(_uv(p, n))
                light = light_at(p, n) if lit else 1.0
                c = int(round(min(1.0, light / 2.0) * 255))
                col.append((c, c, c, 255))
            for i in range(1, len(cell) - 1):
                idx.append((base, base + i, base + i + 1))
    return pos, nrm, uv, col, idx


def _texture(kind: str, colour) -> bytes:
    """Our own 128 x 128 tile (one per 256 uu): the material's colour with grid lines; a few kinds get a pattern."""
    from PIL import Image  # noqa: PLC0415

    size = 128
    base = np.array([int(round(255 * c)) for c in colour], dtype=np.int32)
    img = np.tile(base, (size, size, 1))
    yy, xx = np.mgrid[0:size, 0:size]
    if kind == "fence":
        diamond = ((xx + yy) % 16 < 2) | ((xx - yy) % 16 < 2)
        img = np.where(diamond[..., None], np.minimum(255, base + 70), (base * 0.45).astype(np.int32))
    elif kind == "water":
        ripple = ((xx // 2 + (yy // 6) * 3) % 23) < 2
        img = np.where(ripple[..., None], np.minimum(255, base + 50), base)
    elif kind == "grating":
        bars = (xx % 12 < 3) | (yy % 12 < 3)
        img = np.where(bars[..., None], base, (base * 0.25).astype(np.int32))
    line = (xx < 2) | (yy < 2)
    half = ((xx == size // 2) | (yy == size // 2)) & ~line
    img = np.where(line[..., None], (img * 0.55).astype(np.int32), img)
    img = np.where(half[..., None], (img * 0.8).astype(np.int32), img)
    out = io.BytesIO()
    Image.fromarray(np.clip(img, 0, 255).astype(np.uint8), "RGB").save(out, "PNG", optimize=False)
    return out.getvalue()


def _mark_texture(label: str) -> bytes:
    """A distance mark: a white stripe with the distance written on it."""
    from PIL import Image, ImageDraw  # noqa: PLC0415

    img = Image.new("RGB", (32, 256), (235, 235, 225))
    draw = ImageDraw.Draw(img)
    for k in range(4):
        draw.text((4, 20 + 60 * k), label, fill=(20, 20, 20))
    out = io.BytesIO()
    img.save(out, "PNG", optimize=False)
    return out.getvalue()


def build_render(faces: list[Face]) -> dict[str, bytes]:
    files: dict[str, bytes] = {}
    glb = Glb()
    materials = {}
    gameplay_info = {}
    library, instances = [], []
    by_material: dict[str, list[Face]] = {}
    for f in faces:
        by_material.setdefault(f.material, []).append(f)

    def add_material(name, uri, flags_extra=None, source=None):
        flags = {"masked": False, "texture": uri, "translucent": False, "two_sided": False, "unlit": False}
        flags.update(flags_extra or {})
        materials[name] = {"flags": flags, "index": glb.material(name, uri), "source": source or name}
        return materials[name]["index"]

    for key in sorted(by_material):
        m = L.MATERIALS[key]
        uri = f"textures/TestRange_{key}.png"
        files[uri] = _texture(key, m.colour)
        name = f"TestRange.Materials.{key}"
        mi = add_material(name, uri)
        info = {"OcclusionType": m.occlusion, "SurfaceType": m.surface}
        if m.climbable:
            info["bClimbable"] = 1
        gameplay_info[name] = info
        mesh = glb.mesh(f"TestRange_World_{key}", *_mesh_arrays(by_material[key]), material=mi)
        lib_name = f"TestRange.World.{key}#vc"
        library.append(glb.node(lib_name, mesh=mesh, extras={"svmap": {"asset": f"TestRange.World.{key}",
                                                                      "role": "libraryMesh", "variant": "vc"}}))
        instances.append({"actor": f"World_{key}", "class": "StaticMeshActor", "gameplay": False, "hidden": False,
                          "kind": "staticmesh", "location": [0.0, 0.0, 0.0], "mesh": lib_name,
                          "rotation": {"Pitch": 0, "Roll": 0, "Yaw": 0}, "scale": [1.0, 1.0, 1.0], "vertexLit": True})

    # The grating panel (render-only, bBulletGoTru in the twin): a thin box in the grating opening.
    gx = (L.PANEL_X[0] + L.PANEL_X[1]) / 2
    gy0, gy1 = L.OPENINGS["grating"]
    uri = "textures/TestRange_grating.png"
    files[uri] = _texture("grating", (0.55, 0.55, 0.58))
    mi = add_material("TestRange.Materials.grating", uri, {"masked": False})
    grating = box_faces((gx - 2, gy0, L.OPEN_Z[0]), (gx + 2, gy1, L.OPEN_Z[1]), "metal")
    mesh = glb.mesh("TestRange_Grating", *_mesh_arrays(grating), material=mi)
    library.append(glb.node("TestRange.Grating#vc", mesh=mesh, extras={"svmap": {"asset": "TestRange.Grating",
                                                                               "role": "libraryMesh", "variant": "vc"}}))
    instances.append({"actor": "Grating", "class": "StaticMeshActor", "gameplay": False, "hidden": False,
                      "kind": "staticmesh", "location": [0.0, 0.0, 0.0], "mesh": "TestRange.Grating#vc",
                      "rotation": {"Pitch": 0, "Roll": 0, "Yaw": 0}, "scale": [1.0, 1.0, 1.0], "vertexLit": True})
    gameplay_info["TestRange.Materials.grating"] = {"OcclusionType": 1, "SurfaceType": 14}

    # Render-only boxes (the pipe).
    for name, lo, hi, key in L.VISUALS:
        uri = f"textures/TestRange_{key}.png"
        mi = add_material(f"TestRange.Materials.{key}", uri)
        mesh = glb.mesh(f"TestRange_{name}", *_mesh_arrays(box_faces(lo, hi, key)), material=mi)
        library.append(glb.node(f"TestRange.{name}#vc", mesh=mesh, extras={"svmap": {"asset": f"TestRange.{name}",
                                                                               "role": "libraryMesh", "variant": "vc"}}))
        instances.append({"actor": name, "class": "StaticMeshActor", "gameplay": False, "hidden": False,
                          "kind": "staticmesh", "location": [0.0, 0.0, 0.0], "mesh": f"TestRange.{name}#vc",
                          "rotation": {"Pitch": 0, "Roll": 0, "Yaw": 0}, "scale": [1.0, 1.0, 1.0], "vertexLit": True})

    # Distance marks on the lane floor, 0.5 over it, no collision.
    for name, (x0, y0), (x1, y1), label in L.MARKS:
        uri = f"textures/TestRange_{name}.png"
        files[uri] = _mark_texture(label)
        mi = add_material(f"TestRange.Materials.{name}", uri)
        z = 0.5
        quad = Face([(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)], "floor")
        pos, nrm, _uvs, col, idx = _mesh_arrays([quad])
        # The stripe's texture runs along the lane width.
        uvs = [((p[0] - x0) / (x1 - x0), (p[1] - y0) / (y1 - y0)) for p in pos]
        mesh = glb.mesh(f"TestRange_{name}", pos, nrm, uvs, col, idx, material=mi)
        library.append(glb.node(f"TestRange.{name}#vc", mesh=mesh, extras={"svmap": {"asset": f"TestRange.{name}",
                                                                               "role": "libraryMesh", "variant": "vc"}}))
        instances.append({"actor": name, "class": "StaticMeshActor", "gameplay": False, "hidden": False,
                          "kind": "staticmesh", "location": [0.0, 0.0, 0.0], "mesh": f"TestRange.{name}#vc",
                          "rotation": {"Pitch": 0, "Roll": 0, "Yaw": 0}, "scale": [1.0, 1.0, 1.0], "vertexLit": True})

    glb.node("Library", children=library, extras={"svmap": {"role": "library"}}, root=True)
    files["world.glb"] = glb.to_bytes({"svmap": {"map": L.MAP_ID, "instances": instances}})
    files["materials.json"] = canonical.dump_file({
        "materials": materials, "graph": {}, "gameplay": gameplay_info, "additive": [], "modulate": [],
        "lightmapped": [], "authoredColorMeshes": [],
        "vertexLitMeshes": [{"base": i["mesh"].split("#")[0], "name": i["mesh"]} for i in instances]})
    files["lighting.json"] = canonical.dump_file(build_lighting())
    return files


def build_lighting() -> dict:
    lights = []
    for li in L.LIGHTS:
        lights.append({
            "apply_ingame": True, "apply_static": True, "brightness": float(li.brightness), "class": "Light",
            "color": [1.0, 1.0, 1.0], "cone": 128, "dynamic": False, "echelon_range": 0.0, "effect": 0,
            "gameplay": False, "hidden": False, "hue": li.hue, "initially_on": True, "kind": "light",
            "light_period": 32, "light_phase": 0, "light_type": "point", "location": [float(c) for c in li.location],
            "ltype": 1, "name": li.id, "near_plane_ratio": 0.1, "own_zone_only": True,
            "props": {"LightBrightness": float(li.brightness), "LightRadius": float(li.radius), "Location": _f(li.location),
                      "Tag": "Light", "bAffectOwnZoneOnly": True, "bApplyToInGameLighting": True},
            "radius": float(li.radius), "rotation": [0, 0, 0], "saturation": li.saturation, "scale": [1.0, 1.0, 1.0],
            "spot_height": 0.0, "spot_width": 0.0, "tag": "Light", "triggered": False, "zone": li.zone})
    zones = []
    for i, z in enumerate(L.ZONES, start=1):
        a = int(round(z.ambient * WHITE))
        zones.append({"ambient": [a, a, a, 255], "class": "ZoneInfo", "fog": False, "fog_color": [0, 0, 0, 0],
                      "fog_end": 0.0, "fog_start": 0.0, "hidden": True, "kind": "zone", "location": list(z.marker),
                      "location_name": z.name, "name": z.id,
                      "props": {"AmbientBrightness": z.ambient, "Location": _f(z.marker), "LocationName": z.name,
                                "Tag": "ZoneInfo"},
                      "rotation": [0, 0, 0], "scale": [1.0, 1.0, 1.0], "sky": False, "tag": "ZoneInfo", "zoneIndex": i})
    return {"lights": lights, "zones": zones, "look": {"glow_enabled": False, "glow_threshold": 255,
                                                         "glow_threshold_stored": False},
            "sky": None, "lightmaps": []}


def build_mapinfo() -> dict:
    return {"MapName": "TESTRANGE", "LegendMap": "A test range for the rewrite: every move at a known spot.",
            "LegendMapInBrief": "1. Traversal strip||2. Gradient room||3. Shooting lane||4. Objectives",
            "ExpliMapInBriefSpy": "Hack the terminal and carry the disk to its drop zone.",
            "ExpliMapInBriefSentry": "Protect the terminal and the disk.", "modes": {}}


def class_table(gameplay: dict):
    """The class table the converter writes for these classes, from the reference install; None without it."""
    try:
        from svmbuild.paths import default_game_root  # noqa: PLC0415
        from svmbuild.sources.classdb import class_db  # noqa: PLC0415
        from svmbuild.sources.typedprops import Notes  # noqa: PLC0415

        game = default_game_root()
        if not (game / "System").is_dir():
            return None
        db = class_db(str(game))
    except (ImportError, OSError, ValueError):
        return None
    used = sorted({gameplay["levelInfo"]["class"]} | {a["class"] for a in gameplay["actors"]})
    classes: dict = {}
    notes = Notes()
    for cls in used:
        for i, c in enumerate(db.chain(cls)):
            entry = classes.setdefault(c, {"super": db.classes[c].get("super")})
            if i == 0:
                entry["defaults"] = db.defaults_json(c, notes=notes)
    return {"format": "svm.classes/1", "classes": classes}


def stations_manifest(gameplay: dict, traversal: dict) -> dict:
    ges = {g["id"]: g for g in traversal["ges"]}
    out = []
    for s in L.STATIONS:
        entry = {"id": s.id, "kind": s.kind, "title": s.title, "spot": list(s.spot), "yaw": s.yaw}
        entry.update(s.data)
        if isinstance(s.data.get("ge"), str):
            g = ges[s.data["ge"]]
            entry["geKind"] = g["kind"]
            entry["gePoints"] = g["points"]
        if s.kind == "zipLine":
            hx, hy, hz = s.data["high"]
            lx, ly, lz = s.data["low"]
            entry["length"] = round(math.dist((hx, hy, hz), (lx, ly, lz)), 3)
            entry["run"] = round(math.hypot(hx - lx, hy - ly), 3)
            entry["drop"] = round(hz - lz, 3)
            entry["slope"] = round((hz - lz) / entry["length"], 5)
        out.append(entry)
    return {"format": "svm.testrange/1", "map": L.MAP_ID, "layoutVersion": L.LAYOUT_VERSION,
            "generator": f"{GENERATOR} {GENERATOR_VERSION}", "units": "uu, UE space (Z up, yaw 16384 = +Y)",
            "hall": {"lo": list(L.HALL_LO), "hi": list(L.HALL_HI)},
            "zones": [{"id": z.id, "name": z.name, "lo": list(z.lo), "hi": list(z.hi), "ambient": z.ambient}
                      for z in L.ZONES],
            "lights": [{"id": li.id, "location": list(li.location), "brightness": li.brightness, "radius": li.radius,
                        "zone": li.zone} for li in L.LIGHTS if li.zone == "ZoneRoom"],
            "actors": {a["id"]: {"class": a["class"], "location": [a["props"]["Location"][k] for k in "XYZ"]}
                       for a in gameplay["actors"]},
            "stations": out}


def generate(out: Path, with_classes: bool = True) -> tuple[dict, dict]:
    """Write the map folder at `out`; returns (SVMap manifest, stations manifest)."""
    polys = world_polys()
    faces = [Face(list(p.verts), p.material) for p in polys]
    gameplay = build_gameplay()
    traversal = build_traversal()
    files = {"ui/mapinfo.json": canonical.dump_file(build_mapinfo(), ascii_keys=False)}
    requires = []
    table = class_table(gameplay) if with_classes else None
    if table is not None:
        files["sim/classes.json"] = canonical.dump_file(table)
        pack = REPO / ".content" / "shared" / "classes.json"
        if pack.is_file():
            requires = [{"pack": "classes", "sha256": canonical.sha256_hex(pack.read_bytes())}]
    manifest = write_svmap(Path(out), map_id=L.MAP_ID, title=L.TITLE,
                           source={"kind": "synthetic", "generator": GENERATOR, "generatorVersion": GENERATOR_VERSION},
                           gameplay=gameplay, traversal=traversal, collision=build_collision(polys, gameplay),
                           render=build_render(faces), requires=requires, files=files, format_version=LATEST_VERSION)
    return manifest, stations_manifest(gameplay, traversal)


def _tree(root: Path) -> dict[str, bytes]:
    return {p.relative_to(root).as_posix(): p.read_bytes() for p in sorted(root.rglob("*")) if p.is_file()}


def check(out: Path) -> list[str]:
    with tempfile.TemporaryDirectory() as tmp:
        _, stations = generate(Path(tmp) / L.MAP_ID, with_classes=(Path(out) / "sim" / "classes.json").exists())
        fresh = _tree(Path(tmp) / L.MAP_ID)
    have = _tree(Path(out)) if Path(out).exists() else {}
    diffs = [f"differs: {k}" for k in sorted(fresh) if k in have and have[k] != fresh[k]]
    diffs += [f"missing: {k}" for k in sorted(fresh) if k not in have]
    diffs += [f"extra: {k}" for k in sorted(have) if k not in fresh]
    want = canonical.dump_file(stations)
    if not MANIFEST.is_file() or MANIFEST.read_bytes() != want:
        diffs.append(f"differs: {MANIFEST.relative_to(REPO).as_posix()}")
    return diffs


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="python -B tools/testmap/generate.py", description="Generate the TestRange SVMap.")
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT, help="map folder (default %(default)s)")
    ap.add_argument("--check", action="store_true", help="compare a fresh generation with --out and the manifest")
    ap.add_argument("--no-classes", action="store_true", help="leave out sim/classes.json (as on a machine without the game)")
    ap.add_argument("--no-manifest", action="store_true", help="do not write fixtures/testrange/stations.json")
    ap.add_argument("--stations-out", type=Path, help="write the stations manifest here instead (tests compare it)")
    args = ap.parse_args(argv)
    if args.check:
        diffs = check(args.out)
        for line in diffs:
            print(line)
        print("up to date" if not diffs else f"{len(diffs)} difference(s)")
        return 1 if diffs else 0
    if args.out.exists() and any(args.out.iterdir()) and not (args.out / "manifest.json").exists():
        print(f"{args.out} is not empty and is not an SVMap folder; refusing to overwrite", file=sys.stderr)
        return 2
    manifest, stations = generate(args.out, with_classes=not args.no_classes)
    if args.stations_out is not None:
        args.stations_out.parent.mkdir(parents=True, exist_ok=True)
        args.stations_out.write_bytes(canonical.dump_file(stations))
    elif not args.no_manifest:
        MANIFEST.parent.mkdir(parents=True, exist_ok=True)
        MANIFEST.write_bytes(canonical.dump_file(stations))
    from svmbuild.validation import validate_svmap  # noqa: PLC0415

    errs = validate_svmap(args.out)
    for e in errs:
        print(e, file=sys.stderr)
    print(f"wrote {args.out} sim={manifest['hashes']['sim']} stations={len(stations['stations'])}")
    return 1 if errs else 0


if __name__ == "__main__":
    raise SystemExit(main())
