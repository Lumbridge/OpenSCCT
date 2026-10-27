using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScct.Net;

/// <summary>
/// Link conditions for <see cref="SimLinkNetwork"/>. Latency and jitter are ping figures (round trip): each
/// direction gets half the latency and up to half the jitter either way. Loss, duplication and reordering apply
/// to each datagram in each direction; a reordered datagram is held back by an extra delay.
/// </summary>
public sealed record SimLinkProfile(
    string Name,
    int LatencyMs,
    int JitterMs,
    float LossPercent,
    float DuplicatePercent = 0f,
    float ReorderPercent = 0f,
    int ReorderDelayMs = 0)
{
    /// <summary>Perfect link: 0 ms, no loss.</summary>
    public static readonly SimLinkProfile A = new("A", 0, 0, 0f);

    /// <summary>Good internet: 60 ms ping, 1% loss.</summary>
    public static readonly SimLinkProfile B = new("B", 60, 0, 1f, DuplicatePercent: 0.5f, ReorderPercent: 0.5f, ReorderDelayMs: 20);

    /// <summary>Typical internet: 120 ms ping +-20 ms jitter, 2% loss.</summary>
    public static readonly SimLinkProfile C = new("C", 120, 20, 2f, DuplicatePercent: 1f, ReorderPercent: 1f, ReorderDelayMs: 30);

    /// <summary>Bad internet: 250 ms ping, 5% loss.</summary>
    public static readonly SimLinkProfile D = new("D", 250, 0, 5f, DuplicatePercent: 2f, ReorderPercent: 2f, ReorderDelayMs: 50);

    public static SimLinkProfile Get(string name) => name switch
    {
        "A" => A,
        "B" => B,
        "C" => C,
        "D" => D,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Known profiles are A, B, C and D."),
    };
}

/// <summary>
/// Deterministic in-process network with simulated latency, jitter, loss, duplication and reordering, driven by
/// an <see cref="IClock"/> (normally a <see cref="VirtualClock"/>). The same seed, profile and call order give
/// the same deliveries. Single-threaded.
/// </summary>
public sealed class SimLinkNetwork
{
    private readonly Dictionary<string, SimLinkTransport> _bound = new(StringComparer.Ordinal);
    private readonly Stack<byte[]> _pool = new();
    private long _sequence;

    public SimLinkNetwork(IClock clock, ulong seed, SimLinkProfile profile, int maxDatagramSize = 1500)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(profile);
        Clock = clock;
        Profile = profile;
        MaxDatagramSize = maxDatagramSize;
        Random = new SimRandom(seed, stream: 0x51, purpose: 1);
    }

    public IClock Clock { get; }

    /// <summary>Conditions applied to datagrams sent from now on.</summary>
    public SimLinkProfile Profile { get; set; }

    public int MaxDatagramSize { get; }

    internal SimRandom Random { get; }

    public long Sent { get; private set; }

    public long Lost { get; private set; }

    public long Duplicated { get; private set; }

    public long Reordered { get; private set; }

    public long Delivered { get; internal set; }

    public SimLinkTransport Bind(string address)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        if (_bound.ContainsKey(address))
        {
            throw new InvalidOperationException($"SimLink address '{address}' is already bound.");
        }

        var transport = new SimLinkTransport(this, address);
        _bound.Add(address, transport);
        return transport;
    }

    internal SimLinkTransport? Find(string address) => _bound.GetValueOrDefault(address);

    internal void Unbind(SimLinkTransport transport) => _bound.Remove(transport.Address);

    internal void Send(SimLinkTransport from, SimLinkTransport to, ReadOnlySpan<byte> datagram)
    {
        Sent++;
        SimLinkProfile p = Profile;
        if (p.LossPercent > 0f && Random.FRand() * 100f < p.LossPercent)
        {
            Lost++;
            return;
        }

        Schedule(from, to, datagram, p);
        if (p.DuplicatePercent > 0f && Random.FRand() * 100f < p.DuplicatePercent)
        {
            Duplicated++;
            Schedule(from, to, datagram, p);
        }
    }

    private void Schedule(SimLinkTransport from, SimLinkTransport to, ReadOnlySpan<byte> datagram, SimLinkProfile p)
    {
        long delay = p.LatencyMs * 1000L / 2;
        if (p.JitterMs > 0)
        {
            float spread = (Random.FRand() * 2f - 1f) * (p.JitterMs * 500f);
            delay += (long)spread;
        }

        if (p.ReorderPercent > 0f && Random.FRand() * 100f < p.ReorderPercent)
        {
            Reordered++;
            delay += p.ReorderDelayMs * 1000L;
        }

        if (delay < 0)
        {
            delay = 0;
        }

        byte[] buffer = _pool.Count > 0 ? _pool.Pop() : new byte[MaxDatagramSize];
        datagram.CopyTo(buffer);
        to.Enqueue(new SimLinkTransport.InFlight(buffer, datagram.Length, from), Clock.NowMicroseconds + delay, _sequence++);
    }

    internal void Return(byte[] buffer) => _pool.Push(buffer);
}

/// <summary>One end of a <see cref="SimLinkNetwork"/>.</summary>
public sealed class SimLinkTransport : ITransport
{
    internal readonly record struct InFlight(byte[] Data, int Length, SimLinkTransport From);

    private readonly record struct DeliveryKey(long DeliverAt, long Sequence);

    private sealed class KeyComparer : IComparer<DeliveryKey>
    {
        public static readonly KeyComparer Instance = new();

        public int Compare(DeliveryKey x, DeliveryKey y)
        {
            int c = x.DeliverAt.CompareTo(y.DeliverAt);
            return c != 0 ? c : x.Sequence.CompareTo(y.Sequence);
        }
    }

    private readonly SimLinkNetwork _network;
    private readonly PriorityQueue<InFlight, DeliveryKey> _inbound = new(256, KeyComparer.Instance);
    private readonly List<SimLinkTransport> _peers = new();
    private readonly List<bool> _closed = new();
    private readonly Dictionary<SimLinkTransport, int> _peerIds = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    internal SimLinkTransport(SimLinkNetwork network, string address)
    {
        _network = network;
        Address = address;
    }

    public string Address { get; }

    public int MaxDatagramSize => _network.MaxDatagramSize;

    /// <summary>Datagrams on their way to this transport.</summary>
    public int InFlightCount => _inbound.Count;

    private int IdOf(SimLinkTransport peer)
    {
        if (_peerIds.TryGetValue(peer, out int id))
        {
            return id;
        }

        _peers.Add(peer);
        _closed.Add(false);
        id = _peers.Count;
        _peerIds.Add(peer, id);
        return id;
    }

    internal void Enqueue(InFlight datagram, long deliverAt, long sequence)
    {
        if (_disposed)
        {
            _network.Return(datagram.Data);
            return;
        }

        _inbound.Enqueue(datagram, new DeliveryKey(deliverAt, sequence));
    }

    public EndpointId Connect(string address)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SimLinkTransport remote = _network.Find(address)
            ?? throw new InvalidOperationException($"No SimLink transport is bound to '{address}'.");
        return new EndpointId(IdOf(remote));
    }

    public bool Send(EndpointId to, ReadOnlySpan<byte> datagram)
    {
        int index = to.Value - 1;
        if (_disposed || (uint)index >= (uint)_peers.Count || _closed[index] || datagram.Length > MaxDatagramSize)
        {
            return false;
        }

        _network.Send(this, _peers[index], datagram);
        return true;
    }

    public bool TryReceive(Span<byte> buffer, out EndpointId from, out int length)
    {
        long now = _network.Clock.NowMicroseconds;
        while (_inbound.TryPeek(out InFlight datagram, out DeliveryKey key) && key.DeliverAt <= now)
        {
            _inbound.Dequeue();
            int id = IdOf(datagram.From);
            if (_closed[id - 1])
            {
                _network.Return(datagram.Data);
                continue;
            }

            datagram.Data.AsSpan(0, datagram.Length).CopyTo(buffer);
            length = datagram.Length;
            from = new EndpointId(id);
            _network.Return(datagram.Data);
            _network.Delivered++;
            return true;
        }

        from = EndpointId.None;
        length = 0;
        return false;
    }

    public void Disconnect(EndpointId endpoint)
    {
        int index = endpoint.Value - 1;
        if ((uint)index < (uint)_closed.Count && !_closed[index])
        {
            _closed[index] = true;
            _peerIds.Remove(_peers[index]);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        while (_inbound.TryDequeue(out InFlight datagram, out _))
        {
            _network.Return(datagram.Data);
        }

        _network.Unbind(this);
    }
}
