using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using Godot;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using OpenScctGame.Match;

namespace OpenScctGame.Online;

/// <summary>What a player hosts: the server's name, the first match and the hosting choices.</summary>
public sealed record HostRequest
{
    public string Name { get; init; } = "OpenSCCT game";

    public LobbySetup Setup { get; init; } = new();

    public int Port { get; init; } = BuildInfo.DefaultGamePort;

    public int MaxPlayers { get; init; } = 8;

    public string? Password { get; init; }

    /// <summary>List on the master (when one is set).</summary>
    public bool Listed { get; init; } = true;

    public bool Upnp { get; init; } = true;

    public string? MasterUrl { get; init; }

    public string? InviteKey { get; init; }

    /// <summary>Only signed-in players (with the master's ticket) may join; needs a master.</summary>
    public bool RequireAccounts { get; init; }

    /// <summary>The host's signed-in account (its player goes by the claimed name), or null for a guest host.</summary>
    public HostAccount? HostAccount { get; init; }

    /// <summary>The folder each round is recorded into as a demo (docs/DEMOS.md), or null to record nothing.</summary>
    public string? DemoFolder { get; init; }

    /// <summary>Bind to loopback only (smoke tests).</summary>
    public bool LoopbackOnly { get; init; }

    /// <summary>The LAN discovery port (0: no LAN beacon).</summary>
    public int LanPort { get; init; } = BuildInfo.DefaultLanPort;
}

/// <summary>
/// A listen server from the game (docs/ONLINE.md, "Hosting from the game"): the dedicated server's online session on
/// its own thread, taking the host's own player over loopback and everyone else over UDP (and through the master's
/// introducer and relay when it is listed), the router's port mapped with UPnP. The host's player joins like any
/// other through <see cref="LocalTarget"/> with <see cref="HostKey"/>, which makes it the lobby's host.
/// </summary>
public sealed class ListenHost : IDisposable
{
    public const string LoopbackAddress = "listen-server";

    private readonly LoopbackNetwork _network;
    private readonly IClock _clock;
    private readonly ConcurrentQueue<string> _commands = new();
    private readonly ConcurrentQueue<string> _notes = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private readonly UpnpPorts _upnp = new();
    private int _localPlayers;
    private bool _disposed;

    private ListenHost(HostRequest request, OnlineServer server, LiteNetLibTransport udp, LoopbackNetwork network, IClock clock, string hostKey)
    {
        Request = request;
        Server = server;
        Udp = udp;
        _network = network;
        _clock = clock;
        HostKey = hostKey;
        _thread = new Thread(Run) { IsBackground = true, Name = "OpenSCCT listen host" };
    }

    public HostRequest Request { get; }

    /// <summary>The session; it belongs to the host thread (talk to it through <see cref="Command"/>).</summary>
    public OnlineServer Server { get; }

    public LiteNetLibTransport Udp { get; }

    /// <summary>Proves the host's own player is the host.</summary>
    public string HostKey { get; }

    /// <summary>The thread's failure, if it stopped.</summary>
    public Exception? Failure { get; private set; }

    public bool IsRunning => _thread.IsAlive;

    public int Port => Udp.LocalPort;

    /// <summary>
    /// Opens the UDP port and the session (the lobby with <paramref name="request"/>'s setup) and starts the host thread.
    /// Throws <see cref="InvalidOperationException"/> with a readable reason (the port is in use, the map is missing).
    /// </summary>
    public static ListenHost Start(HostRequest request, MapCatalog maps, NetClassRegistry classes)
    {
        ArgumentNullException.ThrowIfNull(request);
        LiteNetLibTransport udp = LiteNetLibTransport.Listen(request.Port, request.MaxPlayers + 6, request.LoopbackOnly);
        try
        {
            var clock = new StopwatchClock();
            string key = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            var network = new LoopbackNetwork();

            // The composite's first transport is the loopback the host's own player connects over.
            var composite = new CompositeTransport(network.Bind(LoopbackAddress), udp);
            var settings = new OnlineServerSettings
            {
                Name = request.Name,
                Dedicated = false,
                Profile = NetProfile.Enhanced,
                MaxPlayers = request.MaxPlayers,
                Password = string.IsNullOrEmpty(request.Password) ? null : request.Password,
                Setup = request.Setup,
                Maps = maps,
                MapSource = MapDownloads.SourceOf,
                Classes = classes,
                HostKey = key,
                Bots = OpenScct.Bots.OnlineServerBots.Factory(),
                LanPort = request.LanPort,
                LanLoopbackOnly = request.LoopbackOnly,
                RequireAccounts = request.RequireAccounts,
                DemoFolder = request.DemoFolder,
                HostAccount = request.HostAccount,
                Master = request.MasterUrl is { Length: > 0 } url
                    ? new MasterLinkSettings { Url = url, InviteKey = request.InviteKey, Listed = request.Listed }
                    : null,
            };
            var server = new OnlineServer(composite, settings, clock.NowMicroseconds, udp);
            server.Logged += line => GD.Print("SVM_HOST " + line);
            var host = new ListenHost(request, server, udp, network, clock, key);
            host._thread.Start();
            if (request.Upnp && !request.LoopbackOnly)
            {
                host._upnp.MapAsync(udp.LocalPort).ContinueWith(
                    t => host._notes.Enqueue(t.IsCompletedSuccessfully ? t.Result : "UPnP: the port mapping failed."),
                    System.Threading.Tasks.TaskScheduler.Default);
            }

            return host;
        }
        catch
        {
            udp.Dispose();
            throw;
        }
    }

    /// <summary>The host's own player's way in: a loopback connection to the session.</summary>
    public JoinTarget LocalTarget => JoinTarget.Custom(Request.Name, () =>
        _network.Bind("local-player-" + Interlocked.Increment(ref _localPlayers).ToString(CultureInfo.InvariantCulture)), LoopbackAddress);

    /// <summary>Runs an admin command on the host thread (kick, map, say...); the answer goes to the log.</summary>
    public void Command(string line) => _commands.Enqueue(line);

    /// <summary>A line for the host's lobby that does not come from the session (the UPnP result).</summary>
    public bool TryTakeNote([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? note) => _notes.TryDequeue(out note);

    private void Run()
    {
        try
        {
            Server.RunRealtime(_clock, _stop.Token, s =>
            {
                while (_commands.TryDequeue(out string? line))
                {
                    foreach (string reply in s.Execute(line))
                    {
                        GD.Print("SVM_HOST " + reply);
                    }
                }
            });
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException or ArgumentException or System.IO.IOException)
        {
            Failure = e;
            GD.PushError("The listen host stopped: " + e.Message);
        }
    }

    /// <summary>Tells the players the host closed, takes it off the master and stops the thread.</summary>
    public void Stop()
    {
        if (!_thread.IsAlive)
        {
            return;
        }

        Command("close");
        if (!_thread.Join(TimeSpan.FromSeconds(3)))
        {
            _stop.Cancel();
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _stop.Cancel();
        Server.Dispose();
        _upnp.Remove();
        _stop.Dispose();
    }
}
