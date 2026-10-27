# Game flow specs: index

Folder: `spec/game/`. Area: match setup and start, teams, lives, death, respawn, spawn selection,
spectating, end of round, scoring, round travel, the replicated match objects, and the NetMode branches.

## Spec files

| File | Units covered (summary; the file's Units list is authoritative) |
|---|---|
| match-options.md | uc:Engine.GameInfo:{GrabOption, GetKeyValue, ParseOption, GetIntOption}; uc:SBase.SGameInfo:InitGame; uc:SBase.SMatchInfo:{SetOptionString, GetOptionsString, GrabOption, GetKeyValue, ParseOption, GetIntOption, MaxKillLimit, DefaultKillLimit, RespawnFloor, IsDeathMatch, RulesModeFor}; def:SBase.SMatchInfo; uc:SBase.SMapSettings:{SearchForGameMode, PostBeginPlay}; nat:SBase.SMapSettings.IsAmateurMap; def:SBase.SMapSettings; uc:GUI.Create_Match:{SetModeSettings, ApplyMatchRules} (contract); uc:GUI.loading:Timer (contract) |
| match-lifecycle.md | uc:Engine.GameInfo:{PreBeginPlay, PostBeginPlay, InitGameReplicationInfo, SetGameSpeed, InitLogging, StartMatch, PostLogin, SetPause, Timer}; uc:SBase.SGameInfo:{PreBeginPlay, PostBeginPlay, InitGameReplicationInfo, PostLogin, StartMatch, Timer, CreateSLANServerLink}; uc:SBase.SPlayerController:{StartMatchOnServer, launchMatchNow}; uc:GUI.Loading_Menu:{s_Init@code, s_Server@code, s_Client@code, s_StandAlone@code, InternalOnInfoEvent}; uc:GUI.loading:InternalOnInfoEvent; nat:SBase.SPlayerController.NbPendingClientOnServer; def:SBase.SGameInfo; def:Engine.GameInfo |
| login-and-teams.md | uc:SBase.SGameInfo:{Login, PickTeam, ChangeTeam, MoveEveryoneToMercs, StartLateJoiner, Logout}; uc:Engine.GameInfo:{Logout, PreLogin, AtCapacity, ChangeName, GetNumPlayers}; uc:Engine.TeamInfo:{AddToTeam, RemoveFromTeam}; uc:Engine.Controller:SetPawnClass; def:Engine.TeamInfo; def:SBase.STeamInfo; def:SBase.STeamAttaquant; def:SBase.STeamDefenseur; uc:SBase.SPlayerController:{PlayerWaitingToPlayToServer, PlayerOnlySpectatorToServer, PlayerReadyToPlayToServer, s_PlayerWaiting.ServerReStartPlayer, s_PlayerWaiting@code, PlayerWaiting.BeginState, PlayerWaiting.EndState, PlayerWaiting.ChangeTeam, PlayerWaiting.ServerReStartPlayer, PlayerWaiting.Fire} |
| spawn-selection.md | uc:SBase.SGameInfo:{FindPlayerStart, RatePlayerStart, RateStartByDistance}; uc:Engine.GameInfo:{FindPlayerStart, RatePlayerStart} (not used); uc:SBase.STriggerPlayerStart:{Trigger, UnTrigger}; def:Engine.PlayerStart; def:SBase.STriggerPlayerStart |
| respawn-and-spawning.md | uc:SBase.SGameInfo:{RestartPlayer, AddDefaultInventory, ResetVisionMode, s_MatchOver.RestartPlayer}; uc:Engine.GameInfo:RestartPlayer; uc:SBase.SPlayerController:{Possess, UnPossess, EnterStartState, ClientRestart, GivePawn, StandardReactionsToDamage (protection clause), WaitingForPawn.BeginState, WaitingForPawn.EndState, WaitingForPawn.PlayerTick, WaitingForPawn.Timer, WaitingForPawn.Fire, WaitingForPawn.AltFire}; uc:Engine.PlayerController:{Restart, AskForPawn, GivePawn, CanRestartPlayer}; uc:Engine.Controller:{Restart, Possess} |
| death-lives-respawn.md | uc:SBase.SGameInfo:{Killed, UpdateTeamNumLives}; uc:Engine.GameInfo:{Killed, NotifyKilled} (not called); uc:SBase.SPlayerController:{s_Dead.BeginState, s_Dead.EndState, s_Dead.Timer, s_Dead.PlayerMove, s_Dead.PlayerTick, s_Dead.Fire, s_Dead.ServerReStartPlayer, s_Dead.bIsDamageVolumeSensitive, s_MultiSpectating_WhenKilled.BeginState, s_MultiSpectating_WhenKilled.Timer, s_WaitRespawn.BeginState, s_WaitRespawn.EndState, s_WaitRespawn.ServerReStartPlayer, s_WaitRespawn.PlayerMove, s_WaitRespawn.PlayerTick, s_WaitRespawn.Fire, s_WaitRespawn@code, ServerRespawn, Respawn, ServerResign, Resign, AntiBugTellServerIAmDead, CanPickRespawnWeapon, BeginRespawnWeaponPick, CheckRespawnWeaponKey, ServerCycleRespawnWeapon, SpawnWeaponFor, MercWeaponIndex, ClientSetRespawnWeapon} |
| spectating.md | uc:SBase.SPlayerController:{s_BaseSpectating.* (8 functions), s_MultiSpectating.* (8), s_MultiSpectating_WhenKilled.{Fire, PlayerTick, SwitchEnhancedReality, BeginState (spectating part)}, s_Spectating_Visitor.{BeginState, Fire, PlayerTick, SwitchEnhancedReality}, Server_FindNextFriend, Server_FindNextVisitor, BaseSpectating.{IsSpectating, MyProcessMove, PlayerMove}, spectating} |
| end-of-round.md | uc:SBase.SGameInfo:{sCheckEndGame, sEndGame, ReplicateWinningReason, CountMercsStillIn, GetTopKills, GetNbSentries, CheckEndGame, EndGame, NotifyObjectiveTaken, NotifyDogTag, BroadcastMusic, Timer (time limit), s_MatchOver.BeginState, s_MatchOver.Timer, s_MatchOver.ScoreKill, s_MatchOver.ChangeTeam}; uc:Engine.GameInfo:{CheckEndGame, EndGame}; uc:Engine.Controller:{GameHasEnded, ClientGameEnded, GameEnded.BeginState}; uc:Engine.PlayerController:{GameEnded.ServerReStartPlayer, GameEnded.IsSpectating, GameEnded.ThrowWeapon, GameEnded.ServerRestartGame, GameEnded.FindGoodView, GameEnded@code}; uc:SBase.SPlayerController:{GameEnded.* (15 functions)}; uc:SBase.SEndZoneTip:{Touch, s_DelayBeforeEnd.BeginState, s_DelayBeforeEnd.Timer}; def:SBase.SEndZoneTip |
| scoring-and-stats.md | uc:SBase.SGameInfo:{AddPoint, InitScores, BroadcastDeathMessage, FindDamageVolume, CountEnvironmentalKill, BroadcastScore, BroadcastTeamLocalizedMessage}; uc:Engine.LevelInfo:InitArrayNbPoints; uc:Engine.GameInfo:{BroadcastDeathMessage, ScoreKill, ScoreObjective, CheckScore, BroadcastLocalized}; uc:SBase.SPlayerController:StandardReactionsToDamage (kill points and damage stats); def:Engine.SPlayerStats |
| round-travel.md | uc:SBase.SGameInfo:{RestartGame, ProcessServerTravel, ForceMenuLobby, ForceMenuQuit, ForceMenuClientEntry}; uc:Engine.GameInfo:ProcessServerTravel; uc:Engine.LevelInfo:ServerTravel; uc:Engine.PlayerController:SwitchLevel; nat:Engine.PlayerController.ClientTravel; uc:GUI.Ending_Menu:{Timer, InitComponent} (contract); uc:GUI.GUIComponent:{AllPlayerReady, HowManyPlayersInTeam} (contract); uc:SBase.SPlayerController:{ReadyToPlayAgain, ServerReadyToPlayAgain, CLientForceMenuLobby, ClientForceMenuEntry} |
| replication-info.md | uc:Engine.GameReplicationInfo:{PostBeginPlay, PostNetBeginPlay, TeamSymbolNotify, Reset, Timer, PreciseElapsedTime, AddPRI, RemovePRI, GetPRIArray, SetBridage, logBridage}; rep:Engine.GameReplicationInfo.{TimeLimit, ElapsedTime, RemainingTime, TimesRespawn, TeamLivesATT, TeamLivesDEF, TotalTeamLives, WinningTeam, WinningReason, bMatchHasBegun, bMatchLaunching, bMatchLaunchingNow, bStopCountDown, GameMode, Teams, NumObjectives, GamesInARow, Revenge, ServerOnly}; def:Engine.GameReplicationInfo; uc:Engine.PlayerReplicationInfo:{PostBeginPlay, PostNetBeginPlay, Destroyed, Reset, SetFlag, Timer, UpdatePlayerLocation, SetPlayerName, SetWaitingPlayer, playerQuitMatch}; rep:Engine.PlayerReplicationInfo.{NumLives, Score, Kills, Deaths, Team, bOnlySpectator, bIsSpectator, bWaitingPlayer, bReadyToPlay, bReadyToPlayAgain, Ping, StartTime}; def:Engine.PlayerReplicationInfo; rep:Engine.TeamInfo.{Size, Score, TeamIndex}; rep:SBase.STeamInfo.{iNbObjectivesTaken, iNBGadgets, bDisallowedInv}; rep:Engine.LevelInfo.{NbPointsAttack, NbPointsDefense} |
| netmode-branches.md | uc:SBase.SGameInfo:{Kick, KickBan, AllPlayerReadyToPlayToServer}; uc:SBase.SPlayerController:{IAmOnClient, MirrorState, ChangeState, PCChangeClientState, PCChangeServerState}; cross-reference of every NetMode test in this folder |
| game-modes.md | uc:GUI.Create_Match:{LoadMatchRules, LoadModeRules, SetMercDeathMatchRules, SetDiskLimits} (contract); the per-mode view of the rules (missions, teams, lives, end conditions, late joiners, Extraction, Sabotage and Neutralization paths) |

Unit count: 322 distinct unit IDs listed across 12 spec files. SGameInfo: 48 of its 52 functions and all
5 s_MatchOver functions are specced; the other 4 (two voice chat functions, two destructor stubs) are
listed below.

## SteeE Story slice: game-flow rules as decoded (supersedes the plan's summary where they differ)
1. Settings reach the game only through the URL. Network launches from the menus carry SteeE's mode entry
   (10 min, 4 spy lives, 3 merc lives, 10 s). Play Level carries none of them: unlimited lives (999999),
   15 s respawn delay, no time limit.
2. Lives are per player (PRI.NumLives), set at StartMatch; 4 lives means the 4th death is final.
3. Respawn is not 10 s after death: the body stays 5.0 s, blinks 2.5 s, then the player spectates
   teammates for TimesRespawn seconds. Death to respawn is about 17.6 s with SteeE settings (3.6 + 10 after
   a strangle kill).
4. Spawn protection is only a 3.0 s immunity of spies to explosion damage (SDamage_Explosion). Mercs get
   none; bullets, grabs and stuns hit spies immediately. Separately, a new pawn sticky-shocks enemies within
   170 uu (same level) or 220 uu (enemy more than 20 uu above).
5. Spawn selection in network play is distance only: the enabled start of the player's side whose nearest
   pawn is farthest. The Priority pass runs only while bMatchHasBegun is false: every spawn in Play Level
   (so a SteeE merc always uses PlayerStart254), initial spawns in offline matches, never in network play.
   The trigger start `enable_shop_spawn` is enabled for the whole match (nothing triggers or untriggers it).
6. Mercs win on time out (END_TIMEOUT), checked before anything else. Spies win when every active
   objective is taken (END_STORY). Team death (END_TEAMDEATH): in network play one side with players but
   none with lives left loses.
7. Play Level never ends a round (bMatchHasBegun stays false), so round end, time limit and running out of
   lives can only be verified on a listen server.
8. Late joiners in Story become spectators (s_Spectating_Visitor) until the next map.
9. Next game of a series or revenge pair is driven by the host's end menu (10 s after the result), not by
   SGameInfo.RestartGame; revenge swaps sides at Login and carries the team point counters swapped.

## Relevant units found but not specced here
Voice chat (voice spec): uc:SBase.SGameInfo:ChangeVoiceChatter, uc:SBase.SGameInfo:UpdateVoiceChatMuting,
uc:Engine.GameInfo:{ChangeVoiceChatter, MuteVoiceChatters, TeamVoiceOnlyApplies, MuteVoiceChattersByPlayer,
HearVoiceChatter}, SPlayerController TalkToMate, OtherTeamCantHearMe, ServerClearVoiceChatTarget, StopHackComm.

Trivial or dead code (mark n-a with reason): uc:SBase.SGameInfo:Destroyed and uc:SBase.SGameInfo:GameEnding
(both only call the Actor destructor hook), uc:Engine.GameInfo:{Reset, GameEnding, EndLogging,
UpdatePrecacheMaterials, UpdatePrecacheStaticMeshes, PrecacheGameTextures, PrecacheGameStaticMeshes,
FindPlayerByID, UseLowGore, DetailChange, AcceptInventory, SetPlayerDefaults, PreventDeath, IsOnTeam,
CanSpectate, DiscardInventory, SendPlayer, FindTeamDesignation, ParseMessageString, ReviewJumpSpots,
OtherTeam, AdminSay}, uc:Engine.PlayerController:Reset (enters PlayerWaiting, never called),
uc:Engine.Pawn:Dying.BeginState (LastSpawnCampTime, UT legacy), GUI.Restart_Menu (legacy restart page).

Server browser and beacon (net or GUI spec): uc:Engine.GameInfo:{GetServerInfo, GetServerDetails,
GetServerPlayers, GetServerPort, GetBeaconText}, nat:Engine.GameInfo.GetNetworkNumber,
nat:Engine.GameInfo.ParseKillMessage, Engine.Info ServerResponseLine structs.

Broadcast and access control (own spec needed): Engine.BroadcastHandler (all functions; used by
BroadcastLocalized, UpdateSentText), Engine.AccessControl (PreLogin, Kick, KickBan, SetGamePassword,
RequiresPassword), uc:Engine.GameInfo:{Broadcast, BroadcastTeam}, Engine.GameMessage, SBase.SGameMessages,
SBase.SDeathMessage (message texts and switches 1, 4, 23, 24, 56 used here).

PRI cosmetic and minimap (HUD spec): uc:Engine.PlayerReplicationInfo:{setVisibleFromDedicatedServer,
SendMapListToServer, UpdateMinimapParams, SetCharacterName, GetHumanReadableName, GetLocationName,
GetPortrait, UpdateCharacter, DisplayDebug, ClientNameChange}; uc:Engine.TeamInfo:{BelongsOnTeam,
UpdatePrecacheMaterials, GetHumanReadableName}.

Objectives (objectives spec): SBase.SMission (Reset, NotifyTaken, ComputeObjectivesLeft,
GetTotalObjectivesLeft, UpdateObjectivesLeft, PrepareObjectives, DoBroadcastLocalizedMessage),
nat:SBase.SMission.{Desactivate, NotifyChildTaken, ActivateDisks}, SBase.SObjective (NotifyTaken, Reset,
Desactivate, ScheduleReset), SBase.SFlag CarrierKiller use, SObjectiveTrigger.

Death entry and damage (damage and animation specs): every `ChangeState('s_Dead')` and
`MirrorState('s_Dead')` site in SPlayerController, uc:SBase.SPlayerController:StandardReactionsToDamage
(everything except the clauses used here), PawnTakeDamage, CountPoisonDrain,
s_PlayerFalling_OutOfWorld (sets bInstantRespawn), s_GrabbedByPlayer_Released_STRANGLE (sets
fTempsAvantClignote 1.0), DropDisks, DiscardFlag, DiscardPickup, ReinitPawnBecauseOfDivergences,
TellClientServerWaitForFatalBlast.

HUD and end screen (HUD spec): SpawnMagmaHud, SpawnMagmaEndGame, CloseMagmaMenu, SetWeirdEffect,
SHud.EmptyAlarmsList, ClientSetMusic, ClientSetHUD, GUI.Ending_Menu (display parts), GUI.End_Menu_Stat,
GUI.BackToLobby, GUI.GUIPageWaitLaunch, GUI.Lobby_Create, GUI.Dedicated_Server, GUI.Game_Settings.

LAN and services (net spec): SBase.SLANServerLink, SBase.SRJ45Checker, SBase.SLANClientLink,
SBase.SXBoxLiveRef.StartMatch, nat:SBase.SPlayerController.{canStartTravelOnServer,
refuseClientsConnections, unregisterClientTravel}, SPlayerController.travelFinished.

Inventory at spawn (inventory spec): SBasePawn.UpdateInventory, SBasePawn.AddDefaultGadgets,
SBasePawn.DeleteAllInventory, ServerSetSpyInventory, ServerSetMercInventory, GUI.Loading_Menu.SetInventory and
BuildInventoryList.

Movement start states (movement specs): s_PlayerWalkingCr0H, s_PlayerWalkingSt2H BeginState.
