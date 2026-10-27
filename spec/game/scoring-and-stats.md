# Scoring, kill credit, death messages and player statistics

Units:
- uc:SBase.SGameInfo:AddPoint
- uc:SBase.SGameInfo:InitScores
- uc:SBase.SGameInfo:BroadcastDeathMessage
- uc:SBase.SGameInfo:FindDamageVolume
- uc:SBase.SGameInfo:CountEnvironmentalKill
- uc:SBase.SGameInfo:BroadcastScore
- uc:SBase.SGameInfo:BroadcastTeamLocalizedMessage
- uc:Engine.LevelInfo:InitArrayNbPoints
- uc:Engine.GameInfo:BroadcastDeathMessage (overridden)
- uc:Engine.GameInfo:ScoreKill (not called)
- uc:Engine.GameInfo:ScoreObjective (not called)
- uc:Engine.GameInfo:CheckScore (empty)
- uc:Engine.GameInfo:BroadcastLocalized (contract)
- uc:SBase.SPlayerController:StandardReactionsToDamage (kill points and damage stats clauses)
- def:Engine.SPlayerStats

## Purpose
How points, kills, deaths and per-player statistics change during a match, which messages announce a death,
and the team point counters that carry over between revenge rounds. Kill and life bookkeeping itself is in
death-lives-respawn.md (Killed); this spec defines the scoring table and the parts of Killed that touch
statistics.

## Data
| Field | Owner | Type | Default | Replicated |
|---|---|---|---|---|
| NbPointsAttack | LevelInfo | int | 0 (or ScoreATT in a revenge round) | yes, bNetDirty |
| NbPointsDefense | LevelInfo | int | 0 (or ScoreDEF) | yes, bNetDirty |
| ActionPoints | LevelInfo | array of int | filled by InitArrayNbPoints | no |
| Score | PRI | float | 0 | yes |
| Kills, Deaths | PRI | int | 0 | yes |
| iNbObjectivesTaken | PRI and STeamInfo | int | 0 | yes |
| Stats | PRI | SPlayerStats object | created in PRI.PostBeginPlay on the server | no (server only) |
| GPODamageInstigator | SGameInfo | Pawn | None | no; set by gameplay objects that deal damage |
| TeamInfo.Score | TeamInfo | float | 0 | yes, never written by SGameInfo |

`SPlayerStats` (Enhanced addition, plain object, server only) holds integer counters (one float). Those
written by game flow code: TeamKills, DeathFromTeammate, Suicides, EnvironmentalKills,
DeathFromEnvironmental, Merc_SniperHeadshots, DamageTaken, DamageGiven, DropAttacks, DropAttacksTaken,
DeathFromDropAttack. The other counters (gadget, grab, weapon kinds, medkits, objective times) are written by
the systems that own those actions.

Scoring table set by `InitArrayNbPoints("POINT_Multiple")` (the only mode SGameInfo uses), index: name =
points:

| Index | Constant | Points | Index | Constant | Points |
|---|---|---|---|---|---|
| 0 | AP_D_KillWithWeapon | 2 | 10 | AP_A_Grab_Kill | 2 |
| 1 | AP_D_KillWithGadget | 2 | 11 | AP_A_Struck | 3 |
| 2 | AP_D_LockAttacker | 1 | 12 | AP_A_StickyHeadShot | 2 |
| 3 | AP_D_TakeOffSpyBullet | 2 | 13 | AP_A_DeacGadget | 2 |
| 4 | AP_D_WakeUpDefender | 1 | 14 | AP_A_NotDetected | 5 |
| 5 | AP_D_Spot | 2 | 15 | AP_A_TakeObjective | 0 |
| 6 | AP_D_ActivatePassive | 5 | 16 | AP_D_KnockOut | 2 |
| 7 | AP_D_ActivateCamera | 5 | 17 | AP_C_Suicide | -2 |
| 8 | AP_A_SpyBullet | 1 | 18 | AP_C_KillFriend | -5 |
| 9 | AP_A_Grab_Begin | 0 | | | |

(The other modes in InitArrayNbPoints, POINT_Objective, POINT_OffensiveObjective, POINT_Frag and the
all-zero default, are never selected by SGameInfo; port them as data.)

## Rules
`AddPoint(Team, P, Index)` (server):
1. `points = Level.ActionPoints[Index]`. If `Team == 0` add points to `Level.NbPointsAttack`, otherwise to
   `Level.NbPointsDefense`. Add `float(points)` to `P.PRI.Score` (P None reads as None: no score, no error
   effect). Then `sCheckEndGame()` and `sEndGame()` when true.
2. Callers and arguments (the Team argument is chosen by the caller, not derived from P):
   - Death by damage (after Killed): killer's team differs from the victim's: `AddPoint(killer team,
     killer, 0)` (+2); killer is the victim: `AddPoint(team, self, 17)` (-2); same team: `AddPoint(team,
     killer, 18)` (-5). Nothing when there is no killer controller. In Merc Deathmatch every kill is
     same-team and costs the killer 5 points (Kills still counts the kill).
   - Fall out of the world: `AddPoint(own team, self, 17)` if the pawn was still alive.
   - Elbow shock hit, drop attack (all variants), grab release by KO, and one more KO path: `AddPoint(0,
     attacker, 16)` (+2 to the attack counter even when the attacker is a merc).
   - Fall attack received by an enemy: the victim's controller is passed, team 0 when the attacker is a
     spy, 1 otherwise, index 16.
   - Grab start: `AddPoint(0, self, 9)` (0 points, but still runs the end check).
   - Objective taken with bScore: `AddPoint(0, scorer, 15)` (0 points in this table).
3. `InitScores(AP, DP)` sets both counters; no caller exists.

Statistics and credit inside `Killed` (see death-lives-respawn.md rule 1 for the full order):
4. Environmental kill, checked for an enemy (or Merc DM) kill: `FindDamageVolume(Killed.Pawn, DamageType)`
   returns the first SDamageVolume the victim's pawn is touching that is bActive, has the same DamageType
   and has a TriggerController (None if the pawn is None). The kill is environmental when DamageType is
   Crushed, or the killer is the controller of `GPODamageInstigator`, or the killer is that volume's
   TriggerController. `CountEnvironmentalKill(Killer, Killed)`: ignore None or self; ignore teammates
   outside Merc DM; `Killer.Stats.EnvironmentalKills++`, `Killed.Stats.DeathFromEnvironmental++`.
5. If the victim is an SPlayerController whose pawn carries a flag (SFlag, the "Secret Files" disk in
   SteeE), `CarriedFlag.CarrierKiller = Killer` (objectives spec uses it).
6. `BroadcastDeathMessage(Killer, Victim, DamageType)`: switch = the DamageType number; if DamageType is
   SDTSnipeShot and the victim's last damage hit pill 1 (head), switch 23 and the killer's
   `Stats.Merc_SniperHeadshots++`; else if the victim's PRI is bResigned, switch 24. Broadcast with
   `BroadcastLocalized(self, SDeathMessage, switch, Killer PRI or None, Victim PRI)` through the
   BroadcastHandler (every player receives it; message text is the HUD and localization spec).

Damage statistics (inside StandardReactionsToDamage, server):
7. A hit counts for stats when the killer controller exists, is not the victim, is an SPlayerController
   and is on the other team (or any team in Merc DM). For a counted hit with damage above 0:
   `counted = min(ActualDamage, victim Health before the hit)`; victim `Stats.DamageTaken += counted`;
   attacker `Stats.DamageGiven += counted`.
8. Drop attack hits that count: attacker `Stats.DropAttacks++`, victim `Stats.DropAttacksTaken++`, and
   victim `Stats.DeathFromDropAttack++` if the hit left health at 0 or below.

Messages to one team (`BroadcastTeamLocalizedMessage(Team, Class, Switch, PRI1, PRI2, Object)`): every
PlayerController whose `PRI.Team.TeamIndex == Team` receives `ReceiveLocalizedMessage` (team-less
controllers read index 0).

9. `BroadcastScore()` is empty; `BroadcastScoreTime` has no effect.
10. Not called anywhere in this game: GameInfo.ScoreKill (would add 1 point per kill), ScoreObjective,
    NotifyKilled, GameStats events. Score therefore comes only from AddPoint.

## States
None. In GameInfo state s_MatchOver, `ScoreKill` is a no-op (it is never called anyway); AddPoint still
works after the end.

## Constants
See the scoring table. Death message switches: DamageType number 0..22, 23 sniper headshot, 24 resigned.
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Scoring mode | POINT_Multiple | | decoded | SGameInfo.InitGame |
| Head pill | 1 | pill tag | decoded | BroadcastDeathMessage |
| Headshot switch | 23 | | decoded | BroadcastDeathMessage |
| Resign switch | 24 | | decoded | BroadcastDeathMessage |

## Network
Server only. The results replicate through PRI.Score, Kills, Deaths, iNbObjectivesTaken and
LevelInfo.NbPointsAttack, NbPointsDefense. Death messages go out as `ReceiveLocalizedMessage` (reliable,
server to each client). SPlayerStats never leaves the server in script (Reloaded's match log may read it).

## Edge cases
- Spies earn no points for objectives in this table (index 15 is 0); their team counter grows from kills,
  knockouts and drop attacks.
- A merc's knockout or drop attack adds to NbPointsAttack (team 0), not NbPointsDefense.
- AddPoint after the round ended still changes scores and re-runs the end check.
- A kill by a non-SPlayerController killer gives no AddPoint score (the cast fails) but Killed still
  credits Kills.
- `ActionPoints` is a dynamic array; writing index 18 makes it 19 long. Reading an index past the end gives
  0 in UnrealScript.

## Evidence
- local/re-out/uc/SBase/SGameInfo.uc: AddPoint (e277c89ebd4d), InitScores (08aaff412dbf),
  BroadcastDeathMessage (5c4b1759f361), FindDamageVolume (1488dcce31b3), CountEnvironmentalKill
  (9c45b15d8951), BroadcastTeamLocalizedMessage (bee326dd1a9d), Killed (b10d5a56b22b)
- local/re-out/uc/Engine/LevelInfo.uc: AP_ constants, InitArrayNbPoints (72ead77e791e)
- local/re-out/uc/SBase/SPlayerController.uc: StandardReactionsToDamage (3d3a25f936e2) kill points around
  line 4509; AddPoint call sites at lines 3827, 4980, 4993, 4997, 5016, 5424, 11119, 17562, 17601, 17736,
  17916, 19542, 21040
- local/re-out/uc/SBase/SObjective.uc NotifyTaken (AddPoint index 15)
- local/re-out/uc/Engine/SPlayerStats.uc

## Open questions
- Whether Reloaded's JSON match log or scoreboard reads SPlayerStats or TeamInfo.Score (affects what the
  rewrite must expose). Check the Reloaded match log output after a LAN match.
- The HUD/scoreboard formula that turns Score, Kills and NbPoints into what players see (HUD spec).
