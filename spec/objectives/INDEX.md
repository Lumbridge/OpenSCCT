# Objectives and interactions: spec index

Area: SteeE Story objectives and everything a player uses to complete them. The exact unit IDs of each file are in that file's `Units:` header (480 distinct IDs in total); the table gives the groups.

| File | Covers | Unit groups | IDs |
|---|---|---|---|
| [missions.md](missions.md) | SMission and SObjective: GameMode filter at match start, reset (chained, random, minimum, delayed), counters, completion chain, Story end check, mission texts, AtResetEvent | def/rep/uc/nat SObjective, SMission (all functions); SGameInfo StartMatch, NotifyObjectiveTaken, sCheckEndGame (objective parts); SPlayerController GetObjectivesLeft, PlayerTick reminder; Actor.AllObjectives | 38 |
| [objective-trigger.md](objective-trigger.md) | SObjectiveTrigger base: reset, prepare, Triggering, merc radar multicast, TakeObjective, lock/unlock states | def/rep/uc/nat SObjectiveTrigger (all functions and states) | 32 |
| [terminal-hack.md](terminal-hack.md) | SComputerObjectiveTrigger and SComputerInteraction; the spy's s_Computer_* states; use spot, cancel rules, kept progress, DominationCaptureTime, skins, messages | def/rep/uc/nat SComputerObjectiveTrigger, SComputerInteraction (all); SPlayerController UseComputer and every s_Computer_* state function | 52 |
| [disk-flag.md](disk-flag.md) | SFlag, SFlagDropZone and their interactions; pickup at home, carry, drop on death, touch pickup and merc return, reset timer, delivery | def/rep/uc/nat SFlag, SFlagDropZone, SFlagDropZoneInteraction, SPickupInteraction (all); SGamePlayObject pickup/throw subset; SPlayerController DiscardPickup, DiscardFlag, SetHandItemFromInteraction, PickupFlag, s_Pickup_GoTo, s_Pickup, s_FlagAndDropZone_GoTo, s_PutFlagInDropZone; SBasePawn.AnimNotifyPickup | 91 |
| [interaction-system.md](interaction-system.md) | The Use menu: interaction cylinders, UsableBy, iPriority, in-front flags, reuse, fixed mate entries, menu states, RPC flow, generic positioning | def/rep/uc/nat SInteractObject, SInteractionManager, SLeaveInteraction, SShareObjectInteraction, SMateInteraction, SHealMateInteraction, SRemoveSpyBulletInteraction, SBackPackInteraction, SInfirmaryInteraction, SAmmoStockInteraction; SBasicGamePlayObject interaction functions; SPlayerController DoInteraction, CanInteract, InteractionInit/Interaction/InteractionPost, menu and wait states | 123 |
| [team-interactions.md](team-interactions.md) | What a player can do for a team mate: the decoded mate search of the Heal Mate, Unspy Mate and Refill entries, DetectedByMarqueurSpy, the refill; revive and co-op moves in short (their specs hold them); the spy HUD's coop icon driver | nat SMateInteraction.CanUse (per-class virtuals), SBasePawn.DetectedByMarqueurSpy; uc SHealMateInteraction, SRemoveSpyBulletInteraction, SBackPackInteraction InitInteract; SPlayerController UpdateMyInventoryChargeBaseOnMateBackpack | 6 |
| [switch-hack.md](switch-hack.md) | SInterrupteur, STriggerInteraction, SInterrupteurManager; plain, hackable, hold and delayed switches; the crane and shutter panels | def/rep/uc/nat SInterrupteur, STriggerInteraction, SInterrupteurManager (all); SPlayerController s_Hack_* and s_Maintain_* states including the two label blocks | 93 |
| [bomb-target.md](bomb-target.md) | SBombTargetObjectiveTrigger, SBombTargetInteraction, SGadBomb, s_PlaceBomb; the decoded Desactivate, Tick skin swap and CanUse (not in SteeE) | def/rep/uc/nat of those classes; SPlayerController ServerPlaceBomb, s_PlaceBomb, s_PlaceBomb_End; eng:ASBombTargetObjectiveTrigger::Tick | 36 |
| [disk-hunt.md](disk-hunt.md) | SDisk and the GM_DiskHunt mission: ActivateDisks (decoded), pickup and carry, DropDisks, merc return, reset timer, client visibility, the menu's disk counts | def/uc/nat SDisk; nat SMission.ActivateDisks; SPlayerController DropDisks and the disk branches of SetHandItemFromInteraction, s_Pickup.EndState, s_Dead; SBasePawn.AnimNotifyPickup; eng:ASDisk::Tick | 14 |
| [messages.md](messages.md) | Message switches, broadcast and team delivery, HUD routing and stacks, HUD lists and radar markers fed by objectives, multicast RPC flag | SGameMessages.GetString (area switches), broadcast chain, SHud routing helpers, SHud.AddObjective/RemoveObjective | 20 |

## Relevant units not specced here

Out of the slice or owned by another area; listed so coverage can track them.

Alarms and messages not present in SteeE:
- uc:SBase.SAlarm (all; it is the only caller of trigger Lock/UnLock), uc:SBase.SMessageTrigger (all)

HUD and scoreboard drawing (HUD area):
- uc:SBase.SHud:LocalizedMessage (switches outside this area), uc:SBase.SHud:AddInfoMessageToStack, uc:SBase.SHud:AddMessageToAuxiliaryStack, uc:SBase.SHud:AddReticuleMessage, uc:SBase.SHud:PostRender
- nat:SBase.SHud.BuildObjectivesAndTriggersList, nat:SBase.SHud.DrawObjectivesBinocularsAtt, nat:SBase.SHud.LockObjectiveBinocularsAtt, nat:SBase.SHud.HudAtt, nat:SBase.SHud.HudDef, nat:SBase.SHud.AddMalfunctionGPO, nat:SBase.SHud.RemoveMalfunctionGPO
- uc:SBase.SGameMessages:GetString (alarm, death, grab and score switches), def:SBase.SScoreBoardStory, uc:SBase.SScoreBoard (objective columns)

Gadget interactions (gadget area):
- def/uc:SBase.SStickyCamInteraction, def/uc:SBase.SMineProxInteraction, def/uc:SBase.SMineTrapInteraction, def/uc:SBase.SPresDetectorInteraction, def/uc:SBase.SProjoInteraction, def/uc:SBase.SBombInteraction
- uc:SBase.SPlayerController:s_StickyCam_MenuInteraction (all), uc:SBase.SPlayerController:s_PutMineOnWall_InteractionMenu (all)

Team-mate and cooperative actions (controller/coop area):
- nat:SBase.SPlayerController.CheckIfCanInteractWithMate, nat:SBase.SPlayerController.AreaIsSafeToInteract, uc:SBase.SPlayerController:DoCoopInteraction, uc:SBase.SPlayerController:AskServerToStartCoopInteraction, uc:SBase.SPlayerController:UpdateMyInventoryChargeBaseOnMateBackpack, uc:SBase.SPlayerController:CountPoisonDrain, uc:SBase.SBasePawn:DetectedByMarqueurSpy

Map systems that own the effect of a use (map systems area):
- uc:SBase.SInfirmary:Use, uc:SBase.SAmmoStock:Use
- SGamePlayObject outside the pickup/throw subset: uc:SBase.SGamePlayObject:BeginPlay, PostBeginPlay (non-pickup parts), Trigger, UnTrigger, SetLockedByChaff, TakeDamage, WaitReset, Touch, SpawnDamageEmitter, KillDamageEmitter, AcceptStickableGPO, s_LockedByChaff.*, s_WaitForDamageReset.*, nat:SBase.SGamePlayObject.ProcessDamage
- SBasicGamePlayObject outside interaction setup: uc:SBase.SBasicGamePlayObject:AcceptStickableGPO, AddOnHud, RemoveFromHud, SetLockedByChaff, Malfunction, TakeDamage, s_LockedByChaff.*
- The event receivers of `blast`, `windows`, `close_windows`, `fermeture_fenetre` (crane chain and shutter movers)

Hand-item throwing (no pickable object in SteeE besides the disk):
- uc:SBase.SPlayerController:ThrowObject, uc:SBase.SPlayerController:s_ThrowGoTo (all), uc:SBase.SBasePawn:AnimNotifyThrow

Controller infrastructure used everywhere here (controller/netcode area):
- uc:SBase.SPlayerController:MirrorState, MirrorPredictedState, PCChangeClientState, ChangeState, PCChangeServerState, GotoWaitingState, IAmOnClient, TopSynchro (global); uc:Engine.Actor:ChangeState, ChangeServerState, MirrorState, ChangeClientState; nat:Core.Object.GotoState (PreviousState bookkeeping)
- The Use-press chains inside uc:SBase.SPlayerController:s_PlayerWalkingMario.PlayerMove and uc:SBase.SPlayerController:s_PlayerWalkingWeapon.PlayerMove (only the DoInteraction call is described)
- Repositioning motion (`SA_REPOS`, `vVandammePos`, `SetInfoForVandamme`, PHYS_Repositionnal) and the animation actions named in these specs (animation area)
- uc:SBase.SPlayerController:s_Dead.BeginState, uc:SBase.SPlayerController:s_PlayerFalling_OutOfWorld.BeginState, uc:SBase.SPlayerController:Destroyed (only their flag-discard lines are specced)

Game rules (game-rules area):
- uc:SBase.SGameInfo:sCheckEndGame (time and team-death clauses), uc:SBase.SGameInfo:StartMatch (non-mission parts), uc:SBase.SGameInfo:Killed (only the CarrierKiller line is specced), uc:SBase.SGameInfo:AddPoint, uc:SBase.SGameInfo:sEndGame, uc:SBase.SGameInfo:ReplicateWinningReason, uc:Engine.LevelInfo:InitArrayNbPoints
- rep:Engine.GameReplicationInfo.NumObjectives, rep:Engine.GameReplicationInfo.LastTimeObjectif; STeamInfo.iNbObjectivesTaken and PRI.iNbObjectivesTaken, SPlayerStats objective fields (Spy_ObjectivesTaken, Spy_ObjectivesTakenTime, Spy_ObjectiveTimeCaptured, Merc_SecureDisks, Merc_SecuredBombs)

Native C++ with no script unit (proposed `eng:` IDs; behaviour described as contracts in the specs):
- eng:ASComputerObjectiveTrigger::Tick, eng:ASFlag::Tick, eng:ASInteractionManager::Tick (ASDisk::Tick and ASBombTargetObjectiveTrigger::Tick are decoded in disk-hunt.md and bomb-target.md), eng:ASInteractObject::UpdateCollisionRadius
- eng:GetOptimizedRepList for ASObjective, ASMission, ASObjectiveTrigger, ASComputerObjectiveTrigger, ASComputerInteraction, ASFlag, ASFlagDropZone, ASFlagDropZoneInteraction, ASPickupInteraction, ASTriggerInteraction, ASInterrupteur, ASInteractObject, ASInteractionManager, ASMateInteraction, ASBasicGamePlayObject, ASGamePlayObject, ASBombTargetObjectiveTrigger, ASBombTargetInteraction
- eng:IsNetRelevantFor for ASFlag, ASBasicGamePlayObject, ASGamePlayObject, ASLeaveInteraction, ASShareObjectInteraction
- eng: remote-function dispatch for the 0x01000000 multicast flag
