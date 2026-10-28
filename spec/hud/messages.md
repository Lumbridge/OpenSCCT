# HUD messages: localized messages, the four message areas and the kill feed

Units:
- uc:SBase.SHud:LocalizedMessage (all message classes), uc:SBase.SHud:AddMessageToStack,
  uc:SBase.SHud:AddInfoMessageToStack, uc:SBase.SHud:AddMessageToAuxiliaryStack, uc:SBase.SHud:AddReticuleMessage,
  uc:SBase.SHud:AddHudNumberMessageToStack, uc:SBase.SHud:SetGameMessage
- uc:SBase.SGameMessages:GetString (every switch), uc:SBase.SDeathMessage:GetString, uc:Engine.GameMessage:GetString
- the HUD page's message driver (native, game 0x10b32c30)
- uc:SBase.SPlayerController:PlayerTick (the mission reminder at its end)

## Purpose

How a message the server or the local machine raises becomes text in one of the HUD's message areas, including the
death messages (the original's kill feed). spec/objectives/messages.md fixes which objective events send which
switch; this spec covers every class and the display.

## Data

| Stack (SHud) | Area | Most entries | Notes |
|---|---|---|---|
| MessageStack | SystemMessage | MaxStackedMessages (2) | objective and alarm lines |
| AuxiliaryMessageStack | StateMessage | 1 | gadget, goggle and mode lines |
| InfoMessageStack | ZoneMessage | 1 | message triggers (SMessageTrigger) |
| GameMessageQueue | GameMessage | no limit | one shown at a time |
| ReticuleMessage | ReticuleMessage (merc) | 1 | weapon lines, follows the aim point |

An entry is `{Message, StartedTime, StayInStackTime (int s), prio (byte)}`.

## Rules

Texts (built on the receiving machine; `@` joins with one space):

1. `SGameMessages` switches: 1 and 40 alarm text by the alarm's MessagePriority (0, 1: AlarmDescription; 2, 3:
   `AlarmDescription @ ":" @ "Intruder detected"`; 4, 5: the same with "Intruder detected - Area isolated" for 1, the
   short one for 40; no alarm: "Alarm active"), 2 "Game restarting", 4 "You Win", 5 "You Lose", 7 the score line,
   13 "Spy device activated - Target acquired", 14 "Spy device desactivated", 16 `PRI1 has neutralized PRI2`,
   17 `PRI2 has been knocked down by PRI1 + "strike attack"`, 18 `PRI1 has broken PRI2's neck`, 20 `PRI2 has gone
   unconscious`, 24 the alarm's spy description, 25 "Unlocking access in 5 seconds", 26 "Access unlocked", 41 "No
   Sticky Cam Available", 50 `PRI2 choke to death`, 51 "Pres. detector activated", 52 "Spy trap activated", 55 "Get a
   new modem before going to the ND133", 56 `PRI1 used his last life`, 57 and 58 the counter-grab results, 60
   "Target acquired", 61 "Target Lost", 253 and 254 the intruder lines, 255 a single space; the objective switches
   as in spec/objectives/messages.md. Texts come from the localized class defaults.
2. `SDeathMessage`: names default to "Someone"; switch 18 `Victim @ DeathString[17]`, 19 the neck-break line, 23
   `Killer @ "sniped" @ Victim`, 24 `Victim @ ResignedString`; a suicide (both PRIs the same, not None)
   `Victim @ SuicideString`; switches below 18 `Killer @ DeathString[Switch] @ Victim`, others with DeathString[5].
3. `Engine.GameMessage`: 1 `Name @ "entered the game."` (or "A new player entered the game." without a name),
   4 `Name @ "has left the game."` (nothing without a name).

Routing (`SHud.LocalizedMessage(class, switch, PRI1, PRI2, object, critical string)`; a critical string replaces the
built text):

4. SGameMessages: switches 0, 2 to 7, 15, 16, 17, 20, 53, 54 and 55 go to `SetGameMessage(text, 2)`. Every other
   switch goes to `AddMessageToStack(text, time, prio)`: for an alarm object, time = its Duration when on (5 s when
   the Duration is 500 or more), 1 s when off, prio = its MessagePriority, and the alarm list blinks for
   AlarmListBlinkingTime (3 s) from now unless it already does; switch 39 uses 4 s, everything else 10 s, prio 0.
   Switch 23 also pushes a one-second blank. Jingles and music cues (alarms, 2, 5, 6, 7, 10, 12, 23, 27, 33, 34, 36)
   are the audio layer's.
5. SDeathMessage: `SetGameMessage(text, 2)` when the victim (PRI2) is on the local player's team and is not the
   local player (a merc killing a spy also plays the defence jingle); otherwise, like every other class,
   `SetGameMessage(text, 1)`.

Stacks:

6. `AddMessageToStack(text, time, prio)`: an empty text is ignored; a text already there keeps its start and gets
   `StayInStackTime = trunc(now + max(time, MessageMustStayTime) - StartedTime)`; otherwise it is appended with
   stay `max(time, MessageMustStayTime)` (4 s), and when the stack holds more than MaxStackedMessages (2) the first
   entry of the lowest priority is removed. `AddInfoMessageToStack` is the same with a limit of one entry.
   `AddMessageToAuxiliaryStack(text, time, prio)`: a text already there restarts when it had expired and gets
   `StayInStackTime = trunc(time + 1)`; a new text is appended with its own time (no 4 s floor), limit one entry.
   `AddReticuleMessage(text, time, prio)`: empty clears; the same text gets the new time; a different text replaces
   the current one unless the current one has a higher priority. `AddHudNumberMessageToStack(n, time, prio)`
   stacks one of the HUD's own texts: 1 "You've been detected.", 2 the same outside Story mode or "You've been
   detected - Area isolated." in it, 3 the opposite-wall mine text, 4 "Spy trap activated", 5 and 6 the alarm snare
   texts, 7 "System malfunction", 8 "Rebooting ...".
7. `SetGameMessage(text, prio)`: a text already queued is not added. The queue stays in ascending priority: empty
   queue, or prio at least the last entry's: append; below the first entry's: insert at the front; otherwise the
   new entry goes just after the first entry with a higher priority (the search loop steps once more after it
   finds it).

The page's message driver (every HUD draw, both pages):

8. SystemMessage, StateMessage and ZoneMessage: entries with `StartedTime + StayInStackTime < now` are removed; an
   empty stack hides its area; otherwise the area's text is every entry after a leading line break, one per line
   (`"%s\n%s"` folded from an empty string), and a hidden area is shown from frame 0 and played. A visible area is
   not restarted when its text changes. ZoneMessage is not driven in GM_DeathMatch.
9. GameMessage: while the tutorial message area is hidden and the queue is not empty: with no end time, the head
   starts now and ends GameMessageDisplayTime (5 s) later; past its end the head is removed and the next one
   starts (or the end time goes back to 0). The area shows the head's text alone (shown from frame 0 when it was
   hidden), and hides when the queue is empty.

### Mission reminder

The last part of the controller's global `PlayerTick`, on the player's own machine (after the camera update; states
that do not call the global `PlayerTick`, such as `s_Dead` and `s_WaitRespawn`, skip it):

10. While the pawn's velocity is not zero, `fLastTimeMoved = now`.
11. Otherwise, once `now - fLastTimeMoved > fTimeMissionMessage` (20 s): a spy carrying the Story disk gets
    SGameMessages 53 ("go to the drop zone"); else a spy in Sabotage whose modem (`FindGadgetType(SGadBomb)`) has
    `Charge == 0` (no modem reads 0) gets 55; neither resets the wait, so the line is queued again whenever it is no
    longer in the queue. Else every `SMission` not taken queues its `Description` (spies) or `DescriptionDEF`
    (mercs) with `SetGameMessage(text, 3)` and resets the wait.


| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| MessageMustStayTime | 4.0 | s | decoded | def:SBase.SHud |
| MaxStackedMessages | 2 | entries | decoded | def:SBase.SHud |
| GameMessageDisplayTime | 5 | s | decoded | def:SBase.SHud; driver 0x10b32c30 |
| AlarmListBlinkingTime | 3 | s | decoded | def:SBase.SHud |
| fTimeMissionMessage | 20 | s | decoded | def:SBase.SPlayerController |
| Mission reminder priority | 3 | | decoded | SPlayerController.PlayerTick |
| Alarm long time | 5 | s | decoded | SHud.LocalizedMessage (Duration 500 or more) |
| Death message priority | 2 own team, 1 other | | decoded | SHud.LocalizedMessage |
| Other classes' priority | 1 | | decoded | SHud.LocalizedMessage |
| Stack line format | `"%s\n%s"` from "" | | decoded | game 0x10c37334, 0x10c74e4c |

## Network

`ReceiveLocalizedMessage(class, switch, PRI1, PRI2, object)` is a reliable server-to-client function of the
controller; the texts are built on the receiver (`GameInfo.BroadcastLocalized`, `BroadcastDeathMessage`). The
stacks and the queue exist only on the player's own machine.

## Edge cases

- Because the queue is sorted ascending, a priority 1 line (another team's death) shows before queued priority 2
  lines; with three or more priorities the "one step further" insert can put a line after a higher one.
- `AddInfoMessageToStack` inserts at the main stack's length (a script slip); when the main stack is longer, the
  insert is out of range in the original. The rewrite appends.
- A message the stack de-duplicates keeps its start, so its area does not restart.

## Evidence

- `local/re-out/uc/SBase/SHud.uc` (LocalizedMessage, the Add* functions, SetGameMessage, defaults),
  `SBase/SGameMessages.uc`, `SBase/SDeathMessage.uc`, `Engine/GameMessage.uc`, `Engine/LocalMessage.uc`.
- Native message driver, own decode (local/ghidra-work/hud): game 0x10b32c30; area handles from the shared page init
  0x10b318e0 (A_MessageSystem, A_MessageState, A_MessageGame, A_TutoMessage, A_ZoneMessage); format strings at
  0x10c37334, 0x10c3734c.
- Localized texts: the generated class defaults (System/_PC_/*.int through the code generator).

## Open questions

1. Whether Reloaded changes the death message routing (its `!resign` message is the switch 24 text).
