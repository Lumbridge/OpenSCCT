# SFish, SFish_Big and SFish_Follow (aquarium fish)

Units:
- def:SBase.SFish
- uc:SBase.SFish:PostBeginPlay
- uc:SBase.SFish:InitRandomPos
- uc:SBase.SFish:TestOut
- uc:SBase.SFish:RotateFish
- uc:SBase.SFish:Forward
- uc:SBase.SFish:InitSpeed
- uc:SBase.SFish:BackToIn
- uc:SBase.SFish:TestIn
- uc:SBase.SFish:s_inbocal.Tick
- uc:SBase.SFish:s_outbocal.Tick
- def:SBase.SFish_Big
- uc:SBase.SFish_Big:s_inbocal.Tick
- def:SBase.SFish_Follow
- uc:SBase.SFish_Follow:PostBeginPlay
- uc:SBase.SFish_Follow:Tick
- uc:SBase.SFish_Follow:UpdateYawPitch

## Purpose

Cosmetic fish that swim around their placed location: the Aqua maps' tanks and lagoon (AquaD, AquaE, AquaT), OffsD's
sea and SkysD/SkysP's aquarium. Small fish are static meshes (`Aquarium_STM.Aquarium.aqua_fish*`); sharks, mantas
and dolphins are skeletal (`AQU_anm.aqu_shark_mesh`, `AQU_manta_mesh`, `aqu_dauphin_mesh`, DrawType DT_Mesh) and
loop `AnimsFish[0]` swimming straight, `AnimsFish[1]` turning. SFish_Follow fish swim in a school behind a pilot SFish
(`bPilote`).

## Data

| Field | SFish default | SFish_Big default | Meaning |
|---|---|---|---|
| `fEloignementMaxX/Y/Z` | 300, 300, 200 | | box half-size around the placed location (posinit) |
| `fSpeedMin`, `fSpeedMax` | 30, 400 | 100, 100 | speed, uu/s |
| `fRotHmin`, `fRotHmax` | 36000, 72000 | 3000, 6000 | turn rate, rotation units/s |
| `fAngleVmin`, `fAngleVmax` | -3000, 3000 | -2000, 2000 | pitch range |
| `fTempsAvanceMin/Max` | 0.5, 1.5 | 1, 2 | seconds of a straight swim |
| `fTempsTourneMin/Max` | 0.1, 0.2 | 1, 2 | seconds of a turn |
| `bPilote` | false | | leads a school: keeps its placed location at start |
| `AnimsFish[2]` | None | | straight and turning sequences |
| SFish_Follow `Pilot`, `fRefreshTimeMin/Max` | None, 0.2, 1.0 | | the pilot, and how often the wanted point is re-aimed |

All: `RemoteRole=ROLE_SimulatedProxy`, `bNoDelete`, DrawType DT_StaticMesh by default.

## Rules

1. `PostBeginPlay` (simulated): PHYS_MovingBrush (no movement of its own for a non-mover); `posinit = Location`; unless
   `bPilote`, `InitRandomPos`: a random location within 0.7 of the box, a random yaw, pitch `fAngleVNext` in the
   pitch range. Then `bTourne = false`, `InitSpeed`, `fCurrentSpeed = fWantedSpeed`.
2. `InitSpeed`: `fWantedSpeed` random in the speed range. Turning: loop `AnimsFish[1]`, phase ends in a random
   `fTempsTourne`, `fWantedRotH` random in the turn range, negated when `int(TimeSeconds * 100)` is odd, and
   `fAngleVNext` random in the pitch range, negated when `int(TimeSeconds * 333.333)` is odd. Straight: loop
   `AnimsFish[0]`, phase ends in a random `fTempsAvance`. A None sequence leaves the channel as it is.
3. `RotateFish(dt)`: yaw += `int(fCurrentRotH * dt)` wrapped once into -32768..32767; `fAngleVCurrent = (40 x
   fAngleVCurrent + fAngleVNext) / 41`; pitch = `int(fAngleVCurrent)`. `Forward(dt)`: Location += vector(Rotation) x
   `fCurrentSpeed` x dt.
4. `TestOut` / `TestIn` first steer the pitch back: above `posinit.Z + fEloignementMaxZ` `fAngleVNext = FRand x
   fAngleVmin`, below `- fEloignementMaxZ` `FRand x fAngleVmax`. Out means past 0.7 of the box in X or Y. `TestIn`
   also clamps a fish past the box edge back onto it (SetLocation) and, when in, zeroes `fWantedRotH`.
5. SFish `s_inbocal` (auto): `fCurrentRotH = fWantedRotH`; at the phase end flip `bTourne`, `InitSpeed`, and jump
   the speed up to a faster wanted speed. Turning: rotate, ease the speed to `fSpeedMin` (x10 + min) / 11, swim.
   Straight: swim; out goes to `s_outbocal`, else ease the speed to `fSpeedMin`.
6. `s_outbocal`: `fCurrentRotH` eases to `fWantedRotH` ((x8 + wanted) / 9). In again: straight, `InitSpeed`, no
   turn, a phase of `fTempsTourne` length, back to `s_inbocal`. Else `BackToIn` (rotate, then aim at posinit:
   facing within 90 degrees eases speed to `fSpeedMax` and turns at half `fRotHmax`, otherwise half speed and full
   `fRotHmax`, towards the side of the yaw difference) and swim.
7. SFish_Big `s_inbocal`: `fCurrentRotH` eases to `fWantedRotH`; at the phase end flip, `InitSpeed`, and a straight
   phase zeroes `fWantedRotH`; always rotate and swim; out goes to `s_outbocal`, else the speed eases to
   `fWantedSpeed`.
8. SFish_Follow `PostBeginPlay`: PHYS_MovingBrush; with a Pilot, `fSpeedMax = 1.2 x` the pilot's, the flat offset
   to the pilot as relative yaw (pilot yaw minus the offset's yaw) and distance, the height difference, and the
   pilot's X/Y box (pilot location +- its fEloignementMax); then `UpdateYawPitch`. It logs a missing or non-pilot
   Pilot.
9. `UpdateYawPitch`: wanted point = pilot location - vector(pilot rotation + relative yaw) x distance, its Z offset
   the height difference; next refresh in a random `fRefreshTime`.
10. SFish_Follow `Tick` (only while the Pilot is an SFish with `bPilote`): refresh when due; within 5 uu (flat) of
    the wanted point loop `AnimsFish[1]` (once), slow by 100 uu/s to 0 and drift along its facing; else loop
    `AnimsFish[0]` (once), speed eases towards the flat distance - 5 (capped at `fSpeedMax`), direction blends the
    facing (weight `(200 - min(dist, 200)) / 20 + 1`) with the direction to the point, pitch clamped to +-3000,
    and moves. The new location is clamped to the pilot's X/Y box.

## Network

Every function is simulated and the class is bNoDelete with RemoteRole SimulatedProxy, so each machine keeps the map
copy and swims it with its own random numbers (FRand): fish positions differ between machines, which is cosmetic.
The client world ticks its copies (Role SimulatedProxy after the client map setup); the views draw them where they
are: static-mesh fish as gameplay nodes following the actor, skeletal fish as skeletal models looping the sequence
the script last chose.

## Evidence

- `local/re-out/uc/SBase/SFish.uc`, `SFish_Big.uc`, `SFish_Follow.uc`.
- Maps' sim/gameplay.json: 134 SFish, 52 SFish_Big, 58 SFish_Follow; skeletal: 12 per Aqua map (11 SFish_Big, one
  SFish_Follow dolphin), 10 SFish_Big in OffsD.

## Open questions

1. The views loop `AnimsFish` at rate 1 with no tween; LoopAnim's tween into the next sequence is not modelled.
