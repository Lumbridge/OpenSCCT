"""TestRange: the layout of the test map, as data. Our own geometry (boxes, wedges, one carved hall), no game content.

Every station sits at fixed coordinates in Unreal units (UE space: left-handed, Z up, yaw 0 = +X, 16384 = +Y). Both
forms of the map are made from this one module, so they share every coordinate:

- the native SVMap 1.1 folder (`tools/testmap/generate.py`), which tests and CI load without the game;
- the .sdc twin, built through the map editor's automation in the sandbox (`tools/testmap/twin.py`), for recordings
  of the original game on the identical layout.

The hall is one carved space (in the editor: one subtractive brush; in the SVMap: floor, ceiling and wall slabs
round it). Stations are solids added inside it, a carved pool, GEs on their lips, and gameplay actors. Heights and
gaps follow spots measured on the stock maps (docs/spikes/R4-moveset.md, R4-traversal.md and the scenario files), so
recordings here are comparable with the SteeE and MounE ones. docs/TESTMAP.md lists the stations.

Nothing here reads files or the clock: the layout is the same on every run.
"""

from __future__ import annotations

from dataclasses import dataclass, field

MAP_ID = "TestRange"
TITLE = "TestRange"
LAYOUT_VERSION = 1

Vec3 = tuple[float, float, float]

# The hall: the one carved space. Interior bounds; walls, floor and ceiling are T thick in the SVMap form.
HALL_LO: Vec3 = (0.0, 0.0, 0.0)
HALL_HI: Vec3 = (8192.0, 6144.0, 1024.0)
T = 32.0

# Materials: each has a stock texture for the twin and the gameplay values that texture carries in the original
# (SurfaceType, OcclusionType, bClimbable), which the SVMap form writes so both forms collide and sound alike.
# render: the colour of our own grid texture (linear RGB 0..1) in the SVMap form.


@dataclass(frozen=True)
class Material:
    key: str
    stock: str               # texture the twin's brush faces use
    surface: int             # ESurfaceType
    occlusion: int           # OcclusionType of that texture
    climbable: bool
    colour: tuple[float, float, float]


MATERIALS: dict[str, Material] = {m.key: m for m in [
    Material("floor", "Aquarium_TXT.Beton.aqua_betonfloor01", 5, 8, False, (0.42, 0.42, 0.44)),
    Material("wall", "Aquarium_TXT.Beton.aqua_betonwall04", 5, 8, False, (0.60, 0.60, 0.58)),
    Material("ceiling", "Aquarium_TXT.Beton.aqua_betonwall04", 5, 8, False, (0.36, 0.36, 0.40)),
    Material("block", "Aquarium_TXT.Beton.aqua_betonwall04", 5, 8, False, (0.62, 0.56, 0.44)),
    Material("deck", "Map17_TXT.bp_bsp.PB_metalsol01", 17, 8, False, (0.40, 0.48, 0.62)),
    Material("metal", "SQU13_txt.BSP.SQU13_Metal01", 14, 4, False, (0.70, 0.46, 0.22)),
    Material("fence", "drpoc_chantier.SM.moa_grillage", 22, 1, True, (0.30, 0.62, 0.34)),
    Material("water", "SQU13_txt.BSP.SQU13_Water01_PAN", 18, 6, False, (0.18, 0.34, 0.62)),
]}

# Solids: boxes and wedges added in the hall. `up` on a wedge is the direction it rises toward.


@dataclass(frozen=True)
class Solid:
    name: str
    lo: Vec3
    hi: Vec3
    material: str
    up: str | None = None        # None: a box; '+x' '-x' '+y' '-y': a wedge rising that way from lo.z to hi.z


@dataclass(frozen=True)
class Carve:
    """A space cut out of solid: the hall itself and the water pool."""
    name: str
    lo: Vec3
    hi: Vec3
    floor: str
    walls: str
    ceiling: str


@dataclass(frozen=True)
class GE:
    id: str
    kind: str                    # LG PIPE LADDER ZL HOH FENCE (POLE unused)
    points: tuple[Vec3, ...]
    note: str


@dataclass(frozen=True)
class Light:
    id: str
    location: Vec3
    brightness: float
    radius: float                # LightRadius (the reach is 25 x (LightRadius + 1))
    zone: str
    hue: int = 0
    saturation: int = 255


@dataclass(frozen=True)
class Zone:
    id: str
    name: str
    lo: Vec3
    hi: Vec3
    ambient: int                 # AmbientBrightness (0..255), hue 0 saturation 255
    marker: Vec3                 # where the ZoneInfo actor stands


@dataclass
class Actor:
    id: str
    cls: str
    location: Vec3
    yaw: int = 0
    pitch: int = 0
    props: dict = field(default_factory=dict)


@dataclass(frozen=True)
class Station:
    id: str
    kind: str
    title: str
    spot: Vec3                   # where a test pawn stands (its feet), facing `yaw`
    yaw: int
    data: dict


CARVES: list[Carve] = [
    Carve("Hall", HALL_LO, HALL_HI, "floor", "wall", "ceiling"),
    Carve("Pool", (7168.0, 4864.0, -16.0), (7680.0, 5376.0, 0.0), "water", "wall", "floor"),
]

SOLIDS: list[Solid] = []
GES: list[GE] = []
ACTORS: list[Actor] = []
LIGHTS: list[Light] = []
STATIONS: list[Station] = []
# Floor marks: render-only quads lying on the floor (lo, hi in x and y, label), no collision.
MARKS: list[tuple[str, tuple[float, float], tuple[float, float], str]] = []
# Render-only boxes (name, lo, hi, material): drawn by the SVMap form, no collision. The twin leaves them out.
VISUALS: list[tuple[str, Vec3, Vec3, str]] = []


def solid(name, lo, hi, material="block", up=None):
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


def station(ident, kind, title, spot, yaw, **data):
    STATIONS.append(Station(ident, kind, title, tuple(map(float, spot)), int(yaw), data))


def actor(ident, cls, location, yaw=0, pitch=0, **props):
    a = Actor(ident, cls, tuple(map(float, location)), int(yaw), int(pitch), props)
    ACTORS.append(a)
    return a


YAW_E, YAW_N, YAW_W, YAW_S = 0, 16384, 32768, 49152     # facing +X, +Y, -X, -Y

# ---- North strip: blocks against the north wall (y 6144), approached from the south -----------------------------
NORTH_FACE = 5888.0            # the blocks' south faces


def ledge_block(ident, x0, x1, top, title, note):
    solid(ident, (x0, NORTH_FACE, 0), (x1, HALL_HI[1], top))
    g = ge(ident + "LG", "LG", (x0, NORTH_FACE, top), (x1, NORTH_FACE, top), note=note)
    station(ident, "ledge", title, ((x0 + x1) / 2, NORTH_FACE - 100, 0), YAW_N, height=top, ge=g,
            lip=[[x0, NORTH_FACE, top], [x1, NORTH_FACE, top]])


ledge_block("Ledge128", 256, 512, 128, "Ledge 128 (hop-up)", "under the hang height: hop-up")
ledge_block("Ledge208", 768, 1024, 208, "Ledge 208", "lowest hang and climb")
ledge_block("Ledge264", 1280, 1536, 264, "Ledge 264", "SteeE container lip height (LG 104, 263.5)")
ledge_block("Ledge320", 1792, 2048, 320, "Ledge 320", "highest standing-jump grab")

# Co-op boost: too high for a standing jump; a mate's short-ladder boost reaches it (the height is our choice).
solid("Boost448", (2304, NORTH_FACE, 0), (2560, HALL_HI[1], 448))
ge("Boost448LG", "LG", (2304, NORTH_FACE, 448), (2560, NORTH_FACE, 448), note="out of solo reach: co-op boost")
station("Boost448", "coopBoost", "Co-op boost ledge 448", (2432, NORTH_FACE - 100, 0), YAW_N, height=448,
        ge="Boost448LG", helper=[2432, NORTH_FACE - 60, 0])

# Wall jump: a wall with a ledge on its lip 408 over the floor and a long run-up (SteeE LG 205: 409.3, 210 run-up).
solid("WallJump408", (2816, NORTH_FACE, 0), (3200, HALL_HI[1], 408))
ge("WallJump408LG", "LG", (2816, NORTH_FACE, 408), (3200, NORTH_FACE, 408), note="wall jump: kick up to this lip")
station("WallJump408", "wallJump", "Wall jump to a 408 lip", (3008, NORTH_FACE - 210, 0), YAW_N, height=408,
        ge="WallJump408LG", runUp=210)

# Vertical pipe up a 512 block, the pipe 16 off the wall (SteeE PIPE 22 form), top flush with the block top.
solid("Pipe512", (3456, NORTH_FACE, 0), (3712, HALL_HI[1], 512))
PIPE_X, PIPE_Y = 3584.0, NORTH_FACE - 16
# The pipe itself is drawn, not collided with: a climber hangs 35 from the GE line, inside any solid pipe thicker than
# a hair (the stock pipe meshes stop a walker 35.7 from the line), so a solid column would hold him on the way up.
VISUALS.append(("Pipe512Pipe", (PIPE_X - 4, PIPE_Y - 4, 0.0), (PIPE_X + 4, PIPE_Y + 4, 512.0), "metal"))
ge("Pipe512PIPE", "PIPE", (PIPE_X, PIPE_Y, 0), (PIPE_X, PIPE_Y, 512), (PIPE_X, PIPE_Y - 10, 0),
   note="bottom, top flush with the block top, side 10 toward the climber")
station("Pipe512", "pipe", "Vertical pipe 512", (PIPE_X, PIPE_Y - 120, 0), YAW_N, height=512, ge="Pipe512PIPE")

# Ladder up a 320 block, the GE 4 in front of the wall (MounE LADDER 149 form: 320 high, top flush).
solid("Ladder320", (3968, NORTH_FACE, 0), (4224, HALL_HI[1], 320))
LADDER_X, LADDER_Y = 4096.0, NORTH_FACE - 4
ge("Ladder320LADDER", "LADDER", (LADDER_X, LADDER_Y, 0), (LADDER_X, LADDER_Y, 320), (LADDER_X, LADDER_Y - 10, 0),
   note="bottom, top flush with the block top, side 10 toward the climber")
station("Ladder320", "ladder", "Ladder 320", (LADDER_X, LADDER_Y - 146, 0), YAW_N, height=320, ge="Ladder320LADDER")

# Split jump shaft: two walls 192 apart (SteeE PIPE 21 shaft: 191), 512 high, open to the south.
solid("SplitWallW", (4480, 5632, 0), (4512, HALL_HI[1], 512))
solid("SplitWallE", (4704, 5632, 0), (4736, HALL_HI[1], 512))
# The spot copies SteeE's (spy-split-jump): 37.5 from one wall, 134.5 from the shaft's closed end, facing out along it.
station("SplitShaft192", "splitJump", "Split jump shaft 192", (4512 + 37.5, HALL_HI[1] - 134.5, 0), YAW_S, gap=192,
        height=512, walls=[4512, 4704])

# ---- Middle strip: free-standing pieces with floor on both sides -------------------------------------------------
solid("Vault96", (256, 4608, 0), (640, 4640, 96))
ge("Vault96LGS", "LG", (256, 4608, 96), (640, 4608, 96), note="barrier lip, south face")
ge("Vault96LGN", "LG", (256, 4640, 96), (640, 4640, 96), note="barrier lip, north face")
station("Vault96", "vault", "Vault barrier 96 x 32", (448, 4608 - 136, 0), YAW_N, height=96, thick=32,
        ge=["Vault96LGS", "Vault96LGN"])

# The panel is 2 thick like the real maps' fences (StatD, MounE, SancD: 1 to 2): CheckFenceTopArms sweeps a (35, 35, 20)
# box down 75 from the climber, who hangs R + 2 = 37 off the face, so a panel over 3 thick blocks the climb over.
solid("Fence376", (1024, 4623, 0), (1408, 4625, 376), "fence")
ge("Fence376FENCE", "FENCE", (1024, 4624, 376), (1408, 4624, 376), note="climbable both faces, nothing above")
station("Fence376", "fence", "Fence 376 (climbable both sides)", (1216, 4624 - 155, 0), YAW_N, height=376,
        ge="Fence376FENCE")

solid("HohPillarW", (1792, 4592, 0), (1856, 4656, 416))
solid("HohPillarE", (2496, 4592, 0), (2560, 4656, 416))
solid("HohBar", (1856, 4620, 276), (2496, 4628, 284), "metal")
ge("Hoh277HOH", "HOH", (1856, 4624, 277), (2496, 4624, 277), note="bar underside 1 under the line (MounE HOH 12: 277)")
station("Hoh277", "hoh", "Hand over hand 277", (1856 + 198, 4624, 0), YAW_E, height=277, ge="Hoh277HOH")

# Steps: a curb, a step under MAXSTEPHEIGHT, a block over it, and a flight of 10 risers of 16 by 32.
solid("Curb17", (2944, 4480, 0), (3200, 4736, 17))
station("Curb17", "step", "Curb 17", (3072, 4480 - 100, 0), YAW_N, height=17)
solid("Step34", (3328, 4480, 0), (3584, 4736, 34))
station("Step34", "step", "Step 34", (3456, 4480 - 100, 0), YAW_N, height=34)
solid("Block50", (3712, 4480, 0), (3968, 4736, 50))
station("Block50", "step", "Block 50 (blocks a walk)", (3840, 4480 - 100, 0), YAW_N, height=50)
for k in range(10):
    solid(f"Stairs{k + 1}", (4224 + 32 * k, 4480, 0), (4256 + 32 * k, 4736, 16 * (k + 1)))
solid("StairsLanding", (4544, 4480, 0), (4800, 4736, 160))
station("Stairs16", "stairs", "Stairs 10 x 16 rise, 32 run", (4224 - 100, 4608, 0), YAW_E, rise=16, run=32, count=10,
        top=160)

# Ramp up to the deck (26.57 degrees: 384 over 768), the deck with 384 drops, a tower with a 600 drop on the deck.
solid("Ramp384", (5120, 4352, 0), (5888, 4736, 384), "deck", up="+x")
station("Ramp384", "ramp", "Ramp 384 over 768 (26.57 degrees)", (5120 - 100, 4544, 0), YAW_E, rise=384, run=768)
solid("Deck384", (5888, 3712, 0), (6656, 4736, 384), "deck")
station("Drop384", "drop", "Drop 384 off the deck's south edge", (6272, 3712 + 60, 384), YAW_S, height=384)
solid("Tower600", (6400, 4480, 0), (6656, 4736, 600))
TOWER_LADDER_X = 6400.0 - 4
ge("Tower600LADDER", "LADDER", (TOWER_LADDER_X, 4608, 384), (TOWER_LADDER_X, 4608, 600), (TOWER_LADDER_X - 10, 4608, 384),
   note="from the deck to the tower top, flush")
station("Drop600", "drop", "Drop 600 off the tower's east edge", (6656 - 60, 4608, 600), YAW_E, height=600)

# Zip line from the deck south over the hall to a landing deck: 1760 run, 320 drop (SteeE ZL 29: 1788 long, slope
# 0.178, 304.8 over its platform). The landing deck's top is 208 under the low end, where a rider's feet hang.
ZIP_HIGH: Vec3 = (6144.0, 3824.0, 688.0)
ZIP_LOW: Vec3 = (6144.0, 2064.0, 368.0)
ge("Zip1760ZL", "ZL", ZIP_LOW, ZIP_HIGH, note="high end 304 over the deck, low end over the landing deck")
solid("ZipLanding", (6016, 1792, 0), (6272, 2304, 160), "deck")
station("Zip1760", "zipLine", "Zip line 1760 run, 320 drop", (ZIP_HIGH[0], ZIP_HIGH[1] - 40, 384), YAW_S,
        ge="Zip1760ZL", high=list(ZIP_HIGH), low=list(ZIP_LOW), landingTop=160)

# ---- Dark-to-lit gradient room: its own zone, no ambient, six lights brightening toward the east door -----------
ROOM_LO: Vec3 = (256.0, 1536.0, 0.0)
ROOM_HI: Vec3 = (2304.0, 2048.0, 384.0)
DOOR_Y = (1712.0, 1872.0)
DOOR_TOP = 256.0
for side, (x0, x1) in (("W", (224.0, 256.0)), ("E", (2304.0, 2336.0))):
    solid(f"Room{side}0", (x0, 1536, 0), (x1, DOOR_Y[0], 384), "wall")
    solid(f"Room{side}1", (x0, DOOR_Y[1], 0), (x1, 2048, 384), "wall")
    solid(f"Room{side}Lintel", (x0, DOOR_Y[0], DOOR_TOP), (x1, DOOR_Y[1], 384), "wall")
solid("RoomS", (224, 1504, 0), (2336, 1536, 384), "wall")
solid("RoomN", (224, 2048, 0), (2336, 2080, 384), "wall")
solid("RoomRoof", (224, 1504, 384), (2336, 2080, 416), "wall")
GRADIENT_LIGHTS = [(896.0, 24.0), (1152.0, 48.0), (1408.0, 80.0), (1664.0, 128.0), (1920.0, 192.0), (2176.0, 255.0)]
for i, (x, b) in enumerate(GRADIENT_LIGHTS):
    LIGHTS.append(Light(f"RoomLight{i}", (x, 1792.0, 320.0), b, 14.0, "ZoneRoom"))
station("Gradient", "lightGradient", "Dark-to-lit gradient room", (384, 1792, 0), YAW_E,
        samples=[[x, 1792.0, 0.0] for x in range(384, 2305, 256)], doors=[[240, 1792, 0], [2320, 1792, 0]])

# ---- Shooting lane with distance marks, and a panel wall with glass, an open window and a grating -----------------
LANE_Y = (256.0, 768.0)
FIRING_X = 512.0
MARK_STEP = 250.0
for k in range(1, 13):
    x = FIRING_X + MARK_STEP * k
    MARKS.append((f"Mark{int(MARK_STEP * k)}", (x - 4, LANE_Y[0]), (x + 4, LANE_Y[1]), f"{int(MARK_STEP * k)}"))
MARKS.append(("Mark0", (FIRING_X - 4, LANE_Y[0]), (FIRING_X + 4, LANE_Y[1]), "0"))
solid("LaneBerm", (3584, 256, 0), (3648, 768, 384), "wall")
PANEL_X = (1512.0, 1528.0)
OPENINGS = {"glass": (288.0, 416.0), "open": (448.0, 576.0), "grating": (608.0, 736.0)}
OPEN_Z = (32.0, 224.0)
posts = [(256.0, 288.0), (416.0, 448.0), (576.0, 608.0), (736.0, 768.0)]
for i, (y0, y1) in enumerate(posts):
    solid(f"PanelPost{i}", (PANEL_X[0], y0, 0), (PANEL_X[1], y1, 256), "wall")
for name, (y0, y1) in OPENINGS.items():
    solid(f"PanelSill_{name}", (PANEL_X[0], y0, 0), (PANEL_X[1], y1, OPEN_Z[0]), "wall")
    solid(f"PanelLintel_{name}", (PANEL_X[0], y0, OPEN_Z[1]), (PANEL_X[1], y1, 256), "wall")
PANE_CENTRE = {k: ((PANEL_X[0] + PANEL_X[1]) / 2, (y0 + y1) / 2, (OPEN_Z[0] + OPEN_Z[1]) / 2) for k, (y0, y1) in OPENINGS.items()}
PANE_SIZE = (OPENINGS["glass"][1] - OPENINGS["glass"][0], OPEN_Z[1] - OPEN_Z[0])        # 128 wide, 192 high
station("Lane", "shootingLane", "Shooting lane 0 to 3000 with marks every 250", (FIRING_X, 512, 0), YAW_E,
        firingX=FIRING_X, lane=list(LANE_Y), markStep=MARK_STEP, marks=[FIRING_X + MARK_STEP * k for k in range(13)],
        backstop=3584)
station("MotionPanels", "motionPanels", "Panel wall at 1000: glass, open window, grating", (1760, 272, 0), YAW_N,
        panelX=list(PANEL_X), openings={k: list(v) for k, v in OPENINGS.items()}, openZ=list(OPEN_Z),
        walkFrom=[1760, 272, 0], walkTo=[1760, 752, 0], watchFrom=[FIRING_X, 512, 0])

# Glass: an SVitre pane filling the glass opening (the class mesh scaled to it; see generate.py and twin.py).
actor("SVitre0", "SBase.SVitre", PANE_CENTRE["glass"], YAW_N, Tag="SVitre")

# ---- Objectives, pickups, cameras -------------------------------------------------------------------------------
solid("Desk", (4992, 640, 0), (5120, 704, 87), "metal")
TERMINAL = (5056.0, 656.0, 87.0)
actor("SComputerObjectiveTrigger0", "SBase.SComputerObjectiveTrigger", TERMINAL, YAW_S, DominationCaptureTime=10,
      TriggerName="Range Terminal", Tag="SComputerObjectiveTrigger")
station("Terminal", "terminal", "Terminal on a desk (hack from the south)", (5056, 656 - 64, 0), YAW_N,
        actor="SComputerObjectiveTrigger0", at=list(TERMINAL))
solid("DiskTable", (5504, 640, 0), (5568, 704, 80), "metal")
DISK = (5536.0, 672.0, 81.0)
DROP = (5888.0, 448.0, 1.0)
actor("SFlag0", "SBase.SFlag", DISK, YAW_W, DropZone={"$elements": {"0": {"$actor": "SFlagDropZone0"}}},
      LightBrightness=0.0, ResetTime=15, TriggerName="Range Files", fInteractionHeight=30.0, fInteractionRadius=50.0,
      Tag="SFlag")
actor("SFlagDropZone0", "SBase.SFlagDropZone", DROP, TriggerName="Range Files", fInteractionHeight=30.0,
      fInteractionRadius=50.0, Tag="SFlagDropZone")
station("Disk", "disk", "Disk on a table", (5536, 672 - 90, 0), YAW_N, actor="SFlag0", at=list(DISK))
station("DropZone", "dropZone", "Disk drop zone", (5888, 448 - 90, 0), YAW_N, actor="SFlagDropZone0", at=list(DROP))
actor("SObjective0", "SBase.SObjective", (4864.0, 512.0, 256.0), Triggers=[{"$actor": "SComputerObjectiveTrigger0"}],
      ObjectiveName="Range Terminal", Description="Terminal: Range Terminal", DescriptionDEF="Terminal: Range Terminal",
      Tag="SObjective")
actor("SObjective1", "SBase.SObjective", (4864.0, 512.0, 272.0), Triggers=[{"$actor": "SFlag0"}],
      ObjectiveName="Range Files", Description="Disk: Range Files", DescriptionDEF="Disk: Range Files", Tag="SObjective")
actor("SMission0", "SBase.SMission", (4864.0, 512.0, 320.0), Objectives=[{"$actor": "SObjective0"}, {"$actor": "SObjective1"}],
      MinimumObjectives=2, GameMode=5, bChained=False, ObjectiveName="", Description="Accomplish 2 out of 2 objectives.",
      DescriptionDEF="Prevent the intruders from reaching 2 of the 2 objectives.", Tag="SMission")

MEDKIT = (4608.0, 0.0, 140.0)
actor("SInfirmary0", "SBase.SInfirmary", MEDKIT, YAW_N, AmbientGlow=50, bBlockPlayers=True, bInteractInFront=True,
      fInteractionHeight=120.0, fInteractionRadius=60.0, Tag="SInfirmary")
station("Medkit", "medkit", "First aid on the south wall", (4608, 96, 0), YAW_S, actor="SInfirmary0", at=list(MEDKIT))
AMMO = (7680.0, 1536.0, 0.0)
actor("SAmmoStock0", "SBase.SAmmoStock", AMMO, YAW_W, AmbientGlow=40, fInteractionHeight=60.0, fInteractionRadius=120.0,
      Tag="SAmmoStock")
station("Ammo", "ammo", "Ammunition stock by the merc starts", (7680 - 110, 1536, 0), YAW_E, actor="SAmmoStock0",
        at=list(AMMO))

CAMS = [("SCamNetwork0", "North wall", (4096.0, 6144.0 - 24, 512.0), YAW_S),
        ("SCamNetwork1", "West wall", (24.0, 3072.0, 512.0), YAW_E),
        ("SCamNetwork2", "South wall", (2048.0, 24.0, 512.0), YAW_N)]
for i, (ident, name, at, yaw) in enumerate(CAMS):
    nxt, prv = CAMS[(i + 1) % len(CAMS)][0], CAMS[(i - 1) % len(CAMS)][0]
    props = {"CamName": name, "NextCam": {"$actor": nxt}, "PrevCam": {"$actor": prv}, "bBlockActors": True,
             "bBlockPlayers": True, "Tag": "SCamNetwork"}
    if i == 0:
        props["bFirstCam"] = True
    actor(ident, "SBase.SCamNetwork", at, yaw, -3000, **props)
station("CamNetwork", "cameraNetwork", "Camera network: three wall globes in one ring", (4096, 3072, 0), 0,
        actors=[c[0] for c in CAMS], at=[list(c[2]) for c in CAMS])

# ---- Starts, open floor, water ----------------------------------------------------------------------------------
START_Z = 86.0           # a start stands 86 over its floor (SteeE yard starts: 85.5)
SPY_STARTS = [(384.0, y, START_Z) for y in (2560.0, 2816.0, 3072.0, 3328.0)]
MERC_STARTS = [(7808.0, y, START_Z) for y in (2560.0, 2816.0, 3072.0, 3328.0)]
for i, at in enumerate(SPY_STARTS):
    actor(f"PlayerStart{i}", "Engine.PlayerStart", at, YAW_E, TeamNumber=0, Tag="PlayerStart")
for i, at in enumerate(MERC_STARTS):
    actor(f"PlayerStart{i + 4}", "Engine.PlayerStart", at, YAW_W, TeamNumber=1, Tag="PlayerStart")
station("SpyStarts", "spawn", "Spy starts (west)", (384, 2944, 0), YAW_E, team=0, starts=[list(s) for s in SPY_STARTS])
station("MercStarts", "spawn", "Merc starts (east)", (7808, 2944, 0), YAW_W, team=1, starts=[list(s) for s in MERC_STARTS])
station("OpenFloor", "openFloor", "Open floor for bots", (3712, 2688, 0), 0, lo=[2560, 1280, 0], hi=[4864, 4096, 0])
station("Water", "water", "Shallow water pool 16 deep", (7424, 5120, -16), 0, lo=[7168, 4864, -16], hi=[7680, 5376, 0],
        surface=18)

# ---- Zones and lights -------------------------------------------------------------------------------------------
# Zone 1 is the room (looked up first by bounds in the SVMap form), zone 2 the hall.
ZONES = [Zone("ZoneRoom", "Gradient Room", ROOM_LO, ROOM_HI, 0, (512.0, 1792.0, 300.0)),
         Zone("ZoneHall", "Hall", (HALL_LO[0], HALL_LO[1], -16.0), HALL_HI, 16, (4096.0, 3072.0, 900.0))]
for i in range(8):
    for j in range(6):
        LIGHTS.append(Light(f"HallLight{i}{j}", (512.0 + 1024 * i, 512.0 + 1024 * j, 768.0), 128.0, 64.0, "ZoneHall"))

for z in ZONES:
    actor(z.id, "Engine.ZoneInfo", z.marker, LocationName=z.name, AmbientBrightness=z.ambient, Tag="ZoneInfo")

LEVEL_INFO = {"Title": TITLE, "Author": "SVM", "RecommendedNumPlayers": 4, "GEActivationMask": 0, "Tag": "LevelInfo"}
MAP_SETTINGS = {"Story": {"TimeLimit": 30, "SpiesLives": 99, "SentriesLives": 99, "RespawnTime": 5}}


def all_station_ids() -> list[str]:
    return [s.id for s in STATIONS]


def _check() -> None:
    ids = [g.id for g in GES]
    if len(set(ids)) != len(ids):
        raise ValueError("duplicate GE id")
    names = [s.name for s in SOLIDS]
    if len(set(names)) != len(names):
        raise ValueError("duplicate solid name")
    for s in STATIONS:
        for key in ("ge",):
            for g in ([s.data[key]] if isinstance(s.data.get(key), str) else s.data.get(key, [])):
                if g not in ids:
                    raise ValueError(f"{s.id}: unknown GE {g}")


_check()
