using System;
using System.Threading;

namespace OpenScct.Net.Voice;

/// <summary>
/// Voice between a listen server and a player in the same process (the host's own player, whose game traffic goes over
/// loopback inside the listen server). Two lock-free single-producer single-consumer queues, so the host thread and the
/// player's thread never wait for each other; a full queue drops datagrams as a socket would.
/// </summary>
public sealed class VoiceLocalLink
{
    private const int QueueCapacity = 128;

    private readonly DatagramRing _up = new(QueueCapacity, VoiceWire.MaxDatagramBytes);
    private readonly DatagramRing _down = new(QueueCapacity, VoiceWire.MaxDatagramBytes);
    private int _talker;

    /// <summary>
    /// The local player's id (its PlayerReplicationInfo.PlayerID), set by the player's side once its controller
    /// replicated. The server routes nothing from or to the link while it is 0.
    /// </summary>
    public ushort Talker
    {
        get => (ushort)Volatile.Read(ref _talker);
        set => Volatile.Write(ref _talker, value);
    }

    /// <summary>Player side: queues a datagram for the server.</summary>
    public bool SendUp(ReadOnlySpan<byte> datagram) => _up.TryWrite(datagram);

    /// <summary>Player side: the next datagram from the server.</summary>
    public bool TryReceiveDown(Span<byte> buffer, out int length) => _down.TryRead(buffer, out length);

    /// <summary>Server side: the next datagram from the player.</summary>
    public bool TryReceiveUp(Span<byte> buffer, out int length) => _up.TryRead(buffer, out length);

    /// <summary>Server side: queues a datagram for the player.</summary>
    public bool SendDown(ReadOnlySpan<byte> datagram) => _down.TryWrite(datagram);

    /// <summary>Datagrams dropped because a queue was full (up, down).</summary>
    public (long Up, long Down) Dropped => (Volatile.Read(ref _up.Dropped), Volatile.Read(ref _down.Dropped));
}
