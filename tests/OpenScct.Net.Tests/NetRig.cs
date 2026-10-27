using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Net.Tests;

/// <summary>A server-side viewpoint for a connection that has no player controller (replication tests).</summary>
public sealed class TestViewer : Actor
{
    public TestViewer()
    {
        RemoteRole = ENetRole.ROLE_None;
        bHidden = true;
    }
}

/// <summary>A movable actor for replication tests, sent with fine movement and interpolated on clients.</summary>
public sealed class TestMover : Actor
{
    public TestMover()
    {
        RemoteRole = ENetRole.ROLE_DumbProxy;
    }

    public static RepExtension Extension()
    {
        var e = new RepExtension { InterpolateMovement = true };
        e.Properties.Add(new RepExtensionProperty("FineLocation", nameof(Location), RepLeafKind.VectorFine));
        e.Properties.Add(new RepExtensionProperty("FineRotation", nameof(Rotation), RepLeafKind.RotatorFine));
        e.Properties.Add(new RepExtensionProperty("FineVelocity", nameof(Velocity), RepLeafKind.VectorFine));
        e.Properties.Add(new RepExtensionProperty("FinePhysics", nameof(Physics), RepLeafKind.Enum));
        return e;
    }
}

/// <summary>A bNetTemporary actor: sent once, in one unreliable bunch that also closes its channel.</summary>
public sealed class TestTemporary : Actor
{
    public TestTemporary()
    {
        RemoteRole = ENetRole.ROLE_SimulatedProxy;
        bNetTemporary = true;
    }
}

/// <summary>Records what the client copy saw during its spawn events.</summary>
public sealed class TestSpawnProbe : Actor
{
    public ENetRole RoleAtPreBeginPlay;
    public ENetRole RemoteRoleAtPreBeginPlay;
    public int PostNetBeginPlays;
    public bool HiddenAtPostNetBeginPlay;

    public TestSpawnProbe()
    {
        RemoteRole = ENetRole.ROLE_SimulatedProxy;
    }

    protected override void PreBeginPlay()
    {
        RoleAtPreBeginPlay = Role;
        RemoteRoleAtPreBeginPlay = RemoteRole;
        base.PreBeginPlay();
    }

    protected override void PostNetBeginPlay()
    {
        PostNetBeginPlays++;
        HiddenAtPostNetBeginPlay = bHidden;
        base.PostNetBeginPlay();
    }
}

/// <summary>A map actor that records the broadcast and owner calls it receives.</summary>
public sealed class TestMusic : MusicTrigger
{
    public readonly List<(bool Trigger, Pawn? Instigator)> Calls = new();

    /// <summary>Runs where the broadcast arrives (hides the port so the test sees the call).</summary>
    public new void ClientPlayStopMusic(bool bTrigger, Pawn? EventInstigator) => Calls.Add((bTrigger, EventInstigator));
}

/// <summary>A server and clients over SimLink on a virtual clock; each with its world, ticked at 60 Hz.</summary>
internal sealed class NetRig : IDisposable
{
    internal sealed class Notify : INetworkNotify
    {
        private readonly NetRig _rig;

        public Notify(NetRig rig, bool server)
        {
            _rig = rig;
            IsServer = server;
        }

        public bool IsServer { get; }

        public readonly List<string> Texts = new();
        public readonly List<ControlMessage> Chats = new();
        public readonly List<NetConnection> Closed = new();
        public readonly List<ActorChannel> Opened = new();

        public void NotifyControlMessage(NetConnection connection, in ControlMessage message)
        {
            if (message.Type == EControlMessage.Hello && IsServer && message.ProtocolHash == connection.Driver.ProtocolHash)
            {
                connection.SetOpen();
                if (_rig.JoinOnHello)
                {
                    connection.Actor = _rig.ViewerFor(connection);
                    connection.bJoined = true;
                }
            }
            else if (message.Type == EControlMessage.Text && connection.State == EConnectionState.USOCK_Open)
            {
                Texts.Add(message.Text ?? string.Empty);
            }
            else if (message.Type == EControlMessage.Chat && connection.State == EConnectionState.USOCK_Open)
            {
                Chats.Add(message);
            }
        }

        public void NotifyActorChannelOpened(ActorChannel channel) => Opened.Add(channel);

        public void NotifyConnectionClosed(NetConnection connection) => Closed.Add(connection);
    }

    internal sealed class Client
    {
        public required World World;
        public required NetDriver Driver;
        public required NetConnection ToServer;
        public required Notify Notify;
        public required SimLinkTransport Transport;
        public IReadOnlyList<Actor?> MapActors = [];

        /// <summary>The client copy of a server actor.</summary>
        public T? Copy<T>(Actor serverActor)
            where T : Actor => Driver.FindActorByNetGuid((uint)serverActor.ActorId) as T;
    }

    private readonly List<SimLinkTransport> _transports = new();

    public NetRig(SimLinkProfile profile, ulong seed, int clients, Func<Level, List<Actor?>>? map = null,
        NetDriverSettings? server = null, NetDriverSettings? client = null, ENetMode mode = ENetMode.NM_DedicatedServer,
        int flushEvery = 3, Func<NetConnection, Actor>? viewer = null, NetClassRegistry? registry = null)
    {
        FlushEvery = flushEvery;
        Registry = registry ?? CreateRegistry();
        ViewerFactory = viewer;
        Network = new SimLinkNetwork(Clock, seed, profile);
        ServerWorld = new World(1, mode);
        if (map is not null)
        {
            ServerWorld.Level.BeginLoad();
            ServerMap = map(ServerWorld.Level);
            ServerWorld.Level.EndLoad();
        }

        SimLinkTransport st = Network.Bind("server");
        _transports.Add(st);
        ServerNotify = new Notify(this, server: true);
        Server = new NetDriver(st, Registry, ServerNotify, isServer: true, server) { World = ServerWorld };
        Server.MapBinding.Bind(ServerMap);
        for (int i = 0; i < clients; i++)
        {
            AddClient(map, client);
        }
    }

    public readonly VirtualClock Clock = new();
    public readonly SimLinkNetwork Network;
    public readonly World ServerWorld;
    public readonly NetDriver Server;
    public readonly Notify ServerNotify;
    public readonly List<Client> Clients = new();
    public IReadOnlyList<Actor?> ServerMap { get; } = [];
    public NetClassRegistry Registry { get; }
    public Func<NetConnection, Actor>? ViewerFactory { get; }
    public bool JoinOnHello { get; set; } = true;
    public int FlushEvery { get; }
    public long Frame;

    public static NetClassRegistry CreateRegistry()
    {
        var registry = new NetClassRegistry();
        registry.Register<TestMover>(TestMover.Extension());
        registry.Register<TestMusic>();
        registry.Register<TestViewer>();
        registry.Register<TestTemporary>();
        registry.Register<TestSpawnProbe>();
        return registry;
    }

    public Client AddClient(Func<Level, List<Actor?>>? map = null, NetDriverSettings? settings = null)
    {
        var world = new World(0, ENetMode.NM_Client);
        IReadOnlyList<Actor?> actors = [];
        if (map is not null)
        {
            world.Level.BeginLoad();
            List<Actor?> placed = map(world.Level);
            NetMapBinding.PrepareClientMap(world.Level, placed);
            world.Level.EndLoad();
            actors = placed;
        }

        SimLinkTransport t = Network.Bind("client" + Clients.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _transports.Add(t);
        var notify = new Notify(this, server: false);
        var driver = new NetDriver(t, Registry, notify, isServer: false, settings) { World = world };
        driver.MapBinding.Bind(actors);
        NetConnection toServer = driver.Connect("server", Clock.NowMicroseconds);
        toServer.ControlChannel!.Send(ControlMessage.Hello(driver.ProtocolHash, 0, "test", 10000));
        toServer.SetOpen();
        var client = new Client { World = world, Driver = driver, ToServer = toServer, Notify = notify, Transport = t, MapActors = actors };
        Clients.Add(client);
        return client;
    }

    internal Actor ViewerFor(NetConnection connection) =>
        ViewerFactory?.Invoke(connection) ?? ServerWorld.Level.Spawn<TestViewer>()!;

    /// <summary>The server side of client <paramref name="i"/>'s connection, in connection order.</summary>
    public NetConnection ServerSide(int i) => Server.ClientConnections[i];

    public void Step(int frames = 1)
    {
        for (int f = 0; f < frames; f++)
        {
            Frame++;
            Clock.AdvanceTo(VirtualClock.TickToMicroseconds(Frame, Sim.Hz));
            long now = Clock.NowMicroseconds;
            Server.TickDispatch(now);
            ServerWorld.Step();
            if (Frame % FlushEvery == 0)
            {
                Server.TickFlush(now);
            }

            foreach (Client c in Clients)
            {
                c.Driver.LocalTick = Frame;
                c.Driver.TickDispatch(now);
                c.World.Step();
                c.Driver.TickFlush(now);
            }
        }
    }

    public void Dispose()
    {
        Server.Dispose();
        foreach (Client c in Clients)
        {
            c.Driver.Dispose();
        }

        foreach (SimLinkTransport t in _transports)
        {
            t.Dispose();
        }
    }
}
