# Spectator page (P_SpectatorMode)

Units:
- the spectator page object of the HUD page manager (native: init 0x10b2e460, update 0x10b2eeb0, its connected
  players 0x10b2eaa0)
- uc:SBase.SPlayerController:s_MultiSpectating.IsSpectating, uc:SBase.SPlayerController:BaseSpectating.IsSpectating

## Purpose

What a dead player or a spectator sees instead of the spy or merc page: lives, time, objectives, whom they watch,
when they respawn, and a tip.

## Data

| Field | Owner | Notes |
|---|---|---|
| bOnlySpectator, NumLives, iTipNumber, Team | PRI | iTipNumber is read once when the page opens |
| SpecTarget | SPlayerController | the watched pawn; its PRI gives the name |
| fRespawnDate | SPlayerController | set when the player dies: TimeSeconds + GRI.TimesRespawn |
| TotalTeamLives[2], TimeLimit, ElapsedTime, NumObjectives, GameMode, NumDisks, NumDisksToWin, Teams | GRI | |
| aTipStrings | STeamInfo | the side's tips (15 spy, 14 merc) |
| bGameEnded | SPlayerController | the end banner takes over |
| bHideHUD, bShowConnectedPlayers | SHud | |

## Rules

When:

1. The page replaces the spy or merc page while `PC.IsSpectating()`: true in `s_MultiSpectating` and its child
   states (`s_MultiSpectating_WhenKilled`, `s_Spectating_Visitor`) and in `BaseSpectating` and its children. A pure
   spectator gets it for the whole match. When IsSpectating turns false the player page shows again.

Every frame:

2. With `bGameEnded` the page's area hides (the end banner shows). With `bHideHUD` the page hides. Otherwise it
   shows, with the console button art (`bouton01`, `bouton02`) hidden.
3. Objectives: `"%u/%u"` of `NumObjectives - left` and `NumObjectives`, or in GM_DiskHunt `NumDisks - left` and
   `NumDisksToWin`. Remaining time: `rem = TimeLimit - ElapsedTime / 60 - 1`; below 0 "Inf.", else `"%u:%02u"` of
   rem and `59 - ElapsedTime % 60`.
4. A dead player (not bOnlySpectator): own lives under "Lives left:" (`"%u"`, 100 or more "Inf."). With a watched
   pawn: "Teammate lives left:" shows `TotalTeamLives[own side] - NumLives` (100 or more "Inf."); with its PRI the
   watched name shows after "Watching:", and "Switch player" shows once the team has three or more players.
   Without a watched pawn the team lives hide and the name empties.
5. A visitor (bOnlySpectator): "Switch player" shows; with a watched pawn and PRI the team lives hide, the watched
   player's lives show under "Lives left:" and the name after "Watching:". Without a target the texts keep their
   last values.
6. The tip: the PRI team's `aTipStrings[iTipNumber]` when the index is in range, else empty.
7. Respawn: with lives left and not a visitor, `s = trunc(fRespawnDate - now + 1)`; when `0 < s < 50`
   "Respawn in:" and s show and "No respawn left." hides, else all three hide. Without lives "No respawn left." shows.
8. Once shown, "Watching:" and "Switch player" are never hidden again.
9. Connected players: the same list as the player pages (docs/HUD.md), at (49, 167), while bShowConnectedPlayers.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Respawn shown below | 50 | s | decoded | spectator update 0x10b2eeb0 |
| Infinite lives | 100 | lives | decoded | spectator update 0x10b2eeb0 |
| Instance colour | (196, 233, 255) | RGB | decoded | Hud.mgm P_SpectatorMode `Spectator` |
| Connected players | (49, 167), (230, 230, 250) | page units, RGB | decoded | Hud.mgm P_SpectatorMode |

## Network

Read on the spectator's machine from replicated PRI, GRI and SpecTarget.

## Edge cases

- The tip index is read once per page; a new death picks a new index but the page keeps the old one until it is
  created again.
- The dedicated host's own spectator variant (both sides' lives) is not used by the rewrite's hosts.

## Evidence

- SCCT Versus UE5 Bridge spectator research (P_SpectatorMode and P_EndGame spec: page manager OpenMagmaMenu
  0x10b263b0, ctor 0x10b2f520, init 0x10b2e460, update 0x10b2eeb0).
- `Menus/Hud.mgm` P_SpectatorMode (converted `shared/ui/magma/Hud.json`); `System/_PC_/Magma.int` [A_Spectator].
- spec/game/spectating.md (states, SpecTarget).

## Open questions

1. The dedicated-host variant (textSpyLivesSD, textMercLivesSD) when the rewrite gets a non-playing host UI.
