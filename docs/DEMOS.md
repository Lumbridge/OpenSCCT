# Demos

A demo is a recorded match that can be watched later in the game, the way CS2 demos work. It is not a video. The
server records the network stream that a hidden spectator would receive, and playback feeds that stream into an
ordinary client. The game then draws the match with its own maps, models and effects, so a viewer can watch any
player in first or third person, or fly a free camera.

## Recording (server)

- `ServerHost.StartRecording(DemoWriter, now, game?)` (`src/OpenScct.Server/ServerHost.cs`) opens a recorder
  connection: `NetDriver.OpenRecorderConnection` (`src/OpenScct.Net/NetDriver.cs`).
  - The recorder looks like a client whose control channel the remote opened.
  - Its packets go to the demo file instead of the transport.
  - Each packet counts as acked once written, so nothing is ever resent.
  - It never times out and is not counted against the send budget.
- Then the host does what a Hello and a Join would do: it sends Welcome (into the file), logs in a spectator
  controller named Demo (`?SpectatorOnly=true`, with one extra spectator place so no player loses one) and marks the
  connection joined.
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
  - **A game hosted from the menus:** Create a Session's **Record demos** (`OnlineSettings.HostRecordDemos`, saved
    as `host/record_demos`) gives `ListenHost` the user's demos folder as `DemoFolder`, so each round is recorded
    there like a dedicated server's (not uploaded: a listen host is not a trusted server).
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
  installed game. Otherwise the main menu says which version recorded it.
- **Ways in:**
  - The main menu's **Demos** page has two lists.
    - **Recent rounds:** the master's list, for signed-in players. Watch downloads the file into
      `user://demos/cache`, checks its SHA-256 and plays it. Rows recorded on another network protocol are greyed
      out.
    - **On this computer:** `user://demos` (kept) and `user://demos/cache` (downloads, deleted after 7 days), with
      Watch, and Keep for downloads.
  - `--svm-demo=path` opens a file at start.

### Keys

| Key | Does |
| --- | --- |
| Space | pause / play |
| Up / Down | faster / slower |
| Left / Right | 5 s back / on (with Shift: 30 s) |
| Click the bar | jump there (on a marker: 3 s before it) |
| C | first person, then third person, then free camera |
| [ ] or mouse wheel | previous / next player |
| 1 to 9 | watch that player |
| Right mouse (hold) | look round (third person orbit, free camera) |
| W A S D, Q / E, Shift | fly the free camera, down / up, faster |
| H | hide the bar |
| Escape | leave |

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
