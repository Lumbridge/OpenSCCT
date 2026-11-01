using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.Tests.Combat;

/// <summary>
/// A bare level for combat and equipment tests: players are a pawn possessed by an SPlayerController (standalone, so
/// every call stays on this machine), with optional collision and pill pose services.
/// </summary>
internal sealed class CombatScene
{
    public CombatScene(ENetMode mode = ENetMode.NM_Standalone, ulong seed = 1)
    {
        World = new World(seed, mode);
        Level = World.Level;
        // Each parallel scene needs its own mutable hit-zone scratch buffer.
        Level.SetService<IHitZones>(new TraceHitZones(EstimatedPillPose.Instance));
    }

    public World World { get; }

    public Level Level { get; }

    public float Time => Level.TimeSeconds;

    /// <summary>A player: a spy or merc pawn at <paramref name="at"/> facing <paramref name="yaw"/>, possessed.</summary>
    public (SPlayerController PC, SBasePawn Pawn) Player(bool spy, FVector at, int yaw = 0)
    {
        SBasePawn pawn = Level.SpawnActor<SBasePawn>(spy ? new SPawnAttaque_1Mesh() : new SPawnDefense_1Mesh(), at, new FRotator(0, yaw, 0))
            ?? throw new InvalidOperationException("pawn did not spawn");
        SPlayerController pc = Level.SpawnActor(new SPlayerController(), at, default) ?? throw new InvalidOperationException("controller did not spawn");
        pc.Possess(pawn);
        pawn.Rotation = new FRotator(0, yaw, 0);
        return (pc, pawn);
    }

    /// <summary>Spawns an actor owned and instigated by <paramref name="owner"/>.</summary>
    public T Give<T>(T actor, Pawn owner)
        where T : Actor
    {
        T spawned = Level.SpawnActor(actor, owner.Location, owner.Rotation) ?? throw new InvalidOperationException($"{actor} did not spawn");
        spawned.Instigator = owner;
        spawned.SetOwner(owner);
        return spawned;
    }

    /// <summary>Runs whole ticks covering <paramref name="seconds"/>.</summary>
    public void Run(float seconds) => World.Step((int)(seconds * Sim.Hz + 0.5f));
}

/// <summary>A collision world that hits nothing (clear muzzles, free release spots, grounds everywhere).</summary>
internal sealed class EmptyWorld : ICollisionWorld
{
    public bool LineCheck(out FCheckResult Hit, Actor? SourceActor, FVector End, FVector Start, FVector Extent, ETraceFlags TraceFlags)
    {
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

/// <summary>Records the combat calls that would leave the machine.</summary>
internal sealed class RecordingCombatNet : ICombatNet
{
    public readonly List<(PlayerController To, CombatCall Call)> ToClients = new();
    public readonly List<(PlayerController From, CombatCall Call)> ToServer = new();

    public void ClientCall(PlayerController Owner, in CombatCall Call) => ToClients.Add((Owner, Call));

    public void ServerCall(PlayerController Sender, in CombatCall Call) => ToServer.Add((Sender, Call));
}
