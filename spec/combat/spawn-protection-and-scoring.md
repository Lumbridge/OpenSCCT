# Spawn protection, spawn shock, combat points and combat statistics

Units:
- uc:SBase.SPlayerController:EnterStartState (spawn protection part)
- uc:Engine.PlayerController:Restart (calls EnterStartState)
- uc:SBase.SPlayerController:ClientRestart (calls EnterStartState)
- uc:SBase.SPlayerController:StandardReactionsToDamage (protection and point sections)
- uc:SBase.SGameInfo:RestartPlayer (pawn placement and spawn shock)
- uc:SBase.SGameInfo:AddPoint
- uc:Engine.LevelInfo:InitArrayNbPoints
- rep:Engine.LevelInfo.NbPointsAttack
- rep:Engine.LevelInfo.NbPointsDefense
- rep:Engine.PlayerReplicationInfo.Score
- def:SBase.SPlayerController (const SpawnProtectionTime, MercDMGunProtectionTime)
- uc:SBase.SGameInfo:InitGame (scoring mode selection, reference only)

## Purpose
Freshly spawned players get limited protection, and players camping next to an enemy spawn are punished with a shock.
Combat actions award or remove points through one function. Enhanced 4.0 also counts many combat statistics on the
server; their increments are listed here so the port can reproduce the end-of-match stats.

## Data
| Field | Owner | Type | Default | Notes |
|---|---|---|---|---|
| fSpawnProtectionEnd | SPlayerController | float | 0 | now + 3.0 at each start |
| SpawnProtectionTime | const | float | 3.0 | declared; the code uses the literal 3.0 |
| MercDMGunProtectionTime | const | float | 1.0 | declared; the code uses the literal expression |
| Level.ActionPoints[19] | LevelInfo | array<int> | filled by `InitArrayNbPoints("POINT_Multiple")` in `SGameInfo.InitGame` | |
| Level.NbPointsAttack / NbPointsDefense | LevelInfo | int | 0 | team totals, replicated (bNetDirty) |
| PRI.Score | PlayerReplicationInfo | float | 0 | replicated |
| PRI.Stats | PlayerReplicationInfo | SPlayerStats | | server-only object, not replicated |

POINT_Multiple values (index: name: points): 0 AP_D_KillWithWeapon 2; 1 AP_D_KillWithGadget 2; 2 AP_D_LockAttacker 1;
3 AP_D_TakeOffSpyBullet 2; 4 AP_D_WakeUpDefender 1; 5 AP_D_Spot 2; 6 AP_D_ActivatePassive 5; 7 AP_D_ActivateCamera 5;
8 AP_A_SpyBullet 1; 9 AP_A_Grab_Begin 0; 10 AP_A_Grab_Kill 2; 11 AP_A_Struck 3; 12 AP_A_StickyHeadShot 2;
13 AP_A_DeacGadget 2; 14 AP_A_NotDetected 5; 15 AP_A_TakeObjective 0; 16 AP_D_KnockOut 2; 17 AP_C_Suicide -2;
18 AP_C_KillFriend -5.

## Rules

### Spawn protection
1. `EnterStartState` runs on the server (from `Restart` after `Possess`) and on the owner (from `ClientRestart`); both
   set `fSpawnProtectionEnd = now + 3.0` on their own clock. Only the server copy matters (damage is server side).
2. In `StandardReactionsToDamage`, before any effect (`damage-pipeline.md` rule 15):
   - a spy (SPAWNOFFENSE) ignores SDamage_Explosion while `now < fSpawnProtectionEnd`, in every game mode;
   - in GM_MercDeathMatch every player ignores SDamage_Explosion while `now < fSpawnProtectionEnd`, and ignores
     SDTBase, SDTBase_NoAnim and SDTSnipeShot while `now < fSpawnProtectionEnd - 3.0 + 1.0` (the first 1 s);
   - nothing else is protected: in story and objective modes a freshly spawned merc takes full damage of every type,
     and a spy takes full damage from bullets, shocks, drops, melee, crush and volumes.
3. No visual indicator or state is attached to spawn protection.

### Pawn placement at spawn (part of RestartPlayer)
4. After the start spot is chosen (spawn selection is owned by the game mode spec): trace down 200 uu from the spot
   with extent (35, 35, 10) and actors; if it hits, spawn at the hit Z + 79 (else at the spot location), with the
   spot rotation, ignoring collision at spawn (`Spawn(..., true)`). If spawning fails the player becomes a spectator
   (`s_Spectating_Visitor`).
5. Then: anchor and last start bookkeeping, `Possess`, teleport effect; spies get the view rotation of the spot plus
   its `iSpyCam_InitAngle_Hor/Ver` offsets (wrapped to the signed range), mercs the pawn rotation; default inventory;
   the spot's Event; desired rotation; start sound.

### Spawn shock (anti spawn camping)
6. Right after the new pawn is placed, for every other controller with a pawn and a team that differs from the
   spawning player's team: Delta = enemy pawn location - new pawn location;
   - if Delta.Z > 20 (enemy above): shock when `|Delta| < 220`;
   - else if `|Delta.Z| <= 20`: shock when `|Delta| < 170`;
   - enemies more than 20 uu below are never shocked.
   Shock = `enemy.PawnTakeDamage(0, new pawn, enemy location, Delta, SDTStickyShocker, -10)`: the taser path
   (51 damage if he is not already shocking, freeze, knock-down at the end of the freeze in a walking state; kill
   credit to the spawning player if lethal). See `shock-and-taser.md`.

### Points (AddPoint)
7. `AddPoint(Team, P, Index)`: if Team == 0, `NbPointsAttack += ActionPoints[Index]`, else `NbPointsDefense += ...`;
   `P.PRI.Score += ActionPoints[Index]`; then `if sCheckEndGame() then sEndGame()`.
8. Combat calls (Team argument, receiving player, index, points):
   - lethal damage to an enemy: killer's team, killer, 0, +2;
   - lethal damage to self (suicide, resign, self fall): own team, self, 17, -2;
   - lethal damage to a team mate: own team, killer, 18, -5;
   - falling out of the world alive: own team, self, 17, -2 (plus `Killed(self, self)`);
   - elbow strike accepted by the server: team 0, attacker, 16, +2 (for mercs too);
   - drop attack via performDropAttack, TellServerForDropAttack or the grab-state variants: team 0, attacker, 16, +2;
   - fall attack via TellServerToFallAttack between different SPawnTypes: team 0 if the attacker is a spy else team 1,
     receiving player = the VICTIM, 16, +2;
   - grab start: team 0, spy, 9, 0;
   - grab knock-out release: team 0, spy, 16, +2;
   - grab kills (neck break, choke, both die), crush and environmental counts: no AddPoint beyond the general kill
     rule, and grab kills do not go through it at all (they call `Killed` directly).
9. Kill credit rules (who is KillerController) are in `damage-pipeline.md` rules 11 to 13 and
   `environmental-damage.md`.

### Combat statistics (server, PRI.Stats; increments of 1 unless stated)
10. `damage-pipeline.md`: DamageTaken/DamageGiven (min(ActualDamage, Health) for enemy players), DropAttacks,
    DropAttacksTaken, DeathFromDropAttack; poison drain (`health.md`).
11. `DamagePawn_C2S`: Merc_ChargeHits (enemy SDamage_Charged), Merc_DeathFromLedgeGrab (lethal DamTypeGrabBreak).
12. Weapons: ShotsFired (each server shot, merc guns and shocker), ShotsHit (enemy pawn hit; for merc guns also any
    pawn in MercDeathMatch).
13. Shocks: Spy_ShockerGunHeadshots (accepted pill 1 shock on a merc), Merc_TaserHits.
14. Melee: ElbowStrikes / ElbowStrikesTaken (knock-back), StrikeAttacks / StrikeAttacksTaken (elbow knock-down),
    Merc_BerserkHits.
15. Grab: Spy_Grabs, Spy_LedgeGrabs, Merc_DeathFromGrab (CountMercGrabDeath).
16. Deaths: TeamKills, DeathFromTeammate, Suicides, Merc_SniperHeadshots (headshot death message),
    EnvironmentalKills, DeathFromEnvironmental.
17. Gas: Merc_KnockedFromSmoke, Merc_KnockedFromStickyCam, Spy_SmokeKOs, Spy_StickyCamKOs.
18. Declared but never written by script (keep the fields, leave at 0 unless Ghidra shows native writes):
    Merc_TerminateKills, Spy_DeathFromTerminate, Merc_MineKills, Merc_GrenadeKills, Merc_WeaponSniperKills,
    Merc_WeaponHipfireKills, Spy_DeathFromWeaponHipfire, Spy_DeathFromWeaponSniper, Spy_DeathFromMine,
    Spy_DeathFromGrenade, Spy_DeathFromJumpingTasedMerc.

## States
Not applicable.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| spawn protection | 3.0 | s | decoded | EnterStartState |
| MercDeathMatch gun protection | first 1.0 of the 3.0 | s | decoded | StandardReactionsToDamage |
| spawn ground probe | 200 down, extent 35/35/10, spawn at hit + 79 | uu | decoded | RestartPlayer |
| spawn shock radius above | 220 (Delta.Z > 20) | uu | decoded | RestartPlayer |
| spawn shock radius level | 170 (abs Delta.Z <= 20) | uu | decoded | RestartPlayer |
| kill points | +2 | points | decoded | POINT_Multiple index 0 |
| knock-out points | +2 | points | decoded | index 16 |
| suicide / teamkill | -2 / -5 | points | decoded | indices 17, 18 |

## Network
- Protection, shocks, points and stats are server only. `NbPointsAttack`, `NbPointsDefense` (LevelInfo) and
  `PRI.Score` replicate reliably when dirty; Stats never leave the server (Enhanced reports them at match end).

## Edge cases
- A merc spawning next to an enemy spy shocks him even if the spy is inside the spawn room legitimately; the spy can
  die from it if he had 51 hp or less (kill credit to the new merc, +2 points).
- AddPoint can end the match (it runs the end-game check), so a kill's points are applied after `Killed` already ran
  its own check; both checks must be idempotent (`sEndGame` moves the game to `s_MatchOver`).
- Elbow and drop points always go to team 0's total, even when a merc scores them (original quirk).

## Evidence
- uc:SBase.SPlayerController:EnterStartState sha1 57d15df2c32b; uc:SBase.SGameInfo:RestartPlayer eae9c00cb697;
  AddPoint e277c89ebd4d; uc:Engine.LevelInfo:InitArrayNbPoints 72ead77e791e; token dump of
  StandardReactionsToDamage (protection test `fSpawnProtectionEnd - 3 + 1`, AddPoint indices).
- local/re-out/uc/Engine/LevelInfo.uc (AP_ constants, InitArrayNbPoints), local/re-out/uc/SBase/SGameInfo.uc,
  local/re-out/uc/Engine/SPlayerStats.uc.

## Open questions
- Whether Reloaded.Core changes points or stats natively (it hooks several natives; none in this list). A full-match
  trace comparing PRI.Score after scripted kills, elbows and drops would confirm the table.
