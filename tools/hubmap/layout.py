"""Hub: the layout of the OpenSCCT social hub map, as data. An OpenSCCT addition (spec/additions/hub.md), not a port:
our own geometry (one carved hall, boxes and wedges), our own procedural art (tools/hubmap/art.py), no game content.

The look: a night rooftop compound over a city. The hall's walls are a painted skyline behind a parapet with a green
neon line, the ceiling a starry sky; concrete roof decking, neon strips (OpenSCCT green for spies, amber for mercs),
our own signage in JetBrains Mono (OFL). Units are Unreal units in UE space (left-handed, Z up, yaw 0 = +X,
16384 = +Y). The hall is x 0..12288, y 0..12288, z 0..1536. "North" is +Y, "east" +X. Areas:

- the spawn plaza in the middle: 64 player starts on two rings (spies west, mercs east) round a fountain with ledges
  on its lips, the OpenSCCT ring inlaid in the pavers, a four-sided "OPENSCCT HUB" sign hanging over the fountain,
  neon pylons at the corners, benches, side banners and floor arrows to every area;
- the traversal garden in the north: the TestRange station kit at the TestRange heights (ledges 128 to 320, a pipe,
  a ladder, a wall jump wall, a vault barrier, a fence, a hand over hand bar, steps and stairs, a ramp up to a deck
  with a tower and a zip line down toward the plaza), on turf with trees and a label at every station;
- the match pads in the east: four railed courts with corner posts, a sign gantry over each, bleachers to the north
  and east. Each pad wears a swappable theme kit (render-only dressing evoking a map: Polar Base, Steel Squat...);
  the client shows one kit per pad (game/Scripts/Hub/HubPadThemes.cs);
- the merc aim range in the west: a booth with five stalls behind a counter, painted targets on the berm, distance
  marks, a leaderboard board facing the plaza;
- the spy movement course in the south: a numbered route of vault, climb, fence, hand over hand, pipe or ladder up a
  tower and a zip line down to the finish deck, with gates and floor arrows, and a leaderboard board;
- the lounge in the south-west: sofas facing a big screen with a scrolling ticker, a bar, warm light;
- rooftop dressing in the other corners: a water tower landmark, air-conditioning units, a helipad.

Nothing here reads files or the clock: the layout is the same on every run. tools/hubmap/generate.py writes the
SVMap folder (fixtures/hub) and the area manifest (fixtures/hubmap/areas.json).
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field

MAP_ID = "Hub"
TITLE = "Hub"
LAYOUT_VERSION = 2

Vec3 = tuple[float, float, float]

HALL_LO: Vec3 = (0.0, 0.0, 0.0)
HALL_HI: Vec3 = (12288.0, 12288.0, 1536.0)
CENTRE = (6144.0, 6144.0)


@dataclass(frozen=True)
class Material:
    key: str
    surface: int             # ESurfaceType the collision carries (footsteps, impacts)
    occlusion: int           # OcclusionType (sound through the surface)
    climbable: bool
    art: str                 # the texture: an art.TILES kind, or a key of ART (signs, decals)
    tile: tuple[float, float] = (256.0, 256.0)   # world UV tile (uu) for faces without their own UVs
    unlit: bool = False      # fullbright: neon, signs, screens, the sky
    uv_anim: dict | None = None


MATERIALS: dict[str, Material] = {}
# Art that is not a tile: key -> (function name in art, args, kwargs). generate.py draws them.
ART: dict[str, tuple[str, tuple, dict]] = {}


def mat(key, art=None, surface=5, occlusion=8, climbable=False, tile=(256.0, 256.0), unlit=False, uv_anim=None):
    if key not in MATERIALS:
        MATERIALS[key] = Material(key, surface, occlusion, climbable, art or key, tile, unlit, uv_anim)
    return key


def art_mat(key, fn, *args, unlit=True, uv_anim=None, **kwargs):
    """A material with its own drawn texture (a sign, a decal); UVs come from the piece."""
    ART[key] = (fn, args, kwargs)
    return mat(key, key, unlit=unlit, uv_anim=uv_anim)


# World and solid materials (collision and render).
mat("roof", tile=(512.0, 512.0))
mat("skyline", tile=(4096.0, 1536.0), unlit=True)
mat("sky", tile=(2048.0, 2048.0), unlit=True)
mat("concrete")
mat("darkpanel")
mat("plaza")
mat("turf", tile=(512.0, 512.0))
mat("carpet")
mat("court")
mat("track")
mat("range")
mat("booth")
mat("wood", tile=(128.0, 128.0))
mat("deck", surface=17)
mat("metal", surface=14, occlusion=4)
mat("fence", surface=22, occlusion=1, climbable=True)
mat("hedge", tile=(128.0, 128.0))
mat("foliage")
mat("bark", tile=(128.0, 128.0))
mat("sofa", tile=(128.0, 128.0))
mat("sofa2", tile=(128.0, 128.0))
mat("crate", tile=(128.0, 128.0))
mat("hvac", tile=(128.0, 128.0))
mat("screen", tile=(128.0, 128.0), unlit=True)
for _n in ("green", "amber", "cyan", "magenta", "white", "ice", "sodium", "teal", "lab", "gold", "red"):
    mat(f"neon_{_n}", tile=(64.0, 64.0), unlit=True)


@dataclass(frozen=True)
class Solid:
    name: str
    lo: Vec3
    hi: Vec3
    material: str
    up: str | None = None        # None: a box; '+x' '-x' '+y' '-y': a wedge rising that way from lo.z to hi.z


@dataclass(frozen=True)
class GE:
    id: str
    kind: str                    # LG PIPE LADDER ZL HOH FENCE
    points: tuple[Vec3, ...]
    note: str


@dataclass(frozen=True)
class Light:
    id: str
    location: Vec3
    brightness: float
    radius: float                # LightRadius (the reach is 25 x (LightRadius + 1))
    hue: int = 0                 # LightHue / LightSaturation, UE's FGetHSV (saturation 255 is white)
    saturation: int = 255
    kit: tuple[str, str] | None = None   # (pad, theme): baked into that theme kit only, not a map light


@dataclass
class Actor:
    id: str
    cls: str
    location: Vec3
    yaw: int = 0
    pitch: int = 0
    props: dict = field(default_factory=dict)


@dataclass(frozen=True)
class Area:
    """A part of the hub: where it is and what it is for."""
    id: str
    kind: str                    # spawn, traversal, matchPads, aimRange, movementCourse, lounge
    title: str
    lo: tuple[float, float]
    hi: tuple[float, float]
    phase: str                   # H1 (in use), H2, H3
    data: dict


@dataclass
class Piece:
    """A render-only polygon: corners (convex, (v1 - v0) x (v2 - v0) is its front), a material, UVs or None (world)."""
    corners: list[Vec3]
    material: str
    uvs: list[tuple[float, float]] | None = None


@dataclass
class Group:
    """Render-only pieces drawn as one mesh per material. A kit group is one pad's theme dressing (shown or hidden)."""
    name: str
    pieces: list[Piece] = field(default_factory=list)
    kit: tuple[str, str] | None = None


SOLIDS: list[Solid] = []
GES: list[GE] = []
ACTORS: list[Actor] = []
LIGHTS: list[Light] = []
AREAS: list[Area] = []
GROUPS: dict[str, Group] = {}
# Floor zones (lo, hi, material): the hall floor inside each rectangle draws with that material (first match wins).
FLOOR_ZONES: list[tuple[tuple[float, float], tuple[float, float], str]] = []


def solid(name, lo, hi, material="concrete", up=None):
    if not all(a < b for a, b in zip(lo, hi)):
        raise ValueError(f"{name}: empty solid")
    SOLIDS.append(Solid(name, tuple(map(float, lo)), tuple(map(float, hi)), material, up))


def ge(ident, kind, *points, note=""):
    # A zip line's low end comes first, as in every real map: physLinear and the ZipLine states face the rider along
    # P1 - P2 (ZipP1 - ZipP2) flattened, so a high-first ZL rides backwards.
    if kind == "ZL" and points[0][2] > points[1][2]:
        raise ValueError(f"{ident}: a ZL lists its low end first")
    GES.append(GE(ident, kind, tuple(tuple(map(float, p)) for p in points), note))
    return ident


def actor(ident, cls, location, yaw=0, pitch=0, **props):
    a = Actor(ident, cls, tuple(map(float, location)), int(yaw), int(pitch), props)
    ACTORS.append(a)
    return a


def area(ident, kind, title, lo, hi, phase, **data):
    AREAS.append(Area(ident, kind, title, tuple(map(float, lo)), tuple(map(float, hi)), phase, data))


def light(ident, location, brightness, radius, hue=0, saturation=255, kit=None):
    LIGHTS.append(Light(ident, tuple(map(float, location)), float(brightness), float(radius), hue, saturation, kit))


def floor_zone(lo, hi, material):
    FLOOR_ZONES.append((tuple(map(float, lo)), tuple(map(float, hi)), material))


# ---- render-only pieces --------------------------------------------------------------------------------------------

def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def group(name, kit=None) -> Group:
    if name not in GROUPS:
        GROUPS[name] = Group(name, kit=kit)
    return GROUPS[name]


def piece(g, corners, material, uvs=None, normal=None):
    """Adds a polygon; with `normal`, the winding is turned so its front faces that way."""
    corners = [tuple(map(float, c)) for c in corners]
    if normal is not None:
        n = _cross(_sub(corners[1], corners[0]), _sub(corners[2], corners[0]))
        if _dot(n, normal) < 0:
            corners = corners[::-1]
            uvs = uvs[::-1] if uvs else uvs
    group(g).pieces.append(Piece(corners, material, uvs))


def box(g, lo, hi, material, faces="all"):
    """A render-only box (outward faces); `faces` 'all' or a string of the faces to keep: t b n s e w."""
    x0, y0, z0 = lo
    x1, y1, z1 = hi
    sides = {
        "t": ([(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], (0, 0, 1)),
        "b": ([(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)], (0, 0, -1)),
        "n": ([(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)], (0, 1, 0)),
        "s": ([(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], (0, -1, 0)),
        "e": ([(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)], (1, 0, 0)),
        "w": ([(x0, y0, z0), (x0, y1, z0), (x0, y1, z1), (x0, y0, z1)], (-1, 0, 0)),
    }
    for k, (corners, n) in sides.items():
        if faces == "all" or k in faces:
            piece(g, corners, material, normal=n)


def skin(g, s: Solid, material, grow=1.0):
    """A render-only box 1 uu round a solid, in another material (theme kits re-dress shared collision)."""
    box(g, (s.lo[0] - grow, s.lo[1] - grow, s.lo[2]), (s.hi[0] + grow, s.hi[1] + grow, s.hi[2] + grow), material,
        faces="tnsew")


def beam(g, a, b, t, material):
    """A square bar of side t from a to b (cables, rungs, rails)."""
    d = _sub(b, a)
    length = math.sqrt(_dot(d, d))
    d = (d[0] / length, d[1] / length, d[2] / length)
    up = (0.0, 0.0, 1.0) if abs(d[2]) < 0.9 else (1.0, 0.0, 0.0)
    u = _cross(d, up)
    ul = math.sqrt(_dot(u, u))
    u = (u[0] / ul * t / 2, u[1] / ul * t / 2, u[2] / ul * t / 2)
    v = _cross(d, u)
    vl = math.sqrt(_dot(v, v))
    v = (v[0] / vl * t / 2, v[1] / vl * t / 2, v[2] / vl * t / 2)
    ring = [(1, 1), (-1, 1), (-1, -1), (1, -1)]

    def at(p, su, sv):
        return (p[0] + su * u[0] + sv * v[0], p[1] + su * u[1] + sv * v[1], p[2] + su * u[2] + sv * v[2])
    for i in range(4):
        (su0, sv0), (su1, sv1) = ring[i], ring[(i + 1) % 4]
        out = (su0 + su1) / 2 * u[0] + (sv0 + sv1) / 2 * v[0], (su0 + su1) / 2 * u[1] + (sv0 + sv1) / 2 * v[1], \
            (su0 + su1) / 2 * u[2] + (sv0 + sv1) / 2 * v[2]
        piece(g, [at(a, su0, sv0), at(b, su0, sv0), at(b, su1, sv1), at(a, su1, sv1)], material, normal=out)


def _right(f):
    """The reader's right when facing f (left-handed, Z up: yaw 0 faces +X with +Y on the right)."""
    return (-f[1], f[0])


def decal(g, centre, w, h, facing, material, z=0.6):
    """A floor quad of w (across) by h (along `facing`), readable by someone looking along `facing`."""
    fl = math.hypot(*facing)
    f = (facing[0] / fl, facing[1] / fl)
    r = _right(f)
    cx, cy = centre

    def p(sr, sf):
        return (cx + r[0] * sr * w / 2 + f[0] * sf * h / 2, cy + r[1] * sr * w / 2 + f[1] * sf * h / 2, z)
    piece(g, [p(-1, 1), p(1, 1), p(1, -1), p(-1, -1)], material, [(0, 0), (1, 0), (1, 1), (0, 1)], normal=(0, 0, 1))


def wall_sign(g, centre, w, h, normal, material, double=False, u_span=1.0):
    """A vertical quad facing `normal` (horizontal), w wide, h high, centred at `centre` (x, y, z); it shows the
    texture's first `u_span` across (a window onto a wider strip)."""
    nl = math.hypot(*normal)
    n = (normal[0] / nl, normal[1] / nl)
    f = (-n[0], -n[1])             # the reader looks into the sign
    r = _right(f)
    cx, cy, cz = centre

    def p(sr, sz):
        return (cx + r[0] * sr * w / 2, cy + r[1] * sr * w / 2, cz + sz * h / 2)
    piece(g, [p(-1, 1), p(1, 1), p(1, -1), p(-1, -1)], material, [(0, 0), (u_span, 0), (u_span, 1), (0, 1)],
          normal=(n[0], n[1], 0))
    if double:
        back = (cx - n[0] * 1.0, cy - n[1] * 1.0, cz)
        wall_sign(g, back, w, h, (-n[0], -n[1]), material, u_span=u_span)


def ledge(ident, a, b, note):
    """An LG along a top edge, a to b with the solid on the right (as the fountain's lips run)."""
    return ge(ident, "LG", a, b, note=note)


def railing(name, a, b, kit_rail=None):
    """A collision rail 16 thick and 96 high from a to b (axis-aligned), vaultable from both faces."""
    (x0, y0), (x1, y1) = a, b
    if y0 == y1:
        solid(name, (min(x0, x1), y0 - 8, 0), (max(x0, x1), y0 + 8, 96), "metal")
        ledge(name + "LGS", (min(x0, x1), y0 - 8, 96), (max(x0, x1), y0 - 8, 96), "rail top, south face")
        ledge(name + "LGN", (max(x0, x1), y0 + 8, 96), (min(x0, x1), y0 + 8, 96), "rail top, north face")
    else:
        solid(name, (x0 - 8, min(y0, y1), 0), (x0 + 8, max(y0, y1), 96), "metal")
        ledge(name + "LGW", (x0 - 8, max(y0, y1), 96), (x0 - 8, min(y0, y1), 96), "rail top, west face")
        ledge(name + "LGE", (x0 + 8, min(y0, y1), 96), (x0 + 8, max(y0, y1), 96), "rail top, east face")
    return SOLIDS[-1]


YAW_E, YAW_N, YAW_W, YAW_S = 0, 16384, 32768, 49152
N, S, E, W = (0, 1), (0, -1), (1, 0), (-1, 0)

# ---- Hall ----------------------------------------------------------------------------------------------------------
# Fill light: a sparse grid of dim, cool lights (night), so nothing is pitch black between the areas' own lights.
for i in range(6):
    for j in range(6):
        light(f"NightFill{i}{j}", (1024.0 + 2048 * i, 1024.0 + 2048 * j, 1300.0), 95, 90, hue=160, saturation=190)
ZONE_AMBIENT = 34
FOG = {"color": [16, 18, 34], "start": 3000.0, "end": 19000.0}

# ---- Spawn plaza ---------------------------------------------------------------------------------------------------
PLAZA_LO = (4608.0, 4608.0)
PLAZA_HI = (7680.0, 7680.0)
START_Z = 86.0               # a start stands 86 over its floor, as on the stock maps (SteeE yard starts: 85.5)
START_RINGS = (768.0, 1088.0)
STARTS_PER_RING_HALF = 16    # per ring, per side: 64 starts in all, so 50 players never share one
CX, CY = CENTRE
floor_zone(PLAZA_LO, PLAZA_HI, "plaza")

# The fountain: a low basin with a ledge round its lip, and a column in the middle whose ladder leads to a lookout.
solid("FountainBasin", (CX - 256, CY - 256, 0), (CX + 256, CY + 256, 96), "darkpanel")
for side, (a, b) in {"S": ((CX - 256, CY - 256), (CX + 256, CY - 256)), "N": ((CX + 256, CY + 256), (CX - 256, CY + 256)),
                     "W": ((CX - 256, CY + 256), (CX - 256, CY - 256)), "E": ((CX + 256, CY - 256), (CX + 256, CY + 256))}.items():
    ge(f"FountainLG{side}", "LG", (a[0], a[1], 96), (b[0], b[1], 96), note="basin lip: vault or climb")
solid("FountainColumn", (CX - 64, CY - 64, 96), (CX + 64, CY + 64, 416), "darkpanel")
ge("FountainLADDER", "LADDER", (CX, CY - 64 - 4, 96), (CX, CY - 64 - 4, 416), (CX, CY - 64 - 14, 96),
   note="from the basin up the column, top flush")
ge("FountainTopLG", "LG", (CX - 64, CY - 64, 416), (CX + 64, CY - 64, 416), note="column top, south lip")

spy_starts, merc_starts = [], []
for ring, radius in enumerate(START_RINGS):
    for k in range(STARTS_PER_RING_HALF):
        # Spies on the west half (angles 90 to 270 degrees), mercs on the east half, each facing the fountain.
        t = (k + 0.5) / STARTS_PER_RING_HALF
        for team, base, out in ((0, math.pi / 2, spy_starts), (1, -math.pi / 2, merc_starts)):
            ang = base + t * math.pi
            x = round(CX + radius * math.cos(ang))
            y = round(CY + radius * math.sin(ang))
            yaw = int(round(math.atan2(CY - y, CX - x) / (2 * math.pi) * 65536)) & 0xFFFF
            out.append(((float(x), float(y), START_Z), yaw))
for i, (at, yaw) in enumerate(spy_starts + merc_starts):
    team = 0 if i < len(spy_starts) else 1
    actor(f"PlayerStart{i}", "Engine.PlayerStart", at, yaw, TeamNumber=team, Tag="PlayerStart")

# Plaza dressing: the ring emblem, water in the basin, neon round its lip and up the column, the hanging sign.
art_mat("art_plaza_mark", "plaza_mark", unlit=False)
decal("PlazaFloor", (CX, CY), 1440, 1440, N, "art_plaza_mark", z=0.5)
mat("water", tile=(256.0, 256.0), uv_anim={"type": "friendly_panner", "rate": 1.0, "pan_u": 0.02, "pan_v": 0.03,
                                            "scale_u": 1.0, "scale_v": 1.0})
box("PlazaFountain", (CX - 240, CY - 240, 96), (CX + 240, CY + 240, 96.5), "water", faces="t")
for (x0, y0, x1, y1) in ((CX - 258, CY - 258, CX + 258, CY - 256), (CX - 258, CY + 256, CX + 258, CY + 258),
                         (CX - 258, CY - 256, CX - 256, CY + 256), (CX + 256, CY - 256, CX + 258, CY + 256)):
    box("PlazaNeon", (x0, y0, 76), (x1, y1, 86), "neon_green")
for sx, sy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
    x, y = CX + sx * 65, CY + sy * 65
    box("PlazaNeon", (x - 3, y - 3, 96), (x + 3, y + 3, 416), "neon_green")
# The hanging sign: four faces, 768 wide, over the lookout, on cables to the sky.
SIGN_Z = (656.0, 912.0)
art_mat("art_sign_hub", "sign", ["OPENSCCT HUB", "spies and mercs welcome"], 768, 256)
box("PlazaSign", (CX - 384, CY - 384, SIGN_Z[0] - 16), (CX + 384, CY + 384, SIGN_Z[0]), "darkpanel")
box("PlazaSign", (CX - 384, CY - 384, SIGN_Z[1]), (CX + 384, CY + 384, SIGN_Z[1] + 16), "darkpanel")
for n in (N, S, E, W):
    wall_sign("PlazaSign", (CX + n[0] * 385, CY + n[1] * 385, sum(SIGN_Z) / 2), 768, SIGN_Z[1] - SIGN_Z[0], n,
              "art_sign_hub")
for z in (SIGN_Z[0] - 22, SIGN_Z[1] + 16):
    box("PlazaSign", (CX - 387, CY - 387, z), (CX + 387, CY + 387, z + 6), "neon_green", faces="nsew")
for sx, sy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
    beam("PlazaSign", (CX + sx * 370, CY + sy * 370, SIGN_Z[1] + 16), (CX + sx * 370, CY + sy * 370, 1536), 6, "metal")
light("PlazaSignLight", (CX, CY, 600), 120, 64, hue=90, saturation=200)

# Corner pylons with neon caps and lights, benches facing in, side banners (spies west, mercs east).
for k, (px, py) in enumerate(((4800, 4800), (7488, 4800), (4800, 7488), (7488, 7488))):
    solid(f"PlazaPylon{k}", (px - 64, py - 64, 0), (px + 64, py + 64, 512), "darkpanel")
    neon = "neon_green" if px < CX else "neon_amber"
    box("PlazaPylons", (px - 66, py - 66, 512), (px + 66, py + 66, 544), neon)
    for sx, sy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        box("PlazaPylons", (px + sx * 64 - 3, py + sy * 64 - 3, 0), (px + sx * 64 + 3, py + sy * 64 + 3, 512), neon)
    light(f"PlazaPylonLight{k}", (px, py, 620), 150, 60, hue=85 if px < CX else 30, saturation=170)
for k, (bx, by, n) in enumerate(((5344, 7500, N), (6944, 7500, N), (5344, 4788, S), (6944, 4788, S),
                                 (4788, 5344, W), (4788, 6944, W), (7500, 5344, E), (7500, 6944, E))):
    # Seat 320 x 56 x 40, the back on the outer side.
    if n[1]:
        solid(f"PlazaBench{k}", (bx - 160, by - 28, 0), (bx + 160, by + 28, 40), "wood")
        yb = by + 28 if n[1] > 0 else by - 44
        solid(f"PlazaBenchBack{k}", (bx - 160, yb, 0), (bx + 160, yb + 16, 96), "wood")
    else:
        solid(f"PlazaBench{k}", (bx - 28, by - 160, 0), (bx + 28, by + 160, 40), "wood")
        xb = bx + 28 if n[0] > 0 else bx - 44
        solid(f"PlazaBenchBack{k}", (xb, by - 160, 0), (xb + 16, by + 160, 96), "wood")
art_mat("art_banner_spies", "sign", ["SPIES", "west side"], 256, 384)
art_mat("art_banner_mercs", "sign", ["MERCS", "east side"], 256, 384, fg=(255, 168, 60))
wall_sign("PlazaBanners", (PLAZA_LO[0] + 8, CY, 1000), 512, 768, E, "art_banner_spies", double=True)
wall_sign("PlazaBanners", (PLAZA_HI[0] - 8, CY, 1000), 512, 768, W, "art_banner_mercs", double=True)
for x, y in ((PLAZA_LO[0] + 8, CY), (PLAZA_HI[0] - 8, CY)):
    for dy in (-256, 256):
        beam("PlazaBanners", (x, y + dy, 1384), (x, y + dy, 1536), 4, "metal")
# Floor arrows out of the plaza to every area.
for k, (text, centre, facing) in enumerate((("MATCH PADS", (CX + 1290, CY), E), ("AIM RANGE", (CX - 1290, CY), W),
                                            ("GARDEN", (CX, CY + 1290), N), ("COURSE", (CX, CY - 1290), S),
                                            ("LOUNGE", (CX - 1000, CY - 1000), (-1, -1)))):
    art_mat(f"art_arrow_{k}", "floor_label", text, arrow=True, fg=(235, 240, 245), bg=(70, 72, 78), unlit=False)
    decal("PlazaArrows", centre, 640, 160, facing, f"art_arrow_{k}", z=0.7)
area("Plaza", "spawn", "Spawn plaza", PLAZA_LO, PLAZA_HI, "H1", centre=list(CENTRE), rings=list(START_RINGS),
     spyStarts=len(spy_starts), mercStarts=len(merc_starts), signCentre=[CX, CY, sum(SIGN_Z) / 2])

# ---- Traversal garden (north): the TestRange station kit at the same heights --------------------------------------
NORTH_FACE = 11776.0
GARDEN_LO = (2816.0, 8448.0)
GARDEN_HI = (9472.0, HALL_HI[1])
floor_zone(GARDEN_LO, GARDEN_HI, "turf")


def ledge_block(ident, x0, x1, top, note):
    solid(ident, (x0, NORTH_FACE, 0), (x1, HALL_HI[1], top))
    ge(ident + "LG", "LG", (x0, NORTH_FACE, top), (x1, NORTH_FACE, top), note=note)


ledge_block("Ledge128", 3072, 3328, 128, "under the hang height: hop-up")
ledge_block("Ledge208", 3584, 3840, 208, "lowest hang and climb")
ledge_block("Ledge264", 4096, 4352, 264, "container lip height")
ledge_block("Ledge320", 4608, 4864, 320, "highest standing-jump grab")
solid("WallJump408", (5120, NORTH_FACE, 0), (5504, HALL_HI[1], 408))
ge("WallJump408LG", "LG", (5120, NORTH_FACE, 408), (5504, NORTH_FACE, 408), note="wall jump: kick up to this lip")
solid("Pipe512", (5760, NORTH_FACE, 0), (6016, HALL_HI[1], 512))
PIPE_X, PIPE_Y = 5888.0, NORTH_FACE - 16
# Drawn, not collided with: a climber hangs 35 from the GE line, inside any solid pipe thicker than a hair.
box("GardenKit", (PIPE_X - 4, PIPE_Y - 4, 0.0), (PIPE_X + 4, PIPE_Y + 4, 512.0), "metal")
ge("Pipe512PIPE", "PIPE", (PIPE_X, PIPE_Y, 0), (PIPE_X, PIPE_Y, 512), (PIPE_X, PIPE_Y - 10, 0),
   note="bottom, top flush with the block top, side 10 toward the climber")
solid("Ladder320", (6272, NORTH_FACE, 0), (6528, HALL_HI[1], 320))
LADDER_X, LADDER_Y = 6400.0, NORTH_FACE - 4
ge("Ladder320LADDER", "LADDER", (LADDER_X, LADDER_Y, 0), (LADDER_X, LADDER_Y, 320), (LADDER_X, LADDER_Y - 10, 0),
   note="bottom, top flush with the block top, side 10 toward the climber")
solid("SplitWallW", (6784, 11264, 0), (6816, HALL_HI[1], 512))
solid("SplitWallE", (7008, 11264, 0), (7040, HALL_HI[1], 512))

# Free-standing pieces in the middle of the garden.
solid("Vault96", (3072, 10240, 0), (3456, 10272, 96))
ge("Vault96LGS", "LG", (3072, 10240, 96), (3456, 10240, 96), note="barrier lip, south face")
ge("Vault96LGN", "LG", (3072, 10272, 96), (3456, 10272, 96), note="barrier lip, north face")
# Fence panels are 2 thick, as on TestRange and the real maps: a thicker one blocks CheckFenceTopArms' climb over.
solid("Fence376", (3840, 10255, 0), (4224, 10257, 376), "fence")
ge("Fence376FENCE", "FENCE", (3840, 10256, 376), (4224, 10256, 376), note="climbable both faces, nothing above")
solid("HohPillarW", (4608, 10224, 0), (4672, 10288, 416))
solid("HohPillarE", (5312, 10224, 0), (5376, 10288, 416))
solid("HohBar", (4672, 10252, 276), (5312, 10260, 284), "metal")
ge("Hoh277HOH", "HOH", (4672, 10256, 277), (5312, 10256, 277), note="bar underside 1 under the line")
solid("Curb17", (5760, 10112, 0), (6016, 10368, 17))
solid("Step34", (6144, 10112, 0), (6400, 10368, 34))
for k in range(10):
    solid(f"Stairs{k + 1}", (6528 + 32 * k, 10112, 0), (6560 + 32 * k, 10368, 16 * (k + 1)))
solid("StairsLanding", (6848, 10112, 0), (7104, 10368, 160))

# Ramp up to a deck, a tower on the deck, and a zip line from the deck south toward the plaza.
solid("Ramp384", (7424, 9984, 0), (8192, 10368, 384), "deck", up="+x")
solid("Deck384", (8192, 9344, 0), (8960, 10368, 384), "deck")
solid("Tower600", (8704, 10112, 0), (8960, 10368, 600))
TOWER_LADDER_X = 8704.0 - 4
ge("Tower600LADDER", "LADDER", (TOWER_LADDER_X, 10240, 384), (TOWER_LADDER_X, 10240, 600), (TOWER_LADDER_X - 10, 10240, 384),
   note="from the deck to the tower top, flush")
for side, (a, b) in {"S": ((8192, 9344), (8960, 9344)), "W": ((8192, 10368), (8192, 9984))}.items():
    ge(f"Deck384LG{side}", "LG", (a[0], a[1], 384), (b[0], b[1], 384), note="deck lip")
# TestRange's zip line (1760 run, 320 drop), with its landing deck 208 under the low end.
ZIP_HIGH: Vec3 = (8576.0, 9456.0, 688.0)
ZIP_LOW: Vec3 = (8576.0, 7696.0, 368.0)
ge("ZipLineZL", "ZL", ZIP_LOW, ZIP_HIGH, note="high end 304 over the deck, low end over the landing deck")
solid("ZipLanding", (8448, 7424, 0), (8704, 7936, 160), "deck")
ge("ZipLandingLGS", "LG", (8448, 7424, 160), (8704, 7424, 160), note="landing deck lip, climb back up")


def ladder_visual(g, x, y, z0, z1, along="x"):
    """Two rails and rungs every 32 uu, in front of a face (render only; the LADDER GE is the climb)."""
    for s in (-20, 20):
        a = (x + s, y, z0) if along == "x" else (x, y + s, z0)
        b = (a[0], a[1], z1)
        beam(g, a, b, 5, "metal")
    z = z0 + 16
    while z < z1:
        a = (x - 20, y, z) if along == "x" else (x, y - 20, z)
        b = (x + 20, y, z) if along == "x" else (x, y + 20, z)
        beam(g, a, b, 4, "metal")
        z += 32


ladder_visual("GardenKit", LADDER_X, LADDER_Y - 3, 0, 320)
ladder_visual("PlazaFountain", CX, CY - 64 - 7, 96, 416)   # the fountain column's ladder
ladder_visual("GardenKit", TOWER_LADDER_X - 3, 10240, 384, 600, along="y")
beam("GardenKit", ZIP_HIGH, ZIP_LOW, 3, "metal")
beam("GardenKit", (ZIP_HIGH[0], ZIP_HIGH[1], 384), (ZIP_HIGH[0], ZIP_HIGH[1], ZIP_HIGH[2] + 24), 8, "metal")
beam("GardenKit", (ZIP_LOW[0], ZIP_LOW[1] - 200, 160), (ZIP_LOW[0], ZIP_LOW[1] - 200, ZIP_LOW[2] + 24), 8, "metal")
# Station labels: on the block faces, and painted on the turf in front of the free-standing pieces.
for k, (text, x0, x1, top) in enumerate((("LEDGE 128", 3072, 3328, 128), ("LEDGE 208", 3584, 3840, 208),
                                         ("LEDGE 264", 4096, 4352, 264), ("LEDGE 320", 4608, 4864, 320),
                                         ("WALL JUMP", 5120, 5504, 408), ("PIPE", 5760, 6016, 512),
                                         ("LADDER", 6272, 6528, 320))):
    art_mat(f"art_station_{k}", "sign", [text], 256, 64, fg=(235, 240, 245), glow=False)
    wall_sign("GardenLabels", ((x0 + x1) / 2, NORTH_FACE - 0.5, top - 40), 224, 56, S, f"art_station_{k}")
for k, (text, x, y) in enumerate((("VAULT", 3264, 10040), ("FENCE", 4032, 10040), ("HAND OVER HAND", 4992, 10040),
                                  ("CURB", 5888, 9960), ("STEP", 6272, 9960), ("STAIRS", 6816, 9960),
                                  ("SPLIT JUMP", 6912, 11080), ("RAMP", 7808, 9800), ("ZIP LINE", 8576, 9200))):
    art_mat(f"art_garden_{k}", "floor_label", text, fg=(235, 240, 245), bg=(44, 98, 50), unlit=False)
    decal("GardenLabels", (x, y), 384, 96, N, f"art_garden_{k}", z=0.7)
# Trees: a trunk you bump into, a canopy overhead (render only, above any jump).
for k, (tx, ty) in enumerate(((3200, 9000), (4800, 9100), (6100, 8900), (7000, 9050), (9220, 11200), (3000, 11200))):
    solid(f"GardenTree{k}", (tx - 24, ty - 24, 0), (tx + 24, ty + 24, 320), "bark")
    box("GardenTrees", (tx - 160, ty - 160, 320), (tx + 160, ty + 160, 470), "foliage")
    box("GardenTrees", (tx - 104, ty - 104, 470), (tx + 104, ty + 104, 560), "foliage")
art_mat("art_sign_garden", "sign", ["TRAVERSAL GARDEN", "ledges  pipes  ladders  zip line"], 768, 144)
wall_sign("GardenSign", (6144, HALL_HI[1] - 1, 860), 2048, 384, S, "art_sign_garden")
for k, (lx, ly) in enumerate(((3800, 9400), (5600, 9400), (7400, 9400), (4600, 11000), (8200, 11000))):
    light(f"GardenLight{k}", (lx, ly, 760), 170, 68, hue=95, saturation=200)
area("Garden", "traversal", "Traversal garden", GARDEN_LO, GARDEN_HI, "H1",
     ges=[g.id for g in GES if g.id.startswith(("Ledge", "WallJump", "Pipe", "Ladder", "Vault", "Fence", "Hoh", "Tower", "Deck", "Zip"))],
     zipHigh=list(ZIP_HIGH), zipLow=list(ZIP_LOW))

# ---- Match pads (east): railed courts, sign gantries, bleachers, a theme kit per pad -------------------------------
PADS_LO, PADS_HI = (8960.0, 4352.0), (12288.0, 7936.0)
PAD_SIZE = 1024.0
RAIL_OFFSET = 32.0           # the rails run 32 outside the court; corner posts 64 square sit on the rail corners
RAIL_GAP = 256.0             # an opening in the middle of every side
GANTRY = 72.0                # the sign board's front face, this far north of the court

# Theme kits: id -> look. `maps` are the map code prefixes the kit evokes (HubPadThemes.ThemeForMap).
THEMES: dict[str, dict] = {
    "hub": dict(title="OPENSCCT", maps=[], line=(120, 255, 140), floor=(34, 40, 52), accent=(235, 240, 245),
                neon="neon_green", rail="hub_slate", post="hub_slate", hue=85, sat=170, props="beacon"),
    "polar": dict(title="POLAR BASE", maps=["Pola"], line=(40, 110, 190), floor=(205, 228, 246), accent=(255, 255, 255),
                  neon="neon_ice", rail="polar_frost", post="polar_ice", hue=150, sat=110, props="antenna"),
    "steel": dict(title="STEEL SQUAT", maps=["Stee", "Fact", "Ware"], line=(240, 190, 40), floor=(96, 92, 86),
                  accent=(30, 30, 30), neon="neon_sodium", rail="steel_corrugated", post="steel_concrete", hue=22,
                  sat=70, props="lamp"),
    "aquarius": dict(title="AQUARIUS", maps=["Aqua", "Offs"], line=(40, 240, 200), floor=(20, 60, 70),
                     accent=(120, 200, 230), neon="neon_teal", rail="aqua_metal", post="aqua_grating", hue=120,
                     sat=110, props="pipes"),
    "lab": dict(title="KRAUSER LAB", maps=["Krau", "Clar", "Deft", "Zio"], line=(40, 40, 40), floor=(225, 230, 230),
                accent=(150, 220, 40), neon="neon_lab", rail="lab_panel", post="lab_hazard", hue=75, sat=190,
                props="beacon_lab"),
    "museum": dict(title="MUSEUM", maps=["Muse", "Bank", "Pana", "Cine", "Club", "Sanc", "Sche"], line=(200, 160, 70),
                   floor=(110, 36, 36), accent=(240, 220, 170), neon="neon_gold", rail="museum_wood",
                   post="museum_marble", hue=30, sat=200, props="plinth"),
}
DEFAULT_THEME = "hub"
PAD_DEFAULT_THEMES = {"Pad1": "polar", "Pad2": "steel", "Pad3": "aquarius", "Pad4": "lab"}
for _t in THEMES:
    for _k in (THEMES[_t]["rail"], THEMES[_t]["post"]):
        mat(_k, tile=(128.0, 128.0))
    art_mat(f"art_court_{_t}", "court", _t, THEMES[_t]["line"], THEMES[_t]["floor"], THEMES[_t]["accent"], unlit=False)
    art_mat(f"art_padtitle_{_t}", "floor_label", THEMES[_t]["title"], fg=THEMES[_t]["line"], bg=THEMES[_t]["floor"],
            unlit=False)


def kit_props(g, style, x, y, top, theme):
    """The theme's dressing on one corner post (top at `top`), render only."""
    t = THEMES[theme]
    if style == "antenna":
        beam(g, (x, y, top), (x, y, top + 300), 6, "polar_frost")
        for dz in (120, 200, 270):
            beam(g, (x - 40, y, top + dz), (x + 40, y, top + dz), 4, "polar_frost")
        box(g, (x - 4, y - 4, top + 300), (x + 4, y + 4, top + 308), "neon_red")
        box(g, (x - 34, y - 34, top), (x + 34, y + 34, top + 10), "polar_ice")
    elif style == "lamp":
        beam(g, (x, y, top), (x, y, top + 260), 10, "metal")
        beam(g, (x, y, top + 260), (x + 60, y, top + 260), 8, "metal")
        box(g, (x + 40, y - 14, top + 236), (x + 80, y + 14, top + 254), "neon_sodium")
    elif style == "pipes":
        for dx in (-16, 16):
            beam(g, (x + dx, y, top), (x + dx, y, top + 200), 14, "aqua_metal")
        box(g, (x - 30, y - 10, top + 150), (x + 30, y + 10, top + 170), "neon_teal")
    elif style == "beacon_lab":
        box(g, (x - 20, y - 20, top), (x + 20, y + 20, top + 40), "lab_panel")
        box(g, (x - 12, y - 12, top + 40), (x + 12, y + 12, top + 90), "neon_lab")
    elif style == "plinth":
        box(g, (x - 26, y - 26, top), (x + 26, y + 26, top + 70), "museum_marble")
        box(g, (x - 14, y - 14, top + 70), (x + 14, y + 14, top + 110), "neon_gold")
    else:
        box(g, (x - 10, y - 10, top), (x + 10, y + 10, top + 60), t["neon"])


pads = []
for i in range(4):
    pad_id = f"Pad{i + 1}"
    x0 = 9216.0 + (i % 2) * 1280.0
    y0 = 4608.0 + (i // 2) * 1664.0
    x1, y1 = x0 + PAD_SIZE, y0 + PAD_SIZE
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    floor_zone((x0 - 96, y0 - 96), (x1 + 96, y1 + 96), "court")
    o = RAIL_OFFSET
    posts = []
    for k, (px, py) in enumerate(((x0 - o, y0 - o), (x1 + o, y0 - o), (x0 - o, y1 + o), (x1 + o, y1 + o))):
        solid(f"{pad_id}Post{k}", (px - 32, py - 32, 0), (px + 32, py + 32, 128), "crate")
        posts.append(SOLIDS[-1])
    rails = []
    for side, (a, b) in {"S": ((x0 - o, y0 - o), (x1 + o, y0 - o)), "N": ((x0 - o, y1 + o), (x1 + o, y1 + o)),
                         "W": ((x0 - o, y0 - o), (x0 - o, y1 + o)), "E": ((x1 + o, y0 - o), (x1 + o, y1 + o))}.items():
        horizontal = a[1] == b[1]
        lo_end = (a[0] + 32, a[1]) if horizontal else (a[0], a[1] + 32)
        hi_end = (b[0] - 32, b[1]) if horizontal else (b[0], b[1] - 32)
        mid = (cx, a[1]) if horizontal else (a[0], cy)
        g0 = (mid[0] - RAIL_GAP / 2, mid[1]) if horizontal else (mid[0], mid[1] - RAIL_GAP / 2)
        g1 = (mid[0] + RAIL_GAP / 2, mid[1]) if horizontal else (mid[0], mid[1] + RAIL_GAP / 2)
        rails.append(railing(f"{pad_id}Rail{side}0", lo_end, g0))
        rails.append(railing(f"{pad_id}Rail{side}1", g1, hi_end))
    # The sign gantry north of the court: two posts, a board facing south (and its back facing north).
    gy = y1 + GANTRY
    for k, gx in enumerate((x0 + 64, x1 - 96)):
        solid(f"{pad_id}Gantry{k}", (gx, gy - 8, 0), (gx + 32, gy + 24, 720), "metal")
    art_mat(f"art_padboard_{pad_id}", "board", f"PAD {i + 1}", "next match")
    box(f"{pad_id}Gantry", (x0 + 96, gy, 440), (x1 - 96, gy + 16, 712), "darkpanel", faces="tbew")
    wall_sign(f"{pad_id}Gantry", (cx, gy - 0.5, 576), x1 - x0 - 192, 272, S, f"art_padboard_{pad_id}")
    wall_sign(f"{pad_id}Gantry", (cx, gy + 16.5, 576), x1 - x0 - 192, 272, N, f"art_padboard_{pad_id}")
    art_mat(f"art_padnum_{pad_id}", "floor_label", f"PAD {i + 1}", fg=(235, 240, 245), bg=(34, 40, 52), unlit=False)
    decal(f"{pad_id}Floor", (cx, y0 - 80), 256, 64, N, f"art_padnum_{pad_id}", z=0.7)
    light(f"{pad_id}Flood", (cx, cy, 900), 190, 52, hue=30, saturation=225)
    # The theme kits: one group per theme, the pad's default shown, the others hidden until the client swaps.
    for theme, t in THEMES.items():
        g = f"HubTheme_{pad_id}_{theme}"
        group(g, kit=(pad_id, theme))
        decal(g, (cx, cy), PAD_SIZE, PAD_SIZE, N, f"art_court_{theme}", z=0.6)
        decal(g, (cx, y1 - 200), 640, 160, N, f"art_padtitle_{theme}", z=0.9)
        for (a0, b0, a1, b1) in ((x0 - 16, y0 - 16, x1 + 16, y0), (x0 - 16, y1, x1 + 16, y1 + 16),
                                 (x0 - 16, y0, x0, y1), (x1, y0, x1 + 16, y1)):
            box(g, (a0, b0, 0.0), (a1, b1, 1.5), t["neon"], faces="t")
        for r in rails:
            skin(g, r, t["rail"])
            box(g, (r.lo[0] - 2, r.lo[1] - 2, 97), (r.hi[0] + 2, r.hi[1] + 2, 101), t["neon"], faces="tnsew")
        for p in posts:
            skin(g, p, t["post"])
            kit_props(g, t["props"], (p.lo[0] + p.hi[0]) / 2, (p.lo[1] + p.hi[1]) / 2, 129, theme)
        light(f"{g}_Light", (cx, cy, 640), 150, 48, hue=t["hue"], saturation=t["sat"], kit=(pad_id, theme))
    pads.append({"id": pad_id, "lo": [x0, y0], "hi": [x1, y1], "centre": [cx, cy],
                 "signAnchor": [cx, gy - 2, 576.0], "signYaw": YAW_S, "signSize": [x1 - x0 - 192, 272.0],
                 "theme": PAD_DEFAULT_THEMES[pad_id], "railGap": RAIL_GAP})

# Bleachers: north of Pad3 and Pad4 (rising north), east of Pad2 and Pad4 (rising east), a rail behind the north ones.
for k in range(4):
    solid(f"BleacherN{k}", (9152, 7424 + 128 * k, 0), (11584, 7552 + 128 * k, 32 * (k + 1)), "wood")
solid("BleacherNRail", (9152, 7936, 0), (11584, 7952, 176), "metal")
for k in range(5):
    solid(f"BleacherE{k}", (11648 + 128 * k, 4352, 0), (11776 + 128 * k, 7936, 32 * (k + 1)), "wood")
for k in range(4):
    box("PadsDressing", (9152, 7424 + 128 * k - 2, 32 * (k + 1) - 4), (11584, 7424 + 128 * k, 32 * (k + 1)), "neon_amber",
        faces="s")
for k in range(5):
    box("PadsDressing", (11648 + 128 * k - 2, 4352, 32 * (k + 1) - 4), (11648 + 128 * k, 7936, 32 * (k + 1)), "neon_amber",
        faces="w")
art_mat("art_sign_pads", "sign", ["MATCH PADS", "stand on a pad to queue"], 768, 192, fg=(255, 168, 60))
wall_sign("PadsSign", (8960, 6144, 980), 1280, 320, W, "art_sign_pads", double=True)
for dy in (-560, 560):
    beam("PadsSign", (8960, 6144 + dy, 1140), (8960, 6144 + dy, 1536), 5, "metal")
for k, (lx, ly) in enumerate(((10368, 7700), (12000, 6144), (9300, 6000))):
    light(f"PadsLight{k}", (lx, ly, 700), 110, 56, hue=30, saturation=190)
area("MatchPads", "matchPads", "Match pads", PADS_LO, PADS_HI, "H2", pads=pads,
     themes=[{"id": k, "title": v["title"], "maps": v["maps"]} for k, v in THEMES.items()], defaultTheme=DEFAULT_THEME)

# ---- Merc aim range (west): booth, stalls, targets, distance marks, leaderboard ------------------------------------
RANGE_LO, RANGE_HI = (512.0, 4352.0), (3328.0, 7936.0)
LANE = (4864.0, 7424.0)
FIRING_LINE = 3072.0
floor_zone((FIRING_LINE, LANE[0]), (RANGE_HI[0], LANE[1]), "booth")
floor_zone((576.0, LANE[0]), (FIRING_LINE, LANE[1]), "range")
solid("RangeBerm", (512, LANE[0], 0), (576, LANE[1], 384), "darkpanel")
solid("RangeCounter", (FIRING_LINE - 64, LANE[0], 0), (FIRING_LINE, LANE[1], 80), "booth")
ledge("RangeCounterLGE", (FIRING_LINE, LANE[0], 80), (FIRING_LINE, LANE[1], 80), "counter top, shooter side")
STALLS = 5
STALL_W = (LANE[1] - LANE[0]) / STALLS
for k in range(1, STALLS):
    y = LANE[0] + STALL_W * k
    solid(f"RangeStall{k}", (FIRING_LINE, y - 8, 0), (FIRING_LINE + 128, y + 8, 160), "darkpanel")
for k, y in enumerate((LANE[0] - 64, LANE[1])):
    solid(f"RangePost{k}", (RANGE_HI[0] - 64, y, 0), (RANGE_HI[0], y + 64, 480), "darkpanel")
box("RangeBooth", (2944, LANE[0] - 64, 480), (RANGE_HI[0], LANE[1] + 64, 496), "darkpanel")
box("RangeBooth", (RANGE_HI[0], LANE[0] - 64, 480), (RANGE_HI[0] + 2, LANE[1] + 64, 496), "neon_amber", faces="e")
box("RangeBooth", (2942, LANE[0] - 64, 480), (2944, LANE[1] + 64, 496), "neon_amber", faces="w")
box("RangeBooth", (FIRING_LINE - 66, LANE[0], 80), (FIRING_LINE - 64, LANE[1], 84), "neon_amber", faces="w")
art_mat("art_target", "target_board", unlit=False)
for k in range(STALLS):
    y = LANE[0] + STALL_W * (k + 0.5)
    wall_sign("RangeTargets", (576.5, y, 240), 192, 384, E, "art_target")
    art_mat(f"art_stall_{k}", "sign", [f"{k + 1}"], 64, 64, fg=(255, 168, 60))
    wall_sign("RangeTargets", (FIRING_LINE + 0.5, y, 40), 48, 48, E, f"art_stall_{k}")
for k in range(1, 5):
    x = FIRING_LINE - 512 * k
    box("RangeMarks", (x - 8, LANE[0], 0.0), (x + 8, LANE[1], 0.5), "neon_white", faces="t")
    art_mat(f"art_range_{k}", "floor_label", f"{5 * k} M", fg=(235, 240, 245), bg=(70, 70, 66), unlit=False)
    decal("RangeMarks", (x + 64, (LANE[0] + LANE[1]) / 2), 384, 96, W, f"art_range_{k}", z=0.6)
art_mat("art_sign_range", "sign", ["AIM RANGE", "mercs: five stalls, live targets"], 768, 144, fg=(255, 168, 60))
wall_sign("RangeSign", (RANGE_HI[0] + 1, (LANE[0] + LANE[1]) / 2, 600), 1280, 240, E, "art_sign_range")
# The leaderboard: a board on two posts at the range's north end, facing east toward the plaza.
RANGE_BOARD = (3296.0, 7712.0)
for k, y in enumerate((7536, 7856)):
    solid(f"RangeBoardPost{k}", (RANGE_BOARD[0] - 48, y, 0), (RANGE_BOARD[0] - 16, y + 32, 680), "metal")
art_mat("art_board_range", "board", "AIM RANGE", "top scores")
box("RangeBoard", (RANGE_BOARD[0] - 32, 7520, 256), (RANGE_BOARD[0] - 16, 7904, 672), "darkpanel", faces="tbnsw")
wall_sign("RangeBoard", (RANGE_BOARD[0] - 15.5, RANGE_BOARD[1], 464), 384, 416, E, "art_board_range")
for k, ly in enumerate((5300, 6144, 6988)):
    light(f"RangeLight{k}", (1800, ly, 600), 200, 72, hue=28, saturation=160)
light("RangeBoothLight", (3150, 6144, 420), 160, 56, hue=30, saturation=210)
area("AimRange", "aimRange", "Merc aim range", RANGE_LO, RANGE_HI, "H3", firingLine=FIRING_LINE, backstop=576.0,
     lane=list(LANE), stalls=[[LANE[0] + STALL_W * k, LANE[0] + STALL_W * (k + 1)] for k in range(STALLS)],
     leaderboardAnchor=[RANGE_BOARD[0] - 14.0, RANGE_BOARD[1], 464.0], leaderboardYaw=YAW_E,
     leaderboardSize=[384.0, 416.0])

# ---- Spy movement course (south): a numbered route along y 1920 -----------------------------------------------------
COURSE_LO, COURSE_HI = (3072.0, 512.0), (9472.0, 3328.0)
ROUTE_Y = 1920.0
ROUTE = (1536.0, 2304.0)
floor_zone((COURSE_LO[0], ROUTE[0]), (COURSE_HI[0], ROUTE[1]), "track")
for y in ROUTE:
    box("CourseLane", (COURSE_LO[0], y - 6, 0.0), (COURSE_HI[0], y + 6, 0.5), "neon_cyan", faces="t")
# 1 vault wall.
solid("CourseVault", (3840, ROUTE[0], 0), (3872, ROUTE[1], 96))
ledge("CourseVaultLGW", (3840, ROUTE[1], 96), (3840, ROUTE[0], 96), "vault wall, west face")
ledge("CourseVaultLGE", (3872, ROUTE[0], 96), (3872, ROUTE[1], 96), "vault wall, east face")
# 2 climb block (208, the lowest hang and climb).
solid("CourseClimb", (4352, ROUTE[0], 0), (4864, ROUTE[1], 208))
ledge("CourseClimbLGW", (4352, ROUTE[1], 208), (4352, ROUTE[0], 208), "climb block, west face")
ledge("CourseClimbLGE", (4864, ROUTE[0], 208), (4864, ROUTE[1], 208), "climb block, east face")
# 3 fence (376, climb over).
solid("CourseFence", (5251, ROUTE[0], 0), (5253, ROUTE[1], 376), "fence")
ge("CourseFenceFENCE", "FENCE", (5252, ROUTE[0], 376), (5252, ROUTE[1], 376), note="climbable both faces")
# 4 hand over hand bar at the kit height (277), between two pillars on the route.
solid("CourseHohPillarW", (5632, ROUTE_Y - 32, 0), (5696, ROUTE_Y + 32, 416))
solid("CourseHohPillarE", (6336, ROUTE_Y - 32, 0), (6400, ROUTE_Y + 32, 416))
solid("CourseHohBar", (5696, ROUTE_Y - 4, 276), (6336, ROUTE_Y + 4, 284), "metal")
ge("CourseHohHOH", "HOH", (5696, ROUTE_Y, 277), (6336, ROUTE_Y, 277), note="bar underside 1 under the line")
# 5 tower (384): a pipe on its west face, a ladder on its south face.
COURSE_TOWER = (6912.0, 1792.0, 7424.0, 2048.0, 384.0)
tx0, ty0, tx1, ty1, ttop = COURSE_TOWER
solid("CourseTower", (tx0, ty0, 0), (tx1, ty1, ttop))
CPIPE_X = tx0 - 16
box("CourseKit", (CPIPE_X - 4, ROUTE_Y - 4, 0.0), (CPIPE_X + 4, ROUTE_Y + 4, ttop), "metal")
ge("CourseTowerPIPE", "PIPE", (CPIPE_X, ROUTE_Y, 0), (CPIPE_X, ROUTE_Y, ttop), (CPIPE_X - 10, ROUTE_Y, 0),
   note="bottom, top flush with the tower top, side 10 toward the climber")
CLADDER = (7168.0, ty0 - 4)
ge("CourseTowerLADDER", "LADDER", (CLADDER[0], CLADDER[1], 0), (CLADDER[0], CLADDER[1], ttop),
   (CLADDER[0], CLADDER[1] - 10, 0), note="bottom, top flush with the tower top, side 10 toward the climber")
ladder_visual("CourseKit", CLADDER[0], CLADDER[1] - 3, 0, ttop)
ledge("CourseTowerLGE", (tx1, ty0, ttop), (tx1, ty1, ttop), "tower top, east lip")
# 6 zip line down to the finish deck: TestRange's run and drop (1760, 320), high end 304 over the tower top.
CZIP_HIGH: Vec3 = (tx1 - 112, ROUTE_Y, ttop + 304)
CZIP_LOW: Vec3 = (tx1 - 112 + 1760, ROUTE_Y, ttop + 304 - 320)
ge("CourseZipZL", "ZL", CZIP_LOW, CZIP_HIGH, note="high end 304 over the tower top, low end 208 over the finish deck")
FINISH_DECK = (CZIP_LOW[0] - 240, 1792.0, CZIP_LOW[0] - 240 + 512, 2048.0, 160.0)
fx0, fy0, fx1, fy1, ftop = FINISH_DECK
solid("CourseFinish", (fx0, fy0, 0), (fx1, fy1, ftop), "deck")
ledge("CourseFinishLGW", (fx0, fy1, ftop), (fx0, fy0, ftop), "finish deck, west lip")
beam("CourseKit", CZIP_HIGH, CZIP_LOW, 3, "metal")
beam("CourseKit", (CZIP_HIGH[0], ROUTE_Y, ttop), (CZIP_HIGH[0], ROUTE_Y, CZIP_HIGH[2] + 24), 8, "metal")
beam("CourseKit", (CZIP_LOW[0] + 200, ROUTE_Y, ftop), (CZIP_LOW[0] + 200, ROUTE_Y, CZIP_LOW[2] + 24), 8, "metal")
COURSE_START = (3328.0, ROUTE_Y)
COURSE_END = (fx0 + 128, ROUTE_Y)
art_mat("art_course_start", "checker_strip", 256, 64, "START", unlit=False)
art_mat("art_course_finish", "checker_strip", 256, 128, "FINISH", unlit=False)
decal("CourseMarks", COURSE_START, ROUTE[1] - ROUTE[0], 192, E, "art_course_start", z=0.6)
decal("CourseMarks", (fx0 + 160, ROUTE_Y), fy1 - fy0, 128, E, "art_course_finish", z=ftop + 0.6)
# Gates: two posts outside the lane, a crossbar with the checkpoint's number, a floor arrow naming the move.
CHECKPOINTS = [("vault", 3712.0), ("climb", 4224.0), ("fence", 5120.0), ("hoh", 5504.0), ("tower", 6656.0),
               ("zip", CZIP_HIGH[0]), ("finish", fx0 + 160)]
MOVES = {"vault": "VAULT", "climb": "CLIMB", "fence": "FENCE", "hoh": "HANG", "tower": "PIPE / LADDER", "zip": "ZIP LINE",
         "finish": "FINISH"}
checkpoints = []
for k, (kind, x) in enumerate(CHECKPOINTS):
    z = ftop if kind == "finish" else (ttop if kind == "zip" else 0.0)
    if kind not in ("zip", "finish"):
        for j, y in enumerate((ROUTE[0] - 48, ROUTE[1] + 16)):
            solid(f"CourseGate{k}Post{j}", (x - 16, y, 0), (x + 16, y + 32, 448), "darkpanel")
        box("CourseGates", (x - 16, ROUTE[0] - 48, 448), (x + 16, ROUTE[1] + 48, 480), "darkpanel")
        box("CourseGates", (x - 18, ROUTE[0] - 48, 440), (x - 16, ROUTE[1] + 48, 448), "neon_cyan", faces="w")
        art_mat(f"art_gate_{k}", "sign", [f"{k + 1}", MOVES[kind]], 192, 128, fg=(90, 220, 255))
        wall_sign("CourseGates", (x - 17, ROUTE_Y, 400), 192, 128, W, f"art_gate_{k}")
    art_mat(f"art_move_{k}", "floor_label", MOVES[kind], arrow=True, fg=(90, 220, 255), bg=(24, 60, 72), unlit=False)
    if kind == "zip":
        decal("CourseArrows", ((tx0 + tx1) / 2, ROUTE_Y), 256, 64, E, f"art_move_{k}", z=ttop + 0.7)
    elif kind != "finish":
        decal("CourseArrows", (x - 200, ROUTE_Y), 512, 128, E, f"art_move_{k}", z=0.7)
    checkpoints.append({"id": f"CP{k + 1}", "kind": kind, "at": [x, ROUTE_Y, z]})
art_mat("art_sign_course", "sign", ["MOVEMENT COURSE", "spies: start at the gate, beat the clock"], 768, 144,
        fg=(90, 220, 255))
wall_sign("CourseSign", (6144, COURSE_HI[1] + 2, 900), 2048, 384, N, "art_sign_course", double=True)
for dx in (-900, 900):
    beam("CourseSign", (6144 + dx, COURSE_HI[1] + 1, 1092), (6144 + dx, COURSE_HI[1] + 1, 1536), 5, "metal")
# The leaderboard: on two posts near the start, facing north toward the plaza.
COURSE_BOARD = (3712.0, 3200.0)
for k, x in enumerate((COURSE_BOARD[0] - 256, COURSE_BOARD[0] + 224)):
    solid(f"CourseBoardPost{k}", (x, COURSE_BOARD[1] - 32, 0), (x + 32, COURSE_BOARD[1], 680), "metal")
art_mat("art_board_course", "board", "MOVEMENT COURSE", "best times")
box("CourseBoard", (COURSE_BOARD[0] - 224, COURSE_BOARD[1] - 32, 256), (COURSE_BOARD[0] + 224, COURSE_BOARD[1] - 16, 672),
    "darkpanel", faces="tbsew")
wall_sign("CourseBoard", (COURSE_BOARD[0], COURSE_BOARD[1] - 15.5, 464), 448, 416, N, "art_board_course")
for k, lx in enumerate((3600, 4800, 6000, 7200, 8400, 9200)):
    light(f"CourseLight{k}", (lx, ROUTE_Y, 600), 190, 68, hue=128, saturation=160)
area("Course", "movementCourse", "Spy movement course", COURSE_LO, COURSE_HI, "H3", start=list(COURSE_START),
     finish=list(COURSE_END), route=list(ROUTE), checkpoints=checkpoints,
     leaderboardAnchor=[COURSE_BOARD[0], COURSE_BOARD[1] - 14.0, 464.0], leaderboardYaw=YAW_N,
     leaderboardSize=[448.0, 416.0])

# ---- Lounge (south-west): sofas, a big screen with a ticker, a bar -------------------------------------------------
LOUNGE_LO, LOUNGE_HI = (256.0, 256.0), (2816.0, 3840.0)
floor_zone((512.0, 512.0), (2560.0, 3584.0), "carpet")
for k, (sx, sy) in enumerate(((1536, 1536), (1536, 2560))):
    # Seat, back to the east (facing the screen on the west wall), arms, a coffee table in front.
    solid(f"LoungeSofa{k}", (sx - 48, sy - 256, 0), (sx + 48, sy + 256, 40), "sofa" if k == 0 else "sofa2")
    solid(f"LoungeSofaBack{k}", (sx + 48, sy - 256, 0), (sx + 80, sy + 256, 104), "sofa" if k == 0 else "sofa2")
    for j, (y0_, y1_) in enumerate(((sy - 288, sy - 256), (sy + 256, sy + 288))):
        solid(f"LoungeSofaArm{k}{j}", (sx - 48, y0_, 0), (sx + 80, y1_, 64), "sofa" if k == 0 else "sofa2")
    solid(f"LoungeTable{k}", (sx - 256, sy - 128, 0), (sx - 160, sy + 128, 32), "wood")
for k, (sx, sy) in enumerate(((2240, 1600), (2240, 2496))):
    # Side sofas facing west too, a little further back.
    solid(f"LoungeSide{k}", (sx - 48, sy - 160, 0), (sx + 48, sy + 160, 40), "sofa2" if k == 0 else "sofa")
    solid(f"LoungeSideBack{k}", (sx + 48, sy - 160, 0), (sx + 80, sy + 160, 104), "sofa2" if k == 0 else "sofa")
solid("LoungeBar", (768, 640, 0), (2048, 704, 112), "wood")
box("LoungeDressing", (768, 638, 96), (2048, 640, 104), "neon_magenta", faces="s")
box("LoungeDressing", (764, 640, 112), (2052, 708, 116), "darkpanel")
for k in range(5):
    sx = 896 + 256 * k
    solid(f"LoungeStool{k}", (sx - 16, 752, 0), (sx + 16, 784, 64), "metal")
for k, (px, py) in enumerate(((640, 3456), (2432, 3456), (2432, 896))):
    solid(f"LoungePlanter{k}", (px - 64, py - 64, 0), (px + 64, py + 64, 64), "concrete")
    solid(f"LoungeHedge{k}", (px - 56, py - 56, 64), (px + 56, py + 56, 128), "hedge")
SCREEN = (2.0, 2048.0, 704.0, 1536.0, 768.0)   # x, centre y, centre z, width, height
art_mat("art_screen", "board", "OPENSCCT", "lounge  -  welcome", w=1024, h=512)
box("LoungeScreen", (0.5, 2048 - 784, 304), (3.5, 2048 + 784, 1104), "darkpanel", faces="e")
wall_sign("LoungeScreen", (SCREEN[0] + 2, SCREEN[1], SCREEN[2]), SCREEN[3], SCREEN[4], E, "art_screen")
# The ticker texture holds the whole message once (6486 px at its font size), so its wrap is seamless; the strip
# shows a 2048 px window of it (the letters' shape) and pans about 82 px a second.
TICKER_W, TICKER_VIEW = 6528, 2048
art_mat("art_ticker", "ticker", "WELCOME TO THE OPENSCCT HUB   *   MATCH PADS: EAST   *   AIM RANGE: WEST   *   "
        "MOVEMENT COURSE: SOUTH   *   TRAVERSAL GARDEN: NORTH   *   ", TICKER_W, 128,
        uv_anim={"type": "friendly_panner", "rate": 1.0, "pan_u": round(82.0 / TICKER_W, 5), "pan_v": 0.0,
                 "scale_u": 1.0, "scale_v": 1.0})
wall_sign("LoungeScreen", (4.5, SCREEN[1], 256), SCREEN[3], 96, E, "art_ticker", u_span=TICKER_VIEW / TICKER_W)
art_mat("art_sign_lounge", "sign", ["LOUNGE"], 512, 160, fg=(255, 80, 200))
wall_sign("LoungeSign", (1408, 1.5, 820), 1024, 320, N, "art_sign_lounge")
wall_sign("LoungeSign", (2817, 2048, 900), 768, 240, E, "art_sign_lounge", double=True)
for dy in (-320, 320):
    beam("LoungeSign", (2817, 2048 + dy, 1020), (2817, 2048 + dy, 1536), 4, "metal")
for k, (lx, ly, hue, sat) in enumerate(((1408, 1100, 20, 150), (1536, 2048, 20, 160), (1408, 3100, 20, 150),
                                        (800, 2048, 220, 140))):
    light(f"LoungeLight{k}", (lx, ly, 520), 190, 60, hue=hue, saturation=sat)
area("Lounge", "lounge", "Lounge", LOUNGE_LO, LOUNGE_HI, "H1",
     screenAnchor=[SCREEN[0] + 3, SCREEN[1], SCREEN[2]], screenYaw=YAW_E, screenSize=[SCREEN[3], SCREEN[4]],
     musicAnchor=[1536.0, 2048.0, 256.0], seats=[[1536.0, 1536.0], [1536.0, 2560.0], [2240.0, 1600.0], [2240.0, 2496.0]])

# ---- Rooftop dressing: a water tower (north-east), air-conditioning units, a helipad (south-east) ------------------
WT = (10880.0, 10880.0)
for k, (sx, sy) in enumerate(((-1, -1), (1, -1), (-1, 1), (1, 1))):
    solid(f"WaterTowerLeg{k}", (WT[0] + sx * 192 - 16, WT[1] + sy * 192 - 16, 0), (WT[0] + sx * 192 + 16, WT[1] + sy * 192 + 16, 640),
          "metal")
box("WaterTower", (WT[0] - 256, WT[1] - 256, 640), (WT[0] + 256, WT[1] + 256, 1040), "museum_wood")
box("WaterTower", (WT[0] - 270, WT[1] - 270, 1040), (WT[0] + 270, WT[1] + 270, 1080), "darkpanel")
box("WaterTower", (WT[0] - 258, WT[1] - 258, 760), (WT[0] + 258, WT[1] + 258, 776), "neon_green", faces="nsew")
art_mat("art_sign_tower", "sign", ["OPENSCCT"], 512, 128)
for n in (S, W):
    wall_sign("WaterTower", (WT[0] + n[0] * 259, WT[1] + n[1] * 259, 900), 448, 112, n, "art_sign_tower")
beam("WaterTower", (WT[0] + 300, WT[1] + 300, 0), (WT[0] + 300, WT[1] + 300, 1500), 10, "metal")
box("WaterTower", (WT[0] + 294, WT[1] + 294, 1500), (WT[0] + 306, WT[1] + 306, 1512), "neon_red")
for k, (hx, hy) in enumerate(((10000, 9200), (11500, 9500), (1000, 9400), (1800, 10900), (10300, 3100),
                              (11500, 600 + 256))):
    solid(f"Hvac{k}", (hx - 128, hy - 96, 0), (hx + 128, hy + 96, 160), "metal")
    box("Hvac", (hx - 112, hy - 80, 160), (hx + 112, hy + 80, 161), "hvac", faces="t")
art_mat("art_helipad", "helipad", unlit=False)
decal("Helipad", (10880.0, 1920.0), 1280, 1280, N, "art_helipad", z=0.5)
for k, (cx_, cy_) in enumerate(((10880 - 680, 1920), (10880 + 680, 1920), (10880, 1920 - 680), (10880, 1920 + 680))):
    box("Helipad", (cx_ - 12, cy_ - 12, 0), (cx_ + 12, cy_ + 12, 10), "neon_red")
light("HelipadLight", (10880, 1920, 700), 110, 60, hue=0, saturation=200)
light("WaterTowerLight", (WT[0], WT[1] - 500, 500), 120, 56, hue=85, saturation=180)

# ---- The zone ------------------------------------------------------------------------------------------------------
actor("ZoneHall", "Engine.ZoneInfo", (CX, CY, 1400.0), LocationName="Hub", AmbientBrightness=ZONE_AMBIENT, Tag="ZoneInfo")

LEVEL_INFO = {"Title": TITLE, "Author": "OpenSCCT", "RecommendedNumPlayers": 50, "GEActivationMask": 0, "Tag": "LevelInfo"}
MAP_SETTINGS = {"Story": {"TimeLimit": 0, "SpiesLives": 99, "SentriesLives": 99, "RespawnTime": 3}}


def _check() -> None:
    ids = [g.id for g in GES]
    if len(set(ids)) != len(ids):
        raise ValueError("duplicate GE id")
    names = [s.name for s in SOLIDS]
    if len(set(names)) != len(names):
        raise ValueError("duplicate solid name")
    if len({a.id for a in ACTORS}) != len(ACTORS):
        raise ValueError("duplicate actor id")
    for s in SOLIDS:
        if not all(HALL_LO[i] <= s.lo[i] and s.hi[i] <= HALL_HI[i] for i in range(3)):
            raise ValueError(f"{s.name} leaves the hall")
    for g in GROUPS.values():
        for p in g.pieces:
            if p.material not in MATERIALS:
                raise ValueError(f"{g.name}: unknown material {p.material}")
    for a in (s for s in SOLIDS):
        if a.material not in MATERIALS:
            raise ValueError(f"{a.name}: unknown material {a.material}")


_check()
