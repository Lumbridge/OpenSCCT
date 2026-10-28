using System;

namespace OpenScct.Net;

/// <summary>
/// Unreliable datagram transport under the network layer. Everything above it (packets, acks, bunches,
/// reliability, channels) is ours, so a transport only moves whole datagrams, may drop, duplicate or reorder
/// them, and numbers remote endpoints.
/// <para>A transport is used from one thread. Transports that link two threads (the loopback used by a listen
/// server) are safe for one thread on each side.</para>
/// </summary>
public interface ITransport : IDisposable
{
    /// <summary>Largest datagram this transport carries, in bytes.</summary>
    int MaxDatagramSize { get; }

    /// <summary>
    /// Starts talking to <paramref name="address"/> and returns its endpoint id. The meaning of the address is
    /// transport specific (a loopback name, "host:port" for UDP).
    /// </summary>
    EndpointId Connect(string address);

    /// <summary>Sends one datagram. False when it was dropped locally (unknown endpoint, too big, queue full).</summary>
    bool Send(EndpointId to, ReadOnlySpan<byte> datagram);

    /// <summary>
    /// Copies the next received datagram into <paramref name="buffer"/> (at least <see cref="MaxDatagramSize"/>
    /// bytes). False when nothing is waiting.
    /// </summary>
    bool TryReceive(Span<byte> buffer, out EndpointId from, out int length);

    /// <summary>
    /// Forgets an endpoint: datagrams to it are dropped from now on. What happens to later datagrams from the
    /// same remote depends on the transport: loopback drops them (the remote must connect again), SimLink and
    /// UDP report them under a new id.
    /// </summary>
    void Disconnect(EndpointId endpoint);

    /// <summary>
    /// True when <paramref name="endpoint"/> is in this process (loopback): such connections, a listen host's own
    /// players, are not counted against the send budget, as the host player of the original is no connection at all.
    /// </summary>
    bool IsLocalEndpoint(EndpointId endpoint) => false;

    /// <summary>
    /// True when a client connection takes its salt from the first packet it receives instead of choosing one (demo
    /// playback: the recorded packets carry the recorder's salt). Real transports choose.
    /// </summary>
    bool AdoptsRemoteSalt => false;

    /// <summary>The remote address of an endpoint as text for logs, bans and the admin status ("ip:port"), or null.</summary>
    string? RemoteAddress(EndpointId endpoint) => null;
}
