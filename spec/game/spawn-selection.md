# Spawn point selection

Units:
- uc:SBase.SGameInfo:FindPlayerStart
- uc:SBase.SGameInfo:RatePlayerStart
- uc:SBase.SGameInfo:RateStartByDistance
- uc:Engine.GameInfo:FindPlayerStart (overridden, not used)
- uc:Engine.GameInfo:RatePlayerStart (overridden, not used)
- uc:SBase.STriggerPlayerStart:Trigger
- uc:SBase.STriggerPlayerStart:UnTrigger
- def:Engine.PlayerStart
- def:SBase.STriggerPlayerStart

## Purpose
Chooses the PlayerStart a player logs in at and respawns at. Two passes exist: a Priority pass that is only
used while `GRI.bMatchHasBegun` is false, and a distance pass that picks the enabled start of the player's
side that is farthest from the nearest pawn. Merc Deathmatch has its own set of starts.

## Data
PlayerStart (placeable, `bStatic` true, `bNoDelete` true, hidden; CollisionRadius 40, CollisionHeight 80):

| Field | Type | Default | Use here |
|---|---|---|---|
| TeamNumber | byte | 0 | 0 spy start, 1 merc start (compared with SPawnType) |
| Priority | byte | 0 | Priority pass (higher wins, 0 never wins) |
| bEnabled | bool | true | disabled starts are skipped (distance pass: rated 0) |
| bMercDeathMatchStart | bool | false | start belongs to the Merc Deathmatch set |
| iSpyCam_InitAngle_Hor / _Ver | int | 0 | spy camera offset at spawn (respawn-and-spawning.md) |
| Location, Rotation, Event | | | spawn position, facing, event fired on spawn |
| bSinglePlayerStart, bCoopStart, bPrimaryStart, LastSpawnCampTime | | | not used by SGameInfo |

STriggerPlayerStart extends PlayerStart with `bStatic` false (so it is found by event dispatch, which only
visits non-static actors): `Trigger` sets `bEnabled = true`, `UnTrigger` sets `bEnabled = false`. Its initial
bEnabled comes from the map (class default true).

SteeE starts (measured from the map export; Z is the start position, TeamNumber 0 when not stored):

| Actor | Side | Priority | bEnabled | Location (x, y, z) | Yaw |
|---|---|---|---|---|---|
| PlayerStart256 | spy | 0 | true | (-1418.1, 1751.4, -119.0) | -37240 |
| PlayerStart259 | spy | 0 | true | (-1261.4, 1787.4, -119.0) | -33528 |
| PlayerStart3120 | spy | 0 | true | (-1353.6, 1902.5, -119.0) | -32304 |
| PlayerStart3147 | merc | 0 | true | (-1494.7, 2106.2, -1351.0) | -35108 |
| PlayerStart3149 | merc | 1 | true | (-1547.6, -2329.9, -1042.0) | -53636 |
| PlayerStart254 | merc | 2 | true | (-1339.5, 112.1, -1042.0) | -43160 |
| STriggerPlayerStart4614 (Tag enable_shop_spawn) | merc | 0 | true | (-1627.5, -1739.9, -401.0) | -43160 |
| 13 starts with bMercDeathMatchStart | merc | 0 | true | various | various |

No actor in SteeE sends an event to `enable_shop_spawn`, so the trigger start is on for the whole match.

## Rules
`FindPlayerStart(Player, InTeam, incomingName)`:
1. `InTeam = SPlayerController(Player).SPawnType`. The passed team is discarded. When Player is None (Login)
   this reads as 0, so the spy set is used.
2. `bMercDMStarts` = true only when the mode is Merc Deathmatch and at least one PlayerStart in the level has
   `bMercDeathMatchStart` (AllActors scan, stops at the first).
3. Standalone reuse: if Player and its `StartSpot` are not None, NetMode is Standalone, and
   (`bWaitingToStartMatch` or `PRI.bWaitingPlayer`), return `Player.StartSpot` unchanged. Login and
   ChangeTeam clear StartSpot, so this only applies to a controller whose StartSpot was set by an earlier
   RestartPlayer while one of those flags is still true.
4. Priority pass, only when `GRI.bMatchHasBegun` is false: `BestPriority = 0`; for every PlayerStart in
   AllActors order, skip it if not bEnabled, or `TeamNumber != InTeam`, or `bMercDeathMatchStart !=
   bMercDMStarts`; if `Priority > BestPriority` keep it. Return the kept start if any. Starts with Priority
   0 can never be returned by this pass; among equal priorities the first in actor order wins.
5. Distance pass: `BestRating = -1`; for every PlayerStart with `bMercDeathMatchStart == bMercDMStarts`
   (any team, any enabled state): `rating = RatePlayerStart(N, InTeam, Player)`; keep N if
   `rating > BestRating` (strict, so ties keep the earlier start).
6. `RatePlayerStart(N, Team)`: 0 if N is None, not bEnabled, or `TeamNumber != Team`; else
   `RateStartByDistance(N)`.
7. `RateStartByDistance(N)`: start from 1,000,000; for every SBasePawn in the level (AllPawns, both teams,
   living or dead bodies that still exist, including the player's own pawn if it still exists), take
   `d = |pawn.Location - N.Location|` (3D) and keep the minimum. The rating is that minimum distance, so
   the chosen start is the one whose nearest pawn is farthest away.
   $$\text{rating}(N) = \min\left(10^6,\ \min_{p \in \text{SBasePawns}} \lVert p - N \rVert\right)$$
8. Merc Deathmatch fallback: if no start was kept and the mode is Merc Deathmatch, scan every enabled
   PlayerStart (any team, any set) with `RateStartByDistance` and keep the strictly best, continuing from
   the BestRating left by rule 5.
9. Return the kept start (may be None only when the level has no PlayerStart of the wanted set).

When each pass runs (follows from rule 4 and match-lifecycle.md):
10. Network match (listen host and clients): StartMatchOnServer sets bMatchHasBegun before StartMatch
    spawns anyone, so match-start spawns and all respawns use the distance pass only. Priority is never
    used in network play according to the script.
11. Offline match from the menus: the match-start spawns run before bMatchHasBegun is set, so they use the
    Priority pass; respawns use the distance pass.
12. Editor Play Level: bMatchHasBegun never becomes true, so every spawn and respawn uses the Priority pass
    first. In SteeE a merc always lands on PlayerStart254 (Priority 2); spies have no Priority above 0 and
    use the distance pass.
13. Login always uses team 0 starts (rule 1). The login start only positions the controller; the pawn
    position comes from the later RestartPlayer call.

Worked example (estimated; real values depend on spawn order and the trace-adjusted pawn heights): SteeE
2v2 network match, spies spawned first. Spy 1 gets the first spy start in actor order (all rate 1,000,000).
Spy 2 gets the spy start farthest from spy 1 (about 160 uu apart, so whichever of the other two is
farther). Merc 1: nearest-pawn distances are about 1,260 (3147), 4,190 (3149), 1,880 (254), 3,510 (4614),
so 3149. Merc 2: 3149 now holds a pawn; 4614 is about 875 from it, 254 about 1,880 from the spies, so 254.

## States
No states. STriggerPlayerStart has no states; bEnabled is its only runtime variable.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Initial distance rating | 1,000,000 | uu | decoded | RateStartByDistance |
| Initial BestRating | -1 | | decoded | FindPlayerStart |
| Initial BestPriority | 0 | | decoded | FindPlayerStart |
| PlayerStart collision | radius 40, height 80 | uu | decoded | def:Engine.PlayerStart |

## Network
Server only (the GameInfo). STriggerPlayerStart replicates nothing that matters (DumbProxy, hidden).

## Edge cases
- If no enabled start of the player's side exists, the distance pass still returns a start: every
  non-matching start rates 0, which beats -1, so the first start of the right set in actor order is
  returned, even if it belongs to the other side or is disabled.
- A valid start whose nearest pawn stands exactly on it rates 0 and ties with invalid starts; the earlier
  one in actor order wins.
- A start disabled by UnTrigger mid-match is skipped from the next spawn on.
- Dead bodies still in the level (players still in s_Dead, or spectating without a teammate, see
  spectating.md) repel spawns like living pawns.
- Merc Deathmatch on a map without Merc DM starts uses the normal set with `bMercDeathMatchStart` false
  (rule 2), all players being mercs.
- The GameInfo base versions (Engine) are never called.

## Evidence
- local/re-out/uc/SBase/SGameInfo.uc: FindPlayerStart (cf475c55cddb), RatePlayerStart (df1f2e68f772),
  RateStartByDistance (e06d6d618e0b)
- local/re-out/uc/SBase/STriggerPlayerStart.uc: Trigger (9927f5a6b68b), UnTrigger (9cb3dc52f49d)
- local/re-out/uc/Engine/PlayerStart.uc, local/formats/classdefaults_effective.json Engine.PlayerStart,
  SBase.STriggerPlayerStart
- SteeE.sdc exports read with scctpkg (table above); `enable_shop_spawn` appears only as the start's Tag
- Natives: Actor.AllActors 0x10a28310 (304), Actor.AllPawns 0x10a28330 (1140)
- spec/enhanced/FEATURES.md enh:maps.steee ("a merc spawn on the third floor, active only for the match
  start, not for respawns"; "one merc always gets an outside spawn point")

## Open questions
- Conflict with the Enhanced notes: they describe a SteeE merc start that is used only at match start. By
  the script that can only happen when spawns run before bMatchHasBegun, which network play does not do
  (rule 10). Either Reloaded changes the start path, or the note describes Play Level/offline behaviour.
  Trace: 2v2 listen match on SteeE, five restarts, record each pawn's first Location and each respawn
  Location, and the value of GRI.bMatchHasBegun at the first RestartPlayer.
- Iteration order of AllActors and AllPawns (level actor list order); needed for tie breaks. Confirm it
  equals the SVMap actor index order, and whether AllPawns skips pawns pending deletion.
- Whether the native AllPawns list includes a pawn destroyed earlier in the same tick.
