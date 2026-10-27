# Scenarios and traces

A scenario says what to play, frame by frame, in the original game and in the rewrite. The original
recorder (`re/tools/difftest/record.py`) plays it in a headless sandbox Play Level and writes golden
traces; the C# tests (`tests/OpenScct.DiffTest`) read the goldens, and a sim runner (`IScenarioSim`) will
play the same scenario in the rewrite and compare.

Scenario files are ours and tracked: `tests/scenarios/<area>/<name>.scenario.json`, id `<area>.<name>`.
Traces are game-derived and stay local: `local/difftest/golden/<id>/run<N>.jsonl.gz`.
The schema is `svm.scenario-1.schema.json` beside this file.

## svm.scenario/1

| Field | Meaning |
|---|---|
| `format` | `svm.scenario/1` |
| `id` | `<area>.<name>`, the same as the file's folder and name |
| `title` | one line |
| `map` | map name (a MapsEd `.sdc` in the sandbox) |
| `mode` | `playlevel`: editor Play Level (unlimited lives, no time limit) |
| `players` | one entry in v1: `{"team": "spy" or "merc"}`, the local player |
| `spawn` | the spot: `floor` `[x, y, floorZ]`, `yaw` (rotator units), optional `clearance` (uu, default 2), and free text `spot` / `notes` saying how it was chosen |
| `setup` | steps before the inputs (below) |
| `settleFrames` | frames to wait after the setup before frame 0 |
| `durationFrames` | frames recorded from frame 0 |
| `inputs` | frame-indexed input events (below) |
| `repeats` | runs to record (3: the noise envelope) |
| `profile` | comparison profile, `locomotion` or `traversal` (GE moves, roll, back to wall, crawl: the recorder also writes the traversal extras, `re/tools/difftest/README.md`) |
| `measure` | windows for the numbers the tests read off a run (below) |
| `covers` | coverage unit ids the scenario exercises (`uc:`, `nat:`, `def:`, `rep:`...); each must exist in `re/inventory/inventory.json` |

Setup steps, run in order:

| Step | Effect |
|---|---|
| `{"op": "teleport"}` | writes the pawn's Location to `spawn.floor` with Z = floorZ + its current CollisionHeight + clearance, its yaw and the view yaw to `spawn.yaw` |
| `{"op": "stance", "crouched": b}` | presses crouch (a toggle) until `bIsCrouched` is `b` |
| `{"op": "fastSpeed", "value": b}` | writes `SPlayerController.bFastSpeed` (the spy's slow/fast mode) |
| `{"op": "view", "pitch": p, "yaw": y}` | writes the controller's view rotation |
| `{"op": "write", "target": "pawn", "field": "Health", "value": n}` | a setup write; only the fields in `scenario.py` WRITABLE are allowed, never Physics |
| `{"op": "wait", "frames": n}` | waits n frames |
| `{"op": "waitStill", "maxFrames": n}` | waits until the pawn is walking and still for 10 frames |
| `{"op": "leaveGE", "maxFrames": n}` | recorder-side reset: a pawn left on a GE by the previous scenario of the game is dropped with crouch presses until it walks (nothing when it walks; empty for a sim) |

Input events: `{"frame": k, ...}` with frames in order and any of

- `"buttons": {"BT_Up": 1}`: controller input bytes (`BT_Up`, `BT_Down`, `BT_Left`, `BT_Right`, `bCrouch`, `bRoll`, `bUsing`, `bFireMode`, `bUseBackToWall`). Held: the value stays until another event changes it.
- `"jump": true`: sets `bPressedJump` before frame k only (what the `Jump` exec function does).
- `"view": {"pitch": p, "yaw": y}`: the view rotation, frame k only.
- `"fastSpeed": true|false`: `bFastSpeed`, frame k only.
- `"note"`: free text.

The inputs of frame k are written after frame k-1 has been sampled, so the tick of frame k sees
them; a write that lands after frame k began is marked `late` in the trace.

`measure` windows (frame numbers): `plateauSpeed {from, to, expect}` (mean horizontal speed over
`[from, to)`), `accelTime {from, fraction}`, `decelTime {from, fraction}`, `stopDistance {from, to}`,
`jumpApex {from, expect}`, `airtime {expect}`, `jumpDistance {from}`, `fall {expectDrop}`,
`fallDamage {expect}`. `expect` holds the decoded value from the specs, where there is one.

## svm.trace/1

Gzip-compressed JSON lines: a header, one line per frame, a footer. Floats are float32 values written
as the shortest text that reads back to the same float32.

Header: `format`, `scenario` (`id`, `path`, `sha256` of the file, `doc`: the scenario as recorded),
`run`, `runs`, `team`, `map` (`name`, `mapsEd` path and SHA-256, `autoplaySha256` of the map Play
Level ran), `binaries` (SHA-256 of the game, Reloaded.Core, the editor, the editor plugin, the
launchers and the headless proxy) and `binariesMatchLock` (against `re/binaries.lock.json`), `fps`
(the frame cap), `pawnClass`, `controllerClass`, `offsets` (every field's offset as the game's
reflection gave it), `recorder`, `setup` (what the setup did), `timeDilation`, `sandbox`, `startedAt`.

Frame lines (frame -1 is the settled state just before the inputs):

| Key | Field | Source |
|---|---|---|
| `f` | frame index from the first input frame | recorder |
| `t`, `dt` | `LevelInfo.TimeSeconds`, `LevelInfo.m_dT` | level |
| `wall` | wall seconds since frame -1 was sampled | recorder |
| `loc`, `vel`, `acc` | `Location`, `Velocity`, `Acceleration` | pawn |
| `rot`, `crot` | pawn `Rotation`, controller `Rotation` (rotator ints, not masked) | pawn, controller |
| `phys` | `Physics` | pawn |
| `ps`, `cs` | current state of the pawn and of the controller (the class name when in no state) | StateFrame |
| `crouch`, `roll` | `bIsCrouched`, `bIsRolling` | pawn |
| `hp` | `Health` | pawn |
| `gs`, `ch` | `GroundSpeed`, `CollisionHeight` | pawn |
| `zmax` | `fZMaxFalling` | controller |
| `jn` | `nJumpNumber` | pawn |
| `fwd`, `str`, `npm`, `imove` | `aForward`, `aStrafe`, `fNormePadMove`, `iMove` | controller |
| `fast`, `cwr`, `pj` | `bFastSpeed`, `bCrouchWithRoll`, `bPressedJump` | controller |
| `in` | inputs written before this frame's tick (non-zero buttons, `jump`, `view`, `fastSpeed`) | recorder |
| `anim` | animation channels with a sequence and a weight: `[channel, sequence, frame 0..1, rate, alpha]` and, for channel 0, the tween weight | mesh instance |
| `late` | the inputs may have reached the game one frame late | recorder |
| `gap` | frames missed just before this one | recorder |
| `tries`, `unstable` | reads needed for a stable sample; `unstable` when none was | recorder |
| `tv`, `rm`, `ach` | traversal profile only: GE and move fields of the pawn and controller with the GE index held, the mesh's root-motion lock, all 16 animation channels (`re/tools/difftest/README.md`) | pawn, controller, mesh instance |

Footer: `end`, `frames`, `torn` (samples dropped because the next frame began during the read),
`gaps`, `late`, `unstable`, `wallSeconds`, `gameSeconds`, `dtMean`, `dtMin`, `dtMax`, `fps`.

`envelope.json` (`svm.envelope/1`) beside the runs: every pair of runs compared (location RMS,
maximum and end distance, velocity RMS on a common clock), the worst of each, the medoid run used as
reference, and whether the runs went through the same pawn, controller and Physics sequences.
