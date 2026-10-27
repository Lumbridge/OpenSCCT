# The Hub

The hub is an OpenSCCT addition, not part of the original game: a social space for up to about 50 players where
spies and mercs walk around with the full moveset, see each other's names, chat and, later, start matches (H2) and
play minigames (H3). Its rules are our own design and are written down in `spec/additions/hub.md`. This page says
how it is built, how to run it, and what it measured.

## Pieces

| Code | What it is |
|---|---|
| `tools/hubmap` | the hub map's layout (`layout.py`), its procedural art (`art.py`) and the generator (`generate.py`; reuses TestRange's world builder) |
| `game/Scripts/Hub/HubPadThemes.cs` | shows a pad's theme kit (`Apply(padId, themeId)`) and maps a map code to a theme (`ThemeForMap`) |
| `fixtures/hub`, `fixtures/hubmap/areas.json` | the generated SVMap (tracked, like TestBox) and its area manifest |
| `src/OpenScct.Server/Hub/HubActivities*.cs`, `HubLayout`, `HubScores`, `HubPlayProtocol`, `HubPlayState`, `HubPlayK` | the activities (H3): course time trial, aim range, emotes, jukebox, plaza ball, the score boards and their file |
| `src/OpenScct.Server/Hub` | `HubServer` (the session, with its voice relay; `HubServer.Admin.cs` moderation and admin commands, `HubServer.Social.cs` the player list lines and local chat), `SHubGameInfo` (the rules on top of SGameInfo), `HubClient` (a visit: lines, chat, name tags, the roster, signs, pads, queue, match tickets), `HubVoice` (a visit's voice), `HubVoicePositions`, `HubSigns` (server-driven world text), `HubPads` (match pads, queues, votes, watching), `HubMatches` (the pads' match sessions), `HubConfig` (the pads' data), `HubProtocol`, `HubK` |
| `fixtures/hubmap/hub.json` | the match pads' config: each pad's map, mode, team size, bots and vote choices; the countdown, spectator places and the match ports |
| `src/OpenScct.Game/Modes/PlayRules.cs` | the level service that turns damage and pawn attacks off (two gates in the ported code ask it) |
| `src/OpenScct.Net/Replication/NetInterest.cs` | `INetInterest` and `DistanceInterest`, the opt-in interest layer of the server pass |
| `src/OpenScct.Net/Voice/Proximity` | positional voice (falloff, occlusion, per-player volume and mute, the relay filter, `ProximityPlayback`) |
| `src/OpenScct.Bots/HubWanderBrain.cs`, `HubSoak.cs` | hub visitors for the soak test |
| `src/OpenScct.Server.Cli/HubRun.cs` | `svm-server --hub` and `--hub-bots` |
| `game/Scripts/Hub` | the Hub entry of the main menu (`HubScreen`), the visit in the game (`HubService`), a hub hosted from the game (`HubHost`), the hub HUD (`HubHud`, with `HubHud.Activities.cs` and the queue panel), the player list (`HubPlayerList`), the emote poses (`HubEmotePoses`), the hub's voice chat (`HubVoiceChat`), the world signs (`HubSignsView`), the pads' floor overlays and map pictures (`HubPadView`), the activities' world props (`HubProps`) and sounds (`HubAudio`) |
| `tests/OpenScct.Integration.Tests/Hub`, `tests/OpenScct.Net.Tests/InterestTests.cs`, `tests/OpenScct.Net.Tests/Voice/ProximityVoiceTests.cs`, `tools/hubmap/test_hubmap.py` | the checks |

## Running a hub

```
dotnet run --project src/OpenScct.Server.Cli -c Release -- --hub [--port 28886] [--max 50] [--name "My hub"] [--motd "Hello"]
    [--master https://master.example.org --key <invite key> [--server-key <trusted key>] [--region EU] [--unlisted]]
svm-server --hub --config svm-hub.json     # name, port, maxPlayers, motd, maps and master (with region) from the file
```

A hub listens on UDP 28886 unless `--port` or its config file names another (`HubK.DefaultPort`): its own port, so
one PC can host a hub and a game (28888) at once. `svm-server --hub` keeps its bans in `hub-bans.json` next to the
server, beside `hub-scores.json`.

**Listing.** A hub with a master heartbeats like a game server (`MasterLink`, `HubServer.Info`), with `kind: hub`
and its `region`. The master lists hubs apart: `GET /api/v1/servers?kind=hub`; a list asked without `kind` (every
older game) gets game servers only. A hub answers server queries (the ping) and the master's reachability probes on
its game socket, so the master can say whether players reach it directly. The game's server browser skips a saved
address that turns out to be a hub (`ServerInfo.IsHub`, flag bit 4).

**Official hubs.** A dedicated hub with a trusted-server key (`serverKey`, in the master's `TrustedServers`) is
"Official" in the list, and official hubs come first. Each one names its region (`EU` now; a US hub later is a second
`svm-server --hub` on a US machine with `"region": "US"` and its own trusted key), and the list shows each hub's ping,
so a player picks the nearest. Players' own hubs are listed after them.

In the game: main menu, Hub (a menu page like the others: "Your visit" with a name and the side to arrive as, "Hubs"
with the master's hub list (name, region, Official / Dedicated / Player, players, ping; official first, then lowest
ping), "Join a hub by address", "Host a hub" with "Show it in the hub list"; Refresh asks the master again), then either

- **Join from the list**: pick a hub and Join (or double-click). The game asks the master the way in
  (`HubService.JoinListed`) and tries, in turn, the host's own network address (same router), its public address and
  the master's relay (a NAT punch is not tried; the relay covers those hosts). Pad matches go to the hub's public
  address; back from a match the game asks the master again.

- **Host**: the game opens a hub itself (`HubHost`: the dedicated server's `HubServer` on a thread of
  the game, UDP port 28886 on every interface) and joins it on 127.0.0.1. With a master set and "Show it in the hub
  list" on (`[hub] listed` in online.cfg, on by default) it is listed as a player hub; others join it from the list
  (through the relay when this PC cannot be reached) or by this PC's address (LAN IP, or the public IP with UDP 28886
  forwarded; Windows asks once to let OpenSCCT through its firewall). A listed hub's pad matches register on the
  master too (unlisted), so they are reached the same ways, the relay included. The hub closes when the host leaves it
  (O), which drops everyone in it.
- **Join**: the hub's address (LAN or direct IP; the port defaults to 28886). In the hub: J play spy, K play merc, N back to the plaza, Enter or F5 to chat, F6 to chat to your side (Tab while
typing goes everyone, your side, near you; `/l text` says it near you; Escape cancels), G emotes, U use (start an aim
range round at the firing line, next jukebox record, ready on a pad), X stop your run or round, V on a pad to vote for
its next match, H on a pad to watch its match, P the player list, O leave. The hub keys sit on keys the original's default layouts leave free. Names float over the other players' heads in their side's colour. Voice
works as in a match (the options' Voice page: push to talk or voice activation; Caps Lock to talk, Y to your side
only, F8 mutes the microphone), but each player is heard only near him: see "Proximity voice" below. The play scene
needs the converted character content to draw pawns; without it the hub still plays.

The map is regenerated with `python -B tools/hubmap/generate.py` (and checked with `--check`; `python -B -m pytest
tools/hubmap -q`). Nothing in it comes from the game.

## The map

A night rooftop compound over a city, in OpenSCCT's colours (night-vision green for spies, amber for mercs). All of
it is generated: the geometry by `layout.py`, every texture by `art.py` (numpy patterns with seeded noise, signs drawn
with JetBrains Mono Bold from `game/branding/fonts`, SIL Open Font License 1.1). No texture, mesh or text comes from
the game or from Ubisoft, and no third-party art is used. The folder is about 3.3 MB (world.glb 2.0 MB, 125 textures
1.0 MB, collision 0.1 MB).

- **Hall**: the walls are a painted skyline (towers with lit windows, red aircraft lights) above a parapet with a
  green neon line; the ceiling is a starry sky (both fullbright); the floor is dark roof concrete. A sparse cool fill
  light, zone ambient 34 and a light distance fog (navy, 3000 to 19000 uu).
- **Plaza** (spawn): granite pavers with the OpenSCCT ring inlaid round the fountain (water in the basin, neon on its
  lip and up the column), a four-sided "OPENSCCT HUB" sign hung over the lookout, four neon pylons (green west, amber
  east), eight benches, a SPIES banner on the west side and a MERCS banner on the east, and floor arrows to every area.
- **Garden** (traversal): the station kit on striped turf, a label on every station (block faces and turf paint),
  ladder rungs, zip cables, six trees (trunks collide, canopies are above any jump), a "TRAVERSAL GARDEN" wall sign.
- **Match pads**: four 1024 courts, each railed (16 thick, 96 high, vaultable LGs on both faces, a 256 opening in the
  middle of every side) with 64 corner posts, a sign gantry on its north side (a board with "PAD n / next match"
  facing south and north), wooden bleachers with amber step lights north of Pad3 and Pad4 and east of Pad2 and Pad4,
  a hanging "MATCH PADS" sign facing the plaza, and a theme kit per pad (below).
- **Aim range**: a wooden booth with a counter on the firing line (LG on the shooter side) and five numbered stalls
  under a canopy with amber neon, painted targets on the berm, distance marks every 512 uu (5 M to 20 M), a
  leaderboard board at the north end facing the plaza.
- **Movement course**: a cyan-edged track along y 1920 with numbered gates and floor arrows naming the move: 1 vault
  wall (96), 2 climb block (208), 3 fence (376), 4 hand over hand bar (277, between pillars), 5 tower (384: pipe on the
  west face, ladder on the south face), 6 zip line (TestRange's 1760 run and 320 drop) to 7 the finish deck (160).
  A start line, a "MOVEMENT COURSE" sign over the route and a leaderboard board near the start facing the plaza.
- **Lounge** (new area, south-west): a carpet, two sofas with coffee tables and two side sofas facing a big screen on
  the west wall with a scrolling ticker, a bar with stools and magenta neon, planters, warm light, "LOUNGE" signs.
- **Rooftop dressing**: a water tower with "OPENSCCT" on it and a mast (north-east), air-conditioning units, a helipad
  (south-east).

### Anchors for the other hub features (fixtures/hubmap/areas.json)

| Field | Where | What |
|---|---|---|
| `pads[].signAnchor`, `signYaw`, `signSize` | MatchPads | the centre of the gantry board's south face (2 uu in front of it), the way it faces (49152, -Y) and its size; draw the pad's live sign there |
| `pads[].centre`, `pads[].theme`, `railGap` | MatchPads | the court centre, the pad's default theme, the width of the rail openings |
| `themes`, `defaultTheme` | MatchPads | the theme ids with their titles and map code prefixes |
| `leaderboardAnchor`, `leaderboardYaw`, `leaderboardSize` | AimRange, Course | the centre of the leaderboard board's face, its facing (AimRange 0 = +X, Course 16384 = +Y), its size |
| `stalls` | AimRange | the five stalls' y ranges along the firing line |
| `route`, `checkpoints[]` (`id`, `kind`, `at`) | Course | the track's y range and the seven checkpoints (CP1 vault ... CP7 finish) |
| `screenAnchor`, `screenYaw`, `screenSize`, `musicAnchor`, `seats` | Lounge | the big screen's face, a point for music, the sofa centres |
| `signCentre` | Plaza | the centre of the hanging sign |

### Pad themes

Each pad has one render-only kit per theme: the court paint (lines, centre circle, the theme's title), a neon border,
skins over the shared rails and corner posts, props on the posts, and the theme's own coloured light baked into the
kit only. Collision is the same under every theme. The kits are gameplay mesh nodes named
`HubTheme_<pad>_<theme>_<material>`; the map shows each pad's default (`pads[].theme`) and hides the rest.
`HubPadThemes.Apply(padId, themeId)` shows another one on the current play scene's map (or remembers it until
`ApplyChosen(map)`), and `HubPadThemes.ThemeForMap(mapCode)` picks the theme for a map code by its first four letters.

| Theme id | Title | Maps | Look |
|---|---|---|---|
| `hub` | OPENSCCT | any other map | slate court, green lines, green neon, beacons |
| `polar` | POLAR BASE | Pola | ice court with blue lines, frosted rails, ice neon, antenna masts, cold blue light |
| `steel` | STEEL SQUAT | Stee, Fact, Ware | concrete court, yellow lines, corrugated rails, sodium lamp posts, warm sodium light |
| `aquarius` | AQUARIUS | Aqua, Offs | deep teal court, teal panel rails, pipes and valves, aquamarine light |
| `lab` | KRAUSER LAB | Krau, Clar, Deft, Ziop | white lab tiles, hazard-striped posts, green beacons, green-white light |
| `museum` | MUSEUM | Muse, Bank, Pana, Cine, Club, Sanc, Sche | burgundy court, gold lines, wood rails, marble plinths with gold, warm light |

Defaults: Pad1 polar, Pad2 steel, Pad3 aquarius, Pad4 lab. To add a theme: add an entry to `THEMES` in
`tools/hubmap/layout.py` (title, map prefixes, court colours, neon, rail and post tile kinds, light hue and
saturation, a props style in `kit_props`), add its tile kinds to `TILES` in `art.py` if new, add the id to
`HubPadThemes.ThemeIds` and its prefixes to `MapThemes`, regenerate. Each theme adds about 20 nodes and 15 KB.

## Scaling: interest management

A normal match has at most eight players and replicates every other pawn at its full schedule. With 50 players that
would be about 7 KB/s down per client (measured: 6.7 KB/s with the distance bands alone, all 49 others near the plaza)
and a server pass far over its frame. The hub opts in to three changes; a normal match is unchanged
(`NetDriverSettings.Interest` null, the shared send budget):

- **Per-connection budget.** The game's one shared send budget would split one client's rate between 50 clients; the
  hub uses stock UE2's budget per connection.
- **Distance and rank bands** (`DistanceInterest`, rules 12 to 14 of the spec): the 4 nearest other pawns within
  2500 uu replicate as in a match; the rest of a crowd, and everything from 2500 uu, updates at most every 0.5 s, from
  4500 uu every 0.75 s, from 6500 uu every 1 s; beyond 7000 uu nothing new opens, and open channels close past
  8000 uu. The players' replication infos and the game state update at most every 0.5 s.
- **Proxy samples.** The decoded position sample schedule of other pawns is kept for the nearest 4; others are spaced
  10x, 14x and 18x by band.

## The soak test

Automated (`HubSoakTests`, SimLink A, virtual clock, Debug): 50 bots join ten a second, wander the hall (half their
walks end near the plaza), chat and change side; after 15 s every bot must be in and playing, the match still running,
and the mean download per client (from 8 s on, server side) under 5 KB/s. It takes a few minutes in a Debug build.

The long soak, two processes on one machine over UDP on localhost (Release):

```
svm-server --hub --port 28965 --loopback-only --seconds 330 --report 30 --report-file hub-server.log
svm-server --hub-bots 50 --connect 127.0.0.1:28965 --loopback-only --threads 8 --seconds 320 --report 60
```

Measured 2026-10-25 on the development PC (32 logical cores), 5 minutes with 50 bots in (25 spies, 25 mercs, side
changes and chat going on), the server's numbers per 30 s from its report:

| What | Value |
|---|---|
| Players | 50 of 50 in for the whole run, none dropped |
| Server tick time (60 Hz) | p50 0.56 to 0.79 ms, p99 14 to 26 ms, max 75 ms (whole run: p50 0.68, p99 17.6 ms); the p99 is the replication pass, every third tick |
| Server CPU | ticks busy 26 to 31% of the wall time; the process used 38 to 41% of one core (the loop's waits and the socket threads included) |
| Server memory | working set 116 to 142 MB, managed heap 58 to 71 MB; garbage collections only while the crowd joined |
| Download per client | 2.9 to 3.0 KB/s (server side, packet overhead included; the bots measured 3.0 KB/s mean, 3.0 KB/s max) |
| Upload per client | 5.4 KB/s (the move protocol of a bot that never stops turning) |
| Other pawns each client had | 49 (the hall is small enough that the crowd stays within the keep distance) |

For comparison, a four-player match uses 1.1 to 2.3 KB/s down per client (docs/NETCODE.md, "Bot matches"); this is the
worst case for the hub, every other player in view, and the plaza crowd at one spot. On the way:

| Change | Download per client | Server pass |
|---|---|---|
| Distance bands only, the game's shared send budget | the crowd starved (one client's rate shared by 50) | |
| Distance bands, per-connection budget | 6.7 KB/s | |
| Plus the nearest-pawn rank and spaced proxy samples | 3.4 KB/s | first 90% of the wall time (the nearest set was rebuilt on every question), then 21% once fixed |
| Final bands (4 nearest at the full rate, 0.5, 0.75 and 1 s) | 3.0 KB/s | |

The UDP run is all on one machine, so the link has no loss or delay; the bots ran in a second process on eight threads
(each with its own walk graph).

## Other checks

- `HubTests` (SimLink, virtual clock): players join on the side they ask for and spawn at the plaza at once; a side
  change gives a merc pawn on the server and on the client; chat reaches the others with the sender's name; every
  client has every other player's name tag; ten hits of 50 leave a pawn untouched; a leaver does not end the match and
  a late joiner plays and gets the message of the day. Voice: with a spy talking (a 440 Hz tone), a merc 1500 uu
  away hears him at the falloff gain (heard level 0.61 of the tone for a gain of 0.61), a spy 4000 uu away is sent
  nothing (the relay counts each copy out of range) until he walks up to 1000 uu; the merc's own volume for the spy
  halves the mix, and his mute stops the server sending.
- `InterestTests`: far actors are culled and close again past the keep distance; spacing by band; without interest
  the driver is unchanged. `ProximityVoiceTests`: falloff, occlusion, volume, mute, smoothing, the relay filter with
  its hysteresis, 50 players on a line; the relay with the filter (Open mode, near and far listeners) and without it
  (unchanged); the playback's falloff gain on the decoded level, the low-pass through a wall, silence without a
  position, flat again once released.
- `pwsh tools/ci/godot-smoke.ps1 -Tests hub` (headless): a hub server on loopback and a merc guest, then the main
  menu's Hub button, Join, the play scene on the hub map with the hub HUD, the guest's chat line, his name tag on
  screen, the guest's voice (a tone) heard by the player, a side change to merc and O back to the main menu (passed
  2026-10-26: `pawn=SPawnAttaque_1Mesh side=spy chat=3 tags=1 voice=heard rms=0.025 gain=0.12 relayed=57
  switched=SPawnDefense_1Mesh tagsShown=1`; the tone's level is 0.212, times the gain 0.12 at the distance between
  the two plaza rings). Pawns are drawn only with the converted characters, which the worktree has
  not; check that in the main checkout.

## Match pads (H2)

Four pads east of the plaza (`MatchPads` in `fixtures/hubmap/areas.json`, Pad1 to Pad4) each queue for one match,
in the spirit of a park's courts: walk onto a pad and you have "got next", the sign over the pad shows who is waiting,
and the match starts when enough players stand there. The rules are in `spec/additions/hub.md` ("Match pads").

**Config.** `fixtures/hubmap/hub.json` (found beside the hub map; without it the same four pads are built in):

```json
{"countdownSeconds": 10, "matchPorts": [28890, 28897], "spectators": 4, "matchHost": "optional address players use for matches",
 "pads": [{"id": "Pad1", "map": "SteeE", "mode": "Story", "teamSize": 2, "bots": "off",
           "choices": [{"map": "PolaE", "mode": "Story"}, {"map": "AquaE", "mode": "Story"}]},
          {"id": "Pad3", "map": "MuseT", "mode": "Story", "teamSize": 3, "bots": "fill", "minPlayers": 2}]}
```

`map` is the map code; `mode` is Story, DiskHunt, Deathmatch or MercDeathmatch and is checked against the map
catalog's modes for that map (a mode the map does not play becomes `DefaultMode`; a map the server does not have
turns the pad off). `teamSize` is per side (Merc Deathmatch: players in the free-for-all). With `"bots": "fill"`
and `minPlayers` the countdown starts with that many ready people and bots take the empty places. The pads' places come
from the area manifest: each pad's `lo`/`hi`, and `signAnchor` [x, y, z] and `floorZ` when the map gives them (else
the sign floats 360 uu over the pad's centre). `HubPads.Reconfigure` changes a pad's match while the hub runs;
clients are told and their pad hook runs.

**Votes and rotation.** A pad's `choices` (map, mode, optional `teamSize`; bots and minimum players are the pad's) are
the other matches it can play; its own match is option 1 on the sign. A player on the pad presses V to move his vote
on to the next option (`hub vote <pad> -1`); while the pad is idle or waiting the most votes pick its match (a tie
keeps the current one), and the sign lists the options with their votes. Votes end with the match they were for;
with no votes the pad moves on to its next option after each match (a rotation). Choices on maps the server lacks
are dropped, and their modes are checked as the pad's.

**Watching.** H on a pad whose match is in its lobby or being played asks `hub watch <pad>`; the hub answers with an
offer as a spectator (team 255, the match's password) and the player joins it as one (`?Team=255`), then comes back
beside the pad at its end as the players do. Each match has `spectators` places (default 4); watchers do not keep a
match open. The pad's sign shows the match in play: the score ("Playing: Spies 2 - Mercs 5"), the time left and how
many watch (`IHubMatch.Live`, written by the match's thread).

**Through the master.** With a master link, each pad match registers on the hub's master as an unlisted game server
(`HubMatchLauncher.Master`), and the offer carries its master id as a trailing field
(`hub match ... <host> <masterId>`). The game then asks the master the way in, as for a listed server (its address,
punch and relay routes, and a signed-in player's ticket for the match), and falls back to the hub's address when the
master does not answer. The hub holds a start's offers until the match's master entry is known, at most 4 s.

Defaults: Pad1 Enhanced Steel Squat Story 2v2, Pad2 Enhanced Polar Base Deathmatch 2v2, Pad3 Museum 3v3 Story 3v3
(bots fill, 2 people start it), Pad4 Enhanced Club House Merc Deathmatch 4 players (bots fill, 2 people start it).

**Flow.**

1. The server checks each tick which pad every living pawn stands on (its location inside the pad's rectangle, up to
   400 uu over the floor). Stepping on queues the player; stepping off or leaving the hub takes him out.
2. The lineup is the queue in order: his hub side when it has room, else the other side, else "next in line".
3. Each player in the lineup presses U on the pad to ready up (U again unreadies; the sign shows "Ready n/m"). With
   the lineup at the pad's need and everyone in it ready, a 10 s countdown runs (the sign and the HUD count down);
   anyone stepping off below the need, or unreadying, stops it.
4. At zero the hub opens the match session (`HubMatchLauncher`): a dedicated `OnlineServer` on its own thread of the
   hub's process, on the first free UDP port of 28890-28897 (clear of the hub's 28886 and the game's 28888), the pad's
   setup, bots per the pad, a fresh password as the join token, players ready on joining, launch once everyone came
   (or 20 s after the first did). Each lineup player gets `hub match <pad> <port> <team> <token> <map> <mode> <host>`.
   Players on the pad who were not in the lineup keep their places, and the pad goes on queueing (several matches of
   one pad can run at once).
5. The client leaves the hub (a hub this PC hosts keeps running) and joins the match through the normal online path
   (`OnlineService.JoinFromHub`: the lobby screen for a moment, then the round).
6. When the round is over (the results, after the 10 s end banner), or the player leaves or loses the match, he joins
   the hub again with `?HubReturn=<pad>` and is put just off that pad's south edge 1.5 s after he spawned. A note on
   the HUD says why ("The match is over. Back at the pads.", "Could not join the match: ...").
7. The session closes once its players left after the round (or 45 s after it ended), or when nobody came within
   45 s. A start that fails (no free port, map missing) tells the lineup why and retries after 10 s.

**Signs.** `HubSigns` is a generic world text: `hub.Signs.Set(id, position, text, rgba, scale)` and
`hub.Signs.Remove(id)` on the hub's thread (text up to 360 characters, `\n` breaks lines, colour 0xRRGGBBAA). A player is
sent a sign once his pawn is within 7000 uu of it (the interest layer's open distance), its changes while within
8000 uu, and its removal past that, four times a second. The client draws each as a `Label3D` billboard in Inter
with a dark outline, fading out from 60 m. The pads use ids `Pad.<padId>`; the minigames' leaderboards
(`PublishBoard(id, text)`) are meant to map onto `Set` with their own ids.

**Client.** `HubClient` keeps `Signs`, `Pads` (each pad's map code, mode, team size, state and lineup), `Queue` (the
player's own line) and `Offer`. `HubPadView` lays a translucent overlay with a border on each pad, coloured by state
(white idle, yellow waiting, green counting down and pulsing, cyan starting, grey off), and floats the map's own
picture from this install's catalog (the host page's map list picture, `MapCatalogEntry.PreviewImage`) over the sign.
`HubPadView.OnPadConfigChanged(padId, mapCode, mode)` runs when a pad's match is first known or changes: the hook for
the pads' map themes. The hub HUD shows the queue panel top centre while on a pad ("Queued on Pad 2 as a merc: 3/4,
waiting for 1 merc", "starting in 7 s", "this match is full; you are next in line (#1)").

**Trying it.** Two PCs: one hosts (main menu, Hub, "Host", or `svm-server --hub`), the other joins by
address; both walk east from the plaza onto Pad 1 (for a quicker check, set Pad1's `teamSize` to 1 in hub.json). On
one PC: two game instances (the second with `--svm-background` if it should stay out of the way), both joining
127.0.0.1. The hosting PC needs UDP 28886 and 28890-28897 open for players who reach it directly (not through the relay); set `matchHost` when
players reach the hub through an address the hub cannot see (a relay or a DNS name).

Checked 2026-10-26 with a headless diagnostic (a hub on loopback UDP, two visitors put on Pad1 at 1v1): both queued
(the second as a merc, the spies being full), the countdown ran, the match session opened on 28890, both joined it,
travelled to SteeE and played the round; when they left, the session closed and the hub logged it; a visitor
joining back with `?HubReturn=Pad1` was moved beside the pad. Not checked in a windowed game yet: the signs, the
overlays, the pictures and the HUD panel are built but unseen.

## Proximity voice

The hub's voice is the match's voice chat (docs/VOICE.md: Opus frames over the game's transport, the server relay,
the jitter buffers, push to talk and voice activation) with the positional module of `OpenScct.Net.Voice.Proximity`
on top. A normal match is unchanged: the relay's filter and each talker's spatial mix are null unless the hub sets
them.

- **Mode.** `SHubGameInfo.StartHub` sets the voice mode to Open, so spies and mercs hear each other (the team channel
  still reaches the talker's side only).
- **Server.** `HubServer` wraps its transport in a `VoiceTransport` and runs a `VoiceRelay` over the stock directory
  (`NetDriverVoiceDirectory` with the SBase links) with `VoiceRelay.Proximity` set: after the stock routing allows a
  listener, `ProximityRelayFilter.CanHear` must too (3000 uu, 3400 while the pair is in range), or the copy is not
  sent (`VoiceRelayStats.OutOfRange`). Positions come from `HubVoicePositions`: each living pawn's view point by
  talker id (PlayerID plus one), rebuilt once per world tick. A player who leaves is forgotten when the roster drops
  him. Mutes are the listeners' own (their Join packets), as in a match. `HubSettings.Voice` turns it all off;
  `HubSettings.ProximityVoice` holds the distances. The hub's status line adds the voice counters.
- **Client.** `HubClient` wraps its transport for voice; `HubVoice` keeps a `VoiceClient` connected, names the
  talkers from the replication infos, keeps the player's volume and mute for each by name, and every frame runs
  `ProximityPlayback`: for each talker, `ProximityListener.Update` from the listener's ear to the talker's pawn (the
  same `HubVoicePositions`, on the client's world), occluded when the map's BSP blocks the line
  (`PhysicsLineOfSight`, as relevancy uses), with the player's volume and mute for him. The mix goes to
  `VoiceTalker.Spatial`, which the playback applies to the decoded samples: the gain (it holds the player's volume,
  so it replaces it) and a one-pole low-pass at the mix's cut-off (`ProximityVoice.LowPassCoefficient`; none in the
  open). A talker with no pawn is silent.
- **Game.** `HubVoiceChat` (a child of `HubService`) owns the microphone and playback nodes (`VoiceAudio`, one
  generator per talker on the voice bus), reads the voice keys (not while the chat line is open), takes the Voice
  page's settings and passes the play scene's camera as the ear. The hub HUD shows the voice overlay top right (who
  is heard and at which volume, "Your microphone is muted").

## Activities (H3)

Things to do while waiting, server-authoritative (rules A1 to A12 of the spec). `HubServer` makes a
`HubActivities` (unless `HubSettings.Activities` is off) and ticks it after the host's tick; it reads where things
are from the map's area manifest (`HubLayout`) and keeps the boards in `HubScores` (`svm-server --hub` writes
`hub-scores.json` next to the server; with no `ScoresPath` the boards last for the session only).

| Activity | How it plays | Board |
|---|---|---|
| Spy assault course | step out of the green start gate: the clock runs (top middle of the HUD) until the white finish, through every amber checkpoint in order (the next one pulses); a fall into the pit puts you back on the last deck, the clock still running | fastest times |
| Merc aim range | U at the yellow firing line: a 30 s round, two pop-up targets at a time downrange (yours red, other rounds grey); 10 per body hit, 25 per headshot, plus up to 50 for accuracy | best round scores |
| Plaza ball | walk into the orange ball to kick it; into the west or east goal scores for the last kicker | most goals |
| Emotes | G then 1 to 8, or `/wave` in chat: your character does it (a pose laid over its animation) and the caption shows under your name, for everyone within 4500 uu; moving ends it | |
| Jukebox | the box in the plaza's north-east corner; U next to it changes the record for everyone | |

- **Hits without damage.** The hub server's level gets a hit-zones service (`HubRangeHitZones`) that wraps the stock
  trace: after the world and pill passes it tests the raised targets and returns the nearer hit. The rifle's own
  `TraceFire` then treats the target as any non-pawn actor and calls its `TakeDamage`, which only scores (when the
  level's `PlayRules.RangeTargetsTakeHits` is on, as in `PlayRules.Hub`). No pawn is touched, so no damage reaches
  anyone. Targets are not spawned or replicated: the clients draw them from `hubplay target` lines.
- **Boards.** Every board text goes through `HubActivities.PublishBoard(id, text)` (ids `CourseBoard`, `RangeBoard`,
  `BallBoard`), which sends a `hubplay board` line to everyone (and to each joiner) and raises
  `HubActivities.BoardPublished` for the world signs. The HUD's board panel shows the nearest board within 2200 uu
  only when the world has no sign for it (a board is read once). A board stands at its area's `leaderboardAnchor`
  when the manifest has one.
- **HUD.** `HubHud.Activities.cs`: the course clock and checkpoints or the round's clock and score, notices (hits,
  personal bests, records, goals), the board panel, a prompt of what U does here, the emote menu; Inter and
  JetBrains Mono, the menus' fonts (OFL, `game/branding/fonts`). Emote captions go under the name tags.
- **World.** `HubProps`: the gates (rings and posts), the firing line, the targets (they flip up and down), the
  ball (run on between the server's updates with the same friction), the goals and the jukebox, all plain shapes.
- **Sound.** `HubAudio`: a small synthesiser of our own makes the jukebox's four tracks (pad, bass, arpeggio and
  hi-hat over four chords: "Plaza Drift", "Night Shift", "Rooftop Run", "Static Lounge"), on the music bus and faded
  by the distance to the jukebox, and the chimes (start, checkpoint, finish, personal best or fanfare, hit,
  headshot, round over) on the flat effects bus. No recorded audio: nothing to license.
- **Checked** (2026-10-26, a scratch headless run over SimLink with a spy and a merc client, nothing committed):
  the spy's run started on leaving the gate, the finish refused him with checkpoints missing, then 3/3 checkpoints
  and a 6.00 s run, personal best, first place, the fanfare and the board on the other client; the merc's round with
  44 rifle shots through `ServerFireWeapon` aimed at the raised targets scored 44 hits and 15 headshots (715 points),
  neither pawn lost health; the merc's wave reached the spy 300 uu away; U at the jukebox changed the track on the
  other client; a kick moved the ball on both, a ball sent into the west goal scored; the boards came back from the
  scores file on a second start.

### The assault course (layout version 3)

The course south of the plaza is a raised route of decks over a pit (the course floor), along y 1920 from x 3072 to
9472 (`tools/hubmap/layout.py`, "Spy assault course"). Each deck reaches the next only through its piece, and the
decks are 384 wide with drops on both sides, so nothing can be run past or round:

1. climb onto deck A (a 208 lip, hang and climb) from the start apron;
2. a fence (376) across deck A's full width;
3. hand over hand across a 640 gap: the bar 277 over the decks, from 192 over deck A to 192 over deck B;
4. ledge shimmy across a 512 gap: a wall on the north side with its lip 264 over the decks, and a soffit 112 over
   the lip so the wall top cannot be climbed and walked;
5. a split jump chimney: deck C narrows to the 192 corridor between two walls, up to deck D's lip 408 over it;
6. a wall jump to deck E's lip 408 higher;
7. the zip line (TestRange's 1760 run, 320 drop) down to the finish deck, and a ladder down from it.

Each checkpoint stands on a landing deck at its height (radius 192, 48 deep: `HubGate.Depth`; areas.json's
checkpoints carry `radius` and `depth`), so it counts only once its piece is behind you. A running spy whose centre
goes under `fallZ` (96, the manifest's course area) once past the first checkpoint fell into the pit: the server
puts him back on the last passed checkpoint's deck (`pawn.SetLocation`, as pad returns are; the client follows the
server's position correction), the clock still running, with `hubplay run fall <falls> <checkpoint>`, a notice, a
chime and the falls on the HUD's clock. The course board is cleared when the layout version changes
(`courseLayout` in `hub-scores.json`): old times were set on another route. The heights are the garden's kit
heights; the route is laid out by numbers and needs walking with the full moveset (the chimney's split jump up to a
408 lip and the shimmy under the soffit most of all).

### Emotes as poses

The character packages hold no emote clips, so each emote is a short program of bone turns (`HubEmotePoses`) laid
over the pawn's own animation: `SimPawnView.Overlay` (an `IPawnPoseOverlay`) gives turns about the pawn's frame
(forward, right, up), the view turns them into skeleton space and `ChannelPoseApplier.ExtraTurns` applies them parent
first after the blend and the aim; `ExtraShift` lowers the hips for sit. Each blends in and out over 0.25 s. The
server ends an emote for everyone near when its player moves faster than 60 uu/s or jumps (`hubplay emote <id> -`).
The angles are our own, tuned by eye, and want a look in the game.

## Accounts and moderation

- **Tickets.** Joining a listed hub, a signed-in player's game asks the master with his session, and the answer carries
  his ticket for the hub; the visit sends `?SvmTicket=` and `?SvmId=` (`HubClientSettings.Ticket`, `InstallId`).
  `HubServer` checks it with `TicketGate` (signed by the master's key, for this hub's master id, not expired, each
  ticket once): a signed-in player comes in under his account's name, a guest of a hub on a master as `Name#tag` (as on
  the online servers). `HubSettings.RequireAccounts` refuses guests. A hub hosted or typed in by address has no ticket
  to give; its visitors are guests.
- **Bans.** `HubSettings.Bans` (a `BanList`; `svm-server --hub` keeps `hub-bans.json`) by account, install id,
  address or name; the master's account bans kick (`MasterLink.KickAccounts`).
- **Admin portal.** The hub's master link reports its signed-in accounts and players (name, side, ping, account) to
  the portal's server page; a trusted hub (a `serverKey`) takes the portal's commands through `HubServer.Execute`:
  `help`, `status`, `players`, `say <text>`, `kick <id|name> [reason]`, `ban <id|name> [minutes] [reason]`,
  `unban <account|install id|address|name>`. Closing the hub is refused.

## Player list and chat

- **Player list (P).** Both sides' players (signed in ✓ or guest, ping) from `hub who` lines the hub sends every 3 s
  (`hub gone` when one leaves); for each other player a voice volume slider (0 to 200 %), a voice mute (`HubVoice`, kept
  by name for the visit; the relay stops sending a muted talker) and a chat mute (`HubClient.MuteChat`, this game only).
  The pointer is freed while it is open.
- **Local chat.** Tab while typing cycles everyone, your side, near you; `/l text` says a line near you. The client sends
  `hub local <text>`; the hub delivers `hub local <playerId> <team> <text>` to the players within 3000 uu of the
  speaker (`HubK.LocalChatRange`, the voice's hearing range), at most one line per half second. The chat box marks
  them "(near)".
- **Bubbles.** Any chat line a player says shows under his name tag for 6 s (two short lines at most).

## Open items

- Activities: the emote poses and the assault course (layout version 3) were built by numbers and need a look and a
  walk in the game; the HUD, props and music still need a look in a windowed game with the converted content.
- Match pads: the pad flow is checked headless only (see "Match pads"); the windowed look (signs, overlays, map
  pictures, the queue panel) and a full round to the end banner and back are still to see; so are votes, watching and
  joining a match through its master entry. Matches run in the hub's process (a separate `svm-server` per match would
  need a launcher of its own).
- The pad themes follow each pad's map (`HubPadView.OnPadConfigChanged` -> `HubPadThemes.Apply(pad, ThemeForMap(map))`,
  and the chosen kits go on once the hub map is built); maps with no kit of their own show the neutral `hub` theme.
- The course and the new rails, benches and props were laid out by numbers (the kit heights) and drawn in an
  offscreen render; walking them with the full moveset in the game is still to check.
- The replication pass is one block of about 6 to 12 ms every third tick at 50 players; spreading it over the ticks
  would flatten the p99.
- A hub hosted in the game gives its own host no ticket (he joins on 127.0.0.1, not through the master), so the host
  is a guest in his own hub.
- The play scene keeps the side the player arrived as in its settings. After a side change the pawn, its kit, the
  view and the HUD page follow the new pawn, and the key layout is swapped for the new side's (PlayRoot.FollowSide);
  anything else the scene chose from the settings at load still needs a check with the converted content.
- The online server keeps its own ticket check (`OnlineServer.CheckTicket`); it can move onto `TicketGate` too.
- Proximity voice was heard in the headless smoke and the tests; a windowed check with two real microphones (the
  falloff and the muffling by ear) is still to do.
