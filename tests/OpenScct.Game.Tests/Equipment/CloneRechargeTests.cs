using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Svm;
using Xunit;
using static OpenScct.Game.Tests.Equipment.GadgetKit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class CloneRechargeTests
{
    [Fact]
    public void DeploymentRechargesOnceAfterOneMinuteAndCannotBeRefilledEarly()
    {
        var scene = Scene();
        var (pc, pawn) = Local(scene, true, new FVector(0, 0, 100));
        var clone = Gadget(scene, pawn, new CloneGadget());
        pawn.SetPhysics(EPhysics.PHYS_Walking);
        clone.Use(1);
        Assert.Single(All<SpyClone>(scene));
        Assert.Equal(0, clone.Charge);
        Assert.Equal(60, clone.RechargeRemaining);
        clone.Use(1); clone.FullReload();
        Assert.Equal(0, clone.Charge);
        scene.Run(59);
        Assert.Equal(0, clone.Charge);
        Assert.InRange(clone.RechargeRemaining, .5f, 1.1f);
        scene.Run(1.2f);
        Assert.Equal(1, clone.Charge);
        Assert.Equal(0, clone.RechargeRemaining);
    }
}
