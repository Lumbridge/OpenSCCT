namespace OpenScct.Net;

/// <summary>
/// One channel of a connection (UE2 UChannel). Bunches on a channel are either reliable (numbered per channel,
/// resent until acknowledged and delivered in order) or unreliable (delivered as they arrive, never resent).
/// A channel opens with its first reliable bunch and closes with a reliable close bunch; its index is reused
/// only after the close is acknowledged.
/// </summary>
public abstract class Channel
{
    private QueuedBunch? _inRec;

    public NetConnection Connection { get; private set; } = null!;

    public int ChIndex { get; private set; }

    public EChannelType ChType { get; private set; }

    /// <summary>True on the side that opened the channel.</summary>
    public bool OpenedLocally { get; private set; }

    /// <summary>True once the other side has acknowledged the open bunch (always true on the receiving side).</summary>
    public bool OpenAcked { get; internal set; }

    /// <summary>True once a close bunch was sent; later bunches are not processed.</summary>
    public bool Closing { get; private set; }

    /// <summary>Reliable bunches sent and not yet acknowledged.</summary>
    public int NumOutRec { get; internal set; }

    /// <summary>Reliable bunches received ahead of a gap.</summary>
    public int NumInRec { get; private set; }
    public long PayloadBytesSent { get; internal set; }

    internal bool OpenSent;

    internal void Init(NetConnection connection, int chIndex, EChannelType chType, bool openedLocally)
    {
        Connection = connection;
        ChIndex = chIndex;
        ChType = chType;
        OpenedLocally = openedLocally;
        OpenAcked = !openedLocally;
        OpenSent = !openedLocally;
    }

    /// <summary>Handles a bunch in channel order. Reliable bunches arrive here exactly once and in sequence.</summary>
    protected internal abstract void ReceivedBunch(FInBunch bunch);

    /// <summary>A packet that carried unreliable data on this channel was lost.</summary>
    protected internal virtual void ReceivedNak(ushort packetSeq)
    {
    }

    /// <summary>Called once when the channel goes away (closed by either side, or the connection closed).</summary>
    protected internal virtual void CleanUp()
    {
    }

    /// <summary>Starts closing: sends a reliable close bunch. The channel is freed when the close is acknowledged.</summary>
    public void Close()
    {
        if (Closing || Connection.State == EConnectionState.USOCK_Closed)
        {
            return;
        }

        Closing = true;
        FOutBunch bunch = Connection.BeginBunch(this, reliable: true);
        bunch.bClose = true;
        Connection.SendBunch(bunch);
    }

    internal void ReceivedReliableAck(bool bOpen, bool bClose)
    {
        NumOutRec--;
        if (bOpen)
        {
            OpenAcked = true;
        }

        if (bClose)
        {
            Connection.ReleaseChannel(this);
        }
    }

    internal void ReceivedRawBunch(FInBunch bunch)
    {
        NetConnection connection = Connection;
        if (bunch.bReliable)
        {
            int delta = NetConnection.ReliableSeqDiff(bunch.ChSequence, connection.InReliable[ChIndex]);
            if (delta <= 0)
            {
                return;
            }

            if (delta > 1)
            {
                Queue(bunch);
                return;
            }
        }

        ReceivedSequencedBunch(bunch);

        while (_inRec is not null && ReferenceEquals(connection.Channels[ChIndex], this) &&
               NetConnection.ReliableSeqDiff(_inRec.ChSequence, connection.InReliable[ChIndex]) == 1)
        {
            QueuedBunch queued = _inRec;
            _inRec = queued.Next;
            NumInRec--;
            FInBunch next = connection.PrepareQueuedBunch(queued, ChIndex);
            ReceivedSequencedBunch(next);
            connection.ReturnQueued(queued);
        }
    }

    private void ReceivedSequencedBunch(FInBunch bunch)
    {
        if (bunch.bReliable)
        {
            Connection.InReliable[ChIndex] = bunch.ChSequence;
        }

        if (!Closing)
        {
            ReceivedBunch(bunch);
        }

        if (bunch.bClose && ReferenceEquals(Connection.Channels[ChIndex], this))
        {
            Connection.ChannelClosedByRemote(this);
        }
    }

    private void Queue(FInBunch bunch)
    {
        if (NumInRec >= K.RELIABLE_BUFFER)
        {
            Connection.Close("Too many reliable bunches queued");
            return;
        }

        NetConnection connection = Connection;
        int last = connection.InReliable[ChIndex];
        int delta = NetConnection.ReliableSeqDiff(bunch.ChSequence, last);
        QueuedBunch? prev = null;
        QueuedBunch? at = _inRec;
        while (at is not null)
        {
            int atDelta = NetConnection.ReliableSeqDiff(at.ChSequence, last);
            if (atDelta == delta)
            {
                return;
            }

            if (atDelta > delta)
            {
                break;
            }

            prev = at;
            at = at.Next;
        }

        QueuedBunch queued = connection.RentQueued(bunch);
        queued.Next = at;
        if (prev is null)
        {
            _inRec = queued;
        }
        else
        {
            prev.Next = queued;
        }

        NumInRec++;
    }

    internal void ReleaseQueued()
    {
        while (_inRec is not null)
        {
            QueuedBunch queued = _inRec;
            _inRec = queued.Next;
            Connection.ReturnQueued(queued);
        }

        NumInRec = 0;
    }
}
