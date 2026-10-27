# SLaserTrigger (laser tripwire)

Units:
- def:SBase.SLaserTrigger
- uc:SBase.SLaserTrigger:Destroyed
- uc:SBase.SLaserTrigger:PostBeginPlay
- uc:SBase.SLaserTrigger:PostNetBeginPlay
- nat:SBase.SLaserTrigger.AdjustScale
- nat:SBase.SLaserTrigger.CheckPresence
- uc:SBase.SLaserTrigger:SetLockedByChaff
- uc:SBase.SLaserTrigger:s_Active.BeginState
- uc:SBase.SLaserTrigger:s_Active.Timer
- uc:SBase.SLaserTrigger:s_Active.Trigger
- uc:SBase.SLaserTrigger:s_Active.UnTrigger
- uc:SBase.SLaserTrigger:s_Active.Tick
- uc:SBase.SLaserTrigger:s_Inactive.BeginState
- uc:SBase.SLaserTrigger:s_Inactive.Trigger
- uc:SBase.SLaserTrigger:s_Inactive.UnTrigger
- uc:SBase.SLaserTrigger:s_Inactive.Tick
- uc:SBase.SLaserTrigger:s_LockedByChaff.SetLockedByChaff
- uc:SBase.SLaserTrigger:s_LockedByChaff.BeginState
- uc:SBase.SLaserTrigger:s_LockedByChaff.Trigger
- uc:SBase.SLaserTrigger:s_LockedByChaff.UnTrigger
- uc:SBase.SLaserTrigger:s_LockedByChaff.EndState
- uc:SBase.SLaserTrigger:s_LockedByChaff.Tick
- uc:SBase.SLaserTrigger:s_Absent.SetLockedByChaff
- uc:SBase.SLaserTrigger:s_Absent.BeginState
- rep:SBase.SLaserTrigger.bHideLaserInNormalVision
- rep:SBase.SLaserTrigger.LaserLength
- rep:SBase.SLaserTrigger.bIsBroken
- rep:SBase.SLaserTrigger.bLaserHidden
- eng:ASLaserTrigger::IsNetRelevantFor

## Purpose

A wall-mounted emitter whose beam runs `LaserLength` units straight ahead. A spy crossing the beam fires the
tripwire's `Event` with that spy as the instigator (almost always into an `SAlarm`). The beam is cut where the spy
stands on every machine that draws it. Events switch the tripwire off and on, chaff or a sticky shot breaks it for a
while, and `SpawnPerCent` can leave it out of a round. 1088 placed on 53 maps (none in SteeE).

## Data

| Field | Type | Default | Meaning |
|---|---|---|---|
| `LaserLength` | int, edit, replicated first bunch | 300 | beam length (uu) along the emitter's X axis |
| `bHideLaserInNormalVision` | bool, edit, replicated first bunch | true | beam only in thermal and EMF; false: also in normal, night and motion |
| `SpawnPerCent` | byte, edit | 100 | chance (percent) the tripwire is present this round (server roll) |
| `DetectedClass` | class, edit | SPawnAttaque | **not read**: the native check tests SPawnType instead |
| `fCheckFrequency` | float | 0.1 | check period CheckPresence chooses; the timer takes it when s_Active begins |
| `bLaserHidden` | bool, replicated | false | beam hidden (off, broken, absent) |
| `bIsBroken` | bool, replicated | false | chaffed: sparks shown |
| `LaserEndLocation` | vector | | `Location + X * LaserLength` |
| `PrevLaserEndLocation` | vector | | last drawn beam end |
| `Laser` | SLaserBeam | | the drawn beam (drawing machines only) |
| `LaserStaticmesh` | static mesh | SGameplayObjects.Objects.LasDetectBeam | beam mesh (1 uu along X) |
| `LaserSkin` | material | SGameplayObjectsTex.Object.LaserBeamShader | beam texture, through a TexScaler |
| `MalfunctionEmitter` | SMalfunctionEmitter | | sparks while broken |
| Inherited (SBasicGamePlayObject) | | | `TriggerMethode`, `StickyNeutralizationTime` 30, `bCanBeChaffed`, `bStickyDamageIsLikeChaff`, `iPriority` 5, `ERControl`, `bTriggerOnceOnly` |
| Class defaults | | | StaticMesh `lasertrig_standard`, HeatIntensity 0.6, `bNetInitialRotation`, `nativereplication`, RemoteRole SimulatedProxy |

Map values (all 53 maps): `LaserLength` 128 to 400 (161 at 128, 108 at 200, 106 at 256...); `bHideLaserInNormalVision`
false on 386 tripwires (beam visible to everyone in normal vision); `StaticMesh` lasertrig_blind on 260;
`EMFIntensity` 10.0 (204) or 0.4 (125), `HeatIntensity` 0.8 (4), collision 10 x 10 (204), `PrePivot` (4),
`bIgnoreEchelonLighting` (89) are body draw values (the generic gameplay mesh draws them); `ERControl` (254) names an
SGPOControl relay; `bTriggerOnceOnly` (125) has no reader in script or in the tripwire natives; no map sets
`SpawnPerCent`, `DetectedClass` or `TriggerMethode`.

## Rules

1. **Start (PostNetBeginPlay).** The server rolls `FRand() x 100 > SpawnPerCent`: then `bLaserHidden` and the state
   goes to s_Absent (MirrorState), nothing else. Otherwise `bLaserHidden` false. Every machine computes
   `LaserEndLocation`; a machine that draws (not a dedicated server) spawns the SLaserBeam at the emitter pointing at
   the end, gives it `LaserStaticmesh`, no collision, static-mesh draw, `bHidden = bLaserHidden`, HeatIntensity 1
   (halved in a rain volume), unlit, `bHideInNormalVision = bHideLaserInNormalVision`, no dynamic light, a new
   TexScaler over `LaserSkin` as skin 0, and sizes it (AdjustScale to the full end). Then GotoState s_Active.
2. **AdjustScale (native 1326, 0x10ab6970 -> 0x10ab6820).** With a beam: `d = |Location - End|`, beam
   `DrawScale3D = (d - 1, DrawScale, DrawScale)`, the TexScaler's U scale `LaserLength / d`, and the beam is moved back
   onto the emitter (FarMoveActor).
3. **CheckPresence (native 1327, 0x10ab69c0).** `fCheckFrequency = 1.0`; best end = `LaserEndLocation`, no blocker.
   For each pawn of the level's pawn list that is non-null with `SPawnType == SPAWNOFFENSE` (a spy):
   - `N` = point of the beam segment nearest the pawn; skip when `|N.Z - P.Z| > CollisionHeight`;
   - `flat = horizontal |N - P| - CollisionRadius`. When `flat > 0` the pawn is not on the beam: `f = flat / 650`,
     period `0.01` if `f <= 0.01`, `f + 0.01` if `f <= 1`, else `1.0`; `fCheckFrequency = min(fCheckFrequency, period)`;
   - on the beam: `fCheckFrequency = 0.016`; `across = |V| - |unit(Location - End) . V|`. When `250 < across <= 460`
     the block point is `N`; otherwise a zero-extent line check from the emitter to the end against the pawn's own
     primitive (its cylinder), skipped when it misses, the block point its hit location;
   - the block point nearest the emitter wins (strictly nearer than the current best).
   When the best end differs from `PrevLaserEndLocation`: store it and AdjustScale to it. On the authority, with a
   blocker: `TriggerEvent(Event, self, blocker)` (every check while the spy stays on the beam).
4. **s_Active** (auto). BeginState: a `bHidden` tripwire goes to s_Absent (the rest still runs); on the authority
   `bLaserHidden` false and the beam shown; `SetTimer(fCheckFrequency, loop)`; `bIsBroken` false. Timer: CheckPresence.
   Trigger: unless `TriggerMethode == TriggerControl`, s_Inactive. UnTrigger: s_Inactive.
5. **s_Inactive.** BeginState: `bLaserHidden` true, beam hidden, `bIsBroken` false. Trigger: s_Active. UnTrigger: unless
   TriggerControl, s_Active. The looping timer keeps running the global Timer (the description retry, harmless).
6. **Tick of s_Active, s_Inactive and s_LockedByChaff (simulated):** the beam's `bHidden` follows `bLaserHidden`; a
   broken tripwire without sparks spawns an SMalfunctionEmitter, an unbroken one with sparks kills them.
7. **SetLockedByChaff** (global): only when not `bHidden` and not `bIsBroken`, the gameplay object's lock for
   `StickyNeutralizationTime` (the chaff's own length is ignored).
8. **s_LockedByChaff** (extends the gameplay object's): SetLockedByChaff restarts `StickyNeutralizationTime`.
   BeginState: the gameplay object's (malfunction message), beam hidden, `bIsBroken` true, sound
   `Armes_impacts.Impacts.RA_camSurveil_EXPLO` (SLOT_Misc, volume 1, radius 24). Trigger: remembered s_Inactive becomes
   s_Active; remembered s_Active becomes s_Inactive unless TriggerControl. UnTrigger: remembered s_Active becomes
   s_Inactive. EndState: the gameplay object's, then the beam shown again. The inherited Timer returns to the
   remembered state.
9. **s_Absent** ignores Trigger, UnTrigger, Timer, Tick and SetLockedByChaff; BeginState sets `bHidden`.
10. **Destroyed:** the beam and the sparks go, then the gameplay object's clean-up.
11. **Replication** (script conditions; the native list is not decoded): `bHideLaserInNormalVision` and `LaserLength`
    in the first bunch, `bIsBroken` and `bLaserHidden` whenever they change. The tripwire is not bNoDelete, so a
    client destroys its map copy and gets the server's (Location, and Rotation through `bNetInitialRotation`).
12. **Relevancy (0x10ab6d80, read as IsNetRelevantFor):** bAlwaysRelevant: yes; `bHidden` (absent): no; else yes while
    `|Location - viewer|^2 - LaserLength^2 <= 2500^2`.

## Drawing

- The emitter body is an ordinary gameplay mesh (its own Heat/EMF values from the map). A client destroys its map
  copy at load (rule 11), so the body's node follows the server's copy once it arrives (`DeviceViews.Rebind`, same
  class at the map placement) and hides while the tripwire is not relevant.
- The beam (SLaserBeam, drawn by `game/Scripts/Fx/LaserTriggerView.cs` through `Render/Lasers.cs`): from the beam's
  place along its rotation for `DrawScale3D.X x DrawScale` uu. Thermal (36): the heat colour of HeatIntensity (1).
  EMF (38): a full glow. Other modes (5 normal, 34 night, 37 motion): only when `bHideInNormalVision` is false, as the
  additive LaserBeamShader streak. Hidden while `bLaserHidden`.

## Port

`src/OpenScct.Game/SBase/SLaserTrigger.cs`, `SLaserTrigger.States.cs`, constants `src/OpenScct.Game/Devices/SensorK.cs`,
relevancy `src/OpenScct.Net/Replication/NetRelevancy.cs` (LaserTriggerRelevant). Map actors load as the generated
class with these partials (the SBase package is registered for maps), so no registry entry is needed.

## Open questions

- The pawn primitive's line check uses `GadgetWorld.SegmentCylinderHit` (entry point, no pull-back).
- GetRainVolume (native 1789) is not ported; no converted map places an ERainVolume, so the beam is never halved.
- Emitter.Kill: the sparks actor goes at once in the simulation; the drawn particles stop spawning and die out.
- The TexScaler U scale is kept (`LaserScalerUScale`) but the views do not apply it; the normal-vision look reuses the
  spy beam's additive LaserBeamShader material on LasDetectBeam (not checked against the original's picture).
- Which vision modes hide a `bHideInNormalVision` actor is taken from docs/RENDERING.md (shown in 36 and 38 only).
- The native GetOptimizedRepList of the class is not decoded (script conditions used); 0x10ab6d80's vtable slot was
  not read (its shape is IsNetRelevantFor's).
- `ERControl` (SGPOControl relay) is not ported here; `bTriggerOnceOnly` has no reader found.
