# Game modes: what each mode changes in the generic rules

Units:
- uc:GUI.Create_Match:LoadMatchRules (contract only)
- uc:GUI.Create_Match:LoadModeRules (contract only)
- uc:GUI.Create_Match:SetMercDeathMatchRules (contract only)
- uc:GUI.Create_Match:SetDiskLimits (contract only)

The match options themselves are in `match-options.md`, the end conditions in `end-of-round.md`, the series and
revenge rounds in `round-travel.md`, the objective types in `spec/objectives/` (`terminal-hack.md`, `disk-flag.md`,
`bomb-target.md`, `disk-hunt.md`). This spec is the per-mode view and the menu contract that fills the settings.

## Purpose

SCCT Versus has one game rules class (`SGameInfo`) for every mode. A mode is the `GameMode` URL option; the rules
branch on it in a few places only: which map missions are put in play, Merc Deathmatch's team, spawn and end rules,
the spy's bomb gadget, and the menus' defaults. Everything else (lives, respawn, spectating, scoring, time limit,
rounds in a row) is the same code for all modes.

## Data

`EGameMode` (Core.Object), with the names the menus show:

| Id | Enum | Name | Used by stock maps |
|---|---|---|---|
| 0 | GM_Undefined | (zone missions only) | nested missions |
| 1 | GM_Extraction | Extraction | no |
| 2 | GM_Sabotage | Sabotage | no |
| 3 | GM_Neutralization | Neutralization | no |
| 4 | GM_DiskHunt | Disk Hunt | 70 maps |
| 5 | GM_Multi | Story | 78 maps |
| 6 | GM_DeathMatch | Deathmatch | 70 maps |
| 7 | GM_MercDeathMatch | Merc Deathmatch | uses the Deathmatch entry and, on 15 maps, extra merc starts |

`SMapSettings.Modes` entry fields (`GameModeInfo`), per mode:

| Field | Story / Deathmatch | Disk Hunt | Merc Deathmatch (reads the Deathmatch entry) |
|---|---|---|---|
| TimeLimit | minutes, menu clamp 0..20 | same | same |
| SpiesLives | spy lives, clamp 0..10 | same | first shown in the kill limit box, then replaced by the default kill limit |
| SentriesLives | merc lives, clamp 0..10 | same | not used by the defaults (merc lives box set to 0) |
| RespawnTime | seconds, clamp 5..15 | same | not used by the defaults (respawn box set to 1, floor 1) |
| ScoreLimit | clamped to 1..Handicap (Story and Deathmatch maps have Handicap 0, so 0) | disks to take | kill limit only through the menu (default 20) |
| Handicap | clamped to 1..NumDisksMax (0 when NumDisksMax is 0) | active disks | same as Deathmatch |
| NumDisksMax | 0 | maximum active disks | |

SteeE entries (`match-options.md`): Story 10 min, 4 / 3 lives, 10 s; Disk Hunt 10 min, 4 / 3 lives, 10 s, 20 active
of 30, 10 to take; Deathmatch 10 min, 8 / 4 lives, 5 s.

## Rules

Settings a network match starts with (`GUI.Create_Match`, contract; the host can change every box afterwards):

1. `LoadMatchRules` runs `LoadModeRules`, then `SetMercDeathMatchRules` in Merc Deathmatch.
2. `LoadModeRules` with untouched settings (no match running, nothing changed by the host) takes the map entry for
   `SMatchInfo.RulesModeFor(mode)` (Merc Deathmatch reads the Deathmatch entry): time limit clamped to 0..20, spy
   and merc lives to 0..10, respawn to `RespawnFloor(mode)`..15, then `SetDiskLimits(NumDisksMax)` (both disk boxes
   at most `NumDisksMax`), `SPersistentData.NumDisksMax = NumDisksMax`, active disks `Handicap` (below 1 gives 1,
   then above `NumDisksMax` gives `NumDisksMax`) and disks to take `ScoreLimit` (below 1 gives 1, then above the
   clamped Handicap gives Handicap). The boxes are: time, spy lives, merc lives, respawn, active disks, disks to
   take. Without an entry for the mode: time 0, lives 0 and 0, respawn 5, disks 1 and 1.
3. `SetMercDeathMatchRules`: the spy lives box becomes the kill limit, 1 to `MaxKillLimit()` (100); with untouched
   settings, or a stored ScoreLimit outside 1..100, `ScoreLimit = DefaultKillLimit()` (20) and it is shown; with
   untouched settings the merc lives box is set to 0 (unlimited) and the respawn box to 1.
4. `ApplyMatchRules` (`match-options.md` rule 26) copies the boxes into `SMatchInfo`: TimeLimit; Merc Deathmatch puts
   the kill limit box into ScoreLimit and sets NumLivesATT 0, other modes put it into NumLivesATT; NumLivesDEF,
   TimesRespawn (raised to the box minimum), Handicap; other modes than Merc Deathmatch take ScoreLimit from the
   disks to take box. It stores `SPersistentData.NumDisks = Handicap` and `NumDisksToWin = ScoreLimit` for every
   mode, and in Merc Deathmatch moves everyone in the lobby to the mercs.
5. The URL then carries TimeLimit, ScoreLimit, GameMode, LivesATT, TimesRespawnsATT, LivesDEF (`match-options.md`
   rule 23); a lives value of 0 means unlimited. The disk counts reach the game only through the host's
   `SPersistentData` (copied to `GRI.NumDisks` and `GRI.NumDisksToWin` by `InitGameReplicationInfo`).

   SteeE with untouched settings: Story `?TimeLimit=10?ScoreLimit=0?GameMode=5?LivesATT=4?TimesRespawnsATT=10
   ?LivesDEF=3`; Disk Hunt the same with `ScoreLimit=10`, `GameMode=4` and disks 20 / 10; Deathmatch
   `?TimeLimit=10?ScoreLimit=0?GameMode=6?LivesATT=8?TimesRespawnsATT=5?LivesDEF=4`; Merc Deathmatch
   `?TimeLimit=10?ScoreLimit=20?GameMode=7?LivesATT=0?TimesRespawnsATT=1?LivesDEF=0`.

What each mode does at match start (`match-lifecycle.md`, `spec/objectives/missions.md` rule 4):

6. Every map mission is deactivated, then the missions whose `GameMode` equals the match mode are reset (none in
   Merc Deathmatch). Story resets its Story mission (terminals, flags, bomb spots); Disk Hunt its disk mission with
   `MinimumObjectives = NumDisks` (`disk-hunt.md`); Deathmatch its Deathmatch mission, which has no objectives on
   stock maps (it only announces the mission text and fires its AtResetEvent). Missions of the other modes stay
   deactivated, so their objectives count as taken and their devices are hidden.
7. Merc Deathmatch first moves every playing controller to the mercs (`MoveEveryoneToMercs`); PickTeam and
   ChangeTeam force team 1 for the whole match; spawn selection prefers starts with `bMercDeathMatchStart` and falls
   back to any enabled start (`spawn-selection.md`).
8. Lives: each player gets `NumLivesATT` (spy) or `NumLivesDEF` (merc) at StartMatch. In Merc Deathmatch every
   player is a merc and gets `NumLivesDEF`.
9. Spy inventory: the bomb gadget (`SGadBomb`) is given to spies in Story and Sabotage only (inventory spec).
   Outside Story, bomb spots are hidden at begin play (`bomb-target.md` rule 1).

How each mode ends (`end-of-round.md`; the order of the checks is the same in every mode):

10. Time out: the mercs win (`END_TIMEOUT`) in every mode, Merc Deathmatch included.
11. Merc Deathmatch: the first player to reach the kill limit wins for the mercs (`END_STORY`); with limited lives the
    round also ends when at most one merc is still in (`END_TEAMDEATH`); objectives never end it.
12. Other modes: one side out of lives loses (`END_TEAMDEATH`; offline both sides must be out); then the spies win
    (`END_STORY`) when every `SObjective` actor is taken, which means when the live mission is complete: Story when
    its quota of objectives is taken, Disk Hunt when the spies hold `NumDisksToWin` disks at once. A Deathmatch
    mission without objectives never completes, so Deathmatch ends on time (mercs) or by a side running out of
    lives.
13. Kills, points and statistics are the same in every mode (`scoring-and-stats.md`), except that in Merc
    Deathmatch every kill counts for the killer's Kills, a suicide costs a kill, and every kill costs the killer
    5 points (same team). At the end of a Merc Deathmatch, `GRI.NumDisks` holds the winning kill count.

Joining and watching:

14. A player who joins after the match has begun becomes a visitor (`s_Spectating_Visitor`, watching any player)
    until the next map, in every mode but Merc Deathmatch, where he waits and then plays as a merc with
    `NumLivesDEF` lives (`StartLateJoiner`, `login-and-teams.md`). The same holds for a player joining during the
    end screen.
15. A dead player with lives left spectates teammates until his respawn; one out of lives keeps spectating to the
    end of the round (`spectating.md`). In Merc Deathmatch every player is a teammate.

Modes without stock maps:

16. Extraction, Sabotage and Neutralization select their own missions like any mode; the only other code that tests
    them is the bomb gadget (Sabotage), the bomb lock message (Sabotage), and the spy's idle mission reminder
    (Sabotage when the spy has no modem charge left). An absent `GameMode` option means Extraction.

## States

No states of their own.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Menu clamps | time 0..20, lives 0..10, respawn floor..15 | min, lives, s | decoded | Create_Match.LoadModeRules |
| Respawn floor | 5, 1 in Merc Deathmatch | s | decoded | SMatchInfo.RespawnFloor |
| Merc Deathmatch defaults | kill limit 20 (1..100), merc lives 0, respawn 1 | | decoded | Create_Match.SetMercDeathMatchRules |
| Defaults without a map entry | time 0, lives 0, respawn 5, disks 1 and 1 | | decoded | Create_Match.LoadModeRules |

## Network

Server only: the menus run on the host, the rules on the server. Clients learn the mode and the counts from the GRI
(`GameMode`, `TimeLimit`, `TeamLivesATT`, `TeamLivesDEF`, `TimesRespawn`, `NumDisks`, `NumDisksToWin`,
`NumObjectives`).

## Edge cases

- Story and Deathmatch map entries have `Handicap` 0, so the stored disks to take is 0 even though the box shows 1;
  it has no effect outside Disk Hunt.
- Merc Deathmatch's lives box doubles as the kill limit; a late joiner still gets `NumLivesDEF` lives.
- A Deathmatch map whose Deathmatch mission has objectives would end with a spy win when they are all taken (no
  stock map has them; StroD's missions mix triggers in other ways).

## Evidence

- Decompiled: `local/re-out/uc/GUI/Create_Match.uc` (LoadMatchRules, LoadModeRules, SetMercDeathMatchRules,
  SetDiskLimits, ApplyMatchRules, SetModeSettings), `SBase/SGameInfo.uc` (StartMatch, Login, PickTeam, ChangeTeam,
  FindPlayerStart, Killed, sCheckEndGame, sEndGame), `SBase/SBasePawn.uc` (AddDefaultGadgets mode test),
  `SBase/SBombTargetObjectiveTrigger.uc`, `SBase/SPlayerController.uc` (idle reminder).
- Map survey of the converted maps: mission modes and trigger classes per mode (`.content/svmap/*/sim/gameplay.json`).

## Open questions

1. Whether the Enhanced menus change any of these defaults compared with the stock `Create_Match` (the package is the
   Enhanced one, so the values above are the Enhanced ones; check the Reloaded host page).
