using System;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Engine.GameInfo game flow: map load entry points, the match setup order, login and logout bookkeeping, and the
// state dispatch for the functions the SBase match-over state overrides. Written from spec/game/*.md.
public partial class GameInfo
{
    private GameInfo? _classDefaults;

    /// <summary>
    /// The machine the rules run on (client calls, GUI facts, natives outside the simulation): the level's
    /// <see cref="IGameHost"/> service, or <see cref="NullGameHost"/>.
    /// </summary>
    public IGameHost Host => XLevel.GetService<IGameHost>() ?? NullGameHost.Instance;

    // ---- functions a GameInfo state overrides (state dispatch, see the OpenScct.Core README) -------------------

    public interface IRestartPlayer
    {
        void RestartPlayer(GameInfo self, Controller aPlayer);
    }

    public interface IChangeTeam
    {
        bool ChangeTeam(GameInfo self, Controller Other, int num, bool bNewTeam);
    }

    public interface IScoreKill
    {
        void ScoreKill(GameInfo self, Controller? Killer, Controller? Other);
    }

    /// <summary>RestartPlayer through the current state.</summary>
    public void eventRestartPlayer(Controller aPlayer)
    {
        if (StateNode is IRestartPlayer s)
        {
            s.RestartPlayer(this, aPlayer);
        }
        else
        {
            RestartPlayer(aPlayer);
        }
    }

    /// <summary>ChangeTeam through the current state.</summary>
    public bool eventChangeTeam(Controller Other, int num, bool bNewTeam) =>
        StateNode is IChangeTeam s ? s.ChangeTeam(this, Other, num, bNewTeam) : ChangeTeam(Other, num, bNewTeam);

    /// <summary>ScoreKill through the current state.</summary>
    public void eventScoreKill(Controller? Killer, Controller? Other)
    {
        if (StateNode is IScoreKill s)
        {
            s.ScoreKill(this, Killer, Other);
        }
        else
        {
            ScoreKill(Killer, Other);
        }
    }

    // ---- map load and player arrival (native entry points) -------------------------------------------------

    /// <summary>The classes this game can spawn from class references. Game packages add theirs.</summary>
    public virtual UcClassIndex GetClassIndex() => UcSpawn.EngineIndex;

    /// <summary>
    /// Spawns the game rules for a map and runs <see cref="InitGame"/> with the URL options before any begin-play
    /// event, as the engine's map load does. Call it while the level loads (between BeginLoad and EndLoad), after the
    /// map's own actors; on a level that has not loaded or ticked yet it runs a load of its own.
    /// </summary>
    [UcNative("UGameEngine::LoadMap")]
    [Provenance(ProvenanceKind.Estimated, "UE2 load order: the GameInfo spawns after the map actors and InitGame runs before the begin-play passes")]
    public static T SpawnGameInfo<T>(Level level, string Options, ref string Error)
        where T : GameInfo, new()
    {
        ArgumentNullException.ThrowIfNull(level);
        bool ownLoad = !level.IsLoading;
        if (ownLoad)
        {
            level.BeginLoad();
        }

        T game = level.SpawnActor(new T(), default, default)
            ?? throw new InvalidOperationException($"{typeof(T).Name} could not be spawned.");
        level.Info.Game = game;
        game.InitGame(Options ?? string.Empty, ref Error);
        if (ownLoad)
        {
            level.EndLoad();
        }

        return game;
    }

    /// <summary>
    /// A player arrives (UE2 ULevel::SpawnPlayActor): <see cref="Login"/>, then the controller gets its player
    /// object and roles (a local viewport player is SimulatedProxy on its own machine, a remote one
    /// AutonomousProxy), then <see cref="PostLogin"/>. Returns null with <paramref name="Error"/> set when Login fails.
    /// </summary>
    [UcNative("ULevel::SpawnPlayActor")]
    [Provenance(ProvenanceKind.Estimated, "UE2 SpawnPlayActor: Login, set Player and roles, PostLogin")]
    public PlayerController? SpawnPlayActor(string Portal, string Options, bool bLocalPlayer, ref string Error)
    {
        PlayerController? pc = Login(Portal ?? string.Empty, Options ?? string.Empty, ref Error);
        if (pc is null)
        {
            return null;
        }

        pc.Player = bLocalPlayer ? new SimViewport() : new SimNetConnection();
        pc.Role = ENetRole.ROLE_Authority;
        pc.RemoteRole = bLocalPlayer ? ENetRole.ROLE_SimulatedProxy : ENetRole.ROLE_AutonomousProxy;
        if (bLocalPlayer)
        {
            Level.LocalPlayerController = pc;
        }

        PostLogin(pc);
        return pc;
    }

    // ---- setup ---------------------------------------------------------------------------------------------

    [Uc("Engine.GameInfo.PreBeginPlay", Sha1 = "771552325f2d6fdd45fc1f1539a30025061cf8c9")]
    [PortOf("uc:Engine.GameInfo:PreBeginPlay", Spec = "spec/game/match-lifecycle.md#rules")]
    protected internal override void PreBeginPlay()
    {
        StartTime = 0f;
        SetGameSpeed(GameSpeed);
        GameReplicationInfo = UcSpawn.Spawn(this, GameReplicationInfoClass) as GameReplicationInfo;

        // LevelInfo.GRI is a transient engine reference the script never writes; it points at the game's GRI.
        Level.GRI = GameReplicationInfo;
        InitGameReplicationInfo();
        InitLogging();
    }

    /// <summary>The Engine version only starts the stat log, which never exists in this game.</summary>
    [Uc("Engine.GameInfo.PostBeginPlay", Sha1 = "fce6b1c903b6a38d8ad1e4fbc8db5d73c3fe00e1")]
    [PortOf("uc:Engine.GameInfo:PostBeginPlay", Spec = "spec/game/match-lifecycle.md#rules")]
    protected internal override void PostBeginPlay()
    {
    }

    [Uc("Engine.GameInfo.SetGameSpeed", Sha1 = "73155b2fbc99c1620e084b4fb8360bf5e9e5edd9")]
    [PortOf("uc:Engine.GameInfo:SetGameSpeed", Spec = "spec/game/match-lifecycle.md#rules")]
    public virtual void SetGameSpeed(float t)
    {
        GameSpeed = SvmMath.Max(t, 0.1f);
        Level.TimeDilation = GameSpeed;
        SetTimer(Level.TimeDilation, true);
    }

    [Uc("Engine.GameInfo.InitGameReplicationInfo", Sha1 = "f27139677d42cff01d0661b7aa392e7435213739")]
    [PortOf("uc:Engine.GameInfo:InitGameReplicationInfo", Spec = "spec/game/match-lifecycle.md#rules")]
    public virtual void InitGameReplicationInfo()
    {
        if (GameReplicationInfo is null)
        {
            return;
        }

        GameReplicationInfo.bTeamGame = bTeamGame;
        GameReplicationInfo.GameName = GameName;
    }

    /// <summary>Stat logging exists only on a true dedicated server with logging on; this game never starts it.</summary>
    [Uc("Engine.GameInfo.InitLogging", Sha1 = "1e59a4c804d80b1f4da5c85520e9b44cffab0fd2")]
    [PortOf("uc:Engine.GameInfo:InitLogging", Spec = "spec/game/match-lifecycle.md#rules")]
    public virtual void InitLogging()
    {
        if (!bEnableStatLogging || !bLoggingGame || Level.NetMode == ENetMode.NM_Standalone ||
            Level.NetMode == ENetMode.NM_ListenServer)
        {
            return;
        }

        GameStats = UcSpawn.Spawn(this, GameStatsClass) as GameStats;
    }

    [Uc("Engine.GameInfo.InitGame", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.GameInfo:InitGame", Spec = "spec/game/match-options.md#rules")]
    public virtual void InitGame(string Options, ref string Error)
    {
    }

    // ---- login ---------------------------------------------------------------------------------------------

    [Uc("Engine.GameInfo.Login", Sha1 = "e1e338eff805d72fd8475c5da2395c944323c319")]
    [PortOf("uc:Engine.GameInfo:Login", Spec = "spec/game/login-and-teams.md#rules")]
    public virtual PlayerController? Login(string Portal, string Options, ref string Error) => null;

    [Uc("Engine.GameInfo.AtCapacity", Sha1 = "ed9bfab6011b524ae176a07b8cfedfdc6cacf39c")]
    [PortOf("uc:Engine.GameInfo:AtCapacity", Spec = "spec/game/login-and-teams.md#rules")]
    public virtual bool AtCapacity(bool bSpectator)
    {
        if (Level.NetMode == ENetMode.NM_Standalone)
        {
            return false;
        }

        if (bSpectator)
        {
            return NumSpectators >= MaxSpectators && (Level.NetMode != ENetMode.NM_ListenServer || NumPlayers > 0);
        }

        return MaxPlayers > 0 && NumPlayers >= MaxPlayers;
    }

    [Uc("Engine.GameInfo.ChangeName", Sha1 = "300ea02851b4231297f1b99206df0b6af569b834")]
    [PortOf("uc:Engine.GameInfo:ChangeName", Spec = "spec/game/login-and-teams.md#rules")]
    public virtual void ChangeName(Controller Other, string S, bool bNameChange)
    {
        if (string.IsNullOrEmpty(S))
        {
            return;
        }

        Other.PlayerReplicationInfo?.SetPlayerName(S);
        if (!bNameChange)
        {
            return;
        }

        for (Controller? c = Level.ControllerList; c is not null; c = c.nextController)
        {
            if (c is PlayerController pc && pc.Player is UViewport)
            {
                Host.ClientCall(pc, new GameCall("ReceiveLocalizedMessage")
                {
                    Class = GameMessageClass,
                    Int = 2,
                    Pri1 = Other.PlayerReplicationInfo,
                });
            }
        }
    }

    [Uc("Engine.GameInfo.GetNumPlayers", Sha1 = "420cafedb6dbbb8195f6f330e02e4005ffc227bd")]
    [PortOf("uc:Engine.GameInfo:GetNumPlayers", Spec = "spec/game/login-and-teams.md#rules")]
    public int GetNumPlayers() => NumPlayers;

    [Uc("Engine.GameInfo.PickTeam", Sha1 = "2718e0f4ddf0d02e98abee461a60d3105bcde66d")]
    [PortOf("uc:Engine.GameInfo:PickTeam", Spec = "spec/game/login-and-teams.md#rules")]
    public virtual byte PickTeam(byte Current, Controller C) => Current;

    [Uc("Engine.GameInfo.ChangeTeam", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
    [PortOf("uc:Engine.GameInfo:ChangeTeam", Spec = "spec/game/login-and-teams.md#rules")]
    protected virtual bool ChangeTeam(Controller Other, int num, bool bNewTeam) => true;

    /// <summary>
    /// After Login: starts the match or spawns the player unless the start is delayed, then sends the HUD and
    /// scoreboard classes (looked up in SBase) and the pawn rotation.
    /// </summary>
    [Uc("Engine.GameInfo.PostLogin", Sha1 = "97a80089997117d18dc836234247138ffd04232b")]
    [PortOf("uc:Engine.GameInfo:PostLogin", Spec = "spec/game/match-lifecycle.md#rules")]
    public virtual void PostLogin(PlayerController NewPlayer)
    {
        if (!bDelayedStart)
        {
            bRestartLevel = false;
            if (bWaitingToStartMatch && !(GameReplicationInfo?.bMatchHasBegun ?? false))
            {
                StartMatch();
            }
            else
            {
                eventRestartPlayer(NewPlayer);
            }

            bRestartLevel = ClassDefaults.bRestartLevel;
        }

        ClassRef hudClass = new("SBase.SHud");
        ClassRef scoreboardClass = ScoreBoardType.IsNone ? ClassRef.None : new ClassRef("SBase." + ScoreBoardType.ToString());
        Host.ClientCall(NewPlayer, new GameCall("ClientSetHUD") { Class = hudClass, Class2 = scoreboardClass });
        if (NewPlayer.Pawn is { } pawn)
        {
            Host.ClientCall(NewPlayer, new GameCall("ClientSetRotation") { Rotation = pawn.Rotation });
        }
    }

    [Uc("Engine.GameInfo.Logout", Sha1 = "ca0798e631ecfade24d27a441affa84b49d9a3b7")]
    [PortOf("uc:Engine.GameInfo:Logout", Spec = "spec/game/login-and-teams.md#rules")]
    public virtual void Logout(Controller Exiting)
    {
        if (Exiting is PlayerController pc)
        {
            if (pc.PlayerReplicationInfo is { bOnlySpectator: true })
            {
                NumSpectators--;
            }
            else
            {
                NumPlayers--;
            }

            if (Level.NetMode == ENetMode.NM_Client)
            {
                Host.ClientCall(pc, new GameCall("ClientLeaveVoiceChat"));
            }
        }

        if (ShouldAnnounceLogout(Exiting))
        {
            BroadcastLocalizedMessage(GameMessageClass, 4, Exiting.PlayerReplicationInfo);
        }
    }

    /// <summary>Lets game rules select the departure audience without sending a second logout announcement.</summary>
    protected virtual bool ShouldAnnounceLogout(Controller exiting) =>
        !(exiting.PlayerReplicationInfo?.bOnlySpectator ?? false) &&
        Level.NetMode is ENetMode.NM_DedicatedServer or ENetMode.NM_ListenServer;

    // ---- match start ---------------------------------------------------------------------------------------

    /// <summary>
    /// Spawns every player that has no pawn (unless the game already ended), clears the result and sets
    /// bMatchHasBegun when the front end loaded the map for a match.
    /// </summary>
    [Uc("Engine.GameInfo.StartMatch", Sha1 = "7179e8cadb9ca77994b77dea388ab1eb70a50d21")]
    [PortOf("uc:Engine.GameInfo:StartMatch", Spec = "spec/game/match-lifecycle.md#rules")]
    public virtual void StartMatch()
    {
        // Every actor's MatchStarting is the empty Actor version in this game, so the AllActors pass is omitted.
        for (Controller? p = Level.ControllerList; p is not null; p = p.nextController)
        {
            if (p is PlayerController pc && p.Pawn is null)
            {
                if (bGameEnded)
                {
                    return;
                }

                if (pc.CanRestartPlayer())
                {
                    eventRestartPlayer(p);
                }
            }
        }

        bWaitingToStartMatch = false;
        if (GameReplicationInfo is { } gri)
        {
            gri.WinningTeam = 255;
            gri.WinningReason = 255;
            if (Host.IsGUIInGame(Level.ControllerList as PlayerController))
            {
                gri.bMatchHasBegun = true;
            }
        }
    }

    [Uc("Engine.GameInfo.SetPause", Sha1 = "c48389e6f7cbe858825bf04f6d65111a1e58bb09")]
    [PortOf("uc:Engine.GameInfo:SetPause", Spec = "spec/game/match-lifecycle.md#rules")]
    public virtual bool SetPause(bool bPause, PlayerController? P)
    {
        Level.Pauser = bPause ? P?.PlayerReplicationInfo : null;
        return true;
    }

    // ---- players -------------------------------------------------------------------------------------------

    [Uc("Engine.GameInfo.RestartPlayer", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.GameInfo:RestartPlayer", Spec = "spec/game/respawn-and-spawning.md#rules")]
    protected virtual void RestartPlayer(Controller aPlayer)
    {
    }

    [Uc("Engine.GameInfo.AddDefaultInventory", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.GameInfo:AddDefaultInventory", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public virtual void AddDefaultInventory(Pawn PlayerPawn)
    {
    }

    [Uc("Engine.GameInfo.ResetVisionMode", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.GameInfo:ResetVisionMode", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public virtual void ResetVisionMode(Pawn PlayerPawn)
    {
    }

    /// <summary>The Engine version is never called in this game (SBase replaces it); it picks no start.</summary>
    public virtual PlayerStart? FindPlayerStart(Controller? Player, byte InTeam = 0, string incomingName = "") => null;

    /// <summary>The Engine version is never called in this game (SBase replaces it).</summary>
    public virtual float RatePlayerStart(PlayerStart N, byte Team, Controller? Player) => 0f;

    /// <summary>The Engine version (one point per kill) is never called in this game.</summary>
    protected virtual void ScoreKill(Controller? Killer, Controller? Other)
    {
    }

    /// <summary>Kill bookkeeping; the Engine version is never called in this game (SBase replaces it).</summary>
    public virtual void Killed(Controller? Killer, Controller? Killed, Pawn? KilledPawn, SDamageType DamageType)
    {
    }

    /// <summary>The Engine version is never called in this game (SBase replaces it).</summary>
    public virtual void BroadcastDeathMessage(Controller? Killer, Controller? Other, SDamageType DamageType)
    {
    }

    /// <summary>The Engine end check is disabled in this game (SBase returns false).</summary>
    public virtual bool CheckEndGame(PlayerReplicationInfo? Winner, string Reason) => false;

    /// <summary>The Engine end path is disabled in this game (SBase makes it empty).</summary>
    public virtual void EndGame(PlayerReplicationInfo? Winner, string Reason)
    {
    }

    // ---- travel --------------------------------------------------------------------------------------------

    [Uc("Engine.GameInfo.RestartGame", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.GameInfo:RestartGame", Spec = "spec/game/round-travel.md#rules")]
    public virtual void RestartGame()
    {
    }

    /// <summary>Tells clients to follow a server travel; SBase replaces the Engine version.</summary>
    public virtual void ProcessServerTravel(string URL, bool bItems)
    {
        Level.NextSwitchCountdown = 0f;
    }

    // ---- messages ------------------------------------------------------------------------------------------

    /// <summary>
    /// Sends a localized message to every player (GameInfo.BroadcastLocalized through the BroadcastHandler).
    /// Each player receives <c>ReceiveLocalizedMessage</c>.
    /// </summary>
    [PortOf("uc:Engine.GameInfo:BroadcastLocalized", Spec = "spec/game/scoring-and-stats.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Contract only: the BroadcastHandler filter is not ported, every player receives the message")]
    public virtual void BroadcastLocalized(Actor? Sender, ClassRef Message, int Switch = 0,
        PlayerReplicationInfo? RelatedPRI_1 = null, PlayerReplicationInfo? RelatedPRI_2 = null, UObject? OptionalObject = null)
    {
        for (Controller? c = Level.ControllerList; c is not null; c = c.nextController)
        {
            if (c is PlayerController pc)
            {
                Host.ClientCall(pc, new GameCall("ReceiveLocalizedMessage")
                {
                    Class = Message,
                    Int = Switch,
                    Pri1 = RelatedPRI_1,
                    Pri2 = RelatedPRI_2,
                    Object = OptionalObject,
                    Actor = Sender,
                });
            }
        }
    }

    /// <summary>Actor.BroadcastLocalizedMessage: <see cref="BroadcastLocalized"/> with this actor as the sender.</summary>
    public void BroadcastLocalizedMessage(ClassRef MessageClass, int Switch = 0, PlayerReplicationInfo? RelatedPRI_1 = null,
        PlayerReplicationInfo? RelatedPRI_2 = null, UObject? OptionalObject = null) =>
        BroadcastLocalized(this, MessageClass, Switch, RelatedPRI_1, RelatedPRI_2, OptionalObject);

    /// <summary>
    /// The class default object (UnrealScript <c>default.X</c>): an unspawned instance of this object's script class
    /// holding the decoded defaults.
    /// </summary>
    protected GameInfo ClassDefaults => _classDefaults ??= GetUcClass().Create() as GameInfo ?? this;
}

/// <summary>Spawning from class references (UnrealScript <c>Spawn(class&lt;T&gt;)</c>), resolved through a class index.</summary>
public static class UcSpawn
{
    /// <summary>The Core and Engine classes.</summary>
    public static readonly UcClassIndex EngineIndex = new(CorePackage.Info, EnginePackage.Info);

    /// <summary>The index of the level's game rules, or <see cref="EngineIndex"/> when there are none (clients).</summary>
    public static UcClassIndex IndexFor(Actor actor) => actor.Level?.Game?.GetClassIndex() ?? EngineIndex;

    /// <summary>
    /// Creates and spawns an actor of class <paramref name="cls"/>. Location and rotation default to the spawner's.
    /// Returns null for None, an unknown or abstract class, a non-actor class, or a failed spawn.
    /// </summary>
    [UcNative("AActor::execSpawn")]
    public static Actor? Spawn(Actor spawner, ClassRef cls, Actor? owner = null, FVector? location = null,
        FRotator? rotation = null, UcClassIndex? index = null)
    {
        ArgumentNullException.ThrowIfNull(spawner);
        if (cls.IsNone || (index ?? IndexFor(spawner)).Find(cls)?.Create() is not Actor actor)
        {
            return null;
        }

        return spawner.XLevel.SpawnActor(actor, location ?? spawner.Location, rotation ?? spawner.Rotation, owner,
            spawner.Instigator);
    }
}
