namespace OpenScct.Net;

/// <summary>
/// Channel 0 of every connection (UE2 UControlChannel): the handshake and other connection-level messages,
/// one message per reliable bunch. Closing it closes the connection.
/// </summary>
public sealed class ControlChannel : Channel
{
    /// <summary>Sends <paramref name="message"/> reliably. The first message also opens the channel.</summary>
    public void Send(in ControlMessage message)
    {
        if (Closing || Connection.State == EConnectionState.USOCK_Closed)
        {
            return;
        }

        FOutBunch bunch = Connection.BeginBunch(this, reliable: true);
        if (!OpenSent)
        {
            bunch.bOpen = true;
            OpenSent = true;
        }

        message.Write(bunch.Writer);
        Connection.SendBunch(bunch);
    }

    protected internal override void ReceivedBunch(FInBunch bunch)
    {
        FBitReader r = bunch.Reader;
        while (!r.AtEnd)
        {
            ControlMessage message = ControlMessage.Read(r);
            if (r.IsError)
            {
                Connection.Close("Malformed control message");
                return;
            }

            Connection.Driver.Notify.NotifyControlMessage(Connection, message);
            if (Connection.State == EConnectionState.USOCK_Closed)
            {
                return;
            }
        }
    }
}
