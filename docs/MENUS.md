# Menus and controls

[Documentation](README.md)

This guide covers the main menu, session setup, lobby, tutorials, options and results.
For joining and hosting, start with [Online play](ONLINE.md); for microphone setup, use [Voice chat](VOICE.md).

| Task | Page or reference |
| --- | --- |
| Start a match | [Create Session](#create-session), [Lobby Room](#lobby-room) |
| Learn maps and gadgets | [Tutorials](#tutorials) |
| Change gameplay restrictions | [Alternate Mode](#alternate-mode) |
| Review progress | [Player stats and match history](#player-stats-and-match-history) |
| Change bindings | [Role configuration and shared controls](#role-configuration-and-shared-controls) |
| Change video settings | [Display page](#display-page-the-originals-video-options) |
| Report a problem or contribute | [Help & feedback](#help--feedback) |
| Develop or test menu behavior | [Screen sizes](#screen-sizes-and-resizing), [Tests](#tests) |

Menu behavior follows the [lobby and match creation specification](../spec/game/lobby-and-match-creation.md).
OpenSCCT adds its own dashboard, accounts, progression, achievements and cosmetic selection.

## Code map

| Location | Responsibility |
| --- | --- |
| [game/Scripts/UI](../game/Scripts/UI/) | Menu pages, reusable controls and presentation models |
| [game/Scripts/Options](../game/Scripts/Options/) | Display, audio, voice and input settings |
| [game/Scripts/Online](../game/Scripts/Online/) | Session flow, dashboard feeds and lobby integration |
| [game/Scripts/Tutorials](../game/Scripts/Tutorials/) | Tutorial progress, text and offline sessions |
| [Logic tests](../tests/OpenScctGame.Logic.Tests/) | Menu behavior independent of Godot |

`MenuPilot` drives menus with input events for the smoke checks. `MenuPage`, `UiStyle` and `UiArt` provide
the shared appearance. `MainMenu` and `DashboardColumns` handle the dashboard's responsive layout.

## Look

The original's PC menus are UnrealScript GUI pages (GUI.u), not Magma pages, so there is no page data to play; they
are rebuilt with the original's textures and colours. The `ui` content step exports the textures the pages use:
GUIContent `fond` (the page background), `MN_cadre03` (the corner brackets), `loading`, the other frames, and Menu_PC
`buton_select` / `buton_validate` / `buton_push` (sSTY_PcButton: normal, focused, pressed), `TABS_NOselect`
(sSTY_PcList: edit boxes and lists), `Saquare_BOX` (panels) and `CommunicatorON` (the speaking icon). Text colours are
the styles' FontColors (58, 73, 58 normal; 93, 117, 92 focused) and the page title's (69, 85, 69), doubled: every
style draws its images at 128, which reads as neutral, so the colours are taken to be on the same scale. That doubling
is an estimate and needs visual comparison with the original menus.
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
`shared/classes.json`), with its key tokens named from the player's configured actions and preferred input order,
including custom aliases and split command groups. An action without a key shows `Unbound`. The original shows those tips only on the
death page. The results open with the HUD's `P_EndGame` banner (a Magma page), then the end menu; in Merc Deathmatch
its headline is Reloaded's line instead of a side: "*name* wins with *n* kills", "Draw at *n* kills" or "No kills
scored".

Online results show **Next round** while a series has rounds left; the host can separately **End series & return
to lobby**. After the final round the host sees one **Return to lobby** action, while guests can mark themselves
ready with **Continue to lobby**.

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
The dashboard keeps Play, account/collection links and Explore in a centered main column, up to 600 logical units
wide. Profile/community and news/announcements use equal compact side columns, normally 220 units each, so extra
screen width does not enlarge the information panels. At smaller widths, the main actions sit above the two side
columns, then above a single stack, according to their actual combined minimum widths. These are content constraints
in logical units, not physical-screen breakpoints. The footer and build line remain outside the scrolling content.

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
possible signed-in account, server listing or cosmetic unlock state. The separate `demo-ui` smoke uses isolated
recording metadata and a temporary demo to check the library and playback controls at 800×600, 1280×720,
1920×1080 and 3840×2160, then shrinks back to 800×600. It covers filters, compatibility, empty/error states,
playback actions, timeline seeking and control visibility without touching the player's demo cache or account.

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

## Help & feedback

On the main menu, lobby, hub and Escape menu, **Help & feedback** is the same compact control in the
top-right corner, separate from the page body and footer actions. Quit Session stays last in the main stack. Up/down wraps within that stack; Right or Tab
can reach Help.

Open **Help & feedback** from the main menu, Escape menu, online lobby or hub browser. It opens over
the current screen and returns to the same screen when closed. A match keeps running while help is open;
lobby readiness, chat and settings stay in place. Escape or the controller's Back button closes help and
restores focus to the control that opened it.

**Report a bug** opens the [website's bug report form](https://master.lumbridge.co.uk/help/report-bug), which uses
Discord sign-in. The form starts with the game version, platform and available match details shown in help;
reports opened in a map also include the `whereami` position, facing, movement state, physics and animation
details captured when help opened. Review those details and describe the problem before submitting. **Suggest a feature** opens the GitHub form
and requires GitHub sign-in. **Copy details** lets you copy the summary yourself. Automatic details exclude
account information, server addresses, paths and logs. If a browser cannot open, the link stays selectable
and can be copied.

During a game, **Capture game screenshot (optional)** hides the menus for one rendered frame and shows a
preview. **Include captured screenshot with bug report** is checked after capturing; uncheck it to leave the
image out. **Report a bug** transfers the capture to a private temporary draft and opens the browser with it
already attached. This also works when Discord sign-in is needed. You can remove or replace the image in the
browser before confirming publication. If the transfer fails, retry or uncheck the image to continue without it.
Captures are resized to at most 1280 pixels per side and 2 MB. A local PNG is also kept under `user://screenshots`;
**Show saved screenshot** locates it. Unpublished handoffs expire after 15 minutes. Published reports include a
public image link.

The contribution section links to the guides and pull requests. Code contributions need a branch or fork
and GitHub sign-in. The help menu opens without the master service; submitting a bug report requires the
website. The `help-feedback` smoke covers report prefills, navigation, controller focus, copy/browser failures
and layouts at 800×600, 1280×720 and 3840×2160 using fake external actions.

## Player stats and match history

Account uses the same compact cards and 1600×900 responsive reference as the career pages. The profile header
keeps account actions together; rank, XP and prestige emblems sit beside the career snapshot on wide windows and
stack below 950 logical units of content width. The title picker shows unlocked titles first, with an expandable
locked-title list. Sign-in, browser-code and name forms retain a narrower reading width. Actions wrap, the footer
stays visible, and the page supports keyboard/controller scrolling. Appearance-conflict actions also wrap.
The `front-end-layout` smoke includes isolated account fixtures for sign-in, name changes, loading/legacy data,
new players, prestige confirmation and master rank; use `SVM_FRONT_END_LAYOUT_PAGES=account*` for a focused run.

Career stats and recorded matches use the compact dashboard cards and a 1600×900 responsive reference. The
career header shows the player and rank, followed by rounds, wins, win rate and K/D. Seven category buttons open
grouped counters, with separate cards for weapons and maps. Cards reflow into fewer columns on small windows;
the footer stays visible. Counters still come from `StatsModel`, including uncapped totals and unavailable `—`
values on older masters or when a ratio has no denominator.

Match history presents map, outcome, date, duration and server in readable cards. Match details group the round
summary, aligned scoreboard and event timeline. Both pages support the mouse wheel, Page Up/Down, Home/End and
the controller's right stick. The `stats` smoke exercises 50 career layouts and 24 history layouts from 800×600
through 4K, including empty/legacy data and timeline pagination, using isolated fixtures. Windowed runs save
screenshots for visual review.

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

The page uses most of the window: a large map browser on the left and **Game**, **Rules**, and **Hosting** tabs on
the right. Game holds the session name, mode, player limit, duration, voice, bots and personal recording. Rules groups objectives,
team/rotation options and extra rules; Hosting holds access, password, discovery and connection settings. Dropdowns
select modes directly, numeric fields accept typing, and checkboxes include their own labels. Each settings tab
scrolls independently. Below 1100 logical pixels of available width the map browser and settings stack; Back and
Create session stay in the fixed footer. A page-local 1600×900 canvas scales the controls together at high resolutions.
Create session immediately shows a busy state while preparing appearance and starting the host; repeated clicks
cannot start another request. A failed start restores the button and explains the error.

Rows, captions and ranges are the page's (spec rules 1 to 9):

| Row | Range | Notes |
|---|---|---|
| Choose Map | the installed maps of the map list's playlist | changing it reloads the rules from the map's settings for the mode |
| Game Mode | the map's modes, then Merc Deathmatch | changing it reloads the rules |
| Password | 16 of `a-z A-Z 0-9 . _ -` | |
| Voice Chat | Modern, Classic, Team, Open | the hint under it: "Team always - hackable coms", "Living team - hackable coms", "Team always - no com hacking", "Everyone hears everyone" |
| Player slots | 2 to 24 (24) | total human players and bots, supporting 12v12; spectators have their own places |
| Bots | Off, Per Side, Fill Slots, Custom teams | Custom teams offers separate exact bot counts; humans take priority when slots fill |
| Spy bots / Mercenary bots | 0 to 24 | Custom teams only; total player slots still apply; 0 means no bots on that team; Spy bots is hidden in Merc Deathmatch |
| Revenge | | two games, sides swapped for the second; hides Games in a Row |
| Random Map | | after the series, the next map is drawn at random (never the same one twice in a row) |
| Voice in Lobby | on | players talk in the lobby |
| Auto balance | off | Game_Settings' row (hidden and always on in the original): a joiner asking for the bigger side goes to the smaller one, and nobody may switch to a side that would then be two people ahead |
| Alternate | off | Alternate Mode; "Configure alternate rules" in Rules opens its page (below) |
| Games in a Row | 1 to 10 | |
| Game Length (min) | 0 to 20, 0 is "Inf." | |
| Spy Lives ("Kills to Win" in Merc Deathmatch) | 0 to 10 ("Inf."), kills 1 to 100 | |
| Mercenary Lives | 0 to 10 ("Inf.") | |
| Respawn Length (sec) | 1 to 15 | the map's own value is at least 5 (1 in Merc Deathmatch) |
| Active Disks, Disks to Take | 1 to the map's most | Disk Hunt only; Disks to Take never above Active Disks |
| Practice | off | Reloaded's practice server: unlimited lives, 1 s respawn, `!heal` `!regen` `!tank` `!kill` `!ammo`, no stats (docs/ONLINE.md, "Practice servers") |

The Maps list (ours, `UI/MapPickerView.cs`) shows a landscape picture per map of the selected mode; a click picks it.
The selected map's full name stays above the browser. Search and map group filters show a result count and an empty
state with Reset filters when nothing matches; filtering does not change the selected map. The display selector offers
Picture grid, List view and Thumbnail list. Thumbnail rows show a small landscape preview, the map name and its short
code; plain list rows also show the short code. The display choice is saved and shared by all map browsers.
Above the pictures, the map group choice offers All installed maps, Enhanced maps (excluding 3v3 variants), Stock maps
(the eleven maps shipped with the original 2005 game), Community maps (everything outside Stock and the Enhanced family)
and 3v3 optimised maps. Enhanced 3v3 variants remain in All and 3v3 optimised; community 3v3 maps also appear in Community.
Each has a description tooltip. The search box matches the map's
name, its name without the variant word, or its short code (for example `AquaE`, ignoring case). The group choice is kept in the settings
(`active_map_playlist`, All on a first run); the groups work without the optional shared catalog data.

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
returns to the main menu. Tutorial and Visit a Map zone hints are enabled for the offline local player through both
the map trigger and HUD handler, despite offline play using a local client/server session.
The information panel holds its open frame while a hint is active and hides queued game messages underneath it.
Unlit direction and information meshes retain their emissive symbols; existing packs recover these layers from
their base materials without reconversion. The `visit-map` smoke checks the real visit launch, marker materials,
authored node text, and both teams' hint panels remaining visible, updating and clearing.

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
gear menu's choices and slots. In the lobby's Rules tab, "Configure alternate rules" opens the page while Alternate
is on: editable for the host (Save rules updates the match settings draft), read only for everyone else. The host
then uses Apply changes in match settings to send the draft to the server.

## Lobby Room

The lobby follows the original `GUI.Lobby_Create` spatial layout: Spies upper left, Mercenaries upper right,
with Switch Team, Equipment, Cosmetics, Session Options, Briefing and Strategic map in that order between them. The map
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
- Session Options opens a wide match settings panel with the same map browser and form controls as Create Session.
  The selected map, searchable landscape cards, playlist filters and list view sit beside **Game** and **Rules** tabs.
  Game contains mode, duration, voice, player slots, bots and personal recording; Rules groups lives and objectives, teams and rotation, and extra rules.
  Dropdowns select values directly and numeric fields accept typing. Each settings tab scrolls independently;
  below 1000 logical pixels of available width the map browser and tabs stack in the scrolling body. A 1600×900
  canvas scales the panel at high resolutions, and Apply changes and Close/Discard changes remain in its fixed footer.
  Clicking the lobby map image opens these settings and focuses the embedded map search for hosts. Other players can inspect the
  host's settings read-only. Player slots controls the combined capacity for humans and bots. Custom teams sets
  Spy and Mercenary bot counts independently, including zero. The roster preview shows the resulting human/bot
  split and open places, and explains when requested bots cannot fit. Reducing slots makes bots leave first and
  never removes connected humans; requested bot counts are retained when capacity grows again.
  Apply changes sends the draft to the server; Discard changes restores the current server settings. Escape
  confirms before discarding an edited draft. Map picking and Alternate rules update the draft rather than
  applying implicitly. Changes received while a host draft is edited are flagged for review.
- Automatically save demos is a personal switch in the lobby and the Game tab, available to hosts and guests alike.
  It is saved immediately, independently of Apply/Discard, and chooses whether upcoming rounds are saved under
  Demos on this computer. The lobby reports recording/download status; see [Demos](DEMOS.md) for delivery and playback.
- The live overview never previews unapplied settings. Applied changes appear as readable activity notices
  instead of blinking values. Server changes still control readiness invalidation and launch behavior.
- Start match / Cancel countdown is the host's primary action; clients use Ready / Not ready. Countdown,
  named unready players, map mismatches, download progress/failure and waiting-for-host messages are shown beside
  the action. Button availability uses existing session capabilities; the server checks launch requests again.
  No new requirement for players on both teams is introduced.
- Briefing stays in the central menu; Strategic map and Cosmetics sit beside the loadout summary. The briefing uses the
  map's converted side-specific mode/objective/legend text; spectators read the mercenary briefing.

`--svm-smoke=lobby-layout` checks host/client/offline layouts at nine sizes from 800×600 through ultrawide and 4K,
including a live resize from 4K to 800×600. It covers settings tabs and map browsing, Apply/Discard and remote changes,
chat routing/draft preservation, voice volume, moderation confirmation, team selection, spectator restrictions,
countdown captions and map-download status. `SVM_PLAY_SHOTS` saves rendered review images. The two-process
net-lobby smoke visits the Game and Rules tabs, types a respawn value before applying it, checks client read-only
controls, and uses the player menu and activity notices.

Per-player volume and mute (rl:voice.mix) are Reloaded's: its overlay sets a player's volume with the scroll wheel and
mutes with a click. The rewrite puts the same control in each lobby player menu and shows Reloaded's overlay in the round
("Guest - volume 40%", "Your microphone is muted"). Choices last for the visit, kept by player name.

## Strategic map

The original's `StrategicMap` page (spec/hud/strategic-map.md): the shown floor's picture, the map's legend
(`LegendMapInBrief`) on the right, Up and Down at the picture's lower left with a "Floor 1 / 3" caption (only for a
map with more than one floor), and Back. In a match it opens from the In-Game Menu's Strategic Map button (Back,
Escape or the right mouse button return to the menu) and from F7, `DisplayStrategicMap` (Escape, the right mouse
button or F7 again close it); it opens on the player's own floor, and every half second the "you are here" marker
(turned to where he faces) and all team mates within the 24-player lobby limit blink and move. The positions of the mates come from the
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

Options has **Spy**, **Mercenary**, Display, Performance, Audio, Voice, HUD, HUD editor and **Controller (Shared)** tabs. Each role contains
Bindings, Mouse & Aim, Controller Bindings and Controller Movement & Aim tabs. Mercenary also contains the custom
Crosshair editor and preview. The spy retains its original contextual reticle. Performance sits beside Display and holds the shared frame cap,
separate Spy and Mercenary caps, vertical sync, and latency/power settings. The mercenary FOV cap and shared vertical mouse
inversion remain under Mouse & Aim. The Controller (Shared) tab holds shared controller setup, stick behavior and
vibration settings.
Mouse & Aim follows the binding editor's category navigation and search, with Sensitivity, Scope & cameras,
Modifiers and View categories. Each setting has its own aligned row and exact numeric fields;
focusing or hovering a control explains it below the list. A category dropdown replaces the sidebar on narrow
windows. Reset is a compact menu and remains staged until Apply. Role resets preserve the global Performance frame cap.

Display, Performance, Audio, Voice, HUD and Controller (Shared) use the same category/search browser. Display separates
screen setup, widescreen layout and image quality. Performance groups Max FPS and Vertical sync under Frame pacing,
then the Renderer choice (`renderer`: Standard, or Fast (beta), the game's own frame of
[PERFORMANCE-LOWEND-2026-11.md](PERFORMANCE-LOWEND-2026-11.md); it applies from the next map and `SVM_RD_FRAME=1` or `0` overrides it),
separate from latency and power settings. Voice keeps playback,
microphone processing, transmission, shortcuts and microphone testing in distinct categories; the live meter
and test retain their existing behavior. Controller Movement & Aim uses the same settings browser for role-specific
sensitivity, movement and button layout.
Crosshair settings use the same rows with a live preview dock on wide windows and a Preview button on smaller
ones. These settings browsers and Mouse & Aim open with **All** selected, show category headings across the combined
list and search results, and use the binding editor's divider between the category sidebar and settings.
HUD editor has searchable element navigation, aligned properties, a persistent drag preview and a compact
More menu for presets, resets, copying and importing. Browsing and editing stay within the existing Apply,
Save & Close and discard workflow; category and search selections survive page rebuilds.

Bindings use one action per row, with category navigation and search across movement, combat, gadgets, vision,
communication and interface. The category sidebar becomes a dropdown on narrow windows. Two independently
editable input slots are visible; additional inputs remain available through the extra-bindings control. Selecting
an action shows its description and shared-input effects. Applicable Hold/Toggle selectors sit inline beside the
action and its bindings. Arrow navigation follows the next row and scrolls it into view before reaching the footer.
Right-click an assigned
input for Change binding or Clear binding, or hover it and press Delete to clear only that input. Delete still edits
text normally in search fields and remains bindable during capture. The capture panel also offers Clear binding and Cancel.
Adding an input keeps the existing primary and alternate in place; replacing one keeps its column, including after
saving and reopening Options. Controller Bindings uses the same editor for controller inputs. Teammate-name visibility
settings live on the HUD page. The binding editor's More menu includes role resets.

The internal `FireMode` is the spy's ready/holster input (tap draws with laser, hold
past 0.5 seconds draws without it); mercenary **Switch fire mode** uses `bROF`, switching burst/full auto.
`bHoldBreath` steadies the merc scope. Spies have a separate `bCloak` binding (K / controller LB + RB by default)
for their built-in cloak, with immediate Hold or Toggle activation. Cloak consumes no gadget slot and is absent
from loadout and gadget-restriction pickers. Roll is spy-only. Gadget quick-use, alternate action and cloak/breath can be rebound
independently without deleting the other commands from their original combined key.

The options are organized for this game rather than matching the original menu. Inactive original shortcuts
(controls reference, HUD help and the legacy Quit command) are not offered as normal actions. Existing custom
configuration data is retained without advertising nonfunctional features. Keyboard-only menu/chat handlers reject
mouse assignments with an explanation rather than accepting a shortcut that cannot work. Voice shortcuts are
resolved from the active role's layout using the same input selection as gameplay, with the existing defaults
represented in older key layouts.

Keyboard conflicts offer Replace, Share key, Swap for a selected source, or Cancel; controller conflicts offer
Replace, Swap and Cancel. Adding, replacing or removing an input preserves the action's other inputs and unrelated
commands on a shared input. Custom command chains and aliases remain supported. Shared layouts are explicitly labeled **Use the same
bindings for both roles** (`use_split_spy_merc_keyboard` is its inverse): both roles then edit the Spy layout;
the separate Mercenary layout is retained. Existing controller assignments preserve their old full-key
behavior and are shown as saved assignments; the normal choices offer independent actions (`Action:<stable-id>`) do not require a keyboard binding and do not run
other actions sharing a keyboard key. Existing controller values are retained. Contextual controller presets,
including fire with tap-to-quick-use, are named complete assignments rather than silently flattened into one action.
Supported controller gestures remain the existing buttons, triggers, LS/LB combinations and Hold B. Capture waits
for the opener to release, previews a supported gesture and commits on release; B and Start can be assigned.

Keyboard and mouse inputs support Ctrl/Shift/Alt combinations, saved as canonical INI keys such as
`IK_Ctrl+Shift+E`. Left and right modifiers are equivalent. An exact assigned combination wins over its plain base;
otherwise the plain input works normally. Selection is latched on base press: adding a modifier afterward does
not change it, and releasing a required modifier ends the combination without starting the plain input. Standalone
modifier actions remain independent (including Shift's default interaction). Wheel combinations are pulses.
Focus loss, capture and gameplay overlays release accepted inputs and block held inputs until release. Voice
shortcuts remain available in ordinary menus but are suppressed while rebinding and while unfocused.

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

Both roles now default controller free look, gun/look and vertical sensitivity to **0.6**. Saved sensitivity
values remain intact; resetting the role's controller settings applies the new defaults. While gameplay owns
the controller, raw A/B presses cannot activate menu controls or text chat; their role bindings still apply,
with Start opening the game menu by default. In menus, the D-pad and left stick make one initial step, wait
0.4 seconds, then repeat at 0.16-second intervals. Stick press/release thresholds of 0.65/0.35 prevent noisy
axis updates from advancing several rows. A activates, and B/Back/Start return. The optional **Quick chat**
action starts unbound in Communication for keyboard/mouse and controller; see [text chat](HUD.md#text-chat).

Options pages scroll vertically and wrap setting labels. Long controller choices have full-text tooltips. The
Display mode list reads "Borderless fullscreen" for Reloaded's `BorderlessFullscreen` value. `--svm-smoke=options`
checks draft/apply/discard, shared and separate layouts, independent controller dispatch, and every role subsection
(including Advanced) at 1024x768, 1280x720 and 1920x1080. `SVM_PLAY_SHOTS` saves rendered review images.

The HUD tab's **FPS & frame timing** and **Network information** sections each offer Never, Always, or Only during
issues, followed by independent position, scale and opacity controls. Metric toggles, line/bar graph settings,
history duration and warning thresholds follow. Position uses percentages of available screen travel, with
0 at the left/top and 100 at the right/bottom. The usual Apply/Discard/Defaults behavior includes these settings;
details of the measurements and issue detection are in [HUD.md](HUD.md).

## Walk, gadgets and scope input

Spies can switch between third and first person with **X** (rebind **First / third person** under
**Options → Spy → Bindings**). **Perspective** selects **Toggle** or **Hold**; the same choice applies to
controller assignments, while wheel bindings always toggle. Separate **First person** and **Third person**
actions select that view on press and start unbound for keyboard, mouse and controller. For example, bind
**First person** to scroll up and **Third person** to scroll down; repeated notches keep the chosen view.
Existing X bindings are preserved. First person uses a collision-checked eye position inside the current
standing or crouching body, with a final rendered-frame
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

Unexpected returns to the main menu can be traced through `SVM_MAIN_MENU` (previous scene and calling method).
`SVM_SESSION_FAILURE` records the online session state and connection-close reason before disposal;
`SVM_HOST_FAILURE` records the full server exception, including for standalone Visit Map sessions.
These are log diagnostics, not player-chat messages.

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
