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
| `game/Scripts/UI/MainMenu.cs` | Responsive dashboard: Play, profile/progress, community activity, changelog, announcements and direct links to the existing pages |
| `game/Scripts/UI/DashboardColumns.cs`, `DashboardDetailsScreen.cs`, `MenuDashboardModel.cs` | Content-sized column reflow, in-game release/announcement details and Godot-free presentation rules |
| `game/Scripts/Online/MenuDashboardService.cs` | Cached dashboard feeds; requests run only while dashboard/detail pages are open |
| `game/Scripts/UI/Tutorials/` | Tutorials (`TutorialsScreen`), the tutorial maps (`TutorialMapsScreen`), Tips (`TipsScreen`), Gadget Details (`GadgetDetailsScreen`), View HUD and the keyboard pictures (`HudLegendScreen`), Visit a Map (`VisitMapScreen`) |
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
(`--svm-background`) and `SVM_PLAY_SHOTS` it saves offline and populated main menus from 800×600 through ultrawide
and 4K, Create Session, Join with IP and every options tab.

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

## Screen sizes and resizing

The main dashboard and its information pages use an opt-in, page-local logical canvas. Its scale is the larger of
1 and `min(window width / 1280, window height / 720)`: text, controls and spacing grow together on larger displays,
while small windows retain readable text and scroll. Other menus and gameplay keep their existing rendering scale.
The canvas enables scale-based oversampling so dynamic fonts and SVG icons rasterize at their displayed size,
including after a live resize, instead of enlarging cached glyphs and icons from the base layout.
Rank icons refresh inherited sampling when revealed, since account data can arrive after the canvas was scaled.
Dashboard columns share spare width and reflow from three columns to Play above two columns, then a single
Play-first stack, according to their actual combined minimum widths. These are content constraints in logical
units, not physical-screen breakpoints. The footer and build line remain outside the scrolling content.

Menu pages fit their content width to the viewport, with centered reading widths and footer actions on wide screens.
Narrow windows stack session rule groups, lobby columns, cosmetic lists/details and tutorial panels; long pages
scroll vertically. Options keeps Apply/Back and the binding explanation visible while the settings and bindings
scroll. The full-screen map chooser uses the remaining modal height in both picture and list modes, and its grid
recalculates columns when a window shrinks. The Sessions table keeps its column widths and scrolls internally on
small screens.

The `menu-layout`, `options`, `lobby-layout` and `front-end-layout` smokes cover 800×600, 1024×768, 1280×720,
1280×800, 1920×1080, 2560×1440, 3440×1440, 3840×1080 and 3840×2160. The options, lobby and front-end checks also
resize existing controls back down after 4K. They check control bounds, text, footer visibility and map scrolling;
windowed runs save images with `SVM_PLAY_SHOTS`. These checks cover the standard fixture states, rather than every
possible signed-in account, server listing or cosmetic unlock state. Demos is not opened by the layout smoke because
opening it prunes the player's old demo cache.

The dashboard cases include signed-out/unavailable states and isolated fixtures with long names, release notes,
announcements, cached activity and cosmetic progress. `menu-escape` opens the changelog with viewport input and
checks Back/Escape restores its main-menu focus. The dashboard does not send public/account requests in smoke runs.

## Dashboard information

What's new previews three release highlights and opens the latest five stable releases in the game. The newest
entry starts expanded; older entries can be opened individually. External links explicitly say they open a browser.
Detail pages support mouse-wheel scrolling, Page Up/Down, Home/End and the controller's right stick, so long text
can be read without moving focus to a button below it.
Release notes are plain text, never executable HTML or BBCode. Announcements use the existing admin website's
announcement field; an empty message hides the card, and long notices open in a scrolling detail page.

Community activity counts players and sessions in the master's public game and hub lists, refreshed every 30 seconds.
These are heartbeat snapshots, not the total number of signed-in users. Status/announcements and signed-in account
progress refresh at most once a minute; release notes use the shared master's ten-minute cache. Polling starts only
while the dashboard or a detail page is open. Failed refreshes retain received information with a stale label;
missing endpoints on older masters leave the rest of the menu usable. Changing the master discards old responses.

The progress card shows level/XP and the closest reported cumulative achievement that unlocks a cosmetic the player
does not own. It uses completion fraction and stable catalogue order for ties; unreported progress is not invented.
The next-unlock action opens that achievement. Guests can still use the play routes and browse achievements.

```powershell
tools/ci/godot-smoke.ps1 -Tests menu-layout,options,lobby-layout,front-end-layout -ExpectedFailures @()
```

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
Lobby Room ── Start match (server validates readiness) ──> Loading ──> round ──> results ── Next ──> next game of the series, or the Lobby Room
```

## Create Session

Rows, captions and ranges are the page's (spec rules 1 to 9):

| Row | Range | Notes |
|---|---|---|
| Choose Map | the installed maps of the map list's playlist | changing it reloads the rules from the map's settings for the mode |
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
| Practice | off | Reloaded's practice server: unlimited lives, 1 s respawn, `!heal` `!regen` `!tank` `!kill` `!ammo`, no stats (docs/ONLINE.md, "Practice servers") |

Under the rows, the Maps list (ours, `UI/MapPickerView.cs`): a picture per map of the selected mode, a click picks it.
Above the pictures, Reloaded's playlist choice (rl:qol.lobbymaps): Standard, Extended, Classic, 3v3, Merc Deathmatch
(each with its description as the tooltip, and the maps it names that are not installed) or All installed maps, and a
search box (the map's name, its name without the variant word, or its code). The Choose Map arrows then step through
the playlist. The choice is kept in the settings (`active_map_playlist`, Standard on a first run); without the
catalog data every map shows under All.

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

In the Lobby Room, Launch and Ready proceed immediately, including on maps the player has not played before.

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

The lobby follows the original `GUI.Lobby_Create` spatial layout: Spies upper left, Mercenaries upper right,
with Switch Team, Equipment, Session Options, Map / Mode and Briefing in that order between them. The map
preview sits below left and chat below right. These landmarks stay in place at 1024, 1280 and 1920 pixels;
tall rosters can scroll. The map preview and chat expand vertically into the available body space, so larger
windows show a large map image instead of an 80-pixel strip. Leave, Options and Start/Ready span the bottom, with the preparation status above.
Offline lobbies use the same team and map positions without online chat, spectators or moderation.

- The overview shows the map preview/title, mode and essential rules, with the objective summary on the map tooltip. Unlimited lives and
  mode-specific disk/kill targets are named explicitly. Briefing contains the full map-specific instructions.
- Team cards show occupants and open slots; rows keep speaking, name, local/host markers, readiness and ping.
  Spectators have a separate list. Join Spies, Join Mercenaries and Spectate honor existing team/ready/mode rules.
- The preparation footer shows the local role and normalized equipment selection. The central Equipment button opens the
  existing gear picker with the live match's restrictions. It is disabled for spectators and during countdown.
- Chat uses an All/Team selector and one Send button; Enter uses the selected channel. Pending text survives
  session updates. Team chat from spectators is refused without clearing the draft or sending it publicly.
- The player actions button opens per-player voice volume (0–200%), mute and permitted host moderation.
  Kick, Ban and Voice Ban/Unban retain confirmation; losing host control dismisses pending moderation.
- Session Options opens the host draft or the same panel read-only for other players. Map / Mode opens
  the map picker over that draft for hosts, and the read-only settings for clients.
  Apply changes sends the draft to the server; Discard changes restores the current server settings. Escape
  confirms before discarding an edited draft. Map picking and Alternate rules update the draft rather than
  applying implicitly. Changes received while a host draft is edited are flagged for review.
- The live overview never previews unapplied settings. Applied changes appear as readable activity notices
  instead of blinking values. Server changes still control readiness invalidation and launch behavior.
- Start match / Cancel countdown is the host's primary action; clients use Ready / Not ready. Countdown,
  named unready players, map mismatches, download progress/failure and waiting-for-host messages are shown beside
  the action. Button availability uses existing session capabilities; the server checks launch requests again.
  No new requirement for players on both teams is introduced.
- Briefing stays in the central menu; Strategic map and Cosmetics sit beside the loadout summary. The briefing uses the
  map's converted side-specific mode/objective/legend text; spectators read the mercenary briefing.

`--svm-smoke=lobby-layout` checks host/client/offline layouts at 1024×768, 1280×720, 1920×1080 and 2560×1440, settings
Apply/Discard and remote changes, chat routing/draft preservation, voice volume, moderation confirmation, team
selection, spectator restrictions, countdown captions and map-download status. `SVM_PLAY_SHOTS` saves rendered
review images. The existing two-process net-lobby smoke uses the new player menu, settings panel and activity notices.

Per-player volume and mute (rl:voice.mix) are Reloaded's: its overlay sets a player's volume with the scroll wheel and
mutes with a click. The rewrite puts the same control in each lobby player menu and shows Reloaded's overlay in the round
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
`voice_input_gain`, `voice_output_volume`, `voice_jitter_frames`, `voice_muted`, `voice_input_device`,
`voice_auto_gain`, `voice_noise_suppression`, `voice_noise_suppression_level`, `voice_echo_cancellation`) plus
`allow_voice_chat`. Auto-Adjust Mic Volume, Noise suppression with its level (Low, Standard, High, Extreme) and Echo
cancellation (experimental) sit under Microphone Volume (docs/VOICE.md "Microphone clean-up").

## Role configuration and shared controls

Options has **Spy**, **Mercenary**, Display, Audio, Voice, HUD and **Controller (Shared)** tabs. Each role contains
Bindings, Mouse & Aim and Controller sections. Mercenary also contains the custom Crosshair editor and preview;
the spy retains its original contextual reticle. Role frame caps, the mercenary FOV cap and shared vertical mouse
inversion are under Mouse & Aim. The Controller (Shared) tab holds shared controller setup, stick behavior and
vibration settings; role-specific sensitivity and button bindings live in each role's Controller section.
Detailed scope, camera and held-sensitivity modifiers are under Advanced settings.

Bindings are action-based and grouped by movement, combat, gadgets, vision, communication and interface. They show
unbound actions as well as existing keys. Focusing or hovering an action shows its verified description and the
other actions on that key. The internal `FireMode` is the spy's ready/holster input (tap draws with laser, hold
past 0.5 seconds draws without it); mercenary **Switch fire mode** uses `bROF`, switching burst/full auto.
`bHoldBreath` steadies the merc scope but also operates the spy's selected cloak, so the spy shows a separate
role-specific description. Roll is spy-only. Gadget quick-use, alternate action and cloak/breath can be rebound
independently without deleting the other commands from their original combined key.

The options are organized for this game rather than matching the original menu. Inactive original shortcuts
(controls reference, HUD help and the legacy Quit command) are not offered as normal actions. Existing custom
configuration data is retained without advertising nonfunctional features. Keyboard-only menu/chat handlers reject
mouse assignments with an explanation rather than accepting a shortcut that cannot work. Voice shortcuts are
connected to the active role's voice InputMap, with the existing defaults represented in older key layouts.

Conflicts offer Replace, Share key, Swap (when there is one source key), or Cancel. Clear removes only the selected
action. Custom command chains and aliases remain supported. Shared layouts are explicitly labeled **Use the same
bindings for both roles** (`use_split_spy_merc_keyboard` is its inverse): both roles then edit the Spy layout;
the separate Mercenary layout is retained. Existing controller assignments preserve their old full-key
behavior and are shown as saved assignments; the normal choices offer independent actions (`Action:<stable-id>`) do not require a keyboard binding and do not run
other actions sharing a keyboard key. Existing controller values are retained.

All edits, including keyboard bindings, remain in memory until **Apply** or **Save & Close**. Back confirms
before discarding unapplied changes. Section and role resets are staged; reset-all requires confirmation. Display
changes retain the 15-second rollback question, and Save & Close waits for its answer. Settings remain in
`user://settings.json`; key files remain `user://svm_keys_spy.ini` and `user://svm_keys_merc.ini` in Engine.Input
format. New complete snapshots preserve aliases and intentionally cleared keys across reloads; older overlays
are still accepted. Merely opening Options does not create key files.

The redundant mouse **Spy free look response** selector is removed. Loading/clamping an old profile folds its
fixed gain (1, 2, 2.5 or 3) into `controls_spy_kbm.free_look`, then sets `look_response` to Linear. The free-look
range extends to 30 so every valid legacy sensitivity retains its turning speed; repeated loads do not multiply
again. Controller curves remain: the inner 75% is linear and the outer 25% ramps to 2x, 2.5x or 3x. **Hold to
modify sensitivity** replaces the clutch label. For mouse input its multiplier scales sensitivity directly;
controller input is scaled before its curve. The merc's two enabled held modifiers multiply together.

Options pages scroll vertically and wrap setting labels. Long controller choices have full-text tooltips. The
Display mode list reads "Borderless fullscreen" for Reloaded's `BorderlessFullscreen` value. `--svm-smoke=options`
checks draft/apply/discard, shared and separate layouts, independent controller dispatch, and every role subsection
(including Advanced) at 1024x768, 1280x720 and 1920x1080. `SVM_PLAY_SHOTS` saves rendered review images.

## Walk, gadgets and scope input

Spies can switch between third and first person with **X** (rebind **First / third person** under
**Options → Spy → Bindings**). **Perspective** selects **Toggle** or **Hold**; the same choice applies to
controller assignments, while wheel bindings always toggle. Existing X bindings are preserved. First person
uses a collision-checked eye position inside the current standing or crouching body, with a final rendered-frame
check that includes the near plane. Perspective changes cut immediately. If solid geometry leaves no safe eye
position, the world is obscured until the view is clear. Binoculars, remote cameras and scripted views take
priority; focus loss and a new possession reset the perspective to third person.

Both roles now expose **Walk**, **Next gadget** and **Previous gadget** as independent bindable actions.
Running is the default. Role Bindings sections offer Hold or Toggle for Walk, and the Mercenary offers Hold or
Toggle for scope aiming. These behavior choices apply to keyboard, mouse and controller. A scroll notch cannot
be held, so wheel-bound Walk and scope always toggle. Hold sources are tracked separately: releasing one source
does not release another held source. Focus/menu cleanup clears held actions, and new possession restores running.

The Mercenary's new walk mode uses 0.5 input scale, including crouched movement; existing movement curves and
stance limits still apply. It is intentionally additional behavior rather than original-game parity. Scope and
other contextual wheel handlers continue to control zoom, camera views, interaction menus and mine selection.

The old shared wheel dropdown is removed. On load, legacy gadget-wheel bindings become NextGadget / PreviousGadget;
legacy speed-wheel bindings become Run / Walk, and ChangeSpeed becomes Walk. Run is retained as a hidden
compatibility command for old shortcuts, rather than offered as a normal action. Migration preserves other commands
and aliases, is idempotent, and is written with the next binding edit. Existing controller identifiers migrate to
equivalent action identifiers. Behavior settings persist as walk_behavior / scope_behavior in the role's keyboard
control group; the default is Toggle to preserve existing tap-based controls.

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
  its window at every step. Checks the player-menu voice slider against received audio volume, and verifies public
  admin announcements and private command replies in the shared match transcript across both revenge rounds.
- `menu-pilot-popup` smoke: mouse events operate root, scrolled and popup sliders. Popup clicks stay within their
  own viewport without scrolling the owning player row; checks 40% volume and drag release. Runs embedded popups
  headlessly and also native popups with `-Windowed` when the display server supports them.

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
