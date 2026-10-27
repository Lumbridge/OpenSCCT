using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.GameFlow;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.Tests.Game;

/// <summary>Records every call the game rules send off the machine, and answers the host questions from settable fields.</summary>
internal sealed class RecordingGameHost : IGameHost
{
    public readonly List<(PlayerController Target, GameCall Call)> Calls = new();
    public readonly List<(PlayerController Sender, GameCall Call)> ServerCalls = new();
    public readonly List<(Actor Source, int Id)> ScriptEvents = new();

    public bool GUIInGame { get; set; }

    public string URLMap { get; set; } = "TestMap";

    public bool Psx2 { get; set; }

    public bool DedicatedServerMode { get; set; }

    public NetworkType Net { get; set; } = NetworkType.NET_NONE;

    public int Pending { get; set; }

    public void ClientCall(PlayerController target, in GameCall call) => Calls.Add((target, call));

    public void ServerCall(PlayerController sender, in GameCall call) => ServerCalls.Add((sender, call));

    public bool IsGUIInGame(PlayerController? player) => GUIInGame;

    public int NbPendingClientOnServer() => Pending;

    public string GetURLMap() => URLMap;

    public bool IsPsx2() => Psx2;

    public bool IsPBClientEnabled() => false;

    public void OnScriptEvent(Actor source, int id) => ScriptEvents.Add((source, id));

    public int GetPing(PlayerController player) => 42;

    public NetworkType NetType() => Net;

    public bool IsDedicatedServerMode() => DedicatedServerMode;

    /// <summary>The calls named <paramref name="function"/>, in order.</summary>
    public List<(PlayerController Target, GameCall Call)> Named(string function)
    {
        var list = new List<(PlayerController, GameCall)>();
        var name = new Name(function);
        foreach ((PlayerController target, GameCall call) in Calls)
        {
            if (call.Function == name)
            {
                list.Add((target, call));
            }
        }

        return list;
    }

    /// <summary>The calls named <paramref name="function"/> sent to <paramref name="target"/>.</summary>
    public List<GameCall> To(PlayerController target, string function)
    {
        var list = new List<GameCall>();
        foreach ((PlayerController t, GameCall call) in Named(function))
        {
            if (ReferenceEquals(t, target))
            {
                list.Add(call);
            }
        }

        return list;
    }

    /// <summary>The ReceiveLocalizedMessage calls with message switch <paramref name="Switch"/>.</summary>
    public List<(PlayerController Target, GameCall Call)> Messages(int Switch)
    {
        var list = new List<(PlayerController, GameCall)>();
        foreach ((PlayerController t, GameCall call) in Named("ReceiveLocalizedMessage"))
        {
            if (call.Int == Switch)
            {
                list.Add((t, call));
            }
        }

        return list;
    }
}

/// <summary>Records the calls into other areas.</summary>
internal sealed class RecordingPorts : IGameFlowPorts
{
    public readonly List<string> Log = new();
    public readonly List<(SPlayerController PC, bool Spy)> StartStates = new();
    public readonly List<(PlayerController Listener, PlayerController Speaker, bool Mute)> Voice = new();

    public void AddDefaultInventory(SBasePawn Pawn, EGameMode Mode) => Log.Add("AddDefaultInventory " + Mode);

    public void DeleteAllInventory(SBasePawn Pawn) => Log.Add("DeleteAllInventory");

    public void ResetVisionMode(SPlayerController PC) => Log.Add("ResetVisionMode");

    public void CountPoisonDrain(SPlayerController PC) => Log.Add("CountPoisonDrain");

    public void EnterStartState(SPlayerController PC, SBasePawn Pawn, bool bSpy) => StartStates.Add((PC, bSpy));

    public void BeginDeath(SPlayerController PC) => Log.Add("BeginDeath");

    public void EndDeath(SPlayerController PC) => Log.Add("EndDeath");

    public void SpawnMagmaHud(SPlayerController PC) => Log.Add("SpawnMagmaHud");

    public void ZoneTipTouch(SZoneTips Tip, Pawn Pawn) => Log.Add("ZoneTipTouch");

    public void PlayWaiting(Pawn Pawn) => Log.Add("PlayWaiting");

    public void PlayTeleportEffect(Pawn Pawn, bool bOut, bool bSound) => Log.Add("PlayTeleportEffect");

    public void NativePlayerInput(SPlayerController PC, float DeltaTime)
    {
    }

    public void UpdateVoiceChatMuting(SGameInfo Game, bool bInit) => Log.Add("UpdateVoiceChatMuting");

    public void HearVoiceChatter(SGameInfo Game, PlayerController Listener, PlayerController Speaker, bool Mute) =>
        Voice.Add((Listener, Speaker, Mute));

    public int Count(string entry) => Log.FindAll(e => e == entry).Count;
}

/// <summary>A flat floor at <see cref="FloorZ"/>: box traces going down through it hit it, nothing else is hit.</summary>
internal sealed class FlatFloor : ICollisionWorld
{
    public FlatFloor(float floorZ)
    {
        FloorZ = floorZ;
    }

    public float FloorZ { get; }

    public readonly List<(FVector Start, FVector End, FVector Extent, ETraceFlags Flags)> Traces = new();

    public bool LineCheck(out FCheckResult Hit, Actor? SourceActor, FVector End, FVector Start, FVector Extent, ETraceFlags TraceFlags)
    {
        Traces.Add((Start, End, Extent, TraceFlags));
        float bottomStart = Start.Z - Extent.Z;
        float bottomEnd = End.Z - Extent.Z;
        if (bottomStart >= FloorZ && bottomEnd <= FloorZ && bottomStart != bottomEnd)
        {
            float t = (bottomStart - FloorZ) / (bottomStart - bottomEnd);
            Hit = FCheckResult.NoHit(End);
            Hit.Time = t;
            Hit.Location = new FVector(Start.X, Start.Y, FloorZ + Extent.Z);
            Hit.Normal = new FVector(0f, 0f, 1f);
            Hit.Actor = SourceActor?.Level;
            return true;
        }

        Hit = FCheckResult.NoHit(End);
        return false;
    }

    public int MultiLineCheck(List<FCheckResult> Hits, Actor? SourceActor, FVector End, FVector Start, FVector Extent, ETraceFlags TraceFlags)
    {
        Hits.Clear();
        return 0;
    }

    public bool PointCheck(out FCheckResult Hit, Actor? SourceActor, FVector Location, FVector Extent, ETraceFlags TraceFlags)
    {
        Hit = FCheckResult.NoHit(Location);
        return false;
    }

    public bool FastTrace(FVector End, FVector Start) => true;

    public int GetZoneIndex(FVector Location) => 0;

    public ZoneInfo? GetZoneInfo(FVector Location) => null;
}

/// <summary>
/// A synthetic map for game-flow tests: player starts placed during the load, the SBase game rules spawned with URL
/// options, a recording host and recording ports installed.
/// </summary>
internal sealed class GameWorld
{
    public GameWorld(ENetMode netMode = ENetMode.NM_ListenServer, ulong seed = 1)
    {
        World = new World(seed, netMode);
        Level = World.Level;
        Level.SetService<IGameHost>(Host);
        Level.SetService<IGameFlowPorts>(Ports);
    }

    public World World { get; }

    public Level Level { get; }

    public RecordingGameHost Host { get; } = new();

    public RecordingPorts Ports { get; } = new();

    public SGameInfo Game { get; private set; } = null!;

    public GameReplicationInfo Gri => Game.GameReplicationInfo!;

    public TeamInfo Spies => Gri.Teams[0]!;

    public TeamInfo Mercs => Gri.Teams[1]!;

    /// <summary>Loads the map: <paramref name="place"/> spawns map actors, then the game rules spawn with <paramref name="options"/>.</summary>
    public GameWorld Load(string options, Action<GameWorld>? place = null)
    {
        Level.BeginLoad();
        place?.Invoke(this);
        string error = string.Empty;
        Game = GameInfo.SpawnGameInfo<SGameInfo>(Level, options, ref error);
        Level.EndLoad();
        return this;
    }

    /// <summary>A player start (map load only).</summary>
    public T AddStart<T>(T start, FVector location, byte team, byte priority = 0, bool enabled = true,
        bool mercDeathMatch = false, int yaw = 0)
        where T : PlayerStart
    {
        start.TeamNumber = team;
        start.Priority = priority;
        start.bEnabled = enabled;
        start.bMercDeathMatchStart = mercDeathMatch;
        return Level.SpawnActor(start, location, new FRotator(0, yaw, 0)) ?? throw new InvalidOperationException("start did not spawn");
    }

    public PlayerStart AddStart(FVector location, byte team, byte priority = 0, bool enabled = true,
        bool mercDeathMatch = false, int yaw = 0) =>
        AddStart(new PlayerStart(), location, team, priority, enabled, mercDeathMatch, yaw);

    /// <summary>A player joins (Login, then PostLogin); fails the test when the login fails.</summary>
    public SPlayerController Join(string options, bool local = false)
    {
        string error = string.Empty;
        PlayerController? pc = Game.SpawnPlayActor(string.Empty, options, local, ref error);
        if (pc is not SPlayerController spc)
        {
            throw new InvalidOperationException("login failed: " + error);
        }

        return spc;
    }

    /// <summary>A login that may fail: the controller (or null) and the error text.</summary>
    public (PlayerController? Player, string Error) TryJoin(string options, bool local = false)
    {
        string error = string.Empty;
        PlayerController? pc = Game.SpawnPlayActor(string.Empty, options, local, ref error);
        return (pc, error);
    }

    /// <summary>The network start: every player is waiting, the listen host calls StartMatchOnServer.</summary>
    public void StartNetworkMatch(SPlayerController host)
    {
        Game.bDelayedStart = false;
        host.StartMatchOnServer();
    }

    /// <summary>Runs whole ticks covering <paramref name="seconds"/>.</summary>
    public void Run(float seconds) => World.Step((int)(seconds * Sim.Hz + 0.5f));

    /// <summary>Runs ticks, calling PlayerTick on every local player each tick (as the engine does before the actor tick).</summary>
    public void RunWithPlayerTick(float seconds, params SPlayerController[] players)
    {
        int ticks = (int)(seconds * Sim.Hz + 0.5f);
        for (int i = 0; i < ticks; i++)
        {
            foreach (SPlayerController pc in players)
            {
                pc.eventPlayerTick(Sim.DeltaTime);
            }

            World.Step();
        }
    }

    /// <summary>A pawn of an SBase class (spy or merc), placed at <paramref name="location"/>.</summary>
    public SBasePawn SpawnPawn(bool spy, FVector location)
    {
        SBasePawn pawn = spy ? new SPawnAttaque_1Mesh() : new SPawnDefense_1Mesh();
        return Level.SpawnActor(pawn, location, default) ?? throw new InvalidOperationException("pawn did not spawn");
    }
}

/// <summary>A player controller that records the damage it is asked to take.</summary>
internal sealed class DamageRecordingController : SPlayerController
{
    public readonly List<(int Damage, Pawn? By, FVector HitLocation, FVector Momentum, SDamageType Type, int Pill)> Hits = new();

    public override void PawnTakeDamage(int Damage, Pawn? instigatedBy, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0, int PercentDamageRefillable = 0) =>
        Hits.Add((Damage, instigatedBy, HitLocation, Momentum, DamageType, PillTag));
}

internal static class Classes
{
    public static readonly ClassRef SpyPawn = new("SBase.SPawnAttaque_1Mesh");
    public static readonly ClassRef MercPawn = new("SBase.SPawnDefense_1Mesh");
}

internal static class GameFlowIteratorExtensions
{
    public static List<T> ToList<T>(this ActorIterator<T> iterator)
        where T : Actor
    {
        var list = new List<T>();
        foreach (T a in iterator)
        {
            list.Add(a);
        }

        return list;
    }
}
