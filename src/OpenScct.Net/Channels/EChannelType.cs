namespace OpenScct.Net;

/// <summary>Kind of a channel, sent with the open bunch (UE2 EChannelType).</summary>
public enum EChannelType : byte
{
    CHTYPE_None = 0,
    CHTYPE_Control = 1,
    CHTYPE_Actor = 2,
    CHTYPE_File = 3,
    CHTYPE_MAX = 8,
}
