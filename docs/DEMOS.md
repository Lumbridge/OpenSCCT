# Demos

A demo is a recorded match that can be watched later in the game, the way CS2 demos work. It is not a video. The
server records the network stream that a hidden spectator would receive, and playback feeds that stream into an
ordinary client. The game then draws the match with its own maps, models and effects, so a viewer can watch any
player in first or third person, or fly a free camera.

## Saving your own matches

Turn on **Record my matches** in the lobby's **Match demos** panel (or in Match settings → Game). Each
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

## Training data companions

New recordings also produce an optional `<demo-basename>.training.jsonl.gz` companion for analysis and training.
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
`Match-<round-id>`. The two-pending-round limit still counts rounds, not individual files; temporary server
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

## File format (`.scctdemo`)

The format lives in `src/OpenScct.Net/Demo/DemoFile.cs`. It is little endian, and strings are UTF-8 with a 7-bit
length.

| Part | Contents |
| --- | --- |
| Magic and version | `SCCTDEMO`, format version (u16) |
| Header | game version, protocol hash, map content hash, map, game options, server name, start time (Unix ms), tick rate, net rate |
| Body | a Brotli stream of frames: `varint tickDelta, varint length, packet bytes` |
| Index | first tick, last tick, packet count, raw bytes, players (id, name, team), markers (tick, kind, text) |
| Trailer | index length (i64), `SCCTDEND` |

The index is written when the recording finishes. A file without it (the server stopped mid-round) is refused as
"cut short".

## Watching (game)

- **How it plays:**
  - `DemoPlaybackTransport` (`src/OpenScct.Net/Demo/DemoPlaybackTransport.cs`) hands a `ClientSession` the recorded
    packets up to the tick the playback has reached, and drops whatever the client sends.
  - `DemoPlayer` (`game/Scripts/Play/Demo/DemoPlayer.cs`) runs that client through the remote `PlaySession` in the
    normal play scene.
  - `DemoClock` handles pause and speed (0.25× to 4×).
- **Seeking:**
  - Forward: the client is fast-forwarded, at about 30 ms of ticks per frame, behind "Seeking...".
  - Back: playback starts again from the beginning and fast-forwards (`DemoPlayer.Restart`). A delta stream has no
    other place to start from.
- **Checks before playing:** the demo must have the same replication protocol and the same map content hash as the
  installed game. Failures return to the demo library with the reason, preserving the current library view when
  playback has not started.
- **Ways in:**
  - The main menu's **Demos** page opens **On this computer**, with a separate **Recent online** collection.
    Cards show map, mode, length, date, server, players and storage status. Search finds maps, players, servers and
    local filenames; mode/storage filters and newest/oldest sorting narrow the collection. The layout uses one
    column on smaller screens and two on wide screens, with scaled text at high resolutions.
    - **Recent online:** the master's paginated list, for signed-in players. Filters apply to the displayed page.
      Watch downloads into `user://demos/cache`, checks size and SHA-256, then opens playback. Keep downloads a
      permanent copy without launching playback. A progress bar shows download/verification status; leaving the
      library cancels unfinished transfers and cleans up temporary files.
    - **On this computer:** `user://demos` (saved) and `user://demos/cache` (downloads, deleted after 7 days), with
      Watch and Keep for downloads. Saved copies take precedence over duplicate cached copies.
    - Incompatible protocols disable Watch with an explanation. Sign-in, loading, unavailable-server, empty,
      filtered-empty and file errors have their own guidance and relevant actions.
  - `--svm-demo=path` opens a file at start.

The playback dock includes Play/Pause (Replay at the end), restart, five-second skips, speed, player and camera
selectors, controls help and Back to demos. The timeline previews timestamps and named events; dragging commits
one seek on release. The dock stays visible while paused, seeking, hovered, dragging or using a selector/help.
After four idle seconds during playback it hides, and mouse or keyboard activity reveals it again. Explicit Hide
stays hidden until H restores it. Playback exits back to the demo library.

### Keys

| Key | Does |
| --- | --- |
| Space | pause / play |
| Up / Down | faster / slower |
| Left / Right | 5 s back / on (with Shift: 30 s) |
| Click or drag the bar | jump on release (on a marker: 3 s before it) |
| Home / End | restart / seek to the end |
| C | first person, then third person, then free camera |
| [ ] or mouse wheel | previous / next player |
| 1 to 9 | watch that player |
| Right mouse (hold) | look round (third person orbit, free camera) |
| W A S D, Q / E, Shift | fly the free camera, down / up, faster |
| H | hide / show the controls |
| ? | show / hide shortcut help |
| Escape | close help first, otherwise return to Demos |

## Storage online

After a round, `OnlineServer` finishes the demo and hands it to `SubmitDemo`. On a trusted server with
`Demos.Upload` on, the master link then uploads it to the master's S3 bucket through a signed link, once the master
has stored the round's results. The master keeps it for 7 days: a bucket lifecycle rule deletes it, and the
master's database row expires (schema v6). The S3 links, endpoints, bucket setup and costs are in
`docs/ONLINE.md`, under "Demos".

## Limits (first version)

- **First person shows only the view.** You see through the player's eyes with their aim, and their body is hidden.
  Their first-person weapon, HUD and vision mode are not drawn, because demos do not carry other players' owner-only
  data.
- **Seeking back costs a restart.** It reloads the map and replays from the start, so it takes longer the later the
  target is in the match.
- **No camera collision.** The third-person and free cameras can pass through walls.
- **High speeds can run slow.** At 4× with a low frame rate, the play scene's catch-up limit (5 ticks a frame) makes
  playback slower than asked.
