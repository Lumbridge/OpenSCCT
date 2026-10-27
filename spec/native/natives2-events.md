# Event, state and objective natives; motion tracking

Units:
- nat:SBase.SMagicEvent.NativeTrigger (and eng:ASMagicEvent::Tick, the queue)
- nat:SBase.STriggerInteraction.HandleEventInfo
- nat:Core.Object.GotoState, nat:Engine.Controller.GotoState, nat:Core.Object.Enable, nat:Core.Object.Disable
- rl:Engine.Controller.GotoState
- nat:SBase.SObjective.Desactivate, nat:SBase.SObjective.Reset, nat:SBase.SObjective.ScheduleReset
- nat:SBase.SMission.Desactivate, nat:SBase.SMission.NotifyChildTaken
- nat:SBase.SObjectiveTrigger.Desactivate, nat:SBase.SObjectiveTrigger.IsActive,
  nat:SBase.SObjectiveTrigger.GetTriggerTime
- nat:SBase.SMotionTrackingVision.GetActors, rl:SBase.SMotionTrackingVision.GetActors

## Purpose

The natives behind map events, the UnrealScript state machine, objectives and the merc motion tracker, decoded
from the editor build (game addresses in Evidence). They answer the open questions of magic-event.md, events.md,
missions.md, objective-trigger.md, switch-hack.md and motion-tracking.md.

## Rules

### 1. SMagicEvent.NativeTrigger(Other, EventInstigator, bTrigger)

Data: Groups[] of {EventGroup[] of SuperEvent {Event, Delay, Type, ValidOn}, Repeat, SequenceIndex, Sequence};
EventQueue[] of {SuperEvent copy, Other, EventInstigator, Time}.
1. Other == StopActor: the queue is emptied; nothing fires.
2. Otherwise for each group in order, only while Repeat >= 0 (Repeat 0 = unlimited, n > 0 = n more times,
   -1 = spent; BeginPlay sets -1 for an empty group):
   - A SuperEvent passes the ValidOn filter when ValidOn > 1, or ValidOn 0 (EVT_Trigger) with bTrigger, or
     ValidOn 1 (EVT_Untrigger) without it.
   - Not Sequence: every event of the group in order; a passing event with Delay 0 fires at once (rule 3), with a
     Delay it is appended to the queue with Time = Delay + Level.TimeSeconds. Then the group's Repeat step.
   - Sequence: only EventGroup[SequenceIndex]. When it passes it fires or is queued as above, SequenceIndex
     advances, and on reaching the end it returns to 0 and the Repeat step runs. A failing filter changes
     nothing.
   - Repeat step: when Repeat > 0 it decreases; reaching 0 makes it -1.
3. Firing a SuperEvent by Type: 0 TriggerEvent(Event, Other, EventInstigator); 1 UntriggerEvent; 2 TriggerEvent and
   Type becomes 3; 3 UntriggerEvent and Type becomes 2. The toggle is stored in the group's event when fired at
   once, but only in the queue copy when fired from the queue (so a delayed toggle event always starts from the
   authored Type).
4. Queue: the script's SetTimer(0.05, true) drives a native timer (the actor tick advances TimerCounter; when it
   reaches TimerRate the extra periods are dropped). Each expiry walks the queue in order and fires and removes
   every entry with `Time <= Level.TimeSeconds`; later entries wait. Events are therefore up to 0.05 s late and
   fire in queue order.

### 2. STriggerInteraction.HandleEventInfo()

G = the Owner when it is an SBasicGamePlayObject. By G.TriggerMethode:
- TriggerToggle (0): TriggerEvent(G.Event, G, InstigatorTempo.Pawn) on every use.
- TriggerControl (1): TriggerEvent when bIsActive is false, UntriggerEvent when it is true; then bIsActive flips.
- UnTriggerOnly (2): UntriggerEvent.
- Other values: a logged error, no event.
Then, when G.bTriggerOnceOnly, the interaction's bUsable is cleared.

### 3. GotoState and probe masks

Object.GotoState (native 141) and Controller.GotoState (native 1175) take (NewState, Label, bDontSaveCurrentState).
An omitted NewState means the current state. Current = the current state's name, or None in the class's own
code.
1. When NewState equals Current, no state change happens: no EndState, no BeginState, the probe mask is kept.
2. Otherwise the engine GotoState(NewState):
   - the latent action is cleared;
   - NewState 'Auto' picks the class's auto state; a name that is not a state of the class goes to no state (None,
     the class's own code) and the result is "not found";
   - EndState runs when leaving a named state for a different one (also when the target was not found), unless
     already inside an EndState; a state change made inside EndState pre-empts this one;
   - the new state's node is set, its code pointer cleared, and the probe mask becomes
     `(Class.ProbeMask | State.ProbeMask) & State.IgnoreMask` (64 bits; probe names are the name indices 300 to
     363);
   - BeginState runs when entering a named state different from Current; a change made inside BeginState
     pre-empts;
   - the result is success when the new state is named.
3. Controller.GotoState only: on success (or when NewState equals Current), PreviousState = Current unless
   bDontSaveCurrentState. So a same-state call also overwrites PreviousState with the current state.
4. Then, on success or same state, the state code jumps to Label (Begin when omitted); a missing label stops the
   state code (no latent code runs). On "not found" a warning is logged for names other than None and Auto.
5. IsProbing(name) is true for names outside 300..363, for objects without a state frame, and otherwise when the
   name's bit is set in the frame's probe mask. Disable(name) clears the bit until the next state change; Enable
   sets it back only where the class or state has the probe function and the state does not ignore it. Names
   outside the probe range log "not a probe func" and do nothing.
6. Reloaded's Controller.GotoState (Reloaded.Core 0x10024080) follows rules 1 to 4, reading bDontSaveCurrentState
   only when present, and adds an integrity check on the local player's controller: when the game code has been
   modified the target state is replaced by s_MultiSpectating. With an unmodified game the behaviour is stock.

### 4. Objective natives

SObjective.Desactivate (virtual): bDelayedActivation = false; ObjectiveTaken = true; the actor timer is cleared
(TimerRate 0, TimerCounter 0, no loop); each trigger in Triggers is deactivated (a missing entry is logged); then
Ev_AddRemoveObjectiveOnClientHud(false).

SMission.Desactivate: the SObjective rule, then ObjectiveTaken = true and every non-empty Objectives entry is
deactivated.

SObjectiveTrigger.Desactivate: bActive and bLocked cleared; Triggering(false); bEMFIsOn cleared; then
GotoState_Inactive unless the current state is s_TemporaryInactive (or a state below it).

SObjectiveTrigger.IsActive returns NOT bActive (both builds); no script calls it. GetTriggerTime returns
`float(CaptureTime) + TriggerTime` while bTriggering, else 0.

SObjective.ScheduleReset(Time): TimerRate = Time, TimerCounter = 0, timer not looping.

SObjective.Reset:
1. ActivationTime > 0 and not bDelayedActivation: bDelayedActivation = true and ScheduleReset(ActivationTime); stop.
2. Otherwise: bDelayedActivation = false; every trigger gets DoReset; ObjectiveTaken = false; activated = true.
3. RandomTriggers > 0 and below the trigger count: a copy of Triggers loses RandomTriggers entries chosen with the
   C runtime rand() (index `rand() % remaining`, 0 when empty), and every trigger left in the copy is deactivated.
   So RandomTriggers triggers stay active.
4. bShowStatusOnHud: Ev_AddRemoveObjectiveOnClientHud(true).

SMission.NotifyChildTaken(Scorer):
1. Nothing when the mission is already ObjectiveTaken.
2. ObjectivesLeft = max(ObjectivesLeft - 1, 0) unless Objectives[CurrentObjectiveIndex] is itself an SMission;
   event UpdateObjectivesLeft.
3. Not bChained: count the non-empty children with ObjectiveTaken. When not all are taken and (the count is below
   MinimumObjectives or MinimumObjectives < 1): stop. Otherwise NotifyTaken(Scorer, None) and ObjectivesLeft = 0.
4. bChained: CurrentObjectiveIndex += 1. When it is still inside Objectives: that objective gets DoReset and the
   mission DoBroadcastLocalizedMessage(CurrentObjectiveIndex); stop. Past the end: NotifyTaken(Scorer, None) and
   ObjectivesLeft = 0.

### 5. SMotionTrackingVision.GetActors (stock)

1. Needs PlayerOwner and its Pawn. Every lock is marked stale.
2. Viewer point V: PlayerOwner in s_UseObject_CamNetwork with a UsedObject: the used object's Location; with a
   ViewTarget in s_MultiSpectating, s_Spectating_Visitor or s_MultiSpectating_WhenKilled: the controller's
   Location; otherwise the Pawn's Location.
3. For every actor A of the level in list order (not hidden): `|A - V|^2 < (MaxDetectedDistance + A.R)^2`,
   `|A.Velocity| >= MinDetectedVelocity`, and A is a Projectile, or a Pawn other than PlayerOwner.Pawn.
4. Sight: one line from V to A, flags `0x86`, single result. Hits on actors with bSeeThroughInMotionVision are
   passed over; with a single result that means a see-through first hit counts as clear even when a wall stands
   behind it. Clear when the first counting hit is nothing or A.
5. Visible: an existing lock is marked fresh; otherwise a lock {A, StartTime = TimeSeconds, EndTime = 0, flags 0}
   is appended and detectsound plays at V (volume 1, radius 64, pitch 1).
6. Then every stale lock: EndTime 0 becomes TimeSeconds + KeepLockTime; a lock with `EndTime + OutTime <
   TimeSeconds` is removed. A lock seen again keeps its old EndTime (it is not reset to 0).
No friend flag and no screen-size limit is applied here (MinWidth, MaxWidth, MinHeight, MaxHeight are not read).

### 6. GetActors in Reloaded (Reloaded.Core 0x100ca650)

As rule 5, with these differences:
- the viewer actor is the Pawn, or the ViewTarget when Reloaded's internal switch is on;
- an actor of one pawn class whose DesiredGlassEffect is 255 is skipped (the cloaked spy);
- sight first uses flags `0x20086` (all hits, walked past see-through actors to the first real blocker); only when
  that line hits nothing is the stock `0x86` line made.

## Constants

| Name | Value | Provenance |
|---|---|---|
| magic event queue period | 0.05 s (script SetTimer), fire when Time <= TimeSeconds | decoded |
| probe name range | 300 to 363, 64-bit masks | decoded |
| default label | Begin | decoded |
| motion lock sound | radius 64, volume 1, pitch 1 | decoded |
| motion sight flags | stock 0x86 single result; Reloaded 0x20086 then 0x86 | decoded |

## Network

NativeTrigger, HandleEventInfo and the objective natives run on the server. GotoState runs on every machine that
runs the state code. GetActors runs on the merc's own machine (its timer in s_Active).

## Edge cases

- A Sequence group whose current event fails ValidOn stays on that event.
- Reset with RandomTriggers equal to or above the trigger count deactivates nothing.
- GotoState to a misspelt state leaves the current state (EndState runs) for the class code.

## Evidence

- Editor: ASMagicEvent::execNativeTrigger 0x111d30f0, ASMagicEvent::Tick 0x111d34d0, timer 0x111d3420, queue
  0x111d2ff0, fire 0x111d2f10; ASTriggerInteraction::execHandleEventInfo 0x111bdbe0; UObject::execGotoState
  0x10ff00a0, UObject::GotoState 0x10ff0f50, AController::execGotoState 0x111165d0, UObject::IsProbing 0x10f3c630;
  ASObjective::execDesactivate 0x1121f9a0 (body 0x1121fe70), execReset 0x11220210 (body 0x1121ffa0),
  execScheduleReset 0x1121fa80 (body 0x1121f960); ASMission::execDesactivate 0x11220290 (body 0x112205f0),
  execNotifyChildTaken 0x112203f0; ASObjectiveTrigger::execDesactivate 0x111ce0f0 (body 0x111ce1e0),
  execIsActive 0x111ce0d0, execGetTriggerTime 0x111ce1a0; ASMotionTrackingVision::execGetActors 0x11220b70.
- Game: NativeTrigger 0x10ae9cf0, ASMagicEvent::Tick 0x10aea140, HandleEventInfo 0x10af6650, execGotoState
  0x10949900, GotoState 0x109415a0, execEnable 0x10949a40, execDisable 0x10949b10, Controller GotoState 0x109f1f20,
  NotifyChildTaken 0x10abffb0, IsActive 0x10ae8b30, GetTriggerTime 0x10ae8af0, GetActors 0x10ac0a30. The game's
  Desactivate execs are one shared stub (0x10ae9c60) that calls the virtual through the vtable.
- Reloaded.Core: GotoState 0x10024080, GetActors 0x100236f0 (body 0x100ca650).
- Names from the reflection dumps (name table, fields, bits); script context from the decompiled packages (local).

## Open questions

1. SObjective.NotifyTriggering (editor 0x1121fb40): MinTriggerTime is the smallest positive trigger time; the
   RemainingTime rounding was not read from the disassembly in this pass.
2. Reloaded's GetActors switch (viewer = ViewTarget) and the skipped class: the switch value in the reference
   install (reloaded-hooks.md open question 1).
