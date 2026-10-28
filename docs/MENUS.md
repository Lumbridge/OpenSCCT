# Menus

The front end: the main menu, Create Session, Find a Session (the server browser), Join with IP, the Lobby Room, the
loading page, the results, the options' Voice page, and the Tutorials pages (tutorial maps, tips, gadget details, the
HUD and keyboard pictures, Visit a Map). The rules come from the original's own pages, decoded in
`spec/game/lobby-and-match-creation.md` (Enhanced 4.0's GUI package and Reloaded's voice page); the session behind them
is in `docs/ONLINE.md`, voice in `docs/VOICE.md`, and the way to test it with two computers in `docs/NETWORK-TEST.md`.

| Path | What it holds |
|---|---|
| `game/Scripts/UI/MenuPage.cs` | the PC page look: background, typed title, corner brackets, framed panels, page buttons, `Shot` |
| `game/Scripts/UI/UiStyle.cs` | the look switch (`Look`), `MenuTheme` (the PC button and list boxes, the style colours), `MenuBackdrop`, `MenuTexture` |
| `game/Scripts/UI/UiArt.cs`, `UiBox.cs`, `UiCorner.cs`, `UiRule.cs`, `UiWordmark.cs` | the modern look: our own art, drawn at the window's size |
| `game/Scripts/UI/MainMenu.cs` | Create a Session (also how to play offline), Find a Session (joining by IP is on that page), Account, Achievements, Armoury, Tutorials, Options, Quit |
| `game/Scripts/UI/Tutorials/` | Tutorials (`TutorialsScreen`), the tutorial maps (`TutorialMapsScreen`), Tips (`TipsScreen`), Gadget Details (`GadgetDetailsScreen`), View HUD and the keyboard pictures (`HudLegendScreen`), Visit a Map (`VisitMapScreen`), the lobby's first-time box (`FirstTimeMapPrompt`) |
| `game/Scripts/Tutorials/` | the tutorial progress file (`TutorialProfile`, `user://tutorials.json`), the pages' words (`TutorialText`), offline play of a tutorial or a visit and the round's end (`TutorialPlay`, `TutorialRoundWatch`) |
| `game/Scripts/UI/RankIcons.cs` | Rank pictures drawn from SVG (level insignia, prestige and master emblems) and `RankBadge` (emblem, level, title), used by the menus, lobby, results, F8 and hub tags |
| `game/Scripts/UI/AccountScreen.cs` | Account: sign in with Discord (code, browser, wait), pick or change the name, the rank (emblem, level, XP bar, Prestige, prestige emblems, title picker; spec/additions/progression.md), stats, an achievements summary with links to Achievements and the Armoury, sign out (docs/ONLINE.md "Accounts") |
| `game/Scripts/UI/AchievementsScreen.cs` | Achievements (OpenSCCT's own): every achievement by group (filter buttons), what to do, what it unlocks, progress or earned date; the list is the shared catalogue, so it is complete even with an older master |
| `game/Scripts/UI/ArmouryScreen.cs` | Armoury (OpenSCCT's own): the spy's and merc's cosmetics per slot; locked items name the achievement that unlocks them (docs/ONLINE.md "Cosmetics") |
| `game/Scripts/UI/HostScreen.cs` | Create Session: the page's rows plus hosting (server name, UDP port, master, listing, UPnP) |
| `game/Scripts/UI/CreateSessionModel.cs` | the Create Session rules without Godot (rows, captions, ranges, mode rules, password) |
| `game/Scripts/UI/SessionRowsView.cs` | the rows drawn with the original's arrows and boxes; also the lobby's Session Parameters |
| `game/Scripts/UI/LobbyRoomScreen.cs` | the online Lobby Room |
| `game/Scripts/UI/StrategicMapScreen.cs`, `StrategicMapModel.cs` | the Strategic map page (in a match and from the Lobby Room) and its rules without Godot |
| `game/Scripts/UI/LobbyRoomModel.cs` | the Lobby Room rules without Godot (page button, blinking changes, chat spam delay) |
| `game/Scripts/UI/JoinWithIpScreen.cs`, `ServerBrowserScreen.cs`, `ConnectingScreen.cs`, `ResultsScreen.cs` | Join with IP, Sessions, the loading page, the end menu |
| `game/Scripts/UI/LobbyScreen.cs`, `LobbySession.cs` | the offline lobby (bots fill the 2v2) and the lobby interfaces |
| `game/Scripts/Options/OptionsMenu.Voice.cs` | the Voice page and Test my microphone |
| `game/Scripts/Online/OnlineVoiceChat.cs` | the visit's voice: microphone, keys, settings, per-player volume and mute, the round's overlay |
| `game/Scripts/Options/OptionsMenu.cs` | the options pages; HUD holds "Discord status" (docs/DISCORD.md) |
| `game/Scripts/UI/ConfirmBox.cs`, `LoadingTip.cs` | the yes/no question over a page (quit, keep the screen settings); the loading pages' tip |
| `game/Scripts/Online/RoundStartList.cs` | the round's "Waiting for players" list (Loading_Menu) until the match begins |
| `game/Scripts/Options/BrightnessTest.cs` | the brightness test pattern (GUI.Mire) |
| `game/Scripts/Audio/FrontEndAudio.cs` | the menu and lobby music and the lobby's join sound |
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
(SMapSettings.ModesStrings). Under them a tip for that side (enh:ui.tips, `LoadingTip`): one of the team class's
aTipStrings, which Enhanced rewrote (silent movement, proxy mine speed, EMF ghosting; converted from SBase.int into
`shared/classes.json`), with its key tokens named from the player's layout. The original shows those tips only on the
death page. The results open with the HUD's `P_EndGame` banner (a Magma page), then the end menu; in Merc Deathmatch
its headline is Reloaded's line instead of a side: "*name* wins with *n* kills", "Draw at *n* kills" or "No kills
scored".

Once a network round's map is in, the original's Loading_Menu list (`game/Scripts/Online/RoundStartList.cs`,
spec/game/match-lifecycle.md "Waiting for players") stays over the round's view until the match begins: "Waiting for
players (ready / expected)" and a line per player of the session, "is ready" when his PRI reports waiting to play
(`bWaitingPlayer`, replicated), "is connecting" while he loads, "Loading..." when he has no PRI on the map yet, and
"Player timed out..." for one dropped (or gone) while the round loaded. The server already sent all of it (the PRIs
and the session's player list); nothing new travels.

The main menu's Quit asks first (GUI.ConfirmQuitPage, `ConfirmBox`): "Are you sure you want to quit?", Cancel or
Quit Game; Escape cancels.

## Menu music and sounds

The menus play the original's menu-level music (`game/Scripts/Audio/FrontEndAudio.cs`, docs/AUDIO.md "Front end"):
the Entry level's Song (Menu_music1) on the main menu and the pages under it, the Lobby level's (Menu_music2, the
longer track, enh:audio.lobby_music) in the lobby, nothing on the loading pages, the results or in a round. The
lobby plays a beep when a player joins (rl:qol.lobby, rl:audio.join). The songs come from the converted Entry and
Lobby maps (the maps step); without them the menus are silent.

## Flow

```
Main menu ── Create a Session ──> Create Session ── Create ──> Lobby Room (host)
          ── Find a Session  ──> Sessions (LAN, saved, master; the selected one's players by side) ── Join ──> Loading ──> Lobby Room
          ── Join with IP    ──> Server Address, Server Port, Password ── Join ──┘
          ── Account         ──> Sign in with Discord ──> (browser) ──> pick a name ──> stats, achievements
          ── Tutorials       ──> View HUD, Tips, Gadget Details, Spy / Mercenary Tutorial Maps ── a map ──> Loading ──> tutorial
                             ──> Visit a Map ── Launch ──> Loading ──> the map alone, help paths shown ──> (end) Tutorials
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
| Auto balance | off | Game_Settings' row (hidden and always on in the original): a joiner asking for the bigger side goes to the smaller one, and nobody may switch to a side that would then be two people ahead |
| Alternate | off | Alternate Mode; the "Alternate Mode" button under the rows opens its page (below) |
| Games in a Row | 1 to 10 | |
| Game Length (min) | 0 to 20, 0 is "Inf." | |
| Spy Lives ("Kills to Win" in Merc Deathmatch) | 0 to 10 ("Inf."), kills 1 to 100 | |
| Mercenary Lives | 0 to 10 ("Inf.") | |
| Respawn Length (sec) | 1 to 15 | the map's own value is at least 5 (1 in Merc Deathmatch) |
| Active Disks, Disks to Take | 1 to the map's most | Disk Hunt only; Disks to Take never above Active Disks |
| Practice | off | Reloaded's practice server: unlimited lives, 1 s respawn, `!heal` `!regen` `!tank` `!kill`, no stats (docs/ONLINE.md, "Practice servers") |

Added for hosting from the game (the original took them from Ubi.com or the LAN service): server name, UDP port
(28888), the master server, list on the master, map the port with UPnP. The page keeps its last values
(`user://online.cfg`, `[host] setup`).

Every page names a map the same way: the catalog's name of the original's map list (ENHANCED STEEL SQUAT,
docs/MAPS.md), else its id (`UI/MapTitles.cs`): Create Session, the lobby, the server browser's Map column and the
loading page. The server's own chat line ("Next: ...") still names the id. The loading page of a round shows the
mode and the map's words only; the join steps (direct, NAT punch, relay) show while joining.

## Tutorials

The original's tutorial pages (GUI.Tutorials_Page and the pages it opens; spec/game/tutorials.md), from the main
menu's Tutorials button:

| Page | What it shows |
|---|---|
| Tutorials | View HUD, Tips, Gadget Details, Spy Tutorial Maps, Mercenary Tutorial Maps, Visit a Map, and a line of the player's progress (ours) |
| Spy / Mercenary Tutorial Maps | a tick and a name per map: spies `TSP20` to `TSP23` and the Exam `TSP29`, mercs `TME30`, `TME32`, `TME33`; a row plays the map (greyed when the map is not converted) |
| Tips | nine tips per side, Switch Team; a tip shows its picture, legend and explanation (keys named from the player's layout), Next wraps |
| Gadget Details | the side's eight gadgets, Switch Team; a gadget shows its HUD icon, explanation and legend, Next wraps |
| View HUD | the spy or merc HUD picture with its labels; A switches side; Keyboard shows the keyboard pictures (Configuration 1 and 2) |
| Visit a Map | the host maps that play Story, the side's picture, Switch Team, Launch |

A tutorial or a visit is played offline, alone (no bots), in Story with the original's offline rules: no time limit,
unlimited lives, 5 s respawn. When a tutorial round ends with the player's side winning, the map is marked done (the
spy exam also marks the exam passed); 10 s after the end the next spy or merc tutorial not done starts, or the
Tutorials page opens once the exam (spies) or all three (mercs) are done. A visit shows the map's help paths (the
in-game help is on for the visit only) and ends back on the Tutorials page. The progress and the maps already played
are kept in `user://tutorials.json`.

In the Lobby Room, the first Launch or Ready on a map the player has not played opens "This is the first time you
have played this map..." (GUI.FirstTimeToPlayThisMap); OK remembers the map and the press goes on.

Texts come from the converted GUI.int and pictures from `Tutorials_PC` (the ui content step; rebuild it with
`python -m svmbuild ui`, see content/README.md). Without them the pages open with their titles and
empty texts. Differences from the original: the TutoOrExam page (the online button's exam gate) is not shown; the
keyboard pictures, which no PC menu opened, are reachable from View HUD; leaving a tutorial through the In-Game Menu
returns to the main menu; the tutorial maps' zone hints do not show yet (they are standalone only, and offline play is a
listen server).

## Alternate Mode

Create_Match's "Alternate" box and its page (GUI.AlternateModeSettings). With Alternate on, the host picks, per side:

| Spies | Mercenaries |
|---|---|
| Thermal Vision, Night Vision, Grab Player, Elbow Hit, Lethal Drop Tech | EMF Vision, Motion Tracking Vision, Elbow Hit, Lethal Drop Tech, Torch Light, Laser Sight, Berserk, Charge |
| the eight spy gadgets, each allowed or not | the eight merc gadgets, each allowed or not |
| Number of gadgets 0 to 4 (never more than the gadgets allowed) | Number of gadgets 0 to 4 |

and the Cinematic Camera. Untouched values are the stock ones (every ability but the lethal drops, every gadget,
four slots, no cinematic camera). With Alternate off a match uses the stock values, except Deathmatch, which gives
spies the lethal drop, takes the mercs' EMF and motion tracking vision, laser and berserk away and allows one gadget a
side (flash bang, flare launcher), as Create_Match's Apply does. The values travel in the setup line (`SvmAlt`), go
into the host's settings object (`AlternateRules.ApplyTo`) and from there into each round's replicated game info and
team infos, which gate the abilities (goggle modes, grab, elbows, lethal drop, torch, laser, berserk, charge) and the
gear menu's choices and slots. In the lobby, the "Alternate" button (shown while Alternate is on) opens the page:
editable for the host (OK applies it), read only for everyone else.

## Lobby Room

- Spies and Mercenaries lists ("Waiting for players" when a side is empty), spectators below. Each row: the speaking
  icon, name (host mark, "(mic: team)" for a player who restricted his mic, a missing or different map), Ready / Not
  Ready, ping, "(voice banned)"; for other players a volume slider (0 to 200%), Mute and, for the host, Kick ("Do you
  want to kick ...?"), Ban ("Do you want to ban ... from this server?": his account, install id and address, for good)
  and Voice Ban / Voice Unban ("Do you want to ban ... from voice chat?": nobody hears him and he hears nobody for the
  rest of the visit).
- Switch Team (not in Merc Deathmatch, not while ready), Spectate, Equipment, Briefing, Restrict Mic to Team, and a
  line about the local microphone (transmitting or silent, push to talk or voice activation, the level).
- Equipment (Lobby_Create's button, which opens Edit_Profile): the gear picker of the round (`GearMenu`) for the
  local player's side, with the slots and allowed gadgets the session's setup will give that side (Alternate Mode,
  or the stock rules with Deathmatch's one gadget). OK saves the choice to the settings file; the server takes it
  when the round spawns the player (spec/additions/gear-change.md). Greyed for a spectator and while the launch counts
  down.
- Briefing (GUI.Briefing, BriefingDeux): the setup's map and mode for the local player's side (a spectator reads the
  mercs'). Page 1 "MISSION MAIN OBJECTIVE": the map's loading picture, the mode's objective
  (ExplicationModeSpy / Sentry) and the map's briefing (ExpliMapInBriefSpy / Sentry); Next shows page 2, the mode's
  briefing for the side (ExpliModeInBriefSpy / Sentry) and the map legend (LegendMapInBrief); Previous goes back. The
  words are the map's own, converted into its `ui/mapinfo.json` (`MapCatalog.GetMapSettings` fills SMapSettings'
  strings from it). The original's briefing picture (GetLevelBrief) is not converted, so page 1 shows the loading
  picture, and GUIController's objective names (TextSpy, TextSentry) show only when the text table has them.
- Session Parameters: the Create Session rows. The host edits and presses Apply; any change un-readies the others
  (the original's clients un-ready themselves), whose changed values blink for three seconds.
- Chat: Say and TeamSay ("(Team) " prefix); a line within a second of the last one is not sent: "(Spam delay, 1 second)".
- Page buttons: Leave, Options (the options over the lobby), Strategic Map (the selected map's strategic map, below),
  and Launch / Cancel for the host or Ready / Not Ready for the others. Launch needs everyone on a side ready; a player without the map, or with another version of it, is named.

Per-player volume and mute (rl:voice.mix) are Reloaded's: its overlay sets a player's volume with the scroll wheel and
mutes with a click. The rewrite puts the same control on each lobby row and shows Reloaded's overlay in the round
("Guest - volume 40%", "Your microphone is muted"). Choices last for the visit, kept by player name.

## Strategic map

The original's `StrategicMap` page (spec/hud/strategic-map.md): the shown floor's picture, the map's legend
(`LegendMapInBrief`) on the right, Up and Down at the picture's lower left with a "Floor 1 / 3" caption (only for a
map with more than one floor), and Back. In a match it opens from the In-Game Menu's Strategic Map button (Back,
Escape or the right mouse button return to the menu) and from F7, `DisplayStrategicMap` (Escape, the right mouse
button or F7 again close it); it opens on the player's own floor, and every half second the "you are here" marker
(turned to where he faces) and up to two team mates blink and move. The positions of the mates come from the
server: the page asks for them (`UpdateMinimapParams`) and reads the replicated PRI fields. The Lobby Room's Strategic
Map button (an OpenSCCT addition) shows the selected map's pictures and legend without markers.

| Look | Picture | Markers |
|---|---|---|
| original | the map's floor picture in a thin frame | Menu_PC `YouAreHereTexPC` and `ButtonWhite`, turned and tinted with the original's blink colours |
| modern | the same picture in the `UiBox` thumbnail frame | our own: a dot with a view cone, and a ringed dot, in the same colours |

The pictures are map content: the maps step writes them to `ui/strategicmap/floor<n>.png` of each map folder, and the
ui step exports the two marker textures. Until the content is rebuilt (the `maps` step for every map, and the `ui`
step) the page shows "No map picture" and, in the original look, draws the modern markers. A map without a strategic
map (the Cine, Clar, Kraus, Moun, Pana, Sanc, Sche, Shad, Slau, Stro, Term and Vert maps, among others) shows the
page with "No map picture"; maps without a snapshot camera (the tutorials, PyroD, SkysP, Ziop) show their pictures
without markers.

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

## Display page (the original's Video Options)

Beside Reloaded's rows (mode, monitor, window size, frame caps, shadow quality and smoothing, filtering), the
original Video_Settings' own:

- Post-render quality, Normal or High (`post_quality`, default High as DefaultProfile.ini's GraphPostQual 1). What the
  original's native GRAPHICPOSTQUALITY changes is not decoded; ours: Normal leaves the glow out of the normal view.
- Brightness configuration (GUI.Mire, `Options/BrightnessTest.cs`): the test pattern on black, three dark squares with
  a check mark in the left one; the original look shows the converted calibrage picture, the modern look draws it at
  the picture's levels. Back or Escape closes it.
- Keep these settings (GUI.Validate_VideoSettings): after Save changes the display mode, monitor or window size,
  "Do you want to keep these settings? Original settings will be reset in: 15" counts down; Yes keeps them, No or
  the end of the count saves and applies the old three back.

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

- Host options the original had that the rewrite does not show: Tutor Mode, Game Type (Free or Ranking) and Level
  Gap (Ubi.com features), Lock / Unlock (Ubi.com matches), Friends.
- The server browser has no mode legend: the original's LegendModeServer page (mode shapes) is never opened by any
  page, and the browser names each server's mode in words.
- The video options ask "keep these settings?" after a change of display mode, monitor or window size (the original
  asked after its resolution and quality rows); the other display rows apply without asking. Alternate Mode is on the LAN page in
  the original too, and the rewrite has it (above).
- The original's random map pool is the stock maps numbered 01 to 12 without 07; the rewrite draws from the installed
  maps that have the mode.
- Launch: the original waits (launching) when not everyone is ready; the rewrite says who is not ready. Reloaded lets a
  host start with an empty side, and so does the rewrite.
