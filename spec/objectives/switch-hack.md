# Switches and hackable panels (SInterrupteur)

Units:
- def:SBase.SInterrupteur, def:SBase.STriggerInteraction, def:SBase.SInterrupteurManager
- rep:SBase.SInterrupteur.bMaintainsSwitch, rep:SBase.SInterrupteur.iHackingTime, rep:SBase.SInterrupteur.UseRelativeLocation, rep:SBase.SInterrupteur.UseRelativeRotation, rep:SBase.SInterrupteur.iReusableTime
- rep:SBase.STriggerInteraction.MinTriggerTime, rep:SBase.STriggerInteraction.OwnerInterrupteur, rep:SBase.STriggerInteraction.bMaintain, rep:SBase.STriggerInteraction.bHackable, rep:SBase.STriggerInteraction.AddRemoveActorOnClientHud
- uc:SBase.SInterrupteur:ForceTriggerableEvenIfMoving, uc:SBase.SInterrupteur:AcceptStickableGPO, uc:SBase.SInterrupteur:PreBeginPlay, uc:SBase.SInterrupteur:PostBeginPlay, uc:SBase.SInterrupteur:PostNetBeginPlay, uc:SBase.SInterrupteur:Trigger, uc:SBase.SInterrupteur:UnTrigger
- uc:SBase.STriggerInteraction:PostBeginPlay, uc:SBase.STriggerInteraction:AddRemoveActorOnClientHud, uc:SBase.STriggerInteraction:InitInteract, uc:SBase.STriggerInteraction:Interact
- uc:SBase.STriggerInteraction:s_Maintain_Use.BeginState, uc:SBase.STriggerInteraction:s_Maintain_Use.EndState, uc:SBase.STriggerInteraction:s_Maintain_Use.InitInteract, uc:SBase.STriggerInteraction:s_Maintain_Use.PostInteract
- uc:SBase.STriggerInteraction:s_Hacking.EndState, uc:SBase.STriggerInteraction:s_Hacking.PostInteract
- uc:SBase.STriggerInteraction:WaitingBeforeActivation.BeginState, uc:SBase.STriggerInteraction:WaitingBeforeActivation.Timer, uc:SBase.STriggerInteraction:WaitingBeforeActivation.InitInteract
- uc:SBase.STriggerInteraction:WaitingBeforeDesactivation.BeginState, uc:SBase.STriggerInteraction:WaitingBeforeDesactivation.Timer, uc:SBase.STriggerInteraction:WaitingBeforeDesactivation.InitInteract
- nat:SBase.STriggerInteraction.HandleEventInfo, nat:SBase.STriggerInteraction.CanUse
- uc:SBase.SInterrupteurManager:Trigger, uc:SBase.SInterrupteurManager:UnTrigger
- uc:SBase.SPlayerController:s_Hack_GoTo.CanActivateLaser, uc:SBase.SPlayerController:s_Hack_GoTo.NotifyBump, uc:SBase.SPlayerController:s_Hack_GoTo.BeginState, uc:SBase.SPlayerController:s_Hack_GoTo.TopSynchro, uc:SBase.SPlayerController:s_Hack_GoTo.PlayerMove
- uc:SBase.SPlayerController:s_Hack_Enter.BeginState, uc:SBase.SPlayerController:s_Hack_Enter.PawnTakeDamage, uc:SBase.SPlayerController:s_Hack_Enter.ClientDamage, uc:SBase.SPlayerController:s_Hack_Enter.AnimEnd, uc:SBase.SPlayerController:s_Hack_Enter.PlayerMove
- uc:SBase.SPlayerController:s_Hack_FastLeave.BeginState, uc:SBase.SPlayerController:s_Hack_FastLeave.EndState
- uc:SBase.SPlayerController:s_Hack_Leave.BeginState, uc:SBase.SPlayerController:s_Hack_Leave.EndState, uc:SBase.SPlayerController:s_Hack_Leave.PawnTakeDamage, uc:SBase.SPlayerController:s_Hack_Leave.ClientDamage, uc:SBase.SPlayerController:s_Hack_Leave.AnimEnd, uc:SBase.SPlayerController:s_Hack_Leave.PlayerMove, uc:SBase.SPlayerController:s_Hack_Leave@code
- uc:SBase.SPlayerController:s_Hack_Use.ServerWillStartGrabOnMe, uc:SBase.SPlayerController:s_Hack_Use.CanBeGrabbed, uc:SBase.SPlayerController:s_Hack_Use.CanBeDropAttacked, uc:SBase.SPlayerController:s_Hack_Use.GadgetQuickChoice, uc:SBase.SPlayerController:s_Hack_Use.BeginState, uc:SBase.SPlayerController:s_Hack_Use.EndState, uc:SBase.SPlayerController:s_Hack_Use.Timer, uc:SBase.SPlayerController:s_Hack_Use.PlayerMove, uc:SBase.SPlayerController:s_Hack_Use.PawnTakeDamage, uc:SBase.SPlayerController:s_Hack_Use.ClientDamage
- uc:SBase.SPlayerController:s_Maintain_GoTo.CanActivateLaser, uc:SBase.SPlayerController:s_Maintain_GoTo.NotifyBump, uc:SBase.SPlayerController:s_Maintain_GoTo.BeginState, uc:SBase.SPlayerController:s_Maintain_GoTo.TopSynchro, uc:SBase.SPlayerController:s_Maintain_GoTo.PlayerMove
- uc:SBase.SPlayerController:s_Maintain_Enter.BeginState, uc:SBase.SPlayerController:s_Maintain_Enter.PawnTakeDamage, uc:SBase.SPlayerController:s_Maintain_Enter.ClientDamage, uc:SBase.SPlayerController:s_Maintain_Enter.AnimEnd, uc:SBase.SPlayerController:s_Maintain_Enter.PlayerMove
- uc:SBase.SPlayerController:s_Maintain_Leave.BeginState, uc:SBase.SPlayerController:s_Maintain_Leave.EndState, uc:SBase.SPlayerController:s_Maintain_Leave.PawnTakeDamage, uc:SBase.SPlayerController:s_Maintain_Leave.ClientDamage, uc:SBase.SPlayerController:s_Maintain_Leave.AnimEnd, uc:SBase.SPlayerController:s_Maintain_Leave.PlayerMove, uc:SBase.SPlayerController:s_Maintain_Leave@code
- uc:SBase.SPlayerController:s_Maintain_Use.BeginState, uc:SBase.SPlayerController:s_Maintain_Use.CanBeDropAttacked, uc:SBase.SPlayerController:s_Maintain_Use.GadgetQuickChoice, uc:SBase.SPlayerController:s_Maintain_Use.EndState, uc:SBase.SPlayerController:s_Maintain_Use.PlayerMove, uc:SBase.SPlayerController:s_Maintain_Use.PawnTakeDamage, uc:SBase.SPlayerController:s_Maintain_Use.ClientDamage

## Purpose

An `SInterrupteur` is a wall switch or panel that fires its `Event` when used. Three kinds exist, chosen by its settings: a plain switch (press a button, fire at once), a hackable panel (`iHackingTime` seconds of hacking at a use spot; moving, drawing a weapon or damage aborts), and a hold switch (`bMaintainsSwitch`: fires when the hold starts and again when it ends). It can be one-shot (`bTriggerOnceOnly`), have a reuse delay, delays before and after activation, and be restricted to one team. Other events can toggle whether it is usable. In SteeE Story the "Crane Control" panel (5 s, spies, once) starts the crane chain and three shutter panels (4 s, spies) close or open shutters.

## Data

`SInterrupteur` (plus the `SBasicGamePlayObject` interaction fields, `interaction-system.md`):

| Field | Type | Default | Notes |
|---|---|---|---|
| iHackingTime | int | 0 | Seconds of hacking; non-zero makes the panel hackable. Replicated (`bNetInitial`). |
| bMaintainsSwitch | bool | false | Hold switch. Replicated (`bNetInitial`). `ForceTriggerableEvenIfMoving()` returns it. |
| iReusableTime | int | 0 | Copied into the interaction on the server. Replicated. |
| bInitialyActive | bool | false | Copied into `STriggerInteraction.bIsActive` (native meaning, see Open questions). |
| sWarnHackingText | localized string | "" | If set, mercs get it as message 19 when a hack starts. |
| InteractionSound | Sound | `Sound_Persos.divers.RA_interrupteur` | Played on use. |
| UseRelativeLocation | Vector | (55, 0, 0) | Use spot in the switch's axes (X and Y used). Replicated (`bNetInitial`). |
| UseRelativeRotation | Rotator | (0, 32768, 0) | Yaw added to the switch's yaw for the spy's facing. Replicated (`bNetInitial`). |
| bInitialSecondaryObjective | bool | false | Remembers the authored `bSecondaryObjective`. |
| InteractionClass | | STriggerInteraction | |
| Inherited: iPriority 0, bTriggerOnceOnly false, TriggerMethode TriggerToggle (0), iAttenteAvantActivation 0, iAttenteAvantDesactivation 0, bInitialyUsable true, UsableBy 0, fInteractionRadius/Height -1, sDescriptionText "", mesh `SGameplayObjects.SGPO_SM.trigger_standard`, SoundRadius 60, SoundVolume 200, bNoDelete | | | `TriggerMethode`: 0 TriggerToggle (Trigger every use), 1 TriggerControl (alternate Trigger and Untrigger), 2 UnTriggerOnly. |

`STriggerInteraction`: iPriority 20, Description "Switch", Alpha 1, plus `bHackable` (iHackingTime != 0), `bMaintain` (bMaintainsSwitch), `bIsActive` (editable), `OwnerInterrupteur`, `MinTriggerTime` (replicated: time the running hack ends, for the HUD), `InstigatorTempo` (last user), `RemainingTime`, `LocalRemainingTimeSeconds`.

`SInterrupteurManager`: `SInterrupteurList` array of { `Interrupteur`, `TriggeredUsability`, `UntriggeredUsability` }; RemoteRole None, hidden.

SteeE instances (all: `UsableBy = 3` spies, `UseRelativeLocation = (40, 0, 0)`, `fInteractionRadius 60`, `fInteractionHeight 120`, `bInteractInFront`, mesh import -209 (a switch mesh), blocking, `iReusableTime 0`, `iPriority` default 0):

| Actor | sWarnHackingText (merc message) | sDescriptionText (menu) | iHackingTime | Once | Tag | Event | Use spot (X, Y) | Face yaw |
|---|---|---|---|---|---|---|---|---|
| SInterrupteur28808 | Crane Control | Activate | 5 | yes | SInterrupteur | blast | (-264.2, 1038.4) | 16384 |
| SInterrupteur28810 | Hacking into Store and Restaurant shutters | Shutters | 4 | no | hackvolet01 | windows | (-3826.3, 1269.0) | 49152 |
| SInterrupteur28809 | Hacking into Restaurant shutters | Shutters | 4 | no | hackvolet01 | close_windows | (-1939.9, -1389.8) | 16384 |
| SInterrupteur28807 | Hacking into Store shutters | Shutters | 4 | no | hackvolet01 | fermeture_fenetre | (-2100.3, 2062.0) | 49152 |

Use spots are `Location + 40 X` from the switch yaw (float approximation). No two hackable switches are within 200 uu.

## Rules

Setup:

1. `PreBeginPlay` (simulated): parent (menu label timer, `interaction-system.md` rule 5); `bInitialSecondaryObjective = bSecondaryObjective`; `bSecondaryObjective = false`.
2. `PostBeginPlay` (server): parent spawns the `STriggerInteraction` (`interaction-system.md` rule 1; priority becomes the switch's 0 unless authored; `bInteractInFront` copied). During its own `PostBeginPlay` the interaction sets `bHackable = (iHackingTime != 0)` and `bMaintain = bMaintainsSwitch`, then sizes itself (60 x 120 in SteeE) and copies `UsableBy` (3 in SteeE). Then `bIsActive = bInitialyActive`.
3. `PostNetBeginPlay`: if `bInitialSecondaryObjective`, `bSecondaryObjective = InteractionObj.bUsable`; `OwnerInterrupteur = self`; on the server `InteractionObj.iReusableTime = iReusableTime`.
4. `AcceptStickableGPO` returns false.

Enabling and disabling by events (server):

5. `Trigger`: `InteractionObj.bUsable = !bUsable` (toggle); if `bInitialSecondaryObjective`, mirror it into `bSecondaryObjective`. `UnTrigger`: `bUsable = false` (and `bSecondaryObjective = false` when mirrored). In SteeE the shutter panels share Tag `hackvolet01`, which only the DiskHunt and DeathMatch missions fire at reset (making them unusable in those modes); in Story nothing fires it, so they stay usable.
6. `SInterrupteurManager.Trigger`: for every entry, `Interrupteur.InteractionObj.UsableBy = TriggeredUsability`; `UnTrigger`: `= UntriggeredUsability`. SteeE has none.

Use (server `STriggerInteraction.InitInteract`, after `InteractionInit`):

7. If the owner is an `SBasicGamePlayObject` (always for a switch): `InstigatorTempo = Instigator`; then the first that applies:
   - `iAttenteAvantActivation != 0`: `MirrorState('WaitingBeforeActivation')` on the interaction (rule 19), then continue to the sound step;
   - `iHackingTime != 0` and the instigator is alive: `Instigator.MirrorState('s_Hack_GoTo')` and return (no sound now);
   - `bMaintainsSwitch` and alive: `Instigator.MirrorState('s_Maintain_GoTo')` and return;
   - else: `HandleEventInfo()` (native: fire the switch's event, rule 21), then continue.
   If the owner were not an `SBasicGamePlayObject`, `TriggerEvent(Owner.Event, Owner, Instigator.Pawn)`.
8. Sound step: if the owner has an `InteractionSound`, `Owner.PlaySound(sound, SLOT_Misc, SoundVolume / 255, false, SoundRadius, SoundPitch / 64)`.
   A plain switch reaches this function from the press-button state (`interaction-system.md` rule 19), so it fires when the press animation ends on the interact channel; the controller returns to walking itself. A hackable or hold switch is entered straight from the menu release.

Hack (spy controller states):

9. `s_Hack_GoTo` BeginState (input machine): if a weapon is drawn and the pawn is not a merc, holster (`SA_WALK_CR_2Hto0H` or `SA_WALK_ST_2Hto0H`) and camera `ECM_Walking`. `Hack = selected STriggerInteraction.OwnerInterrupteur`; if None: `SA_WALK_ST_0H` and `ChangeState('s_PlayerWalkingSt0H')`. Else target `vVandammePos = Hack.Location + X * UseRelativeLocation.X + Y * UseRelativeLocation.Y` with Z = pawn Z, plus 27.5 if crouched; facing `vector(Hack.Rotation + (0, UseRelativeRotation.Yaw, 0))`; `SA_REPOS`. `NotifyBump(any)`: `SA_EXIT_REPOS`, `GotoWaitingState()`. `CanActivateLaser` false. `PlayerMove`: no jump, `ZoneHorsCrouch()`, camera rotation update for spies, zero-acceleration move. `TopSynchro` (input machine): `ChangeState('s_Hack_Enter')`.
10. `s_Hack_Enter` BeginState (input machine): uncrouch if crouched; spies play `SA_HACK_ENTER`; `UsedInterraction = selected entry`; `Interaction(UsedInterraction, 2)` (reliable client-to-server). `AnimEnd(0)` (both machines, each on its own animation): `GotoState('s_Hack_Use')` (local transition, not mirrored). The server runs the same action (SA_HACK_ENTER) with the same frame advance, so its `AnimEnd(0)` comes after the same clip time counted from the tick the server executed the action (the move's arrival), not from the client's start (execute-action.md Network, anim.md rule 2). Damage: if alive `MirrorState('s_Hack_Leave', 'TriggerHack_Failed')`, then normal damage. `ClientDamage`: `InteractionPost(UsedInterraction, 0)` and clear it.
11. Server `STriggerInteraction.Interact(Instigator, Sub)`: if the owner has a sound and `iHackingTime == 0`, play it. If `iHackingTime != 0` and `Sub == 2`: if `sWarnHackingText` is not empty, `BroadcastTeamLocalizedMessage(1, 19, obj = switch)` (mercs see the warn text); `BroadcastLocalizedMessage(39)` to everyone ("Hacking in progress...", 4 s on the HUD); `AddRemoveActorOnClientHud(Instigator, true)` (multicast: only the hacker's own HUD adds this interaction to its draw list, rule 23); `MirrorState('s_Hacking')` on the interaction (enters `s_Hacking`: `bInUse = true`). Else if `bMaintainsSwitch` and `Sub == 2`: `MirrorState('s_Maintain_Use')`.
12. `s_Hack_Use` BeginState: non-spies become grabbable (`bCanBeGrabbed = true`). Server: take the first `SInterrupteur` within 200 uu of the pawn with `iHackingTime != 0`; if found, `SetTimer(iHackingTime, true)` (looping) and, if its interaction is in `s_Hacking`, `MinTriggerTime = GRI.PreciseElapsedTime() + iHackingTime`; if none, `SetTimer(3.0, true)`. `CanBeGrabbed` returns true only for mercs; `CanBeDropAttacked` true.
13. `s_Hack_Use.Timer` (server, iHackingTime s after rule 12): (the 200 uu search repeats with no effect) if alive, `MirrorState('s_Hack_Leave', 'TriggerHack_Successfull')`.
14. `s_Hack_Leave` BeginState (input machine): spies `SA_HACK_ENTERBACK`; crouch again if crouched. Label `TriggerHack_Successfull` (input machine): `InteractionPost(UsedInterraction, 1)`, clear it; a merc then `GotoWaitingState()`. Label `TriggerHack_Failed`: `InteractionPost(UsedInterraction, 0)`, clear it; a merc `GotoWaitingState()`. `AnimEnd(0)` on the input machine for spies: `GotoWaitingState()`. Damage: `GotoWaitingState()` then normal damage. `ClientDamage`: `InteractionPost(selected, 0)`. EndState: `bCanCheckUsingButton = true`.
15. Server `s_Hacking.PostInteract(Instigator, Sub)`: if `Sub == 1`, `HandleEventInfo()` (the switch fires now); always `ChangeState('None')` on the interaction. `s_Hacking.EndState`: `MinTriggerTime = 0`; `AddRemoveActorOnClientHud(None, false)` (every machine removes it from its HUD list); then `s_Maintain_Use.EndState`: `bInUse = false`.
16. Cancelling a hack: `s_Hack_Use.PlayerMove` (input machine): if `|aStrafe| > 1000` or `|aForward| > 1000` or `bFireMode == 1`: `ChangeState('s_Hack_FastLeave')`. `s_Hack_FastLeave` BeginState (input machine): crouch again if crouched; `InteractionPost(UsedInterraction, 0)` (the server leaves `s_Hacking` without firing); `GotoWaitingState()`. EndState: `bCanCheckUsingButton = true`. `s_Hack_Use.EndState`: non-spies lose grabbable; on the input machine a living spy plays `SA_HACK_ENTERBACK`.
17. Damage in `s_Hack_Use` (server): same structure as the terminal (`terminal-hack.md` rule 27) except: there is no broadcast; `SDTStickyShocker` sends the spy to `s_PlayerWalkingSt0H` and `GotoWaitingState()`; any other non-lethal type (including `SDTBase_NoAnim`) plays the hit animation and `MirrorState('s_Hack_Leave', 'TriggerHack_Failed')`. `ServerWillStartGrabOnMe` and `ClientDamage`: `InteractionPost(selected, 0)`. Quick gadget: like the terminal, a quick grenade launch plays `SA_HACK_ENTERBACK`, sends `InteractionPost(selected, 0)` and `GotoWaitingState()`; a cloak is used without leaving.

Hold switches (not in SteeE):

18. `s_Maintain_GoTo`: as `s_Hack_GoTo` (holster for anyone with a weapon); `TopSynchro` → `ChangeState('s_Maintain_Enter')`. `s_Maintain_Enter` BeginState (input machine): uncrouch, `SA_SWM_ENTER`; `AnimEnd(0)` on the input machine: `UsedInterraction = selected`, `Interaction(UsedInterraction, 2)`, `GotoState('s_Maintain_Use')`. Damage: `MirrorState('s_Maintain_Leave', 'TriggerHack_Failed')` if alive. `s_Maintain_Use` (controller) BeginState (input machine): `SA_SWM_MAINTAIN`, `InteractionPost(UsedInterraction, 3)`. Leaving: input over 1000 on either axis (no fire-mode test) → `ChangeState('s_Maintain_Leave', 'TriggerHack_Failed')`; damage → `MirrorState` to the same; EndState plays `SA_SWM_BACK`. `s_Maintain_Leave`: `SA_SWM_BACK`, labels as rule 14, `AnimEnd(0)` → `GotoWaitingState()`. `NotifyBump` in `s_Maintain_GoTo` (input machine): `SA_EXIT_REPOS`, `GotoWaitingState()`. `ClientDamage` (owner): `s_Maintain_Enter` and `s_Maintain_Leave` send `InteractionPost(UsedInterraction, 0)` and clear it; `s_Maintain_Use` sends `InteractionPost(selected, 0)`; each then runs the global version. So a hit, lethal or not, lets go of the switch on the server (the event fires again) even when the leave label is never reached. Quick gadget in `s_Maintain_Use` (as rule 17, but the slot is selected with the pawn's `SelectGagdet` and there is no cloak branch): a quick grenade plays `SA_SWM_BACK`, crouches again if crouched, `bCanCheckUsingButton = true`, `InteractionPost(UsedInterraction, 0)`, clears it and `GotoWaitingState()`. `s_Maintain_Use` allows drop attacks. The enter's `AnimEnd` uses `GotoState` (local), so on a server whose player is remote the controller stays in `s_Maintain_Enter` during the hold (same damage handler) until the client's `ChangeState` to `s_Maintain_Leave` or the walking state arrives.
    Interaction side: `s_Maintain_Use.PostInteract(Sub)`: `HandleEventInfo()`; if `Sub != 3`, `ChangeState('None')`. So the event fires once when the hold starts (Sub 3) and once when it ends (Sub 0 or 1). `s_Maintain_Use.InitInteract` and `s_Hacking.InitInteract` call the base `SInteractObject.InitInteract` (a second user is sent back to his previous state).
19. Delayed switches: `WaitingBeforeActivation` BeginState: `SetTimer(iAttenteAvantActivation)`; `Timer`: `HandleEventInfo()`; then `MirrorState('WaitingBeforeDesactivation')` if `iAttenteAvantDesactivation != 0`, else `MirrorState('None')`. `WaitingBeforeDesactivation`: `SetTimer(iAttenteAvantDesactivation)`; `Timer`: `HandleEventInfo()`, `MirrorState('None')`. In both, a new use gets the base refusal.

Native:

20. `CanUse(PC)` (thunk 0x10AB96F0; editor `ASTriggerInteraction::execCanUse` 0x111BD780): expected: `bUsable`, team by `UsableBy`, alive, not `bInUse`, reuse delay (`TimeSeconds - iLastUsedTime >= iReusableTime`, measured), facing when `bInteractInFront`.
    Decoded game virtual `0x10AF6720` also rejects a hold switch (`bMaintain`) for a merc
    (`SPawnType = 1`), even if its team setting allows mercs. OpenSCCT applies this restriction.
    The native also rejects `s_Maintain_Use`, `s_Hacking`, `WaitingBeforeActivation` and
    `WaitingBeforeDesactivation` (FNames `0x3CF` through `0x3D2`, respectively). OpenSCCT applies
    those state gates independently of `bInUse`, keeping delayed switches out of the menu until
    both timer phases finish. They check the interaction's own state, not a linked mover's state.
21. `HandleEventInfo()` (0x10AF6650; editor 0x111BDBE0; decoded, natives2-events.md rule 2): G = the owner when it is an SBasicGamePlayObject. By `G.TriggerMethode`: TriggerToggle (0) `TriggerEvent(G.Event, G, InstigatorTempo.Pawn)` on every use; TriggerControl (1) TriggerEvent when `bIsActive` is false, UntriggerEvent when it is true, then `bIsActive` flips; UnTriggerOnly (2) UntriggerEvent; other values log an error and fire nothing. Then, when `G.bTriggerOnceOnly`, the interaction's `bUsable` is cleared (matching the RE+ measurements: TriggerControl alternated, TriggerToggle sent Trigger every use).
22. Timing measured with animations: a 2 s hack fired 2.7 to 2.8 s after Use was tapped; a 3 s hack took about 3.5 s; walking away cancels.
23. `AddRemoveActorOnClientHud(Instigator, add)` (reliable simulated multicast, flag 0x01000000): add: `SHud(Instigator.myHUD).AddRemoveActorOnHud(self, true)` on each machine (only the instigator's own machine has his controller and HUD, so only the hacker's HUD adds it); remove: the local player's HUD removes it.

## States

Interaction object (server):

```mermaid
stateDiagram-v2
    [*] --> Idle
    Idle --> s_Hacking: Interact sub 2, hackable (messages 19 to mercs, 39 to all)
    s_Hacking --> Idle: PostInteract 1 (HandleEventInfo) or 0 (no event)
    Idle --> s_Maintain_Use: Interact sub 2, hold switch
    s_Maintain_Use --> s_Maintain_Use: PostInteract 3 (HandleEventInfo)
    s_Maintain_Use --> Idle: PostInteract 0 or 1 (HandleEventInfo)
    Idle --> WaitingBeforeActivation: InitInteract with activation delay
    WaitingBeforeActivation --> WaitingBeforeDesactivation: Timer (HandleEventInfo), deactivation delay set
    WaitingBeforeActivation --> Idle: Timer (HandleEventInfo)
    WaitingBeforeDesactivation --> Idle: Timer (HandleEventInfo)
```

Spy controller (hack):

```mermaid
stateDiagram-v2
    s_MenuInteraction --> s_Hack_GoTo: release on panel, server InitInteract
    s_Hack_GoTo --> Walking: bump / no owner
    s_Hack_GoTo --> s_Hack_Enter: TopSynchro
    s_Hack_Enter --> s_Hack_Use: AnimEnd 0 (Interaction sub 2 sent at enter)
    s_Hack_Enter --> s_Hack_Leave: damage (Failed)
    s_Hack_Use --> s_Hack_Leave: Timer iHackingTime (Successfull)
    s_Hack_Use --> s_Hack_FastLeave: move over 1000 / draw weapon
    s_Hack_Use --> s_Hack_Leave: damage (Failed)
    s_Hack_FastLeave --> Walking: immediately (PostInteract 0)
    s_Hack_Leave --> Walking: AnimEnd 0
```

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| UseRelativeLocation default | (55, 0, 0) | uu | decoded | def:SBase.SInterrupteur |
| SteeE UseRelativeLocation | (40, 0, 0) | uu | decoded | SteeE switches |
| UseRelativeRotation.Yaw | 32768 | rot units | decoded | def |
| Crouched use-spot lift | 27.5 | uu | decoded | s_Hack_GoTo, s_Maintain_GoTo |
| Hack switch search radius | 200 | uu | decoded | s_Hack_Use |
| No-switch fallback timer | 3.0 | s, looping | decoded | s_Hack_Use |
| Cancel threshold | 1000 | input units | decoded | s_Hack_Use, s_Maintain_Use |
| Crane hack | 5 | s | decoded | SInterrupteur28808 |
| Shutter hacks | 4 | s | decoded | SInterrupteur28807/28809/28810 |
| Interaction cylinder SteeE | 60 x 120 | uu | decoded | SteeE |
| Hack message to all | 39, 4 s on HUD | | decoded | STriggerInteraction.Interact, SHud |
| Merc warning | 19 (sWarnHackingText) | | decoded | STriggerInteraction.Interact |
| Tap to fire, 2 s hack | 2.7 to 2.8 | s | measured | RE+ MapBuilderPatterns |
| Reuse lock 20 s | refused at 19.4, accepted at 20.9 after a use; refused before level time 20 | s | measured | RE+ lab-power.md |
| Interaction sound | RA_interrupteur, volume 200/255, radius 60, pitch 64/64 | | decoded | def |

## Network

- Server: `InitInteract`, `Interact`, `PostInteract`, `HandleEventInfo`, the hack timer, `Trigger`/`UnTrigger`, the interaction's states (its `MirrorState`/`ChangeState` have no owning connection, so they run on the server only).
- Input machine: the repositioning, the cancel test, `Interaction(…, 2)` and `InteractionPost(…, n)` reliable client-to-server calls.
- OpenSCCT validates these calls against a server-owned reservation and the controller's interaction phase. Start accepts the matching GoTo, Enter, or Use state to tolerate arrival ordering; hack success requires Use or Leave after the server's minimum duration. A hold starts only from Maintain Enter or Use, because a remote player's authoritative controller remains in Enter while its input machine enters Use. Moving to an unrelated state invalidates completion; the owning player can still cancel, and another valid user can replace a stale started session.
- Replicated: switch `bMaintainsSwitch`, `iHackingTime`, `UseRelativeLocation`, `UseRelativeRotation` (`bNetInitial`), `iReusableTime`; interaction `MinTriggerTime`, `OwnerInterrupteur`, `bMaintain`, `bHackable`, and the base interaction fields (`bUsable`, `bInUse`, `iLastUsedTime`, `iReusableTime`). `ASInterrupteur` and `ASTriggerInteraction` have native replication lists (editor 0x111D1620, 0x111BD910).
- `AddRemoveActorOnClientHud` is a reliable multicast. Messages 19 and 39 are per-player reliable messages.
- `ClientDamage` runs on the owner, so the panel is freed by the owner's reliable `InteractionPost(…, 0)`; a spy killed mid-hack frees the panel this way (rule 17).
- `ServerWillStartGrabOnMe` runs on the server, where the menu selection is the server's own (the first usable entry when the menu opened; wheel scrolling is local). OpenSCCT: when the server's selection is not a switch entry, the entry of the hackable switch within 200 uu is freed instead. The spy's grab calls it before the merc's state change only when standing; when crouched the original calls it after, where it does nothing.

## Edge cases

- The hack duration is the `iHackingTime` of the first hackable switch within 200 uu of the pawn, not necessarily the selected one; success is reported to the selected one.
- The hack timer is looping and not cleared when `s_Hack_Use` ends (same note as the terminal).
- `bTriggerOnceOnly` is applied by the native only on a successful `HandleEventInfo`; an aborted crane hack can be retried (expected).
- `MinTriggerTime` is set only if the interaction is already in `s_Hacking` when the server enters `s_Hack_Use`; because `Interaction(…, 2)` is sent at the start of the enter animation, it normally is.
- A merc using a hackable switch (possible only where `UsableBy` allows it) plays no enter or leave animation; his leave is driven by the labels, not by `AnimEnd`.
- A second user while the panel is in `s_Hacking` gets the base refusal from `InitInteract`.
- Toggling `bUsable` with `Trigger` while a hack runs does not stop the hack.
- Spies with a weapon drawn holster first; mercs keep their weapon (`SPAWNDEFENSE` excluded from the holster in `s_Hack_GoTo`).
- Missile Strike's `MissD` and `MissE` authored data both make `Release Gas` initially usable by
  mercs. The gas panels are `SInterrupteur39168` / `17724`; their spy/merc `Central Shaft` panels
  are `39169` / `39199` and `17725` / `17755`, respectively. The gas interaction cylinder is
  radius 40, half-height 60 uu in `MissD`, versus 60 and 120 uu in `MissE`. Both gas panels fire
  `Gaz01`, which activates `SMagicEvent261`: `release` at 1 s and 60 s, and `cut_switch`
  immediately and after 90 s. The latter toggles the gas panel off during its own cooldown.
  These two cooldown events are its only authored incoming event edges. The separate shaft
  event `open_vent_bunker` has no link to gas usability; neither map contains a switch manager.
- In both variants, the Story root and child missions have no `AtResetEvent` (default `None`).
  The Deathmatch and DiskHunt reset-event chains also do not reach the gas switch, `Gaz01` or
  `cut_switch`. Original `SMission.Reset` fires its configured `AtResetEvent` after resetting
  its objectives. The checked authored graph therefore supplies no Story-start reset gate that
  would make gas availability depend on first opening the shaft.
- A fresh original `MissE` run with a solo merc, no spy and no prior shaft interaction returned
  `1` from native virtual `CanUse` at the correct panel pose; sampled other facings returned `0`.
  This controlled availability query supports leaving gas initially usable in OpenSCCT. It was
  an editor/autoplay run, not a complete interaction-menu/use sequence or a normal multiplayer
  match, so those runtime flows remain unverified. No shaft/spy-presence prerequisite is added.
- OpenSCCT applies the decoded `bInUse`, `bPresenceDetected`, merc hold-switch refusal and four
  busy-state checks in rule 20. The gas panels have no hold, hack, activation/deactivation delay
  or initial-state override. The known presence-flag writer is `SPresDetector.Tick`, which locks
  its own detector interaction during a warning; no shaft-to-gas setter was identified. A separate
  base-native branch checks `ASAmmoStockInteraction` and `ASGadBomb` (class globals `0x10C9F140` /
  `0x10C9D3B8`), so it does not apply to gas. None of these gates establishes a shaft prerequisite;
  the remaining estimated facing rules still prevent claiming complete native `CanUse` parity.

## Evidence

- Decompiled: `local/re-out/uc/SBase/SInterrupteur.uc`, `STriggerInteraction.uc`, `SInterrupteurManager.uc`, `SBasicGamePlayObject.uc`, `SPlayerController.uc` lines 8565 to 8874 (maintain states), 23230 to 23706 (hack states); `Engine/Actor.uc` lines 1577 to 1606 (Actor `ChangeState`/`MirrorState`).
- Super-call targets checked with `python -B -m ucdecomp dis` (`WaitingBeforeActivation.InitInteract` binds to `SInteractObject.InitInteract`; `s_Hacking.EndState` binds to `s_Maintain_Use.EndState`).
- Defaults: `classdefaults_effective.json` (SInterrupteur, STriggerInteraction).
- SteeE: SInterrupteur28807, 28808, 28809, 28810; DiskHunt/DM missions' `AtResetEvent = hackvolet01`.
- Natives: HandleEventInfo 1336 0x10AF6650, CanUse 1337 (thunk 0x10AB96F0); editor ASTriggerInteraction execCanUse 0x111BD780, GetOptimizedRepList 0x111BD910, execHandleEventInfo 0x111BDBE0; ASInterrupteur GetOptimizedRepList 0x111D1620; vtables ASInterrupteur 0x10BF8AC0, ASTriggerInteraction 0x10BFBAB0.
- Measured: RE+ `docs/MapBuilderPatterns.md` (panel timings, TriggerMethode behaviour), `docs/phase4/lab-power.md` (reuse lock).
- Gas investigation (2026-11-05): `.content/svmap/MissD/sim/gameplay.json` and
  `.content/svmap/MissE/sim/gameplay.json`, including the panels above, `SMagicEvent261` / `2945`,
  Story roots `SMission39221` / `17777`, and Deathmatch / DiskHunt reset chains. Each reset chain's
  conservative event-edge closure reaches 133 actors and excludes gas; the shaft closure reaches
  eight actors and excludes gas. Original `SMission.uc` lines 144 to 251 supplies the reset flow.
  REA read the installed Enhanced 4.0 Beta 5 engine (SHA-256
  `a98873aaec1de21fdcccc0afb9985e3ddcf38a9ddce1207214872d6428ec9950`): switch `CanUse`
  at `0x10AF6720` (vtable `0x10BFBAB0` + `0x138`) delegates to base `0x10AF6EF0`.
  Ghidra did not identify the former as a function, so the returned bytes were decoded locally
  with Capstone. Evidence bundle: `local/re-out/interaction-usability-2026-11-05.json`, records
  `ev_4e2f125732c910692156bb50b2da529706f220ab2d507fba4a86eff0fe62913e` and
  `ev_9fff16d0ec1194272b7c69c72db61f6f6d41d62319eefe2612dacd40e12cacaa`.
  This inspection covers those function bodies, not every called helper or event at runtime.
- State-name resolution: static registrations at `0x10926925` through `0x10926997` pair
  `0x3CF` with UTF-16 `s_Maintain_Use` at `0x10C59C7C`, `0x3D0` with `s_Hacking` at
  `0x10C59C9C`, `0x3D1` with `WaitingBeforeActivation` at `0x10C59CB0`, and `0x3D2` with
  `WaitingBeforeDesactivation` at `0x10C59CE0`. Each sequence pushes the name index, loads
  the string address into EDI and calls `0x1093B590`. Read from the same engine hash above
  using PE offsets and Capstone; bytes and decoded instructions are saved in
  `local/re-out/interaction-state-names-2026-11-05.json`. This static name resolution altered no process.
- Controlled original runtime query (2026-11-05), fresh isolated slot 8: Enhanced Beta 5 source
  engine SHA-256 `a98873aaec1de21fdcccc0afb9985e3ddcf38a9ddce1207214872d6428ec9950`,
  executable copy patched only for the instance mutex, SHA-256
  `2840396f7c360c8a7bbba978eab7128b3c39af76ac812ff7ed441589d0d46170`; `MissE` map SHA-256
  `47af37011facf2cfb9f24d7246e3d8016e8427e1e0430d09e523cd459448c4e9`.
  `AutoplayGameMode=1`, `Editeur=true`, solo merc, no spy and no shaft use. An explicit query of
  virtual `CanUse` at `0x10AF6720` returned `1` at pawn position
  `(-4384, 784.5163, -422.995)` with yaw `32768`; the other sampled cardinal facings returned `0`.
  The same facing was accepted at Z `-370.42`. Initial properties confirmed `bUsable=1`,
  `bInUse=0`, `bPresenceDetected=0`, `bMaintain=0`, `bHackable=0`, and zero reuse/last-use times.
  Evidence: `local/re-out/missile-gas-runtime-2026-11-05.json`, record
  `ev_0b11aa29349f478b517119b5a904130b421ff40477776213fec407f36a51ac77`; initial properties:
  `local/re-out/missile-gas-runtime-initial-2026-11-05.json`. Artifact hashes and launch conditions
  were recorded separately; the Frida observation itself does not bind artifact identity. This
  proves the queried native availability under these conditions, not the full UI or ordinary
  multiplayer flow.

## Open questions

1. `iLastUsedTime` updates (not written by `HandleEventInfo`). Trace: use the SteeE crane panel twice; read `bUsable`, `bIsActive` and the event log.
2. Exact base-native facing and collision-distance rules; the decoded switch busy-state, presence,
   use-lock and merc hold-switch gates are implemented (rule 20 and interaction-system.md rule 23).
3. `bInitialyActive`: `bIsActive` is the TriggerControl on/off state (rule 21); whether `bInitialyActive` seeds it at start or reset.
4. Whether `bTriggerOnceOnly` is also honoured by the plain-switch path and the delayed paths.

### Resolved

5. Server-side `AnimEnd(0)` in `s_Hack_Enter`. Resolved: the server runs the same action and frame advance, so `AnimEnd(0)` arrives after the same clip time measured from the tick the server executed the action (the move arrival), not from the client's start. Rule 10. Evidence: execute-action.md Network, anim.md rule 2 (ANSWERS-actions 17).
1. (part) `HandleEventInfo`: `TriggerMethode`, `bIsActive`, `bTriggerOnceOnly`, instigator. Resolved: rule 21 (toggle triggers every use, control alternates on `bIsActive`, untrigger-only untriggers, instigator `InstigatorTempo.Pawn`, `bTriggerOnceOnly` clears `bUsable`). Evidence: natives2-events.md rule 2 (ANSWERS-natives2 28).
