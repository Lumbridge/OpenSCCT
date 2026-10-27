# SMessageTrigger (map text messages)

Units:
- def:SBase.SMessageTrigger
- uc:SBase.SMessageTrigger:PostBeginPlay
- uc:SBase.SMessageTrigger:DisplayMessage
- uc:SBase.SMessageTrigger:Trigger
- uc:SBase.SMessageTrigger:UnTrigger
- rep:SBase.SMessageTrigger.DisplayMessage

## Purpose

A scripted line in the HUD's zone message area ("Go to Zone 2", "Go to the Attic", "Vault open"), one text for spies
and one for mercs. Fired by SMagicEvent chains, objectives, switches, presence triggers, sound triggers and gameplay
objects. 30 on 19 maps.

## Data

| Field | Type | Default | Meaning |
|---|---|---|---|
| `MessageSpy` | localized string, edit | "" | the spies' line |
| `MessageMerc` | localized string, edit | "" | the mercs' line |
| `DisplayTime` | int, edit | 0 | seconds shown (the HUD keeps at least MessageMustStayTime) |
| `Priority` | byte, edit | 0 | stack priority (the zone area keeps one entry, the lowest priority goes) |
| Defaults | | | `RemoteRole=ROLE_SimulatedProxy`, `bNoDelete`, no movement replication; not always relevant |

## Rules

1. `PostBeginPlay` (every machine): draw type None.
2. `Trigger`: `DisplayMessage(false)`; `UnTrigger`: `DisplayMessage(true)`. Only the server's copy is triggered.
3. `DisplayMessage(bRemove)` (simulated, broadcast from the server): on each machine, when the local player
   controller has a pawn: a spy pawn (`SPawnAttaque`) reads `MessageSpy`, any other `MessageMerc`; the HUD's
   `AddInfoMessageToStack(text, bRemove ? 0 : DisplayTime, Priority)`.
4. `AddInfoMessageToStack`: empty text ignored; the same text already shown stays max(time, MessageMustStayTime)
   from now; otherwise appended and, past one entry, the first entry of the lowest priority is dropped
   (spec/hud/messages.md). So an untrigger does not remove the line: it re-shows it for the minimum time.

## Map data

`DisplayTime` 4 to 20 (5, 10, 8, 6 most common); `Priority` 5 on 12, 3 on 2, default 0 on the rest; `MessageMerc`
missing (empty, nothing shown to mercs) on one trigger. Maps: BankD, BankP, CpusS, MissD, MissE, MissP, OrphD,
OrphE, OrphT, PanaD, PanaS, PyroD, SancD, StatD, StatE, StatS, WareD, ZiopD, ZiopS.

## Network

`DisplayMessage` is a reliable function with the broadcast flag: the server sends it to every client that has an
open channel for the trigger and runs it itself (a listen server's player). The trigger is not always relevant, so
a client too far from it (outside the 2500 uu radius and out of sight) has no channel and misses the line, as in
the original.

## Edge cases

- A player without a pawn (dead, spectating) gets nothing.
- Both teams' lines go to every machine; each shows only its own side's.

## Evidence

- `local/re-out/uc/SBase/SMessageTrigger.uc`, `SBase/SHud.uc` (AddInfoMessageToStack).
- `.content/svmap/*/sim/gameplay.json` survey.

## Open questions

1. Confirm in a capture that a far-away client misses the line (relevancy), for example the MissD "Go to Zone 2".
