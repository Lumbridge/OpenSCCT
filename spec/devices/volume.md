# Volume, BlockingVolume and player starts

Units:
- def:Engine.Volume
- uc:Engine.Volume:PostBeginPlay
- uc:Engine.Volume:AssociatedTouch.Touch
- uc:Engine.Volume:AssociatedTouch.UnTouch
- uc:Engine.Volume:AssociatedTouch.BeginState
- def:Engine.BlockingVolume

## Purpose

A Volume is a brush region that touches actors (bCollideActors, non-blocking). SCCT gives it no physics rule (no
PhysicsVolume). Maps use volumes as named areas for the location name, as the detection regions of
SVolumetricSensor (251 sensors on 33 maps list their volumes), and can forward touches to another actor.

## Data

| Field | Default | Meaning |
|---|---|---|
| `LocationName` | "unspecified" | area name (751 of 1131 map volumes set one) |
| `LocationPriority` | 0 | the highest wins among touched volumes (98 set 1 or 2) |
| `AssociatedActorTag` | None | forward touches to the first actor with this tag (no map sets it) |
| `AssociatedActor` | None | found at PostBeginPlay |
| `DecoList` | None | decoration lists (unused by SCCT script) |
| Defaults | | bCollideActors, bIsAVolume, bSkipActorPropertyReplication |

## Rules

1. PostBeginPlay: with an AssociatedActorTag, the first actor (any class) with that tag becomes AssociatedActor; with
   one, the volume enters `AssociatedTouch` and that becomes its InitialState.
2. AssociatedTouch: Touch and UnTouch are passed to AssociatedActor; entering the state replays Touch for every actor
   already touching.
3. Location name (PlayerReplicationInfo.UpdatePlayerLocation): the named volume with the highest LocationPriority
   among those the pawn touches that encompass it, else the zone's LocationName. Encompasses (native 1216, point
   inside the brush) is not ported: touching stands in.
4. A plain Volume's `Event` is never fired (CineE, CineS, CineU Volume276 set `SAlarm1605`; inert in the original too).
5. BlockingVolume (1802 on 71 maps): an invisible blocking brush; it is collision content (blocks players and
   non-zero-extent traces; 33 also block zero-extent traces, 22 let bullets through with bBulletGoTru, 30 do not
   block the camera, 13 not karma). These flags are read by the collision builder, not by script.
6. PlayerStart: `bEnabled`, `TeamNumber` (0 merc, 1 spy), `bMercDeathMatchStart` and `Priority` are used by the spawn
   choice (SGameInfo.Spawn); 28 map starts are disabled. STriggerPlayerStart (286 on 31 maps, 186 start disabled):
   Trigger enables it, UnTrigger disables it (already ported, `STriggerPlayerStart.cs`), so objectives open new
   spawn rooms.

## Network

Volumes are not replicated (bSkipActorPropertyReplication, static). Player starts are server only.

## Open questions

1. Volume.Encompasses is native and not decoded; brush containment needs the collision hull of the volume.

## Evidence

- `local/re-out/uc/Engine/Volume.uc`, `BlockingVolume.uc`, `PlayerStart.uc`, `SBase/STriggerPlayerStart.uc`,
  `Engine/PlayerReplicationInfo.uc`.
- Map survey (scratchpad maps-zones/survey.py) over the 84 gameplay.json files.
