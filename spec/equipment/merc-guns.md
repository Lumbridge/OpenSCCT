# Merc Uzi and shotgun (SWeaponDefense_Uzi, SWeaponDefense_Shotgun)

Units:
- def:SBase.SWeaponDefense_Uzi, def:SBase.SWeaponDefenseDummy_Uzi, def:SBase.SWeaponDefense_Shotgun,
  def:SBase.SWeaponDefenseDummy_Shotgun, def:SBase.SShotGunProjector, def:SBase.SShotGunEmitter
- uc:SBase.SWeaponDefense_Uzi:SpawnSFX, uc:SBase.SWeaponDefense_Uzi:CreateSmoke2
- uc:SBase.SWeaponDefense_Shotgun:SwitchROF, uc:SBase.SWeaponDefense_Shotgun:FireWeapon,
  uc:SBase.SWeaponDefense_Shotgun:SpawnSFX, uc:SBase.SWeaponDefense_Shotgun:SpawnImpacts,
  uc:SBase.SWeaponDefense_Shotgun:CreateSmoke1, uc:SBase.SWeaponDefense_Shotgun:CreateSmoke2,
  uc:SBase.SWeaponDefense_Shotgun:EventReloadFirst1 .. EventReloadFirst5,
  uc:SBase.SWeaponDefense_Shotgun:s_Reloading.BeginState, uc:SBase.SWeaponDefense_Shotgun:s_FullReloading.BeginState
- uc:SBase.SWeaponDefenseDummy_Shotgun:Tick, uc:SBase.SWeaponDefenseDummy_Shotgun:ClientFireMuzzle,
  uc:SBase.SWeaponDefenseDummy_Shotgun:SpawnImpacts, rep:SBase.SWeaponDefenseDummy_Shotgun.MinProjDistance,
  rep:SBase.SWeaponDefenseDummy_Shotgun.MaxProjDistance
- uc:SBase.SShotGunProjector:PostBeginPlay
- nat:SBase.SWeaponDefense_Shotgun.TraceFire (rules in spec/native/natives2-damage.md rule 7)

## Purpose
The two other merc guns of the team list (`STeamDefenseur.ClassWeapon` = rifle, Uzi, shotgun): how a merc gets them,
and what differs from the rifle (`merc-rifle.md`), which both extend.

## Data
Exact class defaults (`local/re-out/uc/SBase/SWeaponDefense_Uzi.uc`, `SWeaponDefense_Shotgun.uc`); every field not
listed is the rifle's (merc-rifle.md Data).

| Field | Uzi | Shotgun |
|---|---|---|
| ItemName / MagmaIconName / IconU, IconV | Uzi / W_HK81 / 0, 64 | Shotgun / W_BH401 / 64 (rifle's), 64 |
| Mesh (first person) / DummyClass mesh | SWeaponFirst.1DEF_Uzi / Hk81 | SWeaponFirst.1DEF_shotgun / BM401 |
| ClipAmmo, ClipMaxAmmo / Ammo, MaxAmmo | 60 / 360 | 6 / 50 |
| RateOfFire | 0.1 s | 1.15 s |
| BaseDamage | 10 | 17 (rifle's) |
| ShootingRange | 20000 (the rifle's script trace uses 15000) | 2500 (the native trace) |
| MinDispersion / MaxDispersion | 0.1 / 0.35 | 0.15 / 0.7 (rifle's) |
| ValeurReculVer / ValeurReculHor | 6.0 / 4.5 | 2.0 / 4.0 (rifle's) |
| CMax, CoefCaC, CoefMed, CoefMin | 1.765, 1.688, 0.95, 0.65 | 7.0, 6.0, 1.57, 0.43 |
| DistCac, DistMed, DistLoin | 400, 900, 1500 | 450, 950, 1300 |
| MinimumAmmoFlashReticule | 6 (rifle's) | 3 |
| bCanSnipe | false | false |
| FireSingleShotSound | Armes_impacts.Armes.SE_Uzi | Armes_impacts.Armes.SE_ShotGun |
| AFirstShooSt / AFirstReloSt | FirstShooStFd2U / FirstReloStFd2U | FirstShooStFd2S / FirstReloStFd2S |
| MuzzleGeneriqueClass | SMuzzleGenerique (rifle's) | SShotGunEmitter |

The Uzi also has `AFirstShootStLoop = FirstShooStFd2Ub`, which no script reads. The shotgun's
TotalShotgunDamageMultipler, RadiusScale, DemiAngleDispersion(_Coeff) and its own DamageLookupTable are read by
nothing (natives2-damage.md rule 7).

First-person clips (SWeaponFirst.ukx, 15 fps): FirstShooStFd2U 3 frames, FirstShooStFd2Ub 5, FirstReloStFd2U 31,
FirstShooStFd2S 16 (an AnimNotify_Sound Reload_Def_Pump, volume 100, radius 100, at 0.533 s: the pump),
FirstReloStFd2S 46 (AnimNotify_Script EventReloadFirst1..5 at 0.788, 1.110, 1.441, 1.919, 2.279 s, one per shell).

## Rules
1. **Getting one.** The profile's merc weapon (`DEFWeapClass`) can be any of the three: `GUI.Edit_Profile.WeaponClick`
   cycles it through `ClassWeapon[0..2]` and `SPlayerProfile.IsWeaponInMercTeam` accepts all three, so a merc carries
   his choice in every mode (in OpenSCCT the gear menu sets it, spec/additions/gear-change.md). In Merc Deathmatch the
   server substitutes the respawn weapon: `SBasePawn.ServerSpawnWeapon` asks `SPlayerController.SpawnWeaponFor`
   (spec/game/death-lives-respawn.md rules 17 to 21). The first spawn keeps the requested gun and tells the client;
   while dead (Merc DM, lives left, s_Dead) each Use press asks the server for the next gun of the list
   (`ServerCycleRespawnWeapon`, client to server), the server answers with `ClientSetRespawnWeapon`, and the HUD shows
   its icon with the Use key name bottom right (`SHud.DrawRespawnWeapon`, spec/hud/counters-and-prompts.md). The next
   respawn spawns it. There is no server option for it.
2. **Uzi.** The rifle in every way with its defaults. It still has the three fire modes. Its first-person muzzle is
   SMuzzleGenerique (stretched 3.5 to 5.5 along the barrel, random roll) and the casing, without sMuzzleCanon; its
   barrel smoke sits at (-13, 0, 0.5) on the FX bone.
3. **Shotgun trigger.** The rifle's (`Fire`, `s_Firing`, auto-reload in `s_Selected` when the clip runs dry outside
   single shot) with `SwitchROF` empty, so it stays in ROF_Auto: held fire shoots every 1.15 s until the clip is
   empty. `FireWeapon` is the rifle's, then `ChangeEnumAction(SA_FIRE)` (the third-person pump, ReloStSpFd3).
   `CreateSmoke2` is empty (no barrel smoke after a pull).
4. **Shotgun damage.** `ServerFireWeapon` is the rifle's; its `TraceFire` is the native one: nine pellets, the
   largest per actor, no tracer, no stats beyond ShotsFired (natives2-damage.md rule 7). It stores the near and far
   hit ranges in the third-person gun (`MinProjDistance`, `MaxProjDistance`, replicated).
5. **Shotgun reload.** `s_Reloading.BeginState`: with no ammo or a full clip, back to s_Selected (PreviousState
   s_Selected). Else `iNbAmmoToReload = min(ClipMaxAmmo - ClipAmmo, Ammo)`; the rifle's reload start with the third-person
   action SA_RELOAD_1..5 for 1..5 shells and SA_RELOAD for 6. `s_FullReloading.BeginState` (ammo crate) the same
   without the early return, the 2 s message, and it leaves the low-ammo reticle flash alone. The reload clip plays a
   notify per shell; `EventReloadFirstN` with `iNbAmmoToReload == N` jumps the clip to frame 39 (EndOfReloadFrame), so
   a reload of n shells lasts until notify n plus 6 frames (0.4 s); six shells play the whole clip (3 s). The clip
   end fills the clip as the rifle does (`min(Ammo, ClipMaxAmmo)`).
6. **Shotgun pellet marks.** `SpawnSFX`: SMuzzleGenerique at (22, 0, -1) and SShotGunEmitter at (13, 0, 0) on FX, the
   casing; on a machine without authority also `SpawnImpacts`: trace from `GetFireStart()` along the FX bone's X for
   ShootingRange, first straight, then offset by -0.194 x (first range) along Y; both must hit; the ranges are the
   distances from 50 behind FX (the pair widened by 20 each way). Then 8 SShotGunProjector at
   `orig + X max(0, near - 20) + 7 Z - 7 Y + Y (FRand - 0.5) 0.194 near + Z (FRand - 0.5) 0.194 near`, roll
   `Rand(65536)`, MaxTraceDistance `40 + far - near`, DrawScale `near x 0.00176`, attached, abandoned for 10 s and
   destroyed. Each projector plays SW_Impact_Shot (volume 0.8, radius 100) with chance 0.3.
7. **Third-person shotgun** (`SWeaponDefenseDummy_Shotgun.Tick`): the rifle dummy's attachment, then: nothing while
   `MinProjDistance` equals the last one seen; else one muzzle count: `ClientFireMuzzle` (the rifle dummy's effects
   plus SShotGunEmitter) where the shooter has no first-person gun or it is someone else's shot on the server (not a
   dedicated server), and `SpawnImpacts` (rule 6's marks from the replicated ranges, from the first-person FX bone
   when this machine has the gun, else the dummy's) on the server and on every machine but the shooter's.

## States
The rifle's; the shotgun redefines `s_Reloading` (extends the rifle's) and `s_FullReloading`, BeginState only.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Uzi / shotgun clip frames | 3, 5, 31 / 16, 46 | frames at 15 fps | decoded | SWeaponFirst.ukx |
| Shell notifies | 0.788, 1.110, 1.441, 1.919, 2.279 | s | decoded | FirstReloStFd2S AnimNotify_Script |
| End of reload frame | 39 | frame | decoded | EndOfReloadFrame |
| Pump notify | 0.533 | s | decoded | FirstShooStFd2S AnimNotify_Sound |
| Pellet marks | 8, 50, 20, 7, 0.194, 0.00176, 40, 10 s | | decoded | SpawnImpacts |
| Mark sound | 0.3 chance, volume 0.8, radius 100 | | decoded | SShotGunProjector.PostBeginPlay |

## Network
- The weapon's trigger, clip, reload and view-model clips run on the owner; `ServerFireWeapon` (reliable owner to
  server) runs the trace and the damage on the server, as for the rifle.
- `ChangeEnumAction(SA_FIRE / SA_RELOAD_n)` carries the pump and the reload to the other machines' bodies.
- The shotgun dummy's `MuzzleCount`, `MinProjDistance` and `MaxProjDistance` replicate; the owner's client draws its
  own marks at once (rule 6), everyone else from the dummy (rule 7).
- The respawn choice: `ServerCycleRespawnWeapon` (client to server, reliable), `ClientSetRespawnWeapon` (server to
  client, reliable); the spawn itself is the server's.

## Edge cases
- Two shotgun shots in a row whose nearest hit is at the same whole distance (standing still at a wall) show no
  third-person muzzle and no marks on the other machines for the second one (rule 7's early return).
- A shotgun shot that hits nothing leaves the near range at ShootingRange (2500).
- A reload with fewer shells in the reserve than the clip's room plays the reload for the room capped at Ammo (which
  counts the clip too), then fills to `min(Ammo, ClipMaxAmmo)`.

## Evidence
- local/re-out/uc/SBase/SWeaponDefense_Uzi.uc, SWeaponDefense_Shotgun.uc, SWeaponDefenseDummy_Shotgun.uc,
  SWeaponDefenseDummy_Uzi.uc, SShotGunProjector.uc, SShotGunEmitter.uc, SPlayerController.uc 2174-2262,
  SHud.uc 958-1003, SPlayerProfile.uc IsWeaponInMercTeam; GUI/Edit_Profile.uc LoadProfile, WeaponClick.
- SWeaponFirst.ukx clips and notifies as converted (`.content/shared/chars/SWeaponFirst/Default.anim.json`).
- Native trace: ASWeaponDefense_Shotgun::execTraceFire editor 0x111d69c0 (local decompile natives2), game 0x10ab9d00.

## Open questions
- The native's `GetFireStart` (editor 0x111d66e0) is taken as the rifle's (FX bone pulled back 50 with the laser on,
  else the eye); whether the native pulls back is not checked.
- The aim point's EyeHeight and mover offsets are taken along world Z (the decompile multiplies a constant vector).
- The pellet momentum is taken from the aim point (the decompile subtracts the aim point, rule 7 says the start).
