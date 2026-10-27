using System;
using System.Threading;
using Godot;
using OpenScct.Net;
using OpenScct.Server;
using OpenScct.Server.Hub;
using OpenScctGame.Match;

namespace OpenScctGame.Hub;

/// <summary>
/// A hub hosted from the game (docs/HUB.md, "Running a hub"): the dedicated server's
/// <see cref="HubServer"/> on its own thread, on a UDP port of this PC, so a player without a hub server can open one
/// and others (LAN, direct IP, or the master's hub list with its NAT relay) join it. The host's own player joins it
/// like anyone else, on 127.0.0.1.
/// </summary>
public sealed class HubHost : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private bool _disposed;

    private HubHost(HubServer server, LiteNetLibTransport udp)
    {
        Server = server;
        Udp = udp;
        _thread = new Thread(Run) { IsBackground = true, Name = "OpenSCCT hub host" };
    }

    /// <summary>The hub; it belongs to the host thread.</summary>
    public HubServer Server { get; }

    public LiteNetLibTransport Udp { get; }

    public int Port => Udp.LocalPort;

    /// <summary>Where the host's own player joins.</summary>
    public string LocalAddress => "127.0.0.1:" + Port.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The thread's failure, if it stopped.</summary>
    public Exception? Failure { get; private set; }

    public bool IsRunning => _thread.IsAlive;

    /// <summary>
    /// Opens UDP <paramref name="port"/> and the hub on <paramref name="maps"/> and starts the host thread. Throws
    /// <see cref="InvalidOperationException"/> with a readable reason (the port is in use, the hub map is missing).
    /// With <paramref name="master"/> the hub lists itself in that master's hub list.
    /// </summary>
    public static HubHost Start(int port, string name, MapCatalog maps, NetClassRegistry classes, OpenScct.Server.Online.MasterLinkSettings? master = null)
    {
        ArgumentNullException.ThrowIfNull(maps);
        LiteNetLibTransport udp = LiteNetLibTransport.Listen(port, HubK.DefaultMaxPlayers + 8, loopbackOnly: false);
        try
        {
            HubServer server;
            try
            {
                server = new HubServer(udp, new HubSettings
                {
                    Name = name,
                    Maps = maps,
                    Classes = classes,
                    MatchBots = OpenScct.Bots.OnlineServerBots.Factory(),
                    MatchClock = static () => new StopwatchClock(),
                    Master = master,
                });
            }
            catch (System.IO.IOException e)
            {
                throw new InvalidOperationException("The hub map could not be loaded: " + e.Message, e);
            }

            server.Logged += line => GD.Print("SVM_HUB " + line);
            GD.Print("SVM_HUB " + server.PadsSummary);
            var host = new HubHost(server, udp);
            host._thread.Start();
            GD.Print($"SVM_HUB hosting {name} on UDP port {udp.LocalPort}");
            return host;
        }
        catch
        {
            udp.Dispose();
            throw;
        }
    }

    private void Run()
    {
        try
        {
            Server.RunRealtime(new StopwatchClock(), _stop.Token);
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException or ArgumentException or System.IO.IOException)
        {
            Failure = e;
            GD.PushError("The hub host stopped: " + e.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        _thread.Join(TimeSpan.FromSeconds(3));
        Server.Dispose();
        Udp.Dispose();
        _stop.Dispose();
    }
}
