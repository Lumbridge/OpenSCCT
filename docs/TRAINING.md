# Demo training data

[Documentation](README.md)

This is the version 1 schema for optional structured demo exports. It is intended for data analysis and
future training experiments. Ordinary [demo recording and playback](DEMOS.md) does not need it.

## Enable capture

Training data saving is disabled by default. Set `SVM_ENABLE_TRAINING_DATA=1` before starting the game/server to
enable capture; online downloads also require that flag on the client. The equivalent programmatic setting is
`EnableTrainingData` on `ServerSettings`, `OnlineServerSettings` and `OnlineClientSettings`. Ordinary demo
recording does not depend on this flag, and existing training companions are kept.

When enabled, recording a demo also attempts to save `<basename>.training.jsonl.gz`. The companion is a stable
export of structured simulation data; reading it does not require decoding network packets or loading the original game
build. Keep the replay and matching map/build for visual investigation. Older replays cannot retroactively supply
inputs or private player state that were never recorded.

Capture collects data only; it does not train a model or change bot behavior. Training companions remain on the recording server and
are delivered to opted-in personal-demo clients; the master's existing replay upload does not upload companions.

## Envelope and records

Each gzip-compressed UTF-8 line is `{ "type": "...", "tick": 123, "data": { ... } }`. See
[DEMOS.md](DEMOS.md#training-data-companions) for the header, completion record, validation, transfer and size
limits. Require `format = OpenSCCTTraining`, `version = 1` and a valid final completion count. Null fields mean
unavailable data, not zero or a negative observation. Consumers should accept additional fields and reject
unsupported schema versions.

| Type | Data and producer |
| --- | --- |
| `header` | Build, map, protocol/content hashes, rules, start time and rates. `TrainingDataWriter`. |
| `capture` | Snapshot rate, timing/coverage conventions, coordinate units and decimal-string match seed. `DemoRecorder.Training`. |
| `snapshot` | `players`, `publicObjectives`, `coverage`, `timeSeconds`. `TrainingObservations`. |
| `input` | Player/controller/pawn IDs, `observationTick`, and an applied `command`. `ITrainingInputSink`. |
| `action` | The same identity/observation link, with a dispatched exec or applied native action. `ITrainingInputSink`. |
| `event` | Authoritative damage/death, objective completion, dog-tag collection and round markers. These are outcome labels. |
| `complete` | Complete marker and count of all non-header/non-completion records. `TrainingDataWriter`. |

Positions, velocities and acceleration use game units. Rotation components retain 65536 units per turn. Player
and actor IDs are local to the match; a respawn changes the pawn ID. No account IDs are used. Native action codes
retain the game's decoded action-number vocabulary; source/kind/button labels use names rather than enum ordinals.

## Timing and input coverage

Snapshots are sampled at 10 Hz, immediately before network dispatch and application of the next server tick.
Audio cues are collected every tick into bounded per-player buffers and included in the next snapshot. Input and
action records are emitted at their application cadence, not reduced to the snapshot rate. `observationTick`
links each record to the latest already-recorded snapshot; it can be null before the first snapshot.

`command.serverTick` identifies the authoritative step. Remote `inputTick`, `sequence`, `epoch` and
`clientTimeSeconds`, when available, identify the admitted client command. These clocks are deliberately separate:
catch-up can apply several client inputs during one server tick. Preserve file order within a tick. A local input
can be captured between world steps, so file tick values need not be strictly increasing.

- **Remote players:** accepted movement acceleration, jump, view/aim, duration, available command metadata and
  applied native action codes. Rejected packets and duplicate commands are not training actions. Raw remote
  keyboard/mouse data is unavailable and stays null. No-simulation heartbeats do not produce movement rows.
- **Server bots:** button frames and dispatched execs, plus local movement. `kind = bot` in the player snapshot
  distinguishes them from human demonstrations.
- **Offline/local authority players:** assigned buttons, available raw mouse/analog inputs, dispatched execs and
  movement. A listen host's networked client is captured through the remote path.

An action record means the controller received the action after the applicable admission checks. It does not
mean the attempt succeeded: firing with no ammo can still be an input. Use subsequent outcome/state changes to
label effects. Do not interpret unrecorded generic RPCs as absent player actions.

## Observations versus privileged labels

Each `snapshot.players` entry contains:

- `self`: that player's position, velocity, aim/view, health, stance, weapon/ammo, gadget charges/selection,
  vision mode and controller state, with explicit missing fields and truncation flags.
- `perception.visible`: currently sight-filtered contacts, with position/team/strength and no target health or
  private controller state. Contacts outside sight are omitted; hidden targets never refresh a remembered pose.
- `perception.heard`: audible cue strength buckets and relative bearing in 45-degree sectors, with no source ID,
  exact source position or distance. Read the coverage/truncation fields before treating an empty list as silence.

Perception is a **server estimate using the normal AI perception profile**, not a record of client pixels, actual
attention, latency-delayed presentation or a bot's private memory/difficulty. It uses a private observer context
and the shared map-light loader, and does not install or advance live AI services. Missing collision/lighting,
unsupported remote-camera views and other unavailable observations are reported explicitly. Normal/night/motion
body sight is unavailable without map lighting; it does not substitute the AI's fallback brightness. Tracker
locks, rendered flash effects and full client audio/UI are not represented. MakeNoise coverage depends on the live
noise service; PlaySound cues are read independently. Public objective samples include only objectives marked for
HUD display and always-relevant replication, and retain no hidden trigger locations.

To build a policy example, select **only the acting player's `self` and `perception`**, the public objective data,
and permitted past observations. Other players' `self` records and global `event` outcomes are privileged data;
they must not become policy inputs. Use outcomes only as subsequent labels/rewards. Keep human and bot examples
separate, avoid training across respawn boundaries without resetting history, and split evaluation by whole match
rather than individual frames. This prevents neighboring frames from the same match leaking into evaluation.

Capture writers and perception sampling fail independently of gameplay. An incomplete/oversized companion is
abandoned, while the replay can still finish and be watched. Server retention and local cache housekeeping keep
the companion with its replay. A hub wait may expire before both files arrive; a completed replay remains saved.
