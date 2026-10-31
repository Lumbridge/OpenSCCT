using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class CamNetworkDisableTests
{
    private static SCamNetwork PlaceCamera(World world, NetDriver driver, bool authority)
    {
        SCamNetwork cam = world.Level.SpawnActor(new SCamNetwork { bNoDelete = false }, default, default)!;
        cam.bNoDelete = true;
        cam.Role = authority ? ENetRole.ROLE_Authority : ENetRole.ROLE_SimulatedProxy;
        cam.RemoteRole = authority ? ENetRole.ROLE_SimulatedProxy : ENetRole.ROLE_Authority;
        var actors = new List<Actor?>();
        for (int i = 0; i < driver.MapBinding.Count; i++) actors.Add(driver.MapBinding.Get(i));
        actors.Add(cam);
        driver.MapBinding.Bind(actors);
        return cam;
    }

    [Fact]
    public void DisabledCountdownReachesBothMercClientsThenRecovers()
    {
        using WalkerRig rig = WalkerRig.Create(null, 1, 2);
        rig.WaitForAllWalkers(2);
        SCamNetwork cam = PlaceCamera(rig.Server.World, rig.Server.Driver, true);
        SCamNetwork seen = PlaceCamera(rig.Clients[0].World, rig.Clients[0].Driver, false);
        SCamNetwork secondSeen = PlaceCamera(rig.Clients[1].World, rig.Clients[1].Driver, false);
        var spy = new SBasePawn { SPawnType = PawnType.SPAWNOFFENSE };
        cam.eventTakeDamage(0, spy, default, default, SDamageType.SDTStickyShocker);
        for (int i = 0; i < 120; i++) rig.Step();
        Assert.InRange(seen.DisabledSeconds, 28, 29);
        Assert.InRange(secondSeen.DisabledSeconds, 28, 29);
        Assert.Equal(seen.mOff, seen.Skins[1]);
        Assert.Equal(secondSeen.mOff, secondSeen.Skins[1]);
        for (int i = 0; i < 1750; i++) rig.Step();
        Assert.False(cam.IsShotDisabled);
        Assert.False(seen.IsShotDisabled);
        Assert.False(secondSeen.IsShotDisabled);
    }
}
