using System;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Objectives;
using Xunit;

namespace OpenScct.Game.Tests.Team;

/// <summary>
/// The Use menu's mate entries are asked every frame by the HUD and every tick by the bots' team play. Their CanUse
/// read the mate's and the gadgets' class defaults by making a new pawn or gadget each call (a whole pawn with its
/// animation channels), which drove most of a match's garbage collections. After warm-up they allocate nothing.
/// </summary>
public sealed class MateSearchAllocationTests
{
    private const int Behind = 32768;

    private static (TestPC Pc, TestPC Mate) Pair()
    {
        var w = new ObjWorld();
        TestPC pc = w.AddPlayer(spy: true, default);
        TestPC mate = w.AddPlayer(spy: true, new FVector(120f, 0f, 0f), new FRotator(0, Behind, 0));
        ((SBasePawn)mate.Pawn!).Health = 40;
        return (pc, mate);
    }

    [Fact]
    public void TheMateEntriesAllocateNothingAfterWarmUp()
    {
        (TestPC pc, _) = Pair();
        SInteractionManager menu = pc.IManager!;
        var backpack = (SBackPackInteraction)menu.Interactions[0]!;
        var heal = (SHealMateInteraction)menu.Interactions[2]!;

        bool expectedHeal = heal.CanUse(pc);
        bool expectedBackpack = backpack.CanUse(pc);
        _ = menu.HasUsableInteractions();
        Assert.True(expectedHeal);

        long before = GC.GetAllocatedBytesForCurrentThread();
        int wrong = 0;
        for (int i = 0; i < 1000; i++)
        {
            // Plain comparisons: the assertions box their arguments.
            wrong += heal.CanUse(pc) == expectedHeal ? 0 : 1;
            wrong += backpack.CanUse(pc) == expectedBackpack ? 0 : 1;
            _ = menu.HasUsableInteractions();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, wrong);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void TheDefaultHealthIsTheClassDefaultsOne()
    {
        (_, TestPC mate) = Pair();
        var pawn = (SBasePawn)mate.Pawn!;
        Assert.Equal(new SPawnAttaque().Health, SHealMateInteraction.DefaultHealth(pawn));
        pawn.Health = 1;
        Assert.Equal(new SPawnAttaque().Health, SHealMateInteraction.DefaultHealth(pawn));
    }

    [Fact]
    public void TheDefaultChargeIsTheGadgetClassDefaultsOne()
    {
        var gadget = new SGadSmokeGrenade();
        int full = new SGadSmokeGrenade().Charge;
        gadget.Charge = 0;
        Assert.Equal(full, SBackPackInteraction.DefaultCharge(gadget));
    }
}
