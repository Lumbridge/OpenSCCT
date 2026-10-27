# SShaker (camera shake on an event)

Units:
- def:SBase.SShaker
- uc:SBase.SShaker:Trigger
- uc:SBase.SPlayerController:ClientShake
- rep:SBase.SPlayerController.ClientShake
- uc:SBase.SPlayerCam:Tilt
- uc:SBase.SPlayerCam:Shake
- uc:SBase.SPlayerCam:Hit
- uc:SBase.SPlayerCam:ResetEffects
- nat:Engine.Actor.DynamicPlayerControllers

## Purpose

Jolts every player's view when one of a named set of senders fires the shaker's Tag. SteeE has one, Tag `blast01`,
answering only the crane (`SAnimatedMesh277`): the view tilts and rolls as the wall blows.

## Data

| Field | Type | Default | Meaning |
|---|---|---|---|
| `ShakeEvents` | array of ShakeEvent | empty | one entry per sender and effect |
| ShakeEvent.`EventSender` | Actor | None | the `Other` that must match |
| ShakeEvent.`strenght` | int | 0 | amplitude (rotator units) |
| ShakeEvent.`speed` | int | 0 | rate |
| ShakeEvent.`FadeOut` | int | 0 | decay |
| ShakeEvent.`Zone` | ZoneInfo | None | None means every zone |
| ShakeEvent.`bTilt`, `bShake` | bool | false | pitch tilt, roll shake |
| Defaults | | | `RemoteRole=ROLE_None`, hidden, `bBlockZeroExtentTraces=False` |

SPlayerCam receiving fields: `m_tiltPitch`, `m_tiltTargetPitch`, `m_tiltSpeed`, `m_tiltFadeOut`, `m_shakeRoll`,
`m_shakeTargetRoll`, `m_shakeSpeed`, `m_shakeFadeOut`, `m_hitRoll`, `m_hitFadeOut` (all int).

## Rules

1. `Trigger(Other, EventInstigator)`: for each entry `i` in order whose `EventSender == Other`: for each
   SPlayerController `PC` in the level (dynamic player-controller iteration), if `Zone == None` or
   `PC.Pawn.Region.Zone == Zone`, call `PC.ClientShake(strenght, speed, FadeOut, bTilt, bShake)`.
   Senders that are not listed are ignored. UnTrigger does nothing.
2. `ClientShake(strenght, speed, FadeOut, bTilt, bShake, bRumble = false)` (simulated, reliable server-to-owning-
   client): if `bTilt`, `m_camera.Tilt(strenght, speed, FadeOut)`; if `bShake`, `m_camera.Shake(strenght, speed,
   FadeOut)`; if `bRumble` and vibration is allowed, start pad rumble (not used here).
3. `SPlayerCam.Tilt(s, v, f)`: `m_tiltPitch = s`, `m_tiltTargetPitch = s`, `m_tiltSpeed = v`, `m_tiltFadeOut = f`.
   `Shake(s, v, f)`: `m_shakeRoll = s`, `m_shakeTargetRoll = s`, `m_shakeSpeed = v`, `m_shakeFadeOut = f`.
   `Hit(s, f)`: `m_hitRoll = s`, `m_hitFadeOut = f`. `ResetEffects` zeroes hit, tilt and shake values.
   Each call overwrites the previous values: two entries for the same sender leave the last tilt and the last shake.
4. How these fields move the view each frame (decoded, camera.md rule 6, UpdateView editor 0x111f4e50): each of
   m_hitRoll, m_shakeRoll / m_shakeTargetRoll and m_tiltPitch / m_tiltTargetPitch approaches its target (0 for the
   hit roll) at its speed field per second (times Level.m_dT); a reached shake or tilt target shrinks by its fade-out
   and flips sign (game 0x10a96280), so the roll swings side to side and the tilt nods as they die down. The tilt
   is added to the pitch and the rolls replace the roll, applied to the controller with a rotation-only MoveActor;
   the following view shows them, the first-person view keeps roll 0 (camera.md rules 5.1 and 6).

## States

None.

## Constants

SteeE `SShaker37372` (Tag `blast01`):

| Entry | Sender | strength | speed | FadeOut | Tilt | Shake |
|---|---|---|---|---|---|---|
| 0 | SAnimatedMesh277 | 1000 | 10000 | 1000 | yes | yes |
| 1 | SAnimatedMesh277 | 800 | 10000 | 2000 | yes | no |

Net effect per player at the crane `blast01` notify (10.962 s into the crane sequence): tilt 800 / 10000 / 2000
(entry 1 overwrites entry 0's tilt) and shake 1000 / 10000 / 1000. Provenance: decoded (map data and script order).

## Network

Server runs `Trigger`; each player gets two reliable `ClientShake` calls on its own controller, in entry order, so the
final values match on every client. Spectating and dead players are included (no pawn test when `Zone` is None).

## Edge cases

- With a `Zone` set, a controller without a pawn reads a None pawn's zone: the test fails (no shake) and the engine
  logs an access warning.
- The shaker sees the actor that started the chain as `Other`; an SMagicEvent in between is transparent (it forwards
  `Other`).

## Evidence

- `local/re-out/uc/SBase/SShaker.uc`, `SBase/SPlayerController.uc` (ClientShake, replication block lists it as
  reliable from the authority), `SBase/SPlayerCam.uc` (Tilt, Shake, Hit, ResetEffects).
- SteeE.sdc export `SShaker37372`.
- RE+ MapBuilderPatterns.md "Camera shake": the sender seen is the actor that started the chain; stock strengths 300
  to 1000, speed 10000, FadeOut 1000 to 4000.
- Native: DynamicPlayerControllers `0x10a28420`.

## Open questions

2. The controller value at +0x54c that scales the approach rate (camera.md rule 6) is not named; a frame trace of
   the view rotation after the crane blast would pin it down.

### Resolved

1. The camera's use of tilt, shake and hit roll. Resolved: each value approaches its target at its fade field per
   second, a reached shake or tilt target shrinking and flipping sign; tilt adds to the pitch, the rolls replace the
   roll; the first-person view keeps roll 0. Rule 4. Evidence: camera.md rule 6 (ANSWERS-actions 21).
