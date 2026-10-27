using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace OpenScct.Net.Discovery;

/// <summary>
/// A host's LAN beacon: a UDP socket on the fixed LAN port (<see cref="BuildInfo.DefaultLanPort"/>) that answers
/// server info queries, broadcast or direct, with the host's current <see cref="ServerInfo"/>. The socket is bound
/// with address reuse, so several hosts on one machine can listen (each LAN broadcast reaches all of them).
/// Call <see cref="Poll"/> from the host's thread.
/// </summary>
public sealed class LanBeacon : IDisposable
{
    private readonly LiteNetManager _manager;
    private readonly EventBasedLiteNetListener _listener = new();
    private readonly Func<ServerInfo> _info;
    private readonly byte[] _buffer = new byte[DiscoveryPackets.MaxBytes];
    private readonly AnswerLimiter _limiter = new();
    private long _nowMicros;
    private bool _disposed;

    private LanBeacon(Func<ServerInfo> info)
    {
        _info = info;
        _manager = new LiteNetManager(_listener)
        {
            UnconnectedMessagesEnabled = true,
            BroadcastReceiveEnabled = true,
            ReuseAddress = true,
        };
        _listener.NetworkReceiveUnconnectedEvent += OnUnconnected;
    }

    /// <summary>The port the beacon listens on.</summary>
    public int Port => _manager.LocalPort;

    /// <summary>Queries answered so far.</summary>
    public long Answered { get; private set; }

    /// <summary>
    /// Opens the beacon on <paramref name="port"/>, or returns null with the reason when the port cannot be bound (a
    /// host still runs without LAN discovery). With <paramref name="loopbackOnly"/> it binds 127.0.0.1 and ::1 only.
    /// </summary>
    public static LanBeacon? TryStart(int port, Func<ServerInfo> info, bool loopbackOnly, out string? error)
    {
        ArgumentNullException.ThrowIfNull(info);
        var beacon = new LanBeacon(info);
        bool started = loopbackOnly
            ? beacon._manager.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, port)
            : beacon._manager.Start(port);
        if (!started)
        {
            beacon.Dispose();
            error = $"LAN discovery is off: UDP port {port} could not be opened.";
            return null;
        }

        error = null;
        return beacon;
    }

    /// <summary>Answers the queries that arrived; <paramref name="nowMicros"/> is the host's clock (the answer limit).</summary>
    public void Poll(long nowMicros)
    {
        _nowMicros = nowMicros;
        if (!_disposed)
        {
            _manager.PollEvents();
        }
    }

    private void OnUnconnected(IPEndPoint from, NetPacketReader reader, UnconnectedMessageType type)
    {
        ReadOnlySpan<byte> data = reader.GetRemainingBytesSpan();
        if (DiscoveryPackets.IsQuery(data) && _limiter.Allow(from.Address, _nowMicros / 1000))
        {
            int n = DiscoveryPackets.WriteInfo(_buffer, DiscoveryPackets.NonceOf(data), _info());
            if (n > 0)
            {
                _manager.SendUnconnectedMessage(_buffer.AsSpan(0, n), from);
                Answered++;
            }
        }

        reader.Recycle();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _manager.Stop();
    }
}

/// <summary>A server that answered a query: where it answered from, where to join, its info and the ping.</summary>
public sealed record ServerQueryResult(IPEndPoint From, IPEndPoint GameEndPoint, ServerInfo Info, int PingMs, bool FromLan, string? Tag);

/// <summary>
/// The browser's query socket: searches the LAN (a broadcast to the LAN port, plus the local machine) and asks
/// single servers (from the master's list, the manual server list or a typed address) for their info, timing each
/// answer for the ping column. Unanswered queries are sent again up to <see cref="Attempts"/> times, one second apart.
/// Call <see cref="Poll"/> from the browser's thread.
/// </summary>
public sealed class ServerQuery : IDisposable
{
    private sealed class Pending
    {
        public required IPEndPoint Target { get; init; }

        public ulong Nonce { get; init; }

        public long SentAt { get; set; }

        public int Tries { get; set; }

        public bool Lan { get; init; }

        public bool Broadcast { get; init; }

        public string? Tag { get; init; }
    }

    private readonly LiteNetManager _manager;
    private readonly EventBasedLiteNetListener _listener = new();
    private readonly IClock _clock;
    private readonly bool _loopbackOnly;
    private readonly List<Pending> _pending = new();
    private readonly List<ServerQueryResult> _results = new();
    private readonly byte[] _buffer = new byte[DiscoveryPackets.QueryBytes];
    private ulong _nonce;
    private bool _disposed;

    public const int Attempts = 3;

    public const long RetryMicros = 1_000_000;

    public ServerQuery(IClock clock, bool loopbackOnly = false)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _loopbackOnly = loopbackOnly;
        _manager = new LiteNetManager(_listener) { UnconnectedMessagesEnabled = true, BroadcastReceiveEnabled = true };
        _listener.NetworkReceiveUnconnectedEvent += OnUnconnected;
        bool started = loopbackOnly ? _manager.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, 0) : _manager.Start();
        if (!started)
        {
            throw new InvalidOperationException("Could not open a UDP socket for the server browser.");
        }

        // Nonces only need to differ between queries of this socket and be hard to guess from outside.
        _nonce = (ulong)System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue) << 20;
    }

    /// <summary>Every answer so far, newest last (one per server and query round).</summary>
    public IReadOnlyList<ServerQueryResult> Results => _results;

    /// <summary>Raised from <see cref="Poll"/> for each answer.</summary>
    public event Action<ServerQueryResult>? Answered;

    /// <summary>Queries still waiting for an answer.</summary>
    public int Waiting => _pending.Count;

    /// <summary>Searches the LAN on <paramref name="lanPort"/>: a broadcast (not on a loopback-only socket) and this machine.</summary>
    public void SearchLan(int lanPort = BuildInfo.DefaultLanPort)
    {
        if (!_loopbackOnly)
        {
            Send(new IPEndPoint(IPAddress.Broadcast, lanPort), lan: true, broadcast: true, tag: null);
        }

        Send(new IPEndPoint(IPAddress.Loopback, lanPort), lan: true, broadcast: false, tag: null);
    }

    /// <summary>Asks one server (its game port) for its info; <paramref name="tag"/> comes back with the answer.</summary>
    public void QueryServer(IPEndPoint gameEndPoint, string? tag = null) => Send(gameEndPoint, lan: false, broadcast: false, tag);

    /// <summary>Forgets the answers (a new search).</summary>
    public void Clear()
    {
        _results.Clear();
        _pending.Clear();
    }

    /// <summary>Takes answers and resends unanswered queries.</summary>
    public void Poll()
    {
        if (_disposed)
        {
            return;
        }

        _manager.PollEvents();
        long now = _clock.NowMicroseconds;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            Pending p = _pending[i];
            if (now - p.SentAt < RetryMicros)
            {
                continue;
            }

            if (p.Tries >= Attempts)
            {
                _pending.RemoveAt(i);
                continue;
            }

            Transmit(p);
        }
    }

    private void Send(IPEndPoint target, bool lan, bool broadcast, string? tag)
    {
        var p = new Pending { Target = target, Nonce = ++_nonce, Lan = lan, Broadcast = broadcast, Tag = tag };
        _pending.Add(p);
        Transmit(p);
    }

    private void Transmit(Pending p)
    {
        p.Tries++;
        p.SentAt = _clock.NowMicroseconds;
        int n = DiscoveryPackets.WriteQuery(_buffer, p.Nonce);
        try
        {
            if (p.Broadcast)
            {
                _manager.SendBroadcast(_buffer, 0, n, p.Target.Port);
            }
            else
            {
                _manager.SendUnconnectedMessage(_buffer.AsSpan(0, n), p.Target);
            }
        }
        catch (SocketException)
        {
            // An unreachable network (no LAN, no IPv6) is not an error for a search.
        }
    }

    private void OnUnconnected(IPEndPoint from, NetPacketReader reader, UnconnectedMessageType type)
    {
        ReadOnlySpan<byte> data = reader.GetRemainingBytesSpan();
        if (DiscoveryPackets.TryReadInfo(data, out ulong nonce, out ServerInfo info))
        {
            long now = _clock.NowMicroseconds;
            for (int i = 0; i < _pending.Count; i++)
            {
                Pending p = _pending[i];
                if (p.Nonce != nonce)
                {
                    continue;
                }

                int ping = (int)Math.Clamp((now - p.SentAt) / 1000, 0, 9999);
                var game = new IPEndPoint(from.Address, info.GamePort > 0 ? info.GamePort : from.Port);
                var result = new ServerQueryResult(from, game, info, ping, p.Lan, p.Tag);
                int same = _results.FindIndex(r => r.GameEndPoint.Equals(game) ||
                    (p.Lan && r.FromLan && r.Info.GamePort == info.GamePort && r.Info.Name == info.Name));
                if (same >= 0)
                {
                    _results[same] = result with { GameEndPoint = _results[same].GameEndPoint, From = _results[same].From };
                }
                else
                {
                    _results.Add(result);
                }

                Answered?.Invoke(result);
                if (!p.Broadcast && !p.Lan)
                {
                    _pending.RemoveAt(i);
                }

                break;
            }
        }

        reader.Recycle();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _manager.Stop();
    }
}

/// <summary>At most <see cref="DiscoveryPackets.AnswersPerSecond"/> answers a second to one address.</summary>
public sealed class AnswerLimiter
{
    private readonly Dictionary<IPAddress, (long Second, int Count)> _counts = new();

    /// <summary>True when <paramref name="address"/> may get another answer at <paramref name="nowMilliseconds"/>.</summary>
    public bool Allow(IPAddress address, long nowMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(address);
        long second = nowMilliseconds / 1000;
        if (_counts.Count > 4096)
        {
            _counts.Clear();
        }

        (long s, int count) = _counts.GetValueOrDefault(address);
        count = s == second ? count + 1 : 1;
        _counts[address] = (second, count);
        return count <= DiscoveryPackets.AnswersPerSecond;
    }
}
