# difftest: golden traces of the original game

Records what the original does when a scenario (`tests/scenarios/**/*.scenario.json`) is played in
it, one stable sample per frame, so the rewrite can be compared with it (`tests/OpenScct.DiffTest`).
Formats: `tests/scenarios/README.md`. Results and numbers: `docs/spikes/R4-difftest.md` (locomotion) and
`docs/spikes/R4-traversal.md` (GE moves, roll, back to wall, crawl) and `docs/spikes/R4-moveset.md` (ladders, hand
over hand, fence, wall and split jumps, vault, drops, ramps, steps; SteeE and MounE).

Traces, spot surveys and envelopes are game-derived data: they go under `local/difftest/`
(gitignored), never into a tracked path.

## Files

| File | What |
|---|---|
| `record.py` | Command line: `record`, `envelope`, `summary`, `show`, `check` |
| `scenario.py` | Loads and validates `svm.scenario/1`; expands the inputs into a per-frame timeline |
| `trace.py` | Writes and reads `svm.trace/1` (gzip JSON lines, float32 text); run metrics and the noise envelope |
| `livegame.py` | The running game: field offsets from its reflection data, stable per-frame samples, input writes |
| `sandbox.py` | Headless sandbox editor, Play Level under the play lock, clean-up of this run's processes |
| `spots.py` | Finds flat open lanes and walk-off edges in a map with the editor's live collision queries |
| `gespots.py` | Finds spots at the traversal GEs (ledge jump grabs and walk-offs, pipes, zip lines), crawl gaps and box walls for back to wall |
| `phases.py` | A run as phases (one controller state and Physics each) with timing, motion, animation and lock; the spread over runs |
| `movespots.py` | Spots for the rest of the moveset: ladders, hand-over-hand pipes, fences, barrier vaults, wall jumps, split jump corridors, steps and stairs, slopes, drops (builds and caches a floor map when needed) |
| `test_difftest.py` | Tests on synthetic data (no game) |
| `combat*.py`, `rig/` | Two-player combat recordings: a map script (the rig) spawns a target player and drives the local weapon; `combat.py build/check/record/measure/show`, format `svm.combat/1` (`tests/scenarios/combat/README.md`), results `docs/spikes/R4-combat.md` |

## Commands

Always `python -I -B`.

```
# validate scenario files
python -I -B re/tools/difftest/record.py check tests/scenarios

# record every scenario 3 times (one Play Level per map, team and run index)
python -I -B re/tools/difftest/record.py record "tests/scenarios/movement/*.scenario.json" --runs 3

# one line per recorded scenario (noise envelope, state sequences), a trace as a table
python -I -B re/tools/difftest/record.py summary
python -I -B re/tools/difftest/record.py show local/difftest/golden/movement.spy-run-forward/run1.jsonl.gz --every 10

# survey a map for test spots (editor only, no game, no lock); --check details given lanes
python -I -B re/tools/difftest/spots.py --map SteeE --out local/difftest/spots/SteeE.json

# spots at the GEs, crawl gaps and box walls (needs local/formats/ge/<Map>.json and the floor map cache above)
python -I -B re/tools/difftest/gespots.py --map SteeE --out local/difftest/spots/SteeE-ge.json     --floors local/difftest/spots/SteeE.floors.json

# traversal goldens go to their own root (see "Traversal extras")
python -I -B re/tools/difftest/record.py record "tests/scenarios/traversal/*.scenario.json" --runs 3     --out local/difftest/golden-traversal
python -I -B re/tools/difftest/phases.py local/difftest/golden-traversal

# moveset spots on any map (GE kinds from local/formats/ge/<Map>.json; a floor map for split, steps, slopes, drops)
python -I -B re/tools/difftest/movespots.py --map MounE --out local/difftest/spots/MounE-moves.json
python -I -B re/tools/difftest/movespots.py --map SteeE --out local/difftest/spots/SteeE-moves.json     --floors local/difftest/spots/SteeE.floors.json

# moveset goldens go to their own root too (SteeE and MounE; traversal extras for every scenario)
python -I -B re/tools/difftest/record.py record "tests/scenarios/moveset/*.scenario.json" --runs 3 --traversal     --out local/difftest/golden-moveset
python -I -B re/tools/difftest/phases.py local/difftest/golden-moveset

# tests
python -I -B -m unittest discover -s re/tools/difftest -p "test_*.py"
```

`record` options: `--out` (default `local/difftest/golden`), `--sandbox` (default
`F:\SCCT-Automation\svm\Sandbox`), `--re-tools` (the RE+ private `tools/automation` folder, or
`SVM_RE_TOOLS`), `--fps` (frame cap for the run, default 60, restored after), `--delay` (ms after a
frame starts before it is sampled, default 2), `--only id,id`, `--lock-timeout`, `--one-game` (all
runs of a team in one game: faster, but the envelope then misses the game-to-game spread), `--traversal`
(write the traversal extras for every scenario; always on for profile `traversal`).

## How a run is recorded

1. The sandbox must be idle (nothing running from it); otherwise the tool waits. The editor starts on
   the hidden desktop `SVMHeadless` and opens the scenario's map (MapsEd).
2. Per team and run index: Play Level under `gamewait.play_lock` (the machine-wide lock: the sandbox
   is not slotted); run N of every scenario of the team is played in game N, so the runs of one
   scenario come from different games. If another sandbox's game runs, `game_tools.start_game` waits in turn. `game_tools`
   mutes the game when it attaches. A dialog on the hidden desktop ends the run unanswered.
3. Per scenario run: all input bytes released, the setup steps (teleport by a Location write,
   stance by crouch presses, view, `bFastSpeed`, `Health`), `waitStill`, the settle frames, then
   frame -1 is sampled and the inputs of frame 0 written.
4. Per frame: spin on `LevelInfo.TimeSeconds` until it changes (the level tick has begun), wait 2 ms,
   read the pawn, controller and level blocks and the animation channels, read TimeSeconds again
   (changed: the read is torn, dropped, and the next frame taken), read the fields a second time 0.4 ms
   later (different: the tick was still running, read again). Then write the next frame's inputs and
   check TimeSeconds did not move meanwhile (else that frame is marked late). A frame whose
   TimeSeconds step is longer than its `m_dT` counts as a gap.
5. The game is quit (forced; a headless window cannot be closed politely) before the lock is let go;
   at the end every process this tool saw start from the sandbox is ended and the frame cap restored.

Only memory is used: reads everywhere, writes only to the local controller's input bytes,
`bPressedJump`, `bFastSpeed` and view rotation, and (setup only) the pawn's Location, Rotation and
Health. Physics is never written. The test player hook DLL is never loaded.

## Layout facts it relies on

From the game's reflection data at run time (offsets are printed into every trace header), plus:
`UObject+0x0C` is the state frame, whose `+0x18` is the current state; the mesh instance's animation
channels are a TArray at `+0x100` of 144-byte records (`+8` sequence name, `+12` rate, `+16` frame,
`+80` blend alpha, `+100` channel-0 tween weight).

## Traversal extras

A scenario with `"profile": "traversal"` (or `record --traversal`) adds three keys to every frame line, read
in the same stable sample as the rest:

| Key | What |
|---|---|
| `tv.pawn` | SBasePawn and Pawn fields the GE moves use: `ZipP1`, `ZipP2` (the GE held), `NewZipP1`, `NewZipP2`, `LastZipP`, `m_geoNormal`, `vBackFeetNormal`, `PrePivot`, `CollisionRadius`, `bCanStop`, `bIsZipSpeed`, `bIsLeftPart`, `TypeLedge`, `BTW`, `IK_Type`, `AnimAction` (name), `bIsWalking` |
| `tv.pc` | SPlayerController: `bCanStop`, `iOldMove`, `iRealMove`, `bTestBackFeetGrab`, `bFindGE`, `bBlockControlForJump`, `bRollAction`, `bWantsToMove`, `vWallNormal`, `m_LastSpecialMoveTime`, `m_LastRollingTime`, `aForwardRoulade`, `aStrafeRoulade`, `fGEZLocation`, `bToggleBTW`, `bToggleUsing`, `oldbUsing`, `oldbUseBackToWall` |
| `tv.ge`, `tv.newGe` | index in `local/formats/ge/<Map>.json` of the GE whose points are ZipP1/ZipP2 (NewZipP1/NewZipP2), either order, 2 uu; null when none |
| `tv.base` | name of the pawn's Base |
| `rm` | root-motion lock of the mesh instance: `mode` (+0x184: 0 off, 1 or 3), `rootKey` and `rootWorld` (current root bone key and world positions, +0x250 and +0x280), and while locked `origin` and `yaw`, the mesh transform the lock holds (MeshToWorld = Key^T x World from +0x1F0 and +0x220; origin = World.pos - Key.pos x MeshToWorld) |
| `ach` | every animation channel 0..15 that has a sequence, whatever its alpha: `[channel, sequence, frame, rate, alpha, tween weight (+100), tween rate (+24)]` |

The header then carries `traversal.offsets` (every field's offset from the game's reflection, the missing ones,
the lock offsets) and `traversal.geFile` / `geSha256` (the GE list the indices refer to).

The setup step `{"op": "leaveGE", "maxFrames": n}` is a recorder-side reset that each traversal scenario starts
with: a pawn left on a GE or in a move by the previous scenario of the same game is dropped with crouch presses
(every GE state drops on the crouch latch) until it walks. It does nothing when the pawn walks; a sim, which
starts each scenario fresh, treats it as empty.

Traversal goldens are written to `local/difftest/golden-traversal/<id>/`, not the default `golden/` root:
`tests/OpenScct.DiffTest` (GoldenTraceTests, PlaybackOfEveryGoldenPasses) checks every golden in the default root
against locomotion invariants (Physics 1 or 2, steps bounded by the velocity, the locomotion profile only).
Move them into `golden/` once that project gates those checks by profile and knows `traversal`.

Moveset goldens (`tests/scenarios/moveset/`, `docs/spikes/R4-moveset.md`) are written to
`local/difftest/golden-moveset/<id>/` for the same reason. `tests/OpenScct.DiffTest` (RunnerTests) reads only `golden/`
and `golden-traversal/`, so the sim is not compared with these yet. A scenario's map is loaded per game, so one
`record` call covers both maps.
