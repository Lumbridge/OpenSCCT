using System;
using System.Buffers.Binary;

namespace OpenScct.Net.Voice;

/// <summary>What a voice datagram carries.</summary>
public enum EVoicePacketKind : byte
{
    None = 0,

    /// <summary>Client to server: one Opus frame of the sender's microphone.</summary>
    Up = 1,

    /// <summary>Server to client: one Opus frame of the talker the header names.</summary>
    Down = 2,

    /// <summary>Client to server: "I take part in voice", with the talkers this client muted. Repeated as a keep-alive.</summary>
    Join = 3,
}

/// <summary>Who a transmission is meant for (docs/VOICE.md "Channels").</summary>
public enum EVoiceChannel : byte
{
    /// <summary>Everyone the server's routing rules let hear the talker (the stock behaviour).</summary>
    All = 0,

    /// <summary>The talker's own team only, plus eavesdroppers on hacked comms (the stock lobby "team only" switch).</summary>
    Team = 1,
}

/// <summary>Talk spurt markers on audio packets.</summary>
[Flags]
public enum EVoiceFlags : byte
{
    None = 0,

    /// <summary>First frame after the transmit gate opened.</summary>
    TalkStart = 1,

    /// <summary>Last frame before the gate closed.</summary>
    TalkEnd = 2,
}

/// <summary>The fixed part of every voice datagram.</summary>
public readonly record struct VoiceHeader(EVoicePacketKind Kind, EVoiceChannel Channel, EVoiceFlags Flags, ushort Sequence, ushort Talker,
    byte Codec = VoiceWire.CodecOpus48Mono20);

/// <summary>
/// Voice datagrams. They travel on the game's transport next to the game packets (see <see cref="VoiceTransport"/>), never
/// through actor replication, and are unreliable: a lost frame is concealed, not resent.
/// <code>
/// byte 0      0x56, the voice marker (game packets start with 0xA7)
/// byte 1      bits 0-2 kind, bit 3 team channel, bit 4 talk start, bit 5 talk end, bits 6-7 version (1)
/// bytes 2-3   sequence, little endian, one per frame of the sender, wrapping
/// bytes 4-5   talker (the server writes the sender's player id into Down packets; 0 or a claimed id elsewhere)
/// byte 6      codec in the high nibble (1: Opus, 48 kHz, mono, 20 ms), frames in the low nibble (1)
/// bytes 7..   Up and Down: the Opus packet (1 to 62 bytes)
///             Join: flags (bit 0 receive voice), mute count n (at most 32), n talker ids (2 bytes each)
/// </code>
/// </summary>
public static class VoiceWire
{
    public const byte Magic = 0x56;
    public const byte Version = 1;
    public const int HeaderBytes = 7;

    /// <summary>Opus at 48 kHz, mono, one 20 ms frame per packet.</summary>
    public const byte CodecOpus48Mono20 = 0x11;

    /// <summary>Largest audio datagram: header and the largest Opus packet (69 bytes).</summary>
    public const int MaxAudioDatagramBytes = HeaderBytes + VoiceK.MaxOpusPacketBytes;

    /// <summary>Largest join datagram.</summary>
    public const int MaxJoinDatagramBytes = HeaderBytes + 2 + 2 * VoiceK.MaxMutedTalkers;

    /// <summary>Largest voice datagram of any kind.</summary>
    public const int MaxDatagramBytes = MaxJoinDatagramBytes > MaxAudioDatagramBytes ? MaxJoinDatagramBytes : MaxAudioDatagramBytes;

    /// <summary>True when <paramref name="datagram"/> is a voice datagram rather than a game packet.</summary>
    public static bool IsVoice(ReadOnlySpan<byte> datagram) => datagram.Length >= HeaderBytes && datagram[0] == Magic;

    /// <summary>Signed distance from <paramref name="b"/> to <paramref name="a"/> in a wrapping 16-bit sequence space.</summary>
    public static int SeqDiff(ushort a, ushort b) => (short)(ushort)(a - b);

    /// <summary>Writes the header into the first <see cref="HeaderBytes"/> bytes of <paramref name="destination"/>.</summary>
    public static void WriteHeader(Span<byte> destination, in VoiceHeader header)
    {
        if (destination.Length < HeaderBytes)
        {
            throw new ArgumentException("A voice header needs 7 bytes.", nameof(destination));
        }

        destination[0] = Magic;
        destination[1] = (byte)(((byte)header.Kind & 0x07) |
            (header.Channel == EVoiceChannel.Team ? 0x08 : 0) |
            ((header.Flags & EVoiceFlags.TalkStart) != 0 ? 0x10 : 0) |
            ((header.Flags & EVoiceFlags.TalkEnd) != 0 ? 0x20 : 0) |
            (Version << 6));
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], header.Sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], header.Talker);
        destination[6] = header.Codec;
    }

    /// <summary>Reads a header. False for anything that is not a voice datagram of this version.</summary>
    public static bool TryReadHeader(ReadOnlySpan<byte> datagram, out VoiceHeader header)
    {
        header = default;
        if (!IsVoice(datagram) || datagram[1] >> 6 != Version)
        {
            return false;
        }

        byte bits = datagram[1];
        var kind = (EVoicePacketKind)(bits & 0x07);
        if (kind is not (EVoicePacketKind.Up or EVoicePacketKind.Down or EVoicePacketKind.Join))
        {
            return false;
        }

        EVoiceFlags flags = ((bits & 0x10) != 0 ? EVoiceFlags.TalkStart : EVoiceFlags.None) |
            ((bits & 0x20) != 0 ? EVoiceFlags.TalkEnd : EVoiceFlags.None);
        header = new VoiceHeader(kind, (bits & 0x08) != 0 ? EVoiceChannel.Team : EVoiceChannel.All, flags,
            BinaryPrimitives.ReadUInt16LittleEndian(datagram[2..]), BinaryPrimitives.ReadUInt16LittleEndian(datagram[4..]), datagram[6]);
        return true;
    }

    /// <summary>Writes an Up or Down datagram and returns its length.</summary>
    public static int WriteAudio(Span<byte> destination, in VoiceHeader header, ReadOnlySpan<byte> opus)
    {
        if (header.Kind is not (EVoicePacketKind.Up or EVoicePacketKind.Down))
        {
            throw new ArgumentException("Audio datagrams are Up or Down.", nameof(header));
        }

        if (opus.Length is 0 or > VoiceK.MaxOpusPacketBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(opus), opus.Length, "An Opus packet holds 1 to 62 bytes.");
        }

        WriteHeader(destination, header);
        opus.CopyTo(destination[HeaderBytes..]);
        return HeaderBytes + opus.Length;
    }

    /// <summary>The Opus packet of an audio datagram, or false when it is malformed (empty or too long).</summary>
    public static bool TryReadAudio(ReadOnlySpan<byte> datagram, out VoiceHeader header, out ReadOnlySpan<byte> opus)
    {
        opus = default;
        if (!TryReadHeader(datagram, out header) || header.Kind is not (EVoicePacketKind.Up or EVoicePacketKind.Down) ||
            header.Codec != CodecOpus48Mono20)
        {
            return false;
        }

        int length = datagram.Length - HeaderBytes;
        if (length is <= 0 or > VoiceK.MaxOpusPacketBytes)
        {
            return false;
        }

        opus = datagram[HeaderBytes..];
        return true;
    }

    /// <summary>Writes a Join datagram and returns its length. At most <see cref="VoiceK.MaxMutedTalkers"/> mutes are written.</summary>
    public static int WriteJoin(Span<byte> destination, ushort sequence, ushort claimedTalker, bool receive, ReadOnlySpan<ushort> muted)
    {
        int count = Math.Min(muted.Length, VoiceK.MaxMutedTalkers);
        int length = HeaderBytes + 2 + 2 * count;
        if (destination.Length < length)
        {
            throw new ArgumentException("The destination is too small for the join message.", nameof(destination));
        }

        WriteHeader(destination, new VoiceHeader(EVoicePacketKind.Join, EVoiceChannel.All, EVoiceFlags.None, sequence, claimedTalker));
        destination[HeaderBytes] = (byte)(receive ? 1 : 0);
        destination[HeaderBytes + 1] = (byte)count;
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination[(HeaderBytes + 2 + 2 * i)..], muted[i]);
        }

        return length;
    }

    /// <summary>Reads a Join datagram: the receive flag and the mute list (a slice of the datagram, 2 bytes per id).</summary>
    public static bool TryReadJoin(ReadOnlySpan<byte> datagram, out VoiceHeader header, out bool receive, out ReadOnlySpan<byte> mutedIds)
    {
        receive = false;
        mutedIds = default;
        if (!TryReadHeader(datagram, out header) || header.Kind != EVoicePacketKind.Join || datagram.Length < HeaderBytes + 2)
        {
            return false;
        }

        int count = datagram[HeaderBytes + 1];
        if (count > VoiceK.MaxMutedTalkers || datagram.Length != HeaderBytes + 2 + 2 * count)
        {
            return false;
        }

        receive = (datagram[HeaderBytes] & 1) != 0;
        mutedIds = datagram.Slice(HeaderBytes + 2, 2 * count);
        return true;
    }

    /// <summary>The i-th id of a mute list read by <see cref="TryReadJoin"/>.</summary>
    public static ushort MutedAt(ReadOnlySpan<byte> mutedIds, int index) => BinaryPrimitives.ReadUInt16LittleEndian(mutedIds[(2 * index)..]);
}
