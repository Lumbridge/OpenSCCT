# R4 spike: the rewrite against the original (sim runner)

**Result: done, with findings for the next wave.** `PawnRigSim` plays every recorded SteeE scenario (10
locomotion, 12 traversal) in the rewrite: OpenScct.Content loads `.content/svmap/SteeE`, `PawnRig.Create` builds the
level with `PhysicsWorld.Attach`, the pawn starts at the reference run's frame -1 and gets the recorded inputs frame
by frame. In dt-replay mode 9 of the 10 locomotion scenarios match the original to float precision (location RMS
under 0.002 uu, end error under 0.001 uu, velocity RMS under 0.01 uu/s, the same controller and Physics sequence on
the same frames; plateau speed, time to 90 % and to full speed and time to stop exact to the frame, jump apex within
0.003 uu, gravity fit within 0.03 uu/s^2). The tenth (the 790 uu walk-off) matches until the landing, then lands on
a collider the original falls past (F1). One traversal scenario passes (ledge
pull-up); the other eleven fail on eight causes in the physics, the pawn port and the converted collision, none in
the runner (F1 to F10 below). The fixed 60 Hz mode passes 8 of 10 locomotion scenarios; its two failures come from
the 60 Hz grid against the original's 60.35 fps (F11).

Run 2026-10-23 on the working tree of that day, goldens from `local/difftest/golden` (R4) and
`local/difftest/golden-traversal` (R4 traversal). All 44 runs (22 scenarios, two timings) take 17 s.

## What was built

| Path | What |
|---|---|
| `tests/OpenScct.DiffTest/Sim/PawnRigSim.cs` | `IScenarioSim` on `PawnRig`: start (`SimStart.Recorded` or `Setup`), clock warm-up, per-frame inputs in `DtReplay` or `Fixed`, missed recorder frames filled, svm.trace/1 out |
| `tests/OpenScct.DiffTest/Sim/SimTrace.cs` | rig state to `TraceFrame` (every svm.trace/1 frame key), header, footer; UE2 names written with the reference's spelling |
| `tests/OpenScct.DiffTest/Sim/SimContent.cs` | the converted content (`SVM_CONTENT` or `.content`): maps and the clip table, loaded once |
| `tests/OpenScct.DiffTest/Sim/SimHarness.cs` | runs a scenario against its goldens (both golden roots), writes runs to `local/difftest/sim`; `SimProfiles` with a provisional `traversal` profile |
| `tests/OpenScct.DiffTest/Sim/SimReport.cs` | per scenario: profile verdicts, measurements on both runs with the golden spread, phase sequence and start offsets, channel-0 clip sequence, start error, first divergence; markdown and JSON |
| `tests/OpenScct.DiffTest/RunnerTests.cs` | the harness as tests (skipped without goldens or content), a known-mismatch table, a determinism check, and two synthetic-floor tests that run in CI |

Shared file, additive: `tests/OpenScct.DiffTest/OpenScct.DiffTest.csproj` references OpenScct.Game, OpenScct.Physics and OpenScct.Content.

## Method

- **Start.** `Recorded` (the gate): `PawnRig.Place` at frame -1 (Location, Rotation, controller Rotation, stance),
  then 180 idle frames on the spot, then `Place` again with Health, `bFastSpeed`, `fZMaxFalling` and
  `bCrouchWithRoll` from frame -1. When the recorder's setup pressed crouch, `m_LastSpecialMoveTime` is set to
  (still frames + settle frames) x dt before frame -1, where the press happened. `Setup`: the scenario's own steps
  (teleport, stance, speed mode, Health, view, waitStill, settle), after putting the pawn in the stance the
  original had before its setup (from the header's press count) and the same warm-up.
- **Inputs.** `FrameInput` to `PlayerInputFrame.FromButtons`: the input bytes, the Jump exec, the view write and
  the speed-mode write, applied before the frame's tick, as the recorder wrote them.
- **Timing.** `DtReplay`: frame k is stepped by the reference frame k's `m_dT` with that frame's recorded inputs.
  `Fixed`: 1/60 s per frame with the scenario timeline.
- **Compare.** `Comparator` with the scenario's profile and the noise envelope; `Locomotion.MeasureAll` on the
  reference and the sim run; phases (controller state and Physics) with their first frame; the channel-0 clips.
  Runs, `report.md` and `report.json` go to `local/difftest/sim`.

### Runner causes found and fixed

- A fresh level's clock is 0 where the original reads 8 to 95 s, and a cold rig's first frames differ: without the
  warm-up the crouched scenarios keep a 0.09 uu location RMS.
- The rig spawns the spy crouched; the original's pawn comes standing from its previous scenario (0 presses in
  the header). With the setup start the rig pressed crouch to stand, which stamps `m_LastSpecialMoveTime`: the zip
  grab's 2 s cooldown then blocked the catch on zip line 13, and zip line 29 ended its setup crouched, 39 uu low.
  The setup start now begins in the original's stance, so its frame -1 matches the recorded one in every scenario
  except the pipe spot (F9).
- The first-divergence search matched the sim frame before the reference time (the two clocks differ by float
  rounding) and reported states one frame early; it now takes the nearest frame.

## Results

Location and velocity: value / tolerance (max of the profile and 1.5 x the golden envelope). Phases: same
sequence, and the worst start offset beyond the golden spread in frames. Errors are sim minus reference.

| Scenario | Timing | Verdict | loc RMS | loc end | vel RMS | phases | plateau | to 90 % | to stop | apex |
|---|---|---|---|---|---|---|---|---|---|---|
| merc-crouch-walk | dt | pass | 0.00 / 2 | 0.00 / 10 | 0.00 / 10 | same | 0 | 0 | 0 | |
| merc-jump | dt | pass | 0.00 / 2 | 0.00 / 10 | 0.00 / 10 | same | | | | -0.002 |
| merc-walk | dt | pass | 0.00 / 2 | 0.00 / 10 | 0.00 / 10 | same | 0 | 0 | 0 | |
| spy-crouch-walk | dt | pass | 0.00 / 2 | 0.00 / 10 | 0.00 / 10 | same | 0 | 0 | 0 | |
| spy-fall-ledge | dt | FAIL F1 | 19.57 / 2 | 27.75 / 10 | 36.28 / 21.83 | same, +7 | 0 | | | |
| spy-jump-forward | dt | pass | 0.00 / 31.69 | 0.00 / 47.13 | 0.00 / 77.23 | same | 0 | | | +0.001 |
| spy-jump-in-place | dt | pass | 0.00 / 2 | 0.00 / 10 | 0.01 / 10 | same | | | | 0 |
| spy-run-forward | dt | pass | 0.00 / 2 | 0.00 / 10 | 0.00 / 10 | same | 0 | 0 | 0 | |
| spy-run-stop | dt | pass | 0.00 / 2 | 0.00 / 10 | 0.00 / 10 | same | 0 | 0 | 0 | |
| spy-sneak-walk | dt | pass | 0.00 / 2 | 0.00 / 10 | 0.00 / 10 | same | 0 | 0 | 0 | |
| merc-crouch-walk | fixed | pass | 0.17 / 2 | 0.33 / 10 | 1.33 / 10 | same | -0.002 | 0 | 0 | |
| merc-jump | fixed | pass | 0.08 / 2 | 0.00 / 10 | 3.19 / 10 | same, +2 | | | | -0.002 |
| merc-walk | fixed | pass | 0.55 / 2 | 1.10 / 10 | 4.25 / 10 | same | +0.002 | 0 | 0 | |
| spy-crouch-walk | fixed | pass | 0.52 / 2 | 1.05 / 10 | 4.08 / 10 | same | 0 | 0 | 0 | |
| spy-fall-ledge | fixed | FAIL F11 | 4.31 / 2 | 5.83 / 10 | 23.87 / 21.83 | same, +2 | 0 | | | |
| spy-jump-forward | fixed | pass | 5.14 / 31.69 | 8.13 / 47.13 | 44.65 / 77.23 | same, +1 | +0.001 | | | +0.004 |
| spy-jump-in-place | fixed | FAIL F11 | 0.24 / 2 | 0.00 / 10 | 44.10 / 10 | same, +2 | | | | +0.007 |
| spy-run-forward | fixed | pass | 1.00 / 2 | 2.00 / 10 | 7.76 / 10 | same | +0.001 | 0 | 0 | |
| spy-run-stop | fixed | pass | 0.80 / 2 | 1.06 / 10 | 6.76 / 10 | same | +0.001 | 0 | 0 | |
| spy-sneak-walk | fixed | pass | 0.40 / 2 | 0.79 / 10 | 3.07 / 10 | same | +0.002 | 0 | 0 | |

Fall-ledge, dt-replay: fall height 790.61 against 790.72, fall damage 21 in both, landing hold 1.348 s in both;
air time 1.148 s against 1.032 s (F1).

Traversal (provisional profile: location RMS 5, end 10, velocity RMS 30, same sequences, phases within 2 frames):

| Scenario | dt-replay | fixed | loc RMS (dt) | phases (dt) | Cause |
|---|---|---|---|---|---|
| spy-ledge-pull-up | pass | pass | 1.84 / 5 | same, +1 | (F10) |
| spy-ledge-grab | FAIL | FAIL | 63.21 / 5 | same, +38 | F2 |
| spy-ledge-drop | FAIL | FAIL | 62.19 / 5 | same, +38 | F2 |
| spy-ledge-shimmy | FAIL | FAIL | 54.76 / 5 | same, +38 | F2 |
| spy-walkoff-ledge-catch | FAIL | FAIL | 53.04 / 5 | same, +38 | F2 |
| spy-crawl-vent | FAIL | FAIL | 0.00 / 5 | same, +7 | F3 |
| spy-roll | FAIL | FAIL | 1.99 / 5 | same, +6 | F3, F4 |
| spy-btw-peek | FAIL | FAIL | 50.50 / 5 | differ | F5, F6 |
| spy-pipe-climb-up | FAIL | FAIL | 677.23 / 5 | differ | F7, F9 |
| spy-pipe-climb-down | FAIL | FAIL | 328.10 / 5 | differ | F7, F9 |
| spy-zip-line-13 | FAIL | FAIL | 142.70 / 11.10 | same, +29 | F8 |
| spy-zip-line-29 | FAIL | FAIL | 115.62 / 7.15 | same, +24 | F8 |

Up to the frame named in each finding the traversal runs match the original as closely as the locomotion ones.

## Findings for the next wave

Frames are dt-replay frames of the reference run (the medoid: run3 for every traversal scenario, run1 for
fall-ledge). "Original" is the golden, "sim" the rewrite.

**F1. Converted collision: a static mesh the original falls past (content or physics).** fall-ledge frame 177. The
falling spy at (-458.57, 1008.18, -1034.80), Velocity (113.6, 274.2, -1522.5), box 35 x 35 x 87.5. Original:
lands on frame 177 at (-457.19, 1011.53, -1052.82), on collider 1699 (mesh floor, top -1142.01). Sim: the move
hits collider 1711 at t 0.071, normal (0, 0, 1), stops at z -1036.08, slides off at (-204.7, 13.7) uu/s and lands
on frame 184, 27.7 uu away. Collider 1711 is the simple set (18 triangles, kind meshHull) of a level static mesh at
(-373.80, 1126.98, -1131.90), yaw 30720, top -1124.56; its per-poly twin is 1710 (104 triangles); the same mesh
stands at 1712/1713 and 1714/1715. The box's corner overlaps the simple set's corner by about 2.4 uu in X and 5 uu
in Y (box X max -423.57; the set reaches X -426 at Y 1038 to 1043; the per-poly mesh only reaches X -422).
Experiment: without 1711 (per-poly 1710 used) the landing is on time but grazes 1710 at the end of the move (6.4
uu off, loc RMS 4.58); without both the scenario passes (loc RMS 0.46, end 0.64). So the original's collision of
this mesh is smaller than either converted set there. Check with the editor's live `trace.box` along the frame
176 to 177 move, then the converter's placement of this mesh (PrePivot, scale, rotation sense) and its simple set.

**F2. Physics, MyDoJump from the hang: the drop jumps (spec native-contracts.md marks it inferred).** ledge-drop
frame 120 (crouch key on the hang, OnLedge crouch latch: `MyDoJump`, SA_LEDGEFALL, s_PlayerJump). Original:
Velocity (0, 0, 0) then gravity: Vz -25.0 after the first frame, Location.Z -997.21; lands after 16 frames (0.266
s, 58 uu). Sim: Velocity.Z = JumpZ 610 (585.0 after the first frame), Z -987.05, rises about 124 uu and lands after
54 frames. Same at ledge-grab 200 (back key), shimmy 300 and walkoff-ledge-catch 220. Likely rule: MyDoJump sets
JumpZ only from walking; from a GE mode (PHYS 9) it switches to falling with zero velocity. The zip line drop is
right already (s_ZipWall to s_PlayerJump: Vz -25.0 on its first frame in both). Ghidra 0x10ae00b0.

**F3. Pawn port, stand-up clip tween.** crawl-vent frame 110, roll frame 133 (`SA_WAIT_CR2ST_0H`, PawnActions uses
`DefaultTween` 0.1 s). Original: WaitCrAlEd0 starts at frame 0 with no tween (0.000, 0.028, 0.056 per frame), and
s_PlayerWalkingMario_T_CrToSt lasts 33 frames (0.549 s). Sim: -0.111 (= -1/9, a 0.1 s tween over 6 frames), the
state lasts 40 frames. Rule: no tween on the crouched-to-standing clip (at least unarmed; the armed one is not
recorded).

**F4. Pawn port or physics, the frame the roll ends.** roll frame 133. Original: the first frame of
s_PlayerWalkingMario_T_CrToSt still moves at the roll's 603.17 (GroundSpeed 603.17 in that sample), 423.5 from frame
134. Sim: GroundSpeed and speed 423.5 on frame 133, so the run stays 2.99 uu behind. Likely rule: the roll ends on
an animation end after that tick's physics, so its GroundSpeed applies from the next tick.

**F5. Pawn port, input native: strafe keys relative to the view (spec input-and-rotation.md open question 3).**
btw-peek frame 110 on, BT_Left held, pawn yaw -16384, view yaw 16384. Original: iMove 2 (cMoveRight), aStrafe
-6000, the spy steps toward +X and ends in s_BTW_StandLeft (frame 129). Sim: iMove 1, aStrafe +6000 (ControlInput
maps BT_Left to cMoveLeft whatever the facing), steps toward -X, s_BTW_StandRight (frame 127), peek clip
BackStPkNtR instead of BackStPkNtL. Rule (R4 traversal spike): NativeComputeControlVariables swaps left and right
when the pawn faces the view; hanging with both facing +Y BT_Left gives iMove 1.

**F6. Pawn port, back-to-wall entry clip.** btw-peek frame 50. Original: BackStAlBg0 forward, -0.061 on the first
frame (a tween of about 0.05 s, one step already taken), s_EnterBTW 44 frames. Sim: BackStAlEd0 backward from 0.909
(TraversalActions, the clip "taken from the names"), s_EnterBTW 41 frames.

**F7. Physics, vertical pipe grab overshoots (with issue 1 of the movement port).** pipe-climb-up frame 13 (both
pipe scenarios). Original: s_GrabPipe snaps to (-1918.22, 1412.16) then (-1918.40, 1419.00) on frame 14 and holds
there with Velocity 0 and yaw 16272 until the climb (s_OnPipe, frame 52, 145 uu/s straight up). Sim: Velocity (0,
500, 0) on frame 13, through 1420.48 and 1428.79 to Y 1456 (37 uu past the hold point) while the yaw turns 272
units a frame to 0; on s_OnPipe it climbs away from the pipe (frame 100 at (-1751.6, 1329.7, -956.7)), reaches
s_TopPipe on frame 103 instead of 508 and falls. Likely rules: the grab snap ends at the hold point with Velocity
0; NewZipP is set by the grab so physOnPipe does not start a swap (movement report issue 1).

**F8. Physics, zip line grab (physLinear): the hang snap goes through Velocity.** zip-line-13 frame 27 (both zip
scenarios). Original: Velocity becomes the speed along the line, (89.1, -318.0, 0), 330.2 uu/s easing to 201.57;
Velocity.Z stays 0 while Location drops 8.20 uu a frame for 6 frames (about 493 uu/s, frames 27 to 32) to the hang
height, then follows the line; s_ZipLine from frame 94. Sim: Velocity (66.4, -237.8, -493.0), then Vz -899.9 and
-1075.7, the rider overshoots below the line and bounces for about 60 frames; speed 172 against 201.7 at frame 87,
s_ZipLine from 95, 423.3 reached 12 frames later, the ride ends at frame 331 against 302. Rules: on the grab,
Velocity = the horizontal speed projected on the line with Z 0; the drop to the hang height is a Location
interpolation at about 500 uu/s outside Velocity.

**F9. Physics, walking rest height and the box sweep.** pipe-climb frame 0. A box swept down onto a flat BSP floor
stops 0.4 uu below contact (lane floor -1143.5: contact -1056.0, stop -1056.4; pipe base -1127.5: contact -1040.0,
stop -1040.4); zero-extent traces hit the floor exactly. The original keeps the pawn where its settle left it
inside a band: 1.005 above contact on the lane, 1.905 at the pipe base (same teleport 2.0 above), 1.77 on mesh 1699
after the fall. The sim rests at 1.005 above BSP contact and about 2.4 above mesh contact, and a pawn placed 1.9
above BSP contact drops 2.3 on its first walking frame (to the box stop) and settles at 1.005 one frame later. The
setup start reproduces frame -1 exactly everywhere except the pipe base (0.9 uu low).

**F10. Pawn port, clips started by a physics event show one step of play (within the profile).** GrabLedge frame
61: original LedgStAlBg0 at -0.099, sim -0.125 (= -1/8), so OnLedge starts one frame late (80 against 81) in every
ledge scenario; s_GrabZip frame 27: -0.052 against -0.062. Clips started by the controller (jump, landing) match.
Likely order: the pawn's animation advances after physics in the original's tick. Also: the hang yaw is 16380 in
the original and 16384 in the sim.

**F11. Fixed 60 Hz grid against the original's 60.35 fps (not a sim error).** jump-in-place: the fall crosses the
floor between two original frames, so the original lands on frame 79 (1.3146 s) and the sim on frame 78 (1.300 s);
the landing clip's 0.1 s tween ends on its fifth frame at 1/60 and needs six at 16.64 ms; PlayerLanding ends three
frames early in time and the 600 uu/s landing step gives velocity RMS 44.1. fall-ledge: same landing frame and
damage, 5.8 uu apart at the end. Every walk and stop matches to 2 uu in fixed mode. A fixed-mode gate needs goldens
recorded at a 60.00 cap or a time-shift tolerant velocity metric.

## Deviations and limits

- Traces carry channel 0 only, alpha 1 (the movement port keeps no blend weights); the traversal extras (`tv`,
  `rm`, `ach`) are not compared.
- The traversal profile lives in `SimProfiles` until the comparator has one; `DiffHarness` (locomotion only) is
  unchanged and `SimHarness` adds the traversal profile, the second golden root and the report.
- Known mismatches are listed in `RunnerTests.KnownMismatches` with their finding: they report as skipped while they
  fail and pass once fixed; every other scenario and timing must pass.
- The fall-ledge experiment (F1) used a modified copy of the collision in a scratch tool, not a test.

## Next

- Physics: F2 (MyDoJump from GE modes), F7 (pipe grab), F8 (zip grab), F9 (box sweep stop and rest band), F1 with
  the collision oracle.
- Pawn port: F3 (stand-up tween), F5 (strafe relative to the view), F6 (BTW entry clip), F4 and F10 (animation
  order in the tick).
- Comparator: a traversal profile with phase timing, a dt-replay noise measure, the fixed-mode velocity metric.
- Take each pair off the known mismatches as its finding is fixed; record armed stand-up, pipe drop, corner and
  ladder scenarios.

## Fidelity pass: F1 to F10 closed

Run later on 2026-10-23 on that day's working tree, same goldens. Each rule was confirmed in the native code (or in
the editor's live collision queries) before the port changed; the rules and the spec corrections are in
spec/native/ANSWERS-fidelity.md.

**Result: 21 of 22 scenarios pass in dt-replay** (10 of 10 locomotion, 11 of 12 traversal), 17 of 22 in the fixed
60 Hz mode. The runner reports 49 tests: 43 pass, 6 skipped as known mismatches, 0 fail. The setup start now
matches frame -1 in every scenario.

| Scenario | dt loc RMS before | dt loc RMS after | dt | fixed | Fixed by |
|---|---|---|---|---|---|
| spy-fall-ledge | 19.57 | 0.46 | pass | FAIL F11 | F1: static mesh box sweeps move a vertical cylinder |
| spy-ledge-grab | 63.21 | 0.00 | pass | pass | F2: decoded MyDoJump; F10 |
| spy-ledge-drop | 62.19 | 0.00 | pass | pass | F2, F10 |
| spy-ledge-shimmy | 54.76 | 0.00 | pass | pass | F2, F10 |
| spy-walkoff-ledge-catch | 53.04 | 0.00 | pass | pass | F2, F10 |
| spy-ledge-pull-up | 1.84 | 0.01 | pass | pass | F10: animation advanced in the pawn's tick, current dt |
| spy-crawl-vent | 0.00 (phases +7) | 0.00 (phases 0) | pass | pass | F3: stance transitions with no tween, after AnimateWalking |
| spy-roll | 1.99 | 0.00 | pass | pass | F3, F4: root-motion clip clamped at its end, ends next tick |
| spy-btw-peek | 50.50 | 0.00 | pass | pass | F5: decoded iMove bits; F6: decoded CheckBTW, CheckBTWEyes, CheckBTWCorner |
| spy-pipe-climb-up | 677.23 | 0.84 | pass | FAIL F11 | F7, F9: hold flags, NewZipP from the grab, the climb step, velocity start after the hold move |
| spy-pipe-climb-down | 328.10 | 0.00 | pass | pass | F7, F9 |
| spy-zip-line-13 | 142.70 | 0.01 | pass | FAIL F11 | F8: physLinear velocity start and facing |
| spy-zip-line-29 | 115.62 | 25.97 | FAIL F12 | FAIL F12, F11 | F8 fixed the grab; F12 open |

Changes by finding:

- **F1.** UStaticMesh::LineCheck with an extent sweeps a vertical cylinder (radius Extent.X, half height Extent.Z)
  on separating axes; the editor's live box trace along the frame 176 to 177 move hits the sand mesh, as the
  cylinder does, and misses the brick corner the box clipped. No converter change. PointCheck works in the mesh's
  frame; MultiLineCheck ranks hits by pulled-back time. The point check first missed the node-box cull and kept a
  crouched spy from standing up next to a mesh wall below the roof (the setup start of fall-ledge); fixed.
- **F2.** MyDoJump by mode: from a hang (Ledge) it does nothing; SA_LEDGEFALL, fences, pipes and ladders push off
  backward (-200 facing, +50 up); the zip line falls with its velocity; walking adds JumpZ.
- **F3, F4, F10.** The pawn's tick runs UpdateAnimation with the current dt before physics, and
  UpdateMovementAnimation and OldPhysics = Physics after; the port no longer advances the pawn's animation from the
  controller's tick. PlayAnim with no tween starts a one-shot at 0 and a loop at 1e-4.
- **F5, F6.** NativeComputeControlVariables' iMove bits (camera-relative sideways, forward with a held threshold)
  and the three back-to-wall probes from spec/native/natives2-traversal.md.
- **F7, F9.** The pipe hold actions set the climb flags; the climb step (0x11200140) sets bCanStop; the grab's
  script sets NewZipP; physOnPipe, physOnPipeH and physOnLadder measure the velocity from after the hold move. The
  pipe base rest now matches the editor's (the cylinder rests 1.4 above the mesh).
- **F8.** physLinear measures the velocity from after the hold move and faces the cable.
- physFalling's floor branch keeps the remaining time when the pawn does not land.

### Still failing

- **F11 (fixed mode only):** fall-ledge, jump-in-place, pipe-climb-up (end error 10.12 against 10 over a 500
  frame climb), zip-line-13 (velocity RMS 32.57 against 30). The 60 Hz grid against the original's 60.35 fps.
- **F12. zip-line-29, dt-replay frame 330.** The spy falls onto a 4 uu window sill. The sim and the editor's live
  queries agree on the contact (0.44 uu) and the side probe's 3.5 uu push. After that frame each golden run's
  velocity is the average of the velocity before the update and the displacement velocity physFalling writes, in
  all components (all three runs). The decoded physFalling and performPhysics have no such step, so it is a second
  velocity update not yet found; the sim keeps the displacement velocity. Location RMS 25.97 against 7.15, end
  error 0.61, phases 5 frames late.
- Converted BSP: zero-extent traces miss part of the lane floor (polygons wound the other way and front faces
  only). Box sweeps are unaffected.

Port deviations kept: the screen-space stick test for a climbing spy is not ported (aForward against 3000 stands
in), the mouse path of NativeComputeControlVariables is unchanged, loop AnimEnd is not raised, and the walk
blender's weights are not modelled.
