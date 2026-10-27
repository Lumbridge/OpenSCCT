# TestRange, the test map

TestRange is a map made for tests: one hall with every traversal station, a dark-to-lit room, a shooting lane with
glass and a grating, the objectives, pickups and cameras, the starts and open floor for bots, each at coordinates a
test can read from a manifest. It is our own geometry (boxes, two wedges, one carved hall), written as data in
`tools/testmap/layout.py`, and it exists in two forms with the same coordinates:

| Form | Made by | Where | For |
|---|---|---|---|
| SVMap 1.1 (sim and render) | `tools/testmap/generate.py` | `.content/svmap/TestRange` (local), or any folder | tests, smokes and play without the game install |
| `.sdc` twin | the map editor's automation in the sandbox, `tools/testmap/twin.py build` | `local/testrange/TestRange/` and the sandbox's `Packages/MapsEd` | recordings of the original game on the same layout |

Neither form is tracked. The tracked files are the layout, the generator and twin tools (`tools/testmap/`), the
station manifest (`fixtures/testrange/stations.json`, written by the generator), the tests and this page.

## Rebuilding

```
python -B tools/testmap/generate.py              # .content/svmap/TestRange and fixtures/testrange/stations.json
python -B tools/testmap/generate.py --check      # fails when the folder or the manifest is not what the layout makes
python -B tools/testmap/generate.py --out DIR --no-classes --stations-out FILE    # what the C# tests run
python -B -m pytest tools/testmap -q             # layout, BSP and manifest checks (no game)
```

The generator writes `sim/classes.json` only where the reference install is (it reads the class defaults with the
pipeline's class reader, the same table the converter writes for the twin); without it the map has no class table,
as TestBox, and loads with the generated class defaults.

The twin (headless editor and Play Level in `F:\SCCT-Automation\svm\Sandbox`, under the play lock; every process the
tool starts is ended before it returns):

```
python -I -B tools/testmap/twin.py build         # MAP NEW, the change file, GEs, build all, save; editor probes
python -I -B tools/testmap/twin.py play          # Play Level as the spy; keeps the game's form of the map
python -B tools/testmap/twin.py convert          # that form through svmbuild's map converter
python -B tools/testmap/twin.py diff             # the converted twin against .content/svmap/TestRange
```

`build` writes the change file (`local/testrange/work/testrange-changes.json`), the GE commands and a T3D export, and
`local/testrange/build-report.json` (Map Check, GE handling, the editor's level stats, the floor and zone under every
station spot). `play` copies the map Play Level saved for the game (`Packages/Maps/Autoplay.sdc`) while its game
holds the play lock: the converter reads the game's form, not the editor's (`MapsEd` keeps editor data it does not
decode). `convert` makes `local/testrange/game`, the reference install with the twin as its only map (junctions to the
install's folders, read only), and runs `svmbuild.mapconv.build_map` and the SVMap validator on it. `diff` writes
`local/testrange/diff-report.json` and prints:

```
actors: 22 native, 22 twin, worst location 0.000 uu, rotation 0, scale 0.0000
GEs: 14 native, 14 twin, worst point 0.000 uu
world: 54528 lines, 0 hit differently, 0 on another surface, worst hit 0.000 uu
zones: 0 grid points differ
actor collision: 10 colliders, worst bounds 3.48 uu
lights: 54 native, 54 twin
SAME within tolerance
```

The world lines are traced through each map's world BSP the way the engine walks it (down on a 32 uu grid and level
across at six heights); the tolerances are in `tools/testmap/twindiff.py`. Gameplay objects collide through boxes
round their meshes' collision in the SVMap form (measured from the converted twin), so their bounds may differ by a
few units.

## How the SVMap form is made

- The world is the boundary between empty space (the hall and the pool, less the solids) and solid, cut out of the
  faces of every carve and solid (`tools/testmap/bsp.py`). Where two solids have coplanar faces facing the same way,
  the later one's face is kept, as the editor's CSG keeps the later brush's.
- Collision: a world BSP over those polygons (`sim/collision.bin` `BSPN`), a node per polygon plane, front empty and
  back solid, so line checks walk it as they walk a converted map's; the gradient room's zone is split off first by
  planes that carry no polygon, set in the middle of its walls and roof where the twin's portals are. The same
  polygons are the world collider's triangles (box sweeps). Gameplay objects get the converter's colliders (per-poly
  and simple sets, PrePivot), glass a quad, the grating a box that lets bullets through; interaction cylinders and
  zones as the converter writes them.
- Render: the world polygons as vertex-lit meshes (`render/world.glb`), lit by the map's lights with the actor
  lighting model and no shadows, on our own 128 x 128 grid textures (one tile per 256 uu). The lights and zones are in
  `render/lighting.json`, so pawns and the bots' light field use them as on converted maps. The pipe and the distance
  marks are drawn and do not collide; the twin has neither (the original draws no pipe there).
- Materials: every surface names the stock texture the twin uses and carries that texture's SurfaceType,
  OcclusionType and bClimbable (`layout.MATERIALS`), so footsteps, impacts, water and the fence behave alike.

## Stations

Units are uu (UE space, Z up, yaw 16384 = +Y). The hall is x 0..8192, y 0..6144, z 0..1024. A station's spot is
where a test pawn stands (feet), facing its yaw. `fixtures/testrange/stations.json` has every station with its GE
ids and points, sizes, and the actors by id.

| Station | Kind | Spot (yaw) | What | Copies |
|---|---|---|---|---|
| Ledge128, Ledge208, Ledge264, Ledge320 | ledge | 100 south of each block (+Y) | blocks against the north wall, LG on the south lip | the 208 to 320 hang range (RE+), SteeE LG 104 (263.5) |
| Boost448 | coopBoost | 100 south (+Y) | a lip out of solo reach | our choice: the boost's reach is not measured |
| WallJump408 | wallJump | 210 south (+Y) | a wall with an LG on its 408 lip | SteeE LG 205 (409.3, 210 run-up) |
| Pipe512 | pipe | 120 south (+Y) | PIPE 16 off a 512 block, top flush | SteeE PIPE 22 |
| Ladder320 | ladder | 146 south (+Y) | LADDER 4 off a 320 block, top flush | MounE LADDER 149 |
| SplitShaft192 | splitJump | 37.5 from the west wall, 134.5 from the closed end (-Y) | two walls 192 apart, 512 high | SteeE PIPE 21 shaft (191) |
| Vault96 | vault | 136 south (+Y) | barrier 96 high, 32 thick, LG on both lips | SteeE LG 12 |
| Fence376 | fence | 155 south (+Y) | climbable panel 2 thick (as real fences; thicker blocks the climb over), FENCE on top | MounE FENCE 225 (377.4) |
| Hoh277 | hoh | 198 along the bar (+X) | bar between pillars, HOH 277 over the floor | MounE HOH 12 |
| Curb17, Step34, Block50 | step | 100 south (+Y) | blocks of 17, 34 and 50 | SteeE curb, MAXSTEPHEIGHT, MounE 50 block |
| Stairs16 | stairs | 100 before the first riser (+X) | 10 risers of 16 by 32, landing at 160 | |
| Ramp384 | ramp | 100 before the foot (+X) | 384 over 768 (26.57 degrees) up to the deck | SteeE stair ramp (26.85) |
| Drop384, Drop600 | drop | on the deck and tower edges | 384 and 600 drops to the floor | spy unhurt drop 600, merc 450 |
| Zip1760 | zipLine | on the deck, 40 down the line (-Y) | ZL 1788.9 long (1760 run, 320 drop, slope 0.179), 304 over the deck, landing deck 208 under the low end | SteeE ZL 29 |
| Gradient | lightGradient | inside the west door (+X) | room x 256..2304, y 1536..2048, 384 high, own zone, no ambient, six lights from 24 to 255 brightness | samples every 256 along it |
| Lane | shootingLane | firing line, x 512 (+X) | lane y 256..768 to a berm at 3584, marks every 250 | rifle range tests |
| MotionPanels | motionPanels | walk line 240 behind the panel wall (+Y) | panel wall at 1000: SVitre pane, open window, grating (bBulletGoTru) | motion tracking occlusion |
| Terminal, Disk, DropZone | terminal, disk, dropZone | use spots | desk 87 high, table 80, drop zone on the floor; mission: 2 of 2 | MapBuilderFacts laptop rules |
| Medkit, Ammo | medkit, ammo | in front of them | SInfirmary on the south wall at 140, SAmmoStock by the merc starts | |
| CamNetwork | cameraNetwork | | three SCamNetwork globes in one ring | |
| SpyStarts, MercStarts | spawn | | four starts each, 86 over the floor, west and east | SteeE starts (85.5) |
| OpenFloor | openFloor | | x 2560..4864, y 1280..4096, nothing on it | |
| Water | water | the pool's middle | pool 512 x 512, 16 deep, SURFACE_MediumWater | SteeE water texture |

## How tests use it

- `tests/OpenScct.Content.Tests/TestRangeTests.cs` generates the map into a scratch folder (Python with numpy, Pillow and
  jsonschema; skipped without, unless `SVM_REQUIRE_PYTHON`), checks the tracked manifest is current, loads the map
  with the game's classes, and checks every station against the collision, GEs, zones and lights: floors under the
  spots, lips at their heights with a wall under them, pipe and ladder tops flush, the zip line's length, slope and
  landing, the shaft's gap, steps and ramp, drops, the water's surface type, the panel wall's glass, open window and
  bullet-passable grating, the room's own zone and its light rising from 0 to the lit door (the bots' light field).
- Smokes (`game/Scripts/Smoke/TestRangeSmokes.cs`), all through the player's path (keys as Godot input events, host
  thread, wall clock, HUD), with `SVM_PLAY_SHOTS` for screenshots:
  - `range-traversal`: the server puts the spy on a station's spot and reinitialises the client there, then the keys
    play a recorded scenario's inputs: the ledge pull-up at Ledge264, the zip line at Zip1760, the pipe at Pipe512,
    the ladder at Ladder320. Fails on reinits, corrections during a move, or a spy that does not end on top.
  - `range-motion`: a merc at the firing line turns motion tracking on with its key while a spy bot, whose keys the
    test holds on the server, walks behind the panel wall; reports what the window shows at the spy behind each
    opening, with a screenshot each.
  - `range-bots`: a spy and three bots; every bot drawn, two walking, moving pawns animated.
- `TestRange.Stations()` and `TestRange.Start()`, `Place()`, `Face()` and `Shot()` in the same file are the helpers for
  new smokes on the map.
- Recordings: `tests/scenarios/testrange/spy-ledge-pull-up.scenario.json` plays the SteeE ledge scenario's inputs at
  Ledge264. It is recorded on the twin with `re/tools/difftest/record.py` (the twin must be in the sandbox's MapsEd and
  its GE list decoded: `python -B -m scctformats.ge <twin .sdc>` from `re/formats`), and `tests/OpenScct.DiffTest`
  (`RunnerTests`) plays it in the sim on the SVMap form: the same phases on the same frames, location RMS 0.01 uu
  (DtReplay) and 1.44 (Fixed), against a 5 uu profile.

The command for the recording (three runs, three games):

```
python -I -B re/tools/difftest/record.py record "tests/scenarios/testrange/*.scenario.json" --runs 3 --out local/difftest/golden-traversal
```

Known finding: `range-traversal` fails on the pipe. The server corrects the client once in s_OnPipe just after the
grab, 10 to 17 uu up; the same happens on SteeE's PIPE 22 through the same path, so it is the pipe climb's
replication, not the map.

To add a station: add its geometry, GEs and actors to `layout.py` and a `station(...)` with its spot and data, run
the generator (the manifest changes with it), add a check to `TestRangeTests`, and rebuild, play, convert and diff the
twin before recording on it.

## Limits and notes

- The map's name has nine letters. Play Level runs it; the game's own map lists want five (RE+ `newmap.py`), so the
  twin is not a map to ship.
- The co-op boost lip's height is ours: the boost's reach has not been measured in the game.
- The SVMap form's world light is a vertex bake with no shadows; the twin's is the editor's lightmaps. Lights,
  zones and their ambient are the same in both.
- The sandbox is shared by the SVM workers: `twin.py play` keeps the Autoplay map only while its own game holds the
  play lock, and `record.py` loads the twin from the sandbox's MapsEd.
