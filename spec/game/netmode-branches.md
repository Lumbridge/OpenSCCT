# NetMode branches: Play Level, offline, listen server, non-playing host and client

Units:
- uc:SBase.SGameInfo:Kick
- uc:SBase.SGameInfo:KickBan
- uc:SBase.SGameInfo:AllPlayerReadyToPlayToServer
- uc:SBase.SPlayerController:IAmOnClient
- uc:SBase.SPlayerController:MirrorState
- uc:SBase.SPlayerController:ChangeState
- uc:SBase.SPlayerController:PCChangeClientState
- uc:SBase.SPlayerController:PCChangeServerState
- (cross-reference index for every NetMode test in the other spec files of this folder)

## Purpose
Game flow behaves differently depending on `Level.NetMode` (0 Standalone, 1 DedicatedServer,
2 ListenServer, 3 Client) and on how the map was started (editor Play Level, offline menus, network
lobby). This file lists every such branch in one place so the port can implement one code path with the
same switches, and so differential tests know which reference mode reproduces which behaviour.

## Data
Launch profiles (how each is recognised):

| Profile | NetMode | Recognised by | Typical URL |
|---|---|---|---|
| Editor Play Level | Standalone (0) | `Editeur=true` option | Autoplay.sdc?GameMode=5?Team=0?Editeur=true |
| Offline match from menus | Standalone (0) | NetType NET_NONE, loaded through the loading page | Map + options with TimeLimit 0, lives 0, TimesRespawn 5 |
| Listen server host (plays) | ListenServer (2) | host PRI.bIAmServer when launched with LAN=Server | Map + GetOptionsString + NBPlayers |
| Non-playing host ("dedicated" in the menus) | ListenServer (2) | `ServerOnly=true`, GRI.ServerOnly | same, host team 255 |
| Client | Client (3) | | join URL with Name, Team, Mesh, ... |
| True dedicated server | DedicatedServer (1) | not produced by the stock game | |

`IAmOnClient()` is `RemoteRole != ROLE_AutonomousProxy`: true for the locally controlled player on any
machine (the host's own controller has RemoteRole SimulatedProxy; a client's own copy has RemoteRole
Authority), false for a remote player's controller on the server.

## Rules
State mirroring (used by every flow transition):
1. `MirrorState(State, Label, bDontSave, Actor)`: GotoState locally; if this controller has a local viewport
   stop there; else (a remote player's controller on the server) send `PCChangeClientState` with an
   acknowledgement time (refreshed when more than 1.0 s old) and the current TimeSeconds.
2. `PCChangeClientState` (server to client, reliable): apply only if its server time is not older than the
   last applied one; skip one sticky camera case; GotoState.
3. `ChangeState` (simulated): GotoState locally; on a Client, also send `PCChangeServerState` with the last
   acknowledgement time; Standalone and ListenServer send nothing.
4. `PCChangeServerState` (client to server, reliable): applied only if the acknowledgement time matches
   within 0.01 s (otherwise the server has moved on and the client request is dropped); a few
   knock-down states also run gas-KO bookkeeping.

Branch table (rule, where specified, behaviour per profile):

| # | Topic | Play Level | Offline menus | Listen host / non-playing host | Client |
|---|---|---|---|---|---|
| 5 | Match start trigger | PostLogin calls StartMatch (bDelayedStart false via Editeur) | Loading_Menu s_StandAlone after 2 s | Loading_Menu s_Server: all PRIs bWaitingPlayer, +1 s, StartMatchOnServer | waits for replicated bMatchHasBegun |
| 6 | GRI.bMatchHasBegun | stays false (GUI bInGame false) | true at the end of StartMatch | true before StartMatch | replicated |
| 7 | Round end (sCheckEndGame) | never (rule 3 of end-of-round.md) | Standalone branch: both sides out of lives, or objectives | network branch: one side out of lives, or objectives, or time | none |
| 8 | Team death test | n/a | needs both counts 0; no objective actor means never | needs one side 0 with players on that side | |
| 9 | Time limit | inert (TimeLimit 0 and bMatchHasBegun false) | inert (TimeLimit forced 0) | active | |
| 10 | Lives | 999999 (no option) | 999999 (forced 0) | from URL (SteeE menus: 4 spy, 3 merc) | |
| 11 | Respawn delay | 15 s (default) | 5 s (forced) | from URL (SteeE: 10 s) | |
| 12 | Spawn Priority pass | every spawn and respawn | initial spawns only | never | |
| 13 | FindPlayerStart StartSpot reuse | possible (Standalone with bWaitingToStartMatch or bWaitingPlayer) | possible | never | |
| 14 | ChangeTeam team size cap | ignored | ignored | enforced (MaxTeamSize) | |
| 15 | PickTeam auto-balance | never (balance only on the Lobby map, and offline only for non-player controllers) | never | only on the Lobby map, with the host's auto-balance setting | |
| 16 | AtCapacity | never full | never full | MaxPlayers / MaxSpectators | |
| 17 | Late join | n/a (bMatchHasBegun false: joins as player) | n/a | spectator in every mode except Merc DM | |
| 18 | Welcome message (switch 1) | not sent | not sent | sent for new spectators (not the ServerOnly host) | |
| 19 | Logout end check and leave message | none | none | yes (not on the Lobby map) | |
| 20 | Voice chatter cleanup on logout | no | no | yes | client leaves voice chat |
| 21 | SpawnMagmaHud at StartMatch | yes | yes | no (HUD comes from ClientSetHUD) | |
| 22 | s_Dead timer period | 0.2 s | 0.2 s | 0.2 s on the server | 1.0 s, plus AntiBugTellServerIAmDead |
| 23 | End screen exit | loading "VisitMapReturn" (entry map) | same, or tutorial chain | Ending_Menu, then lobby or next game | Ending_Menu |
| 24 | ProcessServerTravel | NextSwitchCountdown 0 (immediate) | same | ClientTravel to each client, host team in NextURL | receives ClientTravel |
| 25 | SwitchLevel | allowed | allowed | allowed | ignored |
| 26 | Kick / KickBan | no | no | ListenServer host only, outside in-game GUI | |
| 27 | GameStats spawn | no | no | no (only a true dedicated server with logging) | |
| 28 | ServerReStartPlayer in waiting states | runs | runs | runs on server | returns at once |
| 29 | GRI ServerName, AdminName, AdminEmail | kept | kept | kept | blanked at PostBeginPlay, then replicated |
| 30 | RestartPlayer bRestartLevel guard | applies only off server (flag is false anyway) | same | never | |

Other details:
31. `Kick(PRI)` and `KickBan(PRI)`: do nothing while the host's GUI says in-game; only on ListenServer and
    not for the host itself; on the game service use its kick, else AccessControl.Kick / KickBan.
32. `AllPlayerReadyToPlayToServer(b)`: sends `PlayerReadyToPlayToServer(b)` to every SPlayerController except
    the local one (lobby helper).
33. Non-playing host: Login places its controller far away (100000, 100000, 100000) on team 255 as a
    spectator; Loading_Menu and the end menus skip PRI index 0 (NumFirstPlayer 1); the welcome message
    skips it; the Dedicated_Server GUI page replaces the HUD.

## States
See the state diagrams in match-lifecycle.md and end-of-round.md.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| MirrorState acknowledgement refresh | 1.0 | s | decoded | MirrorState |
| Server state request tolerance | 0.01 | s | decoded | PCChangeServerState |

## Network
This file is the network-role summary for the folder: everything in SGameInfo is server only; Loading_Menu,
Ending_Menu and GameEnded run per machine; state changes cross the wire only through MirrorState
(server to client) and ChangeState (client to server).

## Edge cases
- Verification consequence: the editor's Play Level, the main scripted reference run, cannot exercise round
  end, time limit, lives running out, the 10 s respawn delay or distance-only spawn selection. Those need a
  listen-server reference run (two game instances or the RE+ slotted sandboxes with LAN), or a Play Level
  URL with explicit options (still without round end).
- In Play Level the merc in SteeE always spawns at the Priority 2 start; a network match never uses it by
  the script (spawn-selection.md open question).
- A remote player's controller on the server is not "on client" (IAmOnClient false), so client-only
  visuals in BeginState blocks do not run there, but some unguarded cosmetic calls (end screen) do.

## Evidence
- local/re-out/uc/SBase/SPlayerController.uc: MirrorState (8619ddcddb5c), PCChangeClientState
  (6acb8bdc3eb9), ChangeState (4ccd2cbe61e3), PCChangeServerState (cc31e8934220), IAmOnClient (af1606566197)
- local/re-out/uc/SBase/SGameInfo.uc: Kick (deae3f374f47), KickBan (4a5afb66dd11),
  AllPlayerReadyToPlayToServer (460acb2489c4); every NetMode test listed in the table
- local/re-out/uc/Engine/GameInfo.uc: AtCapacity, Logout, InitLogging, ProcessServerTravel
- local/re-out/uc/GUI/Loading_Menu.uc, GUI/loading.uc, GUI/Ending_Menu.uc
- RE+ docs/MapBuilderFacts.md line 176; memory notes objective-player-counts.md, scct-test-harness.md
- UE2 spawn roles for local and remote players (SpawnPlayActor with SimulatedProxy or AutonomousProxy):
  engine knowledge, to confirm (Open questions)

## Open questions
- Confirm the RemoteRole given to the listen host's own controller (SimulatedProxy expected), since
  IAmOnClient depends on it. Read SPlayerController.RemoteRole live on a listen host.
- Whether Reloaded adds a true dedicated server mode (NetMode 1) in this build and which of the branches
  above it takes (most SGameInfo tests treat Dedicated like Listen).
- Whether the RE+ test tools can run a two-instance LAN listen match headless for the differential tests
  of rules 7 to 12.
