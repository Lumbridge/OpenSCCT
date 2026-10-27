using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Game;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>Kill credit, statistics, friendly fire and the taser path in a running match (damage-pipeline.md, shock-and-taser.md).</summary>
public sealed class KillCreditTests
{
    private static (GameWorld W, SPlayerController Spy, SPlayerController Merc) Match(params string[] more)
    {
        string[] joins = new string[2 + more.Length];
        joins[0] = "?Name=S?Team=0";
        joins[1] = "?Name=M?Team=1";
        more.CopyTo(joins, 2);
        (GameWorld w, SPlayerController[] p) = DeathLivesRespawnTests.Match(MatchLifecycleTests.SteeEStory, joins);
        w.Run(5f);
        return (w, p[0], p[1]);
    }

    [Fact]
    public void ALethalRifleHitCreditsTheShooterAndCountsTheDamage()
    {
        (_, SPlayerController spy, SPlayerController merc) = Match();
        Pawn victim = spy.Pawn!;
        spy.PawnTakeDamage(40, merc.Pawn, victim.Location, new FVector(-1f, 0f, 0f), SDamageType.SDTBase, 1, 70);
        Assert.True(victim.Health <= 0);
        Assert.Equal(1, merc.PlayerReplicationInfo!.Kills);
        Assert.Equal(1, spy.PlayerReplicationInfo!.Deaths);
        Assert.Equal(100, merc.PlayerReplicationInfo!.Stats!.DamageGiven);
        Assert.Equal(100, spy.PlayerReplicationInfo!.Stats!.DamageTaken);
    }

    [Fact]
    public void FriendlyFireHurtsButIsNotCounted()
    {
        (GameWorld w, SPlayerController[] p) = DeathLivesRespawnTests.Match(MatchLifecycleTests.SteeEStory,
            "?Name=S?Team=0", "?Name=M?Team=1", "?Name=M2?Team=1");
        w.Run(5f);
        SPlayerController merc = p[1];
        SPlayerController mate = p[2];
        var victim = (SBasePawn)merc.Pawn!;
        int before = victim.Health;
        merc.PawnTakeDamage(25, mate.Pawn, victim.Location, new FVector(0f, 0f, 1f), SDamageType.SDTBase, 6, 0);
        Assert.Equal(before - 25, victim.Health);
        Assert.Equal(0, merc.PlayerReplicationInfo!.Stats!.DamageTaken);
        Assert.Equal(0, mate.PlayerReplicationInfo!.Stats!.DamageGiven);
    }

    [Fact]
    public void TheTaserPathDeals51AndAcceptsTheShockOnTheServer()
    {
        (GameWorld w, SPlayerController spy, SPlayerController merc) = Match();
        var net = new RecordingCombatNet();
        w.Level.SetService<OpenScct.Game.Combat.ICombatNet>(net);
        var victim = (SBasePawn)spy.Pawn!;
        int before = victim.Health;
        spy.PawnTakeDamage(0, merc.Pawn, victim.Location, new FVector(1f, 0f, 0f), SDamageType.SDTStickyShocker, -10);
        Assert.Equal(before - 51, victim.Health);
        Assert.Equal(w.Level.TimeSeconds, spy.TimeShock);
        Assert.Contains(net.ToClients, c => ReferenceEquals(c.To, spy) && c.Call.Function == new Name("ClientStickyShock") && c.Call.Int1 == -10);
    }
}
