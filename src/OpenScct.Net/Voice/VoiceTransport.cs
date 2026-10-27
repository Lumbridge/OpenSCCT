using System;

namespace OpenScct.Net.Voice;

/// <summary>The side of voice chat that receives voice datagrams from a <see cref="VoiceTransport"/>.</summary>
public interface IVoiceEndpoint
{
    /// <summary>A voice datagram from <paramref name="from"/> (an id as the game's driver numbers its endpoints).</summary>
    void OnVoiceDatagram(EndpointId from, ReadOnlySpan<byte> datagram);

    /// <summary>
    /// A receive pass of the owning driver begins (at least once per <see cref="NetDriver.TickDispatch"/>). Runs on the
    /// driver's thread, so the endpoint may read the game world here.
    /// </summary>
    void OnDispatch();
}

/// <summary>Voice traffic seen by one <see cref="VoiceTransport"/>.</summary>
public sealed class VoiceTransportStats
{
    public long PacketsIn { get; internal set; }

    public long BytesIn { get; internal set; }

    public long PacketsOut { get; internal set; }

    public long BytesOut { get; internal set; }

    /// <summary>Voice datagrams that could not be sent (unknown endpoint, full queue).</summary>
    public long SendFailures { get; internal set; }

    /// <summary>Game datagrams thrown away by <see cref="VoiceTransport.DrainVoiceOnly"/>.</summary>
    public long GameDatagramsDiscarded { get; internal set; }
}

/// <summary>
/// Puts voice on the game's transport. It wraps the transport a <see cref="NetDriver"/> uses: game datagrams pass through
/// unchanged, voice datagrams (<see cref="VoiceWire.IsVoice"/>) are handed to <see cref="Voice"/> as they are read, and
/// <see cref="SendVoice"/> sends on the same socket, so voice reaches the same endpoints as the game with no extra port,
/// handshake or NAT work.
/// <para>A listen server's remote transport sits inside a <see cref="CompositeTransport"/>: pass its index there
/// (1 for the game's ListenServer, after loopback). The transport side of this class then works in the composite's
/// inner ids, as any transport inside a composite does, while the voice side (<see cref="IVoiceEndpoint"/> and
/// <see cref="SendVoice"/>) works in the driver's ids, so they match the driver's connections.</para>
/// <para>Single-threaded like any transport: the driver's thread calls everything except where noted.</para>
/// </summary>
public sealed class VoiceTransport : ITransport
{
    private const int CompositeIndexBits = 3;

    private readonly bool _ownsInner;
    private readonly int _compositeIndex;
    private bool _inPass;

    /// <param name="inner">The transport that carries the datagrams.</param>
    /// <param name="compositeIndex">This transport's index in a <see cref="CompositeTransport"/>, or -1 when the driver uses it directly.</param>
    /// <param name="ownsInner">Dispose <paramref name="inner"/> with this transport.</param>
    public VoiceTransport(ITransport inner, int compositeIndex = -1, bool ownsInner = true)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfLessThan(compositeIndex, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(compositeIndex, (1 << CompositeIndexBits) - 1);
        if (inner.MaxDatagramSize < VoiceWire.MaxDatagramBytes)
        {
            throw new ArgumentException("The transport cannot carry voice datagrams.", nameof(inner));
        }

        Inner = inner;
        _compositeIndex = compositeIndex;
        _ownsInner = ownsInner;
    }

    public ITransport Inner { get; }

    /// <summary>Where voice datagrams go: the server's <see cref="VoiceRelay"/> or a client's <see cref="VoiceClient"/>.</summary>
    public IVoiceEndpoint? Voice { get; set; }

    public VoiceTransportStats Stats { get; } = new();

    public int MaxDatagramSize => Inner.MaxDatagramSize;

    public EndpointId Connect(string address) => Inner.Connect(address);

    public bool Send(EndpointId to, ReadOnlySpan<byte> datagram) => Inner.Send(to, datagram);

    /// <summary>The driver's id of one of this transport's own endpoint ids (the same id unless inside a composite).</summary>
    public EndpointId DriverEndpoint(EndpointId endpoint) => ToOuter(endpoint);

    /// <summary>Sends a voice datagram to <paramref name="to"/> (a driver endpoint id).</summary>
    public bool SendVoice(EndpointId to, ReadOnlySpan<byte> datagram)
    {
        if (Inner.Send(ToInner(to), datagram))
        {
            Stats.PacketsOut++;
            Stats.BytesOut += datagram.Length;
            return true;
        }

        Stats.SendFailures++;
        return false;
    }

    public bool TryReceive(Span<byte> buffer, out EndpointId from, out int length)
    {
        if (!_inPass)
        {
            _inPass = true;
            Voice?.OnDispatch();
        }

        while (Inner.TryReceive(buffer, out from, out length))
        {
            ReadOnlySpan<byte> datagram = buffer[..length];
            if (!VoiceWire.IsVoice(datagram))
            {
                return true;
            }

            Stats.PacketsIn++;
            Stats.BytesIn += length;
            Voice?.OnVoiceDatagram(ToOuter(from), datagram);
        }

        _inPass = false;
        from = EndpointId.None;
        length = 0;
        return false;
    }

    /// <summary>
    /// For a transport that carries only voice (tests, the loopback smoke): reads everything waiting, hands the voice to
    /// <see cref="Voice"/> and discards anything else. Never call it on a transport a driver reads.
    /// </summary>
    public void DrainVoiceOnly()
    {
        Span<byte> buffer = stackalloc byte[Inner.MaxDatagramSize];
        while (TryReceive(buffer, out _, out _))
        {
            Stats.GameDatagramsDiscarded++;
        }
    }

    public void Disconnect(EndpointId endpoint) => Inner.Disconnect(endpoint);

    public bool IsLocalEndpoint(EndpointId endpoint) => Inner.IsLocalEndpoint(endpoint);

    public string? RemoteAddress(EndpointId endpoint) => Inner.RemoteAddress(endpoint);

    public void Dispose()
    {
        if (_ownsInner)
        {
            Inner.Dispose();
        }
    }

    private EndpointId ToOuter(EndpointId inner) =>
        _compositeIndex < 0 || !inner.IsValid ? inner : new EndpointId((inner.Value << CompositeIndexBits) | _compositeIndex);

    private EndpointId ToInner(EndpointId outer)
    {
        if (_compositeIndex < 0 || !outer.IsValid)
        {
            return outer;
        }

        return (outer.Value & ((1 << CompositeIndexBits) - 1)) == _compositeIndex
            ? new EndpointId(outer.Value >> CompositeIndexBits)
            : EndpointId.None;
    }
}
