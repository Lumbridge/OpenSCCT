using System;
using System.Collections.Generic;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>
/// A synthetic level for device tests: actors are added during a map load (so bNoDelete devices can be placed),
/// optional box colliders for mesh actors (movers, gameplay objects) and a floor, then the device services are
/// installed and begin-play runs.
/// </summary>
internal sealed class DeviceScene
{
    private readonly List<(Actor Actor, FVector Min, FVector Max, ushort Variants)> _colliders = new();
    private readonly TestVariants _variants = new();
    private bool _floor;

    private DeviceScene(ENetMode mode, ulong seed)
    {
        World = new World(seed, mode);
        Animations = new MeshAnimations();
        Hooks = new TestHooks();
        World.Level.BeginLoad();
    }

    public World World { get; }

    public Level Level => World.Level;

    public MeshAnimations Animations { get; }

    public TestHooks Hooks { get; }

    public DeviceNatives Natives { get; private set; } = null!;

    public PhysicsDriver? Driver { get; private set; }

    public CollisionWorld? Collision => Driver?.Collision;

    public SoundLog Sounds { get; } = new();

    public float Time => Level.TimeSeconds;

    /// <summary>Starts a scene; add actors, then call <see cref="Begin"/>.</summary>
    public static DeviceScene Create(ENetMode mode = ENetMode.NM_Standalone, ulong seed = 1) => new(mode, seed);

    /// <summary>Places <paramref name="actor"/> at map load.</summary>
    public T Add<T>(T actor, FVector location = default, FRotator rotation = default, Name tag = default)
        where T : Actor
    {
        T placed = Level.SpawnActor(actor, location, rotation) ?? throw new InvalidOperationException($"{actor} did not spawn.");
        if (!tag.IsNone)
        {
            placed.Tag = tag;
        }

        return placed;
    }

    /// <summary>
    /// Gives <paramref name="actor"/> a box collider (local bounds) that follows it, used by the mesh states in
    /// <paramref name="variants"/> (0: no variant data).
    /// </summary>
    public void Collider(Actor actor, FVector localMin, FVector localMax, ushort variants = 0) =>
        _colliders.Add((actor, localMin, localMax, variants));

    /// <summary>Adds a floor slab whose top is at z = 0.</summary>
    public void Floor() => _floor = true;

    /// <summary>Builds the collision (when any), installs the device services and runs begin-play.</summary>
    public DeviceScene Begin()
    {
        if (_floor || _colliders.Count > 0)
        {
            var b = new CollisionDataBuilder();
            if (_floor)
            {
                b.AddBox(new FVector(-8000f, -8000f, -64f), new FVector(8000f, 8000f, 0f));
            }

            var owners = new Dictionary<string, Actor>();
            foreach ((Actor actor, FVector min, FVector max, _) in _colliders)
            {
                string id = "A" + actor.ActorId;
                owners[id] = actor;
                b.AddBoxHullCollider(ColliderKind.Mover, id, CollisionDataBuilder.WorldFlags, min, max, actor.Location, actor.Rotation);
            }

            CollisionWorld cw = CollisionWorld.Build(b.Build());
            cw.BindLevel(Level, id => owners.TryGetValue(id, out Actor? a) ? a : null);
            Driver = new PhysicsDriver(cw);
            Level.PhysicsDriver = Driver;
            var indexes = new List<int>();
            foreach ((Actor actor, _, _, ushort variants) in _colliders)
            {
                indexes.Clear();
                cw.GetActorColliders(actor, indexes);
                foreach (int ci in indexes)
                {
                    if (variants != 0)
                    {
                        _variants.Masks[ci] = variants;
                    }
                }
            }
        }

        Natives = DeviceNatives.Install(Level, Driver, Animations, Hooks, _variants);
        World.EventSink = Sounds;
        Level.EndLoad();
        return this;
    }

    /// <summary>Spawns an actor after the load.</summary>
    public T Spawn<T>(T actor, FVector location = default, FRotator rotation = default, Name tag = default)
        where T : Actor
    {
        T placed = Level.SpawnActor(actor, location, rotation) ?? throw new InvalidOperationException($"{actor} did not spawn.");
        if (!tag.IsNone)
        {
            placed.Tag = tag;
        }

        return placed;
    }

    /// <summary>A player pawn of type <typeparamref name="TPawn"/> with its recording controller.</summary>
    public TPawn Player<TPawn>(FVector location, bool load = false)
        where TPawn : SBasePawn, new()
    {
        var pawn = new TPawn();
        pawn.Physics = EPhysics.PHYS_None;
        TPawn placed = load ? Add(pawn, location) : Spawn(pawn, location);
        var pc = new TestController();
        TestController controller = load ? Add(pc, location) : Spawn(pc, location);
        controller.Pawn = placed;
        controller.SBPawn = placed;
        controller.bIsPlayer = true;
        controller.SPawnType = placed.SPawnType;
        controller.m_camera = load ? Add(new SPlayerCam(), location) : Spawn(new SPlayerCam(), location);
        placed.Controller = controller;
        placed.Instigator = placed;
        Collision?.NotifyCollisionChanged();
        return placed;
    }

    public void Step(int ticks = 1) => World.Step(ticks);

    /// <summary>Steps until <paramref name="done"/> or the tick budget runs out; returns the ticks run.</summary>
    public int StepUntil(Func<bool> done, int maxTicks = 6000)
    {
        int n = 0;
        while (!done() && n < maxTicks)
        {
            World.Step();
            n++;
        }

        return n;
    }

    /// <summary>Steps for <paramref name="seconds"/> of game time (whole ticks).</summary>
    public void StepSeconds(float seconds) => World.Step((int)MathF.Round(seconds * Sim.Hz));
}

/// <summary>Collider mesh-state masks by collider index.</summary>
internal sealed class TestVariants : IColliderVariants
{
    public readonly Dictionary<int, ushort> Masks = new();

    public bool TryGetVariants(int ColliderIndex, out ushort Variants) => Masks.TryGetValue(ColliderIndex, out Variants);
}

/// <summary>A pawn of either side that records damage and loses health.</summary>
internal interface IDamageLog
{
    List<(int Damage, Pawn? Instigator, SDamageType Type)> Damage { get; }
}

internal sealed class TestSpy : SPawnAttaque, IDamageLog
{
    public List<(int Damage, Pawn? Instigator, SDamageType Type)> Damage { get; } = new();

    protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0)
    {
        this.Damage.Add((Damage, EventInstigator, DamageType));
        Health -= Damage;
    }
}

internal sealed class TestMerc : SPAWNDEFENSE, IDamageLog
{
    public List<(int Damage, Pawn? Instigator, SDamageType Type)> Damage { get; } = new();

    protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0)
    {
        this.Damage.Add((Damage, EventInstigator, DamageType));
        Health -= Damage;
    }
}

/// <summary>A player controller with no state of its own that records the device calls it gets.</summary>
internal sealed class TestController : SPlayerController
{
    public TestController()
    {
        InitialState = "TestNoState";
    }

    public readonly List<(int Damage, Pawn? Instigator, SDamageType Type)> PawnDamage = new();
    public readonly List<(Actor? Actor, float Loudness)> Noises = new();
    public readonly List<(int Strength, int Speed, int FadeOut, bool Tilt, bool Shake)> Shakes = new();

    public override void PawnTakeDamage(int Damage, Pawn? instigatedBy, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0, int PercentDamageRefillable = 0)
    {
        PawnDamage.Add((Damage, instigatedBy, DamageType));
        if (Pawn is not null)
        {
            Pawn.Health -= Damage;
        }
    }

    public override void ClientNotifyNoise(Actor? Actor, float Loudness) => Noises.Add((Actor, Loudness));

    public override void ClientShake(int strenght, int speed, int FadeOut, bool bTilt, bool bShake, bool bRumble = false)
    {
        Shakes.Add((strenght, speed, FadeOut, bTilt, bShake));
        base.ClientShake(strenght, speed, FadeOut, bTilt, bShake, bRumble);
    }
}

/// <summary>Records the Trigger and UnTrigger calls its Tag receives, with the time and arguments.</summary>
internal sealed class EventProbe : Actor
{
    public readonly List<(float Time, long Tick, bool Trigger, Actor? Other, Pawn? Instigator)> Calls = new();

    protected override void Trigger(Actor? Other, Pawn? EventInstigator) =>
        Calls.Add((XLevel.TimeSeconds, XLevel.TickCount, true, Other, EventInstigator));

    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator) =>
        Calls.Add((XLevel.TimeSeconds, XLevel.TickCount, false, Other, EventInstigator));
}

/// <summary>A plain dynamic actor to send events from; <see cref="FireNextTick"/> sends one from inside its next tick.</summary>
internal sealed class Sender : Actor
{
    private Name _event;
    private Pawn? _instigator;
    private bool _armed;

    /// <summary>Time of the last scheduled send.</summary>
    public float FiredAt { get; private set; } = -1f;

    public void FireNextTick(Name eventName, Pawn? instigator)
    {
        _event = eventName;
        _instigator = instigator;
        _armed = true;
    }

    protected override void Tick(float DeltaTime)
    {
        if (_armed)
        {
            _armed = false;
            FiredAt = XLevel.TimeSeconds;
            TriggerEvent(_event, this, _instigator);
        }
    }
}

/// <summary>Collects every tick's presentation events.</summary>
internal sealed class SoundLog : ISimEventSink
{
    public readonly List<SimEvent> Events = new();

    public void OnSimEvents(long tick, ReadOnlySpan<SimEvent> events)
    {
        foreach (SimEvent e in events)
        {
            Events.Add(e);
        }
    }

    public List<SimEvent> Of(SimEventKind kind, string? what = null)
    {
        var list = new List<SimEvent>();
        foreach (SimEvent e in Events)
        {
            if (e.Kind == kind && (what is null || e.What == new Name(what)))
            {
                list.Add(e);
            }
        }

        return list;
    }
}

/// <summary>Records the HUD, message and equipment calls the devices make.</summary>
internal sealed class TestHooks : IDeviceHooks
{
    public readonly List<(SPlayerController Controller, FVector Location, bool Added)> GpoMarkers = new();
    public readonly List<(SPlayerController Controller, string Message)> TutoMessages = new();
    public readonly List<(int Team, int Switch, Actor? Object)> TeamMessages = new();
    public readonly List<Actor> Reloaded = new();
    public readonly Dictionary<SBasePawn, Actor> Gadgets = new();
    public USPlayerProfile? Profile { get; set; }

    public void AddGPOOnHud(SPlayerController Controller, FVector Location) => GpoMarkers.Add((Controller, Location, true));

    public void RemoveGPOFromHud(SPlayerController Controller, FVector Location) => GpoMarkers.Add((Controller, Location, false));

    public void AddTutoMapMessage(SPlayerController Controller, string Message) => TutoMessages.Add((Controller, Message));

    public void BroadcastTeamLocalizedMessage(int Team, ClassRef MessageClass, int Switch, Actor? OptionalObject) =>
        TeamMessages.Add((Team, Switch, OptionalObject));

    public Actor? FindGadgetType(SBasePawn Pawn, ClassRef GadgetClass) => Gadgets.GetValueOrDefault(Pawn);

    public void FullReload(Actor Item) => Reloaded.Add(Item);

    public USPlayerProfile? GetPlayerProfile() => Profile;
}

internal static class V
{
    public static FVector At(float x, float y, float z) => new(x, y, z);

    public static void Near(float expected, float actual, float tolerance) =>
        Assert.True(MathF.Abs(expected - actual) <= tolerance, $"expected {expected} +- {tolerance}, got {actual}");
}
