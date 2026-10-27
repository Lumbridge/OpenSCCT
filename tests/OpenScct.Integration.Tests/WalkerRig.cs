using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Server;

namespace OpenScct.Integration.Tests;

/// <summary>
/// A server and N clients in one thread on a virtual clock. Each frame advances the clock one 60 Hz tick, ticks
/// the server, records every walker's server location (the truth), then ticks each client with its input.
/// </summary>
internal sealed class WalkerRig : IDisposable
{
    private readonly List<ITransport> _transports = new();
    private readonly List<ClientSession> _late = new();
    private readonly Func<string, ITransport> _bind;
    private readonly ClientSettings? _clientSettings;
    private readonly Dictionary<uint, List<FVector>> _truth = new();

    /// <summary>One registry and one map catalog for every rig of the test run (both are thread safe).</summary>
    public static readonly NetClassRegistry Classes = NetClasses.Create();

    public static readonly MapCatalog Maps = MapCatalog.CreateDefault();

    private WalkerRig(VirtualClock clock, ServerSettings serverSettings, Func<string, ITransport> bind, int clientCount,
        ClientSettings? clientSettings)
    {
        Clock = clock;
        _bind = bind;
        _clientSettings = clientSettings;
        ITransport serverTransport = bind("server");
        _transports.Add(serverTransport);
        Server = new ServerHost(serverTransport, serverSettings with
        {
            Classes = serverSettings.Classes ?? Classes,
            Maps = serverSettings.Maps ?? Maps,
        });
        Clients = new ClientSession[clientCount];
        for (int i = 0; i < clientCount; i++)
        {
            ITransport transport = bind("client" + i.ToString(CultureInfo.InvariantCulture));
            _transports.Add(transport);
            ClientSettings baseSettings = clientSettings ?? new ClientSettings();
            ClientSettings settings = baseSettings with
            {
                PlayerName = "P" + i.ToString(CultureInfo.InvariantCulture),
                Classes = baseSettings.Classes ?? Classes,
                Maps = baseSettings.Maps ?? Maps,
            };
            Clients[i] = new ClientSession(transport, "server", Clock.NowMicroseconds, settings);
        }
    }

    public VirtualClock Clock { get; }

    public ServerHost Server { get; }

    public ClientSession[] Clients { get; }

    public SimLinkNetwork? SimLink { get; private set; }

    /// <summary>Clients added after the start with <see cref="AddClient"/>; stepped like the others.</summary>
    public IReadOnlyList<ClientSession> LateClients => _late;

    /// <summary>Connects one more client now (a late joiner).</summary>
    public ClientSession AddClient(string name = "Late")
    {
        ITransport transport = _bind("late" + _late.Count.ToString(CultureInfo.InvariantCulture));
        _transports.Add(transport);
        ClientSettings baseSettings = _clientSettings ?? new ClientSettings();
        var client = new ClientSession(transport, "server", Clock.NowMicroseconds, baseSettings with
        {
            PlayerName = name,
            Classes = baseSettings.Classes ?? Classes,
            Maps = baseSettings.Maps ?? Maps,
        });
        _late.Add(client);
        return client;
    }

    public long Frame { get; private set; }

    /// <summary>A rig over loopback (no <paramref name="profile"/>) or over SimLink with that profile and seed.</summary>
    public static WalkerRig Create(SimLinkProfile? profile, ulong seed, int clients, ServerSettings? server = null,
        ClientSettings? client = null)
    {
        var clock = new VirtualClock();
        if (profile is null)
        {
            var loopback = new LoopbackNetwork();
            return new WalkerRig(clock, server ?? new ServerSettings(), loopback.Bind, clients, client);
        }

        var network = new SimLinkNetwork(clock, seed, profile);
        return new WalkerRig(clock, server ?? new ServerSettings(), network.Bind, clients, client) { SimLink = network };
    }

    /// <summary>Server location of walker <paramref name="netGuid"/> at the end of server tick <paramref name="tick"/>.</summary>
    public bool TryGetTruth(uint netGuid, long tick, out FVector location)
    {
        if (_truth.TryGetValue(netGuid, out List<FVector>? history) && tick >= 0 && tick < history.Count)
        {
            location = history[(int)tick];
            return true;
        }

        location = default;
        return false;
    }

    /// <summary>Truth at a fixed-point render time, interpolated between ticks.</summary>
    public bool TryGetTruthAt(uint netGuid, long renderTimeFixed, out FVector location)
    {
        long tick = renderTimeFixed >> 8;
        float frac = (renderTimeFixed & 255) / 256f;
        if (TryGetTruth(netGuid, tick, out FVector a) && TryGetTruth(netGuid, tick + 1, out FVector b))
        {
            location = a + (b - a) * frac;
            return true;
        }

        location = default;
        return false;
    }

    /// <summary>One frame. <paramref name="input"/> gives each client's input for its next move.</summary>
    public void Step(Func<int, long, CubeInput>? input = null, Action? afterServerTick = null)
    {
        Frame++;
        Clock.AdvanceTo(VirtualClock.TickToMicroseconds(Frame, Sim.Hz));
        Server.Tick(Clock.NowMicroseconds);
        afterServerTick?.Invoke();
        RecordTruth();
        for (int i = 0; i < Clients.Length; i++)
        {
            ClientSession client = Clients[i];
            if (input is not null)
            {
                client.Input = input(i, client.LocalTick);
            }

            client.Tick(Clock.NowMicroseconds);
        }

        foreach (ClientSession late in _late)
        {
            late.Tick(Clock.NowMicroseconds);
        }
    }

    /// <summary>Server tick only (no truth recording, no clients); for allocation measurements.</summary>
    public void TickServerOnly()
    {
        Frame++;
        Clock.AdvanceTo(VirtualClock.TickToMicroseconds(Frame, Sim.Hz));
        Server.Tick(Clock.NowMicroseconds);
    }

    public void TickClients(Func<int, long, CubeInput>? input)
    {
        for (int i = 0; i < Clients.Length; i++)
        {
            ClientSession client = Clients[i];
            if (input is not null)
            {
                client.Input = input(i, client.LocalTick);
            }

            client.Tick(Clock.NowMicroseconds);
        }
    }

    private void RecordTruth()
    {
        long tick = Server.World.TickCount;
        IReadOnlyList<PlayerSlot> players = Server.Players;
        for (int i = 0; i < players.Count; i++)
        {
            CubeWalker? walker = players[i].Walker;
            if (walker is null)
            {
                continue;
            }

            uint guid = (uint)walker.ActorId;
            if (!_truth.TryGetValue(guid, out List<FVector>? history))
            {
                history = new List<FVector>();
                _truth.Add(guid, history);
            }

            while (history.Count < tick)
            {
                history.Add(history.Count > 0 ? history[^1] : walker.Location);
            }

            if (history.Count == tick)
            {
                history.Add(walker.Location);
            }
            else
            {
                history[(int)tick] = walker.Location;
            }
        }
    }

    /// <summary>Runs until every client has joined and sees <paramref name="walkers"/> walkers, or fails.</summary>
    public void WaitForAllWalkers(int walkers, int maxFrames = 600, Func<int, long, CubeInput>? input = null)
    {
        for (int n = 0; n < maxFrames; n++)
        {
            if (AllSee(walkers))
            {
                return;
            }

            Step(input);
        }

        throw new InvalidOperationException("Clients did not all see " + walkers.ToString(CultureInfo.InvariantCulture) +
            " walkers in time.");
    }

    public bool AllSee(int walkers)
    {
        foreach (ClientSession client in Clients)
        {
            if (client.State != EClientState.Joined || client.LocalWalker is null || LiveWalkers(client) != walkers)
            {
                return false;
            }
        }

        return true;
    }

    public static int LiveWalkers(ClientSession client)
    {
        int count = 0;
        foreach (ActorChannel channel in client.ActorChannels)
        {
            if (channel.Actor is CubeWalker { bDeleteMe: false })
            {
                count++;
            }
        }

        return count;
    }

    public void Dispose()
    {
        foreach (ClientSession client in Clients)
        {
            client.Dispose();
        }

        foreach (ClientSession late in _late)
        {
            late.Dispose();
        }

        Server.Dispose();
        foreach (ITransport transport in _transports)
        {
            transport.Dispose();
        }
    }
}
