using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.GameFlow;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;
using K = OpenScct.Game.GameFlow.K;

namespace OpenScct.Game.SBase;

// SBase.SGameInfo: the Versus game rules. This file holds the setup order (InitGame, PreBeginPlay, PostBeginPlay,
// InitGameReplicationInfo) and the 1 s rules timer. Written from spec/game/match-options.md and
// spec/game/match-lifecycle.md; the other SGameInfo.*.cs files hold login, spawning, scoring, the end of the round
// and travel.
public partial class SGameInfo
{
    private static readonly UcClassIndex SBaseIndex = new(CorePackage.Info, EnginePackage.Info, SBasePackage.Info);

    public static new readonly StateTable ClassStates = new(GameInfo.ClassStates, s_MatchOver.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>Core, Engine and SBase classes.</summary>
    public override UcClassIndex GetClassIndex() => SBaseIndex;

    private USPersistentData? _persistentData;

    /// <summary>
    /// GetPersistentData(): the host's settings and profile the rules read (dedicated host, random maps, ability
    /// switches, gadget limits, voice chat mode, disk counts, auto-balance). It outlives the map in the original; here
    /// the host installs it as the level's <see cref="USPersistentData"/> service before the map loads (the front end
    /// or <see cref="Modes.MatchSetup"/> fills it), else the rules use one as the engine makes it (InitPersistentData).
    /// </summary>
    public USPersistentData PersistentData
    {
        get => _persistentData ??= XLevel?.GetService<USPersistentData>() ?? USPersistentData.CreateInitialized();
        set => _persistentData = value;
    }

    /// <summary>Level.GetLevelSettings(MapName): the map's SMapSettings, or null when the map has none.</summary>
    public USMapSettings? LevelSettings { get; set; }

    /// <summary>The level's collision queries (the ICollisionWorld service, else the physics driver's world).</summary>
    protected ICollisionWorld? Collision =>
        XLevel.GetService<ICollisionWorld>() ?? (XLevel.PhysicsDriver as PhysicsDriver)?.Collision;

    /// <summary>The level's hooks into other areas.</summary>
    protected IGameFlowPorts Ports => GameFlowServices.Ports(this);

    // ---- setup ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads the URL options once, before PreBeginPlay: player caps, difficulty, speed, the broadcast handler and
    /// access control, the match settings, the scoring table and, in a revenge game, the carried team points.
    /// </summary>
    [Uc("SBase.SGameInfo.InitGame", Sha1 = "029a22ef8080ccdf24227f3a203d9635eb0eed87")]
    [PortOf("uc:SBase.SGameInfo:InitGame", Spec = "spec/game/match-options.md#rules")]
    public override void InitGame(string Options, ref string Error)
    {
        MaxPlayers = SvmMath.Clamp(GetIntOption(Options, "MaxPlayers", MaxPlayers), 0, K.MaxPlayersCap);
        GameDifficulty = SvmMath.Max(0f, GetIntOption(Options, "Difficulty", (int)GameDifficulty));
        string inOpt = ParseOption(Options, "GameSpeed");
        if (inOpt.Length != 0)
        {
            SetGameSpeed(UcStrings.Atof(inOpt));
        }

        BroadcastHandler = Spawn<BroadcastHandler>();

        // AdminName with AdminPassword marks the access control class default bDontAddDefaultAdmin; the admin list
        // and passwords belong to access control, which is not ported.
        AccessControl = Spawn<AccessControl>();

        inOpt = ParseOption(Options, "AllowThrowing");
        if (inOpt.Length != 0)
        {
            bAllowWeaponThrowing = UcStrings.ToBool(inOpt);
        }

        inOpt = ParseOption(Options, "GameStats");
        bEnableStatLogging = inOpt.Length != 0 && UcStrings.ToBool(inOpt);

        MatchInfo = new USMatchInfo();
        MatchInfo.SetOptionString(Options);
        MaxPlayers = SvmMath.Clamp(GetIntOption(Options, "MaxPlayers", MaxPlayers), 0, K.MaxPlayersCap);
        if (Host.IsPsx2())
        {
            MaxTeamSize = K.Psx2MaxTeamSize;
        }

        PlayersToWait = (byte)GetIntOption(Options, "NBPlayers", K.DefaultPlayersToWait);
        if (MatchInfo.GameMode != EGameMode.GM_Undefined)
        {
            ScoreBoardType = K.StoryScoreBoard;
        }

        if (MatchInfo.Revenge)
        {
            Level.NbPointsAttack = GetIntOption(Options, "ScoreATT", 0);
            Level.NbPointsDefense = GetIntOption(Options, "ScoreDEF", 0);
        }

        MatchInfo.GamesInARow = GetIntOption(Options, "GamesInARow", 0);

        // The original also writes GameReplicationInfo.GamesInARow here; the GRI does not exist yet, so it has no
        // effect (PreBeginPlay and PostLogin copy the value later).
        if (GameReplicationInfo is not null)
        {
            GameReplicationInfo.GamesInARow = MatchInfo.GamesInARow;
        }

        Level.InitArrayNbPoints(K.ScoringMode);
    }

    [Uc("SBase.SGameInfo.PreBeginPlay", Sha1 = "70fa85836a1ed8e35bcef236688836603361c6c5")]
    [PortOf("uc:SBase.SGameInfo:PreBeginPlay", Spec = "spec/game/match-lifecycle.md#rules")]
    protected override void PreBeginPlay()
    {
        base.PreBeginPlay();
    }

    /// <summary>
    /// Spawns the network cable watcher and the two teams (spies at index 0, mercs at 1), then clears the dog tag
    /// count. The watcher's Initialize (LAN service) is not ported.
    /// </summary>
    [Uc("SBase.SGameInfo.PostBeginPlay", Sha1 = "53506c5e2ab66b44d5904d1a102289124ac9a0e3")]
    [PortOf("uc:SBase.SGameInfo:PostBeginPlay", Spec = "spec/game/match-lifecycle.md#rules")]
    protected override void PostBeginPlay()
    {
        rj45checker = Spawn<SRJ45Checker>();
        if (GameReplicationInfo is { } gri)
        {
            gri.Teams[0] ??= Spawn<STeamAttaquant>();
            gri.Teams[1] ??= Spawn<STeamDefenseur>();
        }

        BroadcastScoreTime = 0f;
        base.PostBeginPlay();
        if (GameReplicationInfo is not null)
        {
            GameReplicationInfo.NumDogTagsTaken = 0;
        }
    }

    /// <summary>Copies the match settings and the host's persistent settings into the replicated game info.</summary>
    [Uc("SBase.SGameInfo.InitGameReplicationInfo", Sha1 = "faae61ae67857c71cbddae04af2ca3d9307fa17b")]
    [PortOf("uc:SBase.SGameInfo:InitGameReplicationInfo", Spec = "spec/game/match-lifecycle.md#rules")]
    public override void InitGameReplicationInfo()
    {
        base.InitGameReplicationInfo();
        if (GameReplicationInfo is not { } gri)
        {
            return;
        }

        USMatchInfo? m = MatchInfo;
        gri.GamesInARow = m?.GamesInARow ?? 0;
        gri.ServerName = m?.LevelName ?? string.Empty;
        gri.ShortName = Level.GetLocalPlayerController()?.PlayerReplicationInfo?.PlayerName ?? string.Empty;
        gri.TimeLimit = m?.TimeLimit ?? 0;
        gri.TeamLivesATT = m?.NumLivesATT ?? 0;
        gri.TeamLivesDEF = m?.NumLivesDEF ?? 0;
        gri.TimesRespawn = m?.TimesRespawn ?? 0;
        gri.GameMode = m?.GameMode ?? EGameMode.GM_Undefined;
        gri.ServerOnly = PersistentData.bDedicatedServer;
        gri.Revenge = m?.Revenge ?? false;
        gri.bRandomMap = PersistentData.bRandomMap;
        gri.GameWithRanking = m?.GameWithRanking ?? false;
        gri.allowVoiceChat = IsVoiceChatAllowed();
        gri.allowVoiceChatInLobby = PersistentData.allowVoiceChatInLobby;
        gri.NumDisks = PersistentData.NumDisks;
        gri.NumDisksToWin = PersistentData.NumDisksToWin;
    }

    /// <summary>The profile's voice chat permission; false without a profile (a None call reads false).</summary>
    [Provenance(ProvenanceKind.Estimated, "SPlayerProfile.IsVoiceChatAllowed is not ported: any profile allows voice chat")]
    private bool IsVoiceChatAllowed() => PersistentData.PlayerProfile is not null;

    // ---- the rules timer -----------------------------------------------------------------------------------

    /// <summary>
    /// Every game second until the match is over: poison drain, the welcome message for new spectators, the time
    /// limit, and the level's timed event. The broadcast handler's sent-text reset is not ported.
    /// </summary>
    [Uc("SBase.SGameInfo.Timer", Sha1 = "4a9e8934382b7975d61ffbbd78b0aa40ef8ba48c")]
    [PortOf("uc:SBase.SGameInfo:Timer", Spec = "spec/game/match-lifecycle.md#rules")]
    protected override void Timer()
    {
        for (Controller? c = Level.ControllerList; c is not null; c = c.nextController)
        {
            if (c is SPlayerController spc && spc.PoisonInstigator is not null)
            {
                Ports.CountPoisonDrain(spc);
            }
        }

        GameReplicationInfo? gri = GameReplicationInfo;
        if (bWelcomePending)
        {
            bWelcomePending = false;
            if (Level.NetMode != ENetMode.NM_Standalone && gri is not null)
            {
                for (int i = 0; i < gri.PRIArray.Count; i++)
                {
                    PlayerReplicationInfo pri = gri.PRIArray[i];
                    if (pri is null || pri.bWelcomed)
                    {
                        continue;
                    }

                    pri.bWelcomed = true;
                    if (pri.bOnlySpectator && !(pri.bIAmServer && gri.ServerOnly))
                    {
                        BroadcastLocalizedMessage(GameMessageClass, K.MessageEnteredGame, pri);
                    }
                }
            }
        }

        for (PlayerStart? n = Level.NavigationPointList; n is not null; n = n.nextNavigationPoint)
        {
            n.FearCost = SvmMath.Trunc(n.FearCost * FearCostFallOff);
        }

        if (MatchInfo is not null && gri is not null && gri.TimeLimit > 0 && gri.ElapsedTime >= gri.TimeLimit * 60 &&
            gri.bMatchHasBegun)
        {
            if (sCheckEndGame())
            {
                sEndGame();
            }
        }

        int elapsed = gri?.ElapsedTime ?? 0;
        if (BroadcastScoreTime > 0f && elapsed > BroadcastScoreTime)
        {
            BroadcastScoreTime = 0f;
            BroadcastScore();
        }

        if (Level.SendAtTime != 0)
        {
            if (Level.SendAtTime > 0 && elapsed >= Level.SendAtTime)
            {
                Level.TriggerEvent(Level.Event, Level, null);
                Level.SendAtTime = 0;
            }
            else if (gri is not null && gri.RemainingTime > 0 && gri.RemainingTime <= -Level.SendAtTime)
            {
                Level.TriggerEvent(Level.Event, Level, null);
                Level.SendAtTime = 0;
            }
        }
    }

    /// <summary>Empty in this game; BroadcastScoreTime therefore has no effect.</summary>
    [Uc("SBase.SGameInfo.BroadcastScore", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SGameInfo:BroadcastScore", Spec = "spec/game/scoring-and-stats.md#rules")]
    public void BroadcastScore()
    {
    }
}
