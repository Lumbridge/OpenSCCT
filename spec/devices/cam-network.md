# SCamNetwork (merc surveillance camera ring)

Units:
- def:SBase.SCamNetwork
- uc:SBase.SCamNetwork:AddMuzzleCount
- uc:SBase.SCamNetwork:SwitchToVision
- uc:SBase.SCamNetwork:Fire
- uc:SBase.SCamNetwork:Tick
- nat:SBase.SCamNetwork.ServerFire
- nat:SBase.SCamNetwork.AuthorizeEnergy
- nat:SBase.SCamNetwork.ComputeEnergyLevel
- rep:SBase.SCamNetwork.MuzzleCount
- rep:SBase.SCamNetwork.iReplicatedVisionMode
- rep:SBase.SCamNetwork.aUsedByPawn
- uc:SBase.SOrientableObject:PostNetBeginPlay
- uc:SBase.SOrientableObject:Tick
- uc:SBase.SOrientableObject:Destroyed
- rep:SBase.SOrientableObject.PitchConstraint
- rep:SBase.SOrientableObject.YawConstraint
- rep:SBase.SOrientableObject.RollConstraint
- rep:SBase.SOrientableObject.iReplicatedPitch
- rep:SBase.SOrientableObject.iReplicatedYaw
- rep:SBase.SOrientableObject.fVisionFOV
- def:SBase.SGadCamNetwork
- uc:SBase.SGadCamNetwork:Use
- uc:SBase.SPlayerController:UseCamNetwork
- uc:SBase.SPlayerController:FreeCurrentCamera
- uc:SBase.SPlayerController:ClampRotation
- uc:SBase.SPlayerController:UpdateOrientableRotation
- uc:SBase.SPlayerController:ServerUpdateOrientableRotation
- uc:SBase.SPlayerController:CamNetworkSetFovOnServer
- uc:SBase.SPlayerController:CamFire
- uc:SBase.SBasePawn:CamNetworkSwitchVisionModeOnServer
- uc:SBase.SBasePawn:CamNetworkSwitchVisionModeOnAllClients

## Purpose

Fixed globe cameras that a merc views from anywhere with the CamNet gadget, stepping around a ring with Next/Prev,
turning within limits, zooming, switching vision modes and firing a shock shot from the camera. SteeE has 3:
Courtyard (first), Backstreet, Hotel stairs. The controller states that drive the view (`s_UseObject_CamNetwork`,
`s_UseObject_StickyCam`, `s_UseObject`) belong to the player-controller spec; this file fixes the camera actor, the
ring, the limits and the shared-use rules.

## Data

| Field | Owner | Type | Default | Meaning |
|---|---|---|---|---|
| `NextCam`, `PrevCam` | SCamNetwork | SCamNetwork | None | ring links |
| `bFirstCam` | SCamNetwork | bool | false | entry camera |
| `CamName` | SCamNetwork | localized string | "No Name" | HUD label |
| `aUsedByPawn` | SCamNetwork | SBasePawn | None | the merc in control |
| `iCurrentVisionMode`, `iReplicatedVisionMode`, `iChangeToThisVisionMode` | SCamNetwork | int | 0, 0, -1 | vision skin state |
| `mOff`, `mOn`, `mMotion`, `mEMF` | SCamNetwork | Material | `DEF_Cam_obj_eteint_fx`, `_standart_fx`, `_motion_fx`, `_emf_fx` | lens skin (`Skins[1]`) |
| `EnergyLevel`, `EnergyLevelMax`, `EnergyNeeded`, `EnergyRefillAmount` | SCamNetwork | | 100, 100, 50, 5 | shot energy |
| `ReloadTime` | SCamNetwork | float | 0.5 | s |
| `BaseDamage` | SCamNetwork | int | 10 | |
| `FireSingleShotSound`, `EmptySound` | SCamNetwork | Sound | `StikyShok_SShot_1`, `StikyShok_vide` | |
| `PitchConstraint`, `YawConstraint`, `RollConstraint` | SOrientableObject | vector (X centre, Y min, Z max) | SStickyCam: pitch (0, -13000, 13000), yaw (0, -14000, 14000) | turn limits |
| `iPitch`, `iYaw`, `iReplicatedPitch`, `iReplicatedYaw` | SOrientableObject | int | | view angles |
| `fVisionFOV`, `fMinFOV`, `fMaxFOV` | SOrientableObject | float | 110 (camnet), 10, 110 | zoom |
| sounds | | | `RotationSound camNet_move`, `ActivationSound`/`DesactivationSound CamNet_In`, `ZoomSoundIn camNet_zoomIn`, `ZoomSoundOut camNet_zoomOut` | |
| class defaults | SCamNetwork | | | `StaticMesh=SGameplayObjects.CamNetwork.Globe`, `InteractionClass=None`, `bNoDelete`, `bAlwaysRelevant`, `RemoteRole=ROLE_SimulatedProxy`, collision 15 x 15 then set to 20 x 20 at PostBeginPlay, `nativereplication` |
| SGadCamNetwork | | | | `ItemName CamNet`, `RefireRate 1.0`, `Charge -10` (meaning of a negative charge is in the gadget spec), `bActivatable` |

## Rules

### Ring and entry

1. `SGadCamNetwork.Use`: in a tutorial level, nothing. If the owner walks (`PHYS_Walking`) and the HUD is not
   jammed (`bPerturbation`): change the controller to the crouched or standing "wait for server" camnet state and
   call `UseCamNetwork(0)` (first camera); then the gadget's parent Use.
2. `UseCamNetwork(iPickCam, bLeaveCamMode)`: first `FreeCurrentCamera`. Leaving: play the current camera's
   deactivation sound, clear `UsedObject`, return. Otherwise only in one of the three camnet controller states:
   `iPickCam` 2 moves to `NextCam`, 1 to `PrevCam` (unchanged when the link is None), 0 to the first camera.
3. First camera search (done once per gadget): iterate all SCamNetwork actors in list order, setting
   `FirstNetworkCam` to each one, stopping at the first with `bFirstCam`. If none has it, the last camera in the
   list is used.
4. On the chosen camera: if nobody controls it (`aUsedByPawn == None`), this merc takes control and the camera's
   vision is set from the merc's goggles (motion tracking: 3 (cycle), EMF: 4, else -2; because of an `if`/`if-else`
   chain a motion-tracking user sends 3 and then also -2). If someone controls it, the merc watches as a passenger.
   Play the activation sound; if alive and not yet in `s_UseObject_CamNetwork`, enter it.
5. `FreeCurrentCamera`: when this merc controls the current camera, hand control to another non-spy pawn using the
   same camera (first found); if none, reset the camera's FOV to the merc's default, switch its vision to -1 (off)
   and clear `aUsedByPawn`.

### Turning and limits

6. `SOrientableObject.PostNetBeginPlay` (simulated): the limit centres take the placed rotation:
   `PitchConstraint.X = Rotation.Pitch`, `RollConstraint.X = Rotation.Roll`, `YawConstraint.X = Rotation.Yaw`;
   `iPitch = iReplicatedPitch = Rotation.Pitch`, `iYaw = iReplicatedYaw = Rotation.Yaw`; play `ActivationSound`
   (volume 1, radius 100), so every network camera plays its activation sound at level start on every machine.
7. While in use (controller `s_UseObject.UpdateRotation`): start from the camera's `iPitch`/`iYaw`; add mouse input
   above a 150 dead zone scaled by `50 * sensitivity * dt * input * DesiredFOV / 110`; wrap both to 0..65535; when a
   limit's Y and Z are both non-zero, clamp with `ClampRotation(X, value, Y, Z)`; a change over 2 units requests the
   rotation sound; then write the result back (`UpdateOrientableRotation`, which also sends it to the server unless
   the camera is the predicted sticky cam; the server stores it in `iReplicatedPitch`/`iReplicatedYaw`).
8. `ClampRotation(Value, Target, Min, Max)` (all wrapped to 0..65535): if `Target > Value`: when the forward gap
   exceeds 32768 the target is below the centre, return `Value - |clamp(Value + 65536 - Target, 0, -Min)|`; else
   `Value + |clamp(Target - Value, 0, Max)|`. If `Value > Target`: when the gap exceeds 32768 return
   `Value + |clamp(Target + 65536 - Value, 0, Max)|`; else `Value - |clamp(Value - Target, 0, -Min)|`. Results are
   wrapped. If `Target == Value` no branch returns: the result is 0.
9. Consequence of rule 8 (quirk to reproduce or decide on): whenever the view sits exactly at a limit centre, the
   clamp returns 0, so pitch or yaw snaps to 0 (level, or world yaw 0) on that frame and stays there while within
   limits. A camera entered with no mouse input snaps at once.
10. `SOrientableObject.Tick` / `SCamNetwork.Tick` (simulated, every machine): for viewers that are not the local
    controlling user, adopt `iReplicatedPitch`/`iReplicatedYaw` (a jump over 50 units in total plays the rotation
    sound); manage the zoom and rotation ambient sounds (stop 0.2 s after the last change); set the actor rotation
    to (`iPitch`, `iYaw`, current roll). SCamNetwork also refills energy (`ComputeEnergyLevel(dt)`), applies a
    changed replicated vision mode, and draws the globe at scale 0.1 for the local user while they look through it
    (so it does not block the view), 1.0 otherwise.

### Vision skins and fire

11. `SwitchToVision(NewMode)` (simulated): mode 3 cycles: from 1 to 2, from 2 to -2, else to 1. If the mode
    changes: lens skin `mMotion` for 1, `mEMF` for 2, `mOn` for -2, `mOff` for -1 and 0; store the mode in all three
    mode fields. If unchanged: reset (`mOff` for -1, else `mOn`) and set the three fields to 0.
    `CamNetworkSwitchVisionModeOnServer` (pawn, client to server) calls `CamNetworkSwitchVisionModeOnAllClients`
    (pawn, server to the owning client), which calls `SwitchToVision`.
12. `Fire(F)` (simulated, controlling merc): if `AuthorizeEnergy(EnergyNeeded)` succeeds: ask the server to play the
    single-shot sound (radius 200) and `CamFire(F, self)` (server call to `ServerFire`); else, if more than
    `ReloadTime` since the last shot, ask the server to play the empty sound (radius 60). `LastShot = TimeSeconds`
    either way. The shot itself (`ServerFire`) is native.
13. Damage to a network camera: the inherited sticky-camera handlers reach through a None owner and then call
    `Destroy` on a `bNoDelete` actor, so shooting or encroaching a network camera has no effect (beyond script
    warnings). Network cameras are indestructible.

## States

None on the camera (the controller states hold the mode).

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| energy max / per shot / refill amount | 100 / 50 / 5 | | decoded (units of refill open) | def:SBase.SCamNetwork |
| reload time | 0.5 | s | decoded | def |
| input dead zone | 150 | | decoded | s_UseObject.UpdateRotation |
| remote rotation sound threshold | 50 | rot units | decoded | Tick |
| sound linger | 0.2 | s | decoded | Tick |
| self-view scale | 0.1 | | decoded | Tick |
| default FOV | 110 | deg | decoded | def |

SteeE ring (Next order): Courtyard (SCamNetwork28756, first, rotation (-2976, -100, 0), default limits pitch and yaw
+-13000 / +-14000) to Backstreet (28757, rotation (-2124, -7828, 0), +-8000 / +-8000) to Hotel stairs (28758,
rotation (-2876, 4664, 0), +-8000 / +-8000) and back to Courtyard; Prev goes the other way.

## Network

- Server: control assignment (`aUsedByPawn`, replicated reliably), `ServerFire`, `ServerUpdateOrientableRotation`,
  `CamNetworkSetFovOnServer`.
- Replicated: limits on `bNetInitial`; `iReplicatedPitch`/`iReplicatedYaw` to non-owners; `fVisionFOV`;
  `MuzzleCount`, `iReplicatedVisionMode`, `aUsedByPawn`.
- `bAlwaysRelevant`, so every client knows every camera.

## Edge cases

- Two mercs on one camera: the first controls it; the second watches. When the first leaves, control passes to the
  watcher.
- A spy can never be handed control (the hand-over skips spies), though spies do not get the gadget anyway.
- The vision cycle while the first merc uses motion tracking sends two mode changes in one call (rule 4).

## Evidence

- `local/re-out/uc/SBase/SCamNetwork.uc`, `SStickyCam.uc`, `SOrientableObject.uc`, `SGadCamNetwork.uc`,
  `SPlayerController.uc` (UseCamNetwork, FreeCurrentCamera, ClampRotation, s_UseObject.UpdateRotation,
  s_UseObject_CamNetwork), `SBasePawn.uc` (CamNetworkSwitchVisionMode*).
- SteeE.sdc: `SCamNetwork28756`, `28757`, `28758`.
- RE+ MapBuilderFacts.md: stock network cameras form one ring by NextCam/PrevCam with one `bFirstCam`; opened from
  anywhere with the CamNet gadget.
- Natives: ServerFire `0x10ae9030`, AuthorizeEnergy `0x10ae9510`, ComputeEnergyLevel `0x10ae8fb0`.

## Open questions

1. Native energy rules: refill per second or per tick, `AuthorizeEnergy` deduction, and what `ServerFire` shoots
   (sticky-shocker style stun with `BaseDamage` 10?). Scenario: fire from a camera at a spy, record damage and
   energy over time.
2. Confirm the centre snap of rule 9 in game: enter the Backstreet camera without moving the mouse and read its yaw.
3. `CamNetworkSwitchVisionModeOnAllClients` is a server-to-owner call: other mercs watching the same camera may not
   see the lens skin change. Scenario: two mercs on one camera, one switches vision.
