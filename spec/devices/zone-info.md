# ZoneInfo and actor zones (zone separation)

Units:
- uc:Engine.ZoneInfo:PreBeginPlay
- uc:Engine.ZoneInfo:LinkToSkybox
- uc:Engine.ZoneInfo:ActorEntered
- uc:Engine.ZoneInfo:ActorLeaving
- nat:Engine.ZoneInfo.ZoneActors
- uc:Engine.Actor:ZoneChange

## Purpose

SCCT's ZoneInfo is the stock UE2 zone actor stripped of every physics rule: there is no zone gravity, velocity,
water, pain or kill zone in this game (no PhysicsVolume, no ZoneGravity, no bWaterZone; checked in the decompiled
Engine.ZoneInfo and Actor). A zone is a BSP region closed by zone portals and its ZoneInfo carries presentation
settings plus a name. Gameplay reads zones in four places: the player's location name (PlayerReplicationInfo),
SShaker shake events limited to one zone, SAlarmZoneInfo (alarm zones), and the spy's Enhanced Reality markers
(`SObjective.TriggersERVisibilityZones`, rule 8).

## Data

| Field | Default | Read by |
|---|---|---|
| `ZoneTag` | None | ZoneTrigger (no map has one) |
| `LocationName` | "" | PlayerReplicationInfo.GetLocationName (when no named volume) |
| `ForcedSkyZone`, `SkyZone` (replicated) | None | sky rendering |
| `bDistanceFog`, `bClearToFogColor`, `DistanceFogColor/Start/End/BlendTime` | off, (128,128,128), 3000, 8000, 1 | render fog (`DisplaySpaceEffect`) |
| `AmbientBrightness/Hue/Saturation`, `m_RefAmbientColor` | 0, 0, 255, black | render and AI light (`ActorLighting`, `AiLightField`) |
| `ZoneEffect` (I3DL2 listener) | None | reverb (`ReverbBuses`) |
| `AmbientSound`, `SoundVolume/Pitch/Radius`, `bFullVolume`, `SoundOcclusion` (Actor) | | zone ambience (`AudioWorld`) |
| `bLonelyZone`, `ManualExcludes` | off, empty | zone portal visibility (render culling) |

## Rules

1. Every actor has `Region` (zone actor, leaf, zone number). Zone 0 is the LevelInfo (LevelInfo extends ZoneInfo).
   The zone of a point is the world BSP's point region; zone numbers are the indices of the level model's zone
   array (the SVMap ZONE records keep that order).
2. SetActorZone (ULevel native, ported as `ZoneRegions.SetActorZone`): after every move (MoveActor, FarMoveActor,
   SetLocation), on spawn (with refresh, before Spawned) and at map start (all actors, with refresh) the actor's zone
   is looked up. When the zone actor changes: the old zone gets `ActorLeaving(actor)`, the actor gets
   `ZoneChange(newZone)`, the region is stored, then the new zone gets `ActorEntered(actor)`. Test moves change
   nothing. The LevelInfo is always in its own zone. Clients run the same lookup for replicated actors when their
   smoothed position is applied.
3. Refresh first leaves the current zone (ActorLeaving) and puts the actor in the LevelInfo's zone, so entering a
   real zone sends ActorEntered. In our port map start runs after the begin-play passes (the physics binds after
   the load); the original zones actors before PreBeginPlay (estimated, no map relies on the order: no
   SAlarmZoneInfo is placed).
4. ZoneInfo.ActorEntered and ActorLeaving are empty; Actor.ZoneChange is empty. Subclass SAlarmZoneInfo uses
   ActorEntered (no map places one).
5. PreBeginPlay links the sky: `ForcedSkyZone` when set; else the last SkyZoneInfo in the actor list, then the last
   one allowed by the detail mode (low: neither bHighDetail nor bSuperHighDetail; high: not bSuperHighDetail;
   super high: any).
6. `ZoneActors(class)` iterates live actors of the class whose `Region.Zone` is this zone.
7. ZoneTrigger (Trigger subclass) triggers or untriggers every ZoneInfo whose ZoneTag equals its Event. ZoneInfo has
   no Trigger handler of its own, so a ZoneTag is inert; KrauE, KrauU (`labo`) and PyroD (`Gen_ROom`) set one and
   no map places a ZoneTrigger.

### ER visibility zones

8. Spy Enhanced Reality (ASHud::ERDrawObjectives, game 0x10af8c00, objective pass at 0x10af92be): every frame each
   objective's marker gets a target alpha of 255, or 0 when the objective is taken, the viewer's zone is one of the
   objective's `TriggersERVisibilityZones`, the viewer's zone number equals one of its triggers' zone numbers, or
   equals the objective's own. Each trigger's marker target is 255 when (the objective's target is 0 or the viewer
   shares the trigger's zone number) and the trigger is bActive or bLocked, else 0. A changed target restarts the
   fade (reference time = Level.TimeSeconds) and the alpha ramps towards it; markers with alpha 0 are not drawn.
   So from far away the spy sees one marker per objective, and inside the objective's rooms (the listed zones and
   the triggers' own zones) he sees the individual terminals or disks instead. Ported as
   `SObjective.ERSeesTriggersFrom / ERObjectiveTargetVisibility / ERTriggerTargetVisibility`, used by the HUD
   (`HudSimReader.ReadObjectives`, drawn by `HudMarkers`) with the viewer's pawn region; the alpha ramp is not
   ported (markers switch on and off).

## Zone separation survey (84 converted maps)

How maps separate space, and where each is honoured:

| Mechanism | Maps | Port |
|---|---|---|
| BSP zones (ZoneInfo, 2751 actors) | 82 | zones tracked (rules 1 to 3) |
| Zone ambience (AmbientSound 1846, ZoneEffect 1288 zones) | 82 | audio layer |
| Zone fog (bDistanceFog 260 zones) and ambient light | many | render layer |
| Zone LocationName (2254 zones), Volume LocationName (751 volumes) | 82 / 60 | PlayerReplicationInfo |
| ER visibility zones (SObjective.TriggersERVisibilityZones) | 23 | rule 8, HUD markers |
| Zone portal culling (ManualExcludes on 270 zones; ForcedVisibilityZoneTag on movers, SGamePlayObjects, SAnimatedMeshes, fish, a PlayerStart, an emitter, a light, infirmaries) | about 25 | not honoured (Godot draws all zones) |
| ZoneTag | 3 | inert in the original too |
| SShaker per-zone shake | 0 set a Zone | SShaker reads Region.Zone |
| SAlarmZoneInfo, ZoneTrigger | 0 | not placed |
| Volumes (detection regions of SVolumetricSensor, named areas) | 60 | volume.md |
| BlockingVolume (1802) | 71 | collision content (blocking brushes) |
| Movers and SGamePlayObjects as dividers | many | mover.md, gameplay-object.md |

## Network

Region is not replicated (const, computed on each machine). SkyZone replicates. Zone settings are static map data.

## Constants

| Name | Value | Provenance | Source |
|---|---|---|---|
| default DistanceFogStart / End / BlendTime | 3000 / 8000 / 1 | decoded | def:Engine.ZoneInfo |
| default AmbientSaturation | 255 | decoded | def |
| default DistanceFogColor | (128,128,128) | decoded | def |

## Open questions

1. SetActorZone is stock UE2 behaviour, not decoded in the game exe (ULevel::MoveActor 0x10a1b780 calls it).
2. HUD wiring of rule 8 (objective-level marker and per-trigger fades) belongs to HudMarkers.
3. Zone portal visibility (ManualExcludes, bLonelyZone, ForcedVisibilityZoneTag) has no render equivalent.

## Evidence

- `local/re-out/uc/Engine/ZoneInfo.uc`, `ZoneTrigger.uc`, `Actor.uc` (ZoneChange, Region), `SBase/SAlarmZoneInfo.uc`,
  `SBase/SObjective.uc`, `Engine/PlayerReplicationInfo.uc`.
- Game exe ERDrawObjectives 0x10af8c00 (objective pass 0x10af92be..0x10af93ac), BuildObjectivesAndTriggersList
  0x10af80b0.
- Survey script over `.content/svmap/*/sim/gameplay.json` (scratchpad maps-zones/survey.py, zprops.py).
