using OpenScct.Core.Engine;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>
/// OpenSCCT's own replicated members of PlayerReplicationInfo: the cosmetic look (two ints), sent to every client so
/// each draws the others' skins and light colours, and the rank (level, prestige and title) the scoreboards show.
/// </summary>
public static class PlayerLookNet
{
    public static RepExtension CreateExtension()
    {
        var extension = new RepExtension();
        extension.Properties.Add(new RepExtensionProperty("SvmLookLow", nameof(PlayerReplicationInfo.SvmLookLow), RepLeafKind.Int));
        extension.Properties.Add(new RepExtensionProperty("SvmLookHigh", nameof(PlayerReplicationInfo.SvmLookHigh), RepLeafKind.Int));
        extension.Properties.Add(new RepExtensionProperty("SvmRank", nameof(PlayerReplicationInfo.SvmRank), RepLeafKind.Int));
        extension.Properties.Add(new("SvmSidearmsUnrestricted", nameof(PlayerReplicationInfo.SvmSidearmsUnrestricted), RepLeafKind.Bool));
        extension.Properties.Add(new("SvmLookExtraLow", nameof(PlayerReplicationInfo.SvmLookExtraLow), RepLeafKind.Int));
        extension.Properties.Add(new("SvmLookExtraHigh", nameof(PlayerReplicationInfo.SvmLookExtraHigh), RepLeafKind.Int));
        return extension;
    }
}
