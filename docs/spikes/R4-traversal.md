# R4 spike: golden traversal traces (SteeE)

**Result: done.** Twelve SteeE traversal scenarios were recorded three times each, every run in its own
headless sandbox Play Level game, with every frame sampled (36 runs, 0 torn, missed, late or unstable samples).
Every scenario reaches its move in every run, and the three runs of each go through the same controller states
and Physics modes; phase starts agree to the frame except for four scenarios where one landing or line end
falls a frame apart (zip line 29: three frames). The measured speeds match the decoded spec values (shimmy
80.0, pipe 145.0, zip line 423.3, dive roll 603.2, back-to-wall sidestep 130.9, crouched 221.4), and the
recorder now also writes the GE held, the GE move fields, all 16 animation channels and the root-motion lock.

Recorded 2026-10-23 from Enhanced SCCT Versus v4.0 Public Beta 3 in `F:\SCCT-Automation\svm\Sandbox`: the game
(a98873aa...) and `Reloaded.Core.dll` (6b9d4444...) match `re/binaries.lock.json` (the editor differs, large
address aware as in R4); SteeE MapsEd c78319f4...; frame cap 60. Each of the 3 games held the play lock 97 s.

## What was built

| Path | What |
|---|---|
| `tests/scenarios/traversal/*.scenario.json` | the 12 scenarios, profile `traversal` |
| `re/tools/difftest/gespots.py` | spot finder at the GEs: ledge jump grabs and walk-offs, pipes, zip lines, crawl gaps, box walls |
| `re/tools/difftest/phases.py` | a run as phases (state, Physics, timing, motion, channel 0, lock, GE) and the spread over runs |
| `re/tools/difftest/livegame.py` | traversal extras: `TRAVERSAL_PAWN_FIELDS`, `TRAVERSAL_PC_FIELDS`, `decode_root_lock`, `ge_index` |
| `re/tools/difftest/record.py` | `--traversal`, profile `traversal` turns the extras on; `leaveGE` setup step; `show` prints GE and lock |
| `re/tools/difftest/scenario.py` | profile `traversal`, setup op `leaveGE` |
| `re/tools/difftest/test_difftest.py` | 14 new tests (lock decode, GE match, extras in a sample and a frame line, phases, spot geometry) |

Shared files, additive: `tests/scenarios/README.md` and `svm.scenario-1.schema.json` (profile `traversal`,
op `leaveGE`, trace keys `tv`, `rm`, `ach`).

Local: goldens `local/difftest/golden-traversal/<id>/run{1,2,3}.jsonl.gz` with `envelope.json` and
`phases.json`; spot survey `local/difftest/spots/SteeE-ge.json`. They are not under `golden/` because
`tests/OpenScct.DiffTest` checks every golden there against locomotion rules: with these traces in it 24 tests fail
(unknown profile `traversal`; Physics outside 1..2; steps larger than velocity x dt at the grab snap, the end of a
root-motion move and the crouch height change).

## Method

- Inputs as in R4 (memory writes on the controller's input bytes, `bPressedJump`, `bFastSpeed`, view; setup
  Location, Rotation, Health). Physics never written, no hook DLL. Byte keys (`bRoll`, `bUseBackToWall`) are
  held one frame, `bCrouch` three.
- Approach by walking: each scenario teleports 70 to 150 uu short and walks or runs the last steps, so the GE
  events come from the game's own zone checks. Fast mode for everything but the walk-off: in slow mode the stick
  (2640) stays under cMinPadValue 3000 and `iMove` is 0, which the hang, pipe and back-to-wall states read.
- Ledge jumps start from a stop under the lip: with the stick released `GroundSpeed` is 0, so the jump is not
  turned into a wall jump (the Mario walk tests `GroundSpeed > 335`).
- `leaveGE` (first setup step): a recorder-side reset that drops a pawn left on a GE by the previous scenario.
  It was never needed (every scenario ends walking).
- Spots: `gespots.py` probes the editor's collision at each LG (both sides: lip floor, drop, a standing box swept
  at the wall, headroom, room on top, run-up), each PIPE (foot, lane, top exit), each ZL (floor profile under the
  line) and the walkable floor (cells where a crouched spy fits and a standing one does not). Chosen: container A
  in the yard (LG 104, 263.5 uu), PIPE 22 (1280 uu, flush top), both ZLs, the R4 east lane, the vent in the west
  wall at y -1200.

## Scenarios (all 3 of 3 runs)

States are controller states / Physics; times are TimeSeconds, the same in all runs to 0.01 s unless noted.

| Scenario | Sequence | Timings and numbers |
|---|---|---|
| spy-ledge-grab | Walk/1, s_PlayerJump/2, GrabLedge/9, OnLedge/9, s_PlayerJump/2, PlayerLanding/1, Walk/1 | jump to grab 6 frames (0.100 s), caught when Location.Z has risen 53.4; snap to the hang spot (lip - 39 horizontally, LG z - 117: -997.0) in one frame; GrabLedge 0.316 s; back: fall 0.266 s (58 uu), low landing 0.715 s |
| spy-ledge-shimmy | as above, OnLedge 3.66 s | 80.0 uu/s both ways, full speed on the first input frame and 0 on release; left is +X facing +Y; additive channels 8 LedgStAlLt0 / 5 LedgStAlRt0 |
| spy-ledge-pull-up | ..., OnLedge/9, TopLedge/5, Walk/1 | TopLedge 1.38 s, lock 3 with origin = the hang Location and yaw = pawn yaw - 16384; Location holds 9 tween frames, then follows the clip with Velocity set, yaw swinging up to +1375; the last frame is at +29.3 forward, then walking starts at hang + (57.8 forward, 205.5 up); jump to standing 2.12 s with a 0.33 s hang, so about 1.8 s without one (RE+ 1.7 to 2.0) |
| spy-ledge-drop | crouch key on the hang | same fall and landing as back (0.266 s, 0.715 s) |
| spy-walkoff-ledge-catch | Walk/1, PlayerFallToBackGrab/2, GrabLedge/9, OnLedge/9, ... | slow walk 169.4 (npm 0.40) leaves the top on frame 56; Velocity zeroed; the yaw turns 1332 units per frame (80,300/s, decoded 80000) for 25 frames while falling; grab 30 frames after leaving, snap to the hang at 500 uu/s; walk-off to hanging 0.81 s |
| spy-pipe-climb-up | Walk/1, s_GrabPipe/6, s_OnPipe/6, s_TopPipe/5, Walk/1 | grab 13 frames in; s_GrabPipe 0.649 s; climb 145.0 uu/s with no pause while held (bCanStop pulses every 31 to 35 frames); s_TopPipe 1.70 s, lock 1 after its first frame, +177.9 up, 91 across; grab to standing 9.93 s (RE+ 3.8 s for 400, 6.6 s for 800) |
| spy-pipe-climb-down | Walk/1, s_GrabPipe/6, s_OnPipe/6, Walk/1 | after release the step runs on to the next rest; down at -145.0; NotifyHitWall at the floor on frame 334 or 335 |
| spy-zip-line-13 | Walk/1, s_PlayerJump/2, s_GrabZip/7, s_ZipLine/7, s_ZipWall/7, s_PlayerJump/2, PlayerLanding/1, Walk/1 | caught 15 frames after the jump, at Vz 235 (< 254); s_GrabZip enters at 330 uu/s and eases to 201.6 (1.115 s); s_ZipLine accelerates 201.6 to 423.3 in 1.065 s, then 423.30 flat; TestZipWall fires 98 uu (horizontally) before the low end and the rider stops 46 uu before it (RE+ 31 to 61); s_ZipWall 0.848 s, fall 106 uu |
| spy-zip-line-29 | ..., s_ZipLine/7, s_PlayerJump/2, GrabLedge/9, OnLedge/9, TopLedgeCrouch/5, WalkCr/1 | same catch and speeds; no wall: the ride ends 34 to 39 uu before the low end and the spy falls 0.48 to 0.52 s onto the deck's LG 180, catches it, and forward climbs crouched (TestTopLedge 2, LedgCrAlUp0, lock 3, 1.38 s) |
| spy-roll | Walk/1, s_PlayerRollingUp/1, s_PlayerWalkingMario_T_CrToSt/1, Walk/1 | dive from 423.5: 501.7, 580.0, then 603.16 (GroundSpeed 603.2 = 385 x 4700 / 3000); bIsRolling and crouched all 73 frames (1.213 s, 730 uu); channel 15 RollStJpFdL; stand-up 0.565 s |
| spy-btw-peek | Walk/1, s_EnterBTW/1, s_BTW_up/1, s_BTW_StandLeft/1, s_BTW_up/1, s_SortieBTW/1, Walk/1 | enter 0.732 s (yaw flips in one frame); sidestep reaches 130.9 in 8 frames; peek 33 uu before the corner (BTW 3); exit 0.732 s |
| spy-crawl-vent | WalkCr/1, s_PlayerWalkingMario_T_CrToSt/1, Walk/1 | crouched 221.4 through the vent; a stand request inside it is undone in the same tick (bCrouchWithRoll 1 in every sample); outside one press stands (0.549 s) |

## Noise (3 runs from 3 games, worst pair)

| Scenario | loc RMS | loc end | loc max | vel RMS | phase start spread |
|---|---|---|---|---|---|
| spy-btw-peek | 0.41 | 0.40 | 0.79 | 3.06 | 0 frames |
| spy-crawl-vent | 0.91 | 0.45 | 11.83 | 4.27 | 0 |
| spy-ledge-drop | 0.46 | 0.00 | 2.24 | 13.38 | 0 |
| spy-ledge-grab | 0.47 | 0.06 | 2.84 | 13.24 | 0 |
| spy-ledge-pull-up | 1.15 | 0.00 | 15.26 | 11.86 | 0 |
| spy-ledge-shimmy | 0.86 | 0.13 | 5.43 | 19.97 | 0 |
| spy-pipe-climb-down | 1.34 | 0.00 | 2.31 | 8.38 | 1 |
| spy-pipe-climb-up | 1.58 | 3.69 | 11.43 | 11.59 | 1 |
| spy-roll | 2.50 | 3.69 | 14.26 | 12.67 | 0 |
| spy-walkoff-ledge-catch | 0.45 | 0.00 | 2.02 | 8.25 | 0 |
| spy-zip-line-13 | 7.40 | 12.31 | 12.58 | 13.59 | 1 |
| spy-zip-line-29 | 4.77 | 0.11 | 37.50 | 41.73 | 3 |

Most of it is the game-to-game frame timing of R4: a snap (grab, end of a root-motion move, crouch height) lands
a few ms apart on the common clock, which gives the 11 to 15 uu loc max of a one-frame step. On a zip line the
error grows along the 1550 uu ride (zip 13 ends 12 uu apart). Zip line 29's end of line is reached 5 uu apart
(y 1429.0 to 1434.2), so the fall to the deck ledge takes 29 to 31 frames: the widest envelope.

## Findings for the specs

- back-to-wall.md: the native maps the keys to `iMove` relative to the view. On the wall the spy faces the view
  (pawn yaw -16384, view +16384) and BT_Left gives iMove 2 (cMoveRight), which the BTW move turns into
  aStrafe -6000 and a sidestep toward +X, the spy's own right, ending in s_BTW_StandLeft; hanging (pawn and view
  both +Y) BT_Left gives iMove 1. The first trial pressed BT_Right and went the other way. The state logic is as
  decoded; the key mapping belongs in input-and-rotation.md (open question 3 there).
- zip-line.md: physLinear eases both ways: the grab enters at the jump's speed (330) and settles on 201.6; the
  slide reaches 423.3 after 1.07 s. A line without an end wall drops the rider 34 to 39 uu before its end
  (zip 29), not onto the deck there.
- ledge.md / native-contracts.md: the standing climb ends at hang + 57.8 forward and on the top floor (+205.5;
  C_LEDGE_UPST Z is 210.8), placed in one step after the clip; Location lags the clip root during the move. The
  crouched climb starts with a 37.5 drop of Location (the crouch height).
- jump-fall-land.md: back-ledge catch measured: Velocity zeroed, turn at 80000 units/s, gravity on, grab once the
  arms reach the ledge.
- pipe-vertical.md: with Up held the climb never stops at the rest positions (bCanStop pulses); grab to standing
  on a 1280 pipe 9.93 s.

## Deviations and limits

- Goldens under `golden-traversal/` (above). One player only. No corner change, barrier vault, waist ledge,
  pipe entry from the top, zip speed or slow pose, crouched roll, back-to-wall corner or peek fire yet.
- The `rm` current root positions are 0 while unlocked; the root lock is only meaningful when `mode` is not 0.

## Next

- `tests/OpenScct.DiffTest`: a `traversal` profile (state and channel-0 sequences within 2 frames, location limits
  that allow one-frame snaps), the locomotion checks gated by profile; then move the goldens into `golden/`.
- More scenarios: ledge corners (both kinds, LG 104 to 102), TopLedgeBarrier, pipe from the top
  (s_WaitAtTopPipe), zip speed and slow poses (aForward over 1000 and under -1000), crouched roll, merc ladder.
- Ghidra: physLinear's easing constants and its end-of-line rule; EndRootMotion's final placement.
