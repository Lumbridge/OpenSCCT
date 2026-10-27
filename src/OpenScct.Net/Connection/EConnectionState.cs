namespace OpenScct.Net;

/// <summary>State of a connection (UE2 EConnectionState).</summary>
public enum EConnectionState : byte
{
    USOCK_Invalid = 0,
    USOCK_Closed = 1,
    USOCK_Pending = 2,
    USOCK_Open = 3,
}
