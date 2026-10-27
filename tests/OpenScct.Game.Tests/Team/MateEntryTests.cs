using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Objectives;
using Xunit;

namespace OpenScct.Game.Tests.Team;

/// <summary>
/// The Use menu's mate entries (spec/objectives/team-interactions.md): the decoded mate search of their CanUse, Heal
/// Mate, Unspy Mate and DetectedByMarqueurSpy, the menu order, and ReplicateMate's trip to the server.
/// </summary>
public sealed class MateEntryTests
{
    private const int Behind = 32768;

    /// <summary>A spy at the origin facing +X and a team mate <paramref name="dx"/> in front, facing <paramref name="mateYaw"/>.</summary>
    private static (ObjWorld W, TestPC Pc, TestPC Mate) Pair(float dx = 120f, int mateYaw = Behind, bool spy = true, bool mateSpy = true)
    {
        var w = new ObjWorld();
        TestPC pc = w.AddPlayer(spy, default);
        TestPC mate = w.AddPlayer(mateSpy, new FVector(dx, 0f, 0f), new FRotator(0, mateYaw, 0));
        return (w, pc, mate);
    }

    private static SHealMateInteraction Heal(TestPC pc) => (SHealMateInteraction)pc.IManager!.Interactions[2]!;

    private static SRemoveSpyBulletInteraction Unspy(TestPC pc) => (SRemoveSpyBulletInteraction)pc.IManager!.Interactions[1]!;

    private static SBasePawn Body(TestPC pc) => (SBasePawn)pc.Pawn!;

    [Fact]
    public void HealMateIsOfferedForAHurtMateInFrontWhoFacesThePlayer()
    {
        (_, TestPC pc, TestPC mate) = Pair();
        Body(mate).Health = 40;
        Assert.True(Heal(pc).CanUse(pc));
        Assert.Same(mate.Pawn, Heal(pc).Mate);
        Assert.True(pc.IManager!.HasUsableInteractions());
    }

    [Fact]
    public void HealMateNeedsAHurtMate()
    {
        (_, TestPC pc, _) = Pair();
        Assert.False(Heal(pc).CanUse(pc));
        Assert.False(pc.IManager!.HasUsableInteractions());
    }

    [Theory]
    [InlineData(199f, Behind, true)]
    [InlineData(201f, Behind, false)]
    [InlineData(-120f, 0, false)]
    [InlineData(120f, 0, false)]
    [InlineData(120f, Behind + 4000, true)]
    [InlineData(120f, Behind + 8000, false)]
    public void TheMateSearchReachesTwoHundredInFrontFacingBothWays(float dx, int mateYaw, bool found)
    {
        // 0.75 is about 41 degrees: 4000 (22 degrees) is in, 8000 (44 degrees) is out.
        (_, TestPC pc, TestPC mate) = Pair(dx, mateYaw);
        Body(mate).Health = 40;
        Assert.Equal(found, Heal(pc).CanUse(pc));
    }

    [Fact]
    public void TheMateSearchSkipsTheOtherSideALyingMateAndADeadOne()
    {
        (_, TestPC pc, TestPC merc) = Pair(mateSpy: false);
        Body(merc).Health = 40;
        Assert.False(Heal(pc).CanUse(pc));

        (_, TestPC pc2, TestPC mate) = Pair();
        Body(mate).Health = 40;
        Body(mate).bLyingOnGround = true;
        Assert.False(Heal(pc2).CanUse(pc2));
        Body(mate).bLyingOnGround = false;
        Body(mate).Health = 0;
        Assert.False(Heal(pc2).CanUse(pc2));
    }

    [Fact]
    public void HealMateNeedsHealShotsWhichTheBuildNeverSpends()
    {
        (_, TestPC pc, TestPC mate) = Pair();
        Body(mate).Health = 40;
        Assert.Equal(5, pc.iHealShot);
        pc.iHealShot = 0;
        Assert.False(Heal(pc).CanUse(pc));
        pc.iHealShot = 5;
        Assert.True(Heal(pc).CanUse(pc));
        InteractionMenuRules.InteractionInit(pc, Heal(pc), 0);
        Assert.Equal(5, pc.iHealShot);
        Assert.Equal(new SPawnAttaque().Health, Body(mate).Health);
    }

    [Fact]
    public void MercsHealMercsToTheirClassHealth()
    {
        (_, TestPC pc, TestPC mate) = Pair(spy: false, mateSpy: false);
        Body(mate).Health = 10;
        Assert.True(Heal(pc).CanUse(pc));
        InteractionMenuRules.InteractionInit(pc, Heal(pc), 0);
        Assert.Equal(new SPAWNDEFENSE().Health, Body(mate).Health);
    }

    [Fact]
    public void UnspyMateIsOfferedForAMarkedMateAndTakesTheMarkOff()
    {
        (ObjWorld w, TestPC pc, TestPC mate) = Pair();
        TestPC merc = w.AddPlayer(spy: false, new FVector(2000f, 0f, 0f));
        Assert.False(Unspy(pc).CanUse(pc));
        Body(mate).HitBySpyBullet(1);
        Assert.True(merc.bCanListenSpyBulletedGuy);
        Assert.True(Unspy(pc).CanUse(pc));
        // A full-health mate: Unspy Mate and Exit, no Heal Mate.
        Assert.False(Heal(pc).CanUse(pc));

        w.Run(1f);
        InteractionMenuRules.InteractionInit(pc, Unspy(pc), 0);

        SBasePawn m = Body(mate);
        Assert.False(m.bSpyBulletOn);
        Assert.False(m.bForceSpied);
        Assert.False(merc.bCanListenSpyBulletedGuy);
        // The mark's clock is put TimeSpyBullet back, so the pawn's own tick ends it at once.
        Assert.Equal(w.Level.TimeSeconds - m.TimeSpyBullet, m.fTimeSpy, 3);
    }

    [Fact]
    public void TheOtherSideKeepsListeningWhileAnotherMateIsMarked()
    {
        (ObjWorld w, TestPC pc, TestPC mate) = Pair();
        TestPC third = w.AddPlayer(spy: true, new FVector(-800f, 0f, 0f));
        TestPC merc = w.AddPlayer(spy: false, new FVector(2000f, 0f, 0f));
        Body(mate).HitBySpyBullet(1);
        Body(third).HitBySpyBullet(1);
        Body(mate).DetectedByMarqueurSpy(0, false);
        Assert.False(Body(mate).bSpyBulletOn);
        Assert.True(merc.bCanListenSpyBulletedGuy);
    }

    [Fact]
    public void DetectedByMarqueurSpyMarksAndHolds()
    {
        (ObjWorld w, _, TestPC mate) = Pair();
        TestPC merc = w.AddPlayer(spy: false, new FVector(2000f, 0f, 0f));
        w.Run(0.5f);
        Body(mate).DetectedByMarqueurSpy(1, true);
        Assert.True(Body(mate).bSpyBulletOn);
        Assert.True(Body(mate).bForceSpied);
        Assert.Equal(1f, Body(mate).iTeamSpying);
        Assert.Equal(w.Level.TimeSeconds, Body(mate).fTimeSpy);
        Assert.True(merc.bCanListenSpyBulletedGuy);
    }

    [Fact]
    public void TheMenuListsTheMateEntriesInSlotOrderUnspyBeforeHeal()
    {
        (_, TestPC pc, TestPC mate) = Pair();
        Body(mate).Health = 40;
        Body(mate).HitBySpyBullet(1);
        SInteractionManager menu = pc.IManager!;
        Assert.True(menu.SelectFirstViewAbleItem());
        Assert.Same(Unspy(pc), menu.GetCurrentInteraction());
        Assert.True(menu.SelectNextItem());
        Assert.Same(Heal(pc), menu.GetCurrentInteraction());
        Assert.True(menu.SelectNextItem());
        Assert.IsType<SLeaveInteraction>(menu.GetCurrentInteraction());
    }

    private sealed class RecordingCombatNet : ICombatNet
    {
        public readonly List<CombatCall> Sent = new();

        public void ClientCall(PlayerController Owner, in CombatCall Call)
        {
        }

        public void ServerCall(PlayerController Sender, in CombatCall Call) => Sent.Add(Call);
    }

    [Fact]
    public void AClientsMateSearchReachesTheServerCopyThroughReplicateMate()
    {
        (ObjWorld w, TestPC pc, TestPC mate) = Pair();
        var net = new RecordingCombatNet();
        w.Level.SetService<ICombatNet>(net);
        SHealMateInteraction heal = Heal(pc);
        heal.Role = ENetRole.ROLE_SimulatedProxy;
        Body(mate).Health = 40;

        Assert.True(heal.CanUse(pc));
        CombatCall call = Assert.Single(net.Sent);
        Assert.Equal("ReplicateMate", call.Function.ToString());
        Assert.Same(heal, call.Target);
        Assert.Same(mate.Pawn, call.Actor1);
        // The same mate again sends nothing.
        Assert.True(heal.CanUse(pc));
        Assert.Single(net.Sent);

        // On the server the call sets the entry's mate (the entry belongs to the sender).
        heal.Role = ENetRole.ROLE_Authority;
        heal.Mate = null;
        pc.ReceiveCombatCall(call, FromOwner: true);
        Assert.Same(mate.Pawn, heal.Mate);
    }
}
