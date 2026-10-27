# Alarm snare (SGadNoiseEmitter, SNoiseEmitter)

Units:
- nat:SBase.SGadNoiseEmitter.UseEmitter, nat:SBase.SGadNoiseEmitter.SpawnNoiseEmitter
- uc:SBase.SGadNoiseEmitter:Use, uc:SBase.SGadNoiseEmitter:LocalSystemMessage
- rep:SBase.SGadNoiseEmitter.SpawnNoiseEmitter, rep:SBase.SGadNoiseEmitter.LocalSystemMessage
- uc:SBase.SNoiseEmitter:TakeDamage, uc:SBase.SNoiseEmitter:PreBeginPlay, uc:SBase.SNoiseEmitter:PostBeginPlay,
  uc:SBase.SNoiseEmitter:FakeAlarms, uc:SBase.SNoiseEmitter:Tick, uc:SBase.SNoiseEmitter:Timer,
  uc:SBase.SNoiseEmitter:Destroyed
- def:SBase.SGadNoiseEmitter, def:SBase.SNoiseEmitter

## Purpose
The spy's "Alarm Snare (5 Units Pack)": a small device shot from the Shocker onto a wall. For its life it makes fake
footstep and breathing noises (the merc's sound arrows) and, near a camera, laser or volumetric sensor, sets off that
device's alarm as if a spy were seen.

## Data
`SGadNoiseEmitter`: `NoiseEmitterClass` SNoiseEmitter, `MaxLaunchDist` 10000, `Duration` 60, `SoundEffect`
Gadget_envoi, `RefireRate` 1, `Charge` 5. `SNoiseEmitter`: `ActionSounds[0..2]` RA_breath (radius 1000),
`WalkingSounds[0..2]` SW_FootRun, `fNoiseCoeff` 1, `fMaxDistToJam` 200, `iHackFakeDuration` 30, `LifeSpan` 60,
`Owner` (an SPlayerController field of its own, not replicated).

## Rules
1. **Use** (owner's machine): UseEmitter, the gadget rumble, then the SGadget Use (empty).
2. **UseEmitter** (native 1304, game 0x10aa1240): nothing unless the Shocker is drawn (`WeaponLoaded`) and
   `now - LastUsed >= RefireRate`. With `Charge < 1` the gadget plays `SND_EmptyCharge` (slot Misc, radius 100) and
   stops (no tutorial exception). Without a weapon nothing happens.
   - Aim: the weapon's `FX` bone (name 0x189); start = origin - 10 X, end = start + MaxLaunchDist X.
   - A multi line from end to start (source the pawn, flags 0xc10bf, the sticky cam's stick flags). Hits in order:
     a world-geometry actor that is not a gameplay object, or a gameplay object whose `AcceptStickableGPO` is true,
     takes the snare. A laser projector (SLaserProjector) is passed through; anything else ends the search with
     nothing placed.
   - On the taking hit: `DecCharge(1)`, `LastUsed = now`, the normal turned to face back along -X, and
     `SpawnNoiseEmitter(NoiseEmitterClass, pawn, hit location, normal rotation, hit actor)`. No pull-back from the hit.
3. **SpawnNoiseEmitter** (native 1305, game 0x10aa1570; a reliable client-to-server event): spawns the class at the
   pose; `LifeSpan = Duration`; the emitter's `Owner` = the pawn's controller. On a gameplay object it rides the
   object (`SetBase`), and a world-geometry object also lists it first in `Attached`. Every player hears
   `SoundEffect` at the spy (volume 1, radius 100). Then `LocalSystemMessage`.
4. **LocalSystemMessage** (server to owner): `AddMessageOnHud(5)` (the noise emitter "on" text).
5. **PostBeginPlay** (every machine): off a dedicated server `SetTimer(0.5)` and `fSpeed = 50`; on the authority
   `iStartedFakeTime = int(now)` and FakeAlarms. The gameplay object's own PostBeginPlay is not called.
6. **FakeAlarms** (server): every actor within `fMaxDistToJam` (200) that is a laser trigger with a laser, a camera or a
   volumetric sensor, and whose point 5 uu along its own X axis is in clear sight (a world trace) of the snare: the
   first SAlarm tagged with that actor's `Event` gets `SimulateActivated(actor, the first non-merc pawn or None)`, and
   `iTriggeredAlarmDuration` takes that alarm's `Duration`.
7. **Tick** (every machine): when the local player is not a spy, `fVal += FRand() x 0.48`, past 1 it restarts at
   `FRand() x 0.16`, and the local controller gets `ClientNotifyNoise(self, fVal)`.
8. **Timer** (rule 5's timer, then repeating):
   - Off a dedicated server: one time in five `NotifyNoise(fNoiseCoeff)`, a breath sound (`ActionSounds[int(FRand()
     x 2.9)]`, slot Misc, its radius) and `fSpeed = 10 + 160 FRand()`; otherwise `NotifyNoise(fNoiseCoeff x fSpeed /
     200)` and the footstep `WalkingSounds[int(fSpeed / 67)]` (radius 1000). Then `SetTimer(40 / fSpeed, true)`.
   - On the authority: when `now - iLastFakeUpdateTime > iTriggeredAlarmDuration + Rand(5)` and
     `now - iStartedFakeTime < iHackFakeDuration` (30 s), `iLastFakeUpdateTime = int(now)` and FakeAlarms again.
     A dedicated server never starts the timer, so there only rule 5's first FakeAlarms runs.
9. **TakeDamage**: any damage destroys the snare.
10. **Destroyed**: when the emitter's `Owner` has a HUD on this machine, `AddMessageOnHud(6)` (the "off" text).

## States
`Wait_BeforeSelfDestruction` (its Timer destroys the authority's copy) is declared but nothing enters it.

## Constants
Rule 2's pull-back 10 and empty-charge radius 100 (UseEmitter); the spawn sound radius 100 (SpawnNoiseEmitter); the
rest are the class defaults and the script literals above.

## Network
- Use and UseEmitter run on the owner's machine; `DecCharge` there also sends `ServerDecCharge`.
- `SpawnNoiseEmitter` goes to the server; `LocalSystemMessage` comes back to the owner.
- The emitter is a simulated proxy (SBasicGamePlayObject): each machine runs its own Tick and Timer, so each player's
  HUD gets its own noise arrows and sounds. Its `Owner` field is not replicated, so only the server's copy can show
  the "off" text, and only for a player on the server's machine.

## Evidence
- `local/re-out/uc/SBase/SGadNoiseEmitter.uc`, `local/re-out/uc/SBase/SNoiseEmitter.uc`.
- Game natives 0x10aa1240 (UseEmitter), 0x10aa1570 (SpawnNoiseEmitter); 0x10aa2dc0 AcceptStickableGPO,
  0x10aa3000 DecCharge, 0x10aa2d50 the SpawnNoiseEmitter event call (private decompile under local/re-out/c/noise).
- The `Owner` write at emitter + 0x370 matches SNoiseEmitter.Owner (property offset 880).
