# HUD, chat and options

The in-game HUD is the original's own Magma pages, played from the converted page data: `P_HudATT` for spies,
`P_HudDEF` for mercs, `P_SpectatorMode` for dead players and spectators, `P_BinocularsATT` and `P_BinocularsDEF` in
the binocular views, the spy's interaction list `P_InterATT` and the end banner `P_EndGame`. What the original draws on
the SHud canvas (objective markers, washes, console lines, the knock-out text) is drawn on top in the same order. The
front end (main menu, options, lobby, results) is our own Godot UI in the HUD's font and colours.

| Path | What it holds |
|---|---|
| `content/svmbuild/uiconv/`, `content/svmbuild/steps/ui.py` | the `ui` step: Magma packages, atlases, fonts and strings to `.content/shared/ui/` |
| `game/Scripts/Hud/Magma/` | the page player: package model, instances and clocks, keyframes, renderer, Godot canvas, bitmap fonts |
| `game/Scripts/Hud/` | the HUD: snapshot and sim reader, page drivers, effects, messages, console, radar, markers, binoculars, status page, `HudLayer`, `PlayHud`, the `hud` smoke test (`Hud/Gadgets/` belongs to the gadget overlays) |
| `game/Scripts/Chat/` | the chat line (`ChatInput`) |
| `game/Scripts/Options/` | the settings file, the options screen, applying settings, the controller |
| `src/OpenScct.Net/Channels/ChatLine.cs`, `ControlMessage.cs` | the chat message on the control channel |
| `src/OpenScct.Server/ChatRelay.cs` | the server's chat rules |
| `spec/hud/` | the decoded HUD rules (index in `spec/hud/INDEX.md`) |
| `game/Scripts/UI/`, `game/Scenes/UI/` | front end: main menu, lobby, results, the play entry and lobby interfaces |

Evidence comes from our own decodes (specs in `spec/hud/`, the natives in a local Ghidra copy) and from the HUD
research of the SCCT Versus UE5 Bridge project (its `docs/HUD.md`, `docs/OVERLAYS.md`, `docs/overlays/*.md`).
Constants live in `HudK` (and the classes that use them) with their `[Provenance]`.

## Content: the ui step

`python -m svmbuild ui` (from `content/`) reads the install and writes `.content/shared/ui/` in about a second:

| Output | From | Format |
|---|---|---|
| `manifest.json` | | `svm.ui/1`: packages, fonts, textures with their sizes, what was missing |
| `magma/<Package>.json` | `Menus/Hud.mgm`, `Options.mgm`, `common.mgm` | `svm.magma/1` (below) |
| `textures/<Package>/<Tex>.png` | `Packages/Textures/<Package>_mgm.utx` | the atlases the package's materials use, as stored (top-down) |
| `textures/SHudTextures/*.png` | `SHudTextures.utx` | `MN_boutons` (markers), `Icones_Gadgets`, `Icones_Weapons` (respawn weapon), `Att_Arrow` and `BinocularA_all` (binocular markers) |
| `fonts/<Font>.json`, `<Font>_<n>.png` | `Menus/Fonts/*.mft` and the font pages in the Magma texture packages | `svm.font/1` (below) |
| `strings.json` | `System/_PC_/Magma.int`, `MagmaDynamic.int`, `Engine.int` | `{file: {section: {key: text}}}`; Engine.int gives the key names (`LocalizedKeyName`) |

A missing `Hud.mgm` fails the step; other gaps are listed in the manifest. Tests: `tests/content/test_ui_*.py`.

**svm.magma/1.** A Magma package is Latin-1 text in an XML dialect. The JSON keeps it as found with references
resolved: `materials` (texture base name and texel region), `fonts`, `areas` (pages, areas and buttons in document
order, each with `frameRate` and `widgets`) and `pages` (name to area index). A widget is `image`, `text`, `inst`
(linked area, synchronized with the parent or its own clock) or `list`. Keys: `f` frame, `interp`, `rect` or `pos`,
`rot`, `colors` (0xAABBGGRR), `state`, `color`, `shadow`, `shadowOffset`, `height`, `tracking`, `tiling`, `offset`,
`actions` (goto area frame, stop area).

**svm.font/1.** The `.mft` glyph table: char, width, height, signed x offset, signed baseline, advance, and UVs whose
integer part is the page. Glyph cells are written with 2 texels of padding (`Padding` in the font file) so scaled
text samples no neighbour: keep it when changing the step, the smoke test fails without it.

## Page player

`MagmaNode` is one instance of an area; a page is the root. `MagmaRenderer` turns the tree into a `MagmaDrawList`
(screen polygons and text runs, each with the instance path and widget name), and `MagmaCanvas` draws the list with
Godot canvas items. The draw list is what the smoke test checks, so headless runs test everything but pixels.

- **Clocks.** Each instance runs at its area's frame rate unless synchronized with its parent. Integer frames fire
  keyframe actions (GotoFrame, Stop). A hidden instance does not advance. Drivers use SetFrame, Playing and Restart
  (show, frame 0, play), as the native SetTime / Play.
- **Keyframes.** A widget is not drawn before its first key and holds its last; segments use the destination key's
  interpolation (None, Linear, Circle, Square, Root) at the continuous frame.
- **Colours and blends.** COLORn is 0xAABBGGRR. A widget's colour is its corner colours times the state colours of
  the instance chain. Normal is `tex x C` alpha blended; Dodge reads the back buffer (`dst x (1 + S)`).
- **Masks.** SETMASK groups cut later USEMASK widgets (convex quads); the per-pixel noise dissolves are not reproduced.
- **Screen.** The page is 640 x 480 units with `s = H / 480`. A top-level instance is anchored left, right or centre
  by where its contents sit. `StretchAll` maps x over the full width: the binocular pages use it (the game stretches
  them, texts included).
- **Text.** HandelGothic (Bios Three when asked), top-aligned; tracking after each glyph; heights rounded to whole
  pixels so the bitmap glyphs keep a steady scale; lines 1.45 heights apart. A text with the AUTOSIZED flag (only
  the timer, A_Chrono/Chrono) takes the width of its text from its rect's left edge, so its centre alignment does
  not apply: the spy page places the timer at x 568 with a 135 unit box, and centred in it the text would cross the
  page's right edge; from the left edge it sits under the objective counter (estimated: Magma's AUTOSIZED is not
  decoded).
- **Driver overrides.** A driver writes `MagmaOverride` fields on a widget (`node.Widget(name)`): Hidden, Text,
  Color, Rotation, Pos, Top, MoveTop, Region, KeepInside. `PositionOverride` and `StateOverride` move or tint an
  instance.

## Data binding

The pages never touch simulation objects. Each frame `HudLayer` runs the effects and the message driver, clears a
`HudSnapshot`, an `IHudSource` fills it, area drivers read what they need, and the page drivers read only the
snapshot. `HudSimReader` is the source over the local `SPlayerController`:

| Snapshot | Read from |
|---|---|
| side, health, poison, location, speed, yaw | pawn `SPawnType` (else the controller's), `Health`, `bPoisonned`, `Location`, `Velocity`, controller yaw |
| energy | `SPawnAttaque.EnergyLevel` / `EnergyLevelMax` |
| weapon | `SWeaponDefense` clip, ammo, `ROFMode`, the weapon class (rifle, UZI, shotgun reticle), `WeaponLoaded`, `bNoReticule`, accuracy, laser on, `fDateLastShootedPawn` |
| gadget, vision | `SelectedGadget`; `STeamInfo.iNBGadgets`; `RendMap` |
| timer, objectives, counter | GRI time and mode; `MissionRules.GetObjectivesLeft`; deathmatch team lives (`TotalTeamLives`, 1000 = "Inf.") |
| countdowns, alarm | `SHud.ActorToDraw` (hack, two objectives); the alarm box from `SHud.AlarmsList` (priority above 4) |
| markers | every SObjective's triggers; `MalfunctionGamePlayObjects` (GPO diamonds) |
| messages | the four SHud stacks and the game queue head; `ReticuleMessage`; the tutorial line `sTutoMap` |
| radar, arrows | other pawns by side, spy bullets, alarms, `SHud.ObjectivesList`; the six sound arrow times, set by `SHud.NotifyHotSound` from `SPlayerController.ClientNotifyNoise` (frags, chaff, landings, extinguishers; never the merc rifle) |
| interaction list, prompts | `IManager` entries, Speak / Hack, the coop icons (decoded driver, `InteractionPrompts`), the respawn weapon |
| players, score, status | `GRI.PRIArray` (name, team, objectives or kills, deaths, lives, ping), the mission objectives |
| effects | `HudEffects` (below): washes, perturbation, shutdown, foam, flashes, sleep; pawn sticky shock and rain |
| spectator | `IsSpectating` states, `bGameEnded`, the watched pawn, respawn date, team tips |
| binoculars | state `s_Binoculars_Att` / `s_Binoculars_Def`, `fBinocularVisionFOV`, breath, hit time, voice target, the range finder line, the spy's objective triggers and lock |
| HUD switches | `bHideHUD`, `bLifeOff`, `bGadgetOff`, `bTimeOff`, `bAMMOOff`, `BallisticValue`, `EnhancedRealityLevel`, `bShowConnectedPlayers` |

The reader is read only, with two HUD-local exceptions: `HudMessages.Tick` prunes the SHud stacks (the original HUD
does it in its draw) and `HudLayer` runs `SHud.PostNetBeginPlay`'s HUD part on a new SHud (Enhanced Reality 1,
metric units, the connected players list as the settings keep it).

## Page manager and drivers

Each frame `HudLayer` picks one page: a registered `IHudPageOverride` that is active (the binocular pages come
first, then any added by other modules), else `P_SpectatorMode` while spectating, else the side's page. Every
top-level area of a page starts hidden and stopped at frame 0; each driver shows what it draws.

| Area | Rule (spec) |
|---|---|
| Vie, Energie | fill top `min(133, 137 - v x 1.33)`, triangles 3 units above; both show the current health at once (the native 0x10b30090 curve blend truncates to 0); poison frame |
| Objectif | the Story count restarting on change; in a deathmatch the DeathMatch counter "own/other" lives (counters-and-prompts.md) |
| Chrono | `rem:ss` of the time limit; kept inside the right screen edge |
| ChronoHack, ChronoObjectif(2), ChronoAlarm | countdown boxes; the alarm box while an alarm of priority above 4 runs |
| Gadget, Ammo | gadget charge, name and icon; `ClipAmmo/Ammo` |
| System, State, Zone, Game messages | the decoded message driver (messages.md): stacked lines, restart only when shown, the queue head for 5 s, waits while the tutorial line shows |
| Tutorial | `A_TutoMessage` with `sTutoMap` |
| Spy_Speed, Speak, Coop1..4 (spy) | walk indicator; "Speak"/"Hack"; the coop icons |
| Reticule, ReticuleMessage, Pos_Merc, AlertMessage (merc) | reticle per weapon kind with spread and hit colour, laser gate, sound arrows, reticle message at the aim point, posture icon, alert box (merc-reticle.md) |
| merc effects | perturbation, shutdown and reboot, foam, droplets, sticky shock, flash white-out (screen-effects.md) |
| Ballistic | the charge gauge |
| PCConnectedPlayers | up to four rows while shown (F10); Reloaded's ping column when `show_ping` is on; OpenSCCT's Rank column (level tag, spec/additions/progression.md) when a listed player has a rank; Reloaded's split (`scoreboard_split_team_size`, 1 to 4, default 3): once the bigger team has that many players (and both teams someone), the list shows the spies and a drawn column beside it the mercs, each up to four rows |
| Radar | dish, blips, edge arrows (spy teammates and enemies, merc alarms and hacked terminals) |
| P_SpectatorMode | objectives, time, lives, watched player, respawn countdown, tip (spectator-page.md) |
| P_BinocularsATT / DEF | zoom label and marker, scope ring animations, sights and axes at the aim point, breath gauge, range finder, key help, Hack prompt, reticle message (binoculars.md) |

**When a page shows.** `SPlayerController.SpawnMagmaHud` opens the side's page once the player is ready
(`PlayerAreReady`): the PRI has a team, or it only spectates (`HudLayer.MagmaHudOpen`); before that no page is drawn.

**End banner.** `GameEnded.BeginState` opens `P_EndGame` on the player's machine as the round ends
(spec/game/end-of-round.md rule 17), over the frozen world: `HudLayer.EndGame` (`EndGamePage`). Its update
(0x10b26ce0) leaves the page empty until `GRI.WinningTeam` replicates, then shows You Win when `PRI.Team.TeamIndex` is
the winner and You Lose otherwise (Chapter Completed on a tutorial), restarting an area only when it was hidden; the
area plays once and loops its end. It is a full-screen page, stretched over the screen like the binocular pages. The
end menu opens over it 10 s after the winner is known (rule 18), so online the results page opens on the table; the
banner of `ResultsScreen` plays only offline. The results' "You Win" side is the local player's row of the round's
results (`OnlineLobby.ToMatchResult`, `RoundResultSide`): the lobby keeps the lobby side, which a Revenge pair swaps
for its second game. The `net-lobby` smoke checks the banner and its side in both games of the pair on both players.

**Canvas parts.** Over the page, in this order: the spy's interaction list and Enhanced Reality markers (`HudMarkers`,
player pages only), the merc's motion tracking brackets (`HudMotionTracking`: the goggles' tracker's screen boxes
from the drawn camera and its decoded DrawHUD, in the 640 x 480 frame scaled by height and centred, also on the
camera network page; spec/equipment/motion-tracking.md; and the HUD's own tracker with the laser sight's target, its
typed name and the friend mark, SHud.DrawTargetName, spec/hud/merc-reticle.md rule 3e), the binocular markers (`HudBinocularMarkers`, the spy's binocular page only), the deathmatch
respawn weapon, the console lines, the screen-effect washes and the knock-out text (`HudSfx`), overlays (the chat
line), the frame rate readout. The SHud canvas doubles its colours (screen-effects.md rule 0, `HudK.CanvasColour`).

**Effects.** `HudEffects` turns the simulation's CombatFx notifications into what the SHud natives do on the hit
player's machine: `ClientDamageHudEffect` (red wash for spies, perturbation for mercs, flash-bang after-image and
white-out, extinguisher foam), `HudShutDown`, `SetBallisticValue`, the goggle messages. `HudEventTap` chains the
world's event sink so nothing else changes.

**Messages and kill feed.** `HudMessages` is the receiving side of `ReceiveLocalizedMessage` for SGameMessages,
SDeathMessage and the engine's GameMessage: `GetString` builds the text, `LocalizedMessage` routes each switch to the
game queue (ascending priority, the script's insertion quirk kept) or a stack. Deaths are the kill feed: priority 2
for a teammate, 1 otherwise, five seconds each at the head of the queue. On a client `HudReceiver` takes the server's
`ReceiveLocalizedMessage`, `TeamMessage` and `ClientMessage` calls (a fallback of the net function handlers).

**Console and chat.** `HudConsole` is `HUD.Message` / `AddTextMessage` / `DisplayMessages`: four lines of
"Name: text", each living max(0.09 s a character, 5 s), at 0.15 of the width, ending one line above 0.61 of the
height (chat.md).

**Scoreboard and status.** The connected players list toggles with F10 (`ShowConnectedPlayers`) and starts as the
settings keep it (Reloaded's `show_scoreboard`). F8 (`DisplaySessionStatus`) opens `GameStatusPage`, the mission
status: both teams with objectives or kills, and the objective list; F9 toggles Enhanced Reality
(`SwitchEnhancedReality`). `HudCommands` runs these execs from the player's layout (scoreboard.md).

## In-match menu

Escape (`ShowMenu` in both stock layouts) opens the original's In-Game Menu (`SSMenu_LANMainPage`, spec/hud/in-game-menu.md):
Back, Strategic Map (the map's floors with the player and his team mates, spec/hud/strategic-map.md; F7 opens it
directly), Options (the options screen over the game), Return to Lobby (only the
host of a network game) and Quit Session (End session, Cancel, Quit Game). The match is not paused, as in the original;
the player's controls stop and the pointer is freed while the menu is open, and keys are ignored for its first half
second. `InGameMenuModel` (Godot-free, unit tested) holds the rules, `UI/InGameMenu` draws them in the front end's style
(the original's GUI pages are not Magma pages) and `Play/PlayMenu` opens it and carries out leaving, the lobby and
quitting. Smoke test: `play-menu` (real key and mouse events through the play scene).

## Text chat

The Talk key (F5) opens the chat line with `Say `, TeamTalk (F6) with `TeamSay `; printable keys type (100
characters with the prefix), Backspace deletes, Escape drops, Enter sends. The player's keys stop while it is open.
The client sends `ControlMessage.Chat(scope, text)` on the control channel; the server's `ChatRelay` applies the
original's rules (spectators may not chat unless dead and the voice mode allows it, a shared budget of 260
characters a second, team lines to the sender's team) and sends each recipient a chat message with the sender's id,
name and team; the client hands it to `HudConsole` ("Ghost: on my way"). Spec: `spec/hud/chat.md`.

## Developer console

`UI/DevConsole` (rl:qol.console, rl:input.console) opens and closes on the key left of 1 (backtick or tilde, by
its place on the keyboard, so any layout), or on Chaos Theory's own console key, the apostrophe key, with
`override_console_key` off. It works in the menus and in play, but not over an open chat line or a focused text box.
It is a band over the top of the screen in a monospaced system font (Consolas, then other mono fonts). While it is
open it takes every key press, click and mouse movement, so nothing reaches the game or the menus; releases still
pass so no key stays held. Escape or the console key closes it; Up and Down walk the typed history.

Commands (`UI/ConsoleCommands`, Godot-free; settings through `Options/SettingsConsole`):

| Command | What |
|---|---|
| `help` | the list |
| `current` | every settings.json key and value |
| `get <key>`, `set <key> <value>` | a settings.json key; a group member with a dot (`controls_spy_kbm.look`); switches take on/off; the settings' limits apply; saved and applied at once |
| `<key> [value]` | a settings key alone shows it, with a value sets it (Reloaded's `fps_spy`, `fps_merc`, `ws_fov`, `widescreen`) |
| `login <password>` | the server's admin login (`!login`) |
| `cmd <command>` | a server console command (docs/ONLINE.md), for the host and admins |
| `practice on`, `practice off` | the server's practice mode (host and admins; `practice` alone says what it does) |
| `resign`, `pings`, `practice`, `heal`, `regen`, `tank`, `kill`, `ammo`, `rmap_next`, `rmap_prev`, `map <name> [mode]` | sent as the chat command (`!resign`, `!next`, `!prev`...) |
| `say <text>` | a chat line |
| `quit` | closes the game |

The server's system lines (answers to `cmd`, `!pings` and the others) are shown in it as they arrive. Not in smoke
runs (it is added by `App` outside them).

## Options and settings

The settings live in `user://settings.json`, JSON with comments and trailing commas allowed, keys named as
Reloaded's `settings.json` with its first-run defaults. A broken file is kept as `settings.json.bak` and the defaults
are used. `GameSettings` (Godot-free, unit tested) holds them; `SettingsStore` loads and saves; `SettingsApplier`
applies them at start and on save.

| Group | Keys |
|---|---|
| player | `player_name`, `mouse_sensitivity` (4..40), `invert_y`, `invert_x_spy` (profile InvertX), `shocker_snap` (on: drawing the Shocker turns the spy to the nearest merc in front of him; enh:rules.shocker_snap), `mouse_wheel` (Gadget, Speed: profile SelectedMouse 0 and 1), `clutch_one_key`, `clutch_two_key`, `use_split_spy_merc_keyboard` (on: separate layouts) |
| look per team | `controls_spy_kbm`, `controls_merc_kbm` (`look`, `free_look`, `scope`, `scope_vertical`, `vertical`, `cam`, `look_response`, `clutch_one`, `clutch_one_enabled`, `clutch_two`, `clutch_two_enabled`), and the same for `controls_spy_pad`, `controls_merc_pad` |
| controller | `controller`: `enabled`, `deadzone_left` 0.25, `deadzone_right` 0.15, `vertical_scale` 0.75, `run_threshold` 0.95, `slow_walk` (on), `prompted`, `rumble` (off), `rumble_strength` 1.0, 8-way move per side, `look_speed`, the pad layouts (`Clutch1`, `Clutch2` hold the clutches) |
| display | `display_mode` (Windowed, Fullscreen, BorderlessFullscreen), `display_monitor`, `resolution`, `window_position` (Default, Centered, Stretched), `vsync`, `widescreen`, `widescreen_hud`, `ws_er_small`, `ws_fov` (95..112, default 112), `fps_spy`, `fps_merc` (Monitor by default, Unlimited or 30 to 240: docs/RENDERING.md, frame pacing), `resolution_scale`, `filter_level`, `msaa`, `msaa_samples`, `smaa`, `shadow_quality`, `shadow_edge_smoothing`, `post_quality` (0 Normal: no glow, 1 High), `low_latency_mode`, `throttle_stop` (docs/RENDERING.md) |
| audio | `master_volume`, `sound_volume`, `music_volume`, `objective_sounds` (profile bObjectiveSounds), `voice_output_volume` (Godot buses Master, SvmWorld and the effect buses, SvmMusic, SvmVoice) |
| HUD | `show_scoreboard`, `show_ping`, `scoreboard_split_team_size` (1..4, 3), `show_fps`, `show_net`, `show_net_loss` (off), `show_location`, `hack_alpha` (0..255, 178), `hud_feet` (profile bInFeet), `fire_prediction` (on; docs/NETCODE.md "Lag compensation and fire prediction") |
| console | `override_console_key` (true: the key left of 1; false: the apostrophe key) |

`OptionsMenu` is the screen (from the main menu, or over the game with F12): Keys (rebinding the spy and merc layouts
through `KeyBindingsModel`; click a key, press the new one, Escape cancels), Mouse, Controller, Display, Audio, HUD,
then Save, Defaults, Back. `OptionsRuntime` applies the mouse look multipliers per team, the HUD switches, the frame
cap of the side played (not in headless or smoke runs), the field of view and the controller; `GamepadInput` maps Godot joypads to the
layouts with Reloaded's SDL defaults (radial dead zones, response curves, the spy's run threshold).

**Analog movement** (rl:input.pad): the left stick reaches the sim as a stick, not as keys. `GamepadInput` puts it in
`LocalInput.PadForward`/`PadStrafe` (after the deadzone and the optional eight-way snap; with `slow_walk` off only its
direction), `LocalInput.ApplyButtons` copies it onto the controller (`SPlayerController.PadStickForward`,
`PadStickStrafe`, `bPadStickMove`), and `ControlInput.Compute` (the NativeComputeControlVariables port) uses it in
place of the direction keys: `aForward`/`aStrafe` = stick x 6000 x speed mode, so any direction and a part push walks
slower; `fNormePadMove` is the stick's length x speed mode, and Reloaded's key diagonal fix does not apply. The keys
bound to BT_Up/Down/Left/Right are still held past 0.38 for everything that reads the key bytes. Moves carry the
acceleration, so online play needs nothing more. **Hot plug**: `PadMenus` shows "Gamepad connected: name" or
"Gamepad disconnected" for three seconds at the bottom of the screen, and asks once "Would you like to enable native
controller support?" when a controller is connected while controller support is off (`controller.prompted`; setting
the option on the Controller page counts as the answer).

**Look multipliers** (rl:input.sens, `MouseLook.Multipliers`): the view picks the group's field. A sticky camera or
the camera network takes `cam`; the binoculars and the merc's sniper scope take `scope`, the merc's vertical also
`scope_vertical` (2.45 turns the scope up and down as fast as sideways, 1 is the stock 40.8 %, Reloaded's legacy
sniper mode); otherwise the side's look (the spy's `free_look` holstered, `look` with the gun drawn) with `vertical`
scaling the up and down look relative to the side's default (3.5 spy, 1.0 merc: the meaning of `vertical` is not
decoded, so its defaults keep the stock ratio). They scale the mouse counts before the input mapping; the controller's
counts are scaled so they end up with the controller groups' fields instead (`GamepadInput.PadMultipliers`).
**Clutches** (rl:input.clutch): while `clutch_one_key` (or `clutch_two_key`, the merc's) is held, or a pad control
bound to `Clutch1` or `Clutch2`, the group's enabled clutch multiplies the look (before the response curve on a pad).
The Mouse page sets the keys (click, press the key; Escape clears it).

**Profile options.** The original reads InvertX, bInFeet, bObjectiveSounds and SelectedMouse from the player's
profile, which the rewrite does not have (`IDeviceHooks.GetPlayerProfile` stays empty: a profile there would also set
the kit). They live in the settings file instead: `MouseLook.Apply` puts `invert_x_spy` on the controller
(bInvertHorizontalAxisCamSpy), `HudLayer.DistanceInFeet` calls `SHud.HudUnit`, and the level service
`IProfileOptions` (`SettingsProfileOptions`, installed with the gear services) answers ClientSetMusic's objective
jingle check and the wheel (WheelUp, WheelDown) for the machine's own controller only. `mouse_wheel` defaults to
Gadget, as the rewrite always played; the original's default profile has Speed.

**Rumble** (rl:input.rumble, spec/combat/rumble.md): `PadRumble` hears the local player's `Vibration_*`
notifications through `HudEffects.Applied`, keeps the script's priority rule and plays the script's motor strengths
on the controller in use, scaled by `rumble_strength`. The Controller page's Test plays the blast's rumble.

**HUD extras.** `HudLocation` writes the name of the place the viewed pawn is in (the highest-priority named volume it
touches, else its zone's name: the map data's LocationName) at the top centre (`show_location`; Enhanced's position
is not decoded). `HudMarkers.HackAlpha` draws the icon of an objective being hacked at `hack_alpha` / 255.
Not done: `classic_crosshair` (the reticle already sits on the aim point, centred at rest; the stock offset is not
decoded).

**Field of view.** `ViewFov` (Godot-free, unit tested) is Reloaded's rule 20 as its code does it (Reloaded.Core
0x100a7080, 0x100a4ac0): with `widescreen` on the camera's FovAngle (horizontal over 4:3) keeps its 4:3 vertical angle
and wider screens add width, `2 atan(tan(fov / 2) x 3/4 x aspect)`, with the aspect capped at 16:9; the result is
capped at `ws_fov` in every view but the spy's sticky camera and the merc's camera network (Reloaded's GotoState hook
sets its exempt flag for `s_UseObject_StickyCam` and `s_UseObject_CamNetwork` only). The merc's FovAngle is 95 (read
live), 111 degrees at 16:9. With `widescreen` off the 4:3 horizontal angle is stretched over the screen. The HUD
follows its own key, `widescreen_hud` (rl:gfx.hud, on): off, every HUD page is stretched (`HudOptions.StretchHud`), as
the unpatched game. `ws_er_small` (on) keeps the Enhanced Reality icons at their 4:3 size on a wide screen; off they
grow with the width (`HudLayout.HudScreen`: either `widescreen` or `widescreen_hud` gives the wide ER canvas).
`OptionsRuntime` installs the view policy as
`FaithfulView.VerticalFovPolicy`.

**Widescreen layout** (`Hud/HudLayout`, Godot-free, unit tested in `tests/OpenScctGame.Logic.Tests/LayoutTests.cs`):

| Window | Laid out at | On screen | HUD page | ER extents (ws_er_small on / off) |
|---|---|---|---|---|
| 4:3 (1024 x 768) | 4:3 | as drawn | 640 x 480 at H / 480 | 640 x 480 |
| 16:10 (1152 x 720) | 16:10 | as drawn | edges out by 64 page units | 704 x 480 / 640 x 440 |
| 16:9 (1280 x 720) | 16:9 | as drawn | edges out by 107 page units | 746.7 x 480 / 640 x 420 |
| 21:9 (1680 x 720) | 16:9 | stretched by 21/16 | as at 16:9, stretched | as at 16:9 |

- Reloaded lays the picture out over `min(W, 16/9 H)` (0x100a4ac0) and builds every projection for that width
  (0x100a6260), so a window wider than 16:9 shows the 16:9 picture, 3D and HUD, stretched across it: its options
  call those modes "(21:9 stretched)". `FaithfulView` does the same on the main window (content scale mode
  viewport, aspect ignore, size `round(16/9 H) x H`) while `widescreen` is on, and puts the window's scaling back
  when the view goes.
- The HUD keeps the 4:3 scale (H / 480); an area is anchored left, right or centre by where its contents sit, so
  left-hand areas keep their distance from the left edge and right-hand ones from the right edge. Reloaded moves its
  virtual edges out by `ceil((aspect / (4/3) - 1) x 320)` page units on each side. Checked against the original's
  HUD captured in the sandbox: the spy's gadget box spans x 742 to 939 at 1024 x 768 and 1015 to 1200 at 1280 x 720
  (ours 1015.6 to 1200.3), the merc's left column starts at x 82 and 78.
- Enhanced Reality icons (`HudMarkers`) draw on a canvas of HudScreenRight x HudScreenBottom (0x10079880): with
  `ws_er_small` (Reloaded's first-run default) the icons keep the 4:3 size, without it the 640 wide canvas is kept
  and they grow with the width (`HudMarkers.ErSmall`, `WidescreenHud`; the settings file has no keys for them yet).
- Full-frame overlays: P_StickyCam, P_CamNetWork and the binocular pages are stretched over the picture
  (`StretchAll`; centred at 4:3 the sticky cam's scope effect left bare bands at the sides of a 16:9 screen); the
  merc's gas mask (`MaskGas` on P_HudDEF) covers the picture.

`--svm-smoke=aspect` plays SteeE as the spy and as the merc at 1024 x 768, 1152 x 720, 1280 x 720 and 1680 x 720 and
checks each: the window is stretched only at 21:9 and the play view is then 1280 x 720; the camera's vertical angle
is the 4:3 one (59.84 degrees for the spy's FovAngle 75, 78.60 for the merc's 95) at every size; every unrotated image
of a side area lies inside its page rect as HudLayout maps it and on the picture; the sticky cam, camera network and
binocular frames cover the picture from edge to edge; the gas mask is drawn. With a window and `SVM_PLAY_SHOTS` it saves
each view (`aspect-<side>-<ratio>-<view>.png`, the 21:9 ones stretched to the window). The overlays are forced over the
player page for the check, so every key help line of a page shows at once.

## Extension points

| API | For |
|---|---|
| `IHudAreaDriver` + `HudLayer.AddDriver` | drive areas of `P_HudATT` / `P_HudDEF` another module owns (gadget menus, gas mask, heart sensor); the page stops hiding the areas named in `Areas` |
| `IHudPageOverride` + `HudLayer.AddPageOverride` | replace the player page while a view is active (sticky cam, camera network); returns the `HudPage` to draw or null |
| `IHudOverlay` + `HudLayer.AddOverlay` | draw over the HUD in its font and atlas |
| `HudPage.Area`, `Show`, `Hide`, `SetShown`, `SetText`, `DrawCanvas` | driver helpers; `DrawCanvas` adds SHud canvas items over a page |
| `HudLayer.Connect(ClientSession)` | the server's HUD calls and chat lines |
| `PlayHud.Attach(scene, session, input, hud)` | the play client's wiring: settings, chat line, status page, HUD keys, key names for page texts |
| `ClientSession.SendChat`, `ChatReceived`, `TakeChat`; `ServerHost.Chat` | chat for other front ends (lobby) |

## Using it from the play client

```csharp
Hud = HudLayer.Attach(this, () => Session?.LocalController, out string? error);
if (Hud is not null) Hud.Projector = WorldToScreen;   // FVector (uu) -> screen pixels, null behind the camera
PlayHud.Attach(this, Session, Controls, Hud);           // messages, chat, options, status page, HUD keys
FrontEnd.ShowResults(GetTree(), result);                // at the end of a round
```

`HudLayer.Attach` returns null (with the reason) when `.content/shared/ui` is missing; the game then runs without a
HUD. `HudOptions` holds the slice switches.

## Front end

`Boot` opens the main menu unless a smoke test or `--svm-map` runs. Screens are built in code and switched with
`FrontEnd`: main menu, options (`FrontEnd.ShowOptions` opens `OptionsMenu`), lobby (`LocalLobby` fills the 2v2 with
bots), results (`P_EndGame`'s banner, then the scores, Rematch and Main menu). The old `UI/OptionsScreen` is no
longer opened.

## Smoke test

`--svm-smoke=hud` builds a small offline scene with real SBase actors (spy, mate and merc with pawns and HUDs, a
rifle, a sticky camera, a mission, a terminal being hacked, an alarm, a drop zone, players with pings, messages) and
checks what reaches the widgets, phase by phase:

| Phase | Checks |
|---|---|
| spy | timer, objectives, countdowns, alarm box 0:25, gauges, gadget, messages, kill queue, coop icon, pings, GPO marker, the red terminal marker, console line "Ghost: on my way", the red damage wash, frame rate readout; at 1920 x 1080, 2560 x 1080, 1024 x 768 and 1280 x 720 every text's drawn glyphs end inside the screen, and the timer is centred under the objective counter (before the AUTOSIZED fix: 1251 against 1195, now 1201) |
| spy extras | interaction list, chat line "> TeamSay go_" and its send, kill feed head swap, Enhanced Reality toggle |
| merc | rifle reticle, the left sound arrow from a real noise (none from a weapon dummy), alert box, posture icon, reticle message "Reloading", ammo, radar alarm, the stretched page with widescreen off |
| merc effects | sticky shock, droplets, flash white-out, foam, perturbation, shutdown and reboot |
| binoculars | spy: page swap, "x2"/"x6" and the marker's x, key help "Mouse Wheel Up / Mouse Wheel Down" and "B", hidden console texts, Hack prompt, lock panel on the drop zone with its hint, terminal icon, edge arrow; merc: page swap, zoom-out then zoom-in ring keys, breath gauge frame, red then orange sights, sights centred while holding breath, range text, reticle message |
| counters, spectator | deathmatch "7/Inf.", respawn weapon icon; spectator page texts (1/4, 8:44, lives, watched player, respawn 7, tip) |
| status, menus | mission status lists, main menu, lobby (2v2, ready, start), options (six tabs, key rows, the ws_fov slider 95..112, the settings JSON round trip), results banner and table |

Every drawn texture and font must exist and the font pages must be padded. It needs the converted UI content, so
CI's default set stays `boot` and `testbox`. With a renderer, `--svm-hud-capture=<dir>` saves each screen as a PNG
(keep them under `local/`). Unit tests: `tests/OpenScctGame.Logic.Tests` (settings file, message routing, field of view),
`tests/OpenScct.Game.Tests/Hud/SoundArrowTests.cs`,
`tests/OpenScct.Net.Tests/ChatMessageTests.cs`, `tests/OpenScct.Integration.Tests/ChatRelayTests.cs`.

## Cinematic camera (Free Camera, Hide HUD)

The Enhanced 4.0 binds `FreeCam` and `HideHud` (Options, Keys, "Cinematic Camera" rows) work when the host turned
on Alternate Mode and its "Cinematic Camera" switch (offline they always work). Rules: spec/hud/cinematic-camera.md.

- **Who:** a living player, at any time in the round. Without the switch the console says "Free look is not enabled
  on this server." or "Hiding the hud is not enabled on this server."
- **How:** the key runs `SPlayerController.FreeCam` / `HideHud` (`PlayerCommands`, `LocalInput.cs`), which asks the
  server (`ServerRequestFreeCam` / `ServerRequestHideHud`); the server checks its GRI and answers
  (`ClientStartFreeCam` / `ClientToggleHideHud`), and the player's machine starts the camera or flips the switch
  (`SPlayerController.CinematicCamera.cs`). FreeCam again stops the camera at once, without asking.
- **Free camera:** it starts where the view is. Mouse turns it; the movement keys fly it along the view; Jump goes
  up and Crouch down; the wheel steps the speed (600 uu/s to start); Shift is fast, Alt slow. No collision. The pawn
  stands where it was and takes no input; its body is drawn and the first-person weapon is not. The camera stops by
  itself when the pawn dies. Other players only see the pawn standing still.
- **Hide HUD:** `bCinematicHideHud` on the controller; `HudSimReader` adds it to the snapshot's `HideHud`, so the
  pages hide as for `HUD.bHideHUD`, and the merc's aim point hides too.
- **Not done:** pad controls for the free camera (triggers, shoulders); the server's relevancy still sees from the
  pawn, so things far from it may be missing when the camera flies far away.

## Slice additions and gaps

- `HudOptions.SliceExtras` writes the merc's fire mode and the vision mode as small lines (the original shows them on
  the rifle's sight screen and as the picture). `HudOptions.EnhancedReality` turns the markers on until profiles are
  ported.
- The simulation's damage notifications only notify: `SetHudPerturbationLength(1.5)` and `HudShutDown`'s bShutDown
  are applied by the HUD on the hit player's machine; `OverlayFade` has no SFX type, so its type is inferred.
- Key tokens: every text a page or the canvas draws goes through `KeyTokens.Expand` when it holds a backslash
  (`MagmaRenderer.ExpandText`; the native pass 0x10a02900): `\SECONDARYFIRE` becomes the name of the key whose whole
  binding is `AltFire | GadgetQuickChoice 1 | HoldBreath` (Right Mouse on both stock layouts), and so on for the 34
  tokens of spec/hud/counters-and-prompts.md rule 11. A token whose binding no key has stays as it is, as in the
  original. `HudOptions.KeyBoundTo` gives the key of a binding string from the player's layout.
- The motion tracking lock boxes are drawn (brackets on the HUD, the clean view rectangles in the post pass,
  docs/RENDERING.md).
- The merc's reticle sits on `SHud.SpotReticule` (spec/hud/merc-reticle.md rules 3 to 3d): HudMercAim runs PostRender's
  part and HudDef's aim point helper before each snapshot, projecting the eye plus 15000 along MyDesiredRotation
  through the last tick's calc view, and the page places A_Reticule with the original's half box (64, 62). Smoke
  `play-reticle` (game/Scripts/Smoke/PlayReticleSmoke.cs) checks shots and a frag against the drawn dot. A noise from
  an actor the client does not have gives no sound arrow.
- The original has no hit direction marker: damage feedback is the undirected wash or perturbation, the flash-bang
  only whites out a player facing it (the simulation's ClientDamage cone and trace), and the merc's directional cue
  is the sound arrows.
- The Reloaded scoreboard look is not decoded: the original list and status page are used, with Reloaded's ping column and team split drawn in the list's own layout. (The coop icons follow
  the decoded spy driver: `InteractionPrompts.cs`, spec/objectives/team-interactions.md.)
- Binoculars: the range finder traces from the rifle's FX bone (the original's bone is unidentified); markers behind
  the view get no edge arrow (the projector gives no point); only objective triggers are listed (not secondary
  objective props).
- Reloaded's second FOV path (CL = 0 in 0x100a7080: another runtime factor and 4/3) is used by a few draw paths not
  identified yet.
- The merc's HUD in the original also shows Enhanced Reality markers (terminals with their distances, seen in the
  sandbox captures); `HudMarkers` draws them for the spy only.
- Map-specific HUD (per-map tutorial chains, set pieces, map-placed devices) is not covered in this wave.
- To check against captures of the original: the console text height, the canvas colour doubling, the lock panel's
  colour, the list row pitch, and the overlays (sticky cam, camera network, binoculars, gas mask) at 16:9: they need
  key presses in the sandbox game, which the memory-only tools cannot make.
- The connected players list is shown from the start of a match: that is Reloaded's default (`show_scoreboard`
  true, spec/hud/scoreboard.md rule 2); the unpatched game starts with it hidden (`SHud.bShowConnectedPlayers`
  false). F10 toggles it and the setting is remembered.
