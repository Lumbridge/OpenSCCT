using System;
using System.Buffers.Binary;
using System.Globalization;

namespace OpenScct.Net;

/// <summary>
/// Frames of the session-token UDP relay (docs/ONLINE.md, "Relay"). The relay forwards our datagrams between a client
/// and a host that cannot reach each other directly, for example a host behind carrier-grade NAT. It never reads the
/// datagrams it carries. Frames travel as LiteNetLib unconnected messages on the relay port, so the host and the client
/// use their normal game socket for them.
/// <code>
/// host   -> relay  A5 'H' hostToken(8)                 register and keep the host's NAT mapping open (every 5 s)
/// relay  -> host   A5 'h'                              registration accepted
/// client -> relay  A5 'C' clientToken(8) payload       a datagram for the host
/// relay  -> host   A5 'D' session(4) payload           the same datagram, tagged with the relay session
/// host   -> relay  A5 'D' session(4) payload           a datagram for the client of that session
/// relay  -> client A5 'c' payload                      the same datagram
/// relay  -> host   A5 'N' session(4) address(utf8)     a client bound to a new session (address as the relay sees it)
/// host   -> relay  A5 'X' session(4)                   the host closed that session
/// client -> relay  A5 'x' clientToken(8)               the client left
/// relay  -> either A5 'E' code(1) text(utf8)           refused (unknown token, host not registered, rate limit)
/// </code>
/// Tokens are 64-bit random values the master hands out over HTTPS: the host token with each heartbeat answer, a
/// client token per join request. Session ids are random 32-bit values chosen by the relay.
/// </summary>
public static class RelayFrames
{
    public const byte Magic = 0xA5;

    public const byte HostHello = (byte)'H';

    public const byte HostHelloAck = (byte)'h';

    public const byte ClientData = (byte)'C';

    public const byte ToClient = (byte)'c';

    public const byte HostData = (byte)'D';

    public const byte NewSession = (byte)'N';

    public const byte HostClose = (byte)'X';

    public const byte ClientClose = (byte)'x';

    public const byte Error = (byte)'E';

    public const int TokenBytes = 8;

    public const int SessionBytes = 4;

    /// <summary>Bytes before the payload of a client frame (<see cref="ClientData"/>).</summary>
    public const int ClientHeaderBytes = 2 + TokenBytes;

    /// <summary>Bytes before the payload of a host frame (<see cref="HostData"/>).</summary>
    public const int HostHeaderBytes = 2 + SessionBytes;

    /// <summary>Largest frame the relay forwards: our largest datagram (1200 bytes) plus the client header, with room to spare.</summary>
    public const int MaxFrameBytes = 1280;

    /// <summary>Error codes of an <see cref="Error"/> frame.</summary>
    public enum ErrorCode : byte
    {
        UnknownToken = 1,
        HostNotRegistered = 2,
        RateLimited = 3,
        SessionClosed = 4,
    }

    /// <summary>True when <paramref name="frame"/> starts like a relay frame of any type.</summary>
    public static bool IsRelayFrame(ReadOnlySpan<byte> frame) => frame.Length >= 2 && frame[0] == Magic;

    public static byte TypeOf(ReadOnlySpan<byte> frame) => frame.Length >= 2 && frame[0] == Magic ? frame[1] : (byte)0;

    /// <summary>Writes a header with a token (<see cref="HostHello"/>, <see cref="ClientData"/>, <see cref="ClientClose"/>); returns its length.</summary>
    public static int WriteTokenHeader(Span<byte> destination, byte type, ulong token)
    {
        destination[0] = Magic;
        destination[1] = type;
        BinaryPrimitives.WriteUInt64BigEndian(destination[2..], token);
        return 2 + TokenBytes;
    }

    /// <summary>Writes a header with a session id (<see cref="HostData"/>, <see cref="NewSession"/>, <see cref="HostClose"/>); returns its length.</summary>
    public static int WriteSessionHeader(Span<byte> destination, byte type, uint session)
    {
        destination[0] = Magic;
        destination[1] = type;
        BinaryPrimitives.WriteUInt32BigEndian(destination[2..], session);
        return 2 + SessionBytes;
    }

    public static bool TryReadToken(ReadOnlySpan<byte> frame, out ulong token)
    {
        if (frame.Length < 2 + TokenBytes)
        {
            token = 0;
            return false;
        }

        token = BinaryPrimitives.ReadUInt64BigEndian(frame[2..]);
        return true;
    }

    public static bool TryReadSession(ReadOnlySpan<byte> frame, out uint session)
    {
        if (frame.Length < 2 + SessionBytes)
        {
            session = 0;
            return false;
        }

        session = BinaryPrimitives.ReadUInt32BigEndian(frame[2..]);
        return true;
    }

    /// <summary>An <see cref="Error"/> frame: the code and a short text for the player.</summary>
    public static byte[] ErrorFrame(ErrorCode code, string text)
    {
        byte[] message = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty);
        int length = Math.Min(message.Length, 200);
        var frame = new byte[3 + length];
        frame[0] = Magic;
        frame[1] = Error;
        frame[2] = (byte)code;
        Array.Copy(message, 0, frame, 3, length);
        return frame;
    }

    public static bool TryReadError(ReadOnlySpan<byte> frame, out ErrorCode code, out string text)
    {
        if (frame.Length < 3 || TypeOf(frame) != Error)
        {
            code = 0;
            text = string.Empty;
            return false;
        }

        code = (ErrorCode)frame[2];
        text = System.Text.Encoding.UTF8.GetString(frame[3..]);
        return true;
    }

    /// <summary>Tokens travel as 16 lower-case hex digits in the master's JSON.</summary>
    public static string TokenText(ulong token) => token.ToString("x16", CultureInfo.InvariantCulture);

    public static bool TryParseToken(string? text, out ulong token)
    {
        token = 0;
        return text is { Length: > 0 and <= 16 } &&
            ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out token) && token != 0;
    }
}
