using OpenScct.Game.SBase;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class ProjectileChannelContractTests
{
    [Fact]
    public void EveryGrenadeRetainsItsChannelButCosmeticTemporariesDoNot()
    {
        var classes = NetClasses.Create();
        int grenades = 0;
        foreach (var entry in classes.Classes)
        {
            if (!typeof(SProjGrenade).IsAssignableFrom(entry.Type)) continue;
            Assert.True(entry.Extension?.PersistentActorChannel, entry.Path);
            grenades++;
        }
        Assert.True(grenades >= 4);
        Assert.NotEqual(true, classes.Find(typeof(SMuzzleFlash))!.Extension?.PersistentActorChannel);
        Assert.NotEqual(true, classes.Find(typeof(SBloodSplat))!.Extension?.PersistentActorChannel);
    }
}
