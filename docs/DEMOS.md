# Demos

[Documentation](README.md)

Demos let you watch recorded matches in first person, third person, with a free camera, or as the player saw them.
They store game data rather than video, so playback needs a compatible game build and matching map content.

**To watch:** open **Demos → On this computer** from the main menu. **Recent online** lists recordings
available to signed-in players. Use **Watch** to play or **Keep** to save a permanent local copy.

**Match history** combines permanent local and account scoreboards, including games without recordings.
Open **View scoreboard** for role-specific statistics. Replay expiry/deletion does not remove a scoreboard;
see [Scoreboards and match history](SCOREBOARD.md).

This page covers [saving matches](#saving-your-own-matches), [playback and controls](#watching-game),
[server recording](#recording-server) and [the file format](#file-format-scctdemo).
Optional analysis exports are described under [Training data companions](#training-data-companions).

## Saving your own matches

Turn on **Automatically save demos** in the lobby's **Match demos** panel (or in Match settings → Game). Each
player chooses independently; a guest can request a demo even when the host has recording turned off. Create
Session and the Demos library offer the same preference. It takes effect immediately for upcoming rounds and does not require Apply
changes. The preference is saved as `demos/record_matches`, migrating the older `host/record_demos` value.

An updated server records one full spectator stream when at least one player requests it. After the round, it
delivers a copy to each player who opted in. Stay connected until the lobby reports **Demo saved**, then find it
under **Demos → On this computer**. This is a full match replay, rather than a recording limited to the local
player's visible actors. A server without this extension is explicitly reported as unsupported.

Transfers pause during active play or loading, resume across the session's map changes, and use bounded reliable
chunks in the lobby/results screen. The client checks SHA-256 and the completed demo header/index before moving
its temporary download into the demo library. Downloads are limited to 64 MB; the server retains at most two
pending recordings per player. Leaving/disconnecting before completion cancels an incomplete download.
Hub matches wait on the results screen for the requested download before returning to the pads, for up to five
minutes. Leaving still returns immediately; an expired wait reports that the incomplete demo was not saved. The
hub keeps the match server alive for progressing downloads during this bounded grace period.

## Watching (game)

The library shows map pictures, mode, length, date, server, players and storage status. Search by map,
player, server or local filename; use mode/storage filters and newest/oldest sorting to narrow the list.
Unknown maps keep their recorded code and a placeholder picture. Small screens use one column; wider
screens use two, with text scaled for high resolutions.

- **On this computer** shows saved recordings in `user://demos` and cached downloads in `user://demos/cache`.
  Cached files expire after seven days. **Keep** makes a permanent copy; saved files take precedence over duplicates.
- **Recent online** shows one page of the master's recordings for signed-in players. Filters apply to that page.
  **Watch** downloads, verifies size and SHA-256, then starts playback. **Keep** downloads without playing.
  Leaving the library cancels unfinished transfers and removes temporary files.
- **Delete** asks for confirmation and removes the local recording and any training companion. It does not
  delete the online copy. Cancel or Escape keeps the file.
- **Compatibility:** the protocol and map content hash must match the installed game. An incompatible protocol
  disables Watch. Other failures explain the problem and preserve the library view when playback has not started.
- **Command line:** `--svm-demo=path` opens a recording at startup.

Playback runs from 0.25× to 4× speed. The visible timeline starts at the round-start marker, skipping loading.
Seeking forward advances the current stream. Seeking backward reloads the map, restores the latest checkpoint
at or before the target, and fast-forwards from there. New recordings capture a checkpoint every 60 seconds
when the stream can be saved at a clean packet boundary. For earlier targets and recordings without
checkpoints, playback rebuilds from the beginning. Seeking also works while paused.

The compact playback dock includes Play/Pause (Replay at the end), restart, five-second skips, speed, player and camera
selectors, controls help and Back to demos. The timeline previews timestamps and named events; dragging commits
one seek on release. The dock stays visible while paused, seeking, hovered, dragging or using a selector/help.
After four idle seconds during playback it hides, and mouse or keyboard activity reveals it again. A small
**Show controls · H** button remains available whenever the dock is hidden. Explicit Hide stays hidden until that
button or H restores it. Camera hints and the full shortcut list expand under **Controls**.
Escape (after closing help) and Back to demos ask for confirmation before leaving. The prompt pauses playback;
Keep watching or Escape cancels it and restores the previous pause state.

Third person follows the watched player's recorded view direction. Hold right mouse to pan around them;
release it to smoothly return behind them over about half a second. The return uses real time, so it works
while paused and at any playback speed. Switching players or returning to third person starts behind the
selected player. Switching to free camera keeps the current position and direction.

**As played** follows the player's recorded camera through changes such as aiming the Spy gun, zooming and
switching to a sticky camera. Mouse orbit is available in **Third person**. **First person** stays at the player's
eyes, using a recorded first-person view where available.

Turn on **X-ray** in the playback dock to see team-coloured player outlines through walls in any camera mode.

### Keys

| Key | Action |
| --- | --- |
| Space | pause / play |
| Up / Down | faster / slower |
| Left / Right | Back / forward 5 seconds (Shift: 30 seconds) |
| Click or drag the bar | jump on release (on a marker: 3 s before it) |
| Home / End | restart / seek to the end |
| C | first person, third person, free camera, as played |
| [ ] or mouse wheel | previous / next player |
| 1 to 9 | watch that player |
| Right mouse (hold) | orbit in third person (release to return behind); look round in free camera |
| W A S D, Q / E, Shift | fly the free camera, down / up, faster |
| H | hide / show the controls |
| ? | show / hide shortcut help |
| Escape | close help first, otherwise confirm returning to Demos; cancel an open confirmation |

## Training data companions

Training data saving is disabled by default. Set `SVM_ENABLE_TRAINING_DATA=1` before launching the game or server
to opt in to `<demo-basename>.training.jsonl.gz` companions for analysis and training. Online capture requires the
flag on the server; downloading a companion also requires it on the receiving client. Without the flag, demos
continue recording and downloading normally, with no training capture, companion file or companion transfer.
The implementation remains available for future use; existing companion files are retained.

The replay remains an ordinary `.scctdemo`; the companion is structured telemetry, not an alternate replay format.
Its writer is best effort: a disk, serialization or size failure abandons the companion without stopping gameplay
or the replay. Only a finished gzip stream with a completion record is published, using a temporary file and an
atomic rename. Interrupted recordings do not leave a published, apparently complete training file.

The stable version 1 envelope is one UTF-8 JSON object per line, compressed with gzip:

```json
{"type":"header","tick":0,"data":{"format":"OpenSCCTTraining","version":1,"gameVersion":"...","protocolHash":"0000000000000000","contentHash":"0000000000000000","map":"TestBox","gameOptions":"...","serverName":"...","startUnixMs":0,"tickRate":60,"netRate":20}}
{"type":"snapshot","tick":120,"data":{}}
{"type":"complete","tick":120,"data":{"complete":true,"records":1}}
```

`type` identifies the record schema, `tick` is the unsigned simulation tick, and `data` is an object of primitive
DTO fields. Property names and enum labels use camelCase; enums are strings, not unstable numeric ordinals. Engine
objects are rejected rather than serialized into object graphs. Protocol/content hashes are 16-digit hexadecimal
strings so consumers do not lose 64-bit precision. The header identifies the game build, map, content, rules,
start time and simulation/network rates. The final `complete` line counts all data records (excluding the header
and completion line); its tick is the largest data-record tick. Consumers must check the format/version, required
header fields and completion count before treating a file as complete. [TRAINING.md](TRAINING.md) documents the
input/action, snapshot and event payloads, their producers, timing and observation coverage.

Each companion is capped at 64 MB compressed, 256 MB expanded, 1 MB per JSON line and 16 levels of JSON nesting.
Incoming downloads are SHA-256 checked, then validated within those decompression/line limits and matched to the
replay's map, protocol/content hashes, start time and tick rate before publication. This bounds malformed or
compressed inputs as well as normal recording growth.

The personal-demo extension negotiates version 2 with `democap 1 2` and `demorecord <enabled> 2`; the leading
capability value preserves existing version 1 replay clients. A version 2 replay offer indicates whether a
companion follows. `demotrainingoffer`, `demotrainingdata` and `demotrainingack` transfer it after the replay with
the same round identifier and the existing bounded, resumable chunk transport. Both files use the local basename
`<UTC-date-time>-<map-shortcode>-<Spy|Merc|Spectator>-<round-id>`, using the player's recorded side even after lobby
travel or a revenge team swap. Offline recordings use the same descriptive naming. The two-pending-round limit
still counts rounds, not individual files; temporary server
companions are deleted with their replays once no recipient needs them. Older clients and replay-only recordings
continue to receive just the replay.

Keep the session connected until both saves finish. The replay becomes usable as soon as its own save completes;
a missing or failed companion never invalidates it. The lobby/results status and hub return wait remain pending
through the optional companion download, within the same bounded hub wait described above.

The results screen gives the demo its own status panel while saving, then offers **Open demos folder** when the
file is ready. If a requested demo is still pending, the Leave confirmation explains that disconnecting cancels
the download.

## Recording (server)

- `ServerHost.StartRecording(DemoWriter, now, game?)` (`src/OpenScct.Server/ServerHost.cs`) opens a recorder
  connection: `NetDriver.OpenRecorderConnection` (`src/OpenScct.Net/NetDriver.cs`).
  - The recorder looks like a client whose control channel the remote opened.
  - Its packets go to the demo file instead of the transport.
  - Each packet counts as acked once written, so nothing is ever resent.
  - It never times out and is not counted against the send budget.
- Then the host does what a Hello and a Join would do: it sends Welcome (into the file), logs in a spectator
  controller named Demo (`?SpectatorOnly=true?Team=255`, with one extra spectator place so no player loses one),
  marks the connection joined, and reports the recorder ready so it cannot block the match's loading gate.
  - The recorder is not in `ServerHost.Players`.
  - `PlayerJoined`/`PlayerLeft` are not raised for it.
  - Voice skips it.
- What the recorder is sent:
  - **Every actor.** `NetRelevancy.IsRelevantToRecorder` makes everything relevant, except the owner-only classes:
    controllers, interaction managers and player cameras. Another player's controller would look local on the
    client.
  - **All pawn samples.** Interest culling is off for it, so it gets every pawn's position and aim at full rate.
  - **No owner-only properties of other players.** Ammo and gadget counts are not in demos (see Limits).
- `DemoRecorder` (`src/OpenScct.Server/Demo/DemoRecorder.cs`) adds to the index every 30 ticks: the players, a
  marker each time a player's death count goes up, and a marker when the match starts and ends.
- Once a minute, the recorder saves a bootstrap of the spectator's actor channels, pawn poses, movers and grenades
  after a replication flush. The recorder keeps the original packet stream for uninterrupted playback.
- Who records:
  - **`svm-server`:** the `Demos` section of `svm-server.json` records every round into
    `OnlineServerSettings.DemoFolder`. `OnlineServer.DemoRecorded` reports each finished file, and files older than
    `KeepLocalDays` are deleted. The server log gives each demo's size and KB/s.
  - **Any player in an online match:** the personal preference requests recording through the session protocol.
    `OnlineServer.Demos` shares the finished full recording with opted-in players; `OnlineClient.Demos` saves it
    in their local library. A listen host uses the same mechanism as a guest. Dedicated recording/upload policy
    remains independent, and an already configured server recording is reused rather than duplicated.
  - **The game's offline play:** `--svm-play=Map --svm-record` records into the user's demos folder
    (`PlaySettings.DemoFolder`).

```json
"Demos": { "Record": true, "Folder": "demos", "Upload": true, "KeepLocalDays": 7 }
```

### Playback implementation

`DemoPlaybackTransport` feeds recorded packets into `ClientSession` up to the playback tick. Client replies
are discarded and acknowledged locally; receive silence cannot time out playback or fill the reliable resend
buffer. `DemoPlayer` runs the remote `PlaySession` in the normal scene, and `DemoClock` controls speed/pause.
Seeking processes about 30 ms of ticks per frame behind the seeking indicator. A checkpoint opens the
replicated world and restores the packet and channel sequences before resuming the original stream.

## File format (`.scctdemo`)

The format lives in `src/OpenScct.Net/Demo/DemoFile.cs`. It is little endian, and strings are UTF-8 with a 7-bit
length.

| Part | Contents |
| --- | --- |
| Magic and version | `SCCTDEMO`, format version (u16) |
| Header | game version, protocol hash, map content hash, map, game options, server name, start time (Unix ms), tick rate, net rate; version 3 appends the round UUID |
| Body | a Brotli stream of frames: `varint tickDelta, varint length, packet bytes` |
| Recorded views (version 2) | optional second Brotli stream; fixed 61-byte frames with tick, player/pawn identity, camera position/rotation/FOV, aim rotation, breath, accuracy and spy-first-person flag |
| Seek checkpoints (version 4) | optional independent Brotli blocks, each with the network continuation and the bootstrap packets for one tick |
| Index | first tick, last tick, packet count, raw bytes, players, and markers. Version 2 adds recorded-view counts and size. Version 3 adds the scoreboard. Version 4 adds the checkpoint ticks, continuation packet offsets, and block sizes. |
| Trailer | index length (i64), `SCCTDEND` |

The index is written when the recording finishes. A file without it (the server stopped mid-round) is refused as
"cut short".

Current writers use container version 4. Readers also accept versions 1–3. Scoreboards can be read directly
from the index and must match the header's round identity. Network packets and their protocol hash
are unchanged by the view extension. While recording, the server requests rendered views with `#demo-view 1`
and stops them with `#demo-view 0`. Updated rendering clients submit at most ten samples a second; the server
associates each with the submitting connection's current player and pawn, stamps its receive tick, validates finite
values, and caps acceptance at twenty samples per second. Views are stored only in the replay and never applied to
gameplay or sent to other live players. Backlogged reliable channels skip samples. Older clients and headless bots
do not supply them.

**As played** uses these camera positions, rotations and FOVs, including scope and remote-camera views.
It interpolates nearby samples and avoids blending across respawns or camera jumps. When samples are missing
or stale, it approximates the view from the recorded pawn data, behind the Spy or at the Merc's eyes. The server
timestamps samples on receipt, so network latency and the sample rate affect timing. The view data also stores
breath and accuracy. Weapon and gadget presentation uses the recorded equipment.

### File limits

Demo listing and playback bound strings (16 KiB), the index (16 MiB), players (4096), markers (250,000), packets
(four million) and expanded packet data (512 MiB). Packet sizes cannot exceed the transport's 2048-byte limit.
Playback decompresses incrementally without reserving memory from file-supplied counts, checks framing and tick
arithmetic, and requires the decoded packet count and byte total to match the index. Recorded camera views have
a separate one-million-frame limit (61 bytes per expanded frame), with no more than 4096 distinct players.
Existing version 1 recordings remain supported; files beyond these playback limits report an invalid-data error.

## Storage online

After a round, `OnlineServer` finishes the demo and hands it to `SubmitDemo`. On a trusted server with
`Demos.Upload` on, the master link then uploads it to the master's S3 bucket through a signed link, once the master
has stored the round's results. The master keeps it for 7 days: a bucket lifecycle rule deletes it, and the
master's database row expires (schema v6). The S3 links, endpoints, bucket setup and costs are in
[Online hosting → Demos](ONLINE.md#demos).

## Playback limits

- **First-person presentation uses spectator data.** You see through the player's eyes with their aim, and their
  body is hidden. Merc weapons are drawn using the recorded weapon attachment and observed firing/reload actions,
  including existing recordings. Exact owner-only weapon animation timing, HUD data and vision effects are not
  available in the replay stream.
- **Seeking back reloads the map.** Checkpoints shorten the replay that follows the reload. Presentation that
  started before the checkpoint, such as some sounds or particles, can differ after seeking.
- **No camera collision.** The third-person and free cameras can pass through walls.
- **High speeds can run slow.** At 4× with a low frame rate, the play scene's catch-up limit (5 ticks a frame) makes
  playback slower than asked.
