# Native property lists (GetOptimizedRepList) against the script conditions

Units: eng:AActor::GetOptimizedRepList, eng:AController::GetOptimizedRepList, eng:APlayerController::GetOptimizedRepList, eng:AMover::GetOptimizedRepList, eng:AReplicationInfo::GetOptimizedRepList, eng:APlayerReplicationInfo::GetOptimizedRepList, eng:AGameReplicationInfo::GetOptimizedRepList, eng:ATeamInfo::GetOptimizedRepList, eng:ALevelInfo::GetOptimizedRepList, eng:AInventory::GetOptimizedRepList, eng:ASPlayerController::GetOptimizedRepList, eng:ASBasePawn::GetOptimizedRepList, eng:ASPawnAttaque::GetOptimizedRepList, eng:ASPAWNDEFENSE::GetOptimizedRepList, eng:ASBasicGamePlayObject::GetOptimizedRepList, eng:ASGamePlayObject::GetOptimizedRepList, eng:ASInterrupteur::GetOptimizedRepList, eng:ASAnimatedMesh::GetOptimizedRepList, eng:ASObjectiveTrigger::GetOptimizedRepList, eng:ASComputerObjectiveTrigger::GetOptimizedRepList, eng:ASFlag::GetOptimizedRepList, eng:ASFlagDropZone::GetOptimizedRepList, eng:ASDisk::GetOptimizedRepList, eng:ASObjective::GetOptimizedRepList, eng:ASMission::GetOptimizedRepList, eng:ASInteractionManager::GetOptimizedRepList, eng:ASInteractObject::GetOptimizedRepList, eng:ASTriggerInteraction::GetOptimizedRepList, eng:ASOrientableObject::GetOptimizedRepList, eng:ASStickyCam::GetOptimizedRepList, eng:ASCamNetwork::GetOptimizedRepList, eng:ASTriggerLight::GetOptimizedRepList, eng:ASWeapon::GetOptimizedRepList, eng:ASWeaponAttaque::GetOptimizedRepList, eng:ASGadget::GetOptimizedRepList, eng:ASGadTorche::GetOptimizedRepList, eng:ASWeaponDefenseDummy::GetOptimizedRepList, eng:ASProjGrenade::GetOptimizedRepList, eng:ASSmokeEmitter::GetOptimizedRepList, eng:ASTeamInfo::GetOptimizedRepList, eng:ASAmmoStock::GetOptimizedRepList, eng:ASAmbientSoundTrigger::GetOptimizedRepList, eng:ASLiftDoor::GetOptimizedRepList, eng:ASGoggleBeam::GetOptimizedRepList

## Purpose

All 59 classes with replicated variables carry `NativeReplication`, so the script conditions of
their own variables are never evaluated: a native list per class decides what is sent. This file
says, class by class, what each native list sends and where it differs from the script block
(`spec/net/rep-*.md`). Where nothing is listed, the native list sends exactly the script set under
the script conditions. The port should implement these native rules, not the script text.

## Data

A list runs once per update of one actor on one channel (`net-replication.md` rule 13), with the
flags of that update: `bNetInitial` (first bunch), `bNetDirty` (the actor changed since the
channel's last update, or first bunch), `bNetOwner` (this connection owns it), `RemoteRole` after
the autonomous downgrade. A property is pushed only when its value differs from the channel's
`Recent` copy; object properties that cannot be mapped yet set `bActorMustStayDirty`.

"Every update" below means "every update, when the value changed". Most lists first call their
parent list; a class marked "no parent" does not, and then sends nothing else (not even Actor
properties).

## Rules

### Engine.Actor (base of every list)

1. Only on the authority. Always, also for `bSkipActorPropertyReplication` actors:
   `bBlockNonZeroExtentTraces`, `bBlockZeroExtentTraces`.
2. First bunch only: `bCollideActors`, `bCollideWorld`, and the vision group `bEMFIsOn`,
   `EMFOpacity`, `EMFIntensity`, `HeatIntensity`, `bUnlit`, `bIgnoreEchelonLighting`,
   `bIgnoreDynamicEchelonLighting`, `bSpecialLit`. **Differs:** script sends the vision group on
   every update; natively later changes reach clients only for classes that resend them (pawns,
   objective triggers, weapons, trigger lights, below).
3. A `bSkipActorPropertyReplication` actor stops here after its first bunch (as in script).
4. Movement, when `bReplicateMovement` or first bunch:
   - autonomous proxy: `Rotation` in the first bunch only, for draw types mesh, static mesh and
     particle;
   - otherwise, unless `bCompressedPosition`: `Location` when the actor just lost its `Base`; when it
     has no base or a world base and is a dumb proxy, or has `bUpdateSimulatedPosition`, or in the
     first bunch: `Location` on later updates only (the first bunch carries the location in the spawn
     information), and `Rotation` (mesh, static mesh, particle) on every update and in the first
     bunch unless the class has `bNetInitialRotation`;
   - attached to a non-world base: `RelativeLocation`, `RelativeRotation`, `AttachmentBone`, and
     `Location` when it just became attached;
   - `PHYS_Rotating`: `bFixedRotationDir`, `bRotateToDesired`, `RotationRate`, `DesiredRotation`;
   - simulated proxy: `Velocity` in the first bunch or with `bUpdateSimulatedPosition` (not with
     `bCompressedPosition`), `Physics` in the first bunch only;
   - dumb proxy: `Physics` every update, `Velocity` while falling.
   **Differs:** `Base` is never sent by the Actor list (script: always to proxies); classes that need
   it send it themselves (pickable objects, flag, disk, lift doors, pawns). `Location` and
   `Rotation` are never in a map actor's first bunch.
5. First bunch only, and not to the owner of a `bClientAnim` actor: `AmbientSound`, and with an
   ambient sound `SoundRadius`, `SoundVolume`, `SoundPitch`, `SoundOcclusion`. **Differs:** script
   sends them on every update; `SoundBone` is never sent.
6. Only when `bNetDirty`: `DrawScale`, `DrawType`, `RepSkin`; with collision on `bProjTarget`,
   `bBlockActors`, `bBlockPlayers`; unless skip, `Owner` **to the owning connection only** (script:
   to everyone) and `Instigator` with `bReplicateInstigator`; for `DT_Mesh`: `Mesh` (unless
   `bNoRepMesh`) and `SimAnim` (with `bReplicateAnimations`); for `DT_StaticMesh`: `StaticMesh` in
   the first bunch only (script: never); `LightType` and, with a light type, the light group; the
   light beam group.
7. First bunch only: `bHidden`, `Role`, `RemoteRole`, `bNetOwner`, `bTearOff`. **Differs:** script
   sends them on every update of non-skip actors. Classes that need later `bHidden` or `bTearOff`
   changes resend them (below).
8. Never: `AmbientGlow` (script: every update), `SoundBone`.

### Controllers and infos

9. **Controller** (dirty only): `PlayerReplicationInfo`, `Pawn`, `PawnClass` to the owner; every
   update: `StartSpot`. **PlayerController** adds, on every update, `Role` (when the
   last sent value was not `ROLE_Authority`) and `RemoteRole`, and to the owner when dirty
   `GameReplicationInfo` and `m_szGlobalID`. **SPlayerController**: the 12 script variables, all on
   every update with no `bNetOwner` test (the controller is only relevant to its owner anyway),
   `MyDesiredRotation` in the first bunch only, `SpecTarget` forced once when
   `bForceReplicationForSpecTarget` is set (the flag is then cleared).
10. **PlayerReplicationInfo, GameReplicationInfo, TeamInfo, STeamInfo**: same variables and conditions
    as script (arrays element by element), except that the GRI never sends `VoiceChatMode`,
    `ServerRegion` or `ShortName`. Their send rate is set by the frequency gate
    (`net-replication.md` rule 4).
11. **LevelInfo**: when dirty `Pauser`, `TimeDilation`, `NbPointsAttack`, `NbPointsDefense`; every
    update `GEActivationMask`. **Differs:** `DefaultGravity` is never sent.

### Pawns

12. **SBasePawn** (also `SPawnAttaque` adding `NbPointsRefillable` and `WeaponDummy`, and
    `SPAWNDEFENSE` adding `bLaserOn`, `Torche`, `TorcheLaser`, `WeaponDummy`), only on the authority:
    - every update: `PoisonnedBy`, `bGasMask`, `bPoisonned`, `UsedObject`, `Role`, `RemoteRole`,
      `bTearOff`, the vision group (`bEMFIsOn`, `EMFOpacity`, `EMFIntensity`, `HeatIntensity`),
      `bHidden`, `AmbientSound`, `HandItem`, `CarriedFlag`, `m_AttackedBy`, `m_AttackTarget`,
      `m_InteractionTarget`, `m_PawnToTerminate`, `bCanBeGrabbed`, `bSpyBulletOn`, `iTeamSpying`,
      `aGadgetType[4]`, the two team mates' backpack arrays and `aNbChargesGad[4]`;
    - first bunch only: `WeaponLoaded`, `bIsCrouched`, `m_PawnNoMoreGrabbed`, `PipHFire`, `BTW`,
      `IK_Type`, `bBeginSpecial12Anim`, `Physics`, and, to non-owners, `AllActions[6]`,
      `iNbActionForDistantClient`, `fRepTimeOfLastActionChange`, `ZoneConfigForClient`;
    - to the owner: `MyWeapon`, and `MyGadget` while `replicateGadgets` is set (one shot: the flag is
      cleared after sending);
    - to non-owners (simulated proxies), every update: `Base`; then the movement block of
      `net-proxy.md` rules 1 to 6 (position by RPC, `Rotation`, `iDesiredYawPitch`,
      `iYawPitchFromView` as properties at most every 0.09 s or on large yaw changes); it sets
      `bCompressedPosition`, so the Actor list never sends `Location` or `Velocity` for pawns;
    - at most every 0.09 s per channel (and on tear-off): the Actor list, then `Health`,
      `PlayerReplicationInfo`, `Controller` (to the owner, or to clear it), `HitDamageType`,
      `TakeHitLocation`, `AnimAction`, `bSimulateGravity`, `TearOffMomentum`; between those updates
      only `PlayerReplicationInfo` is checked.
    **Differs:** the Pawn class list is bypassed, so `PawnPosition` (the RPC path replaces it),
    `bIsWalking`, `bIsTyping` and the owner-only movement tuning (`GroundSpeed`, `AccelRate`,
    `AirSpeed`, `JumpZ`, `AirControl`) are never sent, and `bIsCrouched` goes in the first bunch only;
    `aGadgetType`, `AllActions` and the block-4 aim variables follow the timing above rather than the
    script's every-update conditions. The server's per-move `GroundSpeed` (`net-moves.md` rule 11.6)
    therefore stays on the server.

### Map objects

13. **SBasicGamePlayObject** (and every subclass): `AmbientGlow` in the first bunch,
    `bSecondaryObjective`, `InteractionObj` and, for `DT_StaticMesh`, **`StaticMesh` on every update**.
    This is how broken glass (`SVitre`, mesh set to none) and other mesh swaps reach clients; the
    client applies the change with `SetStaticMesh` and drops collision when the mesh is none
    (`net-replication.md` rule 24).
14. **SGamePlayObject**: adds `Base` for `bPickable` objects.
15. **SInterrupteur**: as script.
16. **SAnimatedMesh**: Actor list plus `bHidden` on every update. Its playback reaches clients
    through `SimAnim` (dirty, mesh draw type, `bReplicateAnimations`).
17. **SObjectiveTrigger**: the vision group on every update; `bActive`, `bLocked` on every update;
    **`CaptureTime`, `TriggerTime`, `Objective`, `bTriggering` only while `bActive`** (script:
    always).
18. **SComputerObjectiveTrigger**: `bHidden`, `bCollideActors`, `bBlockPlayers` on every update;
    `UseRelativeRotation`, `UseRelativeLocation`, `DominationCaptureTime` first bunch;
    **`DeltaCaptureTime` while `bActive` and `SkinState` on every update** (script: both first
    bunch only).
19. **SFlag**: `Base`, `bHidden`, `bTriggering`, `FlagCarrier`, `bResetZeroValues` on every update
    (script: `FlagCarrier` first bunch only), `DropZone[5]` first bunch.
20. **SDisk**: `Base`, `bHidden`, `bTriggering` on every update (no script variables).
21. **SFlagDropZone** (no parent): only `bTriggering`. **Differs:** the inherited `bActive`, `bLocked`,
    `CaptureTime`, `TriggerTime`, `Objective`, `bSecondaryObjective` and `InteractionObj` are never
    sent for drop zones.
22. **SObjective** (no parent) and **SMission**: their script variables (`ObjectiveTaken`,
    `MinTriggerTime`, `iSimulatedObjectiveIsBeingTookUntil`, `ObjectivesLeft`, `TotalObjectivesLeft`)
    and nothing else.
23. **SInteractionManager**: `Interactions[15]` and `NbObjects`. **SInteractObject**: as script,
    `ItRadius`/`ItHeight` first bunch only (script: always). **STriggerInteraction**,
    **SComputerInteraction**, **SPickupInteraction**, **SFlagDropZoneInteraction**: as script.
24. **SOrientableObject**: as script, except that `fVisionFOV` is sent only by camera-network
    objects and only to connections whose pawn is not the object's current user. **SStickyCam**: as
    script. **SCamNetwork**: `aUsedByPawn`, `MuzzleCount`; **`iReplicatedVisionMode` is never sent**
    (vision mode reaches clients only through the broadcast `CamNetworkSwitchVisionModeOnAllClients`).
25. **STriggerLight**: the vision group on every update, and `LightType` once each time
    `bForceReplicationForCurrentLightType` is set (cleared after sending).
26. **Mover**: `BaseRot` first bunch, `SimOldPos`, `SimInterpolate`, `RealPosition`, `AmbientSound`
    and `Velocity` on every update. **Differs:** `BasePos` is never sent (it is sent by `SLiftDoor`,
    together with `Base`). `SLift` adds nothing.
27. **SAmmoStock** (no parent): first bunch only, `bIgnoreEchelonLighting` (sent when it did *not*
    change, an inverted test), `bIgnoreDynamicEchelonLighting`, `bSpecialLit`; the inherited
    `bSecondaryObjective` and `InteractionObj` are never sent.
    **SAmbientSoundTrigger** and **SSmokeEmitter** (no parent): `AmbientSound` only.
    **SZoneTips**, **Effects**, **Projector**: Actor list only.

### Inventory

28. **Inventory**: `Charge` to the owner when dirty (as script). **SWeapon**: the vision group on
    every update, `Controller` to the owner. **SWeaponAttaque**, **SGadget**, **SGadTorche**,
    **SWeaponDefenseDummy**, **SProjGrenade**: as script. **SGoggleBeam**: Actor list plus `bHidden`
    on every update.

## States

None.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Pawn full-list interval | 0.09 | s | decoded | editor 0x1120c9d0 |
| Interaction manager slots | 15 | | decoded | editor 0x11222390 |
| Flag drop zones | 5 | | decoded | editor 0x111cf040 |
| Pawn gadget type slots | 4 | | decoded | editor 0x1120c9d0 |

## Network

Server only. The client applies what arrives (`net-replication.md` rules 23 to 25).

## Edge cases

- An actor whose list has no parent (rule 21, 22, 27) never sends `Role`, `RemoteRole`, `bHidden`
  or any movement; its client copy keeps its spawn or map values.
- Because `bHidden` is first-bunch only in the Actor list, hiding or showing a plain actor after its
  channel opened is invisible to clients unless its class resends `bHidden`.
- `StaticMesh` changes of non-gameplay actors (for example a `StaticMeshActor` subclass) do not
  replicate after the first bunch.

## Evidence

- Editor lists (guard-named): `AActor` 0x110a8610, `AController` 0x110aab30, `AMover` 0x110aaeb0,
  `AMoverRotating` 0x110ab320, `AReplicationInfo` 0x110ab5f0, `AInventory` 0x110ab8f0, `APlayerController`
  0x110ad0a0, `ALevelInfo` 0x110ad3e0, `APlayerReplicationInfo` 0x110ad770, `AGameReplicationInfo`
  0x110aef60, `ATeamInfo` 0x110b0ae0, `ASInteractObject` 0x111bc900, `ASTriggerInteraction`
  0x111bd910, `ASAmmoStock` 0x111c0630, `ASAmbientSoundTrigger` 0x111c0520, `ASBasicGamePlayObject`
  0x111c1830, `ASGamePlayObject` 0x111c1dc0, `ASOrientableObject` 0x111c3cb0, `ASTeamInfo`
  0x111c4130, `ASObjectiveTrigger` 0x111ce270, `ASDisk` 0x111ce9c0, `ASFlag` 0x111cf040,
  `ASFlagDropZone` 0x111cf480, `ASComputerObjectiveTrigger` 0x111cf840, `ASStickyCam` 0x111d0040,
  `ASCamNetwork` 0x111d0300, `ASInterrupteur` 0x111d1620, `ASAnimatedMesh` 0x111d2790, `ASLiftDoor`
  0x111d36b0, `ASTriggerLight` 0x111d3900, `ASWeapon` 0x111d5fb0, `ASWeaponAttaque` 0x111d97a0,
  `ASGadget` 0x111da6a0, `ASGadTorche` 0x111dff60, `ASSmokeEmitter` 0x111e07c0, `ASProjGrenade`
  0x111e0ec0, `ASPlayerController` 0x111eecc0, `ASBasePawn` 0x1120c9d0, `ASPAWNDEFENSE` 0x1121efe0,
  `ASPawnAttaque` 0x1121f7a0, `ASObjective` 0x1121fca0, `ASMission` 0x11220640,
  `ASInteractionManager` 0x11222390, `ASGoggleBeam` 0x11222a90.
- Game copies (vtable slot 23): `AActor` 0x10988610 (same structure as the editor's, checked block by
  block), `AMover` 0x1098aeb0, `APlayerReplicationInfo` 0x1098b700, `AGameReplicationInfo`
  0x1098ca70, `ALevelInfo` 0x1098b440, `ASPlayerController` 0x10aaba30, `ASPawnAttaque` 0x10af5bc0,
  `ASBasicGamePlayObject` 0x10a8f500, `ASGamePlayObject` 0x10a8f8f0, `ASComputerObjectiveTrigger`
  0x10abd9f0, `ASFlag` 0x10ae95f0, `ASFlagDropZone` 0x10ae9950, `ASDisk` 0x10abfba0,
  `ASAnimatedMesh` 0x10ab7360, `ASInterrupteur` 0x10aea2a0, `ASCamNetwork` 0x10ae8e90,
  `ASTriggerLight` 0x10ab9210, `ASObjective` 0x10a9f100, `ASMission` 0x10abfe90,
  `ASInteractionManager` 0x10abeb70, `ASInteractObject` 0x10af67f0, `ASTriggerInteraction`
  0x10af6450, `ASWeaponAttaque` 0x10ab7740, `ASTeamInfo` 0x10a931b0, `ASGoggleBeam` 0x10a9a240.
- Property names were taken from the name index each list passes when it looks up its properties,
  and from the compared offsets against `local/reflect/game.json`.

## Open questions

1. Confirm in a capture that a plain actor's `bHidden` change after channel open never reaches a
   client (for example an `SMagicEvent`-hidden decoration in SteeE).
2. The game copies of the lists listed above were matched by vtable slot and spot-checked; a full
   per-class diff of game against editor bodies is still to do for the gadget and interaction
   classes.
