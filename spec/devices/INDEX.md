# Devices spec index

Behaviour specs for the map devices SteeE uses: event dispatch, movers, triggers, animated meshes, shakers, GE
switches, magic events, gameplay objects, glass, extinguishers, damage volumes, stations, hint zones, the camera
network, sound devices, zones and volumes, security control zones, help paths, sprinklers, and the SteeE chains (crane timeline) that compose them.

Total units specced: 518.
network, laser tripwires and volumetric sensors, sound devices, and the SteeE chains (crane timeline) that compose them.


## Files

| File | Title | Units | Kinds |
|---|---|---|---|
| [events.md](events.md) | Event dispatch (Tag, Event, Trigger, UnTrigger) | 13 | 4 nat, 9 uc |
| [mover.md](mover.md) | Mover | 97 | 1 def, 3 nat, 5 rep, 88 uc |
| [triggers.md](triggers.md) | Proximity triggers: Trigger and SPresenceTrigger | 20 | 3 def, 2 nat, 15 uc |
| [sound-music.md](sound-music.md) | Sound devices: SoundTrigger, SAmbientSoundTrigger, MusicTrigger | 17 | 3 def, 5 nat, 1 rep, 8 uc |
| [animated-mesh.md](animated-mesh.md) | SAnimatedMesh and animation notifies | 17 | 3 def, 3 nat, 11 uc |
| [shaker.md](shaker.md) | SShaker (camera shake on an event) | 9 | 1 def, 1 nat, 1 rep, 6 uc |
| [ge-trigger.md](ge-trigger.md) | SGETrigger (traversal element activation mask) | 4 | 1 def, 1 rep, 2 uc |
| [magic-event.md](magic-event.md) | SMagicEvent (delayed event groups) | 5 | 1 def, 1 nat, 3 uc |
| [gameplay-object.md](gameplay-object.md) | SBasicGamePlayObject and SGamePlayObject (breakables, swaps, explosives) | 56 | 4 def, 2 nat, 5 rep, 45 uc |
| [vitre.md](vitre.md) | SVitre (breakable glass) | 2 | 2 def |
| [extincteur.md](extincteur.md) | SExtincteur (fire extinguisher spray) | 6 | 2 def, 4 uc |
| [damage-volume.md](damage-volume.md) | SDamageVolume (periodic damage zone) | 5 | 1 def, 4 uc |
| [infirmary-ammo.md](infirmary-ammo.md) | SInfirmary and SAmmoStock (heal and resupply stations) | 13 | 4 def, 1 rep, 8 uc |
| [zone-tips.md](zone-tips.md) | SZoneTips and SEndZoneTip (tutorial hint zones) | 9 | 2 def, 7 uc |
| [cam-network.md](cam-network.md) | SCamNetwork (merc surveillance camera ring) | 31 | 2 def, 3 nat, 9 rep, 17 uc |
| [lift.md](lift.md) | SLift and SLiftDoor (elevators) | 13 | 2 def, 11 uc |
| [mover-rotating.md](mover-rotating.md) | MoverRotating | 9 | 1 def, 4 rep, 4 uc |
| [ball-trap.md](ball-trap.md) | SBallTrap (projectile launcher) | 6 | 1 def, 1 rep, 4 uc |
| [zone-info.md](zone-info.md) | ZoneInfo and actor zones (zone separation, ER visibility zones) | 6 | 1 nat, 5 uc |
| [volume.md](volume.md) | Volume, BlockingVolume and player starts | 6 | 2 def, 4 uc |
| [gpo-control.md](gpo-control.md) | SGPOControl (security control zone) | 10 | 1 def, 3 rep, 6 uc |
| [help-path.md](help-path.md) | SHelpPath (in-game help markers) | 2 | 2 uc |
| [sprinkler.md](sprinkler.md) | SSprinkler and SFlaque (sprinklers and puddles) | 15 | 2 def, 2 rep, 11 uc |
| [alarm.md](alarm.md) | SAlarm (map alarms: locks, events, HUD alarm) | 35 | 1 def, 2 eng, 2 nat, 4 rep, 26 uc |
| [message-trigger.md](message-trigger.md) | SMessageTrigger (map text messages) | 6 | 1 def, 1 rep, 4 uc |
| [notify-noise.md](notify-noise.md) | SNotifyNoise (repeating noise marker) | 10 | 1 def, 2 rep, 7 uc |
| [security-camera.md](security-camera.md) | SCamera (map security camera: sweep, spy detection, alarm event) | 19 | 1 def, 1 eng, 1 nat, 3 rep, 13 uc |
| [trigger-light.md](trigger-light.md) | STriggerLight (switchable map light, ObjectLights) | 13 | 1 def, 1 eng, 2 rep, 9 uc |
| [projector.md](projector.md) | SProjoFixe, SProjoLight, SProjoInteraction and the merc's Mirador state | 15 | 3 def, 1 rep, 11 uc |
| [laser-trigger.md](laser-trigger.md) | SLaserTrigger (laser tripwire) | 29 | 1 def, 1 eng, 2 nat, 4 rep, 21 uc |
| [volumetric-sensor.md](volumetric-sensor.md) | SVolumetricSensor (motion sensor over volumes) | 20 | 1 def, 1 rep, 18 uc |
| [steee-chains.md](steee-chains.md) | SteeE device chains (worked examples, crane timeline) | 0 | none (composition) |

## Units by file

### events.md

- uc:Engine.Actor:TriggerEvent
- uc:Engine.Actor:UntriggerEvent
- uc:Engine.Actor:ForceTriggerableEvenIfMoving
- uc:Engine.Actor:Trigger
- uc:Engine.Actor:UnTrigger
- uc:Engine.Actor:BeginEvent
- uc:Engine.Actor:EndEvent
- uc:Engine.Actor:SetInitialState
- uc:Engine.Actor:NotifyNoise
- nat:Engine.Actor.DynamicActors
- nat:Core.Object.GotoState
- nat:Core.Object.Enable
- nat:Core.Object.Disable

### mover.md

- def:Engine.Mover
- uc:Engine.Mover:StartInterpolation
- uc:Engine.Mover:Timer
- uc:Engine.Mover:InterpolateTo
- uc:Engine.Mover:SetKeyframe
- uc:Engine.Mover:KeyFrameReached
- uc:Engine.Mover:FinishNotify
- uc:Engine.Mover:FinishedClosing
- uc:Engine.Mover:FinishedOpening
- uc:Engine.Mover:DoOpen
- uc:Engine.Mover:DoClose
- uc:Engine.Mover:BeginPlay
- uc:Engine.Mover:PostBeginPlay
- uc:Engine.Mover:MakeGroupStop
- uc:Engine.Mover:MakeGroupReturn
- uc:Engine.Mover:MakeGroupReturnNoEncroach
- uc:Engine.Mover:EncroachingOn
- uc:Engine.Mover:Bump
- uc:Engine.Mover:TakeDamage
- uc:Engine.Mover:Trigger
- uc:Engine.Mover:UnTrigger
- uc:Engine.Mover:SubTrigger
- uc:Engine.Mover:SubUnTrigger
- uc:Engine.Mover:IsTriggerable
- uc:Engine.Mover:Lock
- uc:Engine.Mover:UnLock
- uc:Engine.Mover:s_WaitAndRetry.EncroachingOn
- uc:Engine.Mover:s_WaitAndRetry@code
- uc:Engine.Mover:s_ReturnNoEncroach.EncroachingOn
- uc:Engine.Mover:s_ReturnNoEncroach.SendClosingEvent
- uc:Engine.Mover:s_ReturnNoEncroach.SendOpeningEvent
- uc:Engine.Mover:s_ReturnNoEncroach@code
- uc:Engine.Mover:OpenTimedMover.DisableTrigger
- uc:Engine.Mover:OpenTimedMover.EnableTrigger
- uc:Engine.Mover:OpenTimedMover@code
- uc:Engine.Mover:StandOpenTimed.Attach
- uc:Engine.Mover:StandOpenTimed.DisableTrigger
- uc:Engine.Mover:StandOpenTimed.EnableTrigger
- uc:Engine.Mover:BumpOpenTimed.Bump
- uc:Engine.Mover:BumpOpenTimed.DisableTrigger
- uc:Engine.Mover:BumpOpenTimed.EnableTrigger
- uc:Engine.Mover:TriggerOpenTimed.Trigger
- uc:Engine.Mover:TriggerOpenTimed.UnTrigger
- uc:Engine.Mover:TriggerOpenTimed.SubTrigger
- uc:Engine.Mover:TriggerOpenTimed.DisableTrigger
- uc:Engine.Mover:TriggerOpenTimed.EnableTrigger
- uc:Engine.Mover:CloseTimedMover.DisableTrigger
- uc:Engine.Mover:CloseTimedMover.EnableTrigger
- uc:Engine.Mover:CloseTimedMover@code
- uc:Engine.Mover:TriggerCloseTimed.Trigger
- uc:Engine.Mover:TriggerCloseTimed.UnTrigger
- uc:Engine.Mover:TriggerCloseTimed.SubTrigger
- uc:Engine.Mover:TriggerCloseTimed.DisableTrigger
- uc:Engine.Mover:TriggerCloseTimed.EnableTrigger
- uc:Engine.Mover:TriggerToggle.Trigger
- uc:Engine.Mover:TriggerToggle.UnTrigger
- uc:Engine.Mover:TriggerToggle.SubTrigger
- uc:Engine.Mover:TriggerToggle@code
- uc:Engine.Mover:TriggerControl.Trigger
- uc:Engine.Mover:TriggerControl.UnTrigger
- uc:Engine.Mover:TriggerControl.SubTrigger
- uc:Engine.Mover:TriggerControl.SubUnTrigger
- uc:Engine.Mover:TriggerControl.BeginState
- uc:Engine.Mover:TriggerControl@code
- uc:Engine.Mover:TriggerPound.Trigger
- uc:Engine.Mover:TriggerPound.UnTrigger
- uc:Engine.Mover:TriggerPound.SubTrigger
- uc:Engine.Mover:TriggerPound.SubUnTrigger
- uc:Engine.Mover:TriggerPound.BeginState
- uc:Engine.Mover:TriggerPound@code
- uc:Engine.Mover:BumpButton.Bump
- uc:Engine.Mover:BumpButton.BeginEvent
- uc:Engine.Mover:BumpButton.EndEvent
- uc:Engine.Mover:BumpButton@code
- uc:Engine.Mover:TriggerOpenOnceOnly.DisableTrigger
- uc:Engine.Mover:TriggerOpenOnceOnly.EnableTrigger
- uc:Engine.Mover:TriggerOpenOnceOnly.SubTrigger
- uc:Engine.Mover:TriggerOpenOnceOnly.SubUnTrigger
- uc:Engine.Mover:TriggerOpenOnceOnly@code
- uc:Engine.Mover:TriggerCloseOnceOnly.DisableTrigger
- uc:Engine.Mover:TriggerCloseOnceOnly.EnableTrigger
- uc:Engine.Mover:TriggerCloseOnceOnly.SubTrigger
- uc:Engine.Mover:TriggerCloseOnceOnly.SubUnTrigger
- uc:Engine.Mover:TriggerCloseOnceOnly@code
- uc:Engine.Mover:s_TemporaryInactive.BeginState
- uc:Engine.Mover:s_TemporaryInactive.Lock
- uc:Engine.Mover:s_TemporaryInactive.UnLock
- uc:Engine.Actor:CrushedBy
- uc:SBase.SBasePawn:CrushedBy
- rep:Engine.Mover.BasePos
- rep:Engine.Mover.RealPosition
- rep:Engine.Mover.SimInterpolate
- rep:Engine.Mover.SimOldPos
- rep:Engine.Mover.BaseRot
- nat:Engine.Actor.FinishInterpolation
- nat:Engine.Actor.SetPhysics
- nat:Engine.Actor.MakeNoise

### triggers.md

- def:Engine.Triggers
- def:Engine.Trigger
- uc:Engine.Trigger:PreBeginPlay
- uc:Engine.Trigger:PostBeginPlay
- uc:Engine.Trigger:Reset
- uc:Engine.Trigger:FindTriggerActor
- uc:Engine.Trigger:SpecialHandling
- uc:Engine.Trigger:CheckTouchList
- uc:Engine.Trigger:IsRelevant
- uc:Engine.Trigger:Touch
- uc:Engine.Trigger:Timer
- uc:Engine.Trigger:TakeDamage
- uc:Engine.Trigger:UnTouch
- uc:Engine.Trigger:OtherTriggerToggles.Trigger
- uc:Engine.Trigger:OtherTriggerTurnsOn.Trigger
- uc:Engine.Trigger:OtherTriggerTurnsOff.Trigger
- def:SBase.SPresenceTrigger
- uc:SBase.SPresenceTrigger:Touch
- nat:Engine.Actor.OnlyAffectPawns
- nat:Engine.Actor.TouchingActors

### sound-music.md

- def:Engine.SoundTrigger
- uc:Engine.SoundTrigger:Trigger
- def:Engine.MusicTrigger
- uc:Engine.MusicTrigger:PreBeginPlay
- uc:Engine.MusicTrigger:ClientPlayStopMusic
- uc:Engine.MusicTrigger:Trigger
- uc:Engine.MusicTrigger:UnTrigger
- rep:Engine.MusicTrigger.ClientPlayStopMusic
- def:SBase.SAmbientSoundTrigger
- uc:SBase.SAmbientSoundTrigger:PreBeginPlay
- uc:SBase.SAmbientSoundTrigger:Trigger
- uc:SBase.SAmbientSoundTrigger:UnTrigger
- nat:Engine.Actor.PlaySound
- nat:Engine.Actor.StopSound
- nat:Engine.Actor.PlayMusic
- nat:Engine.Actor.StopMusic
- nat:Engine.Actor.GetCurrentMusic

### animated-mesh.md

- def:SBase.SAnimatedMesh
- uc:SBase.SAnimatedMesh:PostNetBeginPlay
- uc:SBase.SAnimatedMesh:AnimEnd
- uc:SBase.SAnimatedMesh:s_Wait.BeginState
- uc:SBase.SAnimatedMesh:s_Wait.Trigger
- uc:SBase.SAnimatedMesh:s_Wait.UnTrigger
- uc:SBase.SAnimatedMesh:s_AnimateForward.BeginState
- uc:SBase.SAnimatedMesh:s_AnimateForward.AnimEnd
- uc:SBase.SAnimatedMesh:s_AnimateForward.Trigger
- uc:SBase.SAnimatedMesh:s_AnimateForward.UnTrigger
- uc:SBase.SAnimatedMesh:s_AnimateBackward.BeginState
- uc:Engine.AnimNotify_Trigger:Notify
- def:Engine.AnimNotify_Trigger
- def:Engine.AnimNotify_Sound
- nat:Engine.Actor.AttachToBone
- nat:Engine.Actor.FreezeAnimAt
- nat:Engine.Actor.PlayAnim

### shaker.md

- def:SBase.SShaker
- uc:SBase.SShaker:Trigger
- uc:SBase.SPlayerController:ClientShake
- rep:SBase.SPlayerController.ClientShake
- uc:SBase.SPlayerCam:Tilt
- uc:SBase.SPlayerCam:Shake
- uc:SBase.SPlayerCam:Hit
- uc:SBase.SPlayerCam:ResetEffects
- nat:Engine.Actor.DynamicPlayerControllers

### ge-trigger.md

- def:SBase.SGETrigger
- uc:SBase.SGETrigger:Trigger
- uc:SBase.SGETrigger:UnTrigger
- rep:Engine.LevelInfo.GEActivationMask

### magic-event.md

- def:SBase.SMagicEvent
- uc:SBase.SMagicEvent:BeginPlay
- uc:SBase.SMagicEvent:Trigger
- uc:SBase.SMagicEvent:UnTrigger
- nat:SBase.SMagicEvent.NativeTrigger

### gameplay-object.md

- def:SBase.SBasicGamePlayObject
- uc:SBase.SBasicGamePlayObject:Destroyed
- uc:SBase.SBasicGamePlayObject:PostBeginPlay
- uc:SBase.SBasicGamePlayObject:PreBeginPlay
- uc:SBase.SBasicGamePlayObject:Timer
- uc:SBase.SBasicGamePlayObject:AcceptStickableGPO
- uc:SBase.SBasicGamePlayObject:AddOnHud
- uc:SBase.SBasicGamePlayObject:RemoveFromHud
- uc:SBase.SBasicGamePlayObject:SetLockedByChaff
- uc:SBase.SBasicGamePlayObject:Malfunction
- uc:SBase.SBasicGamePlayObject:TakeDamage
- uc:SBase.SBasicGamePlayObject:s_LockedByChaff.BeginState
- uc:SBase.SBasicGamePlayObject:s_LockedByChaff.EndState
- uc:SBase.SBasicGamePlayObject:s_LockedByChaff.Tick
- uc:SBase.SBasicGamePlayObject:s_LockedByChaff.SetLockedByChaff
- uc:SBase.SBasicGamePlayObject:s_LockedByChaff.Timer
- rep:SBase.SBasicGamePlayObject.bSecondaryObjective
- rep:SBase.SBasicGamePlayObject.InteractionObj
- def:SBase.SGamePlayObject
- uc:SBase.SGamePlayObject:AcceptStickableGPO
- uc:SBase.SGamePlayObject:BeginPlay
- uc:SBase.SGamePlayObject:PostBeginPlay
- uc:SBase.SGamePlayObject:Trigger
- uc:SBase.SGamePlayObject:UnTrigger
- uc:SBase.SGamePlayObject:SetLockedByChaff
- uc:SBase.SGamePlayObject:TakeDamage
- uc:SBase.SGamePlayObject:WaitReset
- uc:SBase.SGamePlayObject:Touch
- uc:SBase.SGamePlayObject:NotifyPickup
- uc:SBase.SGamePlayObject:Throw
- uc:SBase.SGamePlayObject:StoppedMoving
- uc:SBase.SGamePlayObject:SpawnDamageEmitter
- uc:SBase.SGamePlayObject:KillDamageEmitter
- uc:SBase.SGamePlayObject:s_LockedByChaff.BeginState
- uc:SBase.SGamePlayObject:s_LockedByChaff.EndState
- uc:SBase.SGamePlayObject:s_PickedUp.BeginState
- uc:SBase.SGamePlayObject:s_PickedUp.TakeDamage
- uc:SBase.SGamePlayObject:s_Flying.BeginState
- uc:SBase.SGamePlayObject:s_Flying.Landed
- uc:SBase.SGamePlayObject:s_Flying.Bump
- uc:SBase.SGamePlayObject:s_Flying.HitWall
- uc:SBase.SGamePlayObject:s_WaitForDamageReset.BeginState
- uc:SBase.SGamePlayObject:s_WaitForDamageReset.Timer
- nat:SBase.SGamePlayObject.ProcessDamage
- nat:SBase.SGamePlayObject.NativeHitWall
- rep:SBase.SGamePlayObject.Throw
- rep:SBase.SGamePlayObject.SpawnDamageEmitter
- rep:SBase.SGamePlayObject.KillDamageEmitter
- def:SBase.SGrenadeExplosion
- uc:SBase.SGrenadeExplosion:PostBeginPlay
- uc:SBase.SGrenadeExplosion:Tick
- uc:SBase.SGrenadeExplosion:Timer
- def:SBase.SFlashLight_FragGrenade
- uc:SBase.SFlashLight_FragGrenade:PostBeginPlay
- uc:SBase.SGameInfo:CountEnvironmentalKill
- uc:SBase.SPlayerController:DestroyGPO

### vitre.md

- def:SBase.SVitre
- def:SBase.SVitreEmitter

### extincteur.md

- def:SBase.SExtincteur
- uc:SBase.SExtincteur:Reset
- uc:SBase.SExtincteur:s_Waiting.TakeDamage
- uc:SBase.SExtincteur:s_Used.TakeDamage
- uc:SBase.SExtincteur:s_Used.Timer
- def:SBase.SExtincteurEmitter

### damage-volume.md

- def:SBase.SDamageVolume
- uc:SBase.SDamageVolume:Touch
- uc:SBase.SDamageVolume:Trigger
- uc:SBase.SDamageVolume:UnTrigger
- uc:SBase.SGameInfo:FindDamageVolume

### infirmary-ammo.md

- def:SBase.SInfirmary
- uc:SBase.SInfirmary:AcceptStickableGPO
- uc:SBase.SInfirmary:Use
- def:SBase.SInfirmaryInteraction
- uc:SBase.SInfirmaryInteraction:InitInteract
- def:SBase.SAmmoStock
- uc:SBase.SAmmoStock:AcceptStickableGPO
- uc:SBase.SAmmoStock:PostNetBeginPlay
- uc:SBase.SAmmoStock:Use
- def:SBase.SAmmoStockInteraction
- uc:SBase.SAmmoStockInteraction:InitInteract
- uc:SBase.SPlayerController:ReloadAmmoStock
- rep:SBase.SPlayerController.ReloadAmmoStock

### zone-tips.md

- def:SBase.SZoneTips
- uc:SBase.SZoneTips:Touch
- uc:SBase.SZoneTips:UnTouch
- uc:SBase.SZoneTips:AddMessage
- uc:SBase.SZoneTips:RemoveMessage
- def:SBase.SEndZoneTip
- uc:SBase.SEndZoneTip:Touch
- uc:SBase.SEndZoneTip:s_DelayBeforeEnd.BeginState
- uc:SBase.SEndZoneTip:s_DelayBeforeEnd.Timer

### cam-network.md

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

### lift.md

- def:SBase.SLift
- uc:SBase.SLift:PostNetBeginPlay
- uc:SBase.SLift:Trigger
- uc:SBase.SLift:EncroachingOn
- uc:SBase.SLift:KeyFrameReached
- uc:SBase.SLift:PrepareGo.BeginState
- uc:SBase.SLift:PrepareGo.Timer
- uc:SBase.SLift:Go.Arrived
- uc:SBase.SLift:Go@code
- uc:SBase.SLift:WaitDoors.BeginState
- uc:SBase.SLift:WaitDoors.Timer
- def:SBase.SLiftDoor
- uc:SBase.SLiftDoor:EncroachingOn

### mover-rotating.md

- def:Engine.MoverRotating
- uc:Engine.MoverRotating:Timer
- uc:Engine.MoverRotating:InterpolateTo
- uc:Engine.MoverRotating:KeyFrameReached
- uc:Engine.MoverRotating:BeginPlay
- rep:Engine.MoverRotating.RealRotation
- rep:Engine.MoverRotating.SimOldRotRoll
- rep:Engine.MoverRotating.SimOldRotYaw
- rep:Engine.MoverRotating.SimOldRotPitch

### ball-trap.md

- def:SBase.SBallTrap
- uc:SBase.SBallTrap:ThrowGren
- uc:SBase.SBallTrap:Trigger
- uc:SBase.SBallTrap:UnTrigger
- uc:SBase.SBallTrap:Timer
- rep:SBase.SBallTrap.ThrowGren
### zone-info.md

- uc:Engine.ZoneInfo:PreBeginPlay
- uc:Engine.ZoneInfo:LinkToSkybox
- uc:Engine.ZoneInfo:ActorEntered
- uc:Engine.ZoneInfo:ActorLeaving
- nat:Engine.ZoneInfo.ZoneActors
- uc:Engine.Actor:ZoneChange

### volume.md

- def:Engine.Volume
- uc:Engine.Volume:PostBeginPlay
- uc:Engine.Volume:AssociatedTouch.Touch
- uc:Engine.Volume:AssociatedTouch.UnTouch
- uc:Engine.Volume:AssociatedTouch.BeginState
- def:Engine.BlockingVolume

### gpo-control.md

- def:SBase.SGPOControl
- uc:SBase.SGPOControl:PostNetBeginPlay
- uc:SBase.SGPOControl:LockChaff
- uc:SBase.SGPOControl:s_On.BeginState
- uc:SBase.SGPOControl:s_On.EndState
- uc:SBase.SGPOControl:s_On.Tick
- uc:SBase.SGPOControl:s_On.Timer
- rep:SBase.SGPOControl.CylinderHeight
- rep:SBase.SGPOControl.CylinderWidth
- rep:SBase.SGPOControl.ZoneDescription

### help-path.md

- uc:SBase.SHelpPath:PostBeginPlay
- uc:SBase.SHelpPath:Visible

### sprinkler.md

- def:SBase.SSprinkler
- uc:SBase.SSprinkler:PostBeginPlay
- uc:SBase.SSprinkler:SpawnWater
- uc:SBase.SSprinkler:StopWater
- uc:SBase.SSprinkler:TakeDamage
- uc:SBase.SSprinkler:Trigger
- uc:SBase.SSprinkler:UnTrigger
- uc:SBase.SSprinkler:Timer
- rep:SBase.SSprinkler.WaterEmitter
- def:SBase.SFlaque
- uc:SBase.SFlaque:Tick
- uc:SBase.SFlaque:Destroyed
- uc:SBase.SFlaque:Grow
- uc:SBase.SFlaque:Reduce
- rep:SBase.SFlaque.variation
### alarm.md

- def:SBase.SAlarm
- uc:SBase.SAlarm:AlarmStateChange
- uc:SBase.SAlarm:PawnDefenseEntered
- uc:SBase.SAlarm:IsDefenseInAlarmZone
- uc:SBase.SAlarm:SetAlarmListening
- uc:SBase.SAlarm:RestartCountdown
- uc:SBase.SAlarm:SetAlarmActive
- uc:SBase.SAlarm:SimulateActivated
- uc:SBase.SAlarm:s_Listening.Trigger
- uc:SBase.SAlarm:s_Listening.UnTrigger
- uc:SBase.SAlarm:s_Listening.SimulateActivated
- uc:SBase.SAlarm:s_Listening.Timer
- uc:SBase.SAlarm:s_Listening.EndState
- uc:SBase.SAlarm:s_Active.SimulateActivated
- uc:SBase.SAlarm:s_Active.BeginState
- uc:SBase.SAlarm:s_Active.EndState
- uc:SBase.SAlarm:s_Active.Timer
- uc:SBase.SAlarm:s_Active.Trigger
- uc:SBase.SAlarm:s_Active.UnTrigger
- uc:SBase.SAlarm:s_ActiveDefPresent.BeginState
- uc:SBase.SAlarm:s_ActiveDefPresent.EndState
- uc:SBase.SAlarm:s_ActiveDefPresent.Timer
- uc:SBase.SAlarm:s_ActiveDefPresent.SimulateActivated
- uc:SBase.SAlarm:s_ActiveDefPresent.Trigger
- uc:SBase.SAlarm:s_ActiveDefPresent.UnTrigger
- uc:SBase.SAlarm:s_InitialyActive.BeginState
- uc:SBase.SAlarm:s_InitialyActive.Timer
- rep:SBase.SAlarm.MessagePriority
- rep:SBase.SAlarm.Duration
- rep:SBase.SAlarm.bAlarmOn
- rep:SBase.SAlarm.StartTime
- eng:ASAlarm::Tick
- eng:ASAlarm::GetOptimizedRepList
- nat:SBase.SHud.AddAlarm
- nat:SBase.SHud.RemoveAlarm

### message-trigger.md

- def:SBase.SMessageTrigger
- uc:SBase.SMessageTrigger:PostBeginPlay
- uc:SBase.SMessageTrigger:DisplayMessage
- uc:SBase.SMessageTrigger:Trigger
- uc:SBase.SMessageTrigger:UnTrigger
- rep:SBase.SMessageTrigger.DisplayMessage

### notify-noise.md

- def:SBase.SNotifyNoise
- uc:SBase.SNotifyNoise:ChangeStateToAll
- uc:SBase.SNotifyNoise:PostNetBeginPlay
- uc:SBase.SNotifyNoise:Timer
- uc:SBase.SNotifyNoise:Trigger
- uc:SBase.SNotifyNoise:UnTrigger
- uc:SBase.SNotifyNoise:s_Wait.BeginState
- uc:SBase.SNotifyNoise:s_MakeNoise.BeginState
- rep:SBase.SNotifyNoise.bEnabled
- rep:SBase.SNotifyNoise.ChangeStateToAll
### laser-trigger.md

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

### volumetric-sensor.md

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

## Relevant units not specced here

Found while tracing SteeE's devices; each is owned by another spec area or is not used by SteeE. Listed so coverage
can track them. One unit per line.

### security-camera.md

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

### trigger-light.md

- def:SBase.STriggerLight
- uc:SBase.STriggerLight:SetEMFDLightToClient
- uc:SBase.STriggerLight:UnSetEMFDLightToClient
- uc:SBase.STriggerLight:PostBeginPlay
- uc:SBase.STriggerLight:Trigger
- uc:SBase.STriggerLight:UnTrigger
- uc:SBase.STriggerLight:Timer
- uc:SBase.STriggerLight:LightToggle
- uc:SBase.STriggerLight:LightOn
- uc:SBase.STriggerLight:LightOff
- eng:ASTriggerLight::Tick
- rep:SBase.STriggerLight.SetEMFDLightToClient
- rep:SBase.STriggerLight.UnSetEMFDLightToClient

### projector.md

- def:SBase.SProjoFixe
- uc:SBase.SProjoFixe:Tick
- rep:SBase.SProjoFixe.bInUse
- def:SBase.SProjoLight
- def:SBase.SProjoInteraction
- uc:SBase.SProjoInteraction:InitInteract
- uc:SBase.SPlayerController:UseMirador
- uc:SBase.SPlayerController:s_UseObject_Mirador.GadTorcheSwitch
- uc:SBase.SPlayerController:s_UseObject_Mirador.GadLaserSwitch
- uc:SBase.SPlayerController:s_UseObject_Mirador.CanBeDropAttacked
- uc:SBase.SPlayerController:s_UseObject_Mirador.CanBeElbowShocked
- uc:SBase.SPlayerController:s_UseObject_Mirador.CanBeGrabbed
- uc:SBase.SPlayerController:s_UseObject_Mirador.GoInSmoke
- uc:SBase.SPlayerController:s_UseObject_Mirador.BeginState
- uc:SBase.SPlayerController:s_UseObject_Mirador.EndState

### Switches and interactions (interaction spec; they start the crane and shutter chains; `HandleEventInfo` decides the instigator, once-only behaviour and Trigger versus UnTrigger)

- def:SBase.SInterrupteur
- uc:SBase.SInterrupteur:ForceTriggerableEvenIfMoving
- uc:SBase.SInterrupteur:AcceptStickableGPO
- uc:SBase.SInterrupteur:PreBeginPlay
- uc:SBase.SInterrupteur:PostBeginPlay
- uc:SBase.SInterrupteur:PostNetBeginPlay
- uc:SBase.SInterrupteur:Trigger
- uc:SBase.SInterrupteur:UnTrigger
- rep:SBase.SInterrupteur.bMaintainsSwitch
- rep:SBase.SInterrupteur.iHackingTime
- rep:SBase.SInterrupteur.UseRelativeLocation
- rep:SBase.SInterrupteur.UseRelativeRotation
- rep:SBase.SInterrupteur.iReusableTime
- def:SBase.STriggerInteraction
- uc:SBase.STriggerInteraction:PostBeginPlay
- uc:SBase.STriggerInteraction:AddRemoveActorOnClientHud
- uc:SBase.STriggerInteraction:InitInteract
- uc:SBase.STriggerInteraction:Interact
- uc:SBase.STriggerInteraction:s_Maintain_Use.BeginState
- uc:SBase.STriggerInteraction:s_Maintain_Use.EndState
- uc:SBase.STriggerInteraction:s_Maintain_Use.InitInteract
- uc:SBase.STriggerInteraction:s_Maintain_Use.PostInteract
- uc:SBase.STriggerInteraction:s_Hacking.EndState
- uc:SBase.STriggerInteraction:s_Hacking.PostInteract
- uc:SBase.STriggerInteraction:WaitingBeforeActivation.BeginState
- uc:SBase.STriggerInteraction:WaitingBeforeActivation.Timer
- uc:SBase.STriggerInteraction:WaitingBeforeActivation.InitInteract
- uc:SBase.STriggerInteraction:WaitingBeforeDesactivation.BeginState
- uc:SBase.STriggerInteraction:WaitingBeforeDesactivation.Timer
- uc:SBase.STriggerInteraction:WaitingBeforeDesactivation.InitInteract
- rep:SBase.STriggerInteraction.MinTriggerTime
- rep:SBase.STriggerInteraction.OwnerInterrupteur
- rep:SBase.STriggerInteraction.bMaintain
- rep:SBase.STriggerInteraction.bHackable
- rep:SBase.STriggerInteraction.AddRemoveActorOnClientHud
- nat:SBase.STriggerInteraction.HandleEventInfo
- nat:SBase.STriggerInteraction.CanUse
- def:SBase.SInteractObject
- uc:SBase.SInteractObject:DoTouch
- uc:SBase.SInteractObject:PostInteract
- uc:SBase.SInteractObject:TriggerEventServer
- uc:SBase.SInteractObject:UnTriggerEventServer
- uc:SBase.SInteractObject:SetPriority
- uc:SBase.SInteractObject:CanUse
- uc:SBase.SInteractObject:ChangePresenceDetected
- uc:SBase.SInteractObject:Interact
- uc:SBase.SInteractObject:InitInteract
- uc:SBase.SInteractObject:SetFocus
- uc:SBase.SInteractObject:PostBeginPlay
- uc:SBase.SInteractObject:PostNetBeginPlay
- uc:SBase.SInteractObject:Destroyed
- uc:SBase.SInteractObject:Touch
- uc:SBase.SInteractObject:UnTouch
- rep:SBase.SInteractObject.iLastUsedTime
- rep:SBase.SInteractObject.iReusableTime
- rep:SBase.SInteractObject.bPresenceDetected
- rep:SBase.SInteractObject.bInteractInFrontWithCollisions
- rep:SBase.SInteractObject.bInteractInFront
- rep:SBase.SInteractObject.bUsable
- rep:SBase.SInteractObject.bInUse
- rep:SBase.SInteractObject.ItHeight
- rep:SBase.SInteractObject.ItRadius
- rep:SBase.SInteractObject.ChangePresenceDetected

### Receivers owned by other areas (lighting, spawn, effects, audio, gadget specs; SteeE's 16 event emitters all set `bNetworkTrigger`; `enable_shop_spawn` is never fired by a SteeE actor)

- def:SBase.STriggerPlayerStart
- uc:SBase.STriggerPlayerStart:Trigger
- uc:SBase.STriggerPlayerStart:UnTrigger
- def:Engine.Emitter
- uc:Engine.Emitter:PreBeginPlay
- uc:Engine.Emitter:UpdatePrecacheMaterials
- uc:Engine.Emitter:Trigger
- uc:Engine.Emitter:ReplicateTrigger
- rep:Engine.Emitter.ReplicateTrigger
- nat:Engine.Emitter.Kill
- def:SBase.SSpawnableEmitter
- def:SBase.SMetalSpark
- def:SBase.SSparkBeton
- def:SBase.SSparkShocker
- def:Engine.AmbientSound

### Damage, death and credit (combat and game-rules specs; the instigator fallbacks are summarised in `gameplay-object.md` and `damage-volume.md`; the controller, pawn and player-start mover hooks are empty bot hooks)

- uc:SBase.SPlayerController:StandardReactionsToDamage
- uc:SBase.SGameInfo:Killed
- uc:SBase.SPlayerController:CountPoisonDrain
- uc:Engine.Pawn:KilledBy
- uc:SBase.SBasePawn:KilledBy
- uc:Engine.Pawn:EncroachedBy
- uc:Engine.Pawn:gibbedBy
- uc:SBase.SBasePawn:EncroachedBy
- uc:Engine.Pawn:PlayMoverHitSound
- uc:Engine.Controller:WaitForMover
- uc:Engine.Controller:MoverFinished
- uc:Engine.Controller:UnderLift
- uc:Engine.PlayerStart:MoverOpened
- uc:Engine.PlayerStart:MoverClosed

### Camera network, controller and sticky-camera side (player-controller and gadget specs; the parts that matter for network cameras are summarised in `cam-network.md` rules 6 to 10 and 13)

- uc:SBase.SPlayerController:ChangeCamNetworkVision
- uc:SBase.SPlayerController:s_UseObject.StickyCamWasDestroyed
- uc:SBase.SPlayerController:s_UseObject.PlayerTick
- uc:SBase.SPlayerController:s_UseObject.BeginState
- uc:SBase.SPlayerController:s_UseObject.TopSynchro
- uc:SBase.SPlayerController:s_UseObject.PlayerMove
- uc:SBase.SPlayerController:s_UseObject.UpdateRotation
- uc:SBase.SPlayerController:s_UseObject.EndState
- uc:SBase.SPlayerController:s_UseObject_StickyCam_SmoothToPosition.PrevStickyCam
- uc:SBase.SPlayerController:s_UseObject_StickyCam_SmoothToPosition.PawnTakeDamage
- uc:SBase.SPlayerController:s_UseObject_StickyCam_SmoothToPosition.StickyCamWasDestroyed
- uc:SBase.SPlayerController:s_UseObject_StickyCam_SmoothToPosition.BeginState
- uc:SBase.SPlayerController:s_UseObject_StickyCam_SmoothToPosition.PlayerMove
- uc:SBase.SPlayerController:s_UseObject_StickyCam_SmoothToPosition.EndState
- uc:SBase.SPlayerController:s_UseObject_StickyCam.CanBeDropAttacked
- uc:SBase.SPlayerController:s_UseObject_StickyCam.CanBeElbowShocked
- uc:SBase.SPlayerController:s_UseObject_StickyCam.PawnTakeDamage
- uc:SBase.SPlayerController:s_UseObject_StickyCam.AnimEnd
- uc:SBase.SPlayerController:s_UseObject_StickyCam.AltFire
- uc:SBase.SPlayerController:s_UseObject_StickyCam.WheelUp
- uc:SBase.SPlayerController:s_UseObject_StickyCam.WheelDown
- uc:SBase.SPlayerController:s_UseObject_StickyCam.PlayerMove
- uc:SBase.SPlayerController:s_UseObject_StickyCam.EndState
- uc:SBase.SPlayerController:s_UseObject_CamNetwork.CanBeGrabbed
- uc:SBase.SPlayerController:s_UseObject_CamNetwork.GadLaserSwitch
- uc:SBase.SPlayerController:s_UseObject_CamNetwork.GadTorcheSwitch
- uc:SBase.SPlayerController:s_UseObject_CamNetwork.SwitchHeadset
- uc:SBase.SPlayerController:s_UseObject_CamNetwork.BeginState
- uc:SBase.SPlayerController:s_UseObject_CamNetwork.EndState
- uc:SBase.SPlayerController:s_UseObject_CamNetwork.ClientChaffed
- uc:SBase.SPlayerController:s_UseObject_CamNetwork.AltFire
- uc:SBase.SPlayerController:s_UseObject_CamNetwork.WheelUp
- uc:SBase.SPlayerController:s_UseObject_CamNetwork.WheelDown
- uc:SBase.SPlayerController:s_UseObject_CamNetwork.PlayerMove
- uc:SBase.SPlayerController:s_UseObject_CamNetworkCrWaitForServer.PlayerMove
- uc:SBase.SPlayerController:s_UseObject_CamNetworkStWaitForServer.PlayerMove
- uc:SBase.SStickyCam:Destroyed
- uc:SBase.SStickyCam:Tick
- uc:SBase.SStickyCam:Touch
- uc:SBase.SStickyCam:PreBeginPlay
- uc:SBase.SStickyCam:PostNetBeginPlay
- uc:SBase.SStickyCam:PostBeginPlay
- uc:SBase.SStickyCam:Timer
- uc:SBase.SStickyCam:SpawnGaz
- uc:SBase.SStickyCam:TakeDamage
- uc:SBase.SStickyCam:EncroachedBy
- rep:SBase.SStickyCam.SpawnGaz
- rep:SBase.SStickyCam.Smoke_NbDoses
- uc:SBase.SOrientableObject:AcceptStickableGPO
- def:SBase.SStickyCam
- def:SBase.SOrientableObject

### HUD (HUD spec)

- uc:SBase.SHud:AddTutoMapMessage
- uc:SBase.SHud:NotifyHotSound
- uc:SBase.SPlayerController:ClientNotifyNoise
- uc:SBase.SPlayerController:AddGPOOnHud
- uc:SBase.SPlayerController:RemoveGPOFromHud

### Animation notify classes not used by SteeE map props

- uc:Engine.AnimNotify_Scripted:Notify
- def:Engine.AnimNotify_Scripted
- def:Engine.AnimNotify_Effect
- def:Engine.AnimNotify_DestroyEffect
- def:Engine.AnimNotify_Script
- def:Engine.AnimNotify_StopSound

### Device classes not placed in SteeE (class defaults and all their functions)

- def:Engine.ZoneTrigger
- uc:Engine.ZoneTrigger:Touch
- uc:Engine.ZoneTrigger:UnTouch
- def:Engine.UseTrigger
- uc:Engine.UseTrigger:UsedBy
- uc:Engine.UseTrigger:Touch
- def:Engine.LineOfSightTrigger
- uc:Engine.LineOfSightTrigger:PostBeginPlay
- uc:Engine.LineOfSightTrigger:PlayerSeesMe
- uc:Engine.LineOfSightTrigger:Trigger
- def:Engine.MaterialTrigger
- uc:Engine.MaterialTrigger:PostBeginPlay
- uc:Engine.MaterialTrigger:Trigger
- def:Engine.TexPannerTriggered
- uc:Engine.TexPannerTriggered:Trigger
- uc:Engine.TexPannerTriggered:Reset
- def:Engine.TexOscillatorTriggered
- uc:Engine.TexOscillatorTriggered:Trigger
- uc:Engine.TexOscillatorTriggered:Reset
- def:SBase.SMessageTrigger
- uc:SBase.SMessageTrigger:PostBeginPlay
- uc:SBase.SMessageTrigger:DisplayMessage
- uc:SBase.SMessageTrigger:Trigger
- uc:SBase.SMessageTrigger:UnTrigger
- rep:SBase.SMessageTrigger.DisplayMessage
- def:SBase.SAlarm
- uc:SBase.SAlarm:AlarmStateChange
- uc:SBase.SAlarm:PawnDefenseEntered
- uc:SBase.SAlarm:IsDefenseInAlarmZone
- uc:SBase.SAlarm:SetAlarmListening
- uc:SBase.SAlarm:RestartCountdown
- uc:SBase.SAlarm:SetAlarmActive
- uc:SBase.SAlarm:SimulateActivated
- uc:SBase.SAlarm:s_Listening.Trigger
- uc:SBase.SAlarm:s_Listening.UnTrigger
- uc:SBase.SAlarm:s_Listening.SimulateActivated
- uc:SBase.SAlarm:s_Listening.Timer
- uc:SBase.SAlarm:s_Listening.EndState
- uc:SBase.SAlarm:s_Active.SimulateActivated
- uc:SBase.SAlarm:s_Active.BeginState
- uc:SBase.SAlarm:s_Active.EndState
- uc:SBase.SAlarm:s_Active.Timer
- uc:SBase.SAlarm:s_Active.Trigger
- uc:SBase.SAlarm:s_Active.UnTrigger
- uc:SBase.SAlarm:s_ActiveDefPresent.BeginState
- uc:SBase.SAlarm:s_ActiveDefPresent.EndState
- uc:SBase.SAlarm:s_ActiveDefPresent.Timer
- uc:SBase.SAlarm:s_ActiveDefPresent.SimulateActivated
- uc:SBase.SAlarm:s_ActiveDefPresent.Trigger
- uc:SBase.SAlarm:s_ActiveDefPresent.UnTrigger
- uc:SBase.SAlarm:s_InitialyActive.BeginState
- uc:SBase.SAlarm:s_InitialyActive.Timer
- rep:SBase.SAlarm.MessagePriority
- rep:SBase.SAlarm.Duration
- rep:SBase.SAlarm.bAlarmOn
- rep:SBase.SAlarm.StartTime
- def:SBase.SLift
- uc:SBase.SLift:PostNetBeginPlay
- uc:SBase.SLift:Trigger
- uc:SBase.SLift:EncroachingOn
- uc:SBase.SLift:KeyFrameReached
- uc:SBase.SLift:PrepareGo.BeginState
- uc:SBase.SLift:PrepareGo.Timer
- uc:SBase.SLift:Go.Arrived
- uc:SBase.SLift:Go@code
- uc:SBase.SLift:WaitDoors.BeginState
- uc:SBase.SLift:WaitDoors.Timer
- def:SBase.SEffectBarrelExplosion
- def:SBase.SObjectiveTrigger
- uc:SBase.SObjectiveTrigger:GetTriggerTime
- uc:SBase.SObjectiveTrigger:IsActive
- uc:SBase.SObjectiveTrigger:Desactivate
- uc:SBase.SObjectiveTrigger:AcceptStickableGPO
- uc:SBase.SObjectiveTrigger:BeginPlay
- uc:SBase.SObjectiveTrigger:PostBeginPlay
- uc:SBase.SObjectiveTrigger:DoReset
- uc:SBase.SObjectiveTrigger:Prepare
- uc:SBase.SObjectiveTrigger:Reset
- uc:SBase.SObjectiveTrigger:GotoState_Inactive
- uc:SBase.SObjectiveTrigger:Triggering
- uc:SBase.SObjectiveTrigger:AddRemoveObjectiveForRadar
- uc:SBase.SObjectiveTrigger:TakeObjective
- uc:SBase.SObjectiveTrigger:Lock
- uc:SBase.SObjectiveTrigger:UnLock
- uc:SBase.SObjectiveTrigger:s_TemporaryInactive.TakeObjective
- uc:SBase.SObjectiveTrigger:s_TemporaryInactive.BeginState
- uc:SBase.SObjectiveTrigger:s_TemporaryInactive.EndState
- uc:SBase.SObjectiveTrigger:s_TemporaryInactive.Lock
- uc:SBase.SObjectiveTrigger:s_TemporaryInactive.UnLock
- uc:SBase.SObjectiveTrigger:s_TemporaryInactive.Reset
- rep:SBase.SObjectiveTrigger.bLocked
- rep:SBase.SObjectiveTrigger.bTriggering
- rep:SBase.SObjectiveTrigger.bActive
- rep:SBase.SObjectiveTrigger.CaptureTime
- rep:SBase.SObjectiveTrigger.TriggerTime
- rep:SBase.SObjectiveTrigger.Objective
- rep:SBase.SObjectiveTrigger.TakeObjective
- rep:SBase.SObjectiveTrigger.AddRemoveObjectiveForRadar
- def:SBase.SBombTargetObjectiveTrigger
- uc:SBase.SBombTargetObjectiveTrigger:Desactivate
- uc:SBase.SBombTargetObjectiveTrigger:CheckOtherTriggers
- uc:SBase.SBombTargetObjectiveTrigger:Triggering
- uc:SBase.SBombTargetObjectiveTrigger:Timer
- uc:SBase.SBombTargetObjectiveTrigger:TakeDamage
- uc:SBase.SBombTargetObjectiveTrigger:Reset
- uc:SBase.SBombTargetObjectiveTrigger:PostBeginPlay
- uc:SBase.SBombTargetObjectiveTrigger:Lock
- uc:SBase.SBombTargetObjectiveTrigger:TakeObjective
- uc:SBase.SBombTargetObjectiveTrigger:s_TemporaryInactive.BeginState
- uc:SBase.SBombTargetObjectiveTrigger:s_TemporaryInactive.EndState
- uc:SBase.SBombTargetObjectiveTrigger:s_TemporaryInactive.UnLock
- uc:SBase.SBombTargetObjectiveTrigger:s_TemporaryInactive.Reset
- rep:SBase.SBombTargetObjectiveTrigger.bSkinState

### Engine routines without a UFunction (native track)

These have no unit ID in the current grammar; they are described as contracts in the files named: the moving-brush
physics step and its glide curve (`mover.md` rule 2), mover encroachment checks and pushing (`mover.md` rule 20),
animation notify dispatch and the one-shot end rule (`animated-mesh.md` rules 9 and 10), the SDamageVolume damage
tick (`damage-volume.md` rule 4), the SMagicEvent queue update (`magic-event.md` rule 5), mover client interpolation
start from `SimInterpolate` (`mover.md` Network), and the native replication lists of Mover, SAnimatedMesh,
SGamePlayObject and SAmbientSoundTrigger.
