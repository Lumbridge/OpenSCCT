# Match options: URL parsing, match settings and map mode defaults

Units:
- uc:Engine.GameInfo:GrabOption
- uc:Engine.GameInfo:GetKeyValue
- uc:Engine.GameInfo:ParseOption
- uc:Engine.GameInfo:GetIntOption
- uc:SBase.SGameInfo:InitGame
- uc:SBase.SMatchInfo:SetOptionString
- uc:SBase.SMatchInfo:GetOptionsString
- uc:SBase.SMatchInfo:GrabOption
- uc:SBase.SMatchInfo:GetKeyValue
- uc:SBase.SMatchInfo:ParseOption
- uc:SBase.SMatchInfo:GetIntOption
- uc:SBase.SMatchInfo:MaxKillLimit
- uc:SBase.SMatchInfo:DefaultKillLimit
- uc:SBase.SMatchInfo:RespawnFloor
- uc:SBase.SMatchInfo:IsDeathMatch
- uc:SBase.SMatchInfo:RulesModeFor
- def:SBase.SMatchInfo
- uc:SBase.SMapSettings:SearchForGameMode
- uc:SBase.SMapSettings:PostBeginPlay
- nat:SBase.SMapSettings.IsAmateurMap
- def:SBase.SMapSettings
- uc:GUI.Create_Match:SetModeSettings (contract only)
- uc:GUI.Create_Match:ApplyMatchRules (contract only)
- uc:GUI.loading:Timer (contract only: URL it builds)

## Purpose
Every match rule the server enforces (mode, time limit, lives, respawn delay, revenge rounds, scores carried
between rounds, expected player count) reaches the game as `?Key=Value` options on the map URL. This spec
covers how those options are parsed, the defaults when an option is missing, where the values come from
(the map's `SMapSettings.Modes` entry via the match creation menu, or nothing at all in the editor's Play
Level), and how `SGameInfo.InitGame` stores them.

## Data
`SMatchInfo` (a plain object created by `InitGame`, not an actor, not replicated). Defaults are
`def:SBase.SMatchInfo` and are only the starting values of a fresh object; `SetOptionString` overwrites
every field below except `Handicap`.

| Field | Type | Class default | Value after SetOptionString |
|---|---|---|---|
| LevelName | string | "" | option `MapName`, else "" |
| MaxPlayers | int | 4 | clamp(`MaxPlayers`, 0, 32), default the current value (4) |
| TimeLimit | int (minutes) | 0 | option `TimeLimit`, else 0 |
| ScoreLimit | int | 0 | option `ScoreLimit`, else 0 |
| GameMode | EGameMode (byte) | GM_Undefined | option `GameMode`, else 1 (GM_Extraction) |
| NumLivesATT | int | 3 | option `LivesATT`, else 0; then 0 becomes 999999 |
| TimesRespawn | int (seconds) | 15 | option `TimesRespawnsATT` (note the spelling), else 15 |
| NumLivesDEF | int | 3 | option `LivesDEF`, else 0; then 0 becomes 999999 |
| BalanceTeams | bool | true | option `BalanceTeams` equals "true" ignoring case |
| Revenge | bool | false | option `Revenge` equals "true" ignoring case |
| GamesInARow | int | 1 | option `GamesInARow`, else 0 |
| TimesReinitFlag | int | 0 | option `TimesReinitFlag`, else 1 |
| TimeToExplode | int | 0 | option `TimeToExplode`, else 1 |
| GameWithRanking | bool | false | option `GameWithRanking` equals "true" |
| LevelGap | int | 0 | option `LevelGap`, else 0 |
| Handicap | int | 0 | not parsed (set only by the menu, used as the active disk count) |

`EGameMode` (Core.Object): 0 GM_Undefined, 1 GM_Extraction, 2 GM_Sabotage, 3 GM_Neutralization,
4 GM_DiskHunt, 5 GM_Multi (shown to players as "Story"), 6 GM_DeathMatch, 7 GM_MercDeathMatch.

`SGameInfo` fields written by `InitGame`: `MaxPlayers` (GameInfo, config, class default 16),
`GameDifficulty`, `BroadcastHandler`, `AccessControl`, `bAllowWeaponThrowing`, `bEnableStatLogging`,
`MatchInfo`, `MaxTeamSize` (class default 32), `PlayersToWait` (byte), `ScoreBoardType` (name, class
default `SScoreBoard`). Level fields written: `NbPointsAttack`, `NbPointsDefense` (int, replicated),
`ActionPoints` (see scoring-and-stats.md).

`SMapSettings` (one object per map in `<Map>-i.utc`, looked up with `Level.GetLevelSettings(MapName)`):
`Modes` is an array of `GameModeInfo { TimeLimit, ScoreLimit, SpiesLives, RespawnTime, SentriesLives,
Handicap, NumDisksMax, GameModeId }`, plus `MapName` (display name) and localized briefing strings.
SteeE (`SteeE-i.utc`, measured from the package): MapName "ENHANCED STEEL SQUAT", Modes:

| GameModeId | TimeLimit | ScoreLimit | SpiesLives | RespawnTime | SentriesLives | Handicap | NumDisksMax |
|---|---|---|---|---|---|---|---|
| 5 (Story) | 10 | 0 | 4 | 10 | 3 | 0 | 0 |
| 4 (Disk Hunt) | 10 | 10 | 4 | 10 | 3 | 20 | 30 |
| 6 (Deathmatch) | 10 | 0 | 8 | 5 | 4 | 0 | 0 |

## Rules
Option string grammar (GameInfo and SMatchInfo carry identical copies of these four helpers):
1. An options string is a sequence of `?Pair` items. `GrabOption` succeeds only if the string starts with `?`;
   it returns the text up to the next `?` (or the end) as the pair and removes that pair from the string.
   Parsing stops at the first character that is not `?` at a pair boundary.
2. `GetKeyValue` splits a pair at its first `=`: key before, value after. A pair with no `=` is a key with an
   empty value.
3. `ParseOption(Options, Key)` returns the value of the FIRST pair whose key equals `Key` ignoring case, or ""
   when none matches. A key present with an empty value is indistinguishable from an absent key.
4. `GetIntOption(Options, Key, Default)` returns `int(value)` when `ParseOption` is non-empty, else `Default`.
   `int(string)` follows UnrealScript conversion: leading numeric prefix, 0 when not numeric.
5. Boolean options in SMatchInfo are true only for the text "true" (any case). `bool(InOpt)` used for
   `AllowThrowing` and `GameStats` in InitGame follows UnrealScript string-to-bool conversion ("true",
   "1" style); port it with the same conversion helper used elsewhere.

`SGameInfo.InitGame(Options)`, run once on the server after the GameInfo is spawned and before its
PreBeginPlay (UE2 map load order), in this order:
6. `MaxPlayers = clamp(GetIntOption("MaxPlayers", MaxPlayers), 0, 32)`.
7. `GameDifficulty = max(0, GetIntOption("Difficulty", int(GameDifficulty)))`.
8. If `GameSpeed` is non-empty, `SetGameSpeed(float)` (clamps to at least 0.1, sets `Level.TimeDilation`,
   restarts the GameInfo timer with period `TimeDilation`).
9. Spawn `Engine.BroadcastHandler` and `Engine.AccessControl` (the AccessControl class is always the Engine
   one; the dynamic load cannot fail in practice). If both `AdminName` and `AdminPassword` are non-empty,
   set the class default `bDontAddDefaultAdmin` before spawning. `GamePassword` non-empty sets the game
   password.
10. `AllowThrowing` non-empty sets `bAllowWeaponThrowing`. `GameStats` non-empty sets `bEnableStatLogging`,
    otherwise `bEnableStatLogging = false`.
11. Create `MatchInfo` and call `SetOptionString(Options)` (table above).
12. `MaxPlayers` is clamped again from the same option (no effect beyond rule 6).
13. If `IsPsx2()` then `MaxTeamSize = 2` (native, false on PC; MaxTeamSize stays 32).
14. `PlayersToWait = byte(GetIntOption("NBPlayers", 1))`.
15. If `MatchInfo.GameMode != GM_Undefined`, `ScoreBoardType = 'SScoreBoardStory'` (an empty subclass of
    SScoreBoard).
16. If `MatchInfo.Revenge`: `Level.NbPointsAttack = GetIntOption("ScoreATT", 0)` and
    `Level.NbPointsDefense = GetIntOption("ScoreDEF", 0)`. Without Revenge these stay at their level
    defaults (0).
17. `MatchInfo.GamesInARow = GetIntOption("GamesInARow", 0)` (same as SetOptionString already did).
    The write to `GameReplicationInfo.GamesInARow` here hits None (the GRI does not exist yet) and does
    nothing; the GRI gets the value later in InitGameReplicationInfo and PostLogin.
18. `Level.InitArrayNbPoints("POINT_Multiple")` (scoring table).

SMatchInfo helpers:
19. `MaxKillLimit() = 100`, `DefaultKillLimit() = 20` (used when Merc Deathmatch ScoreLimit < 1).
20. `RespawnFloor(Mode) = 1` for GM_MercDeathMatch, else 5 (menu clamp only, never applied to URL values).
21. `IsDeathMatch(Mode)` is true for GM_DeathMatch and GM_MercDeathMatch.
22. `RulesModeFor(Mode)` maps GM_MercDeathMatch to GM_DeathMatch (Merc Deathmatch reads the map's
    Deathmatch settings), every other mode to itself.
23. `GetOptionsString()` produces, in this exact order:
    `?MapName=<LevelName>?MaxPlayers=<n>?TimeLimit=<n>?ScoreLimit=<n>?GameMode=<n>?LivesATT=<n>`
    `?TimesRespawnsATT=<n>?LivesDEF=<n>?BalanceTeams=<b>?Revenge=<b>?GamesInARow=<n>`
    `?TimesReinitFlag=<n>?TimeToExplode=<n>?GameWithRanking=<b>?LevelGap=<n>`, with bools written as
    UnrealScript prints them ("True"/"False") and the enum as its number. Note that a stored 999999 is
    written back as 999999, not 0.

SMapSettings:
24. `SearchForGameMode(Id)` returns the index of the first `Modes` entry whose GameModeId equals `Id`, or -1.
25. `PostBeginPlay` sets `IsAmateur = IsAmateurMap()` (native). SMapSettings is an Object, so this hook only
    matters if the engine calls it; record as unverified.

Where the values come from (contract with the GUI, not ported as gameplay):
26. Network match creation (`Create_Match.SetModeSettings`) takes the map entry for
    `RulesModeFor(GameMode)` and clamps: TimeLimit to 0..20, SpiesLives and SentriesLives to 0..10,
    RespawnTime to RespawnFloor..15 (5..15 outside Merc Deathmatch); if no entry exists it uses TimeLimit 0,
    lives 0, respawn 5, disks 1. The host may edit them; `ApplyMatchRules` copies the edited values into
    MatchInfo (Merc Deathmatch puts the "lives" box into ScoreLimit and sets NumLivesATT 0). In the menus a
    lives value of 0 means unlimited; values above 16 display as 0.
27. Launch from the lobby (`loading.Timer`, networked branch) travels to
    `<LevelName><GetOptionsString()>?NBPlayers=<Level.Game.NumPlayers>`, with BalanceTeams forced false.
28. Offline launch from the menus (`loading.Timer`, NetType NET_NONE) forces TimeLimit 0, both lives 0,
    TimesRespawn 5, then travels to `<LevelName><GetOptionsString()>?Team=<profile team>?Name=<name>`.
29. Editor Play Level uses `Autoplay.sdc?GameMode=5?Team=0?Editeur=true` (team 1 for a merc), with no other
    match options (measured, see Evidence). So Play Level runs with GameMode 5, unlimited lives (999999),
    TimesRespawn 15, TimeLimit 0 (no limit), PlayersToWait 1, MaxPlayers 16, Revenge false, GamesInARow 0.
    The map's `SMapSettings.Modes` values (10 min, 4 and 3 lives, 10 s) are NOT applied in Play Level.

For the slice (SteeE Story launched through the network menus with untouched defaults) the URL therefore
carries TimeLimit 10, LivesATT 4, LivesDEF 3, TimesRespawnsATT 10, GameMode 5.

## States
No states.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Unlimited lives sentinel | 999999 | lives | decoded | SMatchInfo.SetOptionString |
| Default GameMode when absent | 1 (GM_Extraction) | enum | decoded | SMatchInfo.SetOptionString |
| Default TimesRespawn when absent | 15 | s | decoded | SMatchInfo.SetOptionString |
| Default PlayersToWait | 1 | players | decoded | SGameInfo.InitGame |
| MaxPlayers clamp | 0..32 | players | decoded | SGameInfo.InitGame |
| MaxPlayers class default | 16 | players | decoded | def:Engine.GameInfo (no ini override in Default.ini) |
| MaxTeamSize | 32 (2 on PS2) | players | decoded | def:SBase.SGameInfo, InitGame |
| Default kill limit | 20 | kills | decoded | SMatchInfo.DefaultKillLimit |
| Max kill limit | 100 | kills | decoded | SMatchInfo.MaxKillLimit |
| Menu respawn floor | 5 (1 in Merc DM) | s | decoded | SMatchInfo.RespawnFloor |
| Menu clamps | time 0..20 min, lives 0..10, respawn floor..15 s | | decoded | Create_Match.SetModeSettings |
| SteeE Story settings | 10 min, 4 spy lives, 3 merc lives, 10 s | | measured | SteeE-i.utc SMapSettings.Modes[0] |
| Play Level URL | Autoplay.sdc?GameMode=5?Team=0?Editeur=true | | measured | RE+ memory objective-player-counts |

## Network
Server only. `InitGame` runs on the machine that owns the GameInfo (listen host, standalone, Play Level);
clients never parse match options. Clients learn the values through the GameReplicationInfo
(TimeLimit, TeamLivesATT, TeamLivesDEF, TimesRespawn, GameMode, Revenge, GamesInARow; see
replication-info.md). Client join URLs carry only player options (`Name`, `Team`, `Mesh`, `NewProfile`,
`GobalId` [sic], `PB`, `SpectatorOnly`, `LAN`), parsed by Login (login-and-teams.md).

## Edge cases
- Duplicate keys: script `ParseOption` returns the first match. The native URL parser that builds the
  options string handed to InitGame and Login is believed (UE2 behaviour) to keep only the last value of a
  repeated key. The round URLs built by the GUI contain `GamesInARow` twice (see round-travel.md), so the
  two parsers can disagree. Keep both behaviours distinct in the port.
- `LivesATT=0` and an absent `LivesATT` both mean unlimited (999999). A negative value is kept as given and
  makes the player out of lives from the start (`NumLives > 0` fails).
- `TimesRespawnsATT=0` is legal in a URL (menus never produce it): it changes the death flow to immediate
  respawn (death-lives-respawn.md rule 9).
- `TimeLimit=0` means no time limit (every time check requires `TimeLimit > 0`).
- An absent `GameMode` gives Extraction, not Story. A Play Level URL without `GameMode` would therefore
  reset only GameMode 1 missions.
- `Name` is cut to 20 characters by Login, not here.

## Evidence
- local/re-out/uc/Engine/GameInfo.uc (GrabOption, GetKeyValue, ParseOption, GetIntOption, SetGameSpeed)
- local/re-out/uc/SBase/SGameInfo.uc InitGame (sha1 029a22ef8080)
- local/re-out/uc/SBase/SMatchInfo.uc SetOptionString (cae85aad7ec7), GetOptionsString (6ff4f540bd87),
  RespawnFloor (f976a010dc3b), RulesModeFor (0e5a3df592b1)
- local/re-out/uc/SBase/SMapSettings.uc SearchForGameMode (eda559a8af0e)
- local/re-out/uc/GUI/Create_Match.uc SetModeSettings (05b87191b7be), ApplyMatchRules (cab7b3b8c608)
- local/re-out/uc/GUI/loading.uc Timer (28f83e3ad6ff)
- local/formats/classdefaults_effective.json SBase.SMatchInfo, SBase.SGameInfo
- SteeE-i.utc SMapSettings read with scctpkg (Modes table above)
- Natives: Object.IsPsx2 0x10949f40 (shared stub with IsPBClientEnabled and IsDedicatedServerMode),
  LevelInfo.GetLevelSettings 0x10a22ce0, SMapSettings.IsAmateurMap 0x10ab9aa0 (natives_all.txt)
- Play Level URL: memory note objective-player-counts.md (Reloaded Editor project), scct-test-harness.md

## Open questions
- Native URL parsing (FURL) of repeated keys: confirm "last value wins" in this build, and how the options
  string given to InitGame and Login is rebuilt. Scenario: Play Level with `?GamesInARow=2?GamesInARow=1`
  and read `MatchInfo.GamesInARow` (SGameInfo+0x4a4 then SMatchInfo+172) live.
- Whether the editor's Play Level dialog can pass extra options (needed to verify lives/respawn/time limit
  rules in Play Level). Scenario: launch Autoplay with `?LivesATT=4?LivesDEF=3?TimesRespawnsATT=10?TimeLimit=10`
  through the RE+ harness and read GRI.TeamLivesATT/TimesRespawn/TimeLimit.
- `IsPsx2` at 0x10949f40 is shared with two other natives; confirm it returns false (MaxTeamSize 32).
- Whether `SMapSettings.PostBeginPlay` is ever invoked for a non-actor object.
