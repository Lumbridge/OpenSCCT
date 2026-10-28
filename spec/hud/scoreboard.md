# In-match scoreboard: connected players list and the mission status page

Units:
- uc:SBase.SPlayerController:ShowConnectedPlayers, uc:SBase.SPlayerController:DisplaySessionStatus
- uc:GUI.View_GameStatus:InitComponent, uc:GUI.View_GameStatus:InitMission, uc:GUI.View_GameStatus:Timer,
  uc:GUI.View_GameStatus:InternalOnKeyEvent
- uc:GUI.Browser_StatsSpies:RefreshList, uc:GUI.Browser_StatsSpies:MyOnDrawItem, uc:GUI.Browser_StatsMercs:RefreshList,
  uc:GUI.Browser_StatsMercs:MyOnDrawItem
- rl:qol.scoreboard (Reloaded: on by default, remembered, ping)

## Purpose

The two ways a player sees the score during a match: the connected players list on the HUD page (F10) and the
"Mission Status" menu (F8) with both teams and the objective list.

## Data

| Field | Owner | Notes |
|---|---|---|
| bShowConnectedPlayers | SHud | toggled by the exec; false when the HUD begins |
| PRIArray | GRI | PlayerName, Team, bOnlySpectator, iNbObjectivesTaken, Kills, NumLives, Ping |
| Objectives, bChained | SMission | of the GRI's game mode |
| Description, DescriptionDEF, ObjectiveTaken | SObjective | |

## Rules

Connected players (F10, `ShowConnectedPlayers` in both stock layouts):

1. The exec flips `SHud.bShowConnectedPlayers`. The page list shows while it is set (docs/HUD.md: four rows,
   spies then mercs then spectators, objectives or kills, lives).
2. Reloaded keeps the list on by default and remembers its state (`show_scoreboard`, true); `show_ping` (false on a
   first run) adds each player's ping.

Mission status (F8, `DisplaySessionStatus`):

3. The exec stops the player's controls and opens the menu page "Mission Status". It refreshes once a second and
   closes on Escape, Enter, the pad's back or cancel, or the same key.
4. Two lists: "Spies" (columns Spy, Objectives, Lives) and "Mercenaries" (Mercenary, Kills, Lives), each the PRIs of
   its team that are not pure spectators, in PRIArray order; in a deathmatch the spy column reads Kills and shows
   kills. Lives of 100 or more read "Infinite".
5. The objective list: for every SMission of the GRI's game mode, every objective in order, one line each:
   `"- "` for an objective not taken (or, in a chained mission, any objective after one not taken), `"+ "` for a taken
   one, then the side's description (spies Description, mercs DescriptionDEF) with every `|` turned into a space.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Keys | F8 status, F10 players | | decoded | Default_kbd_0.ini, Default_kbd_1.ini |
| Refresh | 1.0 | s | decoded | View_GameStatus.InitComponent SetTimer |
| Infinite lives | 100 | lives | decoded | Browser_StatsSpies/Mercs.MyOnDrawItem |
| Column widths | 0.25, 0.1, 0.1 of the list | | decoded | Browser_Stats* defaults |

## Network

Everything is read on the player's own machine from replicated PRIs and the mission actors.

## Edge cases

- The status page lists every non-spectator PRI of a team, so a 4v4 shows all eight; the HUD list shows four rows.
- A chained mission marks every objective after the first open one as open, taken or not.

## Evidence

- `local/re-out/uc/SBase/SPlayerController.uc` ShowConnectedPlayers, DisplaySessionStatus; `GUI/View_GameStatus.uc`,
  `GUI/Browser_StatsSpies.uc`, `GUI/Browser_StatsMercs.uc`.
- spec/reloaded/FEATURES.md rl:qol.scoreboard; spec/enhanced/FEATURES.md enh:ui.scoreboard.

## Open questions

1. Reloaded's own scoreboard drawing (team columns at `scoreboard_split_team_size`, ping column look) is not
   decoded; the rewrite adds the ping as a column of the HUD list and, once the bigger team reaches the split
   size (its settings text: "Team size at which the scoreboard splits into a column per team, sized on the bigger
   team"), draws the mercs in a second column in the list's layout. Reloaded's Merc Deathmatch lines ("{} wins with
   {} kills", "Draw at {} kills", "No kills scored") are shown as the results headline; where Reloaded draws them is
   not decoded.
