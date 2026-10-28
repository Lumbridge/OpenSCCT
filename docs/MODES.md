# Game modes

How the rewrite runs the generic match rules of every mode: Story, Disk Hunt, Deathmatch and Merc Deathmatch on
stock maps, and Extraction, Sabotage and Neutralization for custom maps built for them. The rules are specced in
`spec/game/game-modes.md` (the per-mode view and the menu defaults), `spec/game/` (match flow) and
`spec/objectives/` (objective types). Map-specific event chains are not part of this.

## One rules class, a mode number

There is a single game rules class, `SGameInfo` (`src/OpenScct.Game/SBase/SGameInfo*.cs`). The mode is the `GameMode` URL
option (`EGameMode`: 1 Extraction, 2 Sabotage, 3 Neutralization, 4 Disk Hunt, 5 Story, 6 Deathmatch, 7 Merc
Deathmatch; absent means Extraction). The code branches on it in a few places only:

| Where | What changes | Code |
|---|---|---|
| Match start | missions whose `GameMode` equals the mode are reset, all others deactivated (their objectives count as taken); Disk Hunt gets `MinimumObjectives = NumDisks` | `SGameInfo.StartMatch`, `SMission.Reset` |
| Teams and spawns | Merc Deathmatch: everyone on the mercs, merc deathmatch starts preferred | `PickTeam`, `ChangeTeam`, `MoveEveryoneToMercs`, `FindPlayerStart` |
| Kills | Merc Deathmatch: every kill counts, a suicide costs one | `SGameInfo.Killed` |
| End of round | Merc Deathmatch: kill limit and survivors; other modes: lives, then objectives | `SGameInfo.sCheckEndGame` |
| Late joiners | play only in Merc Deathmatch, watch otherwise | `SGameInfo.Login`, `StartLateJoiner` |
| Merc weapon | Merc Deathmatch: each Use press while dead picks the next gun (rifle, Uzi, shotgun) for the respawn, shown bottom right; other modes: the profile's gun | `SPlayerController.SpawnWeaponFor`, `ServerCycleRespawnWeapon`, `SHud.DrawRespawnWeapon` (spec/equipment/merc-guns.md) |
| Spy inventory | the bomb gadget in Story and Sabotage | equipment loadouts |
| Bomb spots | hidden outside Story; the "secured" line in Story and Sabotage | `SBombTargetObjectiveTrigger.PostBeginPlay`, `Lock` |
| Death | Disk Hunt: the carrier drops his disks | `SPlayerController.s_Dead`, `DropDisks` |
| Mission reminder | Sabotage: a spy with no modem charge is told so | `SPlayerController.MissionReminder` |
| Alarm list | Neutralization: a merc's HUD also drops its drawn actors | `SHud.EmptyAlarmsList` |

Everything else (lives, respawn, spectating, scoring, time limit, end screen, rounds in a row) is the same code for
every mode.

## Settings: from the map to the rules

```
SMapSettings.Modes entry ──MatchSetup.Defaults──> USMatchInfo ──GetOptionsString──> map URL ──InitGame──> SMatchInfo, GRI
                                       └──────────> disk counts ──USPersistentData (level service)──> GRI.NumDisks, NumDisksToWin
```

- `OpenScct.Game.Modes.MatchSetup.Defaults(settings, mode, map)` is the untouched match creation page
  (`GUI.Create_Match`): the map entry for the mode (Merc Deathmatch reads the Deathmatch entry), clamped as the
  menu does, Merc Deathmatch's kill limit 20 with unlimited merc lives and 1 s respawn, and the Disk Hunt counts
  (`Handicap` clamped to 1..`NumDisksMax` active disks, `ScoreLimit` clamped to 1..active disks to take).
- `GameOptions(nbPlayers)` gives the URL the lobby launch travels with. `ToPersistentData()` gives the host's
  settings object; the disk counts never travel in the URL.
- The host installs the settings object as the level's `USPersistentData` service before the map loads;
  `SGameInfo.PersistentData` reads it (an empty one otherwise, as in Play Level).

SteeE with untouched settings: Story 10 min, 4 and 3 lives, 10 s; Disk Hunt the same with 20 of 30 disks in play and
10 to take; Deathmatch 10 min, 8 and 4 lives, 5 s; Merc Deathmatch 10 min, kill limit 20, unlimited lives, 1 s.

The client's offline play uses the same path (`game/Scripts/Play/Session/PlaySettings.cs`, `PlayMapLoader`):

```
OpenSCCT.exe -- --svm-play=SteeE --mode=DiskHunt --side=spy
OpenSCCT.exe -- --svm-play=SteeE --mode=Deathmatch --side=merc
```

`--mode` takes Story, DiskHunt, Deathmatch or MercDeathMatch (and Extraction, Sabotage or Neutralization on a map
built for them); `--rules=playlevel` uses the editor's Play Level URL instead (unlimited lives, no time limit, no disk
counts).

The dedicated server's own command line (`svm-server --map SteeE --game "?GameMode=4?..."`) installs the same
settings object: `MatchSetup.Defaults` for the map and the URL's mode, given to the host as
`ServerSettings.HostSettings`, so a dedicated Disk Hunt puts the menu's active disks in play and needs the menu's
disks to take. An online session's server (`OnlineServer`) applies the lobby setup's counts instead.

The mission reminder (every mode): a player standing still for 20 s gets the job in the game message area: a spy
carrying the Story disk "go to the drop zone", a Sabotage spy without a modem charge the no-modem line, otherwise
each mission not taken shows its description for his side (`spec/hud/messages.md` "Mission reminder").

## Extraction, Sabotage and Neutralization

No stock map uses them: over the 85 converted maps no `SMission` has `GameMode` 1, 2 or 3 and no `SMapSettings.Modes`
entry has `GameModeId` 1, 2 or 3 (the "Extraction" and "Neutralization" missions of the Orphanage maps are nested
Story zones), and the original's mode box never lists them. Their rules are complete as the script has them (the
table above; `spec/game/game-modes.md` rule 16), so a custom SVMap map can use them:

- give the map an `SMission` with `GameMode` 1, 2 or 3 and its objectives (disks and drop zones, bomb spots,
  terminals), and an `SMapSettings.Modes` entry with that `GameModeId` for the menu's starting values;
- the map catalog then lists the mode after the four menu modes (`MapCatalog.ExtraModes`; playable when the mission
  has objectives and the map has spy and merc starts) and the Create Session page offers it from the settings entry;
- `MatchSetup.Defaults` reads the entry like Story's, the URL carries `?GameMode=n`, and the spies win when every
  objective of the mode's mission is taken, the mercs on time or lives.

Offline: `--mode=Extraction`, `--mode=Sabotage` or `--mode=Neutralization`.

## Objective types

| Type | Class | Mode | Spec |
|---|---|---|---|
| Terminal | `SComputerObjectiveTrigger` | Story | `spec/objectives/terminal-hack.md` |
| Story disk and drop zones | `SFlag`, `SFlagDropZone` | Story | `spec/objectives/disk-flag.md` |
| Bomb spot | `SBombTargetObjectiveTrigger`, `SBombTargetInteraction`, `SGadBomb` | Story (Sabotage) | `spec/objectives/bomb-target.md` |
| Disk Hunt disk | `SDisk` | Disk Hunt | `spec/objectives/disk-hunt.md` |
| End zone | `SEndZoneTip` | tutorials | `spec/game/end-of-round.md` |

The pickup clips' `AnimNotifyPickup` script notify (registered in `Movement/MovementNotifies.cs`) puts the picked
object in the hand and frees the hand of a Disk Hunt disk; `s_Pickup.EndState` still frees a hand that holds a disk
when no notify fired (a pawn without animation content). The controller states that carry them are in
`SPlayerController.Pickup.cs` (the generic pickup walk and pickup, the
Story disk walk and drop) and `SPlayerController.PlaceBomb.cs` (the plant). Their animations are the execute-action
cases 226 to 229, 244, 245, 322 and 323 in `Movement/InteractionActions.cs`.

Disk Hunt in short: `SMission.ActivateDisks` puts the active count in play one group at a time; a spy takes a disk
from the Use menu (it leaves play and counts as taken); the mission completes, and the spies win, once
`active count - disks still in play >= disks to take`; a dying carrier throws all his disks back into play; mercs
walking over a dropped disk send it home; a dropped disk goes home on its own after 20 s.

Bomb spots in short: a spy with the modem plants (about 1 s), the charge counts down `CaptureTime` seconds, mercs can
remove it from the menu and any damage defuses it; only one spot of an objective can be armed; the blast is the
native gameplay-object blast (15 within 200 uu with linear falloff, SDamage_Explosion) and takes the objective.
Pawns in reach go through their controller's `PawnTakeDamage` with the spy hold rule (`GadgetBlast.HurtPawn`,
`spec/native/natives2-damage.md` rule 1.3.4), so the explosion reaction knocks them down; pawns just outside are
shaken.

## End of round, per mode

All checks run in the same order (`spec/game/end-of-round.md`): time out first (mercs win in every mode), then Merc
Deathmatch's kill limit and survivors, then a side out of lives, then every `SObjective` taken (spies win). A
Deathmatch mission has no objectives on stock maps, so Deathmatch ends on time or by a side running out of lives.

## Rounds in a row and revenge

- The end menu decides the next round (`GameFlow/EndingMenuRules.cs`): the same map with GamesInARow one lower (and,
  in a revenge pair, the team points carried swapped), or the lobby when the series is over or a side is empty.
- `host.SwitchLevel(url)` then runs `ServerTravel` and `SGameInfo.ProcessServerTravel`, which give every client its
  travel URL (with its current team) and put the host's team in `Level.NextURL`.
- An online session's server (`OnlineServer.NextRound`) loads the next game of the series itself: it travels to
  `EndingMenuRules.NextRoundUrl` with every player, who logs in again with his side (docs/ONLINE.md "Results").
- `GameFlow/TravelUrl.cs` is the engine side the next map needs: `Split` the map from the options,
  `NativeOptions` keeps the last value of a repeated key (the next-round URL holds GamesInARow twice), `Merge` applies
  a relative travel to a player's own options. Login's revenge rule then swaps each player's side and InitGame reads
  the swapped points.
- `tests/OpenScct.Game.Tests/Modes/ModeRulesTests.cs` plays a revenge pair and a three-game series this way.

## Spectating and late joiners

A dead player with lives left watches teammates (Fire for the next one) until his respawn; one out of lives keeps
watching to the end of the round. A player who joins after the start watches any player until the next map, except
in Merc Deathmatch, where he plays as a merc once ready (`spec/game/spectating.md`, `login-and-teams.md`).

## Tests and smokes

- `tests/OpenScct.Game.Tests/Modes/`: Disk Hunt (`DiskHuntTests`), bomb spots (`BombTargetTests`), per-mode rules, late
  joiners and series (`ModeRulesTests`), the Story disk's carrier (`FlagCarryTests`), all with SteeE's own mode
  settings (`ModeTestKit.cs`). `BombTargetTests.The_blast_hits_pawns_within_the_damage_range_only` covers the blast
  on pawns (it runs; it is no longer skipped). Extraction, Sabotage and Neutralization select their own mission on
  a synthetic level (`ModeRulesTests.Extraction_Sabotage_and_Neutralization_select_their_own_mission`). The mission
  reminder, the pickup notify, the `--game` host settings and the catalog's extra modes have no tests yet.
- Client smokes (`tools/ci/godot-smoke.ps1 -Tests <name>`, need the converted SteeE): `play-diskhunt` (the spy walks
  to the nearest disk, picks it up through the Use menu and the server counts it, with the hand free again and the
  disk on his thigh), `play-deathmatch` (Deathmatch settings on the server, the spy killed and back with one life
  less after the death timeline).

## Not done yet

- A dedicated `svm-server --game` run takes the menu's untouched disk counts; there is no option to set other ones
  (the online session's lobby has them).
