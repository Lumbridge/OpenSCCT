# Text chat: typing, relaying and showing chat lines

Units:
- uc:SBase.SPlayerController:Talk, uc:SBase.SPlayerController:TeamTalk (open the chat line; natives 1902 and 1903)
- uc:Engine.PlayerController:Say, uc:Engine.PlayerController:TeamSay
- uc:SBase.SPlayerController:s_MultiSpectating.Say, uc:SBase.SPlayerController:s_MultiSpectating.TeamSay,
  uc:SBase.SPlayerController:CanTextChatWhileSpectating
- uc:Engine.BroadcastHandler:AllowsBroadcast, uc:Engine.BroadcastHandler:Broadcast, uc:Engine.BroadcastHandler:BroadcastTeam,
  uc:Engine.BroadcastHandler:BroadcastText, uc:Engine.BroadcastHandler:UpdateSentText
- uc:Engine.PlayerController:TeamMessage, uc:Engine.PlayerController:ClientMessage
- uc:Engine.HUD:Message, uc:Engine.HUD:AddTextMessage, uc:Engine.HUD:DisplayMessages
- rep:Engine.PlayerController.TeamSay, rep:Engine.PlayerController.TeamMessage, rep:Engine.PlayerController.ClientMessage

## Purpose

How a player writes a chat line, who receives it, and how it shows on the receiving HUD. The rewrite keeps the
original's rules and look; the line travels as one control channel message instead of the controller's
replicated `Say` / `TeamMessage` calls (see Network).

## Data

| Field | Owner | Type | Notes |
|---|---|---|---|
| TypedStr | Console | string | the line being typed, after the `Say ` or `TeamSay ` prefix |
| SentText | BroadcastHandler | int | characters broadcast since the last reset, all senders together |
| bMuteSpectators | BroadcastHandler | config bool | not set in `Default.ini`: off |
| TextMessages[8] | HUD | ConsoleMessage | Text, TextColor, MessageLife, PRI, MsgType; four are used |
| ConsoleMessageCount | HUD | int | 4 |
| VoiceChatMode | GRI | EVoiceChatMode | Classic forbids dead players' chat |

## Rules

Typing (client):

1. The `Talk` key (F5 in both stock layouts) opens the chat line with the `Say ` prefix, `TeamTalk` (F6) with
   `TeamSay `. Both call Reloaded natives that run the console's `Talk` / `TeamTalk`; nothing happens while the
   console is already typing.
2. While typing, every printable key (32 to 255, not `~` or `` ` ``) appends its character while the typed text is
   shorter than 100 characters; Backspace and Left delete the last character but never the prefix; Escape closes the
   line and drops the text; Enter closes it and, when something follows the prefix, runs it as the `Say` or
   `TeamSay` command. Game keys do not reach the player while the line is open.
3. The line is drawn across the bottom of the screen: `"> " + TypedStr + "_"` in white at alpha 128 over a dark
   band, the band's top and bottom edges in (69, 85, 69).

Relaying (server):

4. `Say(msg)`: a player whose PRI is admin and whose line starts with `#` sends an admin message instead (no admins
   in the rewrite yet); otherwise `GameInfo.Broadcast(self, msg, 'Say')`. `TeamSay(msg)`:
   `GameInfo.BroadcastTeam(self, msg, 'TeamSay')`.
5. While the sender's controller is in `s_MultiSpectating` (and its child states), `Say` and `TeamSay` do nothing
   unless the host is in dedicated mode or `CanTextChatWhileSpectating()`: the GRI exists, its `VoiceChatMode` is
   not `VCM_Classic`, and the controller is in `s_MultiSpectating_WhenKilled`. So a dead player may chat (except in
   Classic), a pure spectator never.
6. `AllowsBroadcast(sender, Len)`: with `bMuteSpectators` a pure spectator is refused; otherwise `SentText += Len`
   and the line passes when `SentText < 260` (or the game is paused). The length is counted even for a refused line.
   The game timer (once a second) resets `SentText` to 0. The budget is shared by every sender on the server.
7. `Broadcast`: every PlayerController in the controller list receives `TeamMessage(senderPRI, msg, 'Say')`.
   `BroadcastTeam`: every PlayerController whose `PRI.Team.TeamIndex` equals the sender's (a player without a team
   reads 0, as UnrealScript reads a property of None) receives `TeamMessage(senderPRI, msg, 'TeamSay')`. The sender
   is among the recipients.

Showing (client):

8. `ClientMessage(S, Type)` is `TeamMessage(ownPRI, S, Type)` with `Event` for a missing type. `TeamMessage` calls
   `myHUD.Message(PRI, S, Type)`.
9. `HUD.Message`: for `Say` and `TeamSay` the text becomes `PlayerName $ ": " $ Msg`; then `AddTextMessage`.
10. `AddTextMessage`: the first empty slot of the first four; when all four are used the oldest is dropped (the rest
    move up). The slot keeps the text, `MessageLife = TimeSeconds + max(trunc(Len * 0.09), Lifetime)` with the
    LocalMessage `Lifetime` of 5 s, the PRI and the type.
11. `DisplayMessages` (every HUD draw, after the HUD page): the first expired line is removed (the others move up);
    the live lines are drawn top down, each one text line below the previous, the last one ending one line above
    `0.61 x ClipY`, left edge at `0.15 x ClipX`. `Say` lines are drawn in (69, 85, 69), others in (69, 69, 85),
    both at alpha 200, in the menu font; the canvas doubles the colours (spec/hud/screen-effects.md rule 0), so they
    show as (138, 170, 138) and (138, 138, 170).

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Typed line length | 100 | characters | decoded | Engine.Console s_ChatTyping.KeyType |
| Broadcast budget | 260 | characters per second, all senders | decoded | BroadcastHandler.AllowsBroadcast, SGameInfo.Timer |
| Budget reset | 1.0 | s | decoded | SGameInfo `SetTimer(1.0, true)`, Timer calls UpdateSentText |
| Console lines | 4 | lines | decoded | HUD defaults ConsoleMessageCount, AddTextMessage |
| Line life | max(trunc(0.09 x length), 5) | s | decoded | HUD.AddTextMessage, LocalMessage Lifetime 5 |
| Line position | x 0.15 W, bottom one line above 0.61 H | | decoded | HUD.DisplayMessages |
| Say colour | (69, 85, 69, 200) | RGBA | decoded | HUD.DisplayMessages |
| Other colour | (69, 69, 85, 200) | RGBA | decoded | HUD.DisplayMessages |
| Typing line text | (255, 255, 255, 128) | RGBA | decoded | Console s_ChatTyping.PostRender |
| Typing line edges | (69, 85, 69, 255) | RGB | decoded | Console s_ChatTyping.PostRender |

## Network

- Original: `Say` and `TeamSay` are unreliable client-to-server functions of the controller; `TeamMessage` and
  `ClientMessage` reliable server-to-client functions (spec/net/rep-controller.md).
- Rewrite: `EControlMessage.Chat` on the control channel (reliable, in order): client to server carries the scope
  and the text; server to client the scope, the sender's player id, name and team, and the text. The server applies
  rules 4 to 7 (`OpenScct.Server.ChatRelay`) and the client rules 8 to 11 (`OpenScctGame.Hud.HudConsole`). This also works
  before a controller exists (lobby). A reliable send can only be more dependable than the original's unreliable
  `Say`.

## Edge cases

- The budget is shared: one player pasting long lines can block everyone for the rest of the second.
- A line pushed out of the four slots disappears at once, whatever its life.
- `DisplayMessages` removes at most one expired line per frame; at 60 fps the difference is invisible.
- The script's colours look dark (about a quarter of full brightness) until the canvas doubles them.

## Evidence

- `local/re-out/uc/SBase/SPlayerController.uc`: Talk, TeamTalk (natives 1902, 1903), s_MultiSpectating.Say/TeamSay,
  CanTextChatWhileSpectating; `Engine/PlayerController.uc`: Say, TeamSay, ClientMessage, TeamMessage, replication
  block; `Engine/BroadcastHandler.uc`; `Engine/GameInfo.uc` and `SBase/SGameInfo.uc` (Timer); `Engine/HUD.uc`
  (Message, AddTextMessage, DisplayMessages, defaults); `Engine/LocalMessage.uc` (Lifetime); `Engine/Console.uc`
  (Talk, TeamTalk, s_ChatTyping).
- spec/native/reloaded-hooks.md rule 26 (RaiseChatLine, RaiseTeamChatLine).
- Default_kbd_0.ini and Default_kbd_1.ini: `IK_F5=Talk`, `IK_F6=TeamTalk`.

## Open questions

1. Reloaded's chat commands (`!map`, `!next`, `!resign`...; rl:qol.chatcmds) are not handled yet.
2. Whether Reloaded changes the console line's look or the HUD line colours (compare one capture).
