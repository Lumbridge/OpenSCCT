# Discord status and joining friends

[Documentation](README.md)

OpenSCCT can show your current map, mode and server on your Discord profile. Friends can use **Join**
or **Ask to Join** to reach the same server or hub. Enable or disable this in **Options → HUD → Discord status**;
turning it off clears the status immediately.

This feature talks to the Discord desktop app on the same PC. It does not require a separate sign-in,
and the game works normally when Discord is closed. Account sign-in through Discord is a separate
feature described in [Accounts](ONLINE.md#accounts).

## Implementation

| Location | Responsibility |
| --- | --- |
| `game/Scripts/Discord/DiscordPresence.cs` | Client lifecycle, status updates, launch registration and joins |
| `game/Scripts/Discord/DiscordInvitePipe.cs` | Compatibility fix for obsolete fields in the library's outgoing invite data |
| `game/Scripts/Discord/PresenceSnapshot.cs` | Current menu, match and hub state |
| `game/Scripts/Discord/DiscordJoinSecret.cs` | Join-secret format |

The managed `DiscordRichPresence` package communicates over Discord's local pipe. The OpenSCCT application
ID is `1564377811014389862` (`DiscordPresence.ApplicationId`).

Version `1.6.1.70` serializes an obsolete `MatchSecret: null` property inside the activity's `secrets`.
This matches [upstream issue #286](https://github.com/Lachee/discord-rpc-csharp/issues/286#issuecomment-4317214867):
Discord offers **Invite to Play**, but sending it produces a blank message and Clyde's delivery error.
`DiscordInvitePipe` removes only the obsolete `MatchSecret`/`match` fields from outgoing `SET_ACTIVITY` secrets;
the join secret and the rest of the RPC exchange still use the library normally. Remove this adapter after
upgrading to a package that omits those fields and verifying its serialized output and live invites.

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

## Validation and future work

- `DiscordInvitePipeTests` checks the installed package's serialized invite payload through the compatibility pipe,
  preservation of join data and unrelated frames, and presence clearing. These checks do not send Discord messages;
  actual delivery and joining still require a live check with a second account.
- Join and Ask to Join over the local pipe are the old Rich Presence API: check them with a second Discord account.
- Per-map large images (asset key per map) once the art exists.
