# Combat specs index

Area: combat and damage for SCCT Versus (Enhanced 4.0): SBase and Engine script plus the native contracts it
relies on. Unit grammar as in the plan. In the spec files, `uc:<Pkg>.<Class>:<State>.{A, B}` groups several
functions of one state; the expanded one-per-line list is below. `eng:` marks native engine behaviour that has
no UFunction of its own. Annotations in parentheses after a unit in the spec files say which part is covered.

| File | Scope | Units |
|---|---|---|
| [damage-pipeline.md](damage-pipeline.md) | Entry points, PawnTakeDamage (global), StandardReactionsToDamage, DamagePawn_C2S, ClientDamage, hit and death animation RPCs, damage type table | 26 |
| [hit-zones-and-weapons.md](hit-zones-and-weapons.md) | Approximate pill, TraceBone contract, zone tables, merc rifle and Uzi falloff, shotgun parameters, spy shocker gun | 25 |
| [state-damage-reactions.md](state-damage-reactions.md) | Dispatch table of PawnTakeDamage per controller state and every override | 45 |
| [shock-and-taser.md](shock-and-taser.md) | Checkshock, ClientStickyShock, freeze, electric and immunity timers, taser knock-down, merc wake shock, jammer | 20 |
| [death-and-respawn.md](death-and-respawn.md) | Killed, lives, death messages, paths into s_Dead, corpse, spectating, respawn, resign, out of world | 46 |
| [knockout-and-terminate.md](knockout-and-terminate.md) | KO causes and durations, s_FallDown, lying, stand-up, wake protections, smoke faint, revive, merc finishing move | 70 |
| [spy-grab.md](spy-grab.md) | Grab start, approach, hold, releases (neck break, KO, choke, damage), release placement, counter-grab, ledge grab, presence triggers | 170 |
| [melee-elbow-charge-berserk.md](melee-elbow-charge-berserk.md) | Elbow strike (both sides), merc charge, merc berserk spin, merc Use priority | 77 |
| [drop-attack.md](drop-attack.md) | Drop attack detection, damage, victim reactions, fall attack by bodies, sticky blast | 40 |
| [blast-and-fall-damage.md](blast-and-fall-damage.md) | Explosion blast flight and landing, blast KO, fall damage hand-off | 44 |
| [environmental-damage.md](environmental-damage.md) | Mover and lift crush, damage volumes, GPO damage and explosions, thrown objects, environmental kill attribution | 37 |
| [spawn-protection-and-scoring.md](spawn-protection-and-scoring.md) | Spawn protection, spawn shock, AddPoint table, combat statistics | 12 |
| [health.md](health.md) | Health values, spy regeneration, poison, healing | 17 |
| [rumble.md](rumble.md) | Controller rumble: the Vibration_* shapes and priorities | 14 |

Total unit references: 629 (unique 583).

## Units covered, by file

### damage-pipeline.md
- uc:Engine.Controller:PawnTakeDamage
- rep:Engine.Controller.PawnTakeDamage
- uc:SBase.SPlayerController:PawnTakeDamage
- uc:SBase.SPlayerController:StandardReactionsToDamage
- uc:SBase.SPlayerController:DamagePawn_C2S
- rep:SBase.SPlayerController.DamagePawn_C2S
- uc:SBase.SPlayerController:ClientDamage
- rep:SBase.SPlayerController.ClientDamage
- nat:SBase.SPlayerController.ClientDamageHudEffect
- uc:SBase.SPlayerController:PlayAnimDmg
- uc:SBase.SPlayerController:s_PlayerWalking.PlayAnimDmg
- uc:SBase.SPlayerController:s_PlayerJump.PlayAnimDmg
- rep:SBase.SPlayerController.PlayAnimDmg
- uc:SBase.SPlayerController:PlayAnimDeath
- rep:SBase.SPlayerController.PlayAnimDeath
- uc:SBase.SPlayerController:TellClientServerWaitForFatalBlast
- rep:SBase.SPlayerController.TellClientServerWaitForFatalBlast
- uc:SBase.SPlayerController:ClientStopBinoculars
- rep:SBase.SPlayerController.bShootedPawn
- uc:SBase.SBasePawn:TakeDamage
- uc:SBase.SBasePawn:TakeFallingDamage
- uc:SBase.SBasePawn:EncroachedBy
- uc:SBase.SBasePawn:PlayDying
- uc:SBase.SBasePawn:PlayFirstPersonDamage
- def:SBase.SBasePawn
- def:SBase.SPlayerController

### hit-zones-and-weapons.md
- uc:SBase.SBasePawn:GetApproxPillFromHit
- nat:Engine.Actor.TraceBone
- def:SBase.SBasePawn
- def:SBase.SPawnAttaque
- def:SBase.SPAWNDEFENSE
- uc:SBase.SWeaponDefense:FireWeapon
- uc:SBase.SWeaponDefense:ServerSetAccurateRotation
- rep:SBase.SWeaponDefense.ServerSetAccurateRotation
- uc:SBase.SWeaponDefense:ServerFireWeapon
- rep:SBase.SWeapon.ServerFireWeapon
- uc:SBase.SWeaponDefense:TraceFire
- nat:SBase.SWeaponDefense.GetAccuracy
- def:SBase.SWeaponDefense
- def:SBase.SWeaponDefense_Uzi
- def:SBase.SWeaponDefense_Shotgun
- nat:SBase.SWeaponDefense_Shotgun.TraceFire
- uc:SBase.SWeaponAttaque:FireWeapon
- uc:SBase.SWeaponAttaque:ServerFireWeaponWithLocation
- rep:SBase.SWeaponAttaque.ServerFireWeaponWithLocation
- uc:SBase.SWeaponAttaque:TraceFireWithPos
- uc:SBase.SWeaponAttaque:GetAxesAndLocation
- uc:SBase.SStickyShocker:Fire
- def:SBase.SStickyShocker
- uc:SBase.SLethalWeapon:TraceFireWithPos
- def:SBase.SLethalWeapon

### state-damage-reactions.md
- uc:SBase.SPlayerController:s_PlayerWalking.PawnTakeDamage
- uc:SBase.SPlayerController:s_PlayerJump.PawnTakeDamage
- uc:SBase.SPlayerController:s_InteractWithObject.PawnTakeDamage
- uc:SBase.SPlayerController:s_Berserk360.PawnTakeDamage
- uc:SBase.SPlayerController:s_PlayerCharging.PawnTakeDamage
- uc:SBase.SPlayerController:s_Blast.PawnTakeDamage
- uc:SBase.SPlayerController:s_BlastFallAfterObstacle.PawnTakeDamage
- uc:SBase.SPlayerController:s_GrabBaseWithBlastDamageOnly.PawnTakeDamage
- uc:SBase.SPlayerController:s_GrabFromLedgeBase.PawnTakeDamage
- uc:SBase.SPlayerController:s_GrabbedByPlayer.PawnTakeDamage
- uc:SBase.SPlayerController:s_GrabbingPlayer.PawnTakeDamage
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer.PawnTakeDamage
- uc:SBase.SPlayerController:s_FallDown.PawnTakeDamage
- uc:SBase.SPlayerController:s_TerminatePawn_Base.PawnTakeDamage
- uc:SBase.SPlayerController:s_Coop_S_Base.PawnTakeDamage
- uc:SBase.SPlayerController:s_Coop_S_Goto.PawnTakeDamage
- uc:SBase.SPlayerController:s_ShortLadder_P_Base.PawnTakeDamage
- uc:SBase.SPlayerController:s_BTW_Base.PawnTakeDamage
- uc:SBase.SPlayerController:s_Binoculars_Def.PawnTakeDamage
- uc:SBase.SPlayerController:s_Binoculars_Att.PawnTakeDamage
- uc:SBase.SPlayerController:s_UseObject_Mirador.PawnTakeDamage
- uc:SBase.SPlayerController:s_UseObject_StickyCam_SmoothToPosition.PawnTakeDamage
- uc:SBase.SPlayerController:s_UseObject_StickyCam.PawnTakeDamage
- uc:SBase.SPlayerController:s_Maintain_Enter.PawnTakeDamage
- uc:SBase.SPlayerController:s_Maintain_Use.PawnTakeDamage
- uc:SBase.SPlayerController:s_Maintain_Leave.PawnTakeDamage
- uc:SBase.SPlayerController:s_Hack_Enter.PawnTakeDamage
- uc:SBase.SPlayerController:s_Hack_Use.PawnTakeDamage
- uc:SBase.SPlayerController:s_Hack_Leave.PawnTakeDamage
- uc:SBase.SPlayerController:s_Computer_Enter.PawnTakeDamage
- uc:SBase.SPlayerController:s_Computer_Use.PawnTakeDamage
- uc:SBase.SPlayerController:s_Computer_Leave.PawnTakeDamage
- uc:SBase.SPlayerController:s_PutFlagInDropZone.PawnTakeDamage
- uc:SBase.SPlayerController:s_PlaceBomb.PawnTakeDamage
- uc:SBase.SPlayerController:s_PlaceBomb_End.PawnTakeDamage
- uc:SBase.SPlayerController:s_GrabBase.ClientDamage
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer.ClientDamage
- uc:SBase.SPlayerController:s_Berserk360.ClientDamage
- uc:SBase.SPlayerController:s_PlayerCharging.ClientDamage
- (no unit) the states s_BlastFloor, s_PlayerFalling_OutOfWorld, s_Dead, s_BaseSpectating, GameEnded and s_Cinematic,
  which only ignore PawnTakeDamage

### shock-and-taser.md
- uc:SBase.SPlayerController:Checkshock
- uc:SBase.SPlayerController:s_Inactive.Checkshock
- uc:SBase.SPlayerController:s_PlayerCharging_Hit.Checkshock
- uc:SBase.SPlayerController:ClientStickyShock
- rep:SBase.SPlayerController.ClientStickyShock
- uc:SBase.SPlayerController:s_TerminatePawn_Base.ClientStickyShock
- uc:SBase.SPlayerController:s_GrabbingPlayer.ClientStickyShock
- uc:SBase.SPlayerController:s_PlayerCharging.ClientStickyShock
- uc:SBase.SPlayerController:s_Coop_S_Goto.ClientStickyShock
- uc:SBase.SPlayerController:s_Hoist_S_Jump.ClientStickyShock
- uc:SBase.SPlayerController:s_ShortLadder_S_Jump.ClientStickyShock
- uc:SBase.SPlayerController:PlayerTick
- uc:SBase.SPlayerController:Client_Jammed
- rep:SBase.SPlayerController.Client_Jammed
- uc:SBase.SPlayerController:s_StandUp.BeginState
- uc:SBase.SGadTazerLauncher:DamagePawn
- nat:SBase.SGadTazerLauncher.UseBulletSet
- def:SBase.SPawnAttaque
- def:SBase.SPAWNDEFENSE
- def:SBase.SPlayerController

### death-and-respawn.md
- uc:SBase.SGameInfo:Killed
- uc:SBase.SGameInfo:BroadcastDeathMessage
- uc:SBase.SGameInfo:UpdateTeamNumLives
- uc:SBase.SGameInfo:sCheckEndGame
- uc:SBase.SDeathMessage:GetString
- def:SBase.SDeathMessage
- uc:SBase.SPlayerController:s_Dead.BeginState
- uc:SBase.SPlayerController:s_Dead.EndState
- uc:SBase.SPlayerController:s_Dead.Timer
- uc:SBase.SPlayerController:s_Dead.PlayerMove
- uc:SBase.SPlayerController:s_Dead.PlayerTick
- uc:SBase.SPlayerController:s_Dead.Fire
- uc:SBase.SPlayerController:s_Dead.ServerReStartPlayer
- uc:SBase.SPlayerController:s_Dead.bIsDamageVolumeSensitive
- uc:SBase.SPlayerController:s_MultiSpectating_WhenKilled.BeginState
- uc:SBase.SPlayerController:s_MultiSpectating_WhenKilled.Timer
- uc:SBase.SPlayerController:s_WaitRespawn@code
- uc:SBase.SPlayerController:s_WaitRespawn.ServerReStartPlayer
- uc:SBase.SPlayerController:s_WaitRespawn.EndState
- uc:SBase.SPlayerController:ServerRespawn
- uc:SBase.SPlayerController:Respawn
- uc:SBase.SPlayerController:UnPossess
- uc:SBase.SPlayerController:AntiBugTellServerIAmDead
- uc:SBase.SPlayerController:Resign
- uc:SBase.SPlayerController:ServerResign
- uc:SBase.SPlayerController:PlayerFallingInerte.BeginState
- uc:SBase.SPlayerController:PlayerFallingInerte.Timer
- uc:SBase.SPlayerController:PlayerFallingInerte.MyNotifyLanded
- uc:SBase.SPlayerController:PlayerFallingInerte.NotifyBump
- uc:SBase.SPlayerController:PlayerFallingInerte.DropAttack
- uc:SBase.SPlayerController:s_LandingInerte.MyFalling
- uc:SBase.SPlayerController:s_LandingInerte.AnimEnd
- uc:SBase.SPlayerController:s_FallDownDying.BeginState
- uc:SBase.SPlayerController:s_FallDownDying.AnimEnd
- uc:SBase.SPlayerController:s_FallDownDying@code
- uc:SBase.SPlayerController:FellOutOfWorld
- uc:SBase.SPlayerController:s_PlayerFalling_OutOfWorld.BeginState
- uc:SBase.SPlayerController:s_PlayerFalling_OutOfWorld.EndState
- uc:SBase.SPlayerController:s_PlayerFalling_OutOfWorld.PlayerTick
- uc:SBase.SPlayerController:s_PlayerFalling_OutOfWorld.MyNotifyLanded
- uc:SBase.SPlayerController:ReinitPawnBecauseOfDivergences
- uc:SBase.SBasePawn:SetVelocityForDyingPawn
- uc:SBase.SPlayerController:ServerSetVelocityForDyingPawn
- uc:SBase.SBasePawn:AnimEnd
- nat:SBase.SBasePawn.ChooseDeathAnimation
- def:SBase.SPlayerController

### knockout-and-terminate.md
- uc:SBase.SPlayerController:s_Inactive.PlayerMove
- uc:SBase.SPlayerController:s_Inactive.PlayerTick
- uc:SBase.SPlayerController:s_Inactive.CanActivateLaser
- uc:SBase.SPlayerController:s_Inactive.HearPC
- uc:SBase.SPlayerController:s_FallDown.BeginState
- uc:SBase.SPlayerController:s_FallDown.AnimEnd
- uc:SBase.SPlayerController:s_FallDown.PlayerTick
- uc:SBase.SPlayerController:s_FallDown@code
- uc:SBase.SPlayerController:s_LyingOnGround.BeginState
- uc:SBase.SPlayerController:s_LyingOnGround.Timer
- uc:SBase.SPlayerController:s_LyingOnGround.EndState
- uc:SBase.SPlayerController:s_LyingOnGround.PlayerTick
- uc:SBase.SPlayerController:s_LyingOnGround.CanBeReanimated
- uc:SBase.SPlayerController:s_StandUp.BeginState
- uc:SBase.SPlayerController:s_StandUp.AnimEnd
- uc:SBase.SPlayerController:s_StandUp.EndState
- uc:SBase.SPlayerController:s_StandUp.PlayerTick
- uc:SBase.SPlayerController:s_StandUp.ExplosionHit
- uc:SBase.SPlayerController:s_Reanimating.BeginState
- uc:SBase.SPlayerController:PlayerFallingKO.BeginState
- uc:SBase.SPlayerController:PlayerFallingKO.MyNotifyLanded
- uc:SBase.SPlayerController:FindPawnToReanimate
- uc:SBase.SPlayerController:FindPawnToReanimateOrTerminate
- uc:SBase.SPlayerController:AskServerToReanimatePawn
- rep:SBase.SPlayerController.AskServerToReanimatePawn
- uc:SBase.SPlayerController:CanBeReanimated
- uc:SBase.SPlayerController:s_ReanimateFriend_0H.BeginState
- uc:SBase.SPlayerController:s_ReanimateFriend_0H.AnimEnd
- uc:SBase.SPlayerController:s_ReanimateFriend_0H.PlayerMove
- uc:SBase.SPlayerController:s_ReanimateFriend_2H.BeginState
- uc:SBase.SPlayerController:s_ReanimateFriend_2H.AnimEnd
- uc:SBase.SPlayerController:s_ReanimateFriend_2H.PlayerMove
- uc:SBase.SPlayerController:AskServerToTerminatePawn
- rep:SBase.SPlayerController.AskServerToTerminatePawn
- uc:SBase.SPlayerController:bIsTerminatingPawn
- uc:SBase.SPlayerController:MercCantTerminate
- uc:SBase.SPlayerController:s_TerminatePawn_Base.BeginState
- uc:SBase.SPlayerController:s_TerminatePawn_Base.MyFalling
- uc:SBase.SPlayerController:s_TerminatePawn_Base.PlayerMove
- uc:SBase.SPlayerController:s_TerminatePawn_Base.ReceiveAnEvent
- uc:SBase.SPlayerController:s_TerminatePawn_Base.ServerSendAnEvent
- uc:SBase.SPlayerController:s_TerminatePawn_Base.SendAnEventToServer
- uc:SBase.SPlayerController:s_TerminatePawn_ReposBegin.BeginState
- uc:SBase.SPlayerController:s_TerminatePawn_ReposBegin.Timer
- uc:SBase.SPlayerController:s_TerminatePawn_ReposBegin.TopSynchro
- uc:SBase.SPlayerController:s_TerminatePawn.BeginState
- uc:SBase.SPlayerController:s_TerminatePawn.AnimEnd
- uc:SBase.SPlayerController:s_TerminatePawn.PlayerMove
- uc:SBase.SPlayerController:s_TerminatePawn_Maintain.BeginState
- uc:SBase.SPlayerController:s_TerminatePawn_Maintain.Timer
- uc:SBase.SPlayerController:s_TerminatePawn_Maintain.PlayerMove
- uc:SBase.SPlayerController:s_TerminatePawn_Kill.BeginState
- uc:SBase.SPlayerController:s_TerminatePawn_Kill.EndState
- uc:SBase.SPlayerController:s_TerminatePawn_Kill.AnimEnd
- uc:SBase.SPlayerController:s_TransitionTPK.BeginState
- uc:SBase.SPlayerController:s_TerminatePawn_Fall.BeginState
- uc:SBase.SPlayerController:s_TerminatePawn_Fall.EndState
- uc:SBase.SPlayerController:s_TerminatedPawn.BeginState
- uc:SBase.SPlayerController:s_TerminatedPawn.Timer
- uc:SBase.SPlayerController:s_TerminatedPawn.MercCantTerminate
- uc:SBase.SPlayerController:s_TerminatedPawn_Released.BeginState
- uc:SBase.SPlayerController:GoInSmoke
- uc:SBase.SPlayerController:PlayerTick
- uc:SBase.SPlayerController:FindGasInstigator
- uc:SBase.SPlayerController:CountGasKO
- uc:SBase.SPlayerController:PCChangeServerState
- uc:SBase.SPlayerController:GotoWaitingState
- rep:SBase.SPlayerController.GotoWaitingState
- rep:SBase.SBasePawn.m_PawnToTerminate
- def:SBase.SPlayerController

### spy-grab.md
- uc:SBase.SPlayerController:AskToGrabPawn
- uc:SBase.SPlayerController:OnLedge.AskToGrabPawn
- uc:SBase.SPlayerController:CanGrabPawn
- uc:SBase.SPlayerController:OnLedge.CanGrabPawn
- uc:SBase.SPlayerController:CanBeGrabbed
- uc:SBase.SPlayerController:AskServerToGrabPawn
- rep:SBase.SPlayerController.AskServerToGrabPawn
- uc:SBase.SPlayerController:AskServerToGrabPawnFromLedge
- rep:SBase.SPlayerController.AskServerToGrabPawnFromLedge
- uc:SBase.SPlayerController:MirrorStateForMercGrabbedFromLedge
- uc:SBase.SPlayerController:PCChangeClientStateForMercGrabbedFromLedge
- rep:SBase.SPlayerController.PCChangeClientStateForMercGrabbedFromLedge
- uc:SBase.SPlayerController:AskServerToUnGrabPawn
- rep:SBase.SPlayerController.AskServerToUnGrabPawn
- uc:SBase.SPlayerController:AskServerToUnGrabPawnAndFreeGuy
- rep:SBase.SPlayerController.AskServerToUnGrabPawnAndFreeGuy
- uc:SBase.SPlayerController:CheckForRoomToFreeGrabbedGuy
- uc:SBase.SPlayerController:GetFreeLocationToRelease
- uc:SBase.SPlayerController:CheckForGround
- uc:SBase.SPlayerController:GrabServerReleaseRequest
- rep:SBase.SPlayerController.GrabServerReleaseRequest
- uc:SBase.SPlayerController:GrabAborted
- rep:SBase.SPlayerController.GrabAborted
- uc:SBase.SPlayerController:CountMercGrabDeath
- uc:SBase.SPlayerController:ServerSetVoiceComWhenGrabbing
- uc:SBase.SPlayerController:DampRotation
- uc:SBase.SBasePawn:GrabSendServerCounterValue
- rep:SBase.SBasePawn.GrabSendServerCounterValue
- uc:SBase.SBasePawn:AnimEnd
- uc:SBase.SPlayerController:s_GrabBase.ClientDamage
- uc:SBase.SPlayerController:s_GrabBase.CanActivateLaser
- uc:SBase.SPlayerController:s_GrabBase.CanInteract
- uc:SBase.SPlayerController:s_GrabBase.DoInteraction
- uc:SBase.SPlayerController:s_GrabBase.ExplosionHit
- uc:SBase.SPlayerController:s_Grabbing_UncrouchForGrab.DoInteraction
- uc:SBase.SPlayerController:s_Grabbing_UncrouchForGrab.BeginState
- uc:SBase.SPlayerController:s_Grabbing_UncrouchForGrab.AnimEnd
- uc:SBase.SPlayerController:s_Grabbing_UncrouchForGrab.PlayerMove
- uc:SBase.SPlayerController:s_Grabbing_GotoPosition.DoInteraction
- uc:SBase.SPlayerController:s_Grabbing_GotoPosition.EndState
- uc:SBase.SPlayerController:s_Grabbing_GotoPosition.BeginState
- uc:SBase.SPlayerController:s_Grabbing_GotoPosition.PlayerTick
- uc:SBase.SPlayerController:s_Grabbing_GotoPosition.NotifyBump
- uc:SBase.SPlayerController:s_Grabbing_GotoPosition.PlayerMove
- uc:SBase.SPlayerController:s_Grabbing_GotoPosition.TopSynchro
- uc:SBase.SPlayerController:s_Grab_GotoPosition.NotifyBump
- uc:SBase.SPlayerController:s_Grab_GotoPosition.EndState
- uc:SBase.SPlayerController:s_Grab_GotoPosition.BeginState
- uc:SBase.SPlayerController:s_Grab_GotoPosition.PlayerMove
- uc:SBase.SPlayerController:s_Grab_GotoPosition.TopSynchro
- uc:SBase.SPlayerController:s_Grabbing_GoToPosToFreeGuy.BeginState
- uc:SBase.SPlayerController:s_Grabbing_GoToPosToFreeGuy.TopSynchro
- uc:SBase.SPlayerController:s_GrabbingPlayer_Begin.performDropAttack
- uc:SBase.SPlayerController:s_GrabbingPlayer_Begin.PlayerMove
- uc:SBase.SPlayerController:s_GrabbingPlayer_RealBegin.GrabAborted
- uc:SBase.SPlayerController:s_GrabbingPlayer_RealBegin.performDropAttack
- uc:SBase.SPlayerController:s_GrabbingPlayer_RealBegin.AnimEnd
- uc:SBase.SPlayerController:s_GrabbingPlayer_RealBegin.BeginState
- uc:SBase.SPlayerController:s_GrabbingPlayer_RealBegin.PlayerMove
- uc:SBase.SPlayerController:s_GrabbingPlayer.CanUseCam
- uc:SBase.SPlayerController:s_GrabbingPlayer.VoiceChatButtonToggle
- uc:SBase.SPlayerController:s_GrabbingPlayer.performDropAttack
- uc:SBase.SPlayerController:s_GrabbingPlayer.GoInSmoke
- uc:SBase.SPlayerController:s_GrabbingPlayer.CanBeDropAttacked
- uc:SBase.SPlayerController:s_GrabbingPlayer.CanActivateLaser
- uc:SBase.SPlayerController:s_GrabbingPlayer.CanUncrouchAndUndraw
- uc:SBase.SPlayerController:s_GrabbingPlayer.CanInteract
- uc:SBase.SPlayerController:s_GrabbingPlayer.DoInteraction
- uc:SBase.SPlayerController:s_GrabbingPlayer.CanIUseBinocular
- uc:SBase.SPlayerController:s_GrabbingPlayer.PawnTakeDamage
- uc:SBase.SPlayerController:s_GrabbingPlayer.ClientStickyShock
- uc:SBase.SPlayerController:s_GrabbingPlayer.Timer
- uc:SBase.SPlayerController:s_GrabbingPlayer.PlayerTick
- uc:SBase.SPlayerController:s_GrabbingPlayer.Fire
- uc:SBase.SPlayerController:s_GrabbingPlayer.EndState
- uc:SBase.SPlayerController:s_GrabbingPlayer.BeginState
- uc:SBase.SPlayerController:s_GrabbingPlayer.UpdateRotation
- uc:SBase.SPlayerController:s_GrabbingPlayer.PlayerMove
- uc:SBase.SPlayerController:s_GrabbingPlayer.UpdatePawnRotationMario
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Begin.PlayerTick
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Begin.BeginState
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Begin.Timer
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Begin.EndState
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Begin.GrabAborted
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Begin.PlayerMove
- uc:SBase.SPlayerController:s_GrabbedByPlayer.GoInSmoke
- uc:SBase.SPlayerController:s_GrabbedByPlayer.PawnTakeDamage
- uc:SBase.SPlayerController:s_GrabbedByPlayer.EndState
- uc:SBase.SPlayerController:s_GrabbedByPlayer.BeginState
- uc:SBase.SPlayerController:s_GrabbedByPlayer.AnimEnd
- uc:SBase.SPlayerController:s_GrabbedByPlayer.PlayerTick
- uc:SBase.SPlayerController:s_GrabbedByPlayer.PlayerMove
- uc:SBase.SPlayerController:s_GrabbedByPlayer_AvoidGrab.BeginState
- uc:SBase.SPlayerController:s_GrabbedByPlayer_AvoidGrab.PlayerMove
- uc:SBase.SPlayerController:s_Grab_Avoided.BeginState
- uc:SBase.SPlayerController:s_Grab_Avoided.PlayerMove
- uc:SBase.SPlayerController:s_AbortGrab.BeginState
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Released.BeginState
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Released.AnimEnd
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Released.PlayerMove
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_KO.BeginState
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_KO.AnimEnd
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_STRANGLE.BeginState
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_STRANGLE.EndState
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_STRANGLE.PlayerTick
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_STRANGLE.AnimEnd
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_DMG.AnimEnd
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_Grabbing_LD.BeginState
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_Grabbing_LD.EndState
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_GrabbingDMG.BeginState
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_GrabbingDMG.AnimEnd
- uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_GrabbingDMG.EndState
- uc:SBase.SPlayerController:s_GrabbedByPlayer_CounterGrab.BeginState
- uc:SBase.SPlayerController:s_GrabbedByPlayer_CounterGrab.PlayerMove
- uc:SBase.SPlayerController:s_GrabbedByPlayer_CounterGrab.AnimEnd
- uc:SBase.SPlayerController:s_GrabbedByPlayer_CounterGrab.EndState
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer.ClientDamage
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer.PawnTakeDamage
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer.PlayerMove
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer.AnimEnd
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer.BeginState
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer.EndState
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_LD.BeginState
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_KO.BeginState
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_Strangle.BeginState
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_DMG.BeginState
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_DMG.AnimEnd
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_DMG.EndState
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_BothDMG.BeginState
- uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_Grabbed_DMGED.BeginState
- uc:SBase.SPlayerController:s_GrabRelease_Counter.BeginState
- uc:SBase.SPlayerController:s_GrabRelease_Counter.AnimEnd
- uc:SBase.SPlayerController:s_GrabRelease_Counter.EndState
- uc:SBase.SPlayerController:s_GrabBaseWithBlastDamageOnly.PawnTakeDamage
- uc:SBase.SPlayerController:s_GrabFromLedgeBase.PawnTakeDamage
- uc:SBase.SPlayerController:s_GrabbingFromLedge_Begin.BeginState
- uc:SBase.SPlayerController:s_GrabbingFromLedge_Begin.Timer
- uc:SBase.SPlayerController:s_GrabbingFromLedge_Begin.PlayerMove
- uc:SBase.SPlayerController:s_GrabbingFromLedge_Begin.AnimEnd
- uc:SBase.SPlayerController:s_GrabbingFromLedge_Begin.ReceiveAnEvent
- uc:SBase.SPlayerController:s_GrabbingFromLedge_Begin.ServerSendAnEvent
- uc:SBase.SPlayerController:s_GrabbingFromLedge_Begin.SendAnEventToServer
- uc:SBase.SPlayerController:s_GrabbingFromLedge_Go.PlayerMove
- uc:SBase.SPlayerController:s_GrabbingFromLedge_Go.AnimEnd
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.bIsDamageVolumeSensitive
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.TopSynchro
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.NotifyBump
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.Timer
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.PlayerTick
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.BeginState
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.EndState
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.GrabAborted
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.AltFire
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.Fire
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.GadLaserSwitch
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.GadTorcheSwitch
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.GadgetQuickChoice
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.SwitchBinocularVision
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.SwitchROF
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.PlayerMove
- uc:SBase.SPlayerController:s_FallAfterGrabbedFromLedge.bIsDamageVolumeSensitive
- uc:SBase.SPlayerController:s_FallAfterGrabbedFromLedge.NotifyBump
- uc:SBase.SPlayerController:s_FallAfterGrabbedFromLedge.MyNotifyLanded
- uc:SBase.SPlayerController:s_FallAfterGrabbedFromLedge.PlayerMove
- uc:SBase.SPresenceTrigger:Touch
- def:SBase.SPresenceTrigger
- rep:SBase.SBasePawn.m_AttackTarget
- rep:SBase.SBasePawn.m_AttackedBy
- rep:SBase.SBasePawn.bCanBeGrabbed
- def:SBase.SPlayerController

### melee-elbow-charge-berserk.md
- uc:SBase.SPlayerController:FindPawnToElbowShotAndGoForIt
- uc:SBase.SPlayerController:CanElbowShock
- uc:SBase.SPlayerController:s_PlayerWalking.CanElbowShock
- uc:SBase.SPlayerController:CanBeElbowShocked
- uc:SBase.SPlayerController:s_PlayerWalking.CanBeElbowShocked
- uc:SBase.SPlayerController:s_PlayerJump.CanBeElbowShocked
- uc:SBase.SPlayerController:AskServerToElbowAttack
- rep:SBase.SPlayerController.AskServerToElbowAttack
- uc:SBase.SPlayerController:Handle_ElbowDmg
- uc:SBase.SPlayerController:ForceRotateTo
- rep:SBase.SPlayerController.ForceRotateTo
- uc:SBase.SPlayerController:s_ElbowShooting.BeginState
- uc:SBase.SPlayerController:s_ElbowShooting.EndState
- uc:SBase.SPlayerController:s_ElbowShooting.AnimEnd
- uc:SBase.SPlayerController:s_ElbowShooting.PlayerMove
- uc:SBase.SPlayerController:s_ElbowShooting@code
- uc:SBase.SPlayerController:s_ElbowShot_GoBackward.BeginState
- uc:SBase.SPlayerController:s_ElbowShot_GoBackward.EndState
- uc:SBase.SPlayerController:s_ElbowShot_GoBackward.AnimEnd
- uc:SBase.SPlayerController:s_ElbowShot_GoBackward.PlayerMove
- uc:SBase.SPlayerController:s_PlayerWalkingWeapon.PlayerMove
- uc:SBase.SPlayerController:s_PlayerWalkingMario.PlayerMove
- uc:SBase.SPlayerController:FindTargetToCharge
- uc:SBase.SPlayerController:s_PlayerCharging.PawnTakeDamage
- uc:SBase.SPlayerController:s_PlayerCharging.ClientStickyShock
- uc:SBase.SPlayerController:s_PlayerCharging.ClientDamage
- uc:SBase.SPlayerController:s_PlayerCharging.MyFalling
- uc:SBase.SPlayerController:s_PlayerCharging.GoInSmoke
- uc:SBase.SPlayerController:s_PlayerCharging.CanActivateLaser
- uc:SBase.SPlayerController:s_PlayerCharging.NotifyHitWall
- uc:SBase.SPlayerController:s_PlayerCharging.NotifyBump
- uc:SBase.SPlayerController:s_PlayerCharging.EndState
- uc:SBase.SPlayerController:s_PlayerCharging.BeginState
- uc:SBase.SPlayerController:s_PlayerCharging.CanBeDropAttacked
- uc:SBase.SPlayerController:s_PlayerCharging.Timer
- uc:SBase.SPlayerController:s_PlayerCharging.FinDeCharge
- uc:SBase.SPlayerController:s_PlayerCharging.PlayerMove
- uc:SBase.SPlayerController:s_PlayerCharging.ComputeTargetDistance
- uc:SBase.SPlayerController:s_PlayerCharging.PlayerTick
- uc:SBase.SPlayerController:s_PlayerCharging_Hit.CanBeGrabbed
- uc:SBase.SPlayerController:s_PlayerCharging_Hit.BeginState
- uc:SBase.SPlayerController:s_PlayerCharging_Hit.CanActivateLaser
- uc:SBase.SPlayerController:s_PlayerCharging_Hit.EndState
- uc:SBase.SPlayerController:s_PlayerCharging_Hit.Checkshock
- uc:SBase.SPlayerController:s_PlayerCharging_Hit.PlayerMove
- uc:SBase.SPlayerController:s_PlayerCharging_Hit.AnimEnd
- uc:SBase.SPlayerController:s_PlayerCharging_HitEnemy.BeginState
- uc:SBase.SPlayerController:s_PlayerCharging_HitFriend.BeginState
- uc:SBase.SPlayerController:s_PlayerCharging_HitWall.BeginState
- uc:SBase.SPlayerController:s_PlayerCharging_HitStickyShocked.CanBeGrabbed
- uc:SBase.SPlayerController:s_PlayerCharging_HitStickyShocked.BeginState
- uc:SBase.SPlayerController:s_PlayerCharging_HitStickyShocked.Timer
- uc:SBase.SPlayerController:s_PlayerCharging_HitStickyShocked.PlayAnimDmg
- uc:SBase.SPlayerController:s_PlayerCharging_HitStickyShocked.AnimEnd
- uc:SBase.SPlayerController:Handle_ChargedDmg
- uc:SBase.SPlayerController:s_Charge_GoBackward.BeginState
- uc:SBase.SPlayerController:s_Charge_GoBackward.EndState
- uc:SBase.SPlayerController:s_Charge_GoBackward.MyFalling
- uc:SBase.SPlayerController:s_Charge_GoBackward.AnimEnd
- uc:SBase.SPlayerController:s_Charge_GoBackward.PlayerMove
- uc:SBase.SPlayerController:s_Berserk360.EndState
- uc:SBase.SPlayerController:s_Berserk360.BeginState
- uc:SBase.SPlayerController:s_Berserk360.CanBeDropAttacked
- uc:SBase.SPlayerController:s_Berserk360.CanBeBlasted
- uc:SBase.SPlayerController:s_Berserk360.ExplosionHit
- uc:SBase.SPlayerController:s_Berserk360.PawnTakeDamage
- uc:SBase.SPlayerController:s_Berserk360.ClientDamage
- uc:SBase.SPlayerController:s_Berserk360.MyFalling
- uc:SBase.SPlayerController:s_Berserk360.CanActivateLaser
- uc:SBase.SPlayerController:s_Berserk360.NotifyHitWall
- uc:SBase.SPlayerController:s_Berserk360.NotifyBump
- uc:SBase.SPlayerController:s_Berserk360.AnimEnd
- uc:SBase.SPlayerController:s_Berserk360.PlayerMove
- uc:SBase.SPlayerController:DestroyGPO
- rep:SBase.SPlayerController.DestroyGPO
- uc:SBase.SPlayerController:Client_Vibration_Charge_Obstacle
- def:SBase.SPlayerController

### drop-attack.md
- uc:SBase.SPlayerController:DropAttack
- uc:SBase.SPlayerController:s_PlayerWalking.DropAttack
- uc:SBase.SPlayerController:s_PlayerRolling.DropAttack
- uc:SBase.SPlayerController:s_PlayerRolling.performDropAttack
- uc:SBase.SPlayerController:s_ZipLine.DropAttack
- uc:SBase.SPlayerController:PlayerFalling.DropAttack
- uc:SBase.SPlayerController:PlayerFallingInerte.DropAttack
- uc:SBase.SPlayerController:s_Coop_S_Base.DropAttack
- uc:SBase.SPlayerController:s_Coop_S_Base.performDropAttack
- uc:SBase.SPlayerController:s_Binoculars_Def.DropAttack
- uc:SBase.SPlayerController:s_Binoculars_Att.DropAttack
- uc:SBase.SPlayerController:performDropAttack
- uc:SBase.SPlayerController:s_GrabbingPlayer_Begin.performDropAttack
- uc:SBase.SPlayerController:s_GrabbingPlayer_RealBegin.performDropAttack
- uc:SBase.SPlayerController:s_GrabbingPlayer.performDropAttack
- uc:SBase.SPlayerController:TellServerForDropAttack
- rep:SBase.SPlayerController.TellServerForDropAttack
- uc:SBase.SPlayerController:TellServerToFallAttack
- rep:SBase.SPlayerController.TellServerToFallAttack
- uc:SBase.SPlayerController:CanBeDropAttacked
- uc:SBase.SPlayerController:DropAttack_PawnFound
- uc:SBase.SPlayerController:DropAttackedDontMove
- uc:SBase.SPlayerController:s_DropAttacked_Incoming.BeginState
- uc:SBase.SPlayerController:s_DropAttacked_Incoming.DropAttackedDontMove
- uc:SBase.SPlayerController:s_DropAttacked_Incoming.PlayerTick
- uc:SBase.SPlayerController:s_DropAttacked_Incoming.PlayerMove
- uc:SBase.SPlayerController:Handle_DropAttackedDmg
- uc:SBase.SPlayerController:s_FallDropAttacked.BeginState
- uc:SBase.SPlayerController:s_FallDropAttacked.MyNotifyLanded
- uc:SBase.SPlayerController:s_FallDropAttacked.PlayerMove
- uc:SBase.SPlayerController:s_FallFallDropAttacked.MyNotifyLanded
- uc:SBase.SPlayerController:s_DropAttack_Landing.EndState
- uc:SBase.SPlayerController:s_DropAttack_Landing.AnimEnd
- uc:SBase.SPlayerController:s_DropAttack_Landing.BeginState
- uc:SBase.SPlayerController:s_DropAttack_Landing.PlayerMove
- uc:SBase.SPlayerController:StickyBlast
- uc:SBase.SPlayerController:TellClientToStickyBlast
- rep:SBase.SPlayerController.TellClientToStickyBlast
- uc:SBase.SPlayerController:s_FallAfterGrabbedFromLedge.NotifyBump
- uc:SBase.SPlayerController:PlayerFallingInerte.NotifyBump

### blast-and-fall-damage.md
- uc:SBase.SPlayerController:ExplosionHit
- uc:SBase.SPlayerController:CanBeBlasted
- uc:SBase.SPlayerController:s_PlayerWalking.CanBeBlasted
- uc:SBase.SPlayerController:s_PlayerWalking.ExplosionHit
- uc:SBase.SPlayerController:s_PlayerJump.CanBeBlasted
- uc:SBase.SPlayerController:s_PlayerJump.ExplosionHit
- uc:SBase.SPlayerController:s_PlayerRolling.CanBeBlasted
- uc:SBase.SPlayerController:s_PlayerRolling.ExplosionHit
- uc:SBase.SPlayerController:s_Berserk360.CanBeBlasted
- uc:SBase.SPlayerController:s_Berserk360.ExplosionHit
- uc:SBase.SPlayerController:s_BTW.CanBeBlasted
- uc:SBase.SPlayerController:s_BTW.ExplosionHit
- uc:SBase.SPlayerController:s_Coop_S_Base.CanBeBlasted
- uc:SBase.SPlayerController:s_Coop_S_Base.ExplosionHit
- uc:SBase.SPlayerController:GrabFence.CanBeBlasted
- uc:SBase.SPlayerController:GrabFence.ExplosionHit
- uc:SBase.SPlayerController:OnFence.CanBeBlasted
- uc:SBase.SPlayerController:OnFence.ExplosionHit
- uc:SBase.SPlayerController:s_GrabBase.ExplosionHit
- uc:SBase.SPlayerController:s_TerminatePawn_Base.ExplosionHit
- uc:SBase.SPlayerController:s_StandUp.ExplosionHit
- uc:SBase.SPlayerController:s_Blast.BeginState
- uc:SBase.SPlayerController:s_Blast.PlayerMove
- uc:SBase.SPlayerController:s_Blast.MyNotifyLanded
- uc:SBase.SPlayerController:s_Blast.CanBeBlasted
- uc:SBase.SPlayerController:s_BlastFallAfterObstacle.MyNotifyLanded
- uc:SBase.SPlayerController:s_BlastFallAfterObstacle.PlayerMove
- uc:SBase.SPlayerController:s_KOAfterBlastFall.AnimEnd
- uc:SBase.SPlayerController:s_SleepAfterKOBlastFall.PlayerMove
- uc:SBase.SPlayerController:s_BlastFloor.AnimEnd
- uc:SBase.SPlayerController:s_BlastFloor.PlayerMove
- uc:SBase.SPlayerController:s_BlastWakeUp.AnimEnd
- uc:SBase.SPlayerController:s_BlastWakeUp.PlayerMove
- uc:SBase.SBasePawn:SetVelocityAndOrientationForBlast
- uc:SBase.SPlayerController:ServerSetVelocityAndOrientationForBlast
- rep:SBase.SPlayerController.ServerSetVelocityAndOrientationForBlast
- uc:SBase.SPlayerController:s_PlayerJump.EndState
- uc:SBase.SPlayerController:s_PlayerJump.NotifyBump
- uc:SBase.SPlayerController:MyFalling
- uc:SBase.SPlayerController:MyNotifyLanded
- def:SBase.SBasePawn
- def:SBase.SPawnAttaque
- def:SBase.SPAWNDEFENSE
- def:SBase.SPlayerController

### environmental-damage.md
- uc:Engine.Mover:EncroachingOn
- uc:Engine.Mover:Trigger
- uc:Engine.Mover:TriggerOpenOnceOnly.SubTrigger
- def:Engine.Mover
- uc:SBase.SLift:EncroachingOn
- uc:Engine.Actor:CrushedBy
- uc:Engine.Actor:HurtRadius
- uc:SBase.SBasePawn:CrushedBy
- uc:SBase.SBasePawn:KilledBy
- uc:SBase.SGameInfo:CountEnvironmentalKill
- uc:SBase.SGameInfo:FindDamageVolume
- uc:SBase.SDamageVolume:Touch
- uc:SBase.SDamageVolume:Trigger
- uc:SBase.SDamageVolume:UnTrigger
- def:SBase.SDamageVolume
- eng:SBase.SDamageVolume.Tick
- uc:SBase.SPlayerController:bIsDamageVolumeSensitive
- uc:SBase.SPlayerController:s_Dead.bIsDamageVolumeSensitive
- uc:SBase.SPlayerController:GameEnded.bIsDamageVolumeSensitive
- uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.bIsDamageVolumeSensitive
- uc:SBase.SPlayerController:s_FallAfterGrabbedFromLedge.bIsDamageVolumeSensitive
- uc:SBase.SBasicGamePlayObject:TakeDamage
- uc:SBase.SGamePlayObject:TakeDamage
- uc:SBase.SGamePlayObject:Trigger
- uc:SBase.SGamePlayObject:s_Flying.Bump
- uc:SBase.SGamePlayObject:s_PickedUp.TakeDamage
- uc:SBase.SGamePlayObject:s_WaitForDamageReset.BeginState
- uc:SBase.SGamePlayObject:s_WaitForDamageReset.Timer
- nat:SBase.SGamePlayObject.ProcessDamage
- def:SBase.SGamePlayObject
- uc:SBase.SAnimatedMesh:s_Wait.Trigger
- uc:Engine.AnimNotify_Trigger:Notify
- nat:SBase.SMagicEvent.NativeTrigger
- def:SBase.SProjFrag
- def:SBase.SMineProx
- def:SBase.SMineLaser
- def:SBase.SBombTargetObjectiveTrigger

### spawn-protection-and-scoring.md
- uc:SBase.SPlayerController:EnterStartState
- uc:Engine.PlayerController:Restart
- uc:SBase.SPlayerController:ClientRestart
- uc:SBase.SPlayerController:StandardReactionsToDamage
- uc:SBase.SGameInfo:RestartPlayer
- uc:SBase.SGameInfo:AddPoint
- uc:Engine.LevelInfo:InitArrayNbPoints
- rep:Engine.LevelInfo.NbPointsAttack
- rep:Engine.LevelInfo.NbPointsDefense
- rep:Engine.PlayerReplicationInfo.Score
- def:SBase.SPlayerController
- uc:SBase.SGameInfo:InitGame

### health.md
- def:Engine.Pawn
- rep:Engine.Pawn.Health
- nat:SBase.SPawnAttaque.TickNative
- uc:SBase.SPawnAttaque:Tick
- def:SBase.SPawnAttaque
- rep:SBase.SPawnAttaque.NbPointsRefillable
- rep:SBase.SPawnAttaque.BloodDropStartTime
- uc:SBase.SBasePawn:PostBeginPlay
- nat:SBase.SBasePawn.NativeTick
- uc:SBase.SBasePawn:Tick
- rep:SBase.SBasePawn.bPoisonned
- rep:SBase.SBasePawn.PoisonnedBy
- def:SBase.SBasePawn
- uc:SBase.SPlayerController:CountPoisonDrain
- uc:SBase.SInfirmary:Use
- uc:SBase.SHealMateInteraction:InitInteract
- uc:SBase.SHud:PostRender
### rumble.md
- uc:SBase.SPlayerController:Vibration_CheckSiPossible
- uc:SBase.SPlayerController:Vibration_StopTout
- uc:SBase.SPlayerController:Vibration_CoupParDefaut
- uc:SBase.SPlayerController:Vibration_SautMoyen
- uc:SBase.SPlayerController:Vibration_SautHaut
- uc:SBase.SPlayerController:Vibration_Bousculade
- uc:SBase.SPlayerController:Vibration_Blast
- uc:SBase.SPlayerController:Vibration_Grabbed
- uc:SBase.SPlayerController:Vibration_Grab_Kill
- uc:SBase.SPlayerController:Vibration_Charge_Course
- uc:SBase.SPlayerController:Vibration_Charge_Obstacle

## Relevant units not specced here
These units touch combat but belong to another area or were only read for context. Each needs an owner spec (or an
n-a reason) before coverage can mark it.

State replication and lifecycle (netcode / controller core):
- uc:SBase.SPlayerController:MirrorState, MirrorPredictedState, ChangeState, PCChangeServerState (only the gas KO
  credit part is specced), PCChangeClientState, MirrorStateForRepos, PCChangeClientStateForRepos, IAmOnClient,
  TopSynchro, SendAnEventToServer, ServerSendAnEvent, ReceiveAnEvent (global versions)
- uc:SBase.SPlayerController:ReinitPawnBecauseOfDivergences (alive branches), ClientReinitPawnBecauseOfDivergences,
  TestReinitPawnBecauseOfDivergences, TellServerIAMReadyToReinit, s_ClientIsReinit, s_WaitForClientToReinit
- uc:SBase.SPlayerController:Possess, UnPossess (only corpse removal used), GivePawn, ClientRestart, EnterStartState
  (only spawn protection used)
- uc:SBase.SBasePawn:ChangeEnumAction, SendActions, GetNewActions, SetInfoForVandamme, DistantClientReinit
- nat:SBase.SBasePawn.ExecuteAction (native action handler; only contracts used: downed flags, paired grab clips,
  death clips) and its live hook Reloaded.Core.dll+0x23740
- Engine.Controller.GotoState hook (Reloaded.Core.dll+0x24080) and UE2 GotoState pre-emption semantics (Core spec)

Native physics and events used as triggers (eng units, physics spec):
- eng:SBase.SBasePawn.DropAttackTrigger (native raising DropAttack, DropAttack_PawnFound, DropAttackedDontMove)
- eng:SBase.SBasePawn.StickyBlastTrigger (native raising StickyBlast)
- eng:SBase.SPlayerController.FallTracking (writes fZMaxFalling; raises MyFalling, MyNotifyLanded, FellOutOfWorld)
- eng:SBase.SBasePawn.Repositionnal (PHYS_Repositionnal motion and the TopSynchro event)
- eng:Engine.Actor.KilledByTrigger (native callers of KilledBy)
- nat:SBase.SPlayerController.NativePlayerInput (consumes iDontUpdatePadInput)
- nat:SBase.SPlayerController.bTestGrabLedgeGeo
- nat:SBase.SPlayerController.NativeFindTargetToSnap, uc:SBase.SPlayerController:ComputeTargetDistance (global)

Weapons and gadgets (weapons and gadgets specs):
- nat:SBase.SWeaponDefense.GetAccuracy, nat:SBase.SWeaponDefense.NativeTickSWeaponDefense,
  uc:SBase.SWeaponDefense:Fire, ApplyWeaponJump, Reload, FullReload, SwitchROF, ServerSetROF, GetNbBulletsFromROF,
  AnimatePain, AnimateBlast, AnimateReanimate, AnimateFall, AnimateLand and states s_Firing, s_Reloading,
  s_FullReloading, s_Dmg_Electric, s_Dmg_Electric_End, s_ElbowShocking, s_Charging, s_Charging_End, s_Charging_Bump,
  s_Death, s_Reanimation, s_Sniping, s_IdleGrabbed
- uc:SBase.SWeaponDefense_Shotgun:SpawnImpacts, FireWeapon, s_Reloading, s_FullReloading
- uc:SBase.SWeaponAttaque:Tick, s_Selected.Tick, ServerSwitchLaser, nat:SBase.SWeaponAttaque.TickSWeaponAttaque
- uc:SBase.SPawnAttaque:AuthorizeEnergy, nat:SBase.SPawnAttaque.ComputeEnergyLevel
- uc:SBase.SPlayerController:Fire, AltFire, CheckIfCanFire, SelectGadget, EteintLaser, RallumeLaserEteint
- uc:SBase.SGadTazerLauncher:Use (the damage event is specced)
- nat:Engine.Projectile.Explode, nat:SBase.SMineProx.Explosion, nat:SBase.SMineLaser.Explosion,
  uc:SBase.SMinePoison:TakeDamage, SBase.SProjGrenade, SBase.SProjFrag, SBase.SProjFlashBang (explosion damage
  producers; their pawn-side effect is specced)
- uc:SBase.SStickyCamInteraction (sticky cam damage), uc:SBase.SPresDetector:TakeDamage,
  uc:SBase.SSprinkler:TakeDamage, uc:SBase.SBombTargetObjectiveTrigger:TakeDamage
- uc:SBase.SBasePawn:StickyShockSfx, evStickyShockSfx, BeginStickySkin, BeginPhosphoSkin, BeginWetSkin, RestoreSkins,
  KillSkins, HitBySpyBullet, ServerHitBySpyBullet, DetectedByMarqueurSpy (writers of bStickySkinned, bWet, marks)

Objects and level scripting (objects / objectives specs):
- uc:SBase.SGamePlayObject:Touch, NotifyPickup, Throw, StoppedMoving, WaitReset, SpawnDamageEmitter,
  KillDamageEmitter, UnTrigger, SetLockedByChaff, s_LockedByChaff, s_Flying (except Bump),
  nat:SBase.SGamePlayObject.NativeHitWall
- Engine.Mover (states other than TriggerOpenOnceOnly.SubTrigger), SBase.SLift (except EncroachingOn)
- uc:SBase.SMagicEvent:Trigger, UnTrigger, BeginPlay (the relay; only its effect on damage volumes is specced)
- SBase.SInterrupteur (crane control instigator chain), SBase.SAnimatedMesh (states other than s_Wait.Trigger)
- SBase.SFlag (CarrierKiller use)
- uc:SBase.SInfirmary:Use and uc:SBase.SHealMateInteraction:InitInteract are summarised in health.md; their
  interaction flow belongs to the objects spec

Interaction, traversal and camera states (only their PawnTakeDamage/ClientDamage overrides are specced):
- uc:SBase.SPlayerController:s_Hack_*, s_Computer_*, s_Maintain_*, s_PlaceBomb, s_PlaceBomb_End, s_PutFlagInDropZone,
  s_InteractWithObject family (all non-damage functions)
- uc:SBase.SPlayerController:s_BTW_*, s_Coop_*, s_ShortLadder_*, s_Hoist_*, OnLedge (non-grab functions), GrabFence,
  OnFence (non-blast functions)
- uc:SBase.SPlayerController:s_Binoculars_Def, s_Binoculars_Att (non-damage functions)
- uc:SBase.SPlayerController:s_UseObject_StickyCam, s_UseObject_StickyCam_SmoothToPosition, s_UseObject_CamNetwork,
  s_UseObject_Mirador (non-damage functions)
- uc:SBase.SPlayerController:ClientDamage overrides in s_BasePutMineOnWall, s_ShortLadder_P_Base,
  s_ShortLadder_P_WaitForShortLadder, s_Hoist_P_Waiting, s_Maintain_Enter, s_Maintain_Use, s_Maintain_Leave,
  s_Hack_Enter, s_Hack_Use, s_Hack_Leave, s_StickyCam_MenuInteraction
- uc:SBase.SPlayerController:PlayAnimDmg overrides in PlayerLanding, s_BTW_Base, s_BTW_StandLeft, s_BTW_StandRight,
  s_BTW_CrouchLeft, s_BTW_CrouchRight
- uc:SBase.SPlayerController:s_UseObject_Mirador.GoInSmoke
- uc:SBase.SPlayerController:ResetMercCam and the native consumer of ForceRotateTo (camera spec)

Spectating, respawn weapon, end of match (game mode spec):
- uc:SBase.SPlayerController:s_MultiSpectating, s_BaseSpectating, s_Spectating_Visitor, Server_FindNextFriend,
  Server_FindNextVisitor
- uc:SBase.SPlayerController:BeginRespawnWeaponPick, CheckRespawnWeaponKey, ServerCycleRespawnWeapon, SpawnWeaponFor,
  MercWeaponIndex, ClientSetRespawnWeapon, CanPickRespawnWeapon
- uc:SBase.SGameInfo:FindPlayerStart, RatePlayerStart, RateStartByDistance, CountMercsStillIn, GetTopKills,
  GetNbSentries, sEndGame, ReplicateWinningReason, EndGame, s_MatchOver.ScoreKill, s_MatchOver.RestartPlayer,
  InitGame (scoring mode)

Voice, HUD and feedback (voice, HUD and input specs):
- uc:SBase.SPlayerController:TalkToMate, OtherTeamCantHearMe, ServerHearSpybullets, HearPC, CanHackEnemyComs,
  CanAlwaysTalkToMates, ServerSetVoiceChatTarget, ServerClearVoiceChatTarget, StopHackComm
- uc:SBase.SPlayerController:ClientShake, SetHudPerturbation (rumble: rumble.md)
- uc:SBase.SHud:DrawBloodSplash, nat:SBase.SHud.ProcessSFX (hooked by Reloaded.Core), uc:SBase.SGameMessages:GetString
  (switches 16, 17, 20, 23, 56, 57, 58 used here); nat:SBase.SPlayerController.ClientDamageHudEffect is listed in
  damage-pipeline.md as a contract only

Unreachable or unused for SBasePawn (candidates for n-a):
- uc:Engine.Pawn:Died, TakeDamage, gibbedBy, EncroachedBy, PlayDying, PlayHit, KilledBy, Dying state;
  uc:Engine.Controller:PawnDied, WasKilledBy, NotifyKilled; uc:Engine.PlayerController:PawnDied;
  uc:Engine.GameInfo:Killed, ScoreKill, NotifyKilled (SGameInfo.Killed replaces them; SBasePawn overrides TakeDamage,
  KilledBy and EncroachedBy)
- uc:Engine.Decoration:TakeDamage, uc:Engine.Fragment:TakeDamage, uc:Engine.Trigger:TakeDamage (no pawn effect)
- uc:SBase.SPlayerController:s_PlayerHit (never entered), uc:SBase.SPlayerController:Dmg (debug, never called)
- SPlayerController.iNbDropAttackBeforePawnDies and fNbSecondsbetweenDropAttackBeforePawnDies (never read by script)
- SPlayerStats fields never written by script (see spawn-protection-and-scoring.md rule 18)
