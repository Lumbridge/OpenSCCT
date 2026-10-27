"""Generate TestBox, a small synthetic SVMap made only of boxes (no game data).

    python -m svmbuild.testbox [--out DIR] [--check]

Layout (uu, UE space, Z up). Room A (spy spawn) at x 0..1024, y 0..1024; corridor east to Room B (merc spawn) at
x 1536..2560 with a sliding door Mover; corridor south to Room C (objective room) at y -1536..-512. Rooms are 512
high, corridors 256. Room A has a 30 degree ramp onto a platform and a ledge block with an LG GE; Room B has a
35 uu step stair up to a mezzanine whose edge carries an LG GE gated by GE ID 4, a zip line down to the floor, a
BlockingVolume and a trigger that toggles GE bit 4. Room C holds the disk (SFlag) and a terminal.

The output is byte-identical on every run: no clocks, no randomness, no libm transcendental calls.
"""

from __future__ import annotations

import argparse
import math
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from .canonical import to_f32
from .paths import TESTBOX_FIXTURE
from .svmap import FORMAT_VERSION, with_derived, write_svmap
from .writers import collision as C
from .writers.glb import GlbBuilder, ue_dirs_to_gltf, ue_points_to_gltf, ue_tris_to_gltf

GENERATOR = "svmbuild.testbox"
GENERATOR_VERSION = 1
MAP_ID = "TestBox"
T = 32.0                       # wall, floor and ceiling thickness
STEP_RISE = 35.0
STEP_RUN = 48.0
STEPS = 8
RAMP_RUN = 256.0
RAMP_RISE = to_f32(RAMP_RUN / math.sqrt(3.0))   # tan(30 deg) = 1 / sqrt(3); sqrt is exactly rounded
MEZZ_TOP = STEP_RISE * STEPS                   # 280
LEDGE_TOP = 224.0
SPAWN_Z = 100.0                                # pawns spawn above the floor and settle

Vec3 = tuple[float, float, float]

# Collision materials (TFLG order) and their render colours (linear RGBA).
MATERIALS = {
    "Concrete": (0, []),
    "Metal": (0, []),
    "Ceiling": (0, ["noStickyCam"]),
    "Ledge": (0, ["climbable"]),
    "Ramp": (0, ["noSplitJump"]),
    "Blocker": (0, ["invisible"]),
    "Door": (0, []),
}
COLOURS = {
    "floor": (0.30, 0.30, 0.32, 1.0),
    "wall": (0.62, 0.62, 0.60, 1.0),
    "ceiling": (0.45, 0.45, 0.48, 1.0),
    "metal": (0.72, 0.45, 0.20, 1.0),
    "ledge": (0.25, 0.60, 0.30, 1.0),
    "ramp": (0.25, 0.40, 0.75, 1.0),
    "door": (0.75, 0.20, 0.20, 1.0),
}
WORLD_FLAGS = ["collideActors", "blockActors", "blockPlayers", "blockZeroExtent", "blockNonZeroExtent", "blockCamera"]
VOLUME_FLAGS = ["collideActors", "blockActors", "blockPlayers", "blockNonZeroExtent"]
TOUCH_FLAGS = ["collideActors"]


@dataclass
class Solid:
    name: str
    corners: list[Vec3]
    faces: list[tuple[int, ...]]
    material: str
    colour: str


def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _outward(corners: list[Vec3], faces: list[tuple[int, ...]]) -> list[tuple[int, ...]]:
    """Order every face so (v1 - v0) x (v2 - v0) points out of the convex solid (UE2 FPoly rule)."""
    n = len(corners)
    centre = tuple(sum(c[i] for c in corners) / n for i in range(3))
    out = []
    for f in faces:
        v = [corners[i] for i in f]
        normal = _cross(_sub(v[1], v[0]), _sub(v[2], v[0]))
        mid = tuple(sum(p[i] for p in v) / len(v) for i in range(3))
        out.append(f if _dot(normal, _sub(mid, centre)) > 0 else tuple(reversed(f)))
    return out


def box(name: str, lo: Vec3, hi: Vec3, material: str, colour: str) -> Solid:
    x0, y0, z0 = lo
    x1, y1, z1 = hi
    if not (x0 < x1 and y0 < y1 and z0 < z1):
        raise ValueError(f"{name}: empty box")
    corners = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
               (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    faces = [(0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return Solid(name, corners, _outward(corners, faces), material, colour)


def ramp(name: str, x0: float, x1: float, y0: float, y1: float, rise: float, material: str, colour: str) -> Solid:
    """Wedge rising along +X from the floor at x0 to `rise` at x1."""
    corners = [(x0, y0, 0.0), (x1, y0, 0.0), (x1, y0, rise), (x0, y1, 0.0), (x1, y1, 0.0), (x1, y1, rise)]
    faces = [(0, 1, 4, 3), (1, 2, 5, 4), (0, 2, 5, 3), (0, 1, 2), (3, 4, 5)]
    return Solid(name, corners, _outward(corners, faces), material, colour)


def _wall_pieces(name, run_axis, u0, u1, fixed, z0, z1, opening, material, colour):
    """Solid wall along run_axis ('x' or 'y') from u0 to u1, thickness span `fixed`, with an optional doorway."""
    spans = [(u0, u1, z0, z1)] if opening is None else [
        (u0, opening[0], z0, z1), (opening[1], u1, z0, z1), (opening[0], opening[1], opening[2], z1)]
    out = []
    for i, (a, b, lo_z, hi_z) in enumerate(spans):
        if b <= a or hi_z <= lo_z:
            continue
        if run_axis == "x":
            lo, hi = (a, fixed[0], lo_z), (b, fixed[1], hi_z)
        else:
            lo, hi = (fixed[0], a, lo_z), (fixed[1], b, hi_z)
        out.append(box(f"{name}{i}", lo, hi, material, colour))
    return out


def room(name: str, lo: Vec3, hi: Vec3, openings: dict[str, tuple[float, float, float]]) -> list[Solid]:
    """Floor, ceiling and four walls around the interior lo..hi. openings: side -> (from, to, top z)."""
    x0, y0, z0 = lo
    x1, y1, z1 = hi
    out = [box(f"{name}_Floor", (x0 - T, y0 - T, z0 - T), (x1 + T, y1 + T, z0), "Concrete", "floor"),
           box(f"{name}_Ceiling", (x0 - T, y0 - T, z1), (x1 + T, y1 + T, z1 + T), "Ceiling", "ceiling")]
    out += _wall_pieces(f"{name}_WallW", "y", y0, y1, (x0 - T, x0), z0, z1, openings.get("W"), "Concrete", "wall")
    out += _wall_pieces(f"{name}_WallE", "y", y0, y1, (x1, x1 + T), z0, z1, openings.get("E"), "Concrete", "wall")
    out += _wall_pieces(f"{name}_WallS", "x", x0 - T, x1 + T, (y0 - T, y0), z0, z1, openings.get("S"), "Concrete",
                        "wall")
    out += _wall_pieces(f"{name}_WallN", "x", x0 - T, x1 + T, (y1, y1 + T), z0, z1, openings.get("N"), "Concrete",
                        "wall")
    return out


def corridor(name: str, axis: str, a0: float, a1: float, b0: float, b1: float, height: float) -> list[Solid]:
    """Corridor along axis from a0 to a1, interior b0..b1 across, open at both ends."""
    if axis == "x":
        def bx(n, lo_b, hi_b, lo_z, hi_z, mat, col):
            return box(n, (a0, lo_b, lo_z), (a1, hi_b, hi_z), mat, col)
    else:
        def bx(n, lo_b, hi_b, lo_z, hi_z, mat, col):
            return box(n, (lo_b, a0, lo_z), (hi_b, a1, hi_z), mat, col)
    return [bx(f"{name}_Floor", b0 - T, b1 + T, -T, 0.0, "Concrete", "floor"),
            bx(f"{name}_Ceiling", b0 - T, b1 + T, height, height + T, "Ceiling", "ceiling"),
            bx(f"{name}_Wall0", b0 - T, b0, 0.0, height, "Concrete", "wall"),
            bx(f"{name}_Wall1", b1, b1 + T, 0.0, height, "Concrete", "wall")]


def static_world() -> list[Solid]:
    door = (448.0, 576.0, 256.0)
    solids = []
    solids += room("RoomA", (0.0, 0.0, 0.0), (1024.0, 1024.0, 512.0), {"E": door, "S": door})
    solids += room("RoomB", (1536.0, 0.0, 0.0), (2560.0, 1024.0, 512.0), {"W": door})
    solids += room("RoomC", (0.0, -1536.0, 0.0), (1024.0, -512.0, 512.0), {"N": door})
    solids += corridor("CorridorAB", "x", 1024.0, 1536.0, 448.0, 576.0, 256.0)
    solids += corridor("CorridorAC", "y", -512.0, 0.0, 448.0, 576.0, 256.0)
    # Room A: 30 degree ramp onto a platform, and a ledge block.
    solids.append(ramp("RoomA_Ramp", 512.0, 768.0, 768.0, 1024.0, RAMP_RISE, "Ramp", "ramp"))
    solids.append(box("RoomA_Platform", (768.0, 768.0, 0.0), (1024.0, 1024.0, RAMP_RISE), "Ramp", "ramp"))
    solids.append(box("RoomA_LedgeBlock", (128.0, 832.0, 0.0), (384.0, 1024.0, LEDGE_TOP), "Ledge", "ledge"))
    # Room B: 35 uu steps up to the mezzanine.
    for k in range(1, STEPS + 1):
        y_lo = 832.0 - STEP_RUN * (STEPS + 1 - k)
        solids.append(box(f"RoomB_Step{k}", (2304.0, y_lo, 0.0), (2496.0, y_lo + STEP_RUN, STEP_RISE * k), "Metal",
                          "metal"))
    solids.append(box("RoomB_Mezzanine", (1536.0, 832.0, 0.0), (2560.0, 1024.0, MEZZ_TOP), "Metal", "metal"))
    return solids


DOOR_LOCATION = (1280.0, 512.0, 0.0)
DOOR_LOCAL = ((-8.0, -64.0, 0.0), (8.0, 64.0, 256.0))
DOOR_OPEN_OFFSET = (0.0, 0.0, 240.0)
VOLUME_LOCATION = (2528.0, 928.0, 396.0)
VOLUME_YAW = 16384
VOLUME_LOCAL = ((-96.0, -32.0, -116.0), (96.0, 32.0, 116.0))


def _rot_yaw90(p: Vec3, yaw: int) -> Vec3:
    """UE2 FRotationMatrix for yaw multiples of 16384 (exact): yaw 16384 maps (x, y, z) to (-y, x, z)."""
    turns = (yaw // 16384) % 4
    if yaw % 16384:
        raise ValueError("only quarter-turn yaws are used in TestBox")
    x, y, z = p
    for _ in range(turns):
        x, y = -y, x
    return (x, y, z)


def _aabb(points) -> tuple[Vec3, Vec3]:
    xs, ys, zs = zip(*points)
    return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))


def _box_corners(lo: Vec3, hi: Vec3) -> list[Vec3]:
    return box("tmp", lo, hi, "Concrete", "wall").corners


def _box_planes(lo: Vec3, hi: Vec3) -> list[tuple[float, float, float, float]]:
    return [(-1.0, 0.0, 0.0, -lo[0]), (1.0, 0.0, 0.0, hi[0]), (0.0, -1.0, 0.0, -lo[1]), (0.0, 1.0, 0.0, hi[1]),
            (0.0, 0.0, -1.0, -lo[2]), (0.0, 0.0, 1.0, hi[2])]


def _f(v: Vec3) -> dict:
    return {"X": float(v[0]), "Y": float(v[1]), "Z": float(v[2])}


def _rot(yaw: int = 0, pitch: int = 0, roll: int = 0) -> dict:
    return {"Pitch": pitch, "Yaw": yaw, "Roll": roll}


def _ref(actor_id: str) -> dict:
    return {"$actor": actor_id}


def build_gameplay() -> dict:
    level_info = {"id": "LevelInfo0", "class": "Engine.LevelInfo", "props": {
        "Title": "TestBox", "Author": "SVM", "RecommendedNumPlayers": 4, "GEActivationMask": 4, "Tag": "LevelInfo"}}
    a = []

    def add(actor_id: str, cls: str, **props) -> None:
        a.append({"id": actor_id, "class": cls, "props": props})

    add("ZoneInfo0", "Engine.ZoneInfo", Location=_f((512.0, 512.0, 256.0)), LocationName="Room A", Tag="ZoneInfo")
    add("ZoneInfo1", "Engine.ZoneInfo", Location=_f((2048.0, 512.0, 256.0)), LocationName="Room B", Tag="ZoneInfo")
    add("ZoneInfo2", "Engine.ZoneInfo", Location=_f((512.0, -1024.0, 256.0)), LocationName="Room C", Tag="ZoneInfo")
    # TeamNumber 0: spies, 1: mercs.
    add("PlayerStart0", "Engine.PlayerStart", Location=_f((128.0, 128.0, SPAWN_Z)), Rotation=_rot(0), TeamNumber=0,
        Tag="PlayerStart")
    add("PlayerStart1", "Engine.PlayerStart", Location=_f((128.0, 384.0, SPAWN_Z)), Rotation=_rot(0), TeamNumber=0,
        Tag="PlayerStart")
    add("PlayerStart2", "Engine.PlayerStart", Location=_f((1792.0, 256.0, SPAWN_Z)), Rotation=_rot(32768),
        TeamNumber=1, Tag="PlayerStart")
    add("PlayerStart3", "Engine.PlayerStart", Location=_f((2048.0, 256.0, SPAWN_Z)), Rotation=_rot(32768),
        TeamNumber=1, Tag="PlayerStart")
    add("Mover0", "Engine.Mover", Location=_f(DOOR_LOCATION), BasePos=_f(DOOR_LOCATION), Rotation=_rot(0),
        InitialState="TriggerOpenTimed", NumKeys=2,
        KeyPos={"$elements": {"0": _f((0.0, 0.0, 0.0)), "1": _f(DOOR_OPEN_OFFSET)}},
        MoveTime=1.0, StayOpenTime=4.0, StaticMesh={"$asset": "TestBox.Meshes.SlidingDoor"}, DrawType=8,
        bCollideActors=True, bBlockActors=True, bBlockPlayers=True, Tag="TestDoor")
    add("Trigger0", "Engine.Trigger", Location=_f((1280.0, 512.0, 64.0)), CollisionRadius=160.0,
        CollisionHeight=64.0, bCollideActors=True, Event="TestDoor", Tag="Trigger")
    add("SComputerObjectiveTrigger0", "SBase.SComputerObjectiveTrigger", Location=_f((2496.0, 128.0, 64.0)),
        Rotation=_rot(32768), DominationCaptureTime=10, TriggerName="Terminal B", CollisionRadius=40.0,
        CollisionHeight=40.0, bCollideActors=True, Tag="SComputerObjectiveTrigger")
    add("SComputerObjectiveTrigger1", "SBase.SComputerObjectiveTrigger", Location=_f((512.0, -1472.0, 64.0)),
        Rotation=_rot(16384), DominationCaptureTime=10, TriggerName="Terminal C", CollisionRadius=40.0,
        CollisionHeight=40.0, bCollideActors=True, Tag="SComputerObjectiveTrigger")
    add("SFlag0", "SBase.SFlag", Location=_f((128.0, -1408.0, 32.0)), Rotation=_rot(0), DropZone=_ref("SFlagDropZone0"),
        ResetTime=15, TriggerName="Test Files", fInteractionRadius=50.0, fInteractionHeight=30.0,
        CollisionRadius=50.0, CollisionHeight=30.0, bCollideActors=True, Tag="SFlag")
    add("SFlagDropZone0", "SBase.SFlagDropZone", Location=_f((896.0, 128.0, 32.0)), TriggerName="Test Files",
        fInteractionRadius=50.0, fInteractionHeight=30.0, CollisionRadius=50.0, CollisionHeight=30.0,
        bCollideActors=True, Tag="SFlagDropZone")
    for i, (target, name, desc) in enumerate([("SComputerObjectiveTrigger0", "Terminal B", "Terminal: Terminal B"),
                                              ("SComputerObjectiveTrigger1", "Terminal C", "Terminal: Terminal C"),
                                              ("SFlag0", "Test Files", "Disk: Test Files")]):
        add(f"SObjective{i}", "SBase.SObjective", Location=_f((512.0, 512.0, 256.0 + 16.0 * i)),
            Triggers=[_ref(target)], ObjectiveName=name, Description=desc, DescriptionDEF=desc, Tag="SObjective")
    add("SMission0", "SBase.SMission", Location=_f((512.0, 512.0, 320.0)),
        Objectives=[_ref("SObjective0"), _ref("SObjective1"), _ref("SObjective2")], MinimumObjectives=2, GameMode=5,
        bChained=False, ObjectiveName="", Description="Accomplish 2 out of 3 objectives.",
        DescriptionDEF="Prevent the intruders from reaching 2 of the 3 objectives.", Tag="SMission")
    add("Trigger1", "Engine.Trigger", Location=_f((1792.0, 704.0, 64.0)), CollisionRadius=64.0, CollisionHeight=64.0,
        bCollideActors=True, Event="TestGEToggle", Tag="Trigger")
    add("SGETrigger0", "SBase.SGETrigger", Location=_f((1920.0, 928.0, 380.0)), GEMask=4, bToggle=True,
        Tag="TestGEToggle")
    add("BlockingVolume0", "Engine.BlockingVolume", Location=_f(VOLUME_LOCATION), Rotation=_rot(VOLUME_YAW),
        bCollideActors=True, bBlockActors=True, bBlockPlayers=True, bBlockZeroExtent=False,
        bBlockNonZeroExtent=True, Tag="BlockingVolume")
    gameplay = {
        "formatVersion": FORMAT_VERSION,
        "map": MAP_ID,
        "levelInfo": level_info,
        "mapSettings": {"Story": {"TimeLimit": 10, "SpiesLives": 4, "SentriesLives": 3, "RespawnTime": 10}},
        "actors": a,
    }
    return with_derived(gameplay)


def build_traversal() -> dict:
    synthetic = {"kind": "synthetic"}
    ges = [
        {"id": "GE0", "kind": "LG", "points": [[128.0, 832.0, LEDGE_TOP], [384.0, 832.0, LEDGE_TOP]], "mask": 0,
         "platform": 0, "src": dict(synthetic, note="Room A ledge block")},
        {"id": "GE1", "kind": "LG", "points": [[1600.0, 832.0, MEZZ_TOP], [2240.0, 832.0, MEZZ_TOP]], "mask": 4,
         "platform": 0, "src": dict(synthetic, note="Room B mezzanine edge, cleared by SGETrigger0")},
        {"id": "GE2", "kind": "ZL", "points": [[1600.0, 960.0, 400.0], [2400.0, 128.0, 150.0]], "mask": 0,
         "platform": 0, "src": dict(synthetic, note="Room B zip line")},
    ]
    return {"formatVersion": FORMAT_VERSION, "map": MAP_ID, "ges": ges}


def _flags(names) -> int:
    return C.flag_bits(names, C.COLLIDER_FLAGS)


def build_collision(world: list[Solid], gameplay: dict) -> bytes:
    d = C.CollisionData()
    mat_index = {}
    for name, (surface, flags) in MATERIALS.items():
        mat_index[name] = len(d.materials)
        d.materials.append(C.Material(surface, C.flag_bits(flags, C.MATERIAL_FLAGS),
                                      d.string(f"TestBox.Materials.{name}")))

    # Collider 0: the static world, collided by its triangles (no BSP nodes).
    vert_index: dict[Vec3, int] = {}
    for s in world:
        for f in s.faces:
            for i in range(1, len(f) - 1):
                tri = []
                for c in (f[0], f[i], f[i + 1]):
                    p = s.corners[c]
                    if p not in vert_index:
                        vert_index[p] = len(d.verts)
                        d.verts.append(p)
                    tri.append(vert_index[p])
                d.tris.append((tri[0], tri[1], tri[2], 0, mat_index[s.material]))
    lo, hi = _aabb(d.verts)
    d.colliders.append(C.Collider(kind=C.KINDS.index("worldBsp"), flags=_flags(WORLD_FLAGS), vert_start=0,
                                  vert_count=len(d.verts), tri_start=0, tri_count=len(d.tris), aabb_min=lo,
                                  aabb_max=hi))
    world_lo, world_hi = lo, hi

    def hull_collider(kind: str, actor: str, flags, material: str, local: tuple[Vec3, Vec3], location: Vec3,
                      yaw: int) -> None:
        corners = _box_corners(*local)
        v0 = len(d.verts)
        d.verts.extend(corners)
        p0 = len(d.planes)
        d.planes.extend(_box_planes(*local))
        h = len(d.hulls)
        d.hulls.append(C.Hull(p0, 6, v0, 8, mat_index[material], local[0], local[1]))
        world_pts = [tuple(a + b for a, b in zip(_rot_yaw90(c, yaw), location)) for c in corners]
        lo_w, hi_w = _aabb(world_pts)
        d.colliders.append(C.Collider(kind=C.KINDS.index(kind), flags=_flags(flags), actor=d.string(actor),
                                      vert_start=v0, vert_count=8, hull_start=h, hull_count=1, location=location,
                                      rotation=(0, yaw, 0), aabb_min=lo_w, aabb_max=hi_w))

    hull_collider("mover", "Mover0", WORLD_FLAGS, "Door", DOOR_LOCAL, DOOR_LOCATION, 0)
    hull_collider("brush", "BlockingVolume0", VOLUME_FLAGS, "Blocker", VOLUME_LOCAL, VOLUME_LOCATION, VOLUME_YAW)

    for actor in gameplay["actors"]:
        p = actor["props"]
        if "CollisionRadius" in p and p.get("bCollideActors"):
            loc = p["Location"]
            d.cylinders.append(C.Cylinder(d.string(actor["id"]), _flags(TOUCH_FLAGS), (loc["X"], loc["Y"], loc["Z"]),
                                          p["CollisionRadius"], p["CollisionHeight"]))

    d.zones.append(C.Zone(d.string("LevelInfo0"), 0, world_lo, world_hi))
    for zone_id, lo_z, hi_z in [("ZoneInfo0", (0.0, 0.0, 0.0), (1024.0, 1024.0, 512.0)),
                                ("ZoneInfo1", (1536.0, 0.0, 0.0), (2560.0, 1024.0, 512.0)),
                                ("ZoneInfo2", (0.0, -1536.0, 0.0), (1024.0, -512.0, 512.0))]:
        d.zones.append(C.Zone(d.string(zone_id), 0, lo_z, hi_z))
    return C.write_collision(d)


def _render_mesh(solids: list[Solid]):
    """Flat-shaded triangles in glTF space: every face gets its own corners, normal and colour."""
    pos, nrm, col, idx = [], [], [], []
    for s in solids:
        colour = COLOURS[s.colour]
        for f in s.faces:
            v = [s.corners[i] for i in f]
            n = _cross(_sub(v[1], v[0]), _sub(v[2], v[0]))
            length = math.sqrt(_dot(n, n))
            n = (n[0] / length, n[1] / length, n[2] / length)
            base = len(pos)
            pos.extend(v)
            nrm.extend([n] * len(v))
            col.extend([colour] * len(v))
            for i in range(1, len(f) - 1):
                idx.append((base, base + i, base + i + 1))
    return (ue_points_to_gltf(np.array(pos)), ue_dirs_to_gltf(np.array(nrm)), np.array(col, dtype=np.float32),
            ue_tris_to_gltf(np.array(idx)))


def build_render(world: list[Solid]) -> bytes:
    g = GlbBuilder(f"{GENERATOR} {GENERATOR_VERSION}")
    mat = g.material("TestBox.Materials.VertexColour")
    world_mesh = g.mesh("TestBox.World", *_render_mesh(world), material=mat)
    door = box("Door", DOOR_LOCAL[0], DOOR_LOCAL[1], "Door", "door")
    door_mesh = g.mesh("TestBox.Meshes.SlidingDoor", *_render_mesh([door]), material=mat)
    g.node("World", mesh=world_mesh, extras={"svmap": {"role": "world"}}, root=True)
    lib = g.node("TestBox.Meshes.SlidingDoor", mesh=door_mesh, extras={"svmap": {"role": "libraryMesh"}})
    g.node("Library", children=[lib], extras={"svmap": {"role": "library"}}, root=True)
    return g.to_bytes()


def generate(out: Path) -> dict:
    """Write TestBox to `out` and return its manifest."""
    world = static_world()
    gameplay = build_gameplay()
    return write_svmap(
        Path(out),
        map_id=MAP_ID,
        title="TestBox",
        source={"kind": "synthetic", "generator": GENERATOR, "generatorVersion": GENERATOR_VERSION},
        gameplay=gameplay,
        traversal=build_traversal(),
        collision=build_collision(world, gameplay),
        render={"world.glb": build_render(world)},
    )


def _tree(root: Path) -> dict[str, bytes]:
    return {p.relative_to(root).as_posix(): p.read_bytes() for p in sorted(root.rglob("*")) if p.is_file()}


def check(out: Path) -> list[str]:
    """Differences between a fresh generation and the folder at `out`."""
    with tempfile.TemporaryDirectory() as tmp:
        generate(Path(tmp) / MAP_ID)
        fresh = _tree(Path(tmp) / MAP_ID)
    have = _tree(Path(out)) if Path(out).exists() else {}
    diffs = [f"differs: {k}" for k in sorted(fresh) if k in have and have[k] != fresh[k]]
    diffs += [f"missing: {k}" for k in sorted(fresh) if k not in have]
    diffs += [f"extra: {k}" for k in sorted(have) if k not in fresh]
    return diffs


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="python -m svmbuild.testbox", description="Generate the TestBox SVMap.")
    ap.add_argument("--out", type=Path, default=TESTBOX_FIXTURE, help="output folder (default %(default)s)")
    ap.add_argument("--check", action="store_true", help="compare a fresh generation with --out instead of writing")
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
    manifest = generate(args.out)
    from .validation import validate_svmap  # noqa: PLC0415 - jsonschema is only needed here

    errs = validate_svmap(args.out)
    for e in errs:
        print(e, file=sys.stderr)
    print(f"wrote {args.out} sim={manifest['hashes']['sim']}")
    return 1 if errs else 0


if __name__ == "__main__":
    raise SystemExit(main())
