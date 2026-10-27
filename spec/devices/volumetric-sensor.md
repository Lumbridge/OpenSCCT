# SVolumetricSensor (motion sensor over volumes)

Units:
- def:SBase.SVolumetricSensor
- uc:SBase.SVolumetricSensor:PostBeginPlay
- uc:SBase.SVolumetricSensor:Destroyed
- uc:SBase.SVolumetricSensor:LightStateChange
- uc:SBase.SVolumetricSensor:SetLockedByChaff
- uc:SBase.SVolumetricSensor:s_Active.BeginState
- uc:SBase.SVolumetricSensor:s_Active.Timer
- uc:SBase.SVolumetricSensor:s_Active.CheckPresence
- uc:SBase.SVolumetricSensor:s_Active.UnTrigger
- uc:SBase.SVolumetricSensor:s_Active@code
- uc:SBase.SVolumetricSensor:s_SomethingDetected.BeginState
- uc:SBase.SVolumetricSensor:s_SomethingDetected.EndState
- uc:SBase.SVolumetricSensor:s_SomethingDetected.Timer
- uc:SBase.SVolumetricSensor:s_SomethingDetected.UnTrigger
- uc:SBase.SVolumetricSensor:s_Inactive.BeginState
- uc:SBase.SVolumetricSensor:s_Inactive.Trigger
- uc:SBase.SVolumetricSensor:s_LockedByChaff.SetLockedByChaff
- uc:SBase.SVolumetricSensor:s_LockedByChaff.BeginState
- uc:SBase.SVolumetricSensor:s_LockedByChaff.Tick
- rep:SBase.SVolumetricSensor.bLightState

## Purpose

A wall box watching one or more plain volumes (`Volumes`). A pawn of `DetectedClass` moving at `MinDetectedVelocity`
or more inside a watched volume fires the sensor's `Event` with that pawn as the instigator (usually into an
`SAlarm`), at once or after a `DetectionDelay` confirmation. Its LED shows green, red, or black. 251 placed on 33 maps.

## Data

| Field | Type | Default | Meaning |
|---|---|---|---|
| `Volumes` | array of Volume, edit | | the watched volumes (brush volumes of the map) |
| `DetectedClass` | class, edit | SPawnAttaque | who is detected |
| `MinDetectedVelocity` | int, edit | 150 | minimum speed (uu/s) |
| `DetectionDelay` | float, edit | 1.0 | confirmation time; 0 fires at once |
| `bHighVisibilityRange` | bool, edit | false | not read by the script |
| `CurrentIndex` | int | 0 | volume checked last |
| `DetectedActor` | Actor | | pawn being confirmed |
| `bLightState` | byte, replicated | 2 | 0 broken, 1 off, 2 green, 3 red |
| `bLocalLightState` | byte | 2 | state the LED shows |
| `LightGreen`, `LightRed`, `LightOff` | heat texture modifiers | heat_SI_alarm_green_cligA, heat_si_alarm_redA, heat_SI_blackA | LED skins (skin 1) |
| Inherited | | | `StickyNeutralizationTime` 30, `bCanBeChaffed`, `bStickyDamageIsLikeChaff`, `iPriority` 5, `ERControl` |
| Class defaults | | | StaticMesh `volum_sens`, HeatIntensity 0.5, EMFIntensity 1, skin 1 green, `bSideFadeEffect`, `nativereplication` |

Map values: `DetectionDelay` 0 on 227 of 241 that set it (0.1, 0.2, 0.5, 0.8 on the others); `MinDetectedVelocity` 1
(131), 0 (41), 10 (21), 200 (12), 99 (9), 5, 50; `DetectedClass` SBasePawn on 10 (mercs too); `bCanBeChaffed` and
`bStickyDamageIsLikeChaff` false on 13; `StickyNeutralizationTime` 45 (4) or 500 (1); `bHidden` (33, an invisible
sensor) and draw values (DrawScale3D, Skins with a yellow LED on 4, StaticMesh None on 8, glow, lighting flags,
Heat/EMF intensities) handled by the generic gameplay mesh; `SoundRadius` 30 or 34 and `SoundVolume` 144 are overwritten
by s_SomethingDetected.

## Rules

1. **PostBeginPlay:** `CurrentIndex = 0`, then the gameplay object's.
2. **s_Active** (auto). BeginState and Timer: `bLightState = 2`. State code: `Sleep(0.5 / Volumes.Length)`,
   CheckPresence, loop (all volumes every 0.5 s). UnTrigger: s_Inactive. No Trigger (an active sensor ignores it).
3. **CheckPresence:** `CurrentIndex + 1`, wrapping to 0 past the end; for each pawn touching that volume of
   `DetectedClass` with `|Velocity| >= MinDetectedVelocity`: `DetectedActor` = it; with `DetectionDelay == 0`
   `TriggerEvent(Event, self, pawn)`, `bLightState = 3` and a 3 s timer (back to green), and the walk goes on (every
   such pawn fires); otherwise GotoState s_SomethingDetected.
4. **s_SomethingDetected.** BeginState: `bLightState = 3`, timer `DetectionDelay`, ambient sound
   `Interface.Objectifs.Alarme_Volume` at volume 200, radius 24. EndState: no ambient sound. Timer: every pawn still
   touching the current volume, of `DetectedClass`, fast enough and equal to `DetectedActor` fires the Event; then
   s_Active. UnTrigger: s_Inactive.
5. **s_Inactive.** BeginState `bLightState = 1`. Trigger: s_Active.
6. **SetLockedByChaff:** the gameplay object's lock for `StickyNeutralizationTime`. **s_LockedByChaff:** SetLockedByChaff
   restarts it; BeginState: malfunction message, `bLightState = 0`, sound RA_camSurveil_EXPLO (SLOT_Misc, radius 24);
   Tick: when `bLightState` differs from `bLocalLightState`, the LED and sparks as LightStateChange. The inherited Timer
   returns to the remembered state.
7. **LightStateChange** (simulated event): `bLocalLightState = bLightState`; skin 1 green (2), red (3), black (below
   2); state 0 spawns the malfunction sparks, any other kills them.
8. **Destroyed:** the sparks go, then the gameplay object's clean-up.
9. **Replication:** `bLightState` whenever it changes (script condition; native list not decoded).

## Port

`src/OpenScct.Game/SBase/SVolumetricSensor.cs`, `SVolumetricSensor.States.cs`, constants in
`src/OpenScct.Game/Devices/SensorK.cs`. Volumes keep their `Touching` list from the physics touch pass (plain Volume
brushes are touch colliders in `collision.bin`).

## Open questions

- The native caller of LightStateChange is not decoded: the port compares `bLightState` with `bLocalLightState` after
  every tick on every machine.
- The LED skin (`Skins[1]`) is drawn per surface from the gameplay pack's skin materials (renderpack `GAMEPLAY_SKINS`),
  so it changes colour on screen; the blinking `green_cligA` is drawn from its first frame only if its texture
  animates (not checked).
- Emitter.Kill: the sparks actor goes at once in the simulation; the drawn particles stop spawning and die out.
- `Volumes.Length == 0` would divide by zero in the original; the port stops the state code (no map has an empty list).
