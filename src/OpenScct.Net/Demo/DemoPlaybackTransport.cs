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
    private DemoCheckpoint? _checkpoint;
    private int _next;
    private int _bootstrapOffset;
    private bool _disposed;

    public DemoPlaybackTransport(DemoRecording recording, uint seekTo = 0)
    {
        Demo = recording ?? throw new ArgumentNullException(nameof(recording));
        _checkpoint = recording.FindCheckpoint(seekTo);
        StartTick = _checkpoint?.Tick ?? recording.Index.FirstTick;
        ReleasedTick = StartTick;
        _next = _checkpoint?.NextPacket ?? 0;
        _bootstrapOffset = _checkpoint?.FirstOffset ?? 0;
    }

    public DemoRecording Demo { get; }

    /// <summary>The original first tick or the tick of the checkpoint used to start this playback.</summary>
    public uint StartTick { get; }

    /// <summary>The newest server tick whose packets the client may receive.</summary>
    public uint ReleasedTick { get; private set; }

    /// <summary>True once every packet was handed out.</summary>
    public bool AtEnd => _checkpoint is null && _next >= Demo.PacketCount;

    public int MaxDatagramSize => NetConnection.MaxPacketBytesLimit;

    /// <summary>True while this transport is delivering the checkpoint's bootstrap packets.</summary>
    public bool IsBootstrapping => _checkpoint is not null;

    /// <summary>Lets the client receive the packets up to <paramref name="serverTick"/>.</summary>
    public void ReleaseUpTo(uint serverTick) => ReleasedTick = Math.Max(ReleasedTick, serverTick);

    public EndpointId Connect(string address) => Recording;

    public bool Send(EndpointId to, ReadOnlySpan<byte> datagram) => !_disposed;

    /// <summary>
    /// Once all bootstrap packets have been delivered, restore the original stream's receive state before
    /// requesting more packets.
    /// </summary>
    public bool TryTakeCheckpointContinuation(out ReadOnlyMemory<byte> continuation)
    {
        if (!_disposed && _checkpoint is { } checkpoint && !checkpoint.HasPacket(_bootstrapOffset))
        {
            continuation = checkpoint.Continuation;
            _checkpoint = null;
            return true;
        }

        continuation = default;
        return false;
    }

    public bool TryReceive(Span<byte> buffer, out EndpointId from, out int length)
    {
        ReadOnlySpan<byte> packet;
        if (!_disposed && _checkpoint is { } checkpoint && checkpoint.HasPacket(_bootstrapOffset))
            packet = checkpoint.TakePacket(ref _bootstrapOffset);
        else if (!_disposed && _checkpoint is null && _next < Demo.PacketCount && Demo.TickOf(_next) <= ReleasedTick)
            packet = Demo.Packet(_next++);
        else
        {
            from = EndpointId.None;
            length = 0;
            return false;
        }

        packet.CopyTo(buffer);
        from = Recording;
        length = packet.Length;
        return true;
    }

    public void Disconnect(EndpointId endpoint)
    {
    }

    /// <summary>A recording is in this process: nothing is counted against a send budget.</summary>
    public bool IsLocalEndpoint(EndpointId endpoint) => endpoint.IsValid;

    public string? RemoteAddress(EndpointId endpoint) => "demo";

    /// <summary>The recorded packets carry the recorder connection's salt; the playing client takes it.</summary>
    public bool AdoptsRemoteSalt => true;

    public bool IsPlayback => true;

    public void Dispose() => _disposed = true;
}
