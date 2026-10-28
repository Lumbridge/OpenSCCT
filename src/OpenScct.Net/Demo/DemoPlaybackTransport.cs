using System;

namespace OpenScct.Net;

/// <summary>
/// Plays a demo into a client (docs/DEMOS.md): a transport whose only remote is the recording. It hands out the
/// recorded packets whose server tick is at or before <see cref="ReleasedTick"/>, in order, and drops whatever the
/// client sends (its Hello, moves and acks: the recording never needed them).
/// </summary>
public sealed class DemoPlaybackTransport : ITransport
{
    private static readonly EndpointId Recording = new(1);
    private int _next;
    private bool _disposed;

    public DemoPlaybackTransport(DemoRecording recording)
    {
        Demo = recording ?? throw new ArgumentNullException(nameof(recording));
        ReleasedTick = recording.Index.FirstTick;
    }

    public DemoRecording Demo { get; }

    /// <summary>The newest server tick whose packets the client may receive.</summary>
    public uint ReleasedTick { get; private set; }

    /// <summary>True once every packet was handed out.</summary>
    public bool AtEnd => _next >= Demo.PacketCount;

    public int MaxDatagramSize => NetConnection.MaxPacketBytesLimit;

    /// <summary>Lets the client receive the packets up to <paramref name="serverTick"/>.</summary>
    public void ReleaseUpTo(uint serverTick) => ReleasedTick = Math.Max(ReleasedTick, serverTick);

    public EndpointId Connect(string address) => Recording;

    public bool Send(EndpointId to, ReadOnlySpan<byte> datagram) => !_disposed;

    public bool TryReceive(Span<byte> buffer, out EndpointId from, out int length)
    {
        if (!_disposed && _next < Demo.PacketCount && Demo.TickOf(_next) <= ReleasedTick)
        {
            ReadOnlySpan<byte> packet = Demo.Packet(_next++);
            packet.CopyTo(buffer);
            from = Recording;
            length = packet.Length;
            return true;
        }

        from = EndpointId.None;
        length = 0;
        return false;
    }

    public void Disconnect(EndpointId endpoint)
    {
    }

    /// <summary>A recording is in this process: nothing is counted against a send budget.</summary>
    public bool IsLocalEndpoint(EndpointId endpoint) => endpoint.IsValid;

    public string? RemoteAddress(EndpointId endpoint) => "demo";

    /// <summary>The recorded packets carry the recorder connection's salt; the playing client takes it.</summary>
    public bool AdoptsRemoteSalt => true;

    public void Dispose() => _disposed = true;
}
