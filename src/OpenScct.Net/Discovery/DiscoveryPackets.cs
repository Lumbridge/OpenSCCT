using System;
using System.Buffers.Binary;

namespace OpenScct.Net.Discovery;

/// <summary>
/// Messages outside connections, as LiteNetLib unconnected (or broadcast) messages: "SVM" and a type byte, then an
/// 8-byte nonce the answer echoes, so the asker can match answers and time them.
/// <code>
/// browser -> server  SVMQ nonce              server info query (game port, LAN port, LAN broadcast)
/// server  -> browser SVMI nonce info         the answer (<see cref="ServerInfo"/>)
/// master  -> host    SVMP nonce              reachability probe from a port the host never sent to
/// host    -> master  SVMp nonce              the probe got through
/// </code>
/// </summary>
public static class DiscoveryPackets
{
    public const byte Query = (byte)'Q';

    public const byte Info = (byte)'I';

    public const byte Probe = (byte)'P';

    public const byte ProbeReply = (byte)'p';

    /// <summary>Magic and type, then the nonce.</summary>
    public const int HeaderBytes = 4 + 8;

    /// <summary>
    /// A query is padded to this size and a shorter one is ignored, so an answer is never much bigger than the
    /// question (a spoofed query cannot turn a server into an amplifier).
    /// </summary>
    public const int QueryBytes = 128;

    /// <summary>Answers a server gives one address per second at most.</summary>
    public const int AnswersPerSecond = 8;

    /// <summary>Largest message: an info with a full player list stays well under one datagram.</summary>
    public const int MaxBytes = 1200;

    public static bool IsDiscovery(ReadOnlySpan<byte> data) =>
        data.Length >= HeaderBytes && data[0] == (byte)'S' && data[1] == (byte)'V' && data[2] == (byte)'M';

    public static byte TypeOf(ReadOnlySpan<byte> data) => IsDiscovery(data) ? data[3] : (byte)0;

    public static ulong NonceOf(ReadOnlySpan<byte> data) => IsDiscovery(data) ? BinaryPrimitives.ReadUInt64BigEndian(data[4..]) : 0;

    /// <summary>Writes a header-only message (<see cref="Query"/>, <see cref="Probe"/>, <see cref="ProbeReply"/>); returns its length.</summary>
    public static int WriteHeader(Span<byte> destination, byte type, ulong nonce)
    {
        destination[0] = (byte)'S';
        destination[1] = (byte)'V';
        destination[2] = (byte)'M';
        destination[3] = type;
        BinaryPrimitives.WriteUInt64BigEndian(destination[4..], nonce);
        return HeaderBytes;
    }

    /// <summary>Writes an <see cref="Info"/> answer; returns its length (0 when it does not fit).</summary>
    public static int WriteInfo(Span<byte> destination, ulong nonce, ServerInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        int n = WriteHeader(destination, Info, nonce);
        int body = info.Write(destination[n..]);
        return n + body <= destination.Length ? n + body : 0;
    }

    /// <summary>Writes a padded <see cref="Query"/>; returns its length (<see cref="QueryBytes"/>).</summary>
    public static int WriteQuery(Span<byte> destination, ulong nonce)
    {
        WriteHeader(destination, Query, nonce);
        destination[HeaderBytes..QueryBytes].Clear();
        return QueryBytes;
    }

    /// <summary>True for a query a server should answer (the right type and padded to <see cref="QueryBytes"/>).</summary>
    public static bool IsQuery(ReadOnlySpan<byte> data) => TypeOf(data) == Query && data.Length >= QueryBytes;

    public static bool TryReadInfo(ReadOnlySpan<byte> data, out ulong nonce, out ServerInfo info)
    {
        nonce = NonceOf(data);
        info = new ServerInfo();
        return TypeOf(data) == Info && ServerInfo.TryRead(data[HeaderBytes..], out info);
    }
}
