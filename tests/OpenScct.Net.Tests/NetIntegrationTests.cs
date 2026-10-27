using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;

namespace OpenScct.Net.Tests;

/// <summary>Counts its script ticks (role gate tests).</summary>
public sealed class TestTicker : Actor
{
    public int Ticks;

    protected override void Tick(float DeltaTime) => Ticks++;
}

/// <summary>A concrete pawn for the Instigator test.</summary>
public sealed class TestPawn : Pawn
{
}

/// <summary>An actor class the test registry never registers.</summary>
public sealed class TestUnregistered : Actor
{
}

/// <summary>
/// The pieces the game's network integration builds on (docs/NETCODE.md): the update frequency gate with its
/// starvation guard, the resend of lost or unmapped properties, hand-written members on original classes, the
/// fallback for calls nothing ports, channels opened for a call, and the role gates of the actor tick.
/// </summary>
public sealed class NetIntegrationTests
{
    private sealed class RecordingPhysics : IActorPhysics
    {
        public readonly List<(Actor Actor, float DeltaSeconds)> Calls = new();

        public void performPhysics(Actor Actor, float DeltaSeconds) => Calls.Add((Actor, DeltaSeconds));

        public int CountFor(Actor actor) => Calls.Count(c => ReferenceEquals(c.Actor, actor));
    }

    [Fact]
    public void GuardedGateSendsSlowActorsAtTheirOwnRate()
    {
        using var rig = new NetRig(SimLinkProfile.A, 21, 0, server: new NetDriverSettings { UpdateGate = NetUpdateGate.DecodedGuarded });
        TestMover mover = rig.ServerWorld.Level.Spawn<TestMover>()!;
        mover.NetUpdateFrequency = 5f;
        rig.Step(60);
        rig.AddClient();
        rig.Step(120);
        Assert.Equal(20, rig.Server.GetMaxTickRate());
        ActorChannel channel = rig.ServerSide(0).FindActorChannel(mover)!;
        int before = channel.PropertyBunchesSent;

        // A change every frame for 2 s: the decoded gate alone sends none of it on a 20 Hz dedicated server; the
        // guard lets one update through every 1 / 5 s.
        for (int f = 1; f <= 120; f++)
        {
            mover.Rotation = new FRotator(0, f * 100, 0);
            rig.Step(1);
        }

        int sent = channel.PropertyBunchesSent - before;
        Assert.InRange(sent, 8, 12);
        rig.Step(30);
        Assert.Equal(12000, rig.Clients[0].Copy<TestMover>(mover)!.Rotation.Yaw);
    }

    [Fact]
    public void LostPropertiesGoAgainWhenTheirConditionNoLongerHolds()
    {
        using var rig = new NetRig(SimLinkProfile.A, 22, 1);
        TestMover mover = rig.ServerWorld.Level.Spawn<TestMover>()!;
        rig.Step(60);
        TestMover copy = rig.Clients[0].Copy<TestMover>(mover)!;
        Assert.Equal(ELightType.LT_None, copy.LightType);

        // LightType goes behind bNetDirty, in an unreliable bunch: the packet carrying it is lost, and nothing else
        // changes afterwards, so bNetDirty stays false. Rule 13 still appends the lost index.
        rig.Network.Profile = SimLinkProfile.A with { LossPercent = 100f };
        mover.LightType = ELightType.LT_Steady;
        rig.Step(6);
        Assert.Equal(ELightType.LT_None, copy.LightType);
        rig.Network.Profile = SimLinkProfile.A;
        rig.Step(120);
        Assert.Equal(ELightType.LT_Steady, copy.LightType);
    }

    [Fact]
    public void ExtendAddsMembersToARegisteredClassAndKeepsItsIndex()
    {
        NetClassRegistry registry = NetRig.CreateRegistry();
        ulong plain = NetRig.CreateRegistry().ComputeProtocolHash();
        NetClassInfo before = registry.Find(typeof(TestViewer))!;
        var extension = new RepExtension();
        extension.Functions.Add(new RepExtensionFunction("TestHandWritten", Reliable: true, ClientToServer: false, Signature: "t1",
            static (_, _, _) => { }));
        NetClassInfo after = registry.Extend<TestViewer>(extension);

        Assert.Equal(before.ClassIndex, after.ClassIndex);
        Assert.Equal(before.Path, after.Path);
        Assert.Same(after, registry.Find(typeof(TestViewer)));
        Assert.Same(after, registry.Get(before.ClassIndex));
        Assert.NotNull(after.Layout.FindFunction("TestHandWritten"));
        Assert.Throws<InvalidOperationException>(() => registry.Extend<TestViewer>(new RepExtension()));
        Assert.Throws<InvalidOperationException>(() => registry.Extend<TestUnregistered>(new RepExtension()));

        // Both sides must extend alike: the members are part of the protocol hash, which closes the registry.
        Assert.NotEqual(plain, registry.ComputeProtocolHash());
        Assert.Throws<InvalidOperationException>(() => registry.Extend<TestTemporary>(new RepExtension()));
    }

    [Fact]
    public void FallbackRunsCallsNothingElseHandles()
    {
        NetClassRegistry registry = NetRig.CreateRegistry();
        var pc = new PlayerController();
        RepLayout layout = registry.Find(typeof(PlayerController))!.Layout;
        var handlers = new NetFunctionHandlers();
        RepFunction? unported = layout.Functions.FirstOrDefault(f => handlers.Find(pc, f) is null);
        Assert.NotNull(unported);

        var seen = new List<string>();
        handlers.Fallback = (self, f, _, _) => seen.Add(f.Name);
        handlers.Find(pc, unported!)!(pc, unported!, new NetCallArgs(), null!);
        Assert.Equal([unported!.Name], seen);

        // A registered handler still wins over the fallback.
        var mine = new List<string>();
        handlers.Register(registry.Find(typeof(PlayerController))!.Path, unported.Name, (_, f, _, _) => mine.Add(f.Name));
        handlers.Find(pc, unported)!(pc, unported, new NetCallArgs(), null!);
        Assert.Single(mine);
        Assert.Single(seen);
    }

    [Fact]
    public void ChannelForCallOpensTheActorOnTheServerOnly()
    {
        using var rig = new NetRig(SimLinkProfile.A, 23, 1);
        rig.Step(30);
        NetConnection connection = rig.ServerSide(0);
        TestMover mover = rig.ServerWorld.Level.Spawn<TestMover>()!;
        Assert.Null(connection.FindActorChannel(mover));

        // A call naming an actor spawned this tick goes on a channel opened (and replicated) for it now.
        ActorChannel? channel = rig.Server.ChannelForCall(mover, connection);
        Assert.NotNull(channel);
        Assert.Same(channel, connection.FindActorChannel(mover));
        Assert.True(channel!.SentInitial);
        rig.Step(30);

        // A client never opens channels: it only finds the ones the server opened.
        NetRig.Client client = rig.Clients[0];
        TestMover copy = client.Copy<TestMover>(mover)!;
        Assert.Same(client.ToServer.FindActorChannel(copy), client.Driver.ChannelForCall(copy, client.ToServer));
        TestMover local = client.World.Level.Spawn<TestMover>()!;
        Assert.Null(client.Driver.ChannelForCall(local, client.ToServer));
    }

    [Fact]
    public void DumbProxiesRunNoScriptAndOnlyFallingOrRotatingPhysics()
    {
        var world = new World(0, ENetMode.NM_Client);
        var physics = new RecordingPhysics();
        world.Level.PhysicsDriver = physics;
        TestTicker dumb = world.Level.Spawn<TestTicker>()!;
        dumb.Role = ENetRole.ROLE_DumbProxy;
        dumb.Physics = EPhysics.PHYS_Projectile;
        TestTicker simulated = world.Level.Spawn<TestTicker>()!;
        simulated.Role = ENetRole.ROLE_SimulatedProxy;
        simulated.Physics = EPhysics.PHYS_Projectile;
        world.Step(10);

        Assert.Equal(0, dumb.Ticks);
        Assert.Equal(0, physics.CountFor(dumb));
        Assert.Equal(10, simulated.Ticks);
        Assert.Equal(10, physics.CountFor(simulated));

        dumb.Physics = EPhysics.PHYS_Falling;
        world.Step(1);
        Assert.Equal(1, physics.CountFor(dumb));
        Assert.Equal(0, dumb.Ticks);
    }

    [Fact]
    public void RemoteAutonomousActorsOnTheServerOnlyCatchUpWhenTheirMovesStall()
    {
        var world = new World(1, ENetMode.NM_DedicatedServer);
        var physics = new RecordingPhysics();
        world.Level.PhysicsDriver = physics;
        PlayerController pc = world.Level.Spawn<PlayerController>()!;
        pc.Player = new UPlayer();
        TestTicker moving = world.Level.Spawn<TestTicker>(owner: pc)!;
        moving.RemoteRole = ENetRole.ROLE_AutonomousProxy;
        moving.Physics = EPhysics.PHYS_Walking;
        moving.Velocity = new FVector(100f, 0f, 0f);
        world.Step(30);
        pc.ServerTimeStamp = world.Level.TimeSeconds;
        float stamped = pc.ServerTimeStamp;

        // Moves run its physics: the tick runs its script but no physics while moves keep coming (0.35 s here).
        int ticks = moving.Ticks;
        world.Step(15);
        Assert.Equal(0, physics.CountFor(moving));
        Assert.Equal(ticks + 15, moving.Ticks);

        // Past half of MaxResponseTime without a move the physics catches up once for the whole stall.
        world.Step(10);
        Assert.Equal(1, physics.CountFor(moving));
        Assert.True(physics.Calls.Single(c => ReferenceEquals(c.Actor, moving)).DeltaSeconds > 0.5f * pc.MaxResponseTime);
        Assert.True(pc.ServerTimeStamp - stamped > 0.5f * pc.MaxResponseTime, "The catch-up did not stamp the controller.");

        // A local player's actor (a viewport) or one nobody controls runs its physics every tick.
        TestTicker unowned = world.Level.Spawn<TestTicker>()!;
        unowned.RemoteRole = ENetRole.ROLE_AutonomousProxy;
        unowned.Physics = EPhysics.PHYS_Walking;
        world.Step(5);
        Assert.Equal(5, physics.CountFor(unowned));
    }

    [Fact]
    public void PawnsAreTheirOwnInstigator()
    {
        var world = new World(1, ENetMode.NM_DedicatedServer);
        TestPawn pawn = world.Level.Spawn<TestPawn>()!;
        Assert.Same(pawn, pawn.Instigator);
    }
}
