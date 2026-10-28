using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>
/// The server's hearing of its sounds (spec/native/audio.md rules 4 and 5): every PlaySound the server's world raises
/// is sent to each remote player in reach as <c>ClientHearSound(Actor, Id, Sound, Location, (Volume x 1000, Radius,
/// Pitch x 1000), Attenuate)</c>, which that player's machine plays (PlayerController.ClientHearSound). A
/// PlayOwnedSound skips the player that owns the actor, whose machine played it through its own simulated call.
/// Sounds the device relay already sends (<see cref="NetDeviceEvents"/>) are left to it, and sounds the server only
/// heard itself are never sent back.
/// <para>Installed as the server world's event sink (wrapping the one there), so it sees every tick's events; the
/// host calls <see cref="RelayLate"/> before each step for the sounds of the calls received before it.</para>
/// </summary>
public sealed class NetHearSound : ISimEventSink
{
    public const string FunctionName = "ClientHearSound";

    private readonly World _world;
    private readonly NetDriver _driver;
    private readonly NetCallArgs _args = new();

    private NetHearSound(World world, NetDriver driver, ISimEventSink? next)
    {
        _world = world;
        _driver = driver;
        Next = next;
    }

    /// <summary>The sink this one passes every event on to.</summary>
    public ISimEventSink? Next { get; }

    /// <summary>ClientHearSound calls sent.</summary>
    public long Sent { get; private set; }

    /// <summary>The relay of <paramref name="world"/> (made and installed on first use; server only).</summary>
    public static NetHearSound InstallServer(World world, NetDriver driver)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(driver);
        if (world.Level.GetService<NetHearSound>() is { } existing)
        {
            return existing;
        }

        var relay = new NetHearSound(world, driver, world.EventSink);
        world.EventSink = relay;
        world.Level.SetService(relay);
        return relay;
    }

    public void OnSimEvents(long tick, ReadOnlySpan<SimEvent> events)
    {
        RelayAll(events);
        Next?.OnSimEvents(tick, events);
    }

    /// <summary>
    /// Sends the sounds the world raised since its last tick (the calls received from the players before the step):
    /// call it before each step. Sounds already sent are not sent again.
    /// </summary>
    public void RelayLate()
    {
        ReadOnlySpan<SimEvent> late = _world.Events.Late;
        if (_world.TickCount != _lateTick)
        {
            _lateTick = _world.TickCount;
            _lateTaken = 0;
        }

        if (late.Length > _lateTaken)
        {
            ReadOnlySpan<SimEvent> fresh = late[_lateTaken..];
            _lateTaken = late.Length;
            RelayAll(fresh);
        }
    }

    private long _lateTick = -1;
    private int _lateTaken;

    private void RelayAll(ReadOnlySpan<SimEvent> events)
    {
        if (_driver.IsServer && _driver.ClientConnections.Count > 0)
        {
            bool devices = _world.Level.GetService<NetDeviceEvents>() is not null;
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly SimEvent e = ref events[i];
                if (e.Kind == SimEventKind.PlaySound && !e.Heard && !e.What.IsNone)
                {
                    Relay(e, devices);
                }
            }
        }
    }

    private void Relay(in SimEvent e, bool devices)
    {
        Actor? source = e.ActorId == 0 ? null : _world.Level.FindActor(e.ActorId);
        if (source is { bDeleteMe: true })
        {
            source = null;
        }

        if (devices && NetDeviceEvents.IsRelayed(source, e))
        {
            return;
        }

        PlayerController? owner = e.Owned ? RemoteOwnerOf(source) : null;
        float radius = e.Radius == 0f ? SoundK.HearRadiusForZero : e.Radius;
        float reach = SoundK.HearRangeFactor * radius;
        for (Controller? c = _world.Level.Info.ControllerList; c is not null; c = c.nextController)
        {
            if (c is not PlayerController pc || !pc.bIsPlayer || ReferenceEquals(pc, owner) || pc.bDeleteMe ||
                _driver.OwningConnection(pc) is not { State: EConnectionState.USOCK_Open, bJoined: true })
            {
                continue;
            }

            FVector ear = pc.ViewTarget?.Location ?? pc.Pawn?.Location ?? pc.Location;
            if (!e.NoAttenuate && FVector.DistSquared(ear, e.Location) > reach * reach)
            {
                continue;
            }

            if (Send(pc, source, e))
            {
                Sent++;
            }
        }
    }

    /// <summary>The player whose machine played a PlayOwnedSound itself: the actor, its owner or its pawn's controller.</summary>
    private static PlayerController? RemoteOwnerOf(Actor? source) => source switch
    {
        null => null,
        PlayerController pc => pc,
        _ when source.Owner is PlayerController owner => owner,
        _ when source.Owner is Pawn { Controller: PlayerController pawnOwner } => pawnOwner,
        Pawn { Controller: PlayerController controller } => controller,
        _ => source.Instigator?.Controller as PlayerController,
    };

    private bool Send(PlayerController pc, Actor? source, in SimEvent e)
    {
        RepFunction? f = _driver.Classes.Find(pc.GetType())?.Layout.FindFunction(FunctionName);
        if (f is null)
        {
            return false;
        }

        _args.Clear();
        int id = (e.ActorId * 16) + ((e.Slot & 7) * 2) + (e.NoOverride ? 1 : 0);
        foreach (RepParam p in f.Parameters)
        {
            switch (p.Name)
            {
                case "Actor":
                    _args.Object(p.ValueOffset, source);
                    break;
                case "Id":
                    _args.Int(p.ValueOffset, id);
                    break;
                case "S":
                    _args.Values[p.ValueOffset].O = e.What.ToString();
                    break;
                case "SoundLocation":
                    _args.Vector(p.ValueOffset, e.Location);
                    break;
                case "Parameters":
                    _args.Vector(p.ValueOffset, new FVector(e.Volume * SoundK.HearParameterScale, e.Radius, e.Pitch * SoundK.HearParameterScale));
                    break;
                case "Attenuate":
                    _args.Bool(p.ValueOffset, !e.NoAttenuate);
                    break;

                // BoneIdx stays 0: the events name bones and the server has no skeleton to number them, so a heard
                // copy follows the actor; the client's own simulated call of a bone sound follows the bone.
            }
        }

        return _driver.ProcessRemoteFunction(pc, FunctionName, _args) == ENetCallResult.Sent;
    }
}
