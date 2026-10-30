using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net;
using OpenScct.Provenance;

namespace OpenScct.Server;

/// <summary>
/// The device events that must reach clients (spec/devices/events.md, Network): what a map device does on the server
/// shows on clients through replicated properties (handled by replication) and through calls the server sends.
/// The device ports run those calls where the server runs them and raise presentation events (<see cref="SimEvent"/>:
/// music, sounds, emitters, notifies), so this relay sends the server's presentation events of devices whose client
/// copies run no script (map actors that are dumb proxies on clients, server-only actors, music triggers) to every
/// client, which adds them to its own world's events. Music goes reliably, the rest unreliably.
/// <para>
/// Wire form (hand-written LevelInfo functions <see cref="FunctionName"/> and <see cref="ReliableFunctionName"/>):
/// kind byte, name text, source actor, location (exact floats), rotation (ints), slot, then volume, radius and
/// pitch as floats.
/// </para>
/// </summary>
[Provenance(ProvenanceKind.Estimated, "The original sends these as broadcast functions on the devices (MusicTrigger.ClientPlayStopMusic, Emitter.ReplicateTrigger) and sound replication; the rewrite relays the presentation events they produce")]
public sealed class NetDeviceEvents
{
    public const string FunctionName = "SvmSimEvent";

    public const string ReliableFunctionName = "SvmSimEventReliable";

    private readonly NetDriver _driver;
    private readonly List<SimEvent> _pending = new();

    private NetDeviceEvents(NetDriver driver)
    {
        _driver = driver;
    }

    /// <summary>Events relayed (server) or received (client).</summary>
    public long Count { get; private set; }

    /// <summary>The hand-written LevelInfo members, the same on both sides (<see cref="NetClasses.Create"/>).</summary>
    public static RepExtension CreateExtension()
    {
        var extension = new RepExtension();
        extension.Functions.Add(new RepExtensionFunction(FunctionName, Reliable: false, ClientToServer: false, Signature: "simevent1", Receive));
        extension.Functions.Add(new RepExtensionFunction(ReliableFunctionName, Reliable: true, ClientToServer: false, Signature: "simevent1",
            Receive));
        return extension;
    }

    /// <summary>The server relay: call <see cref="RelayTick"/> after each world tick.</summary>
    public static NetDeviceEvents InstallServer(Level level, NetDriver driver)
    {
        ArgumentNullException.ThrowIfNull(level);
        var relay = new NetDeviceEvents(driver);
        level.SetService(relay);
        return relay;
    }

    /// <summary>The client side: received events join the world's events of the next tick.</summary>
    public static NetDeviceEvents InstallClient(Level level, NetDriver driver)
    {
        ArgumentNullException.ThrowIfNull(level);
        var relay = new NetDeviceEvents(driver);
        level.SetService(relay);
        level.Spawn<NetEventInjector>();
        return relay;
    }

    /// <summary>True when the server must send <paramref name="source"/>'s events: its client copies run no script.</summary>
    public static bool IsRelayed(Actor? source, in SimEvent e)
    {
        if (e.Kind is SimEventKind.PlayMusic or SimEventKind.StopMusic)
        {
            return true;
        }

        if (source is null)
        {
            return e.ActorId == 0;
        }

        return source.RemoteRole < ENetRole.ROLE_SimulatedProxy && (source.bStatic || source.bNoDelete || source.RemoteRole == ENetRole.ROLE_None);
    }

    /// <summary>
    /// Server: sends this tick's relayed events to every joined client with a LevelInfo channel. Events raised after
    /// the last step (by the calls received before this one) were sent by <see cref="RelayLate"/>.
    /// </summary>
    public void RelayTick(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        Relay(world, world.Events.Current);
    }

    /// <summary>
    /// Server: sends the relayed events the world raised since its last step (a player's shot or use received before
    /// the step breaks a vase whose stage triggers its shatter emitter): call it before each step, which drops them.
    /// Events already sent are not sent again.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Same relay as RelayTick for the events raised between ticks (the original's broadcast calls are sent whenever they are made)")]
    public void RelayLate(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ReadOnlySpan<SimEvent> late = world.Events.Late;
        if (world.TickCount != _lateTick)
        {
            _lateTick = world.TickCount;
            _lateTaken = 0;
        }

        if (late.Length > _lateTaken)
        {
            ReadOnlySpan<SimEvent> fresh = late[_lateTaken..];
            _lateTaken = late.Length;
            Relay(world, fresh);
        }
    }

    private long _lateTick = -1;
    private int _lateTaken;

    private void Relay(World world, ReadOnlySpan<SimEvent> events)
    {
        if (events.Length == 0 || !_driver.IsServer)
        {
            return;
        }

        LevelInfo info = world.Level.Info;
        for (int i = 0; i < events.Length; i++)
        {
            ref readonly SimEvent e = ref events[i];
            Actor? source = e.ActorId == 0 ? null : world.Level.FindActor(e.ActorId);
            if (!IsRelayed(source, e))
            {
                continue;
            }

            bool reliable = e.Kind is SimEventKind.PlayMusic or SimEventKind.StopMusic;
            foreach (NetConnection connection in _driver.ClientConnections)
            {
                if (connection.State != EConnectionState.USOCK_Open || !connection.bJoined ||
                    connection.FindActorChannel(info) is not { Closing: false } channel ||
                    channel.Layout?.FindFunction(reliable ? ReliableFunctionName : FunctionName) is not { } f ||
                    (!reliable && !connection.IsNetReady))
                {
                    continue;
                }

                FOutBunch bunch = channel.BeginFunction(f, reliable);
                Write(bunch.Writer, e, source, connection);
                channel.SendFunction(bunch);
                Count++;
            }
        }
    }

    private static void Write(FBitWriter w, in SimEvent e, Actor? source, NetConnection map)
    {
        w.WriteByte((byte)e.Kind);
        w.WriteString(e.What.IsNone ? string.Empty : e.What.ToString());
        map.WriteObject(w, source, out _);
        w.WriteFloat(e.Location.X);
        w.WriteFloat(e.Location.Y);
        w.WriteFloat(e.Location.Z);
        w.WriteInt32(e.Rotation.Pitch);
        w.WriteInt32(e.Rotation.Yaw);
        w.WriteInt32(e.Rotation.Roll);
        w.WriteInt32(e.Slot);
        w.WriteFloat(e.Volume);
        w.WriteFloat(e.Radius);
        w.WriteFloat(e.Pitch);
    }

    private static void Receive(Actor actor, FBitReader r, NetConnection connection)
    {
        var e = new SimEvent { Kind = (SimEventKind)r.ReadByte() };
        string what = r.ReadString();
        e.What = what.Length == 0 ? Name.None : new Name(what);
        Actor? source = connection.ReadObject(r) as Actor;
        e.ActorId = source?.ActorId ?? 0;
        e.Location = new FVector(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
        e.Rotation = new FRotator(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        e.Slot = r.ReadInt32();
        e.Volume = r.ReadFloat();
        e.Radius = r.ReadFloat();
        e.Pitch = r.ReadFloat();
        if (r.IsError || actor.XLevel?.GetService<NetDeviceEvents>() is not { } relay)
        {
            return;
        }

        relay._pending.Add(e);
        relay.Count++;
    }

    /// <summary>Adds the received events to the current tick's events (called by <see cref="NetEventInjector"/>).</summary>
    internal void Inject(SimEvents events)
    {
        for (int i = 0; i < _pending.Count; i++)
        {
            events.Add(_pending[i]);
        }

        _pending.Clear();
    }
}

/// <summary>A client-local actor that puts the received device events into its world's events during the tick.</summary>
public sealed class NetEventInjector : Info
{
    public NetEventInjector()
    {
        RemoteRole = ENetRole.ROLE_None;
    }

    protected override void Tick(float DeltaTime)
    {
        XLevel.GetService<NetDeviceEvents>()?.Inject(XLevel.World.Events);
    }
}
