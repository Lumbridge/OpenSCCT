using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.GameFlow;

/// <summary>Constants of the game flow (spec/game), with their evidence.</summary>
public static class K
{
    // ---- match options (spec/game/match-options.md) -----------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SMatchInfo.SetOptionString: lives option 0 or absent")]
    public const int UnlimitedLives = 999999;

    [Provenance(ProvenanceKind.Decoded, "SMatchInfo.SetOptionString: TimesRespawnsATT absent")]
    public const int DefaultTimesRespawn = 15;

    [Provenance(ProvenanceKind.Decoded, "SMatchInfo.SetOptionString: GameMode absent (GM_Extraction)")]
    public const int DefaultGameMode = 1;

    [Provenance(ProvenanceKind.Decoded, "SMatchInfo.SetOptionString: TimesReinitFlag and TimeToExplode absent")]
    public const int DefaultFlagAndBombOption = 1;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.InitGame and SMatchInfo.SetOptionString: MaxPlayers clamp")]
    public const int MaxPlayersCap = 32;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.InitGame: NBPlayers absent")]
    public const int DefaultPlayersToWait = 1;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.InitGame: MaxTeamSize on the console build")]
    public const int Psx2MaxTeamSize = 2;

    [Provenance(ProvenanceKind.Decoded, "SMatchInfo.DefaultKillLimit")]
    public const int DefaultKillLimit = 20;

    [Provenance(ProvenanceKind.Decoded, "SMatchInfo.MaxKillLimit")]
    public const int MaxKillLimit = 100;

    [Provenance(ProvenanceKind.Decoded, "SMatchInfo.RespawnFloor: Merc Deathmatch")]
    public const int RespawnFloorMercDeathMatch = 1;

    [Provenance(ProvenanceKind.Decoded, "SMatchInfo.RespawnFloor: other modes")]
    public const int RespawnFloorOther = 5;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.InitGame: the scoring table it selects")]
    public const string ScoringMode = "POINT_Multiple";

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.InitGame: scoreboard class for a defined game mode")]
    public const string StoryScoreBoard = "SScoreBoardStory";

    // ---- the start gate and the end menus (spec/game/match-lifecycle.md, spec/game/round-travel.md) -----------

    [Provenance(ProvenanceKind.Decoded, "GUI.Loading_Menu s_Init: delay before an offline match starts")]
    public const float StartGateStandaloneDelay = 2f;

    [Provenance(ProvenanceKind.Decoded, "GUI.Loading_Menu s_Init: poll period")]
    public const float StartGateInitPoll = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "GUI.Loading_Menu s_Server: poll period and the delay before the start")]
    public const float StartGateServerPoll = 1f;

    // ---- login and teams (spec/game/login-and-teams.md) -------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.Login: Left(Name, 20)")]
    public const int PlayerNameLength = 20;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.Login: team of a spectator or non-playing host")]
    public const byte SpectatorTeam = 255;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.Login: where the non-playing host's controller spawns")]
    public static readonly FVector ServerOnlyLocation = new(100000f, 100000f, 100000f);

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.Login and ProcessServerTravel: GamesInARow absent")]
    public const int RevengeGamesInARowDefault = 2;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.MoveEveryoneToMercs: temporary MaxTeamSize")]
    public const int MoveEveryoneToMercsTeamCap = 255;

    // ---- messages ---------------------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.Timer: GameMessage switch for a new spectator")]
    public const int MessageEnteredGame = 1;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.Logout and GameInfo.Logout: GameMessage switch")]
    public const int MessageLeftGame = 4;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.BroadcastDeathMessage: sniper head shot switch")]
    public const int DeathMessageHeadshot = 23;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.BroadcastDeathMessage: resigned player switch")]
    public const int DeathMessageResigned = 24;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.Killed: SGameMessages switch for a last life")]
    public const int MessageLastLife = 56;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.BroadcastDeathMessage: head pill tag")]
    public const int HeadPill = 1;

    // ---- spawn selection (spec/game/spawn-selection.md) -------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.RateStartByDistance: starting rating")]
    public const float InitialDistanceRating = 1000000f;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.FindPlayerStart: starting best rating")]
    public const float InitialBestRating = -1f;

    // ---- spawning (spec/game/respawn-and-spawning.md) ---------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.RestartPlayer: floor probe length")]
    public const float FloorProbeLength = 200f;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.RestartPlayer: floor probe extent")]
    public static readonly FVector FloorProbeExtent = new(35f, 35f, 10f);

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.RestartPlayer: spawn height above the probe hit")]
    public const float SpawnHeightAboveHit = 79f;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.RestartPlayer: spawn shock height split")]
    public const float SpawnShockLevelZ = 20f;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.RestartPlayer: spawn shock radius, enemy above")]
    public const float SpawnShockRadiusAbove = 220f;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.RestartPlayer: spawn shock radius, same level")]
    public const float SpawnShockRadiusLevel = 170f;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.RestartPlayer: spawn shock pill tag")]
    public const int SpawnShockPillTag = -10;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.EnterStartState: fSpawnProtectionEnd offset (const SpawnProtectionTime)")]
    public const float SpawnProtection = 3f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.WaitingForPawn: AskForPawn retry")]
    public const float WaitingForPawnRetry = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.WaitingForPawn.PlayerTick: slowest accepted retry timer")]
    public const float WaitingForPawnMaxRate = 1f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.EnterStartState: zip line memory reset point")]
    public static readonly FVector FarZipPoint = new(-10000000f, 0f, -10000000f);

    // ---- death and respawn (spec/game/death-lives-respawn.md) -------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SPlayerController s_Dead.Timer: blink duration")]
    public const float BlinkDuration = 2.5f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController s_Dead.BeginState: timer on the server and standalone")]
    public const float DeadTimerServer = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController s_Dead.BeginState: timer on a client")]
    public const float DeadTimerClient = 1f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController s_MultiSpectating_WhenKilled.BeginState: shortest respawn timer")]
    public const float MinRespawnTimer = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.ServerResign: damage beyond the pawn's health")]
    public const int ResignExtraDamage = 100;

    // ---- end of round (spec/game/end-of-round.md) -------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.sCheckEndGame: winner on time out (mercs)")]
    public const byte TimeOutWinner = 1;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.sCheckEndGame: winner when every objective is taken (spies)")]
    public const byte StoryWinner = 0;

    [Provenance(ProvenanceKind.Decoded, "GRI WinningTeam and WinningReason when nothing is decided")]
    public const byte NoResult = 255;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo s_MatchOver.BeginState: timer period")]
    public const float MatchOverTimer = 1f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController GameEnded.PlayerTick: delay before the end menu")]
    public const float EndMenuDelay = 10f;

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.sEndGame: event fired at the end")]
    public static readonly Name EndGameEvent = new("EndGame");

    [Provenance(ProvenanceKind.Decoded, "SGameInfo.NotifyObjectiveTaken: jingle")]
    public const string ObjectiveTakenJingle = "Interface.Objectifs.Jingl_objectifTaken";

    // ---- spectating (spec/game/spectating.md) -----------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SPlayerController spectator PlayerTick: target re-check period")]
    public const float SpectateRecheck = 1f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController s_MultiSpectating.UpdateRotation: look rate")]
    public const float SpectateLookRate = 50f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController s_MultiSpectating.UpdateRotation: pitch clamp")]
    public const int SpectatePitchLimit = 15000;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.Server_FindNextFriend / Server_FindNextVisitor: candidate slots")]
    public const int SpectateCandidateSlots = 12;

    // ---- state mirroring (spec/game/netmode-branches.md) ------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.MirrorState: acknowledgement refresh")]
    public const float MirrorStateAckRefresh = 1f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.PCChangeServerState: acknowledgement tolerance")]
    public const float ServerStateTolerance = 0.01f;
}
