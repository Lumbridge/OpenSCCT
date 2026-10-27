namespace OpenScct.Net;

/// <summary>
/// An outgoing bunch being built (UE2 FOutBunch). A connection owns one and hands it out with
/// <see cref="NetConnection.BeginBunch"/>; write the payload into <see cref="Writer"/>, then call
/// <see cref="NetConnection.SendBunch"/>.
/// </summary>
public sealed class FOutBunch
{
    internal FOutBunch(int maxPayloadBits)
    {
        Writer = new FBitWriter(maxPayloadBits);
    }

    public FBitWriter Writer { get; }

    public Channel Channel { get; private set; } = null!;

    public bool bOpen;
    public bool bClose;
    public bool bReliable;

    internal void Reset(Channel channel, bool reliable)
    {
        Writer.Reset();
        Channel = channel;
        bReliable = reliable;
        bOpen = false;
        bClose = false;
    }
}

/// <summary>A received bunch (UE2 FInBunch). Reused: valid only during the ReceivedBunch call.</summary>
public sealed class FInBunch
{
    public NetConnection Connection { get; internal set; } = null!;

    public int ChIndex { get; internal set; }

    public EChannelType ChType { get; internal set; }

    public int ChSequence { get; internal set; }

    public bool bOpen { get; internal set; }

    public bool bClose { get; internal set; }

    public bool bReliable { get; internal set; }

    /// <summary>Simulation tick the sender stamped on the packet that carried this bunch.</summary>
    public uint PacketTick { get; internal set; }

    /// <summary>The payload.</summary>
    public FBitReader Reader { get; } = new();
}

/// <summary>A reliable bunch kept until acknowledged, for resending.</summary>
internal sealed class ReliableBunch
{
    public ReliableBunch(int maxPayloadBytes)
    {
        Data = new byte[maxPayloadBytes];
    }

    public readonly byte[] Data;
    public int Bits;
    public Channel Channel = null!;
    public int ChSequence;
    public bool bOpen;
    public bool bClose;
    public ushort PacketSeq;
    public bool Acked;
    public bool NeedsResend;
}

/// <summary>A reliable bunch that arrived ahead of a gap, held until the bunches before it arrive.</summary>
internal sealed class QueuedBunch
{
    public QueuedBunch(int maxPayloadBytes)
    {
        Data = new byte[maxPayloadBytes];
    }

    public readonly byte[] Data;
    public int Bits;
    public int ChSequence;
    public EChannelType ChType;
    public bool bOpen;
    public bool bClose;
    public uint PacketTick;
    public QueuedBunch? Next;
}
