# Objective and interaction replication

Units: def:SBase.SObjective, def:SBase.SMission, def:SBase.SObjectiveTrigger, def:SBase.SComputerObjectiveTrigger, def:SBase.SFlag, def:SBase.SFlagDropZone, def:SBase.SDisk, def:SBase.SInteractObject, def:SBase.STriggerInteraction, def:SBase.SComputerInteraction, def:SBase.SFlagDropZoneInteraction, def:SBase.SPickupInteraction, def:SBase.SInteractionManager, rep:SBase.SObjective.AddRemoveObjectiveOnClientHud, rep:SBase.SObjective.MinTriggerTime, rep:SBase.SObjective.ObjectiveTaken, rep:SBase.SObjective.iSimulatedObjectiveIsBeingTookUntil, rep:SBase.SMission.ObjectivesLeft, rep:SBase.SMission.TotalObjectivesLeft, rep:SBase.SObjectiveTrigger.AddRemoveObjectiveForRadar, rep:SBase.SObjectiveTrigger.CaptureTime, rep:SBase.SObjectiveTrigger.Objective, rep:SBase.SObjectiveTrigger.TakeObjective, rep:SBase.SObjectiveTrigger.TriggerTime, rep:SBase.SObjectiveTrigger.bActive, rep:SBase.SObjectiveTrigger.bLocked, rep:SBase.SObjectiveTrigger.bTriggering, rep:SBase.SComputerObjectiveTrigger.DeltaCaptureTime, rep:SBase.SComputerObjectiveTrigger.DominationCaptureTime, rep:SBase.SComputerObjectiveTrigger.SkinState, rep:SBase.SComputerObjectiveTrigger.UseRelativeLocation, rep:SBase.SComputerObjectiveTrigger.UseRelativeRotation, rep:SBase.SFlag.DropZone, rep:SBase.SFlag.FlagCarrier, rep:SBase.SFlag.SetFlagCarrier, rep:SBase.SFlag.bResetZeroValues, rep:SBase.SInteractObject.ChangePresenceDetected, rep:SBase.SInteractObject.ItHeight, rep:SBase.SInteractObject.ItRadius, rep:SBase.SInteractObject.bInUse, rep:SBase.SInteractObject.bInteractInFront, rep:SBase.SInteractObject.bInteractInFrontWithCollisions, rep:SBase.SInteractObject.bPresenceDetected, rep:SBase.SInteractObject.bUsable, rep:SBase.SInteractObject.iLastUsedTime, rep:SBase.SInteractObject.iReusableTime, rep:SBase.STriggerInteraction.AddRemoveActorOnClientHud, rep:SBase.STriggerInteraction.MinTriggerTime, rep:SBase.STriggerInteraction.OwnerInterrupteur, rep:SBase.STriggerInteraction.bHackable, rep:SBase.STriggerInteraction.bMaintain, rep:SBase.SComputerInteraction.ObjectiveTrigger, rep:SBase.SFlagDropZoneInteraction.MyOwner, rep:SBase.SPickupInteraction.bCanPickup, rep:SBase.SInteractionManager.AddObjetOnClient, rep:SBase.SInteractionManager.Interactions, rep:SBase.SInteractionManager.NbObjects, rep:SBase.SInteractionManager.RemoveInteractionObjOnClient, uc:SBase.SObjective:SimulateBeingTook, uc:SBase.SObjective:AddRemoveObjectiveOnClientHud, uc:SBase.SObjective:Ev_AddRemoveObjectiveOnClientHud, uc:SBase.SObjectiveTrigger:Triggering, uc:SBase.SObjectiveTrigger:AddRemoveObjectiveForRadar, uc:SBase.SObjectiveTrigger:TakeObjective, nat:SBase.SObjectiveTrigger.GetTriggerTime, nat:SBase.SObjectiveTrigger.IsActive, uc:SBase.SFlag:SetFlagCarrier, uc:SBase.SFlag:NotifyPickup, uc:SBase.SFlag:Throw, uc:SBase.SFlag:CheckDropZone, uc:SBase.SFlag:PostBeginPlay, uc:SBase.SFlagDropZone:PostBeginPlay, uc:SBase.SInteractObject:ChangePresenceDetected, uc:SBase.SInteractObject:PostBeginPlay, uc:SBase.SInteractObject:PostNetBeginPlay, uc:SBase.SInteractObject:Touch, nat:SBase.SInteractObject.DoTouch, nat:SBase.SInteractObject.CanUse, uc:SBase.SInteractObject:InitInteract, uc:SBase.STriggerInteraction:AddRemoveActorOnClientHud, uc:SBase.STriggerInteraction:Interact, uc:SBase.STriggerInteraction:InitInteract, uc:SBase.STriggerInteraction:s_Maintain_Use.BeginState, uc:SBase.STriggerInteraction:s_Maintain_Use.EndState, uc:SBase.STriggerInteraction:s_Maintain_Use.InitInteract, uc:SBase.STriggerInteraction:s_Maintain_Use.PostInteract, uc:SBase.SComputerInteraction:InitInteract, uc:SBase.SFlagDropZoneInteraction:InitInteract, uc:SBase.SPickupInteraction:InitInteract, uc:SBase.SInteractionManager:PostBeginPlay, uc:SBase.SInteractionManager:AddObjetOnClient, uc:SBase.SInteractionManager:Ev_AddObjetOnClient, uc:SBase.SInteractionManager:RemoveInteractionObjOnClient, uc:SBase.SInteractionManager:Ev_RemoveInteractionObjOnClient, nat:SBase.SInteractionManager.AddInteractionObj, nat:SBase.SInteractionManager.AddObjectAtIndex, nat:SBase.SInteractionManager.RemoveObject, nat:SBase.SInteractionManager.RemoveInteractionObj, uc:SBase.SPlayerController:InteractionInit, uc:SBase.SPlayerController:Interaction, uc:SBase.SPlayerController:InteractionPost, uc:SBase.SPlayerController:DiscardFlag, uc:SBase.SPlayerController:DiscardPickup, uc:SBase.SPlayerController:DestroyGPO, eng:ASObjective::GetOptimizedRepList, eng:ASMission::GetOptimizedRepList, eng:ASObjectiveTrigger::GetOptimizedRepList, eng:ASComputerObjectiveTrigger::GetOptimizedRepList, eng:ASFlag::GetOptimizedRepList, eng:ASFlag::IsNetRelevantFor, eng:ASFlagDropZone::GetOptimizedRepList, eng:ASDisk::GetOptimizedRepList, eng:ASInteractObject::GetOptimizedRepList, eng:ASTriggerInteraction::GetOptimizedRepList, eng:ASComputerInteraction::GetOptimizedRepList, eng:ASFlagDropZoneInteraction::GetOptimizedRepList, eng:ASPickupInteraction::GetOptimizedRepList, eng:ASInteractionManager::GetOptimizedRepList

## Purpose

How the Story mode pieces of SteeE reach clients: missions and objectives (`SMission`, `SObjective`),
the hackable terminals (`SComputerObjectiveTrigger`), the disk (`SFlag` "Secret Files") and its drop
zone (`SFlagDropZone`), the switches' interaction objects (`STriggerInteraction`), and the per-player
interaction list (`SInteractionManager`). All game logic runs on the server; clients get state through
replicated variables, broadcast calls (`model.md` rule 10) and the player's own mirrored states.

## Data

| Class | RemoteRole | bNoDelete | bAlwaysRelevant | NetUpdateFrequency | NetPriority | Other |
|---|---|---|---|---|---|---|
| `SObjective`, `SMission` | DumbProxy | yes | yes | 100 | 1.0 | hidden, no movement |
| `SObjectiveTrigger`, `SComputerObjectiveTrigger` | SimulatedProxy | yes | yes | 5 | 1.4 | skip actor properties |
| `SFlagDropZone` | SimulatedProxy | yes | yes | 5 | 1.4 | skip actor properties |
| `SFlag`, `SDisk` | SimulatedProxy | yes | no (native test) | 100 | 1.4 | movement replicated, `bUpdateSimulatedPosition` |
| `SInteractObject` and subclasses | SimulatedProxy | no (spawned) | no; `SPickupInteraction` yes | 100 | 1.0 | hidden, based on their owner |
| `SInteractionManager` | SimulatedProxy | no (spawned per player) | no | 100 | 1.0 | owned by the player controller |

SteeE instances: 3 `SMission`, 6 `SObjective`, 5 `SComputerObjectiveTrigger`, 1 `SFlag`, 1
`SFlagDropZone`, 30 `SDisk` (from the map exports).

## Rules

1. **Objective state.** `SObjective` replicates `ObjectiveTaken`, `MinTriggerTime` and
   `iSimulatedObjectiveIsBeingTookUntil` (float, server time until which the HUD shows "being taken").
   `SimulateBeingTook` sets it to `Level.TimeSeconds + Triggers[0].GetTriggerTime()` (or + 5.0 without
   triggers) and, when `bShowStatusOnHud`, broadcasts `AddRemoveObjectiveOnClientHud(true)`.
   `SMission` replicates `TotalObjectivesLeft` and `ObjectivesLeft`.
2. **HUD broadcast.** `AddRemoveObjectiveOnClientHud(Add)` (reliable, simulated) runs on every client
   and on the listen host: `Level.GetLocalPlayerController().myHUD.AddRemoveActorOnHud(self, Add)`. On a
   dedicated server it fails harmlessly (no local controller). `Ev_AddRemoveObjectiveOnClientHud` is the
   same call raised by native code.
3. **Trigger state.** `SObjectiveTrigger` replicates `bLocked`, `bTriggering`, `bActive`,
   `CaptureTime`, `TriggerTime` (server `PreciseElapsedTime` when triggering began, 0 otherwise) and
   `Objective`. `Triggering(Status)` (server) sets `bTriggering`, broadcasts the objective HUD add when
   starting, stamps `TriggerTime`, and broadcasts `AddRemoveObjectiveForRadar(Status)` except for bomb
   targets, then calls `Objective.NotifyTriggering()` (native). `AddRemoveObjectiveForRadar(bAdd)`
   (reliable, simulated) acts only where the local player's pawn is a merc: add or remove the trigger on
   the merc radar HUD.
4. **Terminal values.** `SComputerObjectiveTrigger` sends `SkinState`, `DominationCaptureTime`,
   `DeltaCaptureTime`, `UseRelativeLocation`, `UseRelativeRotation` in the first bunch only. Live
   progress therefore reaches clients only through `CaptureTime`, `TriggerTime` and `bTriggering` of
   rule 3 and the objective's `iSimulatedObjectiveIsBeingTookUntil`.
5. **Disk carrier.** `SFlag` sends `FlagCarrier` and `DropZone[5]` in the first bunch, `bResetZeroValues`
   always, and broadcasts `SetFlagCarrier(taken)` (reliable, simulated: sets `FlagTaken` on each
   client) on pickup (`NotifyPickup`) and drop (`Throw`). The carrying pawn's `CarriedFlag` replicates
   always (`rep-pawn.md`), and the attached disk follows through `Base`/`AttachmentBone`/relative
   movement (Actor movement group; `SFlag` keeps actor properties). The drop zone's lid
   (`SFlagDropZone.Cover`, a spawned `SBasicGamePlayObject` in `PHYS_Rotating` with
   `bRotateToDesired` and `RotationRate` (20000, 0, 0)) turns on clients from the replicated rotation
   fields; `SFlag` spawns a second helper, the server rack (`SBasicGamePlayObject` with a static mesh),
   made `ROLE_SimulatedProxy` at run time.
6. **Interaction objects.** Each interactive map object spawns its `SInteractObject` subclass on the
   server in `PostBeginPlay` (owner = the object; the object keeps it in `InteractionObj`, replicated).
   The interaction object sets its base to the owner, so under the UE2 rule it is relevant whenever its
   owner is. It replicates `iLastUsedTime`, `iReusableTime`, `bPresenceDetected`,
   `bInteractInFrontWithCollisions`, `bInteractInFront`, `bUsable`, `bInUse`, `ItHeight` and
   `ItRadius`; clients re-apply the collision size in `PostNetBeginPlay`. `CanUse` and `DoTouch` are
   native and simulated, so each client decides locally what it may use.
   `ChangePresenceDetected(b)` is declared client to server but only an owner could send it, which no
   player is; in practice the server sets `bPresenceDetected` itself.
7. **Subclass members.** `STriggerInteraction`: `MinTriggerTime`, `OwnerInterrupteur`, `bMaintain`,
   `bHackable` and the broadcast `AddRemoveActorOnClientHud(Instigator, Add)` (add on the
   instigator's HUD, remove on each local player's HUD). `SComputerInteraction`: `ObjectiveTrigger`.
   `SFlagDropZoneInteraction`: `MyOwner` (set in the server's `PostNetBeginPlay`). `SPickupInteraction`:
   `bCanPickup` (and `bAlwaysRelevant`, movement replicated).
8. **Interaction protocol** (player controller, reliable, client to server): the client chooses an
   interaction with its local `SInteractionManager`, then sends `InteractionInit(Obj, Index)` (server:
   `Obj.iLastUsedTime = int(Level.TimeSeconds)`, `Obj.InitInteract(PC, Index)`), later
   `Interaction(Obj, Index)` (server: `Obj.Interact(PC, Index)`) and `InteractionPost(Obj, Index)`
   (server: `Obj.PostInteract(PC, Index)`). The server answers by forcing the player's state with
   `MirrorState` (for example `s_Computer_GoTo`, `s_Hack_GoTo`, `s_Maintain_GoTo`,
   `s_FlagAndDropZone_GoTo`, `s_Pickup_GoTo`, or back to `PreviousState` when refused) and changes the
   object's own state with `Actor.MirrorState` (`s_Hacking`, `s_Maintain_Use`), which reaches clients by
   broadcast. The server does not re-run `CanUse` in script before `InitInteract`; the per-object
   `InitInteract` checks (terminal active and unlocked, pawn alive, carrying the disk) are the only
   gate.
9. **Interaction manager.** Spawned on the server by `Possess` (`Spawn(class'SInteractionManager',
   self, ...)`, owner = the controller) and replicated to its owner only through
   `SPlayerController.IManager`. `PostBeginPlay` (simulated, so on both sides) spawns the five built-in
   interactions (leave, share object, heal mate, remove spy bullet, backpack) and adds 5 to `NbObjects`.
   Server natives add and remove touched objects and tell the owner with `AddObjetOnClient(Obj, i)` and
   `RemoveInteractionObjOnClient(Obj)` (reliable, simulated; also raised natively as
   `Ev_AddObjetOnClient` and `Ev_RemoveInteractionObjOnClient`), which call the local natives
   `AddObjectAtIndex` and `RemoveObject`. `NbObjects` and `Interactions` replicate to the owner.
10. **Dropping and damaging.** `DiscardFlag()` (client to server) broadcasts message 11 unless the
    objective is taken and throws the disk with the pawn's velocity; `DiscardPickup()` throws the held
    object; `DestroyGPO(gpo, Damage, Type)` applies `gpo.TakeDamage(Damage, Pawn, 0, (0, 0, 1), Type)`
    on the server without checks.


#### SBase.SObjective (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `iSimulatedObjectiveIsBeingTookUntil` | var | float | `Role == Authority` | S to C | | |
| 0 | `ObjectiveTaken` | var | bool | `Role == Authority` | S to C | | |
| 0 | `MinTriggerTime` | var | float | `Role == Authority` | S to C | | |
| 1 | `AddRemoveObjectiveOnClientHud` | func | bool | `Role == Authority` | S to C | R | S |

#### SBase.SMission (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `TotalObjectivesLeft` | var | int | `Role == Authority` | S to C | | |
| 0 | `ObjectivesLeft` | var | int | `Role == Authority` | S to C | | |

#### SBase.SObjectiveTrigger (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `bLocked` | var | bool | `Role == Authority` | S to C | | |
| 0 | `bTriggering` | var | bool | `Role == Authority` | S to C | | |
| 0 | `bActive` | var | bool | `Role == Authority` | S to C | | |
| 0 | `CaptureTime` | var | int | `Role == Authority` | S to C | | |
| 0 | `TriggerTime` | var | float | `Role == Authority` | S to C | | |
| 0 | `Objective` | var | SObjective | `Role == Authority` | S to C | | |
| 1 | `TakeObjective` | func | - | `Role < Authority` | C to S | R |  |
| 2 | `AddRemoveObjectiveForRadar` | func | bool | `Role == Authority` | S to C | R | S |

#### SBase.SComputerObjectiveTrigger (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `SkinState` | var | byte | `bNetInitial && Role == Authority` | S to C | | |
| 0 | `DominationCaptureTime` | var | int | `bNetInitial && Role == Authority` | S to C | | |
| 0 | `DeltaCaptureTime` | var | float | `bNetInitial && Role == Authority` | S to C | | |
| 0 | `UseRelativeLocation` | var | Vector | `bNetInitial && Role == Authority` | S to C | | |
| 0 | `UseRelativeRotation` | var | Rotator | `bNetInitial && Role == Authority` | S to C | | |

#### SBase.SFlag (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `FlagCarrier` | var | Pawn | `bNetInitial && Role == Authority` | S to C | | |
| 0 | `DropZone` | var | Actor[5] | `bNetInitial && Role == Authority` | S to C | | |
| 1 | `bResetZeroValues` | var | bool | `Role == Authority` | S to C | | |
| 2 | `SetFlagCarrier` | func | bool | `Role == Authority` | S to C | R | S |

#### SBase.SInteractObject (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `iLastUsedTime` | var | int | `Role == Authority` | S to C | | |
| 0 | `iReusableTime` | var | int | `Role == Authority` | S to C | | |
| 0 | `bPresenceDetected` | var | bool | `Role == Authority` | S to C | | |
| 0 | `bInteractInFrontWithCollisions` | var | bool | `Role == Authority` | S to C | | |
| 0 | `bInteractInFront` | var | bool | `Role == Authority` | S to C | | |
| 0 | `bUsable` | var | bool | `Role == Authority` | S to C | | |
| 0 | `bInUse` | var | bool | `Role == Authority` | S to C | | |
| 0 | `ItHeight` | var | float | `Role == Authority` | S to C | | |
| 0 | `ItRadius` | var | float | `Role == Authority` | S to C | | |
| 1 | `ChangePresenceDetected` | func | bool | `Role < Authority` | C to S | R |  |

#### SBase.STriggerInteraction (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `MinTriggerTime` | var | float | `Role == Authority` | S to C | | |
| 0 | `OwnerInterrupteur` | var | SInterrupteur | `Role == Authority` | S to C | | |
| 0 | `bMaintain` | var | bool | `Role == Authority` | S to C | | |
| 0 | `bHackable` | var | bool | `Role == Authority` | S to C | | |
| 1 | `AddRemoveActorOnClientHud` | func | SPlayerController, bool | `Role == Authority` | S to C | R | S |

#### SBase.SComputerInteraction (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `ObjectiveTrigger` | var | SObjectiveTrigger | `Role == Authority` | S to C | | |

#### SBase.SFlagDropZoneInteraction (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `MyOwner` | var | Actor | `Role == Authority` | S to C | | |

#### SBase.SPickupInteraction (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `bCanPickup` | var | bool | `Role == Authority` | S to C | | |

#### SBase.SInteractionManager (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `NbObjects` | var | int | `Role == Authority` | S to C | | |
| 0 | `Interactions` | var | SInteractObject[15] | `Role == Authority` | S to C | | |
| 0 | `AddObjetOnClient` | func | SInteractObject, int | `Role == Authority` | S to C | R | S |
| 0 | `RemoveInteractionObjOnClient` | func | SInteractObject | `Role == Authority` | S to C | R | S |

### Native lists and relevancy (decoded)

- `SComputerObjectiveTrigger`'s list sends `SkinState` on every change (not only in the first bunch) and
  `DeltaCaptureTime` while the terminal is active (editor 0x111cf840).
- `ASFlag::IsNetRelevantFor` (game 0x10ae9a00): a hidden flag is not relevant; a flag carried by a pawn is relevant
  exactly when its carrier is; otherwise it is always relevant.
- `ASDisk`'s list sends `Base`, `bHidden` and `bTriggering` plus the objective trigger list (editor 0x111ce9c0);
  `ASFlagDropZone`'s sends only `bTriggering`, with no parent list at all (editor 0x111cf480).

## States

Object states that matter to the network: `STriggerInteraction.s_Maintain_Use` is a simulated state
whose simulated `BeginState`/`EndState` set `bInUse` true/false on whichever side enters it;
`s_Hacking` extends it on the server. Trigger states (`s_Initial`, `s_Inactive`,
`s_TemporaryInactive`) are server only; clients see their effect through `bActive`, `bLocked` and
`bTriggering`.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Objective trigger NetUpdateFrequency | 5 | 1/s | decoded | def:SBase.SObjectiveTrigger |
| Simulated take time without triggers | 5.0 | s | decoded | uc:SBase.SObjective:SimulateBeingTook |
| Drop zones per disk | 5 | | decoded | `SFlag.DropZone[5]` |
| Drop zone lid rotation rate | (20000, 0, 0) | rotator units/s | decoded | uc:SBase.SFlagDropZone:PostBeginPlay |
| Built-in interactions | 5 | | decoded | uc:SBase.SInteractionManager:PostBeginPlay |

## Network

Server: all objective logic, triggering, carrier changes, interaction handling. Every client:
objective and trigger variables (always relevant), broadcasts for HUD and radar, disk movement when
relevant. Owning client only: its interaction manager and its mirrored states.

## Edge cases

- Objective triggers update at most 5 times per second, so a hack start or stop reaches clients up to
  0.2 s late through variables, but immediately through the broadcast calls.
- `FlagCarrier` is first-bunch only: a client whose disk channel was already open never sees later
  carriers through it and must rely on `SBasePawn.CarriedFlag` and `FlagTaken`.
- A late joiner misses earlier `AddRemoveObjectiveForRadar` and `AddRemoveObjectiveOnClientHud` calls
  (`model.md` edge cases).

## Evidence

- `local/re-out/uc/SBase/SObjective.uc` (replication, `SimulateBeingTook`, `AddRemoveObjectiveOnClientHud`),
  `SObjectiveTrigger.uc` (`Triggering`, `AddRemoveObjectiveForRadar`, `TakeObjective`),
  `SComputerObjectiveTrigger.uc` (replication 18-22), `SFlag.uc` (`PostBeginPlay`, `SetFlagCarrier`,
  `NotifyPickup`, `Throw`, `CheckDropZone`), `SFlagDropZone.uc` (`PostBeginPlay`, cover), `SDisk.uc`,
  `SInteractObject.uc`, `STriggerInteraction.uc`, `SComputerInteraction.uc`,
  `SFlagDropZoneInteraction.uc`, `SPickupInteraction.uc`, `SInteractionManager.uc`;
  `SPlayerController.uc` `InteractionInit` 2007, `Interaction` 2013, `InteractionPost` 2018,
  `DiscardPickup` 6728, `DiscardFlag` 6738, `DestroyGPO` 6899, `Possess` (IManager spawn) 1977.
- Native names raised: `Ev_AddObjetOnClient`, `Ev_RemoveInteractionObjOnClient`,
  `Ev_AddRemoveObjectiveOnClientHud` (`local/reloaded_strings/game_exe.txt`).
- Natives: `SObjective.Reset` 1417 at 0x10a9f2e0, `NotifyTriggering` 1419 at 0x10a9f690,
  `SObjectiveTrigger.GetTriggerTime` 1317 at 0x10ae8af0, `IsActive` 1318 at 0x10ae8b30, `Desactivate`
  1319/1320/1321 at 0x10ae9c60, `SInteractObject.DoTouch` 1329 at 0x10af6bd0, `CanUse` 1334 at
  0x10ab96f0, `PostInteract` 1330 at 0x10af6c90, `STriggerInteraction.HandleEventInfo` 1336 at
  0x10af6650, `SInteractionManager.AddInteractionObj` 1434 at 0x10abf470, `AddObjectAtIndex` 1433 at
  0x10abf390, `RemoveObject` 1435 at 0x10abf5a0, `RemoveInteractionObj` 1436 at 0x10abf970,
  `SMission.NotifyChildTaken` 1420 at 0x10abffb0.
- Native lists (editor): `ASObjective` 0x1121fca0, `ASMission` 0x11220640, `ASObjectiveTrigger`
  0x111ce270, `ASComputerObjectiveTrigger` 0x111cf840, `ASFlag` 0x111cf040 (relevancy 0x111cf570),
  `ASFlagDropZone` 0x111cf480, `ASDisk` 0x111ce9c0, `ASInteractObject` 0x111bc900,
  `ASTriggerInteraction` 0x111bd910, `ASComputerInteraction` 0x111be2b0, `ASFlagDropZoneInteraction`
  0x111bd0c0, `ASPickupInteraction` 0x111bde30, `ASInteractionManager` 0x11222390.

## Open questions

3. Who raises the `Ev_*` events natively (channel open on the client, to replay state?).

### Resolved

1. How clients get the terminal skin after the first bunch. Resolved: the native list sends `SkinState` on every
   change, and `DeltaCaptureTime` while active. Rule "Native lists and relevancy". Evidence: editor 0x111cf840
   (ANSWERS-net rep-objectives 1).
2. `ASFlag::IsNetRelevantFor`. Resolved: hidden: no; carried: the carrier's result; otherwise always relevant.
   Evidence: game 0x10ae9a00 (ANSWERS-net rep-objectives 2).
4. `ASDisk` and `ASFlagDropZone` lists. Resolved: disk `Base`, `bHidden`, `bTriggering` plus the objective
   trigger list; drop zone only `bTriggering`. Evidence: editor 0x111ce9c0, 0x111cf480 (ANSWERS-net
   rep-objectives 4).
