# SSprinkler and SFlaque (sprinklers and puddles)

Units:
- def:SBase.SSprinkler
- uc:SBase.SSprinkler:PostBeginPlay
- uc:SBase.SSprinkler:SpawnWater
- uc:SBase.SSprinkler:StopWater
- uc:SBase.SSprinkler:TakeDamage
- uc:SBase.SSprinkler:Trigger
- uc:SBase.SSprinkler:UnTrigger
- uc:SBase.SSprinkler:Timer
- rep:SBase.SSprinkler.WaterEmitter
- def:SBase.SFlaque
- uc:SBase.SFlaque:Tick
- uc:SBase.SFlaque:Destroyed
- uc:SBase.SFlaque:Grow
- uc:SBase.SFlaque:Reduce
- rep:SBase.SFlaque.variation
- uc:SBase.SSprinklerEmitter:PreBeginPlay
- uc:SBase.SEffectWaterPipe:PostBeginPlay, uc:SBase.SEffectWaterPipe:Timer

## Purpose

Ceiling sprinklers that rain when an event fires (fire alarm), with a puddle growing under each. 101 on 5 maps:
AquaD, AquaE, AquaT (the objective event `Sprinkler` from SObjective420 starts 14 sprinklers and their
SAmbientSoundTriggers) and ShadD, ShadP (an SMagicEvent group fires `Sprinkler` on 30). PrisD places 5 static
puddles (DrawScale 5 or 10).

## Data

| Field | Default | Maps |
|---|---|---|
| `Duration` (s) | 20 | 10000 on all 101 (rains for the rest of the round) |
| `WetVolumeRadius` / `WetVolumeHeight` / `WetVolumeZOffset` | 50 / 100 / -150 | 80 to 125 / 140 to 175 / -147 to -175 |
| `bDamageable` | true | false on all (only events start them) |
| `bHidden`, no collision or traces | | all |
| SFlaque `variation` (transient, replicated) | 0 | growth rate |

## Rules

1. SSprinkler PostBeginPlay (after the parent's): a hidden sprinkler is unhidden and set to DT_None (so it stays
   relevant to clients but draws nothing).
2. Trigger, or TakeDamage when bDamageable: if no water emitter runs, SpawnWater.
3. SpawnWater: spawn SSprinklerEmitter at the head (owner the sprinkler); if no puddle yet, trace 500 uu down from
   (Location.Z + WetVolumeZOffset - WetVolumeHeight) and spawn an SFlaque 5 uu above the hit, then Grow; else Grow the
   existing one. Duration > 0 arms a one-shot timer.
4. UnTrigger and Timer: StopWater: Kill the emitter (it stops spawning and its particles die out), forget it, and
   Reduce the puddle.
5. SFlaque Grow: variation = 0.1; Reduce: variation = -0.01. Tick on every machine: while variation is not 0, X and Y
   scale change by variation x dt; the new scale is applied only while X < 1.5; the location is set again (so
   collision follows the new size); X < 0.1 destroys the puddle. Starting at 0.1 a puddle reaches 1.5 after 14 s and
   would take 140 s to shrink away.
6. SFlaque Destroyed clears its sprinkler's Flaque (no parent call).
7. SSprinklerEmitter PreBeginPlay (each machine, before the parent's): a trace from 50 to 500 uu below the emitter
   (actors included); on a hit its second sub-emitter (the mist on the floor) starts its particles at the hit's
   height: StartLocationRange.Z.Min = Max = hit Z - emitter Z (its X and Y spread of +-32 kept).
8. SEffectWaterPipe (the SImpactWaterPipe spark emitter of a shot water pipe), PostBeginPlay on each drawing machine:
   a trace from 30 uu ahead of it (its X axis) to 300 uu below that point (actors included); on a floor that is not
   SURFACE_MediumWater or SURFACE_DeepWater an SFlaque 3 uu above the hit, turned to the emitter's yaw (pitch and roll
   0), RemoteRole None, then Grow; a 4 s one-shot timer then Reduces it. It grows to 0.5 and takes 40 s to shrink away.
9. The WetVolume fields only place the puddle trace in script; any native wet effect is not known (open).

## Network

WaterEmitter replicates (Role == Authority) so clients show the spray; the emitter itself is a simulated proxy.
SFlaque variation replicates and each client grows its puddle itself.

## Constants

| Name | Value | Provenance |
|---|---|---|
| puddle trace length / lift | 500 / 5 uu | decoded (script) |
| grow / shrink rate | 0.1 / -0.01 scale per s | decoded |
| max applied scale / destroy below | 1.5 / 0.1 | decoded |

## Open questions

1. Emitter.Kill: the emitter actor goes at once in the simulation; the drawn water stops spawning and the live
   particles die out (estimated: the original keeps the actor until then).
2. Whether the native SSprinkler uses the wet volume (wet pawns, extinguished fire) is not decoded.
3. Spawned puddles have no collider in our collision world (the original has bWorldGeometry, bBlockPlayers).

## Evidence

- `local/re-out/uc/SBase/SSprinkler.uc`, `SFlaque.uc`, `SSprinklerEmitter.uc`, `SEffectWaterPipe.uc` (also spawns
  puddles).
- AquaD eventEdges (`Sprinkler` from SObjective420), ShadD (`Sprinkler` from SMagicEvent2752).
