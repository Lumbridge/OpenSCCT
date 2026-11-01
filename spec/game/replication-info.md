# GameReplicationInfo, PlayerReplicationInfo and TeamInfo

Units:
- uc:Engine.GameReplicationInfo:PostBeginPlay
- uc:Engine.GameReplicationInfo:PostNetBeginPlay
- uc:Engine.GameReplicationInfo:TeamSymbolNotify
- uc:Engine.GameReplicationInfo:Reset
- uc:Engine.GameReplicationInfo:Timer
- uc:Engine.GameReplicationInfo:PreciseElapsedTime
- uc:Engine.GameReplicationInfo:AddPRI
- uc:Engine.GameReplicationInfo:RemovePRI
- uc:Engine.GameReplicationInfo:GetPRIArray
- uc:Engine.GameReplicationInfo:SetBridage
- uc:Engine.GameReplicationInfo:logBridage
- rep:Engine.GameReplicationInfo.TimeLimit
- rep:Engine.GameReplicationInfo.ElapsedTime
- rep:Engine.GameReplicationInfo.RemainingTime
- rep:Engine.GameReplicationInfo.TimesRespawn
- rep:Engine.GameReplicationInfo.TeamLivesATT
- rep:Engine.GameReplicationInfo.TeamLivesDEF
- rep:Engine.GameReplicationInfo.TotalTeamLives
- rep:Engine.GameReplicationInfo.WinningTeam
- rep:Engine.GameReplicationInfo.WinningReason
- rep:Engine.GameReplicationInfo.bMatchHasBegun
- rep:Engine.GameReplicationInfo.bMatchLaunching
- rep:Engine.GameReplicationInfo.bMatchLaunchingNow
- rep:Engine.GameReplicationInfo.bStopCountDown
- rep:Engine.GameReplicationInfo.GameMode
- rep:Engine.GameReplicationInfo.Teams
- rep:Engine.GameReplicationInfo.NumObjectives
- rep:Engine.GameReplicationInfo.GamesInARow
- rep:Engine.GameReplicationInfo.Revenge
- rep:Engine.GameReplicationInfo.ServerOnly
- def:Engine.GameReplicationInfo
- uc:Engine.PlayerReplicationInfo:PostBeginPlay
- uc:Engine.PlayerReplicationInfo:PostNetBeginPlay
- uc:Engine.PlayerReplicationInfo:Destroyed
- uc:Engine.PlayerReplicationInfo:Reset
- uc:Engine.PlayerReplicationInfo:SetFlag
- uc:Engine.PlayerReplicationInfo:Timer
- uc:Engine.PlayerReplicationInfo:UpdatePlayerLocation
- uc:Engine.PlayerReplicationInfo:SetPlayerName
- uc:Engine.PlayerReplicationInfo:SetWaitingPlayer
- uc:Engine.PlayerReplicationInfo:playerQuitMatch
- rep:Engine.PlayerReplicationInfo.NumLives
- rep:Engine.PlayerReplicationInfo.Score
- rep:Engine.PlayerReplicationInfo.Kills
- rep:Engine.PlayerReplicationInfo.Deaths
- rep:Engine.PlayerReplicationInfo.Team
- rep:Engine.PlayerReplicationInfo.bOnlySpectator
- rep:Engine.PlayerReplicationInfo.bIsSpectator
- rep:Engine.PlayerReplicationInfo.bWaitingPlayer
- rep:Engine.PlayerReplicationInfo.bReadyToPlay
- rep:Engine.PlayerReplicationInfo.bReadyToPlayAgain
- rep:Engine.PlayerReplicationInfo.Ping
- rep:Engine.PlayerReplicationInfo.StartTime
- def:Engine.PlayerReplicationInfo
- rep:Engine.TeamInfo.Size
- rep:Engine.TeamInfo.Score
- rep:Engine.TeamInfo.TeamIndex
- rep:SBase.STeamInfo.iNbObjectivesTaken
- rep:SBase.STeamInfo.iNBGadgets
- rep:SBase.STeamInfo.bDisallowedInv
- rep:Engine.LevelInfo.NbPointsAttack
- rep:Engine.LevelInfo.NbPointsDefense

## Purpose
The replicated objects that carry the match state to every client: the match clock and rules (GRI), each
player's lives, score and status (PRI), and the two teams (TeamInfo). Defines the clock arithmetic exactly,
since the time limit and the end menu's match time depend on it.

## Data
GameReplicationInfo (one per level, spawned by the GameInfo; `RemoteRole = SimulatedProxy`,
`bAlwaysRelevant`, `NetUpdateFrequency 10`, `bNetNotify`, `nativereplication`). Match fields:

| Field | Type | Default | Written by |
|---|---|---|---|
| TimeLimit | int, minutes | 0 | InitGameReplicationInfo, PostLogin |
| ElapsedTime | int, seconds | 0 | GRI.Timer, GRI.Reset |
| RemainingTime | int, seconds | 0 | GRI.Timer |
| RemainingMinute | int (not replicated) | 0 | Reset (holds TimeLimit * 60 until the first tick) |
| SecondCount | float (not replicated) | | PostBeginPlay, Reset, Timer |
| bStopCountDown | bool | true | Reset (false), sEndGame (true) |
| bMatchLaunchingNow, bMatchLaunching, bMatchHasBegun | bool | false | lifecycle (match-lifecycle.md) |
| WinningTeam, WinningReason | byte | 255, 255 | StartMatch (255), ReplicateWinningReason |
| TimesRespawn | int, seconds | 0 | InitGameReplicationInfo, PostLogin, StartMatch |
| TeamLivesATT, TeamLivesDEF | int | 0 | per-player lives setting (999999 = unlimited) |
| TotalTeamLives[2] | int | 0 | UpdateTeamNumLives |
| GameMode | EGameMode | 0 | |
| Teams[2] | TeamInfo | None | SGameInfo.PostBeginPlay |
| PRIArray | array of PRI | empty | AddPRI, RemovePRI (not replicated; each machine builds its own) |
| NumObjectives, NumDogTagsTaken, LastTimeObjectif, NumDisks, NumDisksToWin | int | 0 | objectives |
| GamesInARow, Revenge, ServerOnly, bRandomMap, GameWithRanking, iScoresInARow[2], iGamesInARow, sessionId, NbPublicSlots | | | settings, mostly display |
| bAvlb_* (13 ability flags), bAvlb_CinematicCamera, VoiceChatMode, voiceChatQuality, allowVoiceChat, allowVoiceChatInLobby | | | PostLogin from host persistent data |
| ServerName, ShortName, AdminName, AdminEmail, ServerRegion | string/int, config | "Another Server", "Server" | |
| m_ActualMapName, GameName, bTeamGame, bAlternateMode, bTutorMode | | | |

PlayerReplicationInfo (one per controller; `NetUpdateFrequency 5`, `nativereplication`): Team, PlayerName,
PlayerID, TeamID, NumLives, Score (float), Kills, Deaths, iNbObjectivesTaken, MyMeshNum, bOnlySpectator,
bIsSpectator, bWaitingPlayer, bReadyToPlay, bReadyToPlayAgain, bOutOfLives (never set by script), bWelcomed,
bIAmServer, bIAmClient, bQuittedMatch (not replicated), bResigned (not replicated), Ping, StartTime,
PlayerZone, PlayerVolume, minimap fields, voice and online fields, Stats (server only object).

TeamInfo (`nativereplication`; STeamInfo adds RemoteRole DumbProxy, bOnlyDirtyReplication false): TeamIndex,
TeamName, TeamColor, AltTeamColor, TeamIcon (initial only), Size, Score, HomeBase (when dirty);
STeamInfo adds iNbObjectivesTaken, iNBGadgets, bDisallowedInv (always).

## Rules
GRI clock (server only: `Timer` and `Reset` are not simulated):
1. PostBeginPlay (both ends): on a client blank ServerName, AdminName, AdminEmail; `SecondCount =
   TimeSeconds`; start a 1.0 s looping timer.
2. `Reset()` (from StartMatch): `SecondCount = TimeSeconds`, `ElapsedTime = 0`, `RemainingMinute =
   TimeLimit * 60`, `bStopCountDown = false`, restart the 1.0 s timer.
3. `Timer()` (server): if `TimeSeconds - SecondCount >= Level.TimeDilation`: `ElapsedTime += 1`; if
   `RemainingMinute != 0` then `RemainingTime = RemainingMinute` and `RemainingMinute = 0`; if not
   bStopCountDown, `RemainingTime -= 1`; `SecondCount += TimeDilation`. At most one second is counted per
   timer call, so a long frame does not catch up.
4. Consequences: after the first counted second, `RemainingTime = TimeLimit * 60 - 1`; generally
   `RemainingTime = TimeLimit * 60 - ElapsedTime` until the end. With TimeLimit 0, RemainingTime keeps its
   previous value and goes negative. After sEndGame, RemainingTime freezes and ElapsedTime keeps counting.
   Before StartMatch, ElapsedTime counts from level start and RemainingTime does not move (bStopCountDown
   true by default). The end menu shows `TimeLimit * 60 - RemainingTime` as the match time.
5. `PreciseElapsedTime() = ElapsedTime + (TimeSeconds - SecondCount) / TimeDilation` (server only).
6. AddPRI appends; RemovePRI removes the first equal entry (logs an error when missing); GetPRIArray copies
   the non-None entries. GRI.PostNetBeginPlay adds every existing PRI (a client receiving the GRI after
   some PRIs); on a client it also calls TeamSymbolNotify, which, once both teams and their icons exist,
   sets bTeamSymbolsUpdated and calls SetGRI on every actor.
7. `SetBridage(Id)` toggles ability flag Id (0 spy thermal, 1 spy night, 2 spy grab, 3 spy elbow, 4 spy
   lethal drop, 5 merc elbow, 6 merc lethal drop, 7 merc EMF, 8 merc motion tracking, 9 merc torch,
   10 merc laser, 11 merc berserk, 12 merc charge). logBridage only logs.

PRI:
8. PostBeginPlay (server only): create the Stats object, `StartTime = GRI.ElapsedTime`, call Timer once,
   then loop the timer every `1.5 + FRand()` seconds (re-drawn each tick).
9. Timer (server): update PlayerZone and PlayerVolume from the pawn (highest LocationPriority volume with a
   LocationName that encompasses the pawn; None without a pawn); re-arm `1.5 + FRand()`; with probability
   0.65 stop; otherwise, unless bReceivedPing, `Ping = Controller.GetPing()`. Uses the shared random stream.
10. PostNetBeginPlay (both ends): add itself to the first GRI found.
11. Destroyed (both ends): off LAN, on a client or listen server, notify the local PlayerController
    (ReplicationInfoDestroyed); remove itself from every GRI's PRIArray.
12. `Reset()` would clear Score, Deaths, flag, bReadyToPlay, NumLives (0) and bOutOfLives; nothing in this
    game calls it.
13. `SetWaitingPlayer(b)` sets bIsSpectator and bWaitingPlayer; `playerQuitMatch()` (client to server)
    sets bQuittedMatch; `SetPlayerName` keeps OldName.

## States
None.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| GRI timer | 1.0 | s | decoded | GRI.PostBeginPlay, Reset |
| GRI NetUpdateFrequency | 10 | Hz | decoded | def:Engine.GameReplicationInfo |
| PRI NetUpdateFrequency | 5 | Hz | decoded | def:Engine.PlayerReplicationInfo |
| PRI timer | 1.5 + FRand() | s | decoded | PRI.Timer |
| Ping refresh chance | 0.35 per PRI timer | | decoded | PRI.Timer |
| WinningTeam, WinningReason none | 255 | | decoded | def:Engine.GameReplicationInfo |

## Network
The script replication blocks (the classes are `nativereplication`, so native code builds the real list;
the script conditions below are the intended contract):
- GRI, reliable when `Role == Authority`: every match field listed above, the ability flags, ShortName,
  ServerName, Teams, voice settings, m_ActualMapName. Only on the initial bunch: ServerRegion, AdminEmail,
  AdminName, GameName.
- PRI, reliable when `bNetDirty` and Authority: status flags, scores, lives, Team, names, mesh, online ids.
  Minimap fields always. `Ping` only to non-owners when dirty. `StartTime` initial only. Client to server
  RPCs: setVisibleFromDedicatedServer, UpdateMinimapParams, playerQuitMatch.
- TeamInfo: HomeBase, Score, Size when dirty; TeamIcon, colours, TeamIndex, TeamName initial only.
  STeamInfo: iNbObjectivesTaken, iNBGadgets, bDisallowedInv when Authority.
- LevelInfo: NbPointsDefense, NbPointsAttack, DefaultGravity, Pauser, TimeDilation when dirty;
  GEActivationMask always.
- All writes happen on the server; clients only read, except the client-side `launchMatchNow` and GUI code
  that set local copies of bMatchLaunching flags (overwritten by the next replication).

OpenSCCT client clock reconciliation: `SecondCount` is a local world-time anchor for the replicated integer
`ElapsedTime`. An increasing elapsed sample preserves any fractional match time the client has already
extrapolated, so a delayed or retransmitted sample cannot make a running bomb countdown increase. A backwards
elapsed reset or a new `bMatchHasBegun` transition rebases the anchor, including a match starting at elapsed zero.
Unrelated replication leaves the anchor unchanged. Objective deadlines and the HUD use this fractional match
clock; positive time remaining is rounded up for display.

## Edge cases
- The clock depends on the order of the GRI timer relative to the frame in which Reset ran: if the GRI
  timer counter advances in the same frame as Reset but TimeSeconds does not, the first tick may fail the
  `>= TimeDilation` test and ElapsedTime then lags by one second for the whole match. See Open questions.
- With `TimeDilation != 1` the GRI counts one ElapsedTime per `TimeDilation` game seconds, which is one per
  real second.
- PRIArray order differs between server and clients (arrival order), so index-based UI (NumFirstPlayer = 1
  for a ServerOnly host) assumes the host's PRI is first on every machine.

## Evidence
- local/re-out/uc/Engine/GameReplicationInfo.uc (Timer 9cac26ab3858, Reset f18892169fd1, PostBeginPlay
  0d4201fd6a33)
- local/re-out/uc/Engine/PlayerReplicationInfo.uc (PostBeginPlay cdcf48183c77, Timer cf102750fd10)
- local/re-out/uc/Engine/TeamInfo.uc, SBase/STeamInfo.uc, Engine/LevelInfo.uc replication blocks
- local/reflect/game.json: GRI property offsets (ElapsedTime 792, RemainingTime 788, TimeLimit 804,
  TimesRespawn 816, TotalTeamLives 808, TeamLivesATT 820, TeamLivesDEF 824, WinningTeam 956,
  WinningReason 957, bool block 784 holds bMatchHasBegun and the other match bools); PRI NumLives 1004,
  Deaths 988, Score 984, Kills 1044, Team 968, bool block 1096, bResigned 1104, Stats 1108
- local/formats/classdefaults_effective.json Engine.GameReplicationInfo, Engine.PlayerReplicationInfo

## Open questions
- The native replication lists of GRI, PRI and TeamInfo (GetOptimizedRepList): which fields really
  replicate and under which conditions. Needs Ghidra on the three classes' native replication functions.
- Clock phase (Edge cases): trace ElapsedTime and TimeSeconds every frame for the first 3 s after
  StartMatch in a listen match.
- Whether bool fields at offset 784 replicate as one bitfield update or separately (affects ordering of
  bMatchLaunching versus bMatchHasBegun on clients, which Loading_Menu.s_Client relies on).
