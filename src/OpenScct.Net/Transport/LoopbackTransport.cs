using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenScct.Net;

/// <summary>
/// An in-process "network" of loopback transports bound to names. A listen server binds one name and its local
/// player connects to it. Each connection gets two single-producer single-consumer rings, so the two sides may
/// run on different threads without locks on the send and receive paths.
/// </summary>
public sealed class LoopbackNetwork
{
    private readonly object _lock = new();
    private readonly Dictionary<string, LoopbackTransport> _bound = new(StringComparer.Ordinal);

    public LoopbackNetwork(int maxDatagramSize = 1500, int queueCapacity = 512)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDatagramSize, 64);
        MaxDatagramSize = maxDatagramSize;
        QueueCapacity = queueCapacity;
    }

    public int MaxDatagramSize { get; }

    /// <summary>Datagrams each direction of a link can hold (a power of two).</summary>
    public int QueueCapacity { get; }

    /// <summary>Creates a transport reachable by <paramref name="address"/>.</summary>
    public LoopbackTransport Bind(string address)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        lock (_lock)
        {
            if (_bound.ContainsKey(address))
            {
                throw new InvalidOperationException($"Loopback address '{address}' is already bound.");
            }

            var transport = new LoopbackTransport(this, address);
            _bound.Add(address, transport);
            return transport;
        }
    }

    internal EndpointId Connect(LoopbackTransport from, string address)
    {
        lock (_lock)
        {
            if (!_bound.TryGetValue(address, out LoopbackTransport? remote))
            {
                return EndpointId.None;
            }

            var toRemote = new DatagramRing(QueueCapacity, MaxDatagramSize);
            var fromRemote = new DatagramRing(QueueCapacity, MaxDatagramSize);
            int id = from.AddLink(new LoopbackTransport.Link(inbound: fromRemote, outbound: toRemote));
            remote.AddLink(new LoopbackTransport.Link(inbound: toRemote, outbound: fromRemote));
            return new EndpointId(id);
        }
    }

    internal void Unbind(LoopbackTransport transport)
    {
        lock (_lock)
        {
            if (_bound.TryGetValue(transport.Address, out LoopbackTransport? bound) && ReferenceEquals(bound, transport))
            {
                _bound.Remove(transport.Address);
            }
        }
    }
}

/// <summary>One end of the in-process loopback network (see <see cref="LoopbackNetwork"/>). Never drops unless a queue fills.</summary>
public sealed class LoopbackTransport : ITransport
{
    internal sealed class Link
    {
        public Link(DatagramRing inbound, DatagramRing outbound)
        {
            Inbound = inbound;
            Outbound = outbound;
        }

        public readonly DatagramRing Inbound;
        public readonly DatagramRing Outbound;
        public volatile bool Closed;
    }

    private readonly LoopbackNetwork _network;
    private Link[] _links = [];
    private int _nextRead;
    private bool _disposed;

    internal LoopbackTransport(LoopbackNetwork network, string address)
    {
        _network = network;
        Address = address;
    }

    public string Address { get; }

    public int MaxDatagramSize => _network.MaxDatagramSize;

    /// <summary>Datagrams this transport sent that were dropped because the other side's queue was full.</summary>
    public long DroppedFull
    {
        get
        {
            long total = 0;
            foreach (Link link in Volatile.Read(ref _links))
            {
                total += link.Outbound.Dropped;
            }

            return total;
        }
    }

    // Called under the network lock. The array is replaced, never changed, so readers need no lock.
    internal int AddLink(Link link)
    {
        Link[] old = _links;
        var next = new Link[old.Length + 1];
        Array.Copy(old, next, old.Length);
        next[old.Length] = link;
        Volatile.Write(ref _links, next);
        return next.Length;
    }

    public EndpointId Connect(string address)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EndpointId id = _network.Connect(this, address);
        if (!id.IsValid)
        {
            throw new InvalidOperationException($"No loopback transport is bound to '{address}'.");
        }

        return id;
    }

    public bool Send(EndpointId to, ReadOnlySpan<byte> datagram)
    {
        Link[] links = Volatile.Read(ref _links);
        int index = to.Value - 1;
        if (_disposed || (uint)index >= (uint)links.Length)
        {
            return false;
        }

        Link link = links[index];
        return !link.Closed && link.Outbound.TryWrite(datagram);
    }

    public bool TryReceive(Span<byte> buffer, out EndpointId from, out int length)
    {
        Link[] links = Volatile.Read(ref _links);
        int count = links.Length;
        for (int i = 0; i < count && !_disposed; i++)
        {
            int index = (_nextRead + i) % count;
            Link link = links[index];
            if (!link.Closed && link.Inbound.TryRead(buffer, out length))
            {
                _nextRead = index + 1;
                from = new EndpointId(index + 1);
                return true;
            }
        }

        from = EndpointId.None;
        length = 0;
        return false;
    }

    /// <summary>Every loopback endpoint is in this process.</summary>
    public bool IsLocalEndpoint(EndpointId endpoint) => endpoint.IsValid;

    public void Disconnect(EndpointId endpoint)
    {
        Link[] links = Volatile.Read(ref _links);
        int index = endpoint.Value - 1;
        if ((uint)index < (uint)links.Length)
        {
            links[index].Closed = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Link link in Volatile.Read(ref _links))
        {
            link.Closed = true;
        }

        _network.Unbind(this);
    }
}
