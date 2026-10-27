using System;

namespace OpenScct.Net;

/// <summary>
/// Several transports seen as one by a server driver, for a listen server that takes its local player over
/// loopback and remote players over UDP. Endpoint ids carry the transport index in their low 3 bits.
/// Server side only: <see cref="Connect"/> is not supported.
/// </summary>
public sealed class CompositeTransport : ITransport
{
    private const int IndexBits = 3;
    private const int IndexMask = (1 << IndexBits) - 1;

    private readonly ITransport[] _transports;
    private int _nextRead;

    public CompositeTransport(params ITransport[] transports)
    {
        ArgumentNullException.ThrowIfNull(transports);
        if (transports.Length is 0 or > IndexMask + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(transports), "Between 1 and 8 transports.");
        }

        _transports = (ITransport[])transports.Clone();
        int size = int.MaxValue;
        foreach (ITransport t in _transports)
        {
            ArgumentNullException.ThrowIfNull(t);
            size = Math.Min(size, t.MaxDatagramSize);
        }

        MaxDatagramSize = size;
    }

    public int MaxDatagramSize { get; }

    public EndpointId Connect(string address) =>
        throw new NotSupportedException("A composite transport only accepts connections.");

    public bool Send(EndpointId to, ReadOnlySpan<byte> datagram)
    {
        if (!to.IsValid)
        {
            return false;
        }

        int index = to.Value & IndexMask;
        return index < _transports.Length && _transports[index].Send(new EndpointId(to.Value >> IndexBits), datagram);
    }

    public bool TryReceive(Span<byte> buffer, out EndpointId from, out int length)
    {
        int count = _transports.Length;
        for (int i = 0; i < count; i++)
        {
            int index = (_nextRead + i) % count;
            if (_transports[index].TryReceive(buffer, out EndpointId inner, out length))
            {
                _nextRead = index + 1;
                from = new EndpointId((inner.Value << IndexBits) | index);
                return true;
            }
        }

        from = EndpointId.None;
        length = 0;
        return false;
    }

    public void Disconnect(EndpointId endpoint)
    {
        int index = endpoint.Value & IndexMask;
        if (endpoint.IsValid && index < _transports.Length)
        {
            _transports[index].Disconnect(new EndpointId(endpoint.Value >> IndexBits));
        }
    }

    public bool IsLocalEndpoint(EndpointId endpoint)
    {
        int index = endpoint.Value & IndexMask;
        return endpoint.IsValid && index < _transports.Length &&
            _transports[index].IsLocalEndpoint(new EndpointId(endpoint.Value >> IndexBits));
    }

    public string? RemoteAddress(EndpointId endpoint)
    {
        int index = endpoint.Value & IndexMask;
        return endpoint.IsValid && index < _transports.Length
            ? _transports[index].RemoteAddress(new EndpointId(endpoint.Value >> IndexBits))
            : null;
    }

    /// <summary>The inner transport an endpoint belongs to, and its id there.</summary>
    public (ITransport Transport, EndpointId Inner)? Resolve(EndpointId endpoint)
    {
        int index = endpoint.Value & IndexMask;
        return endpoint.IsValid && index < _transports.Length
            ? (_transports[index], new EndpointId(endpoint.Value >> IndexBits))
            : null;
    }

    /// <summary>Disposes every inner transport.</summary>
    public void Dispose()
    {
        foreach (ITransport t in _transports)
        {
            t.Dispose();
        }
    }
}
