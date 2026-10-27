# SteeE device chains (worked examples, crane timeline)

Units: none of its own. This file composes the units of the other files in this folder (see `INDEX.md`); the
switches that start the crane and shutter chains (`SInterrupteur`, `STriggerInteraction`) are specified with the
interaction system.

This file composes the device specs into the event chains SteeE actually runs, with the authored values, so a port
can be checked against the original end to end. Times are seconds; positions are uu (X, Y, Z).

## Purpose

Fix the exact order, timing and credit of every SteeE event chain, above all the crane chain that opens the disk
route.

## Data

### Event graph (sender, name, receivers in dispatch order)

| Sender | Name | Receivers (actor-list order) |
|---|---|---|
| SInterrupteur28808 "Crane Control" (spies, 5 s hack, once) | `blast` | SAnimatedMesh277 (crane), SoundTrigger396, SoundTrigger359 |
| crane notify AnimNotify_Trigger236 | `tueletube` | Mover2064 (invisible crushing duct) |
| crane notify AnimNotify_Trigger281 | `blast01` | SGamePlayObject261 (wall), Emitter483, Emitter287, SGamePlayObject3357 (crane collision), Emitter2155, SAnimatedMesh451 (wall collapse), Emitter305, Emitter349, SShaker37372, Emitter511, SGamePlayObject346, SGamePlayObject2889 (hidden explosives), MusicTrigger263, SGETrigger271 |
| SInterrupteur28810 (spies, 4 s) | `windows` | 10 shutter movers |
| SInterrupteur28809 (spies, 4 s) | `close_windows` | 3 shutter movers |
| SInterrupteur28807 (spies, 4 s) | `fermeture_fenetre` | 7 shutter movers |
| SPresenceTrigger1742 (mercs, grab), 8696 (all) | `PorteMerc` | Mover1079 |
| SPresenceTrigger8690 (all) | `Mercporte2` | Mover315 |
| SPresenceTrigger5814 (all) | `portemerc3` | Mover1750 |
| extinguisher SGamePlayObject386 (stage) | `doorblast2` | SGamePlayObject2843 (door, by DestroyOnEventFrom), SAmbientSoundTrigger283 |
| door SGamePlayObject2843 (stage) | `PORTE1` | Emitter262 |
| extinguisher SGamePlayObject1787 (stage) | `doorblast3` | SGamePlayObject266 (door) |
| door SGamePlayObject266 (stage) | `PORTE2` | Emitter451 |
| molotov SGamePlayObject215 (stage) | `molotov` | Emitter169, SMagicEvent232 |
| SMagicEvent232 (+1 s, +32 s) | `moloaie` | SDamageVolume228, SAmbientSoundTrigger273 |
| TV SGamePlayObject259 (stage) | `telespark` | STriggerLight295, SAmbientSoundTrigger285, Emitter263 |
| gas bombs 433, 368; soda packs 758, 1826, 736, 747 (stage) | `boomgaz2`, `boomgaz3`, `canette1`, `3`, `4`, `5` | one Emitter each |
| 14 SVitre panes (stage) | `baie_brisee*`, `vitre_brisee*`, `vitre_bas_brisee` | SAmbientSoundTriggers, SoundTrigger267 |
| Trigger436, 357, 433 / 506, 267 / 4176 / 4177 | `sirene` / `Chien` / `Mouche` / `Rats` | SoundTriggers |

Names received but never sent by any SteeE actor: `S2_general_light` (two lamps), `enable_shop_spawn`
(STriggerPlayerStart4614), `SDamageVolume`, `SPresenceTrigger`, and the static-mesh Tags. They are inert.
`hackvolet01` (the three shutter switches) is sent: it is the `AtResetEvent` of the Disk Hunt mission
(SMission28822) and the Deathmatch mission (SMission28823), so those modes trigger the shutter switches when the
mission resets; the Story mission (SMission28821) has no `AtResetEvent`.

The order above is the saved Actors array order (indices 154 to 1351 in SteeE's Level export), assumed to be the
run-time dynamic order (`events.md` Open question 3).

## Rules

### Crane chain timeline

T0 is the moment the crane switch's hack completes and it fires `blast` with the hacking spy as instigator (switch
behaviour, see Open questions). Offsets are from T0; each event happens on the first tick at or after its time.

| Time | Event | Effect |
|---|---|---|
| -5.0 | spy starts the 5 s hack at the switch (-264, 1078, -999) | switch spec: "Crane Control" warning to mercs, HUD marker |
| 0.000 | `blast` | crane SAnimatedMesh277 (at (-1189, 1374, 1648), DrawScale 10) stores the spy as `Instigator` and starts `sqt_grue_seq`; SoundTrigger396 (-1215, 1130, -264) and SoundTrigger359 (-2753, 621, -434) play `Seq_alarme` (volume 1.0, radius 300) |
| 0.0025 | notify | `Squ_Grue_Seq_Start` (volume 4.0, radius 200), heard on every machine |
| 1.925 | notify | `Seq_Grue_Loop` (volume 3.6) |
| 2.475 | notify | `Seq_Grue_Loop` (volume 2.8) |
| 7.174 | notify | `Squ_Grue_Seq_Stop` (volume 4.0) |
| 9.483 | notify | `Squ_Grue_Seq_Drop` (volume 6.0) |
| 10.270 | `tueletube` | Mover2064 (invisible, base (-3186, 442, 1580)) drops 3920 uu in 1.0 s along the wall line X -3186, crushing (kill, credited to the spy) anything its collision crosses |
| 10.962 | `blast01` | in dispatch order: wall SGamePlayObject261 mesh `sqt_murkc_avant` to `sqt_murkc_apres` (collision follows: the route through the wall opens); debris emitters 483 (y 825) and 287 (y 292); crane collision SGamePlayObject3357 `sqt_grue_coll01` to `coll02`; emitter 2155; wall collapse SAnimatedMesh451 at (-3223, 610, -1102) becomes visible and plays `sqt_mur_seq` with `MC_mur_destroy` (radius 300); emitters 305 and 349; SShaker37372: every player gets tilt 800/10000/2000 and shake 1000/10000/1000; emitter 511; hidden explosives SGamePlayObject346 (-3242, 848, -1062) and 2889 (-3247, 262, -1062) destroyed: each a 600 hit-point `SDamage_Charged` blast within 500 uu, an `SGrenadeExplosion` and `LaserMine_Explo`, credited to the spy; MusicTrigger263 starts `Squ_Mus_Stress`; SGETrigger271 clears GE mask bit 4 (mask 4 to 0): the wall-top ledge GE 153 at X -3186, Y 56 to 1088, Z -808 disappears |
| 11.250 | crane AnimEnd | crane holds its final pose, back in `s_Wait` |
| 11.270 | duct stops | Mover2064 at Z -2340, `FinishedOpening`, locked (`s_TemporaryInactive`) for the rest of the round |
| 14.295 | wall AnimEnd | collapse animation holds its last frame (rubble) |

Derivation: crane notify time `t * 451 / 40`, crane end `450 / 40`, wall end `100 / 30` (`animated-mesh.md`);
duct end `10.270 + 1.0` (MoveTime default 1.0). The plan's crane length 11.28 s is `451 / 40`; the end rule gives
11.25 s (to confirm, see Open questions).

### Crane chain rules

1. The chain runs once per round: the switch is once-only, the duct locks, the explosives stay destroyed (no reset
   time), the wall and crane collision stay swapped (no further `blast01`).
2. Credit: the spy's pawn travels as the crane's `Instigator`. The duct's `Trigger(crane, None)` takes it from
   `Other.Instigator` and crushes with it (`mover.md` rule 27); each hidden explosive's `Trigger(crane, None)` takes it
   likewise and wraps `ProcessDamage` with `GPODamageInstigator = spy` (`gameplay-object.md` rules 8 and 26). Mercs
   killed count as the spy's kills and environmental kills; spies killed are team kills; the spy's own death is a
   suicide.
3. The hidden explosives accept only `Crushed` damage, which only their `DestroyOnEventFrom` path supplies; players
   cannot set them off early.
4. The wall and crane collision objects are not damageable, so their Trigger is a mesh toggle (`gameplay-object.md`
   rule 7).
5. Everything above runs on the server; clients get the swapped meshes, the visible wall animation, the music (see
   `sound-music.md` Open question 1), the shake RPCs, the mask, the explosion actors and the crane animation (and its
   sounds) through replication.

### Shutters

6. Each of the three switches (spies only, 4 s hack, reusable) fires its name; each named shutter (TriggerOpenTimed,
   MoveTime 10, StayOpenTime 20, Return on encroach) slides 160 to 168 uu (or 156 sideways) over the window in 10 s
   with `Squ_Volets_START` and the `Squ_volets_loop` loop, holds 20 s, and slides back in 10 s (40 s in all).
7. A hack while the shutters move does nothing to them; a hack while they hold restarts a 10 s no-motion leg and a
   new 20 s hold. A player in the shutter's path sends it back to where it came from, where it stays idle until the
   next hack (`mover.md` rule 25).

### Merc doors

8. A pawn entering the presence cylinder (22 x 22) next to a door fires it: TriggerOpenTimed, slide 142 or 143 uu in
   1 s with `Squ_Door_Open`, hold 4 s, close in 1 s with `Squ_Door_Close`. Blocked while closing: reopen, wait 1 s,
   retry. SPresenceTrigger1742 answers mercs and spies carrying a grabbed merc; the others answer every pawn.

### Breakable doors

9. Shooting extinguisher SGamePlayObject386 (HP 1) destroys it: 600 `SDamage_Charged` within 400 uu, a grenade
   explosion, its broken mesh, and its stage event `doorblast2`, which destroys door SGamePlayObject2843 (it names 386
   in `DestroyOnEventFrom`) and turns on the street ambience SAmbientSoundTrigger283; the door's own stage event
   `PORTE1` bursts Emitter262. Extinguisher 1787 does the same for door 266 (`doorblast3`, `PORTE2`, Emitter451).
   The doors cannot be broken any other way. Credit flows from the shooter through `Other.Instigator`.

### Molotov fire

10. Shooting the molotov bottle SGamePlayObject215 (10 HP; bullets, explosions, sticky shots) destroys it: 50
    `SDTBase_NoAnim` within 200 uu, `Squ_Explo_Molotov`, stage event `molotov`: Emitter169 bursts, SMagicEvent232
    queues `moloaie` at +1.0 s and +32.0 s. At +1.0 s SDamageVolume228 turns on (30 per second, every SBasePawn
    inside) and the fire loop starts; at +32.0 s both toggle off.

### Other

11. Glass: any hit breaks a pane (`vitre.md`); 13 panes then start an outside-ambience loop.
12. TV SGamePlayObject259 (5 HP): breaks to its `_1kc` mesh, toggles its light, stops its buzz, sparks.
13. Lamps (7): one hit puts them dark (skin swap, lights off) for 60 s, then they relight.
14. Ambient Triggers replay their sounds on every player entry (no delay).

## States

See the device specs; the chains add no state.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| crane hack time | 5 | s | decoded | SInterrupteur28808 `iHackingTime` |
| shutter hack time | 4 | s | decoded | SInterrupteur28807, 28809, 28810 |
| `tueletube` time | 10.270 | s after `blast` | decoded | sqt13_anm.ukx notify, 451 frames at 40 |
| `blast01` time | 10.962 | s after `blast` | decoded | same |
| crane end | 11.25 | s | decoded rule, to confirm | `animated-mesh.md` rule 9 |
| duct drop | 3920 in 1.0 | uu, s | decoded | Mover2064 |
| hidden explosive blast | 600 in 500 | hp, uu | decoded | SGamePlayObject346, 2889 |
| GE mask after blast | 0 | | decoded | SGETrigger271 on mask 4 |

## Network

Server-authoritative throughout (`events.md` rule 7). Per-machine items: notify sounds, the crane and wall poses
(replicated animation), the flare attachments. All 16 event-triggered emitters in SteeE (crane debris, door dust,
molotov, cans, gas, TV sparks) set `bNetworkTrigger`, so their bursts reach clients through
`Emitter.ReplicateTrigger` (effects spec).

## Edge cases

- A player standing on the wall-top ledge when `blast01` fires loses the GE (traversal spec decides the fall); the
  duct falling 0.69 s earlier along the same line X -3186 has probably crushed them already.
- If the crane switch's event arrived with no instigator, the crane keeps `Instigator = None` and nobody gets credit
  for the duct and explosive kills.
- If several notifies fall in one long server tick, they fire in list order (`blast01` before `tueletube`), which
  would put the explosions before the duct starts; at normal tick rates the 0.69 s gap prevents this.

## Evidence

- SteeE.sdc exports named above (scctpkg read of all 2,813 exports; Level export Actors array for order).
- `Packages/Animations/sqt13_anm.ukx` notifies.
- Device specs in this folder; `docs/spikes/C3-ge-list.md` (GE 153).

## Open questions

1. The crane switch: confirm that `STriggerInteraction.HandleEventInfo` (native `0x10af6650`) fires `blast` with the
   hacking spy as instigator and `Other` = the switch, and that `bTriggerOnceOnly` disables it afterwards; and what
   the shutter switches send on reuse (Trigger each time, or alternating Trigger/UnTrigger by `TriggerMethode`).
   An UnTrigger to a holding shutter would freeze it shut (`mover.md` rule 29).
2. Trace the whole crane chain once in the original (server-side event log with TimeSeconds) to confirm order and
   the 10.270 / 10.962 / 11.25 / 11.27 s marks; also on a dedicated server.
3. The duct's collision footprint (SQU13_Aeration01 at DrawScale3D Z 2) against the wall top: run the collision
   oracle along X -3186 to see which standing spots it sweeps.
