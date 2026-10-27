# R4 spike: differential testing v1 (golden locomotion traces)

**Result: done.** The original can be driven and sampled reliably from outside: ten SteeE locomotion
scenarios were recorded three times each, every run in its own headless sandbox Play Level game, with
every frame sampled and no torn, missed, late or unstable sample in 30 runs (nor in 30 more soak runs).
The measured speeds, jump and gravity numbers match the decoded spec values exactly, with one
exception (merc crouched speed) explained by an integer truncation. One behaviour of the original is
not repeatable: a running jump's landing either keeps or zeroes the horizontal speed depending on
frame timing.

Recorded 2026-10-23 from Enhanced SCCT Versus v4.0 Public Beta 3 in `F:\SCCT-Automation\svm\Sandbox`:
the game `SCCT Versus` (a98873aa...) and `Reloaded.Core.dll` (6b9d4444...) match
`re/binaries.lock.json`; the sandbox's `ChaosTheory_Editor.exe` differs from the lock because the RE+
editor plugin marks it large address aware. SteeE MapsEd c78319f4...; frame cap 60.

## What was built

| Path | What |
|---|---|
| `re/tools/difftest/record.py` | Recorder CLI (`record`, `envelope`, `summary`, `show`, `check`) |
| `re/tools/difftest/livegame.py` | Per-frame stable sampling and input writes on the running game |
| `re/tools/difftest/sandbox.py` | Headless editor, Play Level under `gamewait.play_lock`, clean-up of own processes |
| `re/tools/difftest/spots.py` | Spot finder: flat lanes and walk-off edges from the editor's live collision |
| `re/tools/difftest/{scenario,trace}.py` | `svm.scenario/1` and `svm.trace/1`, metrics, noise envelope |
| `tests/scenarios/` | Format (`README.md`), JSON schema, the 10 movement scenarios |
| `tests/OpenScct.DiffTest/` | Trace reader and writer, comparator with profiles, noise envelope, `IScenarioSim`, measurements, tests |

## Method

- Drive: memory writes on the local `SPlayerController`: input bytes (`BT_Up`...), `bPressedJump`
  (the `Jump` exec function's effect), `bFastSpeed`, view rotation. Setup only: the pawn's Location
  (teleport), Rotation and Health. Physics is never written; no hook DLL.
- Sample: spin on `LevelInfo.TimeSeconds`; 2 ms after it changes, read pawn, controller, level and
  animation channels, re-read TimeSeconds (torn read check), read again 0.4 ms later (tick finished
  check). Inputs for frame k+1 are written right after frame k's sample (checked not late).
- Runs: run N of every scenario of a team is played in game N (3 games per team), so a scenario's
  runs come from different games.
- Spots: `spots.py` builds a floor map of SteeE from 550,088 `point.info` probes, floods the
  walkable area from the 19 player starts, sweeps lanes with a widened spy box in 16 directions, and
  searches walk-off edges (620 to 1300 uu drops) with the flight path swept at 430, 230 and 175 uu/s.
  Chosen: the east yard lane (floor z -1143.5, 1792 uu clear along +Y, nearest GE 599 uu) for nine
  scenarios, and a roof edge 790 uu above the yard for the fall (clear at walking speeds; a full-speed
  run-off would hit a static mesh).
- Cost: six games; the play lock was held 27 to 30 s per merc game and 45 to 46 s per spy game,
  3.7 minutes in all for 30 runs.

## Measured vs decoded

The three runs agree to the precision shown (speed spreads below 0.01 uu/s).

| Quantity | Spec (decoded) | Measured | Notes |
|---|---|---|---|
| Spy run (fast) | 423.5 | 423.500 | `GroundSpeed` exactly 423.5; `aForward` 6600 (6000 x 1.1) |
| Spy slow (sneak) | 169.4 | 169.400 | `aForward` 2640 = 6600 x 0.4: the speed mode is already applied to `aForward`; `fNormePadMove` 0.4; `iMove` 0 (2640 < cMinPadValue 3000) |
| Spy crouched (fast) | 221.4 | 221.375 | `aForward` 3450 (6000 x 0.575) |
| Merc walk | 346.5 | 346.500 | `aForward` 5400 (6000 x 0.9) |
| Merc crouched | 117.0 | **116.912** | `aForward` 1823.42 but `Acceleration` 911, not 911.71: half the stick is truncated to an int before `GroundSpeed = 385 x 911 / 3000`. The spec value should be 116.9 |
| Speed-up | not decoded | linear, `Acceleration` = int(0.5 x `aForward`) | 3300, 1320, 1725, 2700 and 911 uu/s^2: every stance reaches its speed on the 8th frame (0.133 s), 90 % on the 7th (0.117 s) |
| Stop | not decoded | instant | the first frame without input has zero velocity and does not move; no slide |
| Gravity | -1500 | -1500.00 (fit of Velocity.Z, +-0.02) | |
| Jump impulse | spy 610, merc 495 | 610.00, 495.00 | take-off frame Velocity.Z + 1500 x dt |
| Jump apex | spy 124.0, merc 81.7 | 124.00, 81.67 | position steps use the mean of the old and new velocity |
| Air time (jump in place) | 2 JumpZ / 1500 = 0.813, 0.660 | 48 or 49 frames (0.80 to 0.82 s), 39 frames (0.649 s) with Physics 2 | the landing falls inside a frame |
| Running jump | not decoded | take-off speed 423.5 drops to 417.0; no air acceleration; 340.1 uu forward | see the landing below |
| Walk-off air control | not decoded | horizontal acceleration 0.5 x `Acceleration`, fading with speed | slow walk-off: 169.4 to 296.8 uu/s before forward is let go |
| Fall height | edge 790 above the yard | `fZMaxFalling` - landing Z = 790.72 | |
| Fall damage | spy `int(100 (h - 600) / 900)` = 21 | 21 (all runs) | Health 100 to 79 |
| Fall time | sqrt(2 x 790 / 1500) = 1.026 s | 1.032 s (62 frames) | last air frame at -1523 uu/s |
| High landing hold | RE+ 1.4 s | `PlayerLanding` 1.348 s (81 frames) | drop over 450 |
| Low landing (standing still) | not decoded | `PlayerLanding` 0.716 s (43 frames), spy and merc | |
| Walking hover | RE+ 1.9 to 2.4 | 1.0 uu | Location.Z = floor + CollisionHeight + 1.0 (87.5 standing, 50 spy crouched, 68 merc crouched) |

States: spy jump `s_PlayerWalkingSt0H > s_PlayerJump (45 frames, the clip) > PlayerFalling >
PlayerLanding > s_PlayerWalkingSt0H`; the merc stays in `s_PlayerJump` all 39 air frames; walk-off
`s_PlayerWalkingSt0H > PlayerFalling` (fNormePadMove 0.4, no ledge behind) `> PlayerLanding`.

`fZMaxFalling` (open question 1 of `spec/movement/jump-fall-land.md`): set to Location.Z on the first
airborne frame, follows the pawn up to the apex, and is neither reset on landing nor refreshed while
walking (the next scenario starts with the last fall's value).

**Running-jump landing is timing-dependent.** In 16 of 17 recorded running jumps the landing frame
zeroes the horizontal speed and the spy re-accelerates over 8 frames (channel 4 `JoggStAlFd0`); in one
(golden run 2) it keeps 417 uu/s and goes on at 423.5 (channel 1 `JoggStRpFd0`). States and Physics
are the same in both. That run is 31 uu ahead at the end, which sets this scenario's envelope.

## Noise envelopes (3 runs from 3 games, worst pair)

| Scenario | loc RMS | loc end | loc max | vel RMS |
|---|---|---|---|---|
| spy-run-forward | 0.61 | 1.22 | 1.22 | 4.70 |
| spy-sneak-walk | 0.24 | 0.49 | 0.49 | 1.88 |
| spy-crouch-walk | 0.31 | 0.61 | 0.61 | 2.36 |
| spy-run-stop | 0.49 | 0.65 | 0.65 | 4.00 |
| spy-jump-in-place | 0.10 | 0.00 | 0.45 | 3.16 |
| spy-jump-forward | 21.13 | 31.42 | 31.42 | 51.49 |
| spy-fall-ledge | 0.78 | 0.38 | 3.03 | 14.55 |
| merc-walk | 0.68 | 1.36 | 1.36 | 5.26 |
| merc-crouch-walk | 0.23 | 0.46 | 0.46 | 1.76 |
| merc-jump | 0.09 | 0.00 | 0.46 | 3.80 |

Frame times: 15.8 to 17.5 ms, standard deviation 0.02 ms; each game settles at its own rate (60.23 to
60.53 fps), so a stop or a landing falls a few ms apart on the common clock and the velocity there
jumps by up to 1,500 uu/s. That is why the envelope is wider across games than within one game (the
first recording, all runs in one game, gave loc RMS 0.00 to 0.77 and vel RMS 0.03 to 5.98), and why
`--one-game` is not the default. With `tolerance = max(profile, 1.5 x envelope)` the profile sets
every limit except vel RMS for spy-fall-ledge (21.8) and everything for spy-jump-forward; the C#
comparator reports such metrics as noise-dominated. Pawn, controller and Physics sequences are the
same in every run.

Soak (all runs in one game, spy-run-forward, spy-jump-forward and spy-fall-ledge 10 times each, 2.9
minutes under the lock): no torn, missed, late or unstable sample; 60.40 to 60.53 fps; longest frame
20.4 ms. Ten runs spread wider than three in one game (jump-forward vel RMS 11.8 against 0.15; fall
loc max 2.25 against 0.20).

## C# side

`tests/OpenScct.DiffTest` (on the solution): `TraceFile` (svm.trace/1 reader and writer), `Scenario`
(timeline expansion identical to the recorder's), `RunMetrics` (location RMS, max and end, velocity
RMS on a common clock), `ComparisonProfile.Locomotion` (2 / 10 / 10), `NoiseEnvelope` (pairwise
worst, medoid reference), `Comparator` (pass, noise-dominated metrics, state sequences), `Locomotion`
measurements, `IScenarioSim` with `SimRequest` (`DtReplay`: the reference run's dt and applied inputs
per frame; `Fixed`: 60 Hz with the scenario timeline), `DiffHarness`, `GoldenStore`
(`SVM_DIFFTEST_GOLDEN` or `local/difftest/golden`). Golden tests skip when no goldens exist (CI).
The metric definitions match the recorder's `envelope.json` (checked by a test).

## Deviations and limits

- One player only; multi-pawn scenarios need the two-pawn rig (plan, R4).
- No sim runner yet: the harness is exercised with a playback stand-in.
- The fall uses a slow walk-off (see Method).
- The RE+ driver caches name tables by process id; a reused pid once gave a wrong table (the pawn
  read as `FindAttClosestTarget`). The recorder now drops caches older than the game and refuses a
  player whose classes read wrong.

## Next

- `DtReplay` gates should compare with the replayed run and need their own (smaller) noise measure;
  the timing-dependent landing needs Ghidra on `processLanded` / `physFalling` before it can be gated.
- Scenarios for the open questions: W+D diagonal in slow mode, turning (Mario turn rate), roll,
  crouch and stand under a 150 uu ceiling, steps and slopes, ledge grab (traversal profile with state
  and animation sequences +-2 frames).
- Ghidra on `MyDoJump` / `physFalling` for the take-off loss (423.5 to 417.0) and the 0.5 air control
  factor; spec updates for merc crouched 116.9, the linear speed-up, the instant stop, `fZMaxFalling`
  and the 1.0 uu hover.
- The OpenScct.Physics / OpenScct.Game runner implementing `IScenarioSim`, first in `DtReplay`.
