# R4 spike: combat ground truth with a two-pawn rig

**Result: done.** A map script compiled into a sandbox copy of SteeE spawns a second player (the target) and drives
the local player's weapon, so every combat case the specs asked to measure could be played in the original and
sampled frame by frame. 32 two-player scenarios were recorded twice each (64 runs, one Play Level game per local
team) and one single-player fall three times. Every damage number, dispersion value, shock deadline, blast speed and
fall damage that could be checked against a spec formula matched it exactly; the measurements also settle several
open questions and correct four spec values (see "Spec corrections").

Recorded 2026-10-23 from Enhanced SCCT Versus v4.0 Public Beta 3 in `F:\SCCT-Automation\svm\Sandbox` (the binaries
of R4-difftest.md), frame cap 60. Traces and tables are game-derived and stay in `local/difftest/golden-combat/`
(`RESULTS.md`, `results.json`, `<id>/run<N>.jsonl.gz`, `<id>/measure.json`).

## What was built

| Path | What |
|---|---|
| `re/tools/difftest/rig/SvmCombatRig.uc`, `SvmRigController.uc` | The map script: command channel, target player, aim, fire, grenades, the target's own shots |
| `re/tools/difftest/combat_rig.py` | Builds the rig map (compile, place, save, install) and drives the placed rig in a game |
| `re/tools/difftest/combat_live.py` | Stable per-frame samples of both players (pawn, controller, weapon, HUD, damage history, animation channels), the grenade and the bullet impacts |
| `re/tools/difftest/combat.py` | CLI: `build`, `check`, `record`, `measure`, `show` |
| `re/tools/difftest/combat_scenario.py` | `svm.combat/1` loader and validator |
| `re/tools/difftest/combat_measure.py`, `combat_results.py` | Measurements against the spec formulas; `RESULTS.md` |
| `re/tools/difftest/combat_suite.py` | Generates the scenario set (`--check` in CI-style use) |
| `re/tools/difftest/combat_test.py` | 15 tests on synthetic data (format, formulas, extraction) |
| `tests/scenarios/combat/` | 32 `*.combat.json`, `merc-fall-ledge.scenario.json` (svm.scenario/1), README, schema |

## Method

**Map.** The project sandbox's RE+ plugin has no `script.compile`, so the map is made in a private editor-only copy
(`F:\SCCT-Automation\svm-combat\Sandbox`, a mirror of the project sandbox with the routecheck sandbox's plugin build
e80a5c68, which has the command): SteeE is opened from MapsEd, the two classes are compiled into MyLevel, one rig
actor is placed, and the map is saved as `SteeECombat.sdc` and copied into the project sandbox's MapsEd. SteeE itself
is never written; no game runs from the copy. `record` rebuilds the map when the rig sources change.

**Target player.** Made as the game makes a player: an `SvmRigController` (an SPlayerController subclass with no
overrides) is spawned, named first (Reloaded walks GRI.PRIArray), given the GRI, a team (`ChangeTeam`), its team's
pawn class and an SHud (`ClientSetHUD`, as PostLogin does; with the plain HUD a controller makes for itself the native
`ClientDamageHudEffect` crashed the game at the first hit), then `RestartPlayer`. Damage therefore runs the real
pipeline on an SPlayerController. Its RemoteRole is set to None before RestartPlayer: as AutonomousProxy the engine
treats it as a remote client's player (no pawn physics, `ChangeEnumAction` returns, no animation). With None it runs
as a host's own player does, except PlayerTick, which needs a viewport (calling it from script crashed the game).

**Driving.** Python writes arguments, a command and a sequence number into the rig's fields; the rig runs one
command per Tick. Fire is what the key alias does (`Button bFire | Function Fire`): the rig sets `bFire` and calls the
exec `Fire` once. Aim writes MyDesiredRotation, its local copy and Rotation from the local eye to a target bone (or
the middle of a limb capsule's two bones). The target's own shots go through the server RPCs its client would send
(`ServerFireWeaponWithLocation` for the shocker). Local input bytes (Use, crouch, fire mode, reload, walk) are written
in memory as in R4. No key is sent, no ini is changed, Physics is never written.

**Sampling.** livegame.py's stable sample: 2 ms after TimeSeconds moves, both players, the rig and the level are read
twice. In the 64 kept runs: 7 runs with a torn sample (at most 2), 2 runs with a late write (1), none unstable,
59.9 to 60.3 fps.

## Measured vs spec

### Merc rifle damage per hit zone (spy target standing, single shots, 2 runs)

The distance is eye to impact (the tracer's ImpactLoc). Expected: `int(int(17 c(d)) table[pill] / 100)` in float32.
49 of 49 hits with an impact matched; the rest of the 68 hits had no tracer (auto mode) and are not checked.

| Zone (pill) | ~240 uu | ~890 uu | ~1490 uu | Spec % |
|---|---|---|---|---|
| head (1) | 75 | 54 | (missed: 0.25 spread at 1500) | 300 |
| torso L/R (2, 9) | 25 | 18 | 12 | 100 |
| upper arm (3, 10) | 10 | 7 | 4 | 40 |
| forearm (4) | 10 | 7 | - | 40 |
| hand (5) | 5 | - | - | 20 |
| thigh (6, 13), calf (7) | 25 | 18 (17 at 934) | 12 | 100 |

Aimed at the foot, shots hit the calf (pill 7); no foot hit was recorded.

### Rifle fire modes, spread, recoil, reload

| Quantity | Spec | Measured |
|---|---|---|
| DispersionActuelle per shot | `min(V/600 + 0.15 + 0.1 BulletInARow, 0.7)` | 236 of 236 shots exact (standing 0.25, 0.35 ... 0.7; walking 346.5 uu/s 0.7; crouch-walking 116.9 uu/s 0.4448, 0.5449, 0.6449, 0.7) |
| Spread square | impact at `A (u - 0.5) 1000` on Y and Z at 15000 uu, u in 0..1 | 150 of 151 impacts in 0..1 (max 0.998); one walking shot at -0.063 (eye bob and the frame's motion are not modelled) |
| Auto cadence | 0.2 s | 108 intervals of 12 frames, one of 11 and one of 13 (engine timer catch-up) |
| Burst | shots at 0, 0.2, 0.4 s, back to s_Selected at 0.6 s | exactly that, 4 bursts x 2 runs |
| Single (0.25 s presses) | BulletInARow back to 0 between | every shot at BulletInARow 1, 0.25 |
| BulletInARow decay | one RateOfFire after the last shot, then -1 per 0.02 s | first -1 at 0.233 s, then -1 every 2 frames (0.033 s: once per tick at most, and 0.02 s is longer than a 60 fps tick) |
| Aim kick (MyDesiredRotation, auto shots 2+) | pitch +66..+230, yaw within +-296 | pitch +68..+230, yaw -258..+277; camera yaw kick up to 413 (spec +-449) |
| Kick decay | not in the spec | the kick shows in MyDesiredRotation for one frame, then the controller copies LocalMyDesiredRotation over it: that holds about 1/8 to 1/2 of the kick, falls to a third two frames later and to a tenth by the fourth, leaving 2 to 8 units per shot: the aim of shot 25 had drifted about +100 pitch units |
| First shot of a press | kicked | no kick (see Limits) |
| Reload (button, 20 rounds in the clip) | clip FirstReloStFd2 31 / 15 = 2.067 s | 2.014 s from s_Reloading to the refill (= (31 - 1) / 15 plus one frame: AnimEnd fires at the clip's last frame); then s_Selected |
| Auto reload, fire held | after the clip empties, fire resumes | 2.031 s, then s_Firing at once (clip 25 then 24 in the same frame) |
| Reload with a full clip | refused | nothing happens |

### Shocks

| Quantity | Spec | Measured |
|---|---|---|
| Merc freeze / electric (local merc shot by the target spy) | until the first tick after `int(t + 3.5)` / `int(t + 4.5)` | deadlines equal in 6 of 6 accepted shocks; freeze 2.61 to 3.45 s, electric 3.61 to 4.44 s (the whole-second truncation) |
| Spy freeze = electric (local spy) | `int(t + 3.0)` | 3 of 3; 2.10 to 2.95 s |
| Immunity | merc 12.5 s, spy 11.0 s | RealTimeInvulSticky 12.5 / 11.0; shocks inside the window record a 0 SDTStickyShocker entry and nothing else; the window carries over to the next scenario |
| Fire while frozen or electric (merc) | refused for the whole electric time | refused 1.5 s after the shock; fired at 4.0 s, after that shock's electric time had ended at 3.61 s; in the first recording, refused at 4.0 s with 4.17 s of electric time |
| Merc weapon | s_Dmg_Electric, back to s_Selected at the end of the freeze | yes |
| Head shock (pill 1) | bStickyShockedInTheHead | set |
| Shocker energy | 15 per shot, +7 per whole second | 15 (18 shots), +7 at 0.999 to 1.004 s spacing; 8 shots 0.5 s apart from a full bar (refills between) |
| Energy cap | open question | not clamped: 99 + 7 gave 106 |
| Shocker hits on a merc 500 uu away | aim from the hand bone | aimed at spine2: pill 3 then pill 2; aimed at the head: pill 2 or a miss |

### Grenades (rig-spawned SProjFrag, its own 2 s fuse)

| Quantity | Spec (natives2-damage.md rule 4, pipeline rule 21) | Measured |
|---|---|---|
| Fuse | 2 s | 2.0 s (the rig saw it gone one frame later: 2.014) |
| Spy at 132, 218, 312 uu; merc at 218 | SDamage_Explosion, 200 | 200 each, approximate pill 5 |
| Merc at 429 uu | SDTBase `int((1 - d / 500) 334)` = 47 | 47 |
| Spy at 458 uu | 27 | 27 |
| Spy at 600 uu | nothing | nothing |
| Distance | victim centre to grenade, 3D | yes (the grenade rests on the floor) |
| Blast speed | `800 - 450 d / 1000` along `Normal(dir, Z = 1)` | 701.85 at 218, 740.35 at 133, 659.44 at 312, 728.92 at 158: exact, both components |
| Blast states | s_Blast, s_BlastFloor, s_BlastWakeUp | s_Blast 0.65 to 0.8 s, s_BlastFloor 1.83 to 1.91 s; a lethal blast (health 100) goes s_Blast then s_Dead 0.7 s later |
| Thrower | not excluded (open question) | the merc's own frag at 218 uu: 200 and the blast |
| Spawn protection | spy ignores explosions for 3 s after RestartPlayer | frag at 2.58 s after spawn: no damage and no history entry; at 6.0 s: 200 |
| Bullets on a fresh spy | not protected (story) | 22 at 1.7 s |

Smoke (spawned at the local merc's feet, fuse 3 s): cough from the pop; s_FallDown exactly 6.0 s (plus one frame)
after the cough started, s_LyingOnGround 5.0 s later.

### Grab, melee, knock-outs

| Case | Measured |
|---|---|
| Grab from 90 uu behind | Use, s_Grabbing_GotoPosition 0.15 to 0.27 s, s_GrabbingPlayer_Begin one frame, s_GrabbingPlayer_RealBegin 1.016 s (the catch clip), s_GrabbingPlayer; merc s_GrabbedByPlayer_Begin, s_GrabbedByPlayer, weapon s_IdleGrabbed |
| Neck break (Use) | merc health 0 in the frame of the press, s_GrabbedByPlayer_Released_STRANGLE; no damage history entry (Killed directly); spy s_ReleaseGrabbedPlayer, walking 1.55 s later |
| Knock-out (Fire) | merc 100 to 49 (51), s_GrabbedByPlayer_Released_KO 3.0 s, s_LyingOnGround 7.0 s, s_StandUp 3.35 s with the wake-up shock (weapon s_Dmg_Electric, input frozen), then walking |
| Choke (held) | s_ReleaseGrabbedPlayer_Strangle 15.0 s after s_GrabbingPlayer began; merc health 0 |
| Merc berserk (still, spy 100 uu ahead) | s_Berserk360 1.46 s; hit 0.47 s in: 35 SDamage_Charged, spy s_Charge_GoBackward 1.95 s pushed 278.7 uu (StrkCrAlBk0), s_LyingOnGround 3.0 s, s_StandUp |
| Merc charge (walking, Use) | hit 0.35 s in, the same 35 and push; merc s_PlayerCharging_HitEnemy, walking 1.16 s after the hit |
| Merc elbow (inside the berserk cooldown) | s_ElbowShooting 1.15 s; input frozen for exactly one frame (the PlayerTick quirk of melee rule 9); spy 0 SDamage_ElbowShock, s_ElbowShot_GoBackward 1.37 s, pushed 223.1 uu (StrkStAlBk2) |
| Spy elbow (merc facing him at 110 uu) | the same timings and the same 223.1 uu push |

### Death direction (lethal rifle shot into a standing spy)

| Merc is on the spy's | Death clip |
|---|---|
| front | XxxxStAlBk0 |
| back | XxxxStAlFd0 |
| right | XxxxStAlLt0 |
| left | XxxxStAlRt0 |

The same in both runs (the pawn's action afterwards reads 269). The clip falls away from the shooter.

### Fall damage on a merc (svm.scenario/1, record.py, 3 runs)

At walking speed the merc lands on a step 18 uu above the yard, so the drop is 772.6 uu, not the 790 of the spy
scenario. Damage 58, 58 and 59 against `int((h - 450) / 550 * 100)`: all three match when h is taken at the lowest
of the last air frame and the landing frame (in the 59 run the pawn was 2.3 uu below the step top before it was lifted
onto it). The rewrite's sim (`tests/OpenScct.DiffTest`, PawnRigSim) passes this golden in DtReplay and Fixed timing.

### Spy regeneration (seen in passing)

A spy hit twice for 25 (rifle, 70 % refillable) regenerated +4 per whole second starting 3.0 s after the last hit,
from 50 to 84: the pool `int(25 x 70 / 100)` x 2 = 34 (health.md rule 3).

## Spec corrections

- Reload time: (31 - 1) / 15 = 2.0 s plus up to one frame, not 2.067 s (equipment/merc-rifle.md constants).
- BulletInARow decay: one step per tick at most, so 1 per 2 frames at 60 fps (merc-rifle.md rule 12 reads as one per
  0.02 s).
- Energy refill is not clamped at 100 (spy-energy.md open question).
- Death clips are `XxxxStAl{Bk,Fd,Lt,Rt}0` (front, back, right, left of the shooter); damage-pipeline.md rule 45
  names `ADeath...`. ChooseDeathAnimation open question: answered by the table above for a standing spy.
- Fall height is taken at the landing before a step-up (blast-and-fall-damage.md rule 15).
- Knockback distances (melee open question): elbow 223.1 uu, charge and berserk 278.7 uu.
- Self damage by the merc's own frag (frag-grenade.md open question): yes.
- Merc wake-up shock (shock-and-taser.md open question): applied at s_StandUp after a grab knock-out.

## Deviations and limits

- The target has no PlayerTick: its freeze and electric times never end, landing states would not end (the rig calls
  GotoWaitingState once it lands), and owner-side reactions of the target (cough, owner timers) do not run. The freeze
  and everything a player lives through were therefore recorded with the local player as the victim and the target
  shooting (server RPC); on the target only the server-side parts (damage, deadlines, states) are measured.
- Placing a walking pawn on the floor made it sink through (36 uu a frame, then a fall): the rig drops placed targets
  from 80 uu and resets their fZMaxFalling (a target moved while falling from its start took fall damage).
- A spy target stands up through the crouch actions and the transition state ProcessCrouch starts (ProcessCrouch
  itself did nothing for it).
- In a headless game the local SHud's perturbation never stops once started (its PostRender does not run), and
  Reload refuses while it is set: scenarios that blast or shock the local player run last in their game (`order`).
- The first shot of every press carries no aim kick: the rig calls the exec Fire in its own Tick, before the
  controller's tick copies LocalMyDesiredRotation over MyDesiredRotation. A key press is processed outside the level
  tick as well, but whether the original keeps that kick was not verified with a real key (open issue).
- The spy's shocker aims from the hand bone, so its hit pills follow the pose rather than the aimed bone.
- Recording runs of a team share one game (default; `--game-per-run` splits them): shock immunity carries over between
  scenarios of one game.
- Not measured: drop attacks, the taser gadget, counter-grab mashing, spawn shock (the start the target spawns at is
  chosen by FindPlayerStart), the merc scope and laser, Uzi and shotgun, merc versus merc (Merc Deathmatch only).

## Commands

```
python -I -B re/tools/difftest/combat_suite.py          # write tests/scenarios/combat (or --check)
python -I -B re/tools/difftest/combat.py check
python -I -B re/tools/difftest/combat.py record --runs 2 [--only id,id] [--build]
python -I -B re/tools/difftest/combat.py measure        # local/difftest/golden-combat/RESULTS.md
python -I -B re/tools/difftest/record.py record tests/scenarios/combat/merc-fall-ledge.scenario.json --runs 3 \
    --out local/difftest/golden-combat
python -I -B -m pytest re/tools/difftest/combat_test.py -q
```

The two games of a full recording hold the play lock for about 10 and 6 minutes.

## Next

- A two-player sim runner for `svm.combat/1` (the target as a second PawnRig, the rig commands as sim calls) and
  gates from `measure.json`: damage and dispersion exact, deadlines exact, blast speed to 0.01 uu/s.
- Fire through a real key binding once a sandbox-only input ini is allowed, to settle the first-shot kick.
- Scenarios for the remaining open questions: drop attacks (needs a jump onto the target), taser, counter-grab.
