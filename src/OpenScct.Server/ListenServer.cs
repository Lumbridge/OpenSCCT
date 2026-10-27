using System;
using System.Globalization;
using System.Threading;
using OpenScct.Core.Engine;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>
/// A listen server: the dedicated <see cref="ServerHost"/> code on its own thread, with local players connected
/// over a <see cref="LoopbackTransport"/> (and remote players over an optional second transport, such as UDP).
/// The host thread owns <see cref="Host"/>; read it from elsewhere only after <see cref="Stop"/>.
/// </summary>
public sealed class ListenServer : IDisposable
{
    public const string HostAddress = "listen-server";

    private readonly LoopbackNetwork _network = new();
    private readonly ITransport _transport;
    private readonly IClock _clock;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private int _localPlayers;
    private bool _disposed;

    public ListenServer(IClock clock, ServerSettings? settings = null, ITransport? remoteTransport = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        LoopbackTransport loopback = _network.Bind(HostAddress);
        _transport = remoteTransport is null ? loopback : new CompositeTransport(loopback, remoteTransport);
        Host = new ServerHost(_transport, (settings ?? new ServerSettings()) with { NetMode = ENetMode.NM_ListenServer });
        _thread = new Thread(Run) { IsBackground = true, Name = "SVM listen server" };
        _thread.Start();
    }

    public ServerHost Host { get; }

    /// <summary>The exception that stopped the host thread, if any.</summary>
    public Exception? Failure { get; private set; }

    public bool IsRunning => _thread.IsAlive;

    /// <summary>Connects a local player over loopback. The returned session belongs to the caller's thread.</summary>
    public ClientSession ConnectLocalPlayer(long nowMicros, ClientSettings? settings = null)
    {
        int n = Interlocked.Increment(ref _localPlayers);
        LoopbackTransport transport = _network.Bind("local-player-" + n.ToString(CultureInfo.InvariantCulture));
        return new ClientSession(transport, HostAddress, nowMicros, settings, ownsTransport: true);
    }

    /// <summary>Stops the host thread and waits for it.</summary>
    public void Stop()
    {
        if (!_stop.IsCancellationRequested)
        {
            _stop.Cancel();
        }

        _thread.Join();
    }

    private void Run()
    {
        try
        {
            Host.RunRealtime(_clock, _stop.Token);
        }
        catch (Exception e)
        {
            Failure = e;
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
        Host.Dispose();
        _transport.Dispose();
        _stop.Dispose();
    }
}
