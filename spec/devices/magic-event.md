# SMagicEvent (delayed event groups)

Units:
- def:SBase.SMagicEvent
- uc:SBase.SMagicEvent:BeginPlay
- uc:SBase.SMagicEvent:Trigger
- uc:SBase.SMagicEvent:UnTrigger
- nat:SBase.SMagicEvent.NativeTrigger

## Purpose

A relay that, when triggered or untriggered, fires a list of named events, each after its own delay, with its own
Trigger/Untrigger type, optionally one step per activation (sequence) and a limited number of cycles. SteeE has one:
`SMagicEvent232` (Tag `molotov`) that lights and later puts out the molotov fire.

## Data

| Field | Type | Default | Meaning |
|---|---|---|---|
| `Groups` | array of EventGroup | empty | |
| EventGroup.`EventGroup` | array of SuperEvent | empty | the entries |
| EventGroup.`Repeat` | int | 0 | 0 unlimited cycles, n > 0 that many, -1 or below disabled |
| EventGroup.`Sequence` | bool | false | one entry per activation |
| EventGroup.`SequenceIndex` | int | 0 | runtime |
| SuperEvent.`Event` | name | None | name to fire |
| SuperEvent.`Delay` | float | 0 | seconds after the activation |
| SuperEvent.`Type` | EEventType | EVT_Trigger | 0 Trigger, 1 Untrigger, 2 TriggerUntrigger, 3 UntriggerTrigger |
| SuperEvent.`ValidOn` | EEventType | EVT_Trigger | which activation the entry answers |
| `StopActor` | Actor | None | a trigger from this actor clears the queue |
| `EventQueue` | array of {SuperEvent copy, Other, EventInstigator, Time} | empty | runtime |
| Defaults | | | `RemoteRole=ROLE_None`, hidden, `bNoDelete`, `NetUpdateFrequency=10`, `native` |

## Rules

1. `BeginPlay`: for every group set `SequenceIndex = 0`, and set `Repeat = -1` for a group with no entries. Start a
   repeating 0.05 s timer (the class has no script `Timer`; the timer drives the native queue, rule 5).
2. `Trigger(Other, I)` calls `NativeTrigger(Other, I, true)`; `UnTrigger` calls `NativeTrigger(Other, I, false)`.
3. NativeTrigger contract (decoded from the editor twin, see Evidence):
   a. If `Other == StopActor` the pending queue is cleared and the call ends there: no group answers. There is no
      None check (game 0x10ae9d5b), so a None Other with no StopActor also only clears the queue.
   b. For each group: skip if `Repeat <= -1`.
   c. Non-sequence group: visit every entry. Sequence group: visit only the entry at `SequenceIndex`.
   d. An entry answers when the activation matches `ValidOn`: `EVT_Trigger` accepts Trigger, `EVT_Untrigger` accepts
      Untrigger, the two combined values accept either. A non-answering sequential entry does not advance the index.
   e. An answering entry with `Delay == 0` is dispatched at once. With `Delay > 0`, a copy of the entry is queued
      with due time `Level.TimeSeconds + Delay`, keeping this activation's `Other` and instigator. All entries of one
      activation share the same time origin; delays do not accumulate between entries.
   f. Sequence group: an answering entry advances `SequenceIndex`, wrapping after the last entry; only the wrap
      counts as a completed cycle.
   g. Cycle accounting: after a cycle (non-sequence: each activation; sequence: each wrap) a positive `Repeat` is
      decremented; reaching 0 turns it into -1 (disabled). `Repeat == 0` never decrements.
4. Dispatch of an entry (immediate or when its due time is reached): by `Type`: Trigger: `TriggerEvent(Event, Other,
   I)`; Untrigger: `UntriggerEvent(...)`; TriggerUntrigger: Trigger, then the entry's Type becomes UntriggerTrigger;
   UntriggerTrigger: Untrigger, then it becomes TriggerUntrigger. For a queued copy the type change applies to the
   copy, which is removed after dispatch: the authored entry keeps its type, so each activation fires a delayed
   alternating entry with its authored type.
5. Queue update (decoded, natives2-events.md rule 1.4): the script's `SetTimer(0.05, true)` drives a native timer
   (the actor tick advances the timer counter; extra periods in one tick are dropped). Each expiry walks the queue in
   order and fires and removes every entry with `Time <= Level.TimeSeconds`; later entries wait. Delayed events are
   therefore up to 0.05 s late and fire in queue order. Repeat 0 is unlimited, n > 0 means n more times, -1 spent;
   a Sequence group advances only on an event that passes its filter.
6. `Other` passed downstream is the activation's `Other` (the actor that started the chain), not the SMagicEvent.

## States

None (all logic native).

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| update timer | 0.05 | s | decoded | BeginPlay |
| SteeE molotov group | `moloaie` at 1.0 s Type TriggerUntrigger ValidOn Trigger; `moloaie` at 32.0 s same; Repeat 0; Sequence false | | decoded | SMagicEvent232 |

SteeE result for one activation (the molotov bottle breaking, `steee-chains.md`): at +1.0 s `moloaie` Trigger
(SDamageVolume228 `bToggle` turns on, SAmbientSoundTrigger273 fire loop starts); at +32.0 s `moloaie` Trigger again
(volume toggles off, loop toggles off). The fire burns 31 s.

## Network

Server only (`ROLE_None`). Receivers replicate their own results.

## Edge cases

- `ValidOn` defaults to Trigger, so an UnTrigger reaching an SMagicEvent with default entries does nothing.
- A group with no entries is disabled at BeginPlay.
- A sequence group needs one activation per step; delayed steps of earlier activations still fire.
- An activation while earlier delayed entries are pending adds more entries; nothing deduplicates.

## Evidence

- `local/re-out/uc/SBase/SMagicEvent.uc`; SteeE.sdc export `SMagicEvent232`.
- Native semantics decoded on the editor twin: `SCCT_Versus_Reloaded_Editor_mb-main/tests/SMagicEventSemantics.md`
  (editor `ChaosTheory_Editor.exe` NativeTrigger thunk `0x111d30f0`, group loop `0x111d31ab`, queue
  `0x111d327b`/`0x111d339a`, update `0x111d2ff0`, dispatch `0x111d2f10`).
- Game native: `SBase.SMagicEvent.NativeTrigger` iNative 1793 at `0x10ae9cf0` (natives_all.txt).

## Open questions

3. Whether the queue survives a round reset (not relevant to Story, one round).

### Resolved

2. Game build check (2026-10-27, game 0x10ae9cf0 disassembled): the group loop matches the editor twin; the StopActor
   test returns after clearing the queue and has no None check (rule 3a corrected).

1. Which routine runs the queue and how often. Resolved: the 0.05 s timer from the script `SetTimer`; each expiry
   fires every entry with `Time <= TimeSeconds` in queue order. Repeat 0 is unlimited; Sequence groups advance only on
   a passing event. Rule 5. Evidence: natives2-events.md rule 1 (ANSWERS-natives2 27).
