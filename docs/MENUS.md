# Menus

The front end: the main menu, Create Session, Find a Session (the server browser), Join with IP, the Lobby Room, the
loading page, the results, and the options' Voice page. The rules come from the original's own pages, decoded in
`spec/game/lobby-and-match-creation.md` (Enhanced 4.0's GUI package and Reloaded's voice page); the session behind them
is in `docs/ONLINE.md`, voice in `docs/VOICE.md`, and the way to test it with two computers in `docs/NETWORK-TEST.md`.

| Path | What it holds |
|---|---|
| `game/Scripts/UI/MenuPage.cs` | the PC page look: background, typed title, corner brackets, framed panels, page buttons, `Shot` |
| `game/Scripts/UI/UiStyle.cs` | the look switch (`Look`), `MenuTheme` (the PC button and list boxes, the style colours), `MenuBackdrop`, `MenuTexture` |
| `game/Scripts/UI/UiArt.cs`, `UiBox.cs`, `UiCorner.cs`, `UiRule.cs`, `UiWordmark.cs` | the modern look: our own art, drawn at the window's size |
| `game/Scripts/UI/MainMenu.cs` | Create a Session (also how to play offline), Find a Session (joining by IP is on that page), Account, Achievements, Armoury, Options, Quit |
| `game/Scripts/UI/RankIcons.cs` | Rank pictures drawn from SVG (level insignia, prestige and master emblems) and `RankBadge` (emblem, level, title), used by the menus, lobby, results, F8 and hub tags |
| `game/Scripts/UI/AccountScreen.cs` | Account: sign in with Discord (code, browser, wait), pick or change the name, the rank (emblem, level, XP bar, Prestige, prestige emblems, title picker; spec/additions/progression.md), stats, an achievements summary with links to Achievements and the Armoury, sign out (docs/ONLINE.md "Accounts") |
| `game/Scripts/UI/AchievementsScreen.cs` | Achievements (OpenSCCT's own): every achievement by group (filter buttons), what to do, what it unlocks, progress or earned date; the list is the shared catalogue, so it is complete even with an older master |
| `game/Scripts/UI/ArmouryScreen.cs` | Armoury (OpenSCCT's own): the spy's and merc's cosmetics per slot; locked items name the achievement that unlocks them (docs/ONLINE.md "Cosmetics") |
| `game/Scripts/UI/HostScreen.cs` | Create Session: the page's rows plus hosting (server name, UDP port, master, listing, UPnP) |
| `game/Scripts/UI/CreateSessionModel.cs` | the Create Session rules without Godot (rows, captions, ranges, mode rules, password) |
| `game/Scripts/UI/SessionRowsView.cs` | the rows drawn with the original's arrows and boxes; also the lobby's Session Parameters |
| `game/Scripts/UI/LobbyRoomScreen.cs` | the online Lobby Room |
| `game/Scripts/UI/LobbyRoomModel.cs` | the Lobby Room rules without Godot (page button, blinking changes, chat spam delay) |
| `game/Scripts/UI/JoinWithIpScreen.cs`, `ServerBrowserScreen.cs`, `ConnectingScreen.cs`, `ResultsScreen.cs` | Join with IP, Sessions, the loading page, the end menu |
| `game/Scripts/UI/LobbyScreen.cs`, `LobbySession.cs` | the offline lobby (bots fill the 2v2) and the lobby interfaces |
| `game/Scripts/Options/OptionsMenu.Voice.cs` | the Voice page and Test my microphone |
| `game/Scripts/Online/OnlineVoiceChat.cs` | the visit's voice: microphone, keys, settings, per-player volume and mute, the round's overlay |
| `game/Scripts/Options/OptionsMenu.cs` | the options pages; HUD holds "Discord status" (docs/DISCORD.md) |
| `game/Scripts/UI/MenuPilot.cs` | drives the menus with input events (clicks, typing) for the smokes |
| `game/Scripts/Online/NetLobbySmoke.cs` | the two-process `net-lobby` smoke |

## Look

The original's PC menus are UnrealScript GUI pages (GUI.u), not Magma pages, so there is no page data to play; they
are rebuilt with the original's textures and colours. The `ui` content step exports the textures the pages use:
GUIContent `fond` (the page background), `MN_cadre03` (the corner brackets), `loading`, the other frames, and Menu_PC
`buton_select` / `buton_validate` / `buton_push` (sSTY_PcButton: normal, focused, pressed), `TABS_NOselect`
(sSTY_PcList: edit boxes and lists), `Saquare_BOX` (panels) and `CommunicatorON` (the speaking icon). Text colours are
the styles' FontColors (58, 73, 58 normal; 93, 117, 92 focused) and the page title's (69, 85, 69), doubled: every
style draws its images at 128, which reads as neutral, so the colours are taken to be on the same scale. That doubling
is an estimate: the original's menus cannot be captured under the test rules (only the editor's Play Level may run).
Page titles type out at ten letters a second with a blinking cursor (GUIPage.MyDrawPage); page buttons sit along the
bottom, B first and A last. Without converted content the pages fall back to plain colours.

That is the original look. The default is our own modern look (`UiLook.Modern`): the same layout and behaviour, drawn
by the game at the window's size so it stays sharp at 1080p, 1440p and 4K, with no texture from the original.

| Part | How it is drawn |
|---|---|
| page background | a shader (`UiArt.Backdrop`): dark green-black gradient, a 32 px grid with a 128 px grid and small crosses over it, scanlines, a slow sweep down the page, grain and a vignette; sizes follow the window height |
| panels, buttons, map tiles, edit boxes | `UiBox`, a StyleBox drawn with lines: dark fill, thin border, two cut corners, brighter corner ticks, faint scanlines; hover adds a left accent bar, pressed fills green, focus draws an outline around the control, disabled fades |
| corner brackets, rules | `UiCorner` (four brackets, in place of MN_cadre03) and `UiRule` (under the title and above the page buttons) |
| check boxes, radio items, toggles, slider grabbers, the list arrow | SVG written in `UiArt`, rasterised by `DpiTexture` at the scale it is shown |
| sliders, scroll bars, tabs, lists, popups, tooltips | flat boxes in the same colours (`UiArt.BuildTheme`) |
| text | Inter (text and titles: weight 500, titles 640, spaced capitals with a faint glow) and JetBrains Mono (panel captions), both under the SIL Open Font License, in `game/branding/fonts/` with their licence files |
| OpenSCCT wordmark | `UiWordmark`: the mark of `game/branding` (ring, scan gap, centre dot, four ticks; the proportions of `tools/branding/make_branding.py`) drawn with lines, then "OPEN" light and "SCCT" bold |

The look is picked by `--svm-ui-look=original|modern`, then `SVM_UI_LOOK`, then `look` under `[ui]` in
`user://ui.cfg`; the default is modern. `UiStyle` holds the switch (`UiStyle.Look`) and the shared colours
(`MenuText`, `MenuFocus`, `MenuTitle`), which follow the look. In the modern look `MenuTexture` returns our own
drawing for the textures pages show as icons (the Lobby Room's speaking icon) and nothing for the rest. The `menu-art` smoke is a preview, not a check: with a window
(`--svm-background`) and `SVM_PLAY_SHOTS` it saves the main menu at 720p, 1080p and 1440p, Create Session, Join with IP
and every options tab.

```
set SVM_AUDIO_MUTE=1 & set SVM_PLAY_SHOTS=C:\...\local\menu-art
tools\godot\4.7.2-stable\Godot_v4.7.2-stable_mono_win64_console.exe --path game -- --svm-smoke=menu-art --svm-background
```

The loading page between maps is the original's (GUI.loading): "Loading...", the mode's picture (common `L_Story`,
`L_diskhunt`, `L_Death`) and name, and the map's explanation of the mode for the player's side
(SMapSettings.ModesStrings). The results open with the HUD's `P_EndGame` banner (a Magma page), then the end menu.

## Flow

```
Main menu ── Create a Session ──> Create Session ── Create ──> Lobby Room (host)
          ── Find a Session  ──> Sessions (LAN, saved, master) ── Join ──> Loading ──> Lobby Room
          ── Join with IP    ──> Server Address, Server Port, Password ── Join ──┘
          ── Account         ──> Sign in with Discord ──> (browser) ──> pick a name ──> stats, achievements
Lobby Room ── Launch (all ready) ──> Loading ──> round ──> results ── Next ──> next game of the series, or the Lobby Room
```

## Create Session

Rows, captions and ranges are the page's (spec rules 1 to 9):

| Row | Range | Notes |
|---|---|---|
| Choose Map | the installed maps | changing it reloads the rules from the map's settings for the mode |
| Game Mode | the map's modes, then Merc Deathmatch | changing it reloads the rules |
| Password | 16 of `a-z A-Z 0-9 . _ -` | |
| Voice Chat | Modern, Classic, Team, Open | the hint under it: "Team always - hackable coms", "Living team - hackable coms", "Team always - no com hacking", "Everyone hears everyone" |
| Max Players | 2 to 8 (4) | |
| Revenge | | two games, sides swapped for the second; hides Games in a Row |
| Random Map | | after the series, the next map is drawn at random (never the same one twice in a row) |
| Voice in Lobby | on | players talk in the lobby |
| Games in a Row | 1 to 10 | |
| Game Length (min) | 0 to 20, 0 is "Inf." | |
| Spy Lives ("Kills to Win" in Merc Deathmatch) | 0 to 10 ("Inf."), kills 1 to 100 | |
| Mercenary Lives | 0 to 10 ("Inf.") | |
| Respawn Length (sec) | 1 to 15 | the map's own value is at least 5 (1 in Merc Deathmatch) |
| Active Disks, Disks to Take | 1 to the map's most | Disk Hunt only; Disks to Take never above Active Disks |

Added for hosting from the game (the original took them from Ubi.com or the LAN service): server name, UDP port
(28888), the master server, list on the master, map the port with UPnP. The page keeps its last values
(`user://online.cfg`, `[host] setup`).

Every page names a map the same way: the catalog's name of the original's map list (ENHANCED STEEL SQUAT,
docs/MAPS.md), else its id (`UI/MapTitles.cs`): Create Session, the lobby, the server browser's Map column and the
loading page. The server's own chat line ("Next: ...") still names the id. The loading page of a round shows the
mode and the map's words only; the join steps (direct, NAT punch, relay) show while joining.

## Lobby Room

- Spies and Mercenaries lists ("Waiting for players" when a side is empty), spectators below. Each row: the speaking
  icon, name (host mark, "(mic: team)" for a player who restricted his mic, a missing or different map), Ready / Not
  Ready, ping; for other players a volume slider (0 to 200%), Mute and, for the host, Kick ("Do you want to kick ...?").
- Switch Team (not in Merc Deathmatch, not while ready), Spectate, Restrict Mic to Team, and a line about the local
  microphone (transmitting or silent, push to talk or voice activation, the level).
- Session Parameters: the Create Session rows. The host edits and presses Apply; any change un-readies the others
  (the original's clients un-ready themselves), whose changed values blink for three seconds.
- Chat: Say and TeamSay ("(Team) " prefix); a line within a second of the last one is not sent: "(Spam delay, 1 second)".
- Page buttons: Leave, Options (the options over the lobby), and Launch / Cancel for the host or Ready / Not Ready for
  the others. Launch needs everyone on a side ready; a player without the map, or with another version of it, is named.

Per-player volume and mute (rl:voice.mix) are Reloaded's: its overlay sets a player's volume with the scroll wheel and
mutes with a click. The rewrite puts the same control on each lobby row and shows Reloaded's overlay in the round
("Guest - volume 40%", "Your microphone is muted"). Choices last for the visit, kept by player name.

## Voice page

Reloaded's voice page, with the rows the rewrite has: Device (the system's default or a named microphone), Enable voice
chat, Voice Chat Volume, Buffering (jitter frames 1 to 12), Mute microphone, Microphone Volume, the microphone level
bar with transmitting or silent, Activation threshold, Release time, Push to talk with the bound keys (Caps Lock, team
Y, mute F8), and Test my microphone (three seconds through gain, gate, Opus, a relay and the jitter buffer, then played
back). Settings keys are Reloaded's (`voice_push_to_talk`, `voice_activation_threshold`, `voice_activation_hold_ms`,
`voice_input_gain`, `voice_output_volume`, `voice_jitter_frames`, `voice_muted`, `voice_input_device`) plus
`allow_voice_chat`. Not built yet, so not shown: Auto-Adjust Mic Volume, Noise suppression, Echo cancellation.
The key rows read "Bind: Capslock" as Reloaded's page writes them ("Bind: %s"). The options pages scroll up and down
only, and a long device name is clipped to its column. The Display mode list reads "Borderless fullscreen" for
Reloaded's `BorderlessFullscreen` value; the Keys page names the layout file, not its whole path.

## Tests

- `tests/OpenScctGame.Logic.Tests/MenuCreateSessionTests.cs`, `MenuLobbyRoomTests.cs`: the page rules.
- `tests/OpenScct.Integration.Tests/LobbyOptionsTests.cs`, `LobbyVoiceTests.cs`: the options and voice through the
  session (SimLink).
- `net-lobby` smoke: two game processes over UDP on localhost, driven only through the menus and keys
  (`game/Scripts/Online/NetLobbySmoke.cs`; steps in its summary). With a window and `SVM_PLAY_SHOTS`, each player saves
  its window at every step.

```
pwsh tools/ci/godot-smoke.ps1 -Tests net-lobby -TimeoutSeconds 900
set SVM_AUDIO_MUTE=1 & set SVM_PLAY_SHOTS=C:\...\local\net-lobby\shots
tools\godot\4.7.2-stable\Godot_v4.7.2-stable_mono_win64_console.exe --path game -- --svm-smoke=net-lobby --svm-smoke-timeout=880
```

## Differences from the original

- Host options the original had that the rewrite does not show: Alternate Mode, Tutor Mode, Game Type (Free or Ranking)
  and Level Gap (Ubi.com and console features), Lock / Unlock (Ubi.com matches), Equipment, Briefing, Friends.
- The original's random map pool is the stock maps numbered 01 to 12 without 07; the rewrite draws from the installed
  maps that have the mode.
- Launch: the original waits (launching) when not everyone is ready; the rewrite says who is not ready. Reloaded lets a
  host start with an empty side, and so does the rewrite.
