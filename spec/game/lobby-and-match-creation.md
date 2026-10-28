# Network match creation, the lobby room and the voice settings

Units:
- uc:GUI.Create_Match:{InitComponent, CaptionNewControls, LayoutLanPage, LoadMatchRules, LoadModeRules, SetMercDeathMatchRules, SetDiskLimits, ApplyMatchRules, RevengeChange, VoiceChatChange, InternalOnKeyEvent, Tick} (contract)
- uc:GUI.Lobby_Create:{InitComponent, Tick, ButtonLaunch, ButtonClick, TeamVoiceOnlyChange, InternalOnKeyEvent} (contract)
- uc:GUI.Ending_Menu:{Timer, ChooseRandomMap} (contract: random map)
- uc:GUI.AlternateModeSettings:{InitComponent, AlternateKeyEvent, SpyGadgetChange, MercGadgetChange, Tick} (contract: Alternate Mode)
- uc:GUI.Briefing:{InitComponent, RefreshText} (contract: briefing)
- uc:GUI.BriefingDeux:{InitComponent} (contract: briefing)
- uc:Engine.PersistentData:{SetGadgetRestriction, GetGadgetRestriction}

Also read, not inventory units: the pages Game_Settings and Game_Settings_Client (the same values, read only for
clients), GUIChatBox (the spam delay), JoinWithIp, LAN_Menu, LAN_Seek_Games_List and KickPlayerValidation (captions
and fields); the page's sub-objects in GUI.u (SessionMaxPlayers, SessionGamesInARow, SessionTimeLimit, SessionSpyLives,
SessionMercLives, SessionRespawn, SessionActiveDisks, SessionDisksToWin, SessionPassword: the ranges); Reloaded's voice
page and overlay (FEATURES rl:voice.activation, device, gain, jitter, mix, test; rl:qol.lobby) and Enhanced's voice
modes and match options (enh:voice.modes, enh:match.create).

## Purpose
What a host sets up when creating a network session (Enhanced 4.0's Create Session page), what the lobby room offers
host and clients before launch, and what Reloaded's voice settings page and voice overlay offer. The URL options the
setup becomes are in match-options.md (rules 23 to 27); the voice routing the modes select is in voice-chat.md. This
spec only covers the menus' rules and values; the rewrite's own front end implements them (docs/MENUS.md).

## Data

Create Session page (LAN page of `Create_Match`, Enhanced 4.0). Captions from `GUI.int [Create_Match]`; ranges from the
page's sub-objects in GUI.u.

| Row | Caption | Range | Default | Stored in |
|---|---|---|---|---|
| Map | "Choose Map" | the installed maps of the active playlist | last map | SPersistentData.LevelName |
| Mode | "Game Mode" | the map's modes | the map's first | MatchInfo.GameMode |
| Password | "Password" | at most 16 characters of `a-z A-Z 0-9 . _ -`, masked | empty | the server's wanted password |
| Voice Chat | "Voice Chat" | Modern, Classic, Team, Open | Modern (index 0) | SPersistentData.VoiceChatMode, GRI.VoiceChatMode |
| Max Players | "Max Players" | 2 to 8 | 4 | SPersistentData.MaxPlayers, MatchInfo.MaxPlayers |
| Revenge | "Revenge" | check box | MatchInfo default (false) | MatchInfo.Revenge, GRI.Revenge |
| Random Map | "Random Map" | check box | false | SPersistentData.bRandomMap, GRI.bRandomMap |
| Voice in Lobby | "Voice in Lobby" | check box | true (SPersistentData default) | allowVoiceChatInLobby (profile and GRI) |
| Games in a Row | "Games in a Row" | 1 to 10 | MatchInfo default (1) | MatchInfo.GamesInARow, GRI.GamesInARow |
| Game Length | "Game Length (min)" | 0 to 20, 0 shown as infinite | map | MatchInfo.TimeLimit |
| Spy Lives | "Spy Lives" ("Kills to Win" in Merc Deathmatch) | 0 to 10, 0 infinite (1 to 100 as kills) | map | NumLivesATT (ScoreLimit) |
| Mercenary Lives | "Mercenary Lives" | 0 to 10, 0 infinite | map | NumLivesDEF |
| Respawn Length | "Respawn Length (sec)" | 1 to 15 | map | TimesRespawn |
| Active Disks | "Active Disks" (Disk Hunt only) | 1 to the map's NumDisksMax (control 1 to 50) | map Handicap | Handicap, SPersistentData.NumDisks |
| Disks to Take | "Disks to Take" (Disk Hunt only) | 1 to Active Disks | map ScoreLimit | ScoreLimit, SPersistentData.NumDisksToWin |

Voice Chat hints (`VoiceChatModeHints`), shown while the row is highlighted: Modern "Team always - hackable coms",
Classic "Living team - hackable coms", Team "Team always - no com hacking", Open "Everyone hears everyone".

The Ubi.com page (not LAN) hides Password and Voice Chat and adds Game Type (Free, Ranking) and Level Gap; the
dedicated server page adds Server Name (32 characters, default "Server") and "Minimum Players in Game" (1 to 4) and
hides Max Players. The LAN page also has "Alternate" (Alternate Mode, with its "Alternate Mode" button; section
"Alternate Mode" below); Tutor Mode is an Ubi.com feature. Game_Settings has an "Auto balance" row, hidden and always
checked (rule 23).

Lobby room (`Lobby_Create`, title "Lobby Room"): two lists "Spies" and "Mercenaries" (a "Lvl" column, "Waiting for
players" when empty), buttons "Switch Team", "Equipment", "Session Parameters", "Map / Mode", "Briefing", "Friends",
a check box "Restrict Mic to Team", the chat box ("Say", "TeamSay", "(Team) " prefix), and the page buttons: host
"Launch" / "Cancel", client "Ready" / "Not Ready", "Leave", "Options". "Inf." shows infinite lives.

Join with IP (`JoinWithIp`): "Server Address", "Server Port" (default 8888 in the original). LAN list
(`LAN_Seek_Games_List`): columns Ping, Server, Map, Mode, Players; "Unjoinable", "Incompatible server version".
Kick confirmation (`KickPlayerValidation`): "Do you want to kick" and the name.

Reloaded's voice settings page (its own settings window, strings in Reloaded.Core.dll), in order:
Device ("Windows default", "Windows reports no microphones.", "Not connected - falling back to Windows' choice."),
"Enable voice chat" (the game's Allow VoiceChat); Playback: "Voice Chat Volume", "Buffering" (jitter frames);
Microphone: mute ("Stops sending your voice. You still hear everyone else."), "Auto-Adjust Mic Volume", "Microphone
Volume"; Transmission: "Microphone level" bar ("transmitting" / "silent"), "Activation threshold", "Release time",
"Push to talk" (with the bound key, "Bind: none - Nobody can hear you"); Processing: "Noise suppression" (Low,
Standard, High, Extreme), "Echo cancellation" (Experimental); "Test my microphone" ("Recording - speak now",
"Playing it back", "Nothing was captured. Voice chat may not be running.").
Reloaded's voice overlay lists talkers per team (`##VoiceSpies`, `##VoiceMercs`) as "name - volume n%", "Speaking",
"Audible", "Muted"; "Click to mute/unmute, scroll to adjust" sets one player's volume; "Your microphone is muted",
"Click to unmute yourself".

## Rules
Create Session:
1. Changing map or mode reloads the rule rows from the map's settings for `RulesModeFor(mode)`, clamped as in
   match-options.md rule 26 (time 0..20, lives 0..10, respawn floor..15 with floor 5, 1 in Merc Deathmatch; Active
   Disks clamped to 1..NumDisksMax and Disks to Take to 1..Active Disks; no entry: 0, 0, 0, 5, 1, 1).
2. Respawn Length accepts 1 to 15 in every mode once edited; the floor of 5 only applies to the values loaded from
   the map (ApplyMatchRules clamps to the row's minimum, 1).
3. Merc Deathmatch turns the Spy Lives row into "Kills to Win" (1 to 100, default 20 when unset or out of range),
   sets Mercenary Lives 0 and Respawn 1 on a fresh page, and stores the kills as ScoreLimit with NumLivesATT 0.
4. Active Disks and Disks to Take show in Disk Hunt only; Disks to Take never exceeds Active Disks.
5. Revenge checked forces Games in a Row to 2 and hides that row; unchecked restores the last value (or 1).
6. Games in a Row below 1 becomes 1.
7. Voice Chat's index maps to the GRI mode: 1 Classic, 2 Team, 3 Open, anything else Modern; an index above 3 in the
   profile resets to 0.
8. Max Players outside 2..8 in the profile resets to 4. It cannot change once the session exists (row disabled in
   match); Password is read only then too.
9. Voice in Lobby is disabled (and unchecked) when the host's profile does not allow voice chat. Changing it while
   in the lobby opens or closes the lobby's voice session at once.
10. Applying copies every value to MatchInfo and the GRI (GameMode, TimeLimit, TeamLivesATT/DEF, TimesRespawn,
    NumDisks, NumDisksToWin, Revenge, GamesInARow, bRandomMap, VoiceChatMode, allowVoiceChatInLobby); Merc
    Deathmatch moves everyone to the mercenaries (`MoveEveryoneToMercs`).

Lobby room:
11. A client's page button is "Ready" / "Not Ready"; while ready, its side and option buttons are disabled.
12. When any session value the client was shown changes while it is ready (map, mode, rules), the client un-readies
    itself and the changed fields blink for 3 s (three blinks a second).
13. The host's "Launch" needs at least one player per team ("At least one player per team is required to launch the
    game.") and every player ready; then the match launches 1 s later and new connections are refused. Not all
    ready: the launch waits ("bMatchLaunching"); pressing again cancels. Reloaded lets a host start with an empty
    team (rl:fix.empty_team_start).
14. "Switch Team" toggles the profile team; it does nothing in Merc Deathmatch.
15. "Session Parameters" opens the editable page for the host and a read-only one for clients; "Map / Mode" opens
    Create Session again for the host (title "Session Map").
16. "Restrict Mic to Team" calls `ServerSetTeamVoiceOnly`: in the lobby the other team stops hearing this player
    (voice-chat.md rule 5).
17. Chat: a line sent less than 1 s after the previous one is not sent; the box shows "(Spam delay, 1 second)".
18. Leaving a ready client un-readies first and asks for confirmation.

After a match (Ending_Menu):
19. Once every player is ready ("Next"), a series with games left travels to the next game (match-options.md,
    round-travel.md); otherwise everyone goes back to the lobby with the session's options.
20. Random Map: when set and the series is over (no Revenge, or GamesInARow at most 1), the next map is drawn at
    random from a pool that excludes the map just played; the pool refills when one map is left. The stock pool is
    the maps numbered 01 to 12 without 07 (by the last two characters of the map name). For Disk Hunt the disk counts
    are reloaded from the new map's settings.

Voice (Reloaded):
21. Push to talk off (default) transmits through the activation gate (voice-chat.md, docs/VOICE.md); on, only while
    the Push To Talk key is held. Mute Microphone stops sending; everyone is still heard.
22. Per-player volume: the overlay's scroll wheel sets one talker's volume, shown as a percentage; a click mutes or
    unmutes him. Volumes and mutes last for the session.

Auto balance (Game_Settings, SGameInfo.PickTeam):
23. Game_Settings' "Auto balance" row is hidden and always checked; the lobby sets SPersistentData.bAutoBalance once
    no client is still connecting, and PickTeam, on the Lobby map only, then puts a player whose team is bigger than
    the other on the smaller one. The launch URL carries BalanceTeams false. The rewrite makes it the host's choice
    (off by default): a joiner asking for the bigger side goes to the smaller one, and a lobby switch that would leave
    the new side two people ahead is refused. Bots are not counted.

## Alternate Mode
24. Create_Match's "Alternate" box sets SPersistentData.bAlternateMode (and the GRI's). Its "Alternate Mode" button,
    and in the lobby the "Alternate" button (visible while the GRI's bAlternateMode is set), open AlternateModeSettings.
25. The page has, for the spies: Thermal Vision, Night Vision, Grab Player, Elbow Hit, Lethal Drop Tech, the eight
    gadgets of STeamInfo(Teams[0]).ClassInventory (captioned with each class's ItemName) and "Number of gadgets"; for
    the mercs: EMF Vision, Motion Tracking Vision, Elbow Hit, Lethal Drop Tech, Torch Light, Laser Sight, Berserk,
    Charge, their eight gadgets and "Number of gadgets"; and the Cinematic Camera box.
26. A gadget box is checked when its bit of the side's restriction byte is set (GetGadgetRestriction: the byte AND
    2^index); validation clears the byte and sets the bit of every checked box (SetGadgetRestriction toggles it). A set
    bit allows the option (Loadouts' IsAllowed).
27. "Number of gadgets" ranges 0 to 4; when four or fewer gadgets of the side are checked its maximum becomes that
    count and a higher value drops to it.
28. Validation (the A button) writes every box to SPersistentData; on the host it also copies them to the GRI's
    bAvlb_* switches, each STeamInfo's bDisallowedInv and iNBGadgets, and the cinematic camera, which every
    controller gets with ClientSetCinematicCamera. A client's page is read only and follows the GRI each tick.
29. Create Session's Apply without Alternate resets the settings object (InitPersistentData: every ability but both
    lethal drops, every gadget, four slots, no cinematic camera); in Deathmatch it then gives spies the lethal drop,
    takes the mercs' EMF, motion tracking, laser and berserk away, and allows one gadget a side (spy restriction 64,
    the flash bang; merc 128, the flare launcher; one slot each). The GRI's cinematic camera is on only in Alternate
    Mode.

## Briefing
30. The lobby's "Briefing" opens GUI.Briefing for the local player's side (team 0 spies, anything else the mercs'
    words): the level picture, "MISSION MAIN OBJECTIVE" with the map's ModesStrings[i].ExplicationModeSpy or
    ExplicationModeSentry for i = SearchForGameMode(RulesModeFor(mode)), the mode picture (story, disk hunt, or
    deathmatch for both deathmatches) beside GUIController's TextSpy or TextSentry for the mode, and the map's
    ExpliMapInBriefSpy or ExpliMapInBriefSentry.
31. Its A button ("Next") replaces it with BriefingDeux: the level's briefing picture (GetLevelBrief, the defender
    picture when none), ModesStrings[j].ExpliModeInBriefSpy or ExpliModeInBriefSentry for j = SearchForGameMode(mode)
    (the mode itself, not its rules mode), and LegendMapInBrief. Its A button ("Previous") goes back.

## States
No states.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Max Players | 2..8, default 4 | players | decoded | GUI.u SessionMaxPlayers; Create_Match.InitComponent |
| Games in a Row | 1..10 | games | decoded | GUI.u SessionGamesInARow |
| Game Length | 0..20 | min | decoded | GUI.u SessionTimeLimit (bZeroInfinit) |
| Lives | 0..10 | lives | decoded | GUI.u SessionSpyLives, SessionMercLives |
| Respawn Length | 1..15 | s | decoded | GUI.u SessionRespawn |
| Disks | 1..50 (capped by the map) | disks | decoded | GUI.u SessionActiveDisks, SessionDisksToWin |
| Password | 16 characters, `a-zA-Z._-0-9` | | decoded | Create_Match.InitComponent |
| Voice modes | Modern, Classic, Team, Open | | decoded | GUI.int [Create_Match] VoiceChatModes |
| Changed-field blink | 3 s, 3 Hz | | decoded | Lobby_Create.Tick |
| Launch delay | 1 s | | decoded | Lobby_Create.ButtonLaunch |
| Chat spam delay | 1 s | | decoded | GUIChatBox |
| Original join port | 8888 | | decoded | JoinWithIp.InitComponent |

## Network
The pages run on the host (listen) or the client; the server takes the values from the GRI and the travel URL. In
the rewrite the lobby is the session protocol (docs/ONLINE.md): the setup line carries every row above, a player line
carries ready, side and the team-only microphone flag.

## Edge cases
- Disk Hunt with a map whose NumDisksMax is 0: both disk rows show 1.
- A client joining after a change sees the current values without blinking.
- Random Map with Revenge: the map only changes once the revenge pair is over.

## Evidence
- local/re-out/uc/GUI/Create_Match.uc, Lobby_Create.uc, Game_Settings.uc, Ending_Menu.uc, GUIChatBox.uc, JoinWithIp.uc
- GUI.u sub-objects Session* read with scctpkg (ranges above)
- System/_PC_/GUI.int sections Create_Match, Lobby_Create, Game_Settings, JoinWithIp, LAN_Seek_Games_List,
  KickPlayerValidation, ExtraKeyRows
- local/reloaded_strings/reloaded_core_filtered.txt (voice page and overlay strings)

## Open questions
- Whether Reloaded's voice overlay is open in the lobby or only in a match.
- The stock random-map pool depends on map names ending in two digits; the converted maps' names do not, so the
  rewrite draws from the installed maps of the mode.
