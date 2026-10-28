# Network test with two PCs

A short plan to play OpenSCCT between two computers: player 1 hosts, player 2 joins, both meet in the lobby, set up
the match, talk, play a revenge pair and come back to the lobby; then the same with server bots filling the sides
(section 4). The same steps run automatically on one machine in
the `net-lobby` smoke (`docs/MENUS.md`); this is the version with real people, real networks and real microphones.

## 1. Before you start (both PCs)

- The same build on both: copy the same `OpenSCCT` folder (or the same commit's export) to both PCs. The server
  browser greys out another build ("another build"), and the lobby names a player whose map differs.
- The same converted maps: copy `.content` (or the release's `maps` folder) from PC 1 to PC 2, so the map content
  hashes match. The lobby says "missing map" or "different map version" otherwise.
- A headset with a microphone on each PC. In Options, Voice: pick the microphone under Device (or leave
  "System default"), press Test my microphone, speak, and listen to the playback. If it says "Nothing was captured",
  check Windows' privacy settings (Microphone access for desktop apps) and the communications device.
- Each player sets his own name in Options (Player name), so the lobby can tell them apart.

## 2. Network

| Port | Protocol | Who | What |
|---|---|---|---|
| 28888 | UDP | the host | the game and voice (one socket) |
| 28889 | UDP | the host | LAN search answers |

Windows Firewall on the host: the first time OpenSCCT hosts, Windows asks to allow it; allow Private networks (and
Public if the PCs are on a network Windows calls public). Or add the rule by hand in an administrator PowerShell:

```
New-NetFirewallRule -DisplayName "OpenSCCT UDP" -Direction Inbound -Protocol UDP -LocalPort 28888,28889 -Action Allow -Profile Private
```

- **Same LAN (home network):** nothing else to do. Player 2 finds the game with Find a Session, or types the host's
  LAN address (`ipconfig`, "IPv4 Address", for example 192.168.1.20) in Join with IP.
- **Over the internet:** the host's router must pass UDP 28888 to the host PC. Create Session's "Map the port (UPnP)"
  does it on most routers (the lobby says whether it worked); otherwise forward UDP 28888 by hand to the host's LAN
  address. Player 2 joins with Join with IP and the host's public address (search "what is my IP" on the host).
  Behind carrier-grade NAT no port can be opened: use a master server with its relay (`docs/ONLINE.md`, "Deploying on
  a VPS"), list the game on it, and player 2 finds it under Find a Session with the same master and invite key.

## 3. The test

Player 1 (host):

1. Main menu, Create a Session.
2. Choose Map and Game Mode (Story), then set: Password (any, for example `swordfish`), Voice Chat (try Modern, then
   Open in a second session), Max Players 4, Game Length 1 or 2 minutes for a quick test, Revenge on, Voice in Lobby on.
3. Server Name, UDP port 28888, UPnP on for the internet; Create. The Lobby Room opens with you on the Spies.

Player 2:

4. Main menu, Find a Session (LAN), pick the game (it shows a lock: it has a password), type the password, Join. Or
   Join with IP: Server Address, Server Port 28888, Password, Join.
5. The Lobby Room opens. Check Session Parameters show what player 1 set.

Both, in the lobby:

6. Talk: each sees the other's speaking icon and hears him. Each moves the other's volume slider (for example to 40%)
   and hears the change; tick Mute and the voice stops; untick it.
7. Player 2 ticks Restrict Mic to Team while on the other side: player 1 stops hearing him; untick it.
8. Chat with Say and TeamSay.
9. Player 2 presses Ready. Player 1 changes a value (Respawn Length) and presses Apply: player 2 becomes Not Ready and
   the value blinks; player 2 presses Ready again.
10. Player 1 presses Launch: the loading page, then the map. Play; in Open mode you hear each other across sides, in
    Modern only your own side (and the special cases: grab talk, hacked coms). The volumes from the lobby still apply;
    the overlay at the top right shows who speaks and at which volume. Press F8 to mute your microphone and see
    "Your microphone is muted".
11. When the round ends: the end banner, the results, Next on both. Revenge plays the second game with the sides
    swapped. After it, Next again: both are back in the Lobby Room.
12. Player 2 presses Leave, joins again with Join with IP, and leaves. Player 1 sees him go and come back.

What to check at each step: the screen you expect opens; names, sides, Ready and ping are right on both PCs; voices
are heard at the volume set; nothing freezes. Write down anything that differs, with the time.

## 4. With bots

The host can fill the match with server bots. They play on the host's PC with the game's AI; player 2 only receives
their pawns, as for any other player.

Player 1 (host):

1. Create a Session as above, with Max Players 4, Game Length 2 minutes, Revenge on. In the rules at the bottom
   right: **Bots** Fill Slots, **Bot Skill** Easy (or Per Side with Bots per Side 1, for one bot on each side whoever
   joins). Create.
2. The Lobby Room shows you on the Spies with SpyBot1, and MercBot1 and MercBot2 on the Mercenaries; a bot's ready
   column reads "Bot".

Player 2:

3. Join (LAN list or Join with IP) on the Mercenaries: MercBot2 leaves to make room. Each side now has one person and
   one bot. A bot's row has no volume slider and no Kick.
4. Switch Team to the Spies and back: the bots move to keep two on each side.

Both:

5. Player 2 Ready, player 1 Launch. In the round the bots move, hack and shoot; both of you see them. Talk: only the
   two of you are heard (the voice overlay never lists a bot).
6. After the results, Next: the second game of the revenge pair has everyone swapped, bots included. After it, back
   in the Lobby Room the bots are on their own sides again.
7. Player 2 leaves: a bot takes his place among the Mercenaries. Player 1 sets Bots Off and presses Apply: the bot
   rows go.

Check the bots' names and sides on both PCs, that they play (not standing still), and that the round starts without
waiting for them. The host's log has a line `SVM_HOST <n> Easy bots on <map>` for every round.

The same on one machine, automatically (one game, no revenge):

```
pwsh tools/ci/godot-smoke.ps1 -Tests net-bots -TimeoutSeconds 600
```

## 5. Logs

Each PC writes its log to the Godot user folder:

```
%APPDATA%\Godot\app_userdata\OpenSCCT\logs\godot.log        (the newest; older runs beside it)
```

Lines starting with `SVM_HOST` are the host's session (joins, travels, kicks, the setup), `SVM_VOICE` the microphone.
To keep a run's log apart, start the game with a log file of its own:

```
OpenSCCT.exe --log-file C:\temp\openscct-host.log        (player 1)
OpenSCCT.exe --log-file C:\temp\openscct-guest.log       (player 2)
```

Send both logs, the times of anything odd, and a screenshot of each screen that looked wrong.

## 6. One machine, automatically

```
pwsh tools/ci/godot-smoke.ps1 -Tests net-lobby -TimeoutSeconds 900
```

It starts two game processes (ports 28988 and 28989, so it never meets a game hosted on 28888) and plays steps 1 to 12
through the menus, with a 440 Hz tone for player 2's microphone. Player 2's log is `local/net-lobby/guest.log`. With a
window, `SVM_AUDIO_MUTE=1` and `SVM_PLAY_SHOTS=<folder under local/>` both players save a picture at every step.
