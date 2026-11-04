using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Game;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

public sealed class ScoreboardCounterTests
{
    [Theory]
    [InlineData("s_PlayerWalking", 1)]
    [InlineData("s_GrabbedByPlayer", 0)]
    [InlineData("s_BlastFloor", 0)]
    [InlineData("s_BasePutMineOnWall", 0)]
    public void FlashesCountOnlyWhenTheMercStateAcceptsTheEffect(string state, int expected)
    {
        var (world, players) = DeathLivesRespawnTests.Match(MatchLifecycleTests.SteeEStory,
            "?Name=Spy?Team=0", "?Name=Merc?Team=1");
        world.Run(5f);
        var merc = players[1];
        merc.Pawn!.Rotation = default;
        merc.GotoState(new Name(state));
        merc.PawnTakeDamage(0, players[0].Pawn, merc.Pawn.Location + new FVector(1000, 0, 100), default, SDamageType.SDTFlash);
        Assert.Equal(expected, RoundStatCounters.Read(players[0].PlayerReplicationInfo!).GetValueOrDefault(FeatKeys.SpyFlashes));
    }

    [Fact]
    public void ShockStunsCountAcceptedEnemyHitsAndHeadshotsAreASubset()
    {
        var (world, players) = DeathLivesRespawnTests.Match(MatchLifecycleTests.SteeEStory,
            "?Name=Spy?Team=0", "?Name=Merc?Team=1");
        world.Run(5f);
        var spy = players[0]; var merc = players[1];
        merc.TimeShock = -100;
        merc.PawnTakeDamage(0, spy.Pawn, merc.Pawn!.Location, default, SDamageType.SDTStickyShocker, 1);
        merc.PawnTakeDamage(0, spy.Pawn, merc.Pawn!.Location, default, SDamageType.SDTStickyShocker, 2);
        var stats = RoundStatCounters.Read(spy.PlayerReplicationInfo!);
        Assert.Equal(1, stats[FeatKeys.SpyShockerStuns]);
        Assert.Equal(1, stats[FeatKeys.SpyShockerHeadshots]);
        Assert.Equal(0, stats.GetValueOrDefault(FeatKeys.SpyNeutralisations));
    }

    [Fact]
    public void GrabKnockoutThenLethalChokeIsTwoNeutralisations()
    {
        var (world, players) = DeathLivesRespawnTests.Match(MatchLifecycleTests.SteeEStory,
            "?Name=Spy?Team=0", "?Name=Merc?Team=1");
        world.Run(5f);
        var spy = players[0]; var merc = players[1];
        var a = (SBasePawn)spy.Pawn!; var b = (SBasePawn)merc.Pawn!;
        a.m_AttackTarget = b; b.m_AttackedBy = a;
        spy.AskServerToUnGrabPawn(GrabReleaseMethode_.KO);
        Assert.Equal(1, RoundStatCounters.Read(spy.PlayerReplicationInfo!)[FeatKeys.SpyGrabKos]);
        a.m_AttackTarget = b; b.m_AttackedBy = a;
        spy.AskServerToUnGrabPawn(GrabReleaseMethode_.Normal);
        var stats = RoundStatCounters.Read(spy.PlayerReplicationInfo!);
        Assert.Equal(1, stats[FeatKeys.EnemyKills]);
        Assert.Equal(2, stats[FeatKeys.SpyNeutralisations]);
        Assert.Equal(1, stats[FeatKeys.SpyChokes]);
    }

    [Theory]
    [InlineData(100, 1, 0)]
    [InlineData(40, 0, 1)]
    public void HeadJumpRecordsEitherKnockoutOrKillOnce(int health, int kos, int kills)
    {
        var (world, players) = DeathLivesRespawnTests.Match(MatchLifecycleTests.SteeEStory,
            "?Name=Spy?Team=0", "?Name=Merc?Team=1");
        world.Run(5f);
        world.Game.GameReplicationInfo!.bAvlb_SPY_LeathalDropTech = false;
        players[1].Pawn!.Health = health;
        players[0].performDropAttack((SBasePawn)players[1].Pawn!);
        var stats = RoundStatCounters.Read(players[0].PlayerReplicationInfo!);
        Assert.Equal(kos, stats.GetValueOrDefault(FeatKeys.SpyDropKos));
        Assert.Equal(kills, stats.GetValueOrDefault(FeatKeys.EnemyKills));
        Assert.Equal(1, stats[FeatKeys.SpyNeutralisations]);
        Assert.Equal(1, stats["career.dropattacks"]);
    }
}
