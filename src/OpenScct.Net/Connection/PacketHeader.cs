namespace OpenScct.Net;

/// <summary>
/// Header at the start of every packet. Acks are piggybacked: <see cref="Ack"/> is the newest packet received
/// from the other side and bit i of <see cref="AckBits"/> says whether packet Ack-1-i was received too.
/// Times are in 0.1 ms units and wrap; the echo pair lets the sender measure round trip time without a
/// synchronised clock.
/// </summary>
internal struct PacketHeader
{
    public const byte Magic = 0xA7;

    /// <summary>Header size when every optional part is present.</summary>
    public const int MaxBits = 8 + 16 + 1 + 16 + 32 + 16 + 16 + 32 + 16 + 1;

    /// <summary>Header size without acks.</summary>
    public const int MinBits = 8 + 16 + 1 + 32 + 16 + 1;

    public ushort Seq;
    public bool HasAck;
    public ushort Ack;
    public uint AckBits;

    /// <summary>The <see cref="SendTime"/> the other side stamped on packet <see cref="Ack"/>.</summary>
    public ushort EchoTime;

    /// <summary>How long packet <see cref="Ack"/> was held before this packet was sent (0.1 ms, saturating).</summary>
    public ushort EchoDelay;

    /// <summary>Simulation tick of the sender when the packet was built.</summary>
    public uint Tick;

    /// <summary>Sender clock in 0.1 ms units, wrapping.</summary>
    public ushort SendTime;

    /// <summary>
    /// Set on the last packet of a server replication pass that covered every relevant actor: actors with no
    /// bunch in that pass did not change.
    /// </summary>
    public bool bFlushComplete;

    public readonly void Write(FBitWriter w)
    {
        w.WriteByte(Magic);
        w.WriteUInt16(Seq);
        w.WriteBit(HasAck);
        if (HasAck)
        {
            w.WriteUInt16(Ack);
            w.WriteUInt32(AckBits);
            w.WriteUInt16(EchoTime);
            w.WriteUInt16(EchoDelay);
        }

        w.WriteUInt32(Tick);
        w.WriteUInt16(SendTime);
        w.WriteBit(bFlushComplete);
    }

    public static bool Read(FBitReader r, out PacketHeader header)
    {
        header = default;
        if (r.ReadByte() != Magic)
        {
            return false;
        }

        header.Seq = r.ReadUInt16();
        header.HasAck = r.ReadBit();
        if (header.HasAck)
        {
            header.Ack = r.ReadUInt16();
            header.AckBits = r.ReadUInt32();
            header.EchoTime = r.ReadUInt16();
            header.EchoDelay = r.ReadUInt16();
        }

        header.Tick = r.ReadUInt32();
        header.SendTime = r.ReadUInt16();
        header.bFlushComplete = r.ReadBit();
        return !r.IsError;
    }
}
