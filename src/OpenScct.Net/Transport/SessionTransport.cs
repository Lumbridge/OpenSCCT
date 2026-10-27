using System;

namespace OpenScct.Net;

/// <summary>
/// One socket kept for a whole online visit while the connections on it change with every map: the lobby, each
/// round and the lobby again each run their own driver and connection, as a UE2 client reconnects on
/// <c>ClientTravel</c>. Each driver gets a <see cref="Generation"/> of the transport; only the newest one receives,
/// a retired one neither sends nor receives, and disconnects of endpoints are held back while the visit goes on,
/// so the remote peer (a LiteNetLib connection, a punched NAT mapping, a relay session) survives the change of map.
/// <para>Travel rule (docs/ONLINE.md, "Travel"): the server retires the old generation before the new one starts
/// and at most <c>TravelGraceSeconds</c> after it told the clients to travel; a client waits longer than that before
/// it starts its next connection and drops whatever arrived meanwhile (<see cref="Drain"/>), so no late datagram of
/// the old connection can reach the new one.</para>
/// </summary>
public sealed class SessionTransport : IDisposable
{
    private readonly ITransport _inner;
    private readonly bool _ownsInner;
    private readonly byte[] _drain;
    private Generation? _current;
    private bool _disposed;

    public SessionTransport(ITransport inner, bool ownsInner = true)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _ownsInner = ownsInner;
        _drain = new byte[inner.MaxDatagramSize];
    }

    /// <summary>The transport underneath (the real socket).</summary>
    public ITransport Inner => _inner;

    /// <summary>The generation that receives now, or null before the first.</summary>
    public Generation? Current => _current;

    /// <summary>
    /// Retires the current generation and starts a new one for the next driver. With
    /// <paramref name="holdDisconnects"/> the new generation never disconnects remote endpoints (a client keeps its
    /// one server; a server holds its players across a travel and lets go once they leave for good).
    /// </summary>
    public Generation NewGeneration(bool holdDisconnects = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _current?.Retire();
        _current = new Generation(this, holdDisconnects);
        return _current;
    }

    /// <summary>Drops every datagram waiting on the socket (late ones of a retired connection); returns how many.</summary>
    public int Drain()
    {
        int n = 0;
        while (_inner.TryReceive(_drain, out _, out _))
        {
            n++;
        }

        return n;
    }

    /// <summary>Disconnects an endpoint on the socket now, whatever the generations hold (a player who left for good).</summary>
    public void DisconnectNow(EndpointId endpoint) => _inner.Disconnect(endpoint);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _current?.Retire();
        if (_ownsInner)
        {
            _inner.Dispose();
        }
    }

    /// <summary>The transport one driver sees.</summary>
    public sealed class Generation : ITransport
    {
        private readonly SessionTransport _owner;

        internal Generation(SessionTransport owner, bool holdDisconnects)
        {
            _owner = owner;
            HoldDisconnects = holdDisconnects;
        }

        /// <summary>A retired generation neither sends nor receives.</summary>
        public bool Retired { get; private set; }

        /// <summary>While set, <see cref="Disconnect"/> keeps the remote endpoint (the visit goes on).</summary>
        public bool HoldDisconnects { get; set; }

        public int MaxDatagramSize => _owner._inner.MaxDatagramSize;

        public void Retire()
        {
            Retired = true;
            HoldDisconnects = true;
        }

        public EndpointId Connect(string address)
        {
            ObjectDisposedException.ThrowIf(Retired, this);
            return _owner._inner.Connect(address);
        }

        public bool Send(EndpointId to, ReadOnlySpan<byte> datagram) => !Retired && _owner._inner.Send(to, datagram);

        public bool TryReceive(Span<byte> buffer, out EndpointId from, out int length)
        {
            if (Retired || !ReferenceEquals(_owner._current, this))
            {
                from = EndpointId.None;
                length = 0;
                return false;
            }

            return _owner._inner.TryReceive(buffer, out from, out length);
        }

        public void Disconnect(EndpointId endpoint)
        {
            if (!HoldDisconnects && !Retired)
            {
                _owner._inner.Disconnect(endpoint);
            }
        }

        public bool IsLocalEndpoint(EndpointId endpoint) => _owner._inner.IsLocalEndpoint(endpoint);

        public string? RemoteAddress(EndpointId endpoint) => _owner._inner.RemoteAddress(endpoint);

        /// <summary>Retires the generation; the socket stays with its owner.</summary>
        public void Dispose() => Retire();
    }
}
