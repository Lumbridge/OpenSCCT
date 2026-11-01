using OpenScct.Core;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class PersistentProjectileTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LostSpawnAndCloseRecoverWithoutDuplicatingGameplayProjectile(bool destroyDuringOutage)
    {
        NetClassRegistry registry = NetRig.CreateRegistry();
        registry.Extend<TestTemporary>(new RepExtension { PersistentActorChannel = true });
        using var rig = new NetRig(SimLinkProfile.A, 915, 1, registry: registry);
        rig.Step(60);
        rig.Network.PauseLink("server", "client0", rig.Clock.NowMicroseconds + 300_000);
        TestTemporary grenade = rig.ServerWorld.Level.Spawn<TestTemporary>(new FVector(5, 6, 7))!;
        rig.Step(6);
        if (destroyDuringOutage) grenade.Destroy();
        rig.Step(120);

        // A lost open followed by a close still opens once, then destroys in reliable order.
        Assert.Single(rig.Clients[0].Notify.Opened, c => c.Actor is TestTemporary);
        if (destroyDuringOutage)
        {
            Assert.Null(rig.Clients[0].Copy<TestTemporary>(grenade));
            foreach (TestTemporary survivor in rig.Clients[0].World.Level.AllActors<TestTemporary>())
                Assert.True(survivor.bDeleteMe, "A projectile survived its authoritative close.");
        }
        else
        {
            TestTemporary copy = Assert.IsType<TestTemporary>(rig.Clients[0].Copy<TestTemporary>(grenade));
            Assert.False(copy.bDeleteMe);
            rig.Network.PauseLink("server", "client0", rig.Clock.NowMicroseconds + 300_000);
            grenade.Destroy();
            rig.Step(120);
            Assert.True(copy.bDeleteMe);
        }
        Assert.Null(rig.ServerSide(0).FindActorChannel(grenade));
        Assert.Equal(EConnectionState.USOCK_Open, rig.Clients[0].ToServer.State);
    }

    [Fact]
    public void PersistentDeliveryParticipatesInCompatibilityHash()
    {
        NetClassRegistry baseline = NetRig.CreateRegistry();
        NetClassRegistry reliable = NetRig.CreateRegistry();
        reliable.Extend<TestTemporary>(new RepExtension { PersistentActorChannel = true });
        Assert.NotEqual(baseline.ComputeProtocolHash(), reliable.ComputeProtocolHash());
    }
}
