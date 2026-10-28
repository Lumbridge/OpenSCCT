# SCamera (map security camera)

Units:
- def:SBase.SCamera
- uc:SBase.SCamera:Destroyed
- uc:SBase.SCamera:PreBeginPlay
- uc:SBase.SCamera:SetLockedByChaff
- uc:SBase.SCamera:s_Patrol.BeginState
- uc:SBase.SCamera:s_Patrol.Trigger
- uc:SBase.SCamera:s_Patrol.UnTrigger
- uc:SBase.SCamera:s_Patrol@code
- uc:SBase.SCamera:s_Inactive.BeginState
- uc:SBase.SCamera:s_Inactive.Trigger
- uc:SBase.SCamera:s_Inactive.UnTrigger
- uc:SBase.SCamera:s_LockedByChaff.SetLockedByChaff
- uc:SBase.SCamera:s_LockedByChaff.BeginState
- uc:SBase.SCamera:s_LockedByChaff.Timer
- nat:SBase.SCamera.CheckPresence
- eng:ASCamera::Tick
- rep:SBase.SCamera.RotationVelocity
- rep:SBase.SCamera.LightState
- rep:SBase.SCamera.CameraDesiredRotation

## Purpose

A wall or ceiling camera placed in the map that sweeps left and right, spots spies, turns to follow one and raises
its `Event` (on every map that has cameras the event goes to an `SAlarm`). It has nothing to do with the merc's
`SCamNetwork` ring (`cam-network.md`): mercs cannot look through it, and it does not extend `SOrientableObject`.
It extends `SBasicGamePlayObject` (interaction, chaff lock, malfunction message). 150 cameras on 36 maps.

## Data

| Field | Default | Meaning |
|---|---|---|
| `PatrolAngle` (edit) | 90 | half sweep in degrees, x 91 rotator units per degree |
| `RotationVelocity` (edit, replicated) | 5000 | yaw turn rate, units/s (0: the head never turns in yaw) |
| `VisibilityConeAngle` (edit) | 40 | detection cone, degrees; pitch and yaw offsets must both be below cone x 91 |
| `VisibilityMaxDistance` (edit) | 2000 | detection range, uu |
| `DetectionDelay` (edit) | 2 | seconds between a sighting and the event, then between repeats |
| `AlertOnEventFrom` (edit) | None | an actor whose Trigger raises (and UnTrigger clears) the alert directly |
| `bAutoWallSupport` (edit) | true | stick to the wall behind and spawn the mount |
| `bHighVisibilityRange` (edit) | false | not read by any script or decoded native |
| `LightState` (replicated) / `LocalLightState` | 2 / 2 | 0 locked, 1 inactive, 2 patrol, 3 alert |
| `CameraDesiredRotation` (replicated) | | where the head turns to |
| `PatrolDirection` | 1 | current sweep side, +1 / -1 |
| `fCheckPeriode` | 0.5 | state-code poll period (0.2 after a sighting) |
| `LightGreen`, `LightRed`, `LightOff` | `heat_SI_alarm_greenA`, `heat_si_alarm_redA`, `heat_SI_blackA` | LED skins (`Skins[1]`) |
| `CamDzzz` / `CamBipbip` | `Cam_Surveillance` / `Alarme_Volume` | spotted sound / alert ambient beep |
| `StickyNeutralizationTime` | 30 (maps: 15, 20, 45, 60) | lock length; chaff and sticky shots both use it |
| `WallSupportStaticmesh`, `WallSupportHeatIntensity` | `camera_blindee_pied`, 0.2 | the mount |

Class defaults also set `TriggerMethode = TriggerControl`, `iPriority = 5`, `bCanBeChaffed`,
`bStickyDamageIsLikeChaff`, `bBlockActors`, `bNoDelete`, mesh `camera_standard`, `HeatIntensity = 1`.

## Start-up

1. PreBeginPlay (the parent's is not called): remember the placed rotation as `InitialRotation`; with
   `bAutoWallSupport`, trace 200 uu straight back (world only); on a hit move the camera to the hit plus 24 uu
   along the normal and face the mount along the normal, else the mount takes the camera's yaw only; spawn an
   `SBasicGamePlayObject` mount owned by the camera with the mount mesh, drawn, simulated proxy, heat 0.2. Then
   `CameraDesiredRotation = InitialRotation + PatrolAngle x 91` in yaw, `PatrolDirection = 1`, nothing detected,
   poll 0.5 s. A map-set `PatrolDirection` or `CameraDesiredRotation` is therefore overwritten.
2. Destroyed: kill the malfunction emitter, destroy the mount.

## States

3. `s_Patrol` (auto). BeginState: `LightState = 2`. State code: when `CheckPresence` finds a spy and nothing was
   detected yet: play `CamDzzz` (Misc slot, volume 0.7, radius 64, attenuated), `LightState = 3`, detected, record
   the time, poll 0.2. Sleep the poll period and loop. The sighting itself fires nothing; the native tick does.
4. `s_Patrol` Trigger from `AlertOnEventFrom`: unless already detected, clear `DetectedPawn`, the same alert as
   rule 3, and fire `Event` at once (no instigator). Any other Trigger switches a camera whose `TriggerMethode` is
   not TriggerControl to `s_Inactive`. UnTrigger from `AlertOnEventFrom`: when detected, clear it and go green.
   Any other UnTrigger: `s_Inactive`.
5. `s_Inactive`: `LightState = 1`. A Trigger, or an UnTrigger when the method is not TriggerControl, flips the sweep
   (on the authority: `PatrolDirection` negated, desired yaw = initial + direction x angle, normalised) and goes
   back to `s_Patrol`.

## Chaff and sticky lock

6. `SetLockedByChaff(any)` calls the parent with `StickyNeutralizationTime` (remembers the state, timer, lock).
7. `s_LockedByChaff` (extends the parent's): BeginState runs the parent's (malfunction message), `LightState = 0`,
   plays `RA_camSurveil_EXPLO` (Misc, volume 1, radius 64). A new lock only restarts the timer at
   `StickyNeutralizationTime`. Timer: flip the sweep as in rule 5, back to the remembered state. EndState (parent)
   forgets it.

## Detection

8. `CheckPresence` (native 1328, game 0x10ab5dd0 / 0x10ab5df0) walks the level's pawn list and takes the first
   `SPawnAttaque` (spies only) with Health above 0 such that: a single line check from the camera to the pawn with
   flags 0x286 (level, movers, level geometry, stop at first hit; pawns are not traced) hits nothing or the pawn;
   the pitch and yaw of the line (atan2 x 10430.21875, truncated) minus the camera's current rotation, normalised,
   are both strictly below `VisibilityConeAngle x 91` in absolute value; the distance is strictly below
   `VisibilityMaxDistance`. It stores the pawn in `DetectedPawn`, its location in `DetectedLocation`, returns
   true. The cone is checked against the head's current rotation, so a sweeping camera sees what it faces.

## Native tick

9. ASCamera::Tick (game 0x10ab5fe0), every machine after the actor tick: when `LightState` differs from
   `LocalLightState`, copy it and apply: 2 green LED, no ambient sound; 3 red LED and `CamBipbip` as ambient sound;
   below 2 black LED, no ambient sound. Then a camera at state 0 spawns `SMalfunctionEmitter` sparks at its location
   when it has none; any other state destroys them.
10. Authority only, state above 1:
    - detected with a pawn: aim at `DetectedLocation`; when that yaw is within `PatrolAngle x 91` of the initial yaw
      the desired rotation becomes (initial pitch, that yaw, that roll). When more than `DetectionDelay` seconds
      passed since the last record: if `CheckPresence` still finds a spy, fire `Event` (instigator the detected
      pawn) and record the time again; otherwise calm down (not detected, `LightState = 2`), choose the sweep away
      from the side the head is on (yaw offset from initial above 0 gives -1, else +1), aim at that sweep end and
      poll 0.5. With `DetectionDelay` 0 (111 of the 150 cameras) the event repeats every tick while a spy is seen.
    - otherwise: when `PatrolAngle > 0` and the head's yaw offset is within 100 units of the current sweep end,
      flip the sweep (desired = initial + new direction x angle).
11. Authority only, state 0 or 1: desired = initial rotation with pitch -12000 (the head droops).
12. Every machine: turn the head towards `CameraDesiredRotation`: yaw by `sign x RotationVelocity x dt`, pitch by
    `sign x 10000 x dt`, each clamped to the remaining (normalised) difference and truncated; roll at once.

## Network

- `LightState`, `CameraDesiredRotation` and `RotationVelocity` replicate from the authority (script block; SCamera
  is not in the native list survey of `net-replists.md`, so the script conditions are used). Clients run rules 9
  and 12, so the LED, the beep, the sparks and the head motion follow the server.
- Detection, the alarm event and every state change are server decisions; the event dispatch is server only.

## Edge cases

- `DetectedPawn` may be cleared by an alert from `AlertOnEventFrom`; the tick then skips the follow and repeat
  branch until the next sighting.
- A camera with `RotationVelocity 0` (13 placed) never turns in yaw but still droops and sees.

## Port notes

- `src/OpenScct.Game/SBase/SCamera.cs`. Estimated: the malfunction emitter class name (the spawn loads a class by
  name; `SBase.SMalfunctionEmitter` is the only matching SBase class), the tick's emitter call (virtual +0x140) taken as `Emitter.Kill`, the head
  rotation set directly instead of a zero-delta MoveActor, and the pawn list taken as the level's `SPawnAttaque`s in
  level order.
- Render: the head turns through `DeviceViews` (the mesh follows Rotation, drawn where PreBeginPlay put it, 24 uu off
  the wall). The LED is `Skins[1]` drawn per surface from the gameplay pack's skin materials (renderpack
  `GAMEPLAY_SKINS`); the mount is drawn by `CameraMountViews`. Killed sparks stop spawning and die out in the views.
- Collision: the camera's mesh colliders are converted as kind `gpo` (spec/svmap/1.1/collision.md), so they turn with
  the head and bullet traces from a pawn hit them (a sticky shot locks the camera). The wall mount spawned at run
  time has no mesh collider and collides as the default 22 uu cylinder at the camera's centre (open: the original
  mount collides by its static mesh, if `camera_blindee_pied` has collision).
- The head only turns on a client while the camera is relevant to it; relevancy looks from the server's copy of the
  player's controller, which `UpdatePositionForServer` keeps at the client's camera (input-and-rotation.md rule 7).
- Vision values (`HeatIntensity`, `EMFIntensity` 0.6 / 0.8 on 32 cameras) come from the map props through the
  gameplay mesh path.
- The `Event` target `SAlarm` is ported elsewhere; until it is, the event reaches no behaviour.

## Sources

- `local/re-out/uc/SBase/SCamera.uc`; game 0x10ab5dd0 (exec), 0x10ab5df0 (CheckPresence), 0x10ab5fe0 (Tick);
  class `ASPawnAttaque` at 0x10c98b80; constant 10430.21875 at 0x10c10c5c, 91.0 at 0x10c10f38.
