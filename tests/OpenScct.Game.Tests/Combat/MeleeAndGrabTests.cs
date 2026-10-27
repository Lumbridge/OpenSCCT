using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>The spy grab outcomes, the elbow, the charge and the finishing move (spy-grab.md, melee, knockout).</summary>
public sealed class MeleeAndGrabTests
{
    private static (CombatScene Scene, SPlayerController SpyPc, SBasePawn Spy, SPlayerController MercPc, SBasePawn Merc) Pair(
        float gap = 70f)
    {
        var scene = new CombatScene();
        (SPlayerController spyPc, SBasePawn spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        (SPlayerController mercPc, SBasePawn merc) = scene.Player(false, new FVector(gap, 0f, 100f));
        scene.Run(5f);
        return (scene, spyPc, spy, mercPc, merc);
    }

    private static void Link(SBasePawn spy, SBasePawn merc)
    {
        spy.m_AttackTarget = merc;
        merc.m_AttackedBy = spy;
    }

    [Fact]
    public void TheGrabReachNeedsTheSpyBehindAndCloseEnough()
    {
        (_, SPlayerController spyPc, _, _, SBasePawn merc) = Pair();
        merc.iLastWakeTime = -100;
        Assert.True(spyPc.CanGrabPawn(merc));
        merc.Rotation = new FRotator(0, 32768, 0);
        Assert.False(spyPc.CanGrabPawn(merc));
    }

    [Fact]
    public void ANeckBreakKillsTheMercAndCountsTheGrabDeath()
    {
        (_, SPlayerController spyPc, SBasePawn spy, SPlayerController mercPc, SBasePawn merc) = Pair();
        Link(spy, merc);
        spyPc.AskServerToUnGrabPawn(GrabReleaseMethode_.Normal);
        Assert.Equal(0, merc.Health);
        Assert.Equal(new Name("s_GrabbedByPlayer_Released_STRANGLE"), mercPc.GetStateName());
        Assert.Equal(new Name("s_ReleaseGrabbedPlayer"), spyPc.GetStateName());
    }

    [Fact]
    public void AKnockOutCosts51AndKnocksOutAHealthyMerc()
    {
        (_, SPlayerController spyPc, SBasePawn spy, SPlayerController mercPc, SBasePawn merc) = Pair();
        Link(spy, merc);
        int before = merc.Health;
        spyPc.AskServerToUnGrabPawn(GrabReleaseMethode_.KO);
        Assert.Equal(before - 51, merc.Health);
        Assert.Equal(new Name("s_GrabbedByPlayer_Released_KO"), mercPc.GetStateName());
        Assert.Equal(new Name("s_ReleaseGrabbedPlayer_KO"), spyPc.GetStateName());
    }

    [Fact]
    public void AKnockOutOnAWeakMercBecomesAChoke()
    {
        (_, SPlayerController spyPc, SBasePawn spy, SPlayerController mercPc, SBasePawn merc) = Pair();
        Link(spy, merc);
        merc.Health = 40;
        spyPc.AskServerToUnGrabPawn(GrabReleaseMethode_.KO);
        Assert.Equal(0, merc.Health);
        Assert.Equal(new Name("s_GrabbedByPlayer_Released_STRANGLE"), mercPc.GetStateName());
        Assert.Equal(new Name("s_ReleaseGrabbedPlayer_Strangle"), spyPc.GetStateName());
    }

    [Fact]
    public void ACancelledGrabUnlinksBothPlayers()
    {
        (_, SPlayerController spyPc, SBasePawn spy, _, SBasePawn merc) = Pair();
        Link(spy, merc);
        spyPc.AskServerToUnGrabPawn(GrabReleaseMethode_.Grabbing_Cancelled);
        Assert.Null(spy.m_AttackTarget);
        Assert.Null(merc.m_AttackedBy);
        Assert.Equal(100, merc.Health);
    }

    [Fact]
    public void TheReleaseYawSnapsToTheNearestQuarter()
    {
        Assert.Equal(0, SPlayerController.SnapYaw(100));
        Assert.Equal(0, SPlayerController.SnapYaw(-8000));
        Assert.Equal(16384, SPlayerController.SnapYaw(9000));
        Assert.Equal(32768, SPlayerController.SnapYaw(40000));
        Assert.Equal(-16384, SPlayerController.SnapYaw(50000));
    }

    [Fact]
    public void DampRotationTurnsAtMostRateTimesDt()
    {
        Assert.Equal(333, SPlayerController.DampRotation(0, 10000, 20000f, 1f / 60f));
        Assert.Equal(-333, SPlayerController.DampRotation(0, -10000, 20000f, 1f / 60f));
        Assert.Equal(100, SPlayerController.DampRotation(0, 100, 20000f, 1f / 60f));
    }

    [Fact]
    public void AnElbowKnocksAStandingVictimBack()
    {
        (_, SPlayerController spyPc, SBasePawn spy, SPlayerController mercPc, SBasePawn merc) = Pair(100f);
        mercPc.GotoState(new Name("s_PlayerWalkingSt2H"));
        spyPc.AskServerToElbowAttack(merc, 0, spy, FVector.Zero, merc.Location - spy.Location, SDamageType.SDamage_ElbowShock);
        Assert.Equal(new Name("s_ElbowShot_GoBackward"), mercPc.GetStateName());
        Assert.Same(spy, merc.m_AttackedBy);
        Assert.Equal(new Name("s_ElbowShooting"), spyPc.GetStateName());
    }

    [Fact]
    public void AnElbowKnocksACrouchedVictimDown()
    {
        (_, SPlayerController spyPc, SBasePawn spy, SPlayerController mercPc, SBasePawn merc) = Pair(100f);
        mercPc.GotoState(new Name("s_PlayerWalkingSt2H"));
        merc.bIsCrouched = true;
        spyPc.AskServerToElbowAttack(merc, 0, spy, FVector.Zero, merc.Location - spy.Location, SDamageType.SDamage_ElbowShock);
        Assert.Equal(new Name("s_FallDown"), mercPc.GetStateName());
    }

    [Fact]
    public void AnElbowOutOfServerReachDoesNothing()
    {
        (_, SPlayerController spyPc, SBasePawn spy, SPlayerController mercPc, SBasePawn merc) = Pair(400f);
        mercPc.GotoState(new Name("s_PlayerWalkingSt2H"));
        spyPc.AskServerToElbowAttack(merc, 0, spy, FVector.Zero, merc.Location - spy.Location, SDamageType.SDamage_ElbowShock);
        Assert.Equal(new Name("s_PlayerWalkingSt2H"), mercPc.GetStateName());
    }

    [Fact]
    public void TheChargeSlowDownLastsTwoSecondsAndRecoversOverHalfASecond()
    {
        (CombatScene scene, _, _, SPlayerController mercPc, _) = Pair();
        mercPc.fLastChargingTime = scene.Time;
        Assert.Equal(0.5f, mercPc.PostChargeSpeedFactor());
        mercPc.fLastChargingTime = scene.Time - 2.25f;
        Assert.Equal(0.75f, mercPc.PostChargeSpeedFactor(), 3);
        mercPc.fLastChargingTime = scene.Time - 10f;
        Assert.Equal(1f, mercPc.PostChargeSpeedFactor());
    }

    [Fact]
    public void TheFinishingHitDeals300ToTheDownedSpy()
    {
        (_, _, SBasePawn spy, SPlayerController mercPc, SBasePawn merc) = Pair(150f);
        merc.m_PawnToTerminate = spy;
        spy.m_PawnToTerminate = merc;
        mercPc.GotoState(new Name("s_TransitionTPK"));
        Assert.True(spy.Health <= 0);
        Assert.Null(merc.m_PawnToTerminate);
        Assert.Equal(new Name("s_PlayerWalkingSt2H"), mercPc.GetStateName());
    }
}
