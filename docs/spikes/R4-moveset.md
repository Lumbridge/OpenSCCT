# R4 spike: golden traces for the rest of the moveset (SteeE, MounE)

**Result: done.** Nineteen scenarios for the moves R4 and R4-traversal did not cover were recorded three times each,
every run in its own headless sandbox Play Level game (57 runs, 12 games). Every run reaches its move, and the three
runs of each scenario go through the same pawn states, controller states and Physics modes. Ladders (both teams, up
and down), hand over hand, the fence climb over the top, the wall jump, the split jump into the Vandamme brace, the
barrier vault, drops off a walkway (both teams), the 26.9 degree stairwell ramp (spy running, merc crouched and
jumping), a 17.1 uu curb (both teams) and a 50 uu step (both teams) are in. The measured speeds and offsets match the
decoded values (ladder 126.0, fence 120.0, hand over hand 126.0, split push 250 and 700, Vandamme placement 87 to the
midpoint, top-out offsets 73.9 / 68.8, down-from-top 72 / -192, fence crossing 75 / -3.25).

Recorded 2026-10-23 from Enhanced SCCT Versus v4.0 Public Beta 3 in `F:\SCCT-Automation\svm\Sandbox`: the game
(a98873aa...) and `Reloaded.Core.dll` (6b9d4444...) match `re/binaries.lock.json` (the editor differs, large address
aware as in R4); SteeE MapsEd c78319f4..., MounE MapsEd 033c9972...; frame cap 60. The play lock was held 30 to 39 s
per merc game and 42 to 52 s per spy game, 8.2 minutes in all.

Co-op moves (short ladder boost, hoist) need two players and were skipped; the recorder drives one local player.

## What was built

| Path | What |
|---|---|
| `tests/scenarios/moveset/*.scenario.json` | the 19 scenarios (profile `traversal` for GE moves and jumps, `locomotion` for walking, ramps, steps and drops) |
| `re/tools/difftest/movespots.py` | spot finder for the moveset on any map: ladders, hand-over-hand pipes, fences (face texture), barrier vaults (CheckBarrier room box), wall jumps (window, kick, CheckGEForWallJump traces), split jump corridors, steps and stairs, slopes, drops; builds and caches a floor map |
| `re/tools/difftest/test_difftest.py` | 4 new tests (steps and stairs, slope gradient, wall jump window and kick, the tracked moveset scenarios: ids, leaveGE, repeats, covers in the inventory) |
| `re/tools/difftest/README.md` | movespots.py, the moveset commands and golden root |

Local: goldens `local/difftest/golden-moveset/<id>/run{1,2,3}.jsonl.gz` with `envelope.json`; surveys
`local/difftest/spots/{SteeE,MounE,FactD,StatD,DeftD}-moves.json` (and the floor maps beside them). The goldens are not
under `golden/` for the reason in R4-traversal (locomotion invariants in GoldenTraceTests); RunnerTests reads only
`golden/` and `golden-traversal/`, so the sim is not compared with these yet.

## Maps and spots

SteeE has no ladder, fence or hand-over-hand GE (C3 census), so those are on MounE, the stock map with all three
(14 ladders, 11 fences, 13 HOH). Everything SteeE has stays on SteeE. Spots were chosen with `movespots.py` and
checked with the editor's live traces before the first trial.

| Move | Map, spot |
|---|---|
| Wall jump | SteeE yard wall under the deck railing, LG 205 409.3 over the floor (gz - Location.Z 320.8, kick up), BSP wall, 210 uu run-up |
| Barrier vault | SteeE LG 12, a yard wall 95.5 high and 32 thick, yard floor both sides |
| Split jump | SteeE pipe shaft at PIPE 21: BSP walls 191 apart (y -1823.5, -1632.5) to 500 over the floor |
| Drops | SteeE walkway z -746.3, edge to the yard 381 below, no LG at the edge |
| Ramp (stairs) | SteeE east stairwell, lowest flight: a collision plane, normal z 0.89216 (26.85 degrees), 320 up over 672 |
| Curb | SteeE yard, x -2047.7: a 1.1 gutter then a 17.1 curb |
| Ladder | MounE LADDER 149 (320 high, foot strip z -627, top floor z -319 flush) |
| Hand over hand | MounE HOH 12, 277 over a flat roof z 641 |
| Fence | MounE FENCE 225 (Prison_TEX CIA_chainfence), 377.4 over the floor z 160.5 on both sides |
| 50 uu step | MounE ladder room: a 50 block in the pit z -723.5, LG 502 on its lip |

Real stairs with risers were not found: SteeE, MounE, FactD, StatD and DeftD stairs all have ramp collision (the
stair detector found no run of risers), so "stairs" are the SteeE ramp and "steps of known height" are the 17.1 curb
and the 50 block. StatD's 32 and 33.5 steps are in its rail trenches (trains) and FactD's 31.6 platform is curved, so
neither was used.

## Scenarios (all 3 of 3 runs)

States are controller states / Physics; times are TimeSeconds, the same in all runs to 0.02 s unless noted.

| Scenario | Sequence | Timings and numbers |
|---|---|---|
| spy-wall-jump | Walk/1, s_FirstWallJumpPrepareFromJump/1, s_FirstWallJump/5, s_WallJumpSalto/5, GrabLedge/9, OnLedge/9, s_PlayerJump/2, PlayerLanding/1, Walk/1 | jump 158 from the wall at 423.5; the run-up runs at 385.0 for 10 frames to 100.5 from it; WljpStAlBgR 0.266 s (lock 1), WljpStAlUpR 0.40 to 0.42 s rising 170.5 (lock 1); GrabLedge on LG 205, hang at lip - 39, LG z - 117 (-835.2); back drops 202.7 |
| spy-vault-barrier | Walk/1, s_BarrierJumpDetect/9 (1 frame), s_BarrierJump/5, s_PlayerJump/2, PlayerLanding/1, Walk/1 | jump standing against the wall; JumpStAlBr0 0.68 s (lock 3) moves 142.9 forward, 18.5 aside and 9.9 up (C_BARRIER_LEDGE 140, 20, 12.8), then a 10 uu fall (4 frames) and a low landing 0.715 s |
| spy-split-jump | s_PlayerJump/2, s_PreSplitJump/0, s_SplitJump/2, VandammeBegin/10, Vandamme/10, s_PlayerJump/2, PlayerLanding/1, Walk/1 | second jump at the apex (Vz 11.9); SpltStAlRt0 holds 0.183 s, then Velocity (0, -250, 675) (250 vWallJumpDir, 700 less one frame of gravity); brace 0.465 s later 111 across and 152 up; physVandamme slides 57 to the shaft midpoint y -1728.0 at 85.8 (87) in 0.68 s; crouch drops 287 unhurt |
| spy-run-off-ledge | Walk/1, PlayerFalling/2, Walk/1 | leaves the edge 32 past it at 423.5; in the air 417.0 (the cap); 0.716 to 0.730 s, 297.7 forward, 380.7 down; lands straight into walking (no PlayerLanding with forward held) |
| merc-walk-off-ledge | Walk/1, PlayerFalling/2, Walk/1 | leaves at 346.5, air speed grows to 383 (air control); 0.715 s, 266.6 forward, 381.6 down, no damage, straight into walking |
| spy-ramp-up-down | Walk/1 | 423.5 across the ground both ways (475 along the 26.85 degree plane), climbing 214.4 uu/s; Physics never leaves walking on the way down; the turn at the top stops the pawn at once (Velocity and GroundSpeed 0) and turns it 2662 units a frame for 13 frames, then it accelerates again |
| merc-crouch-stairs | Walk/1 | crouched 116.91 across the ground up the whole flight (770.7 across, 320 up) |
| merc-jump-stairs | Walk/1, s_PlayerJump/2, Walk/1 | 346.5 up the ramp; the jump lasts 0.399 s, 150.5 forward and lands 71.3 higher on the ramp |
| spy-step-curb, merc-step-curb | Walk/1 | the 17.1 curb is climbed in one frame (z +17.6, then +1.4 the next), and walked down in one frame (-19.0) without falling; the 1.1 gutter is followed |
| spy-step-blocked, merc-step-blocked | Walk/1 | the 50 step stops both (spy slow at 169.4, merc at 346.5) at 34.5 from its face |
| spy-run-at-step | Walk/1 | at 423.5 into the same step the auto hop (LG 502, 49 above the feet) is refused: the block is 68 deep and the 46.5 strip behind it blocks the AutoFeetLedge room box (112.4 over the top); stops at 34.5 |
| spy-ladder-up, merc-ladder-up | Walk/1, s_GrabLadder/13, s_OnLadder/13, s_LadderToTopTr/13, s_LadderToTop/13, s_OnLadderTop/13 (1 frame), s_LadderTop/5, Walk/1 | grab 4.4 from the grab spot, pulled to the ladder line + 37.5 and snapped to the rung zb + 28 + 64; s_GrabLadder 0.549 s; climb 126.0; TopLadder 2 rungs up; LaddStAlEuR / EuL 0.55 s; top-out LaddStAlEu0 / Eu2 (lock 1) spy 1.04 s, merc 1.56 s, 73.9 / 68.8 forward, 178.1 up onto the floor; grab to walking spy 3.2 s, merc 3.7 s |
| spy-ladder-down | Walk/1, s_WaitAtTopLadder/11, s_downTopLadder/5, s_OnLadderTop/13, s_Back_LadderTop/13, s_OnLadder/13, Walk/1 | TopLadder by walking to the edge (no Use); repositioning 0.37 to 0.40 s; LaddStAlBu0 1.38 s (lock 3) 72 across, 192 down; back: LaddStAlEuR 0.55 s; down at 126.0; NotifyHitWall at the floor straight to walking |
| merc-ladder-down | Walk/1, s_WaitAtTopLadder/11, s_downTopLadder/5, s_LadderGrabFall/13, s_OnLadderTop/13, s_Back_LadderTop/13, s_OnLadder/13, s_EndLadderDrawWeapon/5, Walk/1 | repositioning 0.18 to 0.25 s; LaddStSpBu2 0.65 s (lock 3); without Down held s_LadderGrabFall (LaddStAlCt0 0.85 s, near the top: s_OnLadderTop); at the floor LaddStEdIOR 0.58 s (lock 3), 35 back |
| spy-hoh | Walk/1, s_PlayerJump/2, s_GrabPipH/14, s_OnPipeH/14, s_PlayerJump/2, PlayerLanding/1, Walk/1 | caught 3 frames after a standing jump (rising at 535); hang Location = pipe - 128; PipHstAlBg0 1.17 s; with forward held 126.0 uu/s without a pause (2.10 a frame); crouch drops 60 |
| spy-fence-climb | Walk/1, GrabFence/8, OnFence/8, OnTopFence/5, OnFence/8, Walk/1 | NotifyHitWall at the fence (CheckFence 1); FencStAlIO0 0.93 s; climb 120.0 until Location.Z is 88.4 under the fence GE (FenceTop); FencStAlCo0 2.36 s (lock 3) 76 across, 3.2 down (C_TOPFENCE 75, -3.25); down the far side at 120.0; MyNotifyLanded straight to walking |

## Noise (3 runs from 3 games, worst pair)

| Scenario | loc RMS | loc end | loc max | vel RMS | phase start spread |
|---|---|---|---|---|---|
| merc-crouch-stairs | 1.22 | 4.08 | 4.08 | 7.90 | 0 frames |
| merc-jump-stairs | 12.79 | 7.18 | 20.04 | 36.99 | 0 |
| merc-ladder-down | 9.73 | 5.44 | 55.55 | 82.33 | 4 |
| merc-ladder-up | 1.04 | 0.28 | 4.79 | 10.43 | 1 |
| merc-step-blocked | 0.48 | 0.49 | 1.58 | 13.16 | 0 |
| merc-step-curb | 0.45 | 0.23 | 1.74 | 4.18 | 0 |
| merc-walk-off-ledge | 1.02 | 1.30 | 2.64 | 14.60 | 0 |
| spy-fence-climb | 2.29 | 0.01 | 10.97 | 25.69 | 3 |
| spy-hoh | 1.97 | 2.75 | 4.77 | 14.64 | 2 |
| spy-ladder-down | 3.74 | 0.16 | 10.33 | 49.08 | 3 |
| spy-ladder-up | 1.42 | 0.03 | 6.37 | 12.21 | 1 |
| spy-ramp-up-down | 4.17 | 0.06 | 6.96 | 23.58 | 0 |
| spy-run-at-step | 0.25 | 0.28 | 0.28 | 1.35 | 0 |
| spy-run-off-ledge | 4.13 | 5.69 | 5.69 | 67.60 | 1 |
| spy-split-jump | 1.64 | 0.00 | 7.75 | 35.03 | 0 |
| spy-step-blocked | 0.28 | 0.11 | 1.93 | 12.12 | 0 |
| spy-step-curb | 1.72 | 0.09 | 7.53 | 15.08 | 0 |
| spy-vault-barrier | 0.53 | 0.45 | 1.74 | 13.03 | 0 |
| spy-wall-jump | 1.39 | 0.14 | 6.60 | 39.23 | 1 |

Sampling: 8 torn reads (each a skipped frame, marked `gap`) and 8 late writes in 57 runs, none on a frame where an
input changed; no unstable sample; 59.7 to 60.7 fps. The machine was busy with other work during the recording,
which R4's 0 torn did not have. The widest envelopes are the ladder top (the repositioning of s_WaitAtTopLadder takes
0.18 to 0.25 s for the merc, moving every later phase by up to 4 frames, and a one-frame step of 192 uu root-motion
placement then gives the 55 uu loc max) and the merc jump on the ramp (where it lands on the slope).

## Findings for the specs

- ladder.md: TopLadder fires from walking to the top edge, with no Use (the RE+ note says Use), for both teams, as the
  decoded CheckLadderFeet says. The ladder Up and Down are the raw keys (iMove 16 and 32 whatever the view; 4 and 8 are
  view-relative): with the view turned away from the ladder, back walks the spy into it on the floor and NarrowLadder
  grabs it again (a first trial). Rungs: the grab snaps to zb + 28 + 64 (-537.1), TopLadder two rungs up. Grab to
  standing 3.2 s (spy), 3.7 s (merc) for 320; adding 280 at 126 gives RE+'s 5.4 / 5.9 s for 600.
- pipe-horizontal.md open question 1: hand over hand moves at 126.0, the request, with no stop and go; RE+'s 105 is
  not what the game does on HOH 12 with forward held.
- wall-jump-split-vandamme.md: the brace needs the pawn within R + 3 = 38 of the second wall, and the falling wall
  pre-probe (physics-falling.md 3) stops a pawn short of a wall by up to one frame step (4.17 at 250 uu/s), so the same
  191 uu shaft braces or not depending on where the push starts: a start 36.5 from the first wall stopped 38.2 from the
  second (no brace), 37.5 from it braces in every run (37.05). The split push is exactly (250 vWallJumpDir, 700), one
  frame of gravity seen at the first sample.
- barrier-and-low-ledges.md: vault root motion measured 142.9 forward, 18.5 aside, 9.9 up; AutoFeetLedge is refused
  when the 112.4 room box over the top is blocked, even at full speed and with the ledge 49 above the feet.
- fence.md: FenceTop fires with Location.Z 88.4 under the fence GE; the crossing is a 2.36 s lock 3 move of 76 across
  and 3.2 down; held forward on the far side climbs back over (first trial), as RE+ noted.
- walking.md: on a 26.85 degree plane the ground speed is the horizontal speed (423.5, so 475 along the plane); a 17.1
  step is climbed in one frame and walked down in one frame without falling; a 50 step stops both teams 34.5 from its
  face. jump-fall-land.md: off a 381 edge with forward held neither team enters PlayerLanding.

## Deviations and limits

- One player only: co-op moves skipped. No ladder slide (Use + Down), side change, catch while falling, pipe-to-HOH,
  HOH legs-over stance or fire, wall jump salto, waist or feet ledge, Vandamme weapon, fence from a jump yet.
- The 50 step auto hop could not be shown hopping (no spot with room on SteeE or the MounE ladder room); the scenario
  records the refusal instead.
- Map-specific content was not touched; the StatD trench steps were avoided because trains run there.

## Next

- `tests/OpenScct.DiffTest`: read `golden-moveset/` in SimGoldens (and the traversal profile there), then compare the ported
  ladder, fence, HOH, wall jump, split jump and vault states once they exist (Movement README: not ported yet).
- More scenarios: ladder slide and side change, ladder catch from a jump, fence from a jump, waist and feet ledges,
  wall jump salto (gz outside the window), HOH stance 4 and turn round.
- Ghidra: s_WaitAtTopLadder's repositioning time (0.18 to 0.40 s), the 3.6 uu the falling pre-probe leaves.
