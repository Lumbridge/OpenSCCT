# Discord Rich Presence

The game shows what the player is doing on their Discord profile, and friends can press **Join** (or **Ask to Join**)
there to land on the same server or hub. It talks to the Discord app running on the same PC over its local pipe
(the `DiscordRichPresence` NuGet package, `DiscordRPC.dll`); there is no native DLL and the player does not sign in.
Without the Discord app the game runs as normal and the client keeps retrying quietly.

| Path | What it holds |
|---|---|
| `game/Scripts/Discord/DiscordPresence.cs` | the node under `/root/App`: client, 1 s updates, launch registration, Join handling |
| `game/Scripts/Discord/PresenceSnapshot.cs` | reads the scene, the online visit, the hub and the match into a `PresenceState` |
| `game/Scripts/Discord/DiscordJoinSecret.cs` | the join secret's format |

Application id: `1564377811014389862` (`DiscordPresence.ApplicationId`), the OpenSCCT app on the Discord Developer
Portal.

## When it runs

`App._Ready` adds the node on normal runs only: never on smoke runs, headless runs or background windows
(`--svm-background`, `SVM_BACKGROUND`, `SVM_PLAY_SHOTS`). Options → HUD → "Discord status" (`discord_presence` in
`user://settings.json`, on by default) turns it on and off live; off clears the status.

## What it shows

Checked once a second; sent only when something changed (the elapsed timer restarts when the first line changes).

| Where | First line | Second line | Party |
|---|---|---|---|
| menus | In the menus / Browsing servers / Setting up a game / Setting up an offline match / Looking for a hub / In the options / Looking at the results | | |
| offline match | Offline: *mode* on *map* | Spy · Spies 2 – 1 Mercs · vs bots | |
| online, connecting | Joining *server* | *mode* on *map* | players / max |
| online lobby | In the lobby of *server* (Hosting *server* for the host) | *mode* on *map* | players / max |
| online round | *mode* on *map* | Spy · Spies 2 – 1 Mercs · *server* | players / max |
| online results | Match over on *server* | *mode* on *map* | players / max |
| hub | In the hub: *name* | Spy / Mercenary | players / max |

Images: the large image is `logo` (hover: "OpenSCCT *version*"), the small one `spy` or `merc` for the player's side.
Upload three Rich Presence art assets with exactly those keys under the app's Rich Presence → Art Assets in the
Developer Portal: `tools/branding/discord/logo.png`, `spy.png` and `merc.png` (1024 px, drawn by
`python tools/branding/make_discord_art.py` in the look of the game's mark).

## Join

A joinable game puts a join secret on the status (Discord passes it only to friends the player let in):

| Secret | Meaning | Set when |
|---|---|---|
| `oscct1\|m\|<id>` | a server on the master, by its id | joined from the master's list, or hosting a server the master registered |
| `oscct1\|a\|<host:port>` | a server by address | joined by a typed public address, or hosting with a public address but no master id |
| `oscct1\|h\|<host:port>` | a hub by address | in a hub joined by address |
| `oscct1\|mp\|<id>`, `oscct1\|ap\|<host:port>` | as `m` and `a`, for a server with a password | the same, when the server has one |

A password server's secret only says that it has one: the password never goes to Discord. On such a Join the
friend's game asks for it: a master id opens the server browser with "... has a password. Type it in the password
box and press Join", and an address opens Join with IP with the address filled in and the password box focused.
Builds older than this ignore the `mp` and `ap` kinds. No secret (status only) for a hub pad's match, a hub this PC
hosts (loopback only), offline play, or a loopback/LAN-only address.

On Join the game waits until the player is in the menus (a running match finishes or is left first), leaves the
current visit, and goes the normal way: a master id opens the server browser and asks the master how to reach it
(NAT punch, relay and sign-in tickets as for a browser join), an address joins directly, a hub opens the hub page and
joins on the spy side with the player's name.

Discord starts the game for a friend who does not have it running through `discord-<app id>://`, registered under
the current user's classes on each run of an exported Windows build (not from the editor). The command is the exe
alone; the join arrives over the pipe once the game is up.

## Open items

- Join and Ask to Join over the local pipe are the old Rich Presence API: check them with a second Discord account.
- Per-map large images (asset key per map) once the art exists.
