# Network test with two PCs

Use this checklist to test a real host, guest, network and pair of microphones. It
covers joining, lobby settings, voice, a revenge pair, reconnecting and server bots.
For automated local runs, use the [network lab](NETWORK-LAB.md) or the smoke commands
below. Local tests do not establish behavior between physical PCs.

[Documentation](README.md) · [Testing](TESTING.md) · [Online setup](ONLINE.md)

## 1. Before you start (both PCs)

- Install the same build and maps. The browser marks incompatible builds; the lobby
  reports missing maps or different map versions.
- Set distinct names in **Options > Player name**.
- Connect a headset and microphone. In **Options > Voice**, choose a device and use
  **Test my microphone**. If nothing is captured, check Windows microphone access
  for desktop apps and the selected communications device.
- Record the build, map, network type and test start time.

## 2. Network

| Default port | Protocol | Host use |
|---|---|---|
| 28888 | UDP | Game and voice |
| 28889 | UDP | LAN discovery |

Allow OpenSCCT through the host's firewall on the network profile you use. For a
private network, an administrator can add the rule in PowerShell:

```powershell
New-NetFirewallRule -DisplayName "OpenSCCT UDP" -Direction Inbound -Protocol UDP -LocalPort 28888,28889 -Action Allow -Profile Private
```

On the same LAN, the guest can use **Find a Session** or **Join with IP** and the host's
LAN address (`ipconfig` shows its IPv4 address).

For a direct internet connection, forward UDP 28888 to the host PC. **Map the port
(UPnP)** can do this on supported routers; check its result in the lobby. The guest
joins the host's public address. For server discovery, NAT traversal or relay setup,
see [online play](ONLINE.md).

## 3. The test

### Host and join

1. Host: choose **Create a Session**, a map and **Story** mode. Set a password, four
   players, a one- or two-minute game, **Revenge** on and **Voice in Lobby** on.
2. Use **Modern** voice first and repeat with **Open** voice. Set the server name and
   port, then create the session.
3. Guest: find the session or use **Join with IP**, enter the password and join.
4. Both: check names, teams, ping and session parameters match on both screens.

### Lobby

- Speak in turn. Check the speaking icons and sound on the other PC.
- Change the other player's volume, mute them, then unmute them. Confirm each change.
- While on opposite teams, enable **Restrict Mic to Team** and confirm the other
  player stops hearing you; then disable it.
- Send **Say** and **TeamSay** messages.
- Guest: press **Ready**. Host: change **Respawn Length** and press **Apply**. Check
  that the guest becomes **Not Ready** and the changed value blinks. Ready again.

### Match and return

1. Host: press **Launch**. Both players should load and enter the map.
2. Play and check voice. **Open** allows cross-team speech; **Modern** uses team voice
   with the game's special cases, including grabs and hacked communications.
3. Confirm lobby volume settings still apply and the voice overlay identifies speakers.
   Press F8 to mute your microphone and check the on-screen message.
4. Finish the round and press **Next** on both PCs. The revenge round swaps teams.
   Finish it and return to the lobby.
5. Guest: leave, rejoin with IP, then leave again. Host: confirm each change appears.

Record freezes, missing screens, incorrect team/ready state and voice problems with
the time they happened. Repeat the same steps in the other voice mode.

## 4. With bots

Bots run on the server. The guest receives their pawns through the same replication
path as other players.

1. Host: create a four-player, two-minute session with revenge enabled. Choose
   **Bots: Fill Slots** and **Bot Skill: Easy**. Alternatively, use **Per Side** with
   one bot per side.
2. With Fill Slots, the host starts beside SpyBot1, with MercBot1 and MercBot2 on the
   other side. Bot ready rows should say **Bot**.
3. Guest: join the mercenaries. One bot should leave to make room. Each side now has
   one person and one bot; bot rows have no volume slider or Kick control.
4. Guest: switch teams and back. Check bots keep the sides balanced.
5. Ready and launch. Both PCs should show moving, playing bots. Only human players
   should appear in the voice overlay.
6. Complete the revenge pair. Bots should swap sides for revenge and return to their
   lobby sides afterward.
7. Guest: leave. Check a bot fills the slot. Host: set **Bots: Off** and apply; the bot
   rows should disappear.

The round must not wait for bots to ready up. The host log records each round as
`SVM_HOST <n> Easy bots on <map>`.

The automated bot smoke covers one game without revenge:

```powershell
pwsh tools/ci/godot-smoke.ps1 -Tests net-bots -TimeoutSeconds 600
```

## 5. Logs

Each PC writes its latest log here, with older runs beside it:

```text
%APPDATA%\Godot\app_userdata\OpenSCCT\logs\godot.log
```

`SVM_HOST` lines describe session events; `SVM_VOICE` lines describe microphone activity.
To keep separate logs, create the destination folder and launch each PC with:

```powershell
# Host
OpenSCCT.exe --log-file C:\temp\openscct-host.log
# Guest
OpenSCCT.exe --log-file C:\temp\openscct-guest.log
```

Include both logs, the build and map, event times and relevant screenshots in a bug
report. Remove private account or connection details before posting publicly.

## 6. One machine, automatically

```powershell
pwsh tools/ci/godot-smoke.ps1 -Tests net-lobby -TimeoutSeconds 900
```

This smoke uses two game processes on ports 28988 and 28989, drives the lobby flow
through the menus and supplies a 440 Hz tone as the guest microphone. The guest log
is `local/net-lobby/guest.log`.

For a windowed capture run, `SVM_AUDIO_MUTE=1` mutes playback and
`SVM_PLAY_SHOTS=<folder under local/>` enables screenshots at each step. See
[player-path testing](TESTING-PLAY.md) for the harness and its limits.
