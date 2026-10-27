# Event dispatch (Tag, Event, Trigger, UnTrigger)

Units:
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

## Purpose

Every map device in SteeE talks to the others through one mechanism: an actor fires a name (its `Event`, or a
name held in a struct such as `DamagedMesh.Event`), and every live, non-static actor whose `Tag` equals that name
receives `Trigger` (or `UnTrigger`). This file fixes the exact dispatch rules, the meaning of the two arguments
(`Other`, `EventInstigator`), ordering, re-entrancy, and the core state-machine rules (probe masks, `GotoState`
to the current state) that the device specs rely on.

## Data

| Field | Owner | Type | Default | Notes |
|---|---|---|---|---|
| `Tag` | Actor | name | the class name (editor sets it on placement) | receiving key, compared case-insensitively (names are case-insensitive) |
| `Event` | Actor | name | `None` | the name an actor fires; `None` means nothing is fired |
| `Instigator` | Actor | Pawn | `None` | the pawn held responsible; devices copy it to pass credit along a chain |
| `InitialState` | Actor | name | `None` (Mover: `BumpOpenTimed`, Trigger: `NormalTrigger`) | state entered by `SetInitialState` |
| `bStatic` | Actor | bool | false | static actors are never dispatch targets |

## Rules

1. `TriggerEvent(EventName, Other, EventInstigator)`: if `EventName` is `None`, do nothing. Otherwise iterate the
   dynamic actor list (rule 4) and, for every actor `A` with `A.Tag == EventName`, call `A.Trigger(Other,
   EventInstigator)`. The caller is not excluded: an actor whose own Tag equals the name it fires triggers itself.
2. `UntriggerEvent` is identical but calls `A.UnTrigger(Other, EventInstigator)`.
3. `Other` is whatever the caller passes. By convention it is the actor that fires (a switch passes itself, a mover
   passes itself, an anim notify passes the animated actor). Receivers that filter by sender (SShaker
   `EventSender`, SGamePlayObject `DestroyOnEventFrom`, Mover `SpecialBehaviors.EventFromActor`) compare against
   `Other` by identity. SMagicEvent forwards the `Other` it was given, so the sender seen downstream is the actor that
   started the chain, not the SMagicEvent (see `magic-event.md`).
4. Iteration order and set. The iterator walks the level's actor list from the first dynamic (non-`bStatic`) actor to
   the end, in list order, skipping actors being destroyed. `bStatic` actors (StaticMeshActor, Light, SZoneTips,
   Volume subclasses that stay static) are never visited, so a Tag on them is inert. Only Tag is tested; class does
   not matter (the iterator class is `Actor`). The order among receivers is the order of the map's Actors array as
   loaded (dynamic actors keep their relative saved order). For SteeE the saved order is listed in
   `steee-chains.md`.
5. Dispatch is synchronous and re-entrant. A receiver may fire further events from inside its `Trigger`; those nested
   dispatches complete before the outer loop moves to its next receiver. There is no queue and no deduplication.
   The only delay mechanisms are latent state code (`Sleep`, `FinishInterpolation`), timers, and SMagicEvent queues.
6. A receiver with no `Trigger` override does nothing (the Actor event has an empty body). `BeginEvent` and
   `EndEvent` are empty on Actor; only Mover's `BumpButton` state gives them meaning.
7. Authority. `TriggerEvent`, `UntriggerEvent` and every device `Trigger`/`UnTrigger` in this folder are
   non-simulated. On a network client an actor whose `Role` is below `ROLE_AutonomousProxy` absorbs any call to a
   non-simulated script function (it is not executed). So the whole event graph runs on the server (or the
   standalone or listen host) only; clients see the results through replication and client RPCs.
8. Instigator conventions (all devices): a receiver that needs a responsible pawn and gets `EventInstigator == None`
   falls back to `Other.Instigator` (Mover, SGamePlayObject, SDamageVolume do this explicitly). Devices that start
   a chain from an animation pass `None` as the instigator (rule 10), so credit then rides on the animated actor's
   own `Instigator`, which it stored when it was triggered.
9. `SetInitialState` (called once when an actor is initialised for play): set `bScriptInitialized`, then
   `GotoState(InitialState)` if it is not `None`, else go to the class's `auto` state.
10. `AnimNotify_Trigger.Notify(Owner)`: `Owner.TriggerEvent(EventName, Owner, Pawn(Owner))`. For a non-pawn owner
    (SAnimatedMesh) the instigator argument is `None`. The notify runs wherever the animation is updated, but by
    rule 7 the dispatch only takes effect on the authority.
11. `ForceTriggerableEvenIfMoving()` returns false on Actor. SInterrupteur overrides it to return its
    `bMaintainsSwitch`. Movers ask it of `Other` (see `mover.md`, `IsTriggerable`).
12. `NotifyNoise(Loudness)`: if `Loudness == 0` use 1. For every PlayerController in the level call
    `ClientNotifyNoise(self, Loudness)` (a reliable server-to-owning-client call). SPlayerController shows it as a
    HUD noise marker (`SHud.NotifyHotSound`) unless the actor is an `SWeaponDefenseDummy`.

### Core state-machine rules the devices depend on

These are engine core rules (UE2 lineage), stated here because Mover, SGamePlayObject and SAnimatedMesh behaviour
depends on them. They are not yet confirmed in this binary (see Open questions).

13. Probe functions. `Trigger`, `UnTrigger`, `Timer`, `Touch`, `UnTouch`, `Bump`, `Attach`, `BeginState`,
    `EndState`, `EncroachingOn` and the other engine events are probe functions with a bit each in the state frame's
    probe mask (bit 6 Trigger, 7 UnTrigger, 8 Timer, 13 Touch, 14 UnTouch, 15 Bump, 16 BeginState, 17 EndState,
    19 Attach, 27 EncroachingOn; decoded from the states' `ignoreMask` values in the reflection dump).
    A call to a probe function whose bit is clear does nothing, whether the call comes from the engine or from
    script (`TriggerEvent` calling `A.Trigger`). `Disable('X')` clears the bit, `Enable('X')` sets it.
14. `ignores X` in a state: for a probe function, the bit is cleared in that state's ignore mask; for any other
    function the compiler emits an empty stub in the state. Either way the call does nothing in that state.
15. Entering a different state recomputes the probe mask from the new state and the class (so it re-enables any
    `Disable`d probe), runs `EndState` on the old state and `BeginState` on the new one, and clears any running
    latent action.
16. `GotoState(S, L)`: an omitted `S` means the current state. If `S` is the current state no state change happens
    (no `EndState`/`BeginState`, the probe mask is kept). In every case the state code then jumps to label `L`, or
    to `Begin` when `L` is omitted (or `None`). If that label does not exist in the state (or its parents), the state
    code stops (the running latent action is dropped and nothing runs until the next `GotoState` with a valid label).
    So `GotoState(CurrentStateName)` on a state without a `Begin` label means "stop my state code here".
    Decoded (natives2-events.md rule 3; native 141, editor and game): an unknown state goes to no state (the class's own
    code) with EndState run; the probe mask of a new state is `(Class.ProbeMask | State.ProbeMask) & State.IgnoreMask`
    (64 bits, probe name indices 300 to 363); `Disable(name)` clears a bit only until the next state change, and
    `Enable` sets it back only where the class or state has the probe function and the state does not ignore it; a
    state change made inside EndState or BeginState pre-empts the outer one.
17. Latent `Sleep(t)` with `t <= 0` returns on the next state-code update. `FinishInterpolation` returns once
    `bInterpolating` is false.

## Network

- Server (authority) only: the whole dispatch. Nothing in this file is replicated.
- `ClientNotifyNoise` is a reliable server-to-client function on PlayerController (sent to the owning connection).
- Receivers make their results visible on clients in three ways: replicated properties (mesh, skin, hidden,
  ambient sound, the GE mask), calls on a player's own controller (`ClientShake`, `ClientNotifyNoise`), and
  server-to-client calls on unowned, always-relevant map actors (`Emitter.ReplicateTrigger` when `bNetworkTrigger`,
  `MusicTrigger.ClientPlayStopMusic`, `SGamePlayObject.SpawnDamageEmitter`). The last kind is expected to reach every
  client in this build (see `sound-music.md`); the net spec must provide it.
- `Emitter.Trigger` calls `ReplicateTrigger`, which fires every sub-emitter (`ParticleEmitter.Trigger`). With
  `bNetworkTrigger` (set on 1,304 of the map emitters) the emitter's `PreBeginPlay` makes it an always-relevant
  simulated proxy so the call reaches clients; without it only the server's own screen shows the burst. The rewrite
  keeps map emitters unreplicated: the authority raises a `ReplicateTrigger` notify of the emitter, the device-event
  relay sends it to every client, and each machine's view fires the drawn particle system of that map id. A client
  copy (Role below AutonomousProxy) absorbs a direct Trigger (rule 7). Event-triggered emitters are the largest
  receiver set after movers and props: 1,511 event edges on 44 maps (debris on prop damage stages, SMagicEvent
  sequences, presence triggers, switches).

## Edge cases

- Self-trigger: an actor firing its own Tag receives its own Trigger inside its own call stack.
- Destroying an actor while a dispatch is iterating: destroyed actors are skipped from then on; the current one
  finishes its call.
- Deep chains recurse in script; the engine's script recursion guard (about 250 nested calls in UE2) is far above
  anything SteeE reaches (deepest chain is about 6).
- Events fired from `PostBeginPlay` (SGamePlayObject `OnResetEvent`) reach only actors already spawned and
  initialised in list order before them; receivers later in the list may not have run `BeginPlay` yet. SteeE sets
  no `OnResetEvent`.
- Many SteeE StaticMeshActors carry Tags (`Squ_*`, `STM_*`): they are static and can never be triggered.

## Evidence

- Script: `local/re-out/uc/Engine/Actor.uc` (TriggerEvent, UntriggerEvent, ForceTriggerableEvenIfMoving,
  SetInitialState, NotifyNoise), `Engine/AnimNotify_Trigger.uc`, `SBase/SPlayerController.uc` (ClientNotifyNoise),
  `SBase/SInterrupteur.uc` (ForceTriggerableEvenIfMoving override).
- Probe bits: reflection `local/reflect/game.json` `states[]` (`Engine.Mover.s_TemporaryInactive` ignoreMask
  `0xffffffffffffff3f`, `SBase.SGamePlayObject.s_WaitForDamageReset` ignoreMask `0xffffffffffffdf3f` with stub
  functions only for its non-probe ignores).
- Natives: GotoState `0x10949900`, Enable `0x10949a40`, Disable `0x10949b10`, DynamicActors `0x10a283d0`,
  AllActors `0x10a28310`, IsInState `0x10949ed0`, GetStateName `0x10949f60`, Sleep `0x10a230f0`,
  FinishInterpolation `0x10a23440` (natives_all.txt).

## Open questions

1. Rule 13 for script calls: the native GotoState and probe masks are decoded (rule 16); that a script call to a
   disabled probe function is also dropped (not only an engine call) is not. Scenario: a TriggerOpenOnceOnly mover
   triggered twice 0.2 s apart; with the rules as written the second trigger is ignored.
2. Confirm rule 7 (non-simulated calls absorbed on simulated proxies) in the script call path; it decides whether
   anim notifies could ever fire events on clients.
3. Confirm the dynamic actor list order at run time equals the saved Actors array order for SteeE (the cooked map
   may already be sorted). Scenario: log the order of `Trigger` arrivals for `blast01` receivers.

### Resolved

1. (part) Native GotoState and probe rules. Resolved: same-state calls keep probes and jump to the label (Begin), a
   missing label stops the code; unknown states go to None with EndState; the mask is (Class or State) and Ignore;
   Disable lasts to the next state change. Rule 16. Evidence: natives2-events.md rule 3 (ANSWERS-natives2 29).
4. `bDontSaveCurrentState`. Resolved: Controller.GotoState stores PreviousState = the state left on success, and also
   on a same-state call, unless the flag is given; Object.GotoState keeps no previous state. Evidence:
   natives2-events.md rule 3.3 (ANSWERS-natives2 30).
