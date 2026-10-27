# Online play

How players find, join and play each other: the online session (lobby, travel, round, results), the master server
(list, NAT punch introducer, relay), the dedicated server, hosting from the game, and exactly what to deploy on a
VPS. The game's own network model (packets, replication, moves) is in `docs/NETCODE.md`; this file is the layer
above it. Reloaded's behaviour that this replaces is in `spec/reloaded/FEATURES.md` (rl:net.*, rl:qol.lobby) and
`spec/native/reloaded-features-from-code.md`; the original's game flow is in `spec/game/`.

Nothing here ever talks to the original game's or the Reloaded patch's master servers. Every address comes from
our own settings.

## Pieces

```
                      HTTPS (list, heartbeat, join)               UDP 28880 introducer, 28881 relay
  game client  ───────────────────────────────►  svm-master  ◄──────────────────────────  hosts
  (browser, lobby)                                (VPS)        heartbeats, punch registration, relay hello
        │  UDP 28888: direct, after a NAT punch, or through the relay
        ▼
  svm-server (dedicated, VPS)   or   a player's listen host (the game, UPnP)
```

| Code | What it is |
|---|---|
| `src/OpenScct.Net/Transport` | `LiteNetLibTransport` (UDP: peers, NAT punch, relay frames, unconnected messages), `SessionTransport` (one socket across map changes), `RelayFrames` |
| `src/OpenScct.Net/Discovery` | `ServerInfo` and `DiscoveryPackets` (server queries, master probes), `LanBeacon` and `ServerQuery` (LAN search, ping), `MasterApi` and `MasterClient` (the master's HTTP API) |
| `src/OpenScct.Server/Online` | `OnlineServer` (the session: lobby, travel, round, results, admin, bans, master link), `OnlineClient` (a player's visit), `LobbySetup`, `SessionProtocol`, `JoinTarget`, `MasterLink`, `BanList`, `Scoreboard` |
| `src/OpenScct.Master` | `svm-master`: ASP.NET Core minimal API, `Introducer`, `Relay`, `Prober`, `ServerRegistry` |
| `src/OpenScct.Server.Cli` | `svm-server --config`: the dedicated online server, its config file and admin console |
| `game/Scripts/Online` | `OnlineService` (the visit in the game, scene flow), `ListenHost` (hosting from the game), `UpnpPorts`, `PlayCatalog`, `OnlineLobby`, `OnlineSettings`, the `online` smoke test |
| `game/Scripts/UI` | main menu, server browser, host page, connecting page, lobby, results |
| `tests/OpenScct.Integration.Tests/Online` | the session over SimLink, the master's API, and master, server and players over real UDP |

## Ports

| Port | Protocol | Who listens | What |
|---|---|---|---|
| 28888 | UDP | dedicated server, listen host | the game socket: game traffic, server info queries, master probes, relay frames |
| 28889 | UDP | dedicated server, listen host | LAN discovery (fixed, so a host on another game port still answers LAN searches) |
| 28880 | UDP | svm-master | NAT punch introducer |
| 28881 | UDP | svm-master | relay |
| 28800 | TCP | svm-master | HTTP list API, on loopback behind a TLS proxy (443) |
| any | UDP | svm-master | reachability probes go out from a free port the hosts never send to |

None of these clash with the original game (8888, 7776) or the Reloaded Editor test slots.

## The session

One online server runs one session (`OnlineServer`). It moves through four phases, which every client and the
master see (`SessionPhase`):

1. **Lobby.** The server runs the built-in empty `Lobby` level (never the converted menu map of that name). Players
   join, pick a side (spies, mercs, spectators), get ready, chat (the control channel's chat messages and
   `ChatRelay`), talk (voice, below), and see each other's ping and whether they have the map. The setup is Enhanced
   4.0's Create Session page (spec/game/lobby-and-match-creation.md, spec/game/match-options.md rules 26 and 27,
   docs/MENUS.md): map, mode, time limit, both sides' lives (the kill limit in Merc Deathmatch), respawn delay, Disk
   Hunt's disk counts, revenge and games in a row, the voice chat mode (Modern, Classic, Team, Open), voice in the
   lobby and random map, starting from the map's own settings for the mode (`LobbySetup.ForMap` over
   `OpenScct.Game.Modes.MatchSetup.Defaults`), and our own bot rows (Bots, Bots per Side, Bot Skill; see "Bots"
   below). Any change of the setup un-readies the players (the host need not be
   ready); Merc Deathmatch moves everyone to the mercenaries and Switch Team does nothing there.
   - Who may change the setup, launch and kick: on a listen host, the host's own player (it proves it with the
     host key); on a dedicated server, everyone unless an admin password is set, then admins (`#svm admin`,
     `!login`). Reloaded's rule (rl:net.dedicated).
   - Launch: "Could not start. Not all players are ready" until every player on a side is ready (the host need not
     be), and nobody lacks the map or has a different version of it (the lobby names who). Nobody on a side stops
     it too; an empty side does not (Reloaded lets a host start with an empty team). A dedicated lobby launches on
     its own once `MinPlayersToLaunch` players are ready. A countdown (5 s) runs first; a change stops it, and the
     host's Launch pressed again (the page button reads Cancel) cancels it.
2. **Loading.** The server travels to the map with the setup's URL options (`?MapName=...?GameMode=5?...?NBPlayers=n`)
   and the game's `SGameInfo` spawned during the load; each player logs in with its lobby side
   (`?Team=`, and in a revenge series `?GamesInARow=` so `Login` swaps the sides of the second game, spec/game/
   round-travel.md rule 10). As the engine's URL parser does, a key given twice keeps its last value
   (`NativeUrl.Rebuild`): the series URL carries GamesInARow twice and the rules must see the lower one. The start gate (`DedicatedStartGate`, on listen hosts too) starts the round once every
   expected player is in and waiting; after `LoadTimeoutSeconds` (60) it starts with those who came.
3. **Match.** The game's own rules run the round. A player who joins now watches as a spectator (Login rule 15).
   When the round has a winner, the end screen shows for 10 s (the original opens its end menu 10 s after the
   winner), then the results.
4. **Results.** Every client gets the winner, the reason, the objectives and each player's score, kills, deaths
   and lives. Players say they are ready for the next round; once all are (a dedicated server also goes on after
   `ResultsSeconds`):
   - the next game of the series, as `EndingMenuRules.NextRoundUrl` decides (GamesInARow one lower, revenge points
     carried over swapped);
   - when the series is over or a side is empty: everyone goes back to the lobby with the session's options
     (Ending_Menu.Timer), each on the side he chose there (the revenge game's swap is only for that game), where the
     host launches again for a rematch; a dedicated server takes the next match of
     its rotation. With Random Map the next map is drawn from the installed maps that have the mode, never the one
     just played, each once before the pool refills (Ending_Menu.ChooseRandomMap).
   The host (or an admin) may send everyone back to the lobby at any time.

Leaving: a player who leaves (or times out) is removed and announced; when nobody is left in a round, the server
goes back to its lobby.

### Rejoining a round

A player who drops out of a round, whatever the cause (a crash, Alt+F4, a killed process, a dead PC, a lost
connection, or leaving through the menu), is offered the round again the next time the game shows its main menu,
as long as the round is still being played. Our addition; the original's reconnect take-over
(`SGameInfo.Login`) only works before the match starts.

- **The server keeps his place** (`OnlineServer.Rejoin.cs`): a person on a side who leaves while the session is
  loading, playing or showing the results of a series game (not a bot, a spectator, the listen host's own player, a
  kicked or banned player, or anyone while the server closes) is kept as a `DroppedPlayer`: his account (or, for a
  guest, his install id), his session side, the round, and during the match his score, kills, deaths and lives.
  The places are dropped when the session goes back to the lobby or the last game of the series ends.
- **Coming back**: a new player whose ticket's account (or, as a guest, install id) matches a kept place joins on
  that side instead of as a spectator, and a signed-in one needs no server password (his ticket proves who he is,
  and he may be rejoining from another PC). The map login carries `?SvmRejoin=1`, which lets `SGameInfo.Login` put him
  on his side waiting (as a Merc Deathmatch late joiner) instead of making him a visitor. Once he is in, the server
  gives back the score, kills, deaths and lives he left with (the side's starting lives when the series moved on to
  another game), spawns him if he has lives left, and announces "X rejoined the round."
- **The master knows** (signed-in players): heartbeats carry the kept places' account ids (`RejoinAccounts`), and the
  account answer (`GET /api/v1/account`, also the sign-in's answer) carries `rejoin` (`RejoinOffer`: server id,
  name, map, mode) when a game server that is loading or playing holds one for the account and he is not on it. So
  signing in on any PC offers the round.
- **The game remembers** (`RejoinService`, `user://rejoin.cfg`): from the moment a round of someone else's server
  loads until the visit is back in a lobby, the game keeps the server's master id (or the typed address), its
  password and the map. When the main menu opens it asks the master for that server (`GET /api/v1/servers/{id}`),
  or the server itself for a typed address (a server query), and shows the **Match in progress** panel with
  **Rejoin** and **Not now** while the server is loading or playing a round. Rejoin joins through the server browser
  (with the ticket) or the address; Not now forgets it. A kick, or a rejoin that failed, forgets it too; leaving
  through the menu keeps it, but the panel only shows the next time the game starts. Without a remembered round the
  panel shows the master's offer for the account. Hosting and hub pad matches are not remembered.

Limits: a round ends as before when a leaver was the last player of his side (the game's own end check) or the last
person in it (back to the lobby), so there is then nothing to rejoin. A heartbeat names the dropped accounts without
proof, so a hostile host could make the panel offer its server to an account; Not now dismisses it.

### Travel

Each map runs its own `ServerHost` with its own driver, and clients reconnect for each map, as UE2 clients do on
`ClientTravel`. To keep punched NAT mappings and relay sessions, both sides keep the same socket and peer and give
each map's driver a new `SessionTransport` generation:

1. The server sends `travel <map> <content>` to everyone, stops taking new connections, and holds its peers.
2. Each client closes its connection at once (the close carries the acknowledgement), checks it has the map (same
   content hash, or it leaves with "Your game's version is not the same as the server's"), waits
   `TravelQuietSeconds` (2 s), drops whatever arrived meanwhile, and connects again on the same socket with its
   session token.
3. The server keeps ticking the old map until every old connection closed or `TravelGraceSeconds` (1.5 s) passed,
   retires that generation (it can no longer send), then loads the new map. A client still early is refused until
   the new map is up and simply resends its hello.

Because the quiet time is longer than the grace, no late datagram of an old connection reaches a new one (our
packets carry no connection salt; see Open items).

### Session protocol

The session's own commands are text lines on the control channel starting with `#svm ` (`SessionProtocol`), as
UE2's control channel carries text commands. Server to client: `hello`, `setup`, `player`, `rank` (a signed-in
player's level, prestige and title, `PlayerRank` packed), `gone`, `phase`, `msg`, `travel`, `row`, `results`, `kicked`. Client to server: `team`, `ready`, `setup`, `launch`, `lobby`, `mapcheck`,
`kick`, `ban`, `admin`, `cmd`, `teamvoice` (the lobby's "Restrict Mic to Team"; a player line carries it as flag 128).
A bot's player line carries flag 256. The setup line is `LobbySetup.ToOptions`: GetOptionsString, then `SvmDisks`,
`SvmDisksToWin`, `SvmDisksMax`, `SvmVoice` (the page's voice index), `SvmVoiceLobby`, `SvmRandomMap`, `SvmBots`
(0 off, 1 per side, 2 fill), `SvmBotsPerSide` and `SvmBotSkill` (0 Easy to 3 Expert). A player's identity across maps is the session token in its login options
(`?SvmToken=`), with its install id (`?SvmId=`, bans) and, for a listen host's own player, the host key (`?SvmHost=`).
The Hello text that carries them may be up to 400 bytes (`ControlMessage.MaxLoginBytes`).

### Missing maps

After each `setup` line, a host whose map came from the SCCT-Maps repository (downloaded through the launcher's
Maps window) sends `mapsource <map> <fingerprint> <commit> <id>`: the repository id last, so it keeps its spaces.
`OnlineServerSettings.MapSource` gives it; the game's host uses `MapDownloads.SourceOf` (the downloaded map whose
codes include the setup's map, and only when the host's own install has no map of that name). A `setup` line clears
the client's `LobbyState.SetupSource`; older clients ignore the new line.

A player whose `mapcheck` says Missing or Differs while a source is known sees a line under the lobby status with a
Download button (`LobbyRoomScreen`, `OnlineLobby.MapFetch`). `MapFetch` then, in the background: reads the
repository's tree at the host's commit and takes the package with the host's id and fingerprint (so both have the
same files), downloads it into the launcher's maps folder (`--svm-map-downloads=`, default
`%LOCALAPPDATA%\OpenSCCT\maps`), and runs the content builder shipped beside the exe (`contenttools`, or a
checkout's `content` with Python from PATH) for that map only: `maps-catalog renderpack maps audio-map --maps <codes>
--addons <maps\game>` in the content's language (`content-build.json`), output in `<content>\map-fetch.log`. When it
ends the catalog is read again (`MapCatalog.Refresh`) and the client sends `mapcheck` again (`OnlineClient.RecheckMap`),
so the host sees the player turn ready to play. `content-build.json`'s `downloadedMaps` moves on with it, so the
launcher does not prepare the game files again for that map.

It refuses when the player's own install has a map of that name (the install's copy always wins), when the build
has no builder that reads downloaded maps, or when the game was not started with its content and game folders. Not
done: a dedicated server (svm-server) has no `MapSource` yet, and a map the host converted from its own install
cannot be fetched.

### Bots

The original has no bots in a network session; ours are an addition. The host sets them on the Create Session page
(and in the lobby's Session Parameters, like every other row): **Bots** Off, **Per Side** (Bots per Side, 1 to 4, on
each side) or **Fill Slots** (each side filled to half of Max Players, the spies taking the odd place), and **Bot
Skill** Easy, Normal, Hard or Expert (docs/AI.md, "Difficulty"). The dedicated server's console has
`set bots off|side|fill`, `set botsperside <n>` and `set botskill <name>`.

- **In the lobby** the bots are rows of the session (`SessionPlayer.IsBot`, `LobbyPlayer.IsBot`), named SpyBot1,
  MercBot1 and so on, always ready, with the map; their ready column reads "Bot", and they have no volume slider,
  mute or kick. They never take a person's place: a player who joins (or switches sides) makes a bot leave, a player
  who leaves brings one back (`OnlineBotRoster.Wanted`). People are capped by Max Players as before; people plus bots
  never exceed it. In Merc Deathmatch every bot is a mercenary.
- **In a round** each map's server makes the bots before its first tick (`OnlineServerSettings.Bots`, the factory
  `OpenScct.Bots.OnlineServerBots.Factory`, set by the listen host and svm-server): `ServerBots` logs each one in
  through the game's Login and PostLogin on its lobby side, reports it waiting like a loaded client (the start gate
  counts it), and drives it with the game's AI (`AiController`) on the server's thread. Clients see the bots as any
  other player. The results list them.
- **Across maps** the bots stay in the session: the next game of a series makes them again on the new map's server,
  with the series count in their login, so Login swaps them in a revenge game as it swaps everyone; back in the lobby
  they are on their own sides again. The roster changes only in the lobby.
- **Never people**: bots have no connection, so the voice relay never routes them (the lobby's voice directory skips
  them, the round's reads connections); they do not count for the launch's ready check, `MinPlayersToLaunch`, the
  server browser's player count and "full", or a round's "everyone left" (bots alone send the session back to the
  lobby).
- Bots play with the host settings as they are, like the people: the round's rules start from the original's
  defaults (InitPersistentData: goggles, torch, laser, grab, elbows, berserk and charge on), PostLogin copies them into
  the replicated game info for every login, and the clients read them from there.

### Voice

Every map of the session, the lobby included, carries voice on its own transport (`VoiceTransport` over the map's
`SessionTransport` generation; docs/VOICE.md): the server's `VoiceRelay` routes it inside the driver's receive pass.
In a round the relay reads the game's players (`NetDriverVoiceDirectory`, `SessionVoiceLinks` for the SBase special
cases); a talker is the player's PlayerReplicationInfo id plus one (`VoiceK.TalkerOf`: GameInfo hands out ids from 0,
and the first player, a listen host's own, has 0). The lobby level has no game rules, so the lobby routes the
session's players (talker = session player id) by the lobby rules: everyone hears everyone, only with Voice in Lobby
on, and not across sides from a player who restricted his mic to his team. The setup's voice mode reaches the running
map's replication info at once (`LobbySetup.ApplyTo(GameReplicationInfo)`), and `allowVoiceChat` is on (the host's
profile switch, which the server has no profile for). On the client, `OnlineVoice` follows the visit from map to map
and keeps each other player's volume and mute by name.

## Joining

A client joins through a `JoinTarget`: an address someone typed or the LAN search found (one direct attempt), the
listen host's loopback, or a listed server's join answer from the master, which gives the attempts in order:

| Reachability (master's probes) | Attempts |
|---|---|
| direct | the host's own addresses when the player shares its public address (3 s each), the public address, a NAT punch, the relay |
| needs intro | (own addresses), a NAT punch, the relay |
| relay only | (own addresses), the relay |

The player can force one path (server browser: Connect Auto, Direct, NAT punch, Relay). An attempt fails on no
answer (10 s), a failed punch (5 s) or a relay error, and the next one starts; a refusal by the server ends the
join with its reason. What players read:

| Situation | Message |
|---|---|
| another build | Your game's version is not the same as the server's (OpenScct.Net) |
| map content differs | Your game's version is not the same as the server's (TestBox/sim/gameplay.json) |
| map missing at travel | The server is changing to X, which you do not have. |
| full | The server is full |
| password | This server needs a password. / Wrong password. |
| banned | You are banned from this server: reason |
| kicked | the admin's reason |
| unreachable | Could not reach X (tried direct, NAT punch, relay; no answer). |
| master down | Could not reach the master server at host: ... |
| private master | The master server is private and needs an invite key. / The invite key was refused. |
| host closed | The server is shutting down. |

The server browser greys out servers this install cannot join and says why: another build (protocol hash), a
missing map, a different version of the map (content hash), or full.

## Master server (svm-master)

One process: the HTTP list API on Kestrel, and three UDP sockets (introducer, relay, prober) sharing one in-memory
registry. It keeps no database; after a restart hosts register again on their next heartbeat (a 404 makes them).

HTTP API (`MasterApi`, JSON camelCase, enums as text). With invite keys set, every call except the status needs
`X-Svm-Key`:

| Call | What |
|---|---|
| `POST /api/v1/servers/heartbeat` | a host's info every 30 s; the first gets an id and a host token, later ones must send both |
| `GET /api/v1/servers?protocol=&content=&kind=` | the listed servers (filter by build and map content hashes); `kind=hub` lists hubs instead of game servers; the master's trusted servers come first |
| `GET /api/v1/servers/{id}` | one server, listed or not (join by server id) |
| `POST /api/v1/servers/{id}/join` | how to reach it: public address, own addresses (same network only), a punch token, a relay token |
| `DELETE /api/v1/servers/{id}?hostToken=` | unlist now (listings lapse 90 s after the last heartbeat) |
| `GET /api/v1/status` | versions, notices, counts (health check) |

- **The master's own version.** The master is versioned apart from the game: `MasterVersion` in
  `src/OpenScct.Master/OpenScct.Master.csproj` (bump it when the master changes; a release's `-p:Version` does not
  touch it), stamped into svm-master.dll with the build number (the commit's UTC timestamp, yyyyMMdd.HHmm) and the
  short commit (`MasterBuildInfo`). The status answer carries them as `masterVersion`, `masterBuild` and
  `masterCommit`; `version` stays the game version the master was built with. The master logs both on start, and
  the game shows the master's line under its own version in the main menu's and options' corner
  (`MasterVersionCheck`: "Master 1.0.0  build 20261027.1200 (abc1234)"; "Master: checking...", "Master: not
  reached" (asked again a minute later), "Master: none set", or "Master (game 0.7.0)" from a master older than these
  fields). To see which master build is live: `curl https://master.lumbridge.co.uk/api/v1/status`.
- **Versions.** `LatestVersion` adds an update notice to the list and to heartbeats; a host older than `MinVersion`
  is refused with an update message (rl:net.version).
- **Reachability** (`ServerRegistry.Classify`). On a host's first heartbeat (and every 5 minutes, or when its address
  or port changes) the prober sends `SVMP` from a port the host never sent to, to its public address and game port
  and to the mapping the introducer saw. An answer means anyone can reach it: **direct**. Otherwise, when the
  introducer and the relay saw the host's socket on the same external port (the NAT maps one port for every
  destination) a punch should work: **needs intro**. When they differ (a symmetric NAT, typical of carrier-grade
  NAT) or the host offers no punch: **relay only**.
- **Introducer.** LiteNetLib's NAT punch module: hosts register `h:<host token>` every 10 s from their game socket;
  a client sends `c:<punch token>` from its join answer; both get each other's internal and external endpoints and
  punch. Punch tokens are single use and last a minute.
- **Relay.** Frames in `RelayFrames`: hosts say hello with their host token every 5 s from the game socket (which
  also keeps their NAT mapping open); a client sends `C <client token> payload`, the relay forwards `D <session>
  payload` to the host and back. It never reads payloads. Limits: 400 datagrams and 64 KB per second per session and
  direction, `MaxRelaySessions`, sessions per server, 60 s idle. A game uses about 2 to 5 KB/s per player each way
  (docs/NETCODE.md "Bot matches"), so the relay adds that to the VPS traffic for every relayed player.
- **Abuse limits.** 240 API calls per minute per address, 8 servers per address, 500 servers.

Config: `svm-master.json` (`Master` section, see `src/OpenScct.Master/svm-master.example.json`), overridable with
`SVM_Master__Key=value` environment variables.

## Accounts

Players sign in with **Discord** (nothing else: no passwords, no email). Signing in gives a claimed, unique name,
stats and achievements, settings that follow the player between PCs, and entry to **signed-in only** servers.
Everyone else still plays offline, on LAN and on open servers; on a server linked to a master a guest shows as
`Name#3fa2` (`PlayerNames.GuestName`: the name cut to 15 characters, then four hex digits from the install id), so
a guest can never pass for a claimed name. Servers with no master (LAN, offline, direct only) keep plain names.

**Pieces.** The master keeps accounts in SQLite (`accounts.db` next to its config: accounts, sign-in sessions, name
history, bans, stats, achievements, synced settings, an audit log). It signs join tickets with an ECDsa P-256 key
(`ticket-key.pem`, made on first start) and publishes the public key in its status and heartbeat answers.

**Signing in** (`AccountScreen`, main menu → Account):
1. The game asks `POST /api/v1/account/link` and gets a short code, a page address and a poll secret.
2. It opens `https://<master>/link/<code>` in the browser, which goes on to Discord (scope `identify`: the Discord
   id only). Discord comes back to `/auth/discord/callback`. The master finds or makes the account and shows "Signed
   in, go back to the game".
3. The game polls `POST /api/v1/account/link/<code>` with the secret and gets a sign-in token. It keeps the token in
   `user://account.cfg` and sends it as `Authorization: Bearer` from then on; the master stores only its hash.
4. A new account picks its name: 3 to 16 characters, letters, digits, `_ - .`. Names compare folded (case,
   separators and look-alikes 0/o, 1/i/l, 5/s … are the same name), and a few words are reserved. A name can change
   once every 30 days; the old one stays held for 14 days.

**Joining.** A signed-in player's join through the master (`POST /api/v1/servers/<id>/join`) also returns a
**ticket** (`PlayerTicket`): account id, name, admin flag, the server's master id and a 2-minute expiry, signed by
the master. The client sends it in the Hello as `?SvmTicket=`. The server checks the signature, its own master id,
the expiry and that the ticket was not used before. Then the player gets the account's name and a verified mark
(`SessionPlayerFlags.Verified`, a tick in the lobby). A player returning on a map change is matched by its
session token as before, so the ticket is only checked once. The master refuses a ticket to banned players and
says why. It sees the player's real address here, even for players who reach the server through the relay.

**Server settings.** `requireAccounts` (svm-server, default on for a listed server with a master) or "Signed-in
players only" on the Host page: no valid ticket, no entry. The listen host's own player is never refused. A
**trusted** server has a `serverKey` the master knows (`TrustedServers`). Only there:
- admin tickets make the player a server admin (as `!login` does). The master's admins are Discord ids in its
  config (`Admins`).
- finished rounds are reported (`POST /api/v1/results`) and count for stats and achievements. A round counts when
  it ended normally with a signed-in person on each side. The answer carries the stored round's `matchId` (empty
  from masters before demos), which the round's demo is uploaded under (see "Demos").

**Achievements** (`src/OpenScct.Net/Cosmetics/Achievements.cs`, shared by the master and the game) come in five groups. Each one has progress (current and
target) and unlocks cosmetics:
- **General:** tiers of matches, wins and kills, such as First Mission, Veteran and Centurion. Each gives a colour
  tint for both sides.
- **Spy:** win tiers, objectives taken, neck breaks, a drop-attack kill, three grab kills in a round (Triple
  Threat), sticky-shock knockouts, and Untouchable (win without dying). Rewards are suit camos, pistol camos and
  goggle light colours. Gilded Ghost (every spy achievement) gives the golden pistol.
- **Merc:** win tiers, The Wall (win without losing an objective), marking 50 spies (Laser Grid) and charge hits.
- **Weapons:** a CoD4/MW2-style camo ladder for each merc gun (assault rifle, SMG, shotgun):
  - Woodland 25 kills, Desert 75 kills, Digital 150 kills;
  - Red Tiger 50 headshots, Blue Tiger 150 headshots;
  - sniper, terminate, grenade and mine feats alongside them.
  
  Gilded (every weapon achievement) gives the golden camo on all three guns.
- **Maps:** win on a base map (the code's first four letters, so every variant counts) as a spy and as a merc. Each
  gives that map's camo.

The master's database (schema v2) keeps per-account feat totals (`feat_totals`) and wins per base map (`map_wins`)
alongside the stats. A v1 database is upgraded in place.

**Feats.** Trusted servers count each round's feats per player and send them in the results row (`Feats`, names in
`OpenScct.Core.Engine.FeatKeys`). The master adds at most 200 per feat per round and ignores names it does not know.
- **Kills** are sorted in `SGameInfo.Killed` (`SGameInfo.Feats.cs`) by:
  - damage type: grab break or choke, drop attack, scope;
  - the running blast (`GadgetBlast.CurrentIsMine`: grenade or mine);
  - the killer's `m_PawnToTerminate` (terminate);
  - the merc's gun class (`SWeaponDefense`, `_Uzi`, `_Shotgun`) for the ladders. A headshot is the head pill on the
    victim's last damage.
- **Laser marks** are counted in `SGadTorcheLaser.ThrowSpyBullet`.
- **Sticky knockouts, charge hits and objectives** come from the retail statistics.

**Cosmetics** (`src/OpenScct.Net/Cosmetics/CosmeticCatalog.cs`). Every item has a permanent id and bit. A slot is a
suit or armour, a light colour, the spy pistol, or one of the three merc guns. An item is one of:
- a camo texture, which the content builder paints over the player's own retail texture (`content/svmbuild/cosmetics.py`,
  in the chars step);
- a tint, a colour that keeps each texel's brightness;
- a light hue, which turns the goggle lenses, helmet lamp and the normal goggle beam to a new hue at the same
  brightness. The merc's motion and EMF beams keep their colours, and the torch stays white.

How a look gets into a match:
1. `AccountInfo.Unlocked` lists what the earned achievements unlock. The join ticket carries the same set as bits
   (`PlayerTicket` format 2; format 1 tickets are still read).
2. The player picks items in the Armoury, which saves them to `cosmetics_spy` and `cosmetics_merc` in
   `settings.json`. The settings sync carries them to other PCs.
3. The client sends its looks in the Hello as `?SvmLookS=` and `?SvmLookM=` (`CosmeticLook`, hex, a byte per slot).
4. The server keeps only the items the ticket unlocks; for the listen host, its own account's list. Guests and bots
   wear the retail look.
5. The server passes the checked look to the map login as `?SvmLook=`. The game stores it on
   `PlayerReplicationInfo.SvmLookLow/High`, replicated by `PlayerLookNet`, so every client draws it
   (`PawnCosmetics`, `SkeletalModel.SetCosmetic/SetTint/SetGlowHue`).

A Sticky shock's skin draws over a camo and gives it back when it ends.

The master's operator can also give cosmetics without the achievements (`admin unlock`, kept in `cosmetic_grants`, schema v3) and take them back (`admin lock`). The account and its tickets carry them like earned ones.

**Levels and prestige** (spec/additions/progression.md; `ProgressionK`, `PlayerRank`, `TitleCatalog` in
`src/OpenScct.Net/Cosmetics/`). Every counted round gives each signed-in player XP (score, finishing, winning,
objectives) plus the XP of the achievements it earned; the master keeps it in `progression` (schema v5: prestige,
XP within it, best level, lifetime XP, the last round's award as JSON). 55 levels, 10 prestiges, a master prestige
emblem at prestige 10 level 55, and titles (per prestige, level milestones, some achievements).
1. `AccountInfo.Progress` (`ProgressInfo`) gives the game the rank, XP, unlocked titles and the last award
   (`XpAward`, which the results screen shows).
2. `POST /api/v1/account/prestige` prestiges a level 55 account (409 below 55 or at prestige 10).
3. The join ticket carries the packed rank and the title bits after the unlocks (trailing fields of format 2).
4. The Hello carries the chosen title (`?SvmTitle=`); the server keeps it when the ticket unlocks it, passes
   `?SvmRank=` to the map login, and `PlayerReplicationInfo.SvmRank` replicates to everyone (`PlayerLookNet`). The
   lobby sends it as a `rank <id> <packed>` line after the player line.
5. Admins set a rank on the portal's player page or with `svm-master admin rank <who> <prestige> <level>`.

**Settings sync** (`ProfileSync`): the account keeps one record: `settings.json` without the per-PC fields
(display mode, monitor, resolution, resolution scale, vsync, frame caps, microphone), plus the player's keybind
files `svm_keys_spy.ini` and `svm_keys_merc.ini`. The game uploads it a few seconds after a save. On sign-in and at
start-up it takes the master's copy when that is newer, or when this PC has not synced with the account before.
The newer copy wins.

**Bans.** Global bans (account, Discord id, address or range, with an end and a reason) are kept by the master and
apply to tickets, sign-ins and, through `KickAccounts` in the heartbeat answer, to players already on listed
servers. Only the master's operator sets them, from a shell on the master's machine:
```
svm-master admin ban <name|discord id|ip|cidr> [--days N] [--reason text] --config svm-master.json
svm-master admin unban <name|discord id|ip|cidr|ban id> --config svm-master.json
svm-master admin bans | whois <name|discord id> | rename <name> <new> | release-name <name> | audit [N]
svm-master admin unlock|lock <name|discord id> <all|spy|merc|item id...> | cosmetics
```
Each server keeps its own `bans.json` as well. There, a signed-in player is banned by account id, and a guest as
before (install id, address, name).

## Admin portal

`https://<master>/admin` (`src/OpenScct.Master/Admin/`): web pages for the master's admins, the Discord ids in its
config's `Admins` list. It is on when Discord sign-in is set up.

**Signing in.**
- `/admin/login` sends the browser to Discord with `state = admin.<nonce>` and the nonce in a short `svm_admin_state`
  cookie. Discord comes back to the game's own `/auth/discord/callback`, which hands `admin.` states to the portal.
  No second redirect URL needs registering with Discord.
- A Discord id that is not in `Admins` is refused and audited (`admin.refused`).
- A session is a random token in the `svm_admin` cookie: HttpOnly, Secure, SameSite=Strict, path `/admin`. Only its
  hash is kept, in `admin_sessions`. It ends after 12 hours unused or 7 days at most, and every request checks
  `Admins` again.
- Every form carries the session's CSRF token.
- Pages send their own content policy (scripts and styles only from `/admin/assets`), no framing and no caching, and
  go through the master's rate limit.
- Every change is audited with its admin (`audit.actor` is `admin:<name>`; the command line writes `cli`).

**Pages.**

| Page | What it does |
|---|---|
| Dashboard | Players online (games, hubs), servers, accounts, rounds today, relay and introducer counts, the 24-hour players chart, latest rounds and audit lines. It refreshes itself. |
| Servers | Every registered server, unlisted ones too. A server's page lists its players (team, ping, account) and, for trusted servers, runs commands: say, kick or ban a player, change map or mode, start, next map, restart round, back to lobby, set an option, or any console line. It can also block the server's address. |
| Players | Search by name, Discord id or account id; online, banned and admin marks. |
| Player | Stats, achievements, cosmetics, bans, rounds, game sign-ins and old names. Actions: rename, release the name, ban or unban, kick from the server they are on, sign out everywhere, unlock or take back cosmetics. |
| Bans | Bans in force (and ended ones). Ban a name, `#account`, Discord id, address or CIDR range; end a ban. |
| Matches | Every round a trusted server reported, counted or not, with each player's line and feats. |
| History | Players, servers and relay sessions over a day, a week or a month; peak players per day. |
| Settings | Message of the day, latest and minimum version (stored in the database, they override the config at once); an announcement to every trusted server; blocked servers; the admin list. |
| Audit | The audit log with a filter. |

**Commands to servers.**
- The master keeps a queue per trusted server and sends it with the next heartbeat answer (`HeartbeatResponse.Commands`).
- While commands wait, or while an admin has the server's page open, the answer asks for heartbeats every 5 s.
- The server runs each line through its console handler (`OnlineServer.Execute`), except closing the server. It
  sends the output back with its next heartbeat (`HeartbeatRequest.CommandResults`), and the server page shows it.
- Servers that are not trusted get no commands and ignore any.

**What servers send.** Heartbeats also carry `PlayerDetails` (session id, name, team, ping, account, bot, admin, host)
and `RoundSecondsLeft`. Results also carry each row's `Name` and the round's `DurationSeconds`. Older servers send
none of these and still work.

**Data (schema v4).**
- `admin_sessions`;
- `matches` and `match_rows`, every reported round;
- `metrics`, one sample a minute, kept 90 days;
- `settings`;
- `blocked_servers`, refused at heartbeat;
- `audit.actor`.

## Demos

The master keeps round demos (the recorded network stream of a round, about 1 to 3 MB) in a private **S3 bucket**
for 7 days. Trusted servers upload them; any player signed in with Discord can list and download them. The master
only **signs short-lived links** (`DemoStorage`, AWSSDK.S3 `GetPreSignedURL`, signed locally): the files go straight
between S3 and the servers or players, never through the VPS, and neither servers nor players hold AWS keys. Demos
are off while `Demos.Bucket` is empty (or sign-in is not set up).

**Bucket setup** (once, in the AWS console):
1. Create a bucket (for example `openscct-demos`) in **eu-west-2** (London). Keep **Block Public Access** on (all
   four settings) and ACLs disabled; the bucket stays private.
2. Management → Lifecycle rules → add a rule limited to the prefix `demos/`:
   - expire current versions of objects **7 days** after creation;
   - delete expired object delete markers and **abort incomplete multipart uploads after 1 day**.
3. IAM → Users → add a user for the master (no console access) with this inline policy, then make an access key for it:

   ```json
   {
     "Version": "2012-10-17",
     "Statement": [
       {
         "Effect": "Allow",
         "Action": [ "s3:PutObject", "s3:GetObject" ],
         "Resource": "arn:aws:s3:::<bucket>/demos/*"
       }
     ]
   }
   ```

4. Put the bucket and keys in `svm-master.json` (the secret is better in the service's environment as
   `SVM_Master__Demos__SecretKey`) and restart the master. Its start line says `demos s3://<bucket> (...)`.

| `Demos` field | Default | What |
|---|---|---|
| `Bucket` | empty (off) | the bucket's name |
| `Region` | eu-west-2 | the bucket's region |
| `AccessKey` / `SecretKey` | none | the IAM user's key (only `s3:PutObject` and `s3:GetObject` on `demos/*`) |
| `KeepDays` | 7 | days a demo is listed; keep it the same as the lifecycle rule |
| `MaxBytes` | 67108864 (64 MB) | the largest demo a server may upload |

**Endpoints.**

| Call | Who | What |
|---|---|---|
| `POST /api/v1/demos/upload` | trusted server (`X-Svm-Server-Key`) | body `{ matchId, size, sha256, durationSeconds, gameVersion, netVersion, contentHash }`; the round must be one this server reported (its server id registered under the same key). Answers `{ demoId, uploadUrl, contentType, urlExpires }`: a PUT link valid 15 minutes with the Content-Length and Content-Type signed in, so S3 refuses any other size. Over `MaxBytes`: 413; a round that already has a ready demo: 409 |
| `POST /api/v1/demos/{id}/done` | trusted server | the upload finished; the demo is listed from now on |
| `GET /api/v1/demos?page=` | signed-in player (`Authorization: Bearer`) | ready, unexpired demos newest first, 25 a page: map, mode, server name, time, duration, winner, players (from `match_rows`), size, sha256, versions, expiry |
| `POST /api/v1/demos/{id}/download` | signed-in player | `{ url, urlExpires, size, sha256 }`: a GET link valid 5 minutes; 30 links per account per hour |

Client calls: `MasterClient.RequestDemoUploadAsync`, `DemoDoneAsync`, `DemosAsync`, `DemoDownloadAsync`.

**On the server.** When a trusted server's round ends, `OnlineServer` reports the results and keeps the answer's
`matchId`. The demo recorder hands its finished file to `OnlineServer.SubmitDemo(path, DemoMeta)`;
`MasterLink.UploadDemo` then hashes the file, asks for the link, PUTs the file with `HttpClient` off the server's
thread (three tries ten seconds apart, like results) and confirms with `done`. The outcome shows in the server log.
`demos.upload: false` in `svm-server.json` turns uploads off.

**Data (schema v6).** `demos`: id, match, object key (`demos/{yyyy-MM-dd}/{matchId}-{random}.scctdemo`), size,
sha256, duration, game, net and content versions, state (`pending`, `ready`, `expired`), created, expires. Once a
minute (with the metrics sample) the master marks expired the demos past `KeepDays` and the uploads not confirmed
within an hour; the lifecycle rule deletes the files. The admin portal's round page (`/admin/matches/{id}`) shows the
round's demo with a download link (a fresh 5-minute link each click).

**Cost.** S3 Standard in eu-west-2 is about $0.024 per GB-month; with demos of 1 to 3 MB kept 7 days, even
hundreds of rounds a week stay under 1 GB stored. Uploads into S3 are free. Downloads to the internet are free up to
100 GB a month (AWS's free data-out allowance), then about $0.09 per GB. Requests cost fractions of a cent per
thousand. Expect pennies a month; the per-account download limit keeps one player from running it up.

## Dedicated server (svm-server)

```
svm-server --write-config svm-server.json        # an example to edit
svm-server --config svm-server.json              # run it
svm-server --online --name "Test" [--master https://... --key ...] [--port 28888] [--loopback-only]
```

The old single-map flags (`--map`, `--game`, `--bots`) still run a bare `ServerHost` for tests and measurements.

`svm-server.json` (JSON with comments; every field optional):

| Field | Default | What |
|---|---|---|
| `name` | SVM dedicated server | listed name |
| `port` / `lanPort` | 28888 / 28889 | game port; LAN discovery port (0: off) |
| `loopbackOnly` | false | bind 127.0.0.1 only (testing) |
| `maxPlayers` / `maxSpectators` | 8 / 2 | |
| `password` | none | |
| `adminPassword` | none | in-game admins (`!login <password>`); with one set, only admins change maps |
| `openMapCommands` | true | without admins, every player may change the map and launch |
| `motd` | none | shown to each player joining |
| `netRate` / `lanPlay` | 20 / false | replication rate; the original's -lanplay |
| `maps` | none | extra SVMap folder (searched before `maps/` next to the executable) |
| `rotation` | `[{ "map": "SteeE", "mode": "Story" }]` | each `{ map, mode, timeLimit, spyLives, mercLives, respawn, killLimit, disks, disksToWin, revenge, gamesInARow }`; unset values come from the map's own settings |
| `minPlayersToLaunch` | 2 | the lobby launches when this many are ready |
| `launchCountdownSeconds` / `resultsSeconds` / `loadTimeoutSeconds` | 5 / 30 / 60 | |
| `bansFile` | bans.json | next to the config |
| `master` | none | `{ url, inviteKey, listed, natPunch, relay }` |
| `demos` | none | `{ upload }`: upload recorded demos to the master's bucket (default true; trusted servers only, see "Demos") |

Console (standard input, and `cmd <command>` from in-game admins): `status`, `players`, `kick <id|name> [reason]`,
`ban <id|name> [minutes] [reason]`, `unban <install id|address|name>`, `map <name> [mode]`, `mode <name>`,
`set time|spylives|merclives|respawn|score|revenge|games|maxplayers <value>`, `launch`, `lobby`, `next`,
`say <text>`, `close`. Chat commands (rl:qol.chatcmds): `!map <name> [mode]`, `!next`, `!prev` (`!back`),
`!pings`, `!login <password>`, `!help`. Ctrl+C, SIGTERM and `close` tell the players and the master before
stopping.

Bans (rl:ac.bans) are by install id and address (no hardware fingerprints), with an optional end.

## Hosting from the game

Main menu, Create a Session (docs/MENUS.md): the match (map, mode, rules, password, voice chat mode, max players,
revenge, random map, voice in lobby) and the hosting: name, port, the master to list on, listing on or off, UPnP. The game
runs the same `OnlineServer` on its own thread (`ListenHost`), the host's own player over loopback and everyone
else over UDP. UPnP (Godot's `Upnp`, rl:net.upnp) maps the game port for 24 h and the lobby says whether it worked,
naming the port to forward by hand when not. With a master set, the host also registers for punches and the relay,
so players behind any NAT reach it; behind carrier-grade NAT (Ryan's line) no port can be opened and the master
reports **relay only**: every remote player plays through the relay.

## The game client

`OnlineService` keeps the visit across scenes and ticks it at 60 Hz. Screens: the server browser (master list, LAN
search, saved addresses with ping, phase, how each is reached and whether it can be joined), the host page, the
connecting page (what is being tried), the lobby (sides, ready, pings and map checks, the setup the host edits,
kick, countdown, chat), the round (the play scene on the visit's session: `PlaySession` in remote mode, ticked with
the local input; the HUD, chat and scoreboard work as offline; the HUD's ping column reads `PRI.Ping`, which the
server sets from the connection's round trip), the results (ready for the next round, back to lobby for the host,
leave), and the main menu with the reason when a visit ends. Online settings live in `user://online.cfg` (master,
invite key, saved addresses, connect path, hosting choices, install id). The master and invite key start as our
AWS master, `https://master.lumbridge.co.uk` (`OnlineSettings.DefaultMasterUrl`, `DefaultInviteKey`); a saved file
with no master picks it up once (`[master] defaults`), and a player who clears it afterwards keeps it cleared. Smoke
runs (isolated settings) keep the master empty.

The player's Discord status (what they are playing, with Join for friends) is in `docs/DISCORD.md`.

## Updating the VPS

`tools/deploy/Deploy-Vps.ps1` updates the running master, dedicated server and hub in one command (default: all
three; `-Services master` for one). For each service it:
1. publishes the program (linux-x64, self-contained) and uploads it;
2. stops the service and backs up its folder to `/opt/svm/backups` (program, config, `accounts.db`, `ticket-key.pem`;
   not the converted maps and characters). It keeps the last ten backups of each service;
3. unpacks the new program over the old one, starts it, and checks it is up (the master's `/api/v1/status`);
4. restores the backup and restarts the service when it does not come back.

Configs, the accounts database, the ticket key and the content folders are never replaced. Each service is down for
a few seconds: the game retries master calls, and servers re-register on their next heartbeat. The master upgrades
its database itself on start.

Keeping updates seamless:
- **Master API:** add fields to the JSON answers, never rename or remove them; older games ignore what they do not
  know.
- **Join tickets:** new fields go after the existing ones. Servers skip bytes they do not know, so the master can be
  updated before the servers.
- **Game protocol:** a change to replication or the session protocol needs the servers and the players' game updated
  together (a release); update the master first, then the servers.

## Deploying on a VPS

What Ryan provisions later. Nothing here is deployed by the repository. One small VPS runs the master and a
dedicated server: 1 vCPU, 1 GB RAM and a public IPv4 are enough for the master, the relay and one 2v2 server;
traffic is about 5 KB/s per relayed player each way plus the server's own 2 to 5 KB/s per player.
Empty servers idle (`RealtimeLoop`, an OpenSCCT addition): a dedicated server in its lobby with nobody connected,
and a hub with nobody in it, tick 10 times a second instead of 60 and sleep in between, so they cost almost no CPU
(this matters on burstable plans such as Lightsail's). The first connection puts them back on 60 Hz; a joining
player waits at most a tenth of a second longer for the first answer.

1. **Name.** Point a DNS name at the VPS, for example `master.example.org` (an A record; AAAA too if it has IPv6).

2. **Build on a development machine** (framework-independent binaries, so the VPS needs no .NET install):
   ```
   dotnet publish src/OpenScct.Master -c Release -r linux-x64 --self-contained true -o local/publish/svm-master
   dotnet publish src/OpenScct.Server.Cli -c Release -r linux-x64 --self-contained true -o local/publish/svm-server
   ```
   (`--self-contained false` gives smaller output that needs `aspnetcore-runtime-10.0` and `dotnet-runtime-10.0`
   from Microsoft's package feed. Use `-r win-x64` for a Windows host.)

3. **Content for the dedicated server.** It reads only each map's `manifest.json` and `sim/`, plus the pawn
   animations, which must be the same build the players have (the moves depend on them). From the converted
   content on Ryan's machine (never from git):
   ```
   local/publish/svm-server/maps/<Map>/manifest.json
   local/publish/svm-server/maps/<Map>/sim/...           (for every map in the rotation)
   local/publish/svm-server/chars/...                     (all of .content/shared/chars)
   ```
   Players need the same maps; a different one is refused at the lobby's map check with its file named.

4. **Copy and install** (as root on the VPS):
   ```
   useradd --system --home /opt/svm --shell /usr/sbin/nologin svm
   mkdir -p /opt/svm/master /opt/svm/server
   # copy local/publish/svm-master/* to /opt/svm/master and local/publish/svm-server/* to /opt/svm/server
   chown -R svm:svm /opt/svm
   ```

5. **Master config** `/opt/svm/master/svm-master.json` (from `svm-master.example.json`):
   ```json
   { "Master": {
       "HttpUrl": "http://127.0.0.1:28800",
       "PublicHost": "master.example.org",
       "InviteKeys": [ "<output of: openssl rand -hex 24>" ],
       "Motd": "Private test server.",
       "LatestVersion": "0.5.0", "MinVersion": "0.5.0" } }
   ```

6. **Dedicated server config** `/opt/svm/server/svm-server.json` (from `svm-server --write-config`):
   ```json
   { "name": "SVM VPS", "port": 28888, "lanPort": 0, "maxPlayers": 4,
     "rotation": [ { "map": "SteeE", "mode": "Story" } ],
     "adminPassword": "<another random string>",
     "master": { "url": "https://master.example.org", "inviteKey": "<the invite key>" } }
   ```

7. **Services** `/etc/systemd/system/svm-master.service`:
   ```ini
   [Unit]
   Description=SVM master server
   After=network-online.target
   Wants=network-online.target

   [Service]
   User=svm
   WorkingDirectory=/opt/svm/master
   ExecStart=/opt/svm/master/svm-master --config /opt/svm/master/svm-master.json
   Restart=on-failure
   NoNewPrivileges=true
   ProtectSystem=strict
   ReadWritePaths=/opt/svm/master

   [Install]
   WantedBy=multi-user.target
   ```
   and `/etc/systemd/system/svm-server.service` the same with `Description=SVM dedicated server`,
   `After=network-online.target svm-master.service`, `WorkingDirectory=/opt/svm/server`,
   `ExecStart=/opt/svm/server/svm-server --config /opt/svm/server/svm-server.json` and
   `ReadWritePaths=/opt/svm/server` (bans.json is written there). Then
   `systemctl daemon-reload && systemctl enable --now svm-master svm-server`.

8. **TLS proxy.** Install Caddy and put in `/etc/caddy/Caddyfile`:
   ```
   master.example.org {
       reverse_proxy 127.0.0.1:28800
   }
   ```
   `systemctl reload caddy`. Caddy gets the certificate itself. The master trusts `X-Forwarded-For` only from
   loopback (`TrustForwardedFor`), so it sees players' real addresses.

9. **Firewall** (ufw):
   ```
   ufw allow 22/tcp && ufw allow 80/tcp && ufw allow 443/tcp
   ufw allow 28888/udp && ufw allow 28880/udp && ufw allow 28881/udp
   ufw enable
   ```
   Port 28800 stays closed (loopback only). The prober's replies come back through the firewall's connection
   tracking. Some providers also have a cloud firewall: open the same ports there.

10. **Check.**
    ```
    curl https://master.example.org/api/v1/status                       # {"version":"0.7.0","masterVersion":"1.0.0",...,"inviteOnly":true}
    journalctl -u svm-server -f                                           # "Listed on the master server as ..."
                                                                          # "Players reach this server directly."
    ```
    In the game: Find a game, master `https://master.example.org`, the invite key, Refresh: the VPS server shows
    with its ping. Host a game from Ryan's PC with the same master: the lobby says "Players reach this server
    through the relay only"; a second player joins it from the list.

11. **Dedicated hub** (docs/HUB.md). The same `svm-server` build runs the hub as a second service from its own
    folder, so the game server can be updated apart: copy `local/publish/svm-server/*` to `/opt/svm/hub` (keep
    `svm-server` executable: `chmod 755`), give it the game server's maps and characters
    (`cp -as /opt/svm/server/maps /opt/svm/hub/maps`, `ln -s /opt/svm/server/chars /opt/svm/hub/chars`, the same for
    `shared`), and add the hub map and its config: `fixtures/hub` to `/opt/svm/hub/maps/Hub` and `fixtures/hubmap`
    (hub.json, areas.json) to `/opt/svm/hub/maps/hubmap` (a pad whose map is missing is turned off). Give the hub its
    own trusted key in the master's `TrustedServers` (that makes it "Official"), then `/opt/svm/hub/svm-hub.json`:
    ```json
    { "name": "OpenSCCT Hub (EU)", "port": 28889, "maxPlayers": 50, "motd": "Welcome.",
      "master": { "url": "https://master.example.org", "inviteKey": "<the invite key>",
                  "serverKey": "<the hub's trusted key>", "region": "EU" } }
    ```
    and `/etc/systemd/system/svm-hub.service` as `svm-server.service` with `WorkingDirectory=/opt/svm/hub`,
    `ExecStart=/opt/svm/hub/svm-server --hub --config /opt/svm/hub/svm-hub.json` and `ReadWritePaths=/opt/svm/hub`
    (hub-scores.json is written there). Open `ufw allow 28889/udp` and `ufw allow 28890:28897/udp` (the pads'
    matches), and the same UDP range in the provider's cloud firewall (Lightsail: Networking), then
    `systemctl enable --now svm-hub`. The log should say "Players reach this server directly." The master must run
    this build or newer first: an older master ignores the kind and would list the hub among the game servers. A
    second region (a US VPS) is the same service there with `"region": "US"` and its own key.

12. **Updates.** Publish again, `systemctl stop svm-server svm-master`, replace the files, start them. Raise
    `LatestVersion` (notice) or `MinVersion` (refuse older hosts) in the master config. Back up
    `/opt/svm/server/bans.json`.

## Testing

```
dotnet test --project tests/OpenScct.Integration.Tests/OpenScct.Integration.Tests.csproj    # includes Online/
pwsh tools/ci/godot-smoke.ps1 -Tests online
```

- `Online/LobbyTests` (SimLink, virtual clock): two players meet, pick sides, check the map, get ready, launch,
  play a one-minute round to the mercs' time win, see the results, go back to the lobby, and one leaves; the map
  check names a player with another version of the map; full server, passwords and bans are refused with their
  reasons; console commands change the setup, `say` reaches the chat and `close` tells the players.
- `Online/MasterTests`: registration, filters, tokens, unlisting, invite keys, the minimum version, bad heartbeats,
  an unreachable master.
- `Online/OnlineUdpTests` (real UDP on localhost, real time): a master, a dedicated server listed on it (probed
  direct), the browser's info query, two players and two bots (OpenScct.Bots spies hacking the terminals) joining from
  the list, the round on TestBox to the spies' objective win, the results, the lobby again and leaving; a
  relay-only host (probes off, no punch) joined and played through the relay, map change included; a NAT punch
  through the introducer; a LAN search and a readable error for an address where nothing answers; the join
  attempts for each reachability.
- `online` smoke (Godot, headless): host from the game, a UDP guest joins, both meet in the lobby screen, the host
  launches, the play scene shows the round on the remote session with the spy's pawn, and leaving returns to the
  main menu while the guest hears the host closed.
- `LobbyOptionsTests`, `LobbyVoiceTests` (SimLink): every Create Session option through the session, the page's
  ranges, a change un-readying the guests, lobby chat, Merc Deathmatch's sides, a revenge pair with the sides swapped
  and back to the lobby with a random map; voice in the lobby at the volume the listener sets, mute, Voice in Lobby,
  Restrict Mic to Team, the round's voice by the host's mode with the lobby's volumes kept.
- `net-lobby` smoke: two game processes over UDP driven only through the menus and keys (docs/MENUS.md;
  docs/NETWORK-TEST.md is the same test with two PCs).
- `Online/OnlineBotsTests` (SimLink): the roster's rules (fill, per side, Merc Deathmatch, never a person's place)
  and the bot options through the protocol; a listen host and a connected client with bots filling the 2v2 of a
  revenge pair on TestBox: two bot rows in the lobby, voice for the two people only, the bots walking in the round
  and seen by the client, in the results, swapped in the second game, back in the lobby on their sides, a bot taking
  the place of the client who left, a bot that cannot be kicked, Bots Off.
- `net-bots` smoke: two game processes over UDP through the menus (ports 28990 and 28991): the host sets Bots Fill
  Slots and Easy on the Create Session page, player 2 joins by Join with IP, a bot makes room for him, both see the bot
  rows, the round has the bots' pawns on both screens, player 2 walks with the movement key, the results list the
  bots, everyone is back in the lobby with the same bots, and a bot takes player 2's place when he leaves
  (docs/NETWORK-TEST.md, "With bots", is the same with two PCs).

A local run by hand:
```
dotnet run --project src/OpenScct.Master -- --config local/svm-master.json      # HttpUrl http://127.0.0.1:28800, PublicHost 127.0.0.1
dotnet run --project src/OpenScct.Server.Cli -- --online --master http://127.0.0.1:28800 --key <key>
# then two game clients: Find a game, master http://127.0.0.1:28800
```

## Open items

- Packets carry no per-connection salt or encryption (docs/NETCODE-M0.md). Travel relies on the quiet time being
  longer than the server's grace; a spoofed datagram could still disturb a connection. A salt from the handshake
  is the planned fix.
- Hosts and clients are not authenticated beyond tokens and invite keys; signed player keys (rl:ac.bans) and signed
  releases (rl:ac.release) are later work. Bans use the install id, which a player can reset.
- The relay forwards LiteNetLib unconnected messages on LiteNetLib's receive threads; it has been run on localhost
  only. Its throughput on the VPS and the overhead budget (under 30 ms) need measuring in the CGNAT playtests.
- The symmetric NAT test compares two ports of the same master address; a NAT that keeps the port for one
  destination address but not for another is not caught and shows as needs intro (the relay is still tried after
  a failed punch).
- No IPv6 pinholes for listen hosts yet; LAN addresses sent to the master are IPv4 only.
- `!resign`, `!lagcomp` and the practice commands of Reloaded are not ported; playlists (rl:qol.lobbymaps) are a
  plain rotation in the server config, and the lobby's map picker lists every installed map.
- Bots: the dedicated server's config file has no bot fields yet (the console's `set bots` works); its rotation
  entries could carry them.
