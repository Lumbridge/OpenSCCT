using OpenScct.Core.Engine;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>
/// OpenSCCT's own replicated members of PlayerReplicationInfo: the cosmetic look (two ints), sent to every client so
/// each draws the others' skins and light colours.
/// </summary>
public static class PlayerLookNet
{
    public static RepExtension CreateExtension()
    {
        var extension = new RepExtension();
        extension.Properties.Add(new RepExtensionProperty("SvmLookLow", nameof(PlayerReplicationInfo.SvmLookLow), RepLeafKind.Int));
        extension.Properties.Add(new RepExtensionProperty("SvmLookHigh", nameof(PlayerReplicationInfo.SvmLookHigh), RepLeafKind.Int));
        return extension;
    }
}
