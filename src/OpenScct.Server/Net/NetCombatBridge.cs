using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Provenance;

namespace OpenScct.Server;

/// <summary>
/// The combat and equipment port's calls between machines (<see cref="ICombatNet"/>) over the network driver. Each
/// <see cref="CombatCall"/> travels reliably as one hand-written function of SPlayerController,
/// <see cref="FunctionName"/>, on the channel of the sending or receiving player's controller; the receiving machine
/// hands it to its copy of that controller (<see cref="SPlayerController.ReceiveCombatCall"/>), which checks that an
/// owner-to-server call only names actors that player owns.
/// <para>
/// Wire form: the function name, the target actor, then each field behind a bit saying whether it is set (as UE2
/// writes parameters): Int1, Int2, Int3, Float1, Bool1, Vector1, Vector2 (exact floats, because the port also carries
/// non-geometric values in vectors), Rotation (exact ints), Actor1, Actor2, DamageType, Name1. Actors go through the
/// connection's object map (a dynamic actor without an acknowledged channel arrives as None).
/// </para>
/// </summary>
[Provenance(ProvenanceKind.Estimated, "One reliable net-layer function per call; the original sends each combat function on its own actor")]
public sealed class NetCombatBridge : ICombatNet
{
    /// <summary>The hand-written SPlayerController function that carries the calls.</summary>
    public const string FunctionName = "SvmCombatCall";

    private readonly NetDriver _driver;

    public NetCombatBridge(NetDriver driver)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
    }

    /// <summary>Calls sent and received, for tests and diagnostics.</summary>
    public long Sent { get; private set; }

    public long Received { get; private set; }

    /// <summary>Calls that found no channel to their player (dropped).</summary>
    public long Dropped { get; private set; }

    /// <summary>When set, every call sent and received is listed here (diagnostics).</summary>
    public List<string>? Trace { get; set; }

    /// <summary>The hand-written members SPlayerController gets on both sides (<see cref="NetClasses.Create"/>).</summary>
    public static RepExtension CreateExtension()
    {
        var extension = new RepExtension();
        extension.Functions.Add(new RepExtensionFunction(FunctionName, Reliable: true, ClientToServer: true, Signature: "combat1",
            ReceiveFromClient));
        extension.Functions.Add(new RepExtensionFunction(FunctionName + "ToClient", Reliable: true, ClientToServer: false,
            Signature: "combat1", ReceiveFromServer));
        return extension;
    }

    /// <summary>Installs a bridge on <paramref name="level"/>.</summary>
    public static NetCombatBridge Install(Level level, NetDriver driver)
    {
        ArgumentNullException.ThrowIfNull(level);
        var bridge = new NetCombatBridge(driver);
        level.SetService<ICombatNet>(bridge);
        level.SetService(bridge);
        return bridge;
    }

    public void ClientCall(PlayerController Owner, in CombatCall Call)
    {
        if (_driver.IsServer && _driver.OwningConnection(Owner) is { } connection)
        {
            Send(Owner, connection, FunctionName + "ToClient", Call);
        }
    }

    public void ServerCall(PlayerController Sender, in CombatCall Call)
    {
        if (!_driver.IsServer && _driver.ServerConnection is { } connection)
        {
            Send(Sender, connection, FunctionName, Call);
        }
    }

    /// <summary>Seconds a call may wait for the actors it names to reach the other side before it is dropped.</summary>
    public const float MaxWaitSeconds = 5f;

    private readonly List<Pending> _pending = new();

    private readonly record struct Pending(PlayerController Controller, NetConnection Connection, string Function, CombatCall Call, long QueuedAt);

    /// <summary>Calls waiting for an actor they name to be known on the other side.</summary>
    public int Waiting => _pending.Count;

    private void Send(PlayerController pc, NetConnection connection, string function, in CombatCall call)
    {
        // A call naming an actor the other side does not have yet (a pawn spawned this tick, whose channel opening is
        // not acknowledged) waits, in order, until it is: the original sends such a call on that actor's own channel,
        // which carries the actor first.
        if (HasPendingFor(connection) || !Mapped(connection, call))
        {
            _pending.Add(new Pending(pc, connection, function, call, _driver.Time));
            return;
        }

        SendNow(pc, connection, function, call);
    }

    private readonly List<Inbound> _inbound = new();

    private readonly record struct Inbound(SPlayerController Controller, CombatCall Call, bool FromOwner, long ReceivedAt);

    /// <summary>Calls received for a pawn this machine's player has not possessed yet (see <see cref="Deliver"/>).</summary>
    public int Held => _inbound.Count;

    /// <summary>
    /// Sends the waiting calls whose actors are now known and delivers the held calls whose pawn is now possessed
    /// (call once a tick, before the driver flushes).
    /// </summary>
    public void Pump()
    {
        long now = _driver.Time;
        while (_inbound.Count > 0)
        {
            Inbound held = _inbound[0];
            bool expired = now - held.ReceivedAt > (long)(MaxWaitSeconds * 1_000_000f);
            if (!held.Controller.bDeleteMe && !expired && !Possessed(held.Controller, held.Call))
            {
                break;
            }

            _inbound.RemoveAt(0);
            if (!held.Controller.bDeleteMe)
            {
                held.Controller.ReceiveCombatCall(held.Call, held.FromOwner);
            }
        }

        for (int i = 0; i < _pending.Count;)
        {
            Pending p = _pending[i];
            if (p.Connection.State == EConnectionState.USOCK_Closed || p.Controller.bDeleteMe ||
                now - p.QueuedAt > (long)(MaxWaitSeconds * 1_000_000f))
            {
                _pending.RemoveAt(i);
                Dropped++;
                Trace?.Add("expire " + p.Call.Function);
                continue;
            }

            if (!Mapped(p.Connection, p.Call) || HasPendingBefore(p.Connection, i))
            {
                i++;
                continue;
            }

            _pending.RemoveAt(i);
            SendNow(p.Controller, p.Connection, p.Function, p.Call);
        }
    }

    private bool HasPendingFor(NetConnection connection) => HasPendingBefore(connection, _pending.Count);

    private bool HasPendingBefore(NetConnection connection, int index)
    {
        for (int i = 0; i < index; i++)
        {
            if (ReferenceEquals(_pending[i].Connection, connection))
            {
                return true;
            }
        }

        return false;
    }

    private bool Mapped(NetConnection connection, in CombatCall call) =>
        Mapped(connection, call.Target) && Mapped(connection, call.Actor1) && Mapped(connection, call.Actor2);

    // Whether a reference to the actor arrives as the actor (net-replication.md rule 18).
    private bool Mapped(NetConnection connection, Actor? actor)
    {
        if (actor is null || actor.bDeleteMe)
        {
            return true;
        }

        if ((actor.bStatic || actor.bNoDelete) && _driver.MapBinding.IndexOf(actor) >= 0)
        {
            return true;
        }

        return connection.FindActorChannel(actor) is { Closing: false } channel && (channel.OpenAcked || !_driver.IsServer);
    }

    private void SendNow(PlayerController pc, NetConnection connection, string function, in CombatCall call)
    {
        ActorChannel? channel = _driver.ChannelForCall(pc, connection);
        RepFunction? f = channel?.Layout?.FindFunction(function);
        if (channel is null || f is null)
        {
            Dropped++;
            Trace?.Add("drop " + call.Function);
            return;
        }

        Trace?.Add("send " + call.Function + " target " + call.Target?.GetType().Name);
        FOutBunch bunch = channel.BeginFunction(f, reliable: true);
        Write(bunch.Writer, call, connection);
        channel.SendFunction(bunch);
        Sent++;
    }

    private static void ReceiveFromClient(Actor actor, FBitReader reader, NetConnection connection) => Receive(actor, reader, connection, true);

    private static void ReceiveFromServer(Actor actor, FBitReader reader, NetConnection connection) => Receive(actor, reader, connection, false);

    private static void Receive(Actor actor, FBitReader reader, NetConnection connection, bool fromOwner)
    {
        CombatCall call = Read(reader, connection);
        if (reader.IsError || actor is not SPlayerController { bDeleteMe: false } pc)
        {
            return;
        }

        if (pc.XLevel.GetService<NetCombatBridge>() is not { } bridge)
        {
            pc.ReceiveCombatCall(call, fromOwner);
            return;
        }

        bridge.Received++;
        bridge.Trace?.Add("recv " + call.Function + " target " + call.Target?.GetType().Name);
        bridge.Deliver(pc, call, fromOwner);
    }

    /// <summary>
    /// Hands a received call to the controller. On a client, a call aimed at the player's own pawn (an autonomous
    /// proxy here) that arrives before the controller's Pawn does is held, with every later call, until the
    /// controller possesses that pawn: the original sends such a call on the pawn's own channel, behind the pawn's
    /// Controller, so it never runs on a pawn its player does not yet own.
    /// </summary>
    private void Deliver(SPlayerController pc, in CombatCall call, bool fromOwner)
    {
        if (_inbound.Count > 0 || !Possessed(pc, call))
        {
            _inbound.Add(new Inbound(pc, call, fromOwner, _driver.Time));
            return;
        }

        pc.ReceiveCombatCall(call, fromOwner);
    }

    private static bool Possessed(SPlayerController pc, in CombatCall call) =>
        call.Target is not Pawn { Role: ENetRole.ROLE_AutonomousProxy } own || ReferenceEquals(pc.Pawn, own);

    // ---- wire form --------------------------------------------------------------------------------------------

    /// <summary>Writes <paramref name="call"/> (see the class remarks).</summary>
    public static void Write(FBitWriter w, in CombatCall call, NetConnection map)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(map);
        w.WriteString(call.Function.ToString());
        map.WriteObject(w, call.Target, out _);
        WriteInt(w, call.Int1);
        WriteInt(w, call.Int2);
        WriteInt(w, call.Int3);
        if (Flag(w, call.Float1 != 0f))
        {
            w.WriteFloat(call.Float1);
        }

        w.WriteBit(call.Bool1);
        WriteVector(w, call.Vector1);
        WriteVector(w, call.Vector2);
        bool rot = call.Rotation != default;
        if (Flag(w, rot))
        {
            w.WriteInt32(call.Rotation.Pitch);
            w.WriteInt32(call.Rotation.Yaw);
            w.WriteInt32(call.Rotation.Roll);
        }

        WriteActor(w, call.Actor1, map);
        WriteActor(w, call.Actor2, map);
        w.WriteByte((byte)call.DamageType);
        if (Flag(w, !call.Name1.IsNone))
        {
            w.WriteString(call.Name1.ToString());
        }
    }

    /// <summary>Reads a call written by <see cref="Write"/>.</summary>
    public static CombatCall Read(FBitReader r, NetConnection map)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(map);
        var function = new Name(r.ReadString());
        Actor? target = map.ReadObject(r) as Actor;
        int i1 = ReadInt(r);
        int i2 = ReadInt(r);
        int i3 = ReadInt(r);
        float f1 = r.ReadBit() ? r.ReadFloat() : 0f;
        bool b1 = r.ReadBit();
        FVector v1 = ReadVector(r);
        FVector v2 = ReadVector(r);
        FRotator rot = r.ReadBit() ? new FRotator(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()) : default;
        Actor? a1 = ReadActor(r, map);
        Actor? a2 = ReadActor(r, map);
        var damage = (SDamageType)r.ReadByte();
        Name name1 = r.ReadBit() ? new Name(r.ReadString()) : Name.None;
        return new CombatCall(function)
        {
            Target = target, Int1 = i1, Int2 = i2, Int3 = i3, Float1 = f1, Bool1 = b1, Vector1 = v1, Vector2 = v2, Rotation = rot,
            Actor1 = a1, Actor2 = a2, DamageType = damage, Name1 = name1,
        };
    }

    private static bool Flag(FBitWriter w, bool value)
    {
        w.WriteBit(value);
        return value;
    }

    private static void WriteInt(FBitWriter w, int value)
    {
        if (Flag(w, value != 0))
        {
            w.WriteInt32(value);
        }
    }

    private static int ReadInt(FBitReader r) => r.ReadBit() ? r.ReadInt32() : 0;

    private static void WriteVector(FBitWriter w, FVector v)
    {
        if (Flag(w, !v.IsZero()))
        {
            w.WriteFloat(v.X);
            w.WriteFloat(v.Y);
            w.WriteFloat(v.Z);
        }
    }

    private static FVector ReadVector(FBitReader r) => r.ReadBit() ? new FVector(r.ReadFloat(), r.ReadFloat(), r.ReadFloat()) : default;

    private static void WriteActor(FBitWriter w, Actor? actor, NetConnection map)
    {
        if (Flag(w, actor is not null))
        {
            map.WriteObject(w, actor, out _);
        }
    }

    private static Actor? ReadActor(FBitReader r, NetConnection map) => r.ReadBit() ? map.ReadObject(r) as Actor : null;
}
