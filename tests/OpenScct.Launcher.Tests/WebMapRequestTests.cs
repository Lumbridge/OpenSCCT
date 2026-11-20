using OpenScct.Launcher.Launch;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class WebMapRequestTests
{
    [Theory]
    [InlineData("openscct://maps/USDOJO", "USDOJO")]
    [InlineData("OpenSCCT://MAPS/dm_contain", "dm_contain")]
    [InlineData("openscct://maps/")]
    [InlineData("openscct://maps/%2Fsomewhere")]
    [InlineData("openscct://maps/../USDOJO")]
    [InlineData("openscct://maps/USDOJO?install=true")]
    [InlineData("openscct://maps/-switch")]
    [InlineData("openscct://join/USDOJO")]
    public void OpensOnlyASingleCatalogueCode(string argument, string? expected = null)
    {
        Assert.Equal(expected != null, WebMapRequest.TryParse(argument, out string? actual));
        Assert.Equal(expected, actual);
    }
}
