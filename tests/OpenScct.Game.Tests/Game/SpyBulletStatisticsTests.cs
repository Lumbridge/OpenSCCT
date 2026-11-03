using System.Collections.Generic;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Game.Tests.Game;

public sealed class SpyBulletStatisticsTests
{
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void OnlyASuccessfulLivingMercHitCountsAsATarget(bool alive, int expected)
    {
        var (world, players) = DeathLivesRespawnTests.Match(MatchLifecycleTests.SteeEStory,
            "?Name=Spy?Team=0", "?Name=Merc?Team=1");
        CollisionWorld collision = CollisionWorld.Build(new CollisionDataBuilder().Build());
        collision.BindLevel(world.Level);
        world.Level.SetService<ICollisionWorld>(collision);
        Pawn spy = players[0].Pawn!, merc = players[1].Pawn!;
        spy.SetLocation(new FVector(0, 0, 100));
        merc.SetLocation(new FVector(1000, 0, 100));
        merc.Health = alive ? 100 : 0;
        var launcher = world.Level.SpawnActor(new SSpyBulletLauncher { Charge = 2 }, spy.Location, default, spy, spy)!;
        launcher.GiveTo(spy);
        launcher.ThrowBullet(new FVector(100, 0, 100), new FVector(2000, 0, 100));
        Assert.Equal(expected, players[0].PlayerReplicationInfo!.Stats!.Spy_SpyBulletTargets);
        Assert.Equal(expected, players[0].PlayerReplicationInfo!.Stats!.Feats.GetValueOrDefault(FeatKeys.SpyBulletHits));
    }
}
