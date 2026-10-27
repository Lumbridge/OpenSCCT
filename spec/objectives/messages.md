# Objective messages, HUD lists and radar markers

Units:
- def:SBase.SGameMessages
- uc:SBase.SGameMessages:GetString (switches 3, 10, 11, 12, 15, 19, 21, 22, 23, 27, 28, 29, 36, 37, 39, 53, 54)
- uc:SBase.SGameInfo:BroadcastTeamLocalizedMessage, uc:Engine.Actor:BroadcastLocalizedMessage, uc:Engine.GameInfo:BroadcastLocalized, uc:Engine.BroadcastHandler:AllowBroadcastLocalized, uc:Engine.BroadcastHandler:BroadcastLocalized, uc:Engine.PlayerController:ReceiveLocalizedMessage, uc:Engine.LocalMessage:ClientReceive
- partial: uc:SBase.SHud:LocalizedMessage (routing for the switches above), uc:SBase.SHud:AddMessageToStack, uc:SBase.SHud:SetGameMessage, uc:SBase.SHud:AddRemoveActorOnHud
- nat:SBase.SHud.AddObjective, nat:SBase.SHud.RemoveObjective
- rep:SBase.SObjective.AddRemoveObjectiveOnClientHud, rep:SBase.SObjectiveTrigger.AddRemoveObjectiveForRadar, rep:SBase.STriggerInteraction.AddRemoveActorOnClientHud, rep:SBase.SFlag.SetFlagCarrier, rep:Engine.PlayerController.ReceiveLocalizedMessage

## Purpose

What the objective and interaction code tells players, how it reaches them, and which HUD list each objective event feeds. The HUD drawing itself (marker shapes, fades, radar) belongs to the HUD spec; this spec fixes the data and the routing so the objective port can feed the same lists.

## Data

Message texts (English, `classdefaults_localized.json`; `@` joins with one space):

| Switch | Text | Built from |
|---|---|---|
| 3 | the SObjective's `Description` | obj |
| 15 | the SObjective's `DescriptionDEF` | obj |
| 10 | `TriggerName @ ": Data stolen"` | trigger |
| 11 | `TriggerName @ ": Data dropped"` | trigger |
| 12 | `TriggerName @ ": Data secured"` | trigger |
| 22 | `TriggerName @ ": Data captured"` | trigger |
| 19 | the SInterrupteur's `sWarnHackingText` ("" if none) | switch |
| 21 | "Security failure" | (chaff on a game object) |
| 23 | "Neutralization process interrupted" | none |
| 27 | `Objective.ObjectiveName @ ": Under attack"` | trigger |
| 28 | `Objective.ObjectiveName @ ": Secured"` | trigger |
| 29 | `Objective.ObjectiveName @ ": Bombed"` | trigger |
| 36 | `TriggerName @ ": Neutralization in progress"` | trigger |
| 37 | `Objective.ObjectiveName @ ": Neutralized"` | trigger |
| 39 | "Hacking in progress..." | none |
| 53 | "Go to the drop zone (secured case)" | none |
| 54 | "Retrieve the hard drive" | none |

Unused or other-area strings carry their source defaults as listed in `def:SBase.SGameMessages` (for example `ObjectiveTaken` "ND133 neutralized" has no switch). `SGameMessages` is a `LocalMessage`: Lifetime 5, white, XPos 0.5, YPos 0.7, console message.

SteeE examples: 36 "Laboratory (2nd floor) : Neutralization in progress", 37 "Laboratory (2nd floor) : Neutralized", 10 "Secret Files : Data stolen", 19 "Crane Control".

## Rules

Delivery:

1. `Actor.BroadcastLocalizedMessage(class, switch, PRI1, PRI2, obj)` (server) goes through `GameInfo.BroadcastLocalized` and the broadcast handler to every `PlayerController` in the controller list: `ReceiveLocalizedMessage(...)`.
2. `SGameInfo.BroadcastTeamLocalizedMessage(team, class, switch, PRI1, PRI2, obj)` (server): every `PlayerController` whose `PRI.Team.TeamIndex == team` (0 spies, 1 mercs) gets `ReceiveLocalizedMessage`.
3. `ReceiveLocalizedMessage` is a reliable server-to-client function of the player controller (local call for the listen host and for client-side calls such as the idle reminder). On the receiving machine it calls `class.ClientReceive(...)`, which calls `myHUD.LocalizedMessage(...)` and, for console messages, the console (twice in practice: once from `ClientReceive` through the interaction master with 6 s, once from `ReceiveLocalizedMessage` itself with 0 s).
4. The text is built on the receiving machine from the replicated object reference. If `obj` is not relevant to that client (the disk can be out of relevance) the reference arrives as None and the name part is empty.

HUD routing (`SHud.LocalizedMessage`, SGameMessages only):

5. Switches 0, 2, 3, 4, 5, 6, 7, 15, 16, 17, 20, 53, 54 go to the game message queue: `SetGameMessage(text, 2)` (priority 2; a text already queued is not added again).
6. Every other switch goes to the message stack: alarms use their own timing; 39 uses time 4 s, priority 0; all others time 10 s, priority 0. `AddMessageToStack(text, time, prio)`: empty texts are ignored; a text already on the stack has its stay time extended to `max(time, MessageMustStayTime)` from now; otherwise it is appended with stay `max(time, MessageMustStayTime)` (MessageMustStayTime 4.0), and when the stack exceeds `MaxStackedMessages` (2) the lowest priority entry (first such) is removed. Switch 23 also pushes a one-second blank entry `" "` with priority 0.
7. Music cues (through the controller's `ClientSetMusic(sound, MTRAN_None)`, which a client skips when its profile has objective sounds off): switches 10, 27, 33, 36: `Interface.Objectifs.Jingl_AttacB`; 12, 34: `Interface.Objectifs.Jingl_DefA`; 23: `Interface.Objectifs.Jingl_DefB`; 5 ("You Lose"): the lose sound. Every objective taken also plays `Interface.Objectifs.Jingl_objectifTaken` for both teams (`missions.md` rule 26).

Who sends what (this area):

| Event | Switch | Recipients | Sender |
|---|---|---|---|
| Mission reset | 3 / 15 | spies / mercs | `SMission.Reset`, chained first child, `DoBroadcastLocalizedMessage` |
| Terminal hack started | 36 | all | `SComputerObjectiveTrigger.Triggering(true)` |
| Terminal hack interrupted | 23 | all | `s_Computer_Enter` damage, `s_Computer_FastLeave`, `s_Computer_Use` damage (every hit), quick grenade |
| Terminal taken | 37 | all | `SComputerObjectiveTrigger.TakeObjective` |
| Disk picked up by a spy | 10 / 53 / 54 | all / carrier / mercs | `SFlag.NotifyPickup` (54 for any carrier) |
| Disk dropped | 11 | all | `DiscardFlag` (only if the objective is not taken) |
| Disk returned by a merc touch | 12 | all | `SFlag.s_Initial.Touch` |
| Disk delivered | 22 | all | `SFlag.TakeObjective` |
| Idle carrier reminder | 53 | carrier (local) | `PlayerTick` |
| Panel hack started | 19 / 39 | mercs (if warn text) / all | `STriggerInteraction.Interact` |
| Bomb armed / secured / blown | 27 / 28 / 29 | all | `SBombTargetObjectiveTrigger` |
| Game object chaffed | 21 | mercs | `SBasicGamePlayObject.Malfunction` |

HUD lists fed by objective code:

8. `SHud.ActorToDraw` (array of actors, added at the front, removed by every match), changed through `AddRemoveActorOnHud(actor, add)`:
   - the SObjective, when one of its triggers starts triggering and `bShowStatusOnHud` (multicast `AddRemoveObjectiveOnClientHud(true)`; removal is native, see `missions.md` Open questions);
   - the panel's `STriggerInteraction`, on the hacker's own machine only, from the start of a panel hack until the interaction leaves `s_Hacking` (multicast `AddRemoveActorOnClientHud`). The HUD reads `MinTriggerTime` of these entries for the countdown.
   In GM_Neutralization the merc HUD clears this list every frame (other mode).
9. `SHud.ObjectivesList` (array of SObjectiveTrigger), through the natives `AddObjective` / `RemoveObjective`: on every merc's machine while a trigger other than a bomb target is triggering (multicast `AddRemoveObjectiveForRadar`). In the slice this is a terminal being hacked; the carried disk never enters it (its `bTriggering` is set without calling `Triggering`). Read by the merc radar and merc HUD markers.
10. Spy Enhanced Reality markers come from `SHud.ObjectivesAndTriggersList`, built natively (`BuildObjectivesAndTriggersList`) from the objectives and their triggers; colour by any trigger `bTriggering` (red) or `bLocked` (yellow); the terminal percent is `((DominationCaptureTime - CaptureTime) + DeltaCaptureTime) * 100 / DominationCaptureTime`, truncated toward zero (game 0x10ABDF80) and clamped 0..99 by the HUD (game 0x10AF8C00), the objective showing its highest trigger percent; the label alternates two seconds of distance and two of percent (game 0x10AF8A30). Only the server advances DeltaCaptureTime; clients show the replicated value (terminal-hack.md rules 31 to 35). Texts: `ObjectiveHackingSpy` "Under hacking", `ObjectiveSecuredSpy` "Access denied", `DropZone` "Drop zone".
11. `SHud.NbObjectives = GRI.NumObjectives` and `SHud.NbObjectivesLeft = GetObjectivesLeft()` every frame (`missions.md` rule 32).
12. `SFlag.FlagTaken` is set on every machine by the multicast `SetFlagCarrier` (a HUD or native reader; no script reader).

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| MessageMustStayTime | 4.0 | s | decoded | def:SBase.SHud |
| MaxStackedMessages | 2 | entries | decoded | def:SBase.SHud |
| Default stack time | 10 | s | decoded | SHud.LocalizedMessage |
| Switch 39 stack time | 4 | s | decoded | SHud.LocalizedMessage |
| Game message priority | 2 (reminder 3) | | decoded | SHud.LocalizedMessage, PlayerTick |
| Console message time | 6.0 | s | decoded | LocalMessage.ClientReceive |
| Multicast RPC flag | 0x01000000 | function flag | decoded | local/reflect/game.json |

## Network

- Per-player messages: reliable server-to-client `ReceiveLocalizedMessage` with (class, switch, PRI1, PRI2, object) arguments; the text is computed by the receiver.
- HUD list changes: reliable multicast functions (flag 0x01000000) on the objective, the trigger, the switch interaction and the disk. The 20 functions carrying this flag in the game are the evidence that it means "send to all clients"; OpenScct.Net needs a multicast RPC kind with the same reliability, executed on the server as well (listen host).
- Late joiners: multicast calls are not replayed; a client that connects during a hack will not have the marker until the next change (to be confirmed).

## Edge cases

- Message 23 can be sent while the hack continues (non-cancelling `SDTBase_NoAnim` damage), and the stack de-duplicates it, so it shows as one long-lived entry while the damage repeats.
- The stack holds only two entries; a third lower-or-equal priority message removes the oldest lowest-priority entry, so rapid objective messages push each other out.
- `SetGameMessage` ignores a text already queued, so the idle reminder repeating every tick does not flood the queue.
- An empty `sWarnHackingText` suppresses message 19; message 39 is still sent to everyone.

## Evidence

- `local/re-out/uc/SBase/SGameMessages.uc`, `SHud.uc` (LocalizedMessage, AddMessageToStack, SetGameMessage, AddRemoveActorOnHud, PostRender), `SGameInfo.uc` (BroadcastTeamLocalizedMessage, BroadcastMusic), `Engine/Actor.uc`, `Engine/GameInfo.uc`, `Engine/BroadcastHandler.uc`, `Engine/PlayerController.uc` (replication block, ReceiveLocalizedMessage), `Engine/LocalMessage.uc`.
- `local/formats/classdefaults_localized.json` (SGameMessages, SHud strings).
- Natives: SHud.AddObjective 1446 0x10A91C50, RemoveObjective 1447 0x10A91CF0, BuildObjectivesAndTriggersList 1808 0x10AF80B0; editor ASHud execAddObjective 0x111C31C0, execRemoveObjective 0x111C32C0, ERDrawObjectives 0x1123CF60.
- Bridge `docs/HUD.md` (ER marker data and percent formula), `docs/OVERLAYS.md` (radar inputs).

## Open questions

1. Who removes an SObjective from `ActorToDraw` after a hack stops (native `NotifyTriggering` or the HUD)? Trace on a client.
2. Whether multicast functions are delivered to clients for which the actor is not currently relevant, and whether they run on the server (needed for listen-host HUDs). Ghidra the remote-function path.
3. The merc HUD's drawing of `ObjectivesList` and `ActorToDraw` (HUD owner).
