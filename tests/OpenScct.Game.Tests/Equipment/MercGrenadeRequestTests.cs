using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class MercGrenadeRequestTests
{
    private static (CombatScene Scene, SBasePawn Pawn, SWeaponDefense Weapon, SGadFragGrenade Gadget) Rig(bool sidearm)
    {
        var scene = GadgetKit.Scene();
        var (pc, pawn) = GadgetKit.Local(scene, false, new FVector(0, 0, 100));
        var primary = scene.Give(new SWeaponDefense(), pawn);
        primary.GiveTo(pawn);
        pawn.WeaponLoaded = 2;
        pc.GotoState(new Name("s_PlayerWalkingSt2H"));
        if (sidearm)
        {
            SidearmEquipment.Give(pawn, SidearmKind.Suppressed);
            Assert.True(SidearmEquipment.Select(pawn, 1, force: true));
        }
        var gadget = GadgetKit.Gadget(scene, pawn, new SGadFragGrenade());
        scene.Run(3f);
        return (scene, pawn, Assert.IsAssignableFrom<SWeaponDefense>(pawn.MyWeapon), gadget);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AQueuedRemoteThrowCannotExceedTheRemainingGrenadeCharges(bool sidearm)
    {
        var (scene, pawn, _, gadget) = Rig(sidearm);
        gadget.Charge = 1;
        gadget.ServerThrowGren(500, pawn.Location + new FVector(400, 0, 0), default, false);
        Assert.Single(GadgetKit.All<SProjFrag>(scene));
        Assert.Equal(0, gadget.Charge);
        scene.Run(gadget.RefireRate);

        // The second click can be queued before the client receives the zero-charge update.
        gadget.ServerThrowGren(500, pawn.Location + new FVector(400, 0, 0), default, false);

        Assert.Single(GadgetKit.All<SProjFrag>(scene));
        Assert.Equal(0, gadget.Charge);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ARejectedThrowDoesNotPlayTheGrenadeAnimation(bool sidearm, bool coolingDown)
    {
        var (_, _, weapon, gadget) = Rig(sidearm);
        gadget.Charge = coolingDown ? 1 : 0;
        if (coolingDown) gadget.LastUsed = gadget.Level.TimeSeconds;

        gadget.Use(1f);

        Assert.False(weapon.IsInState("s_FireGrenade"));
        Assert.NotEqual(weapon.AFirstReloGrenSt, weapon.ViewAnim.Sequence(SWeaponDefense.ACTIONCHANNEL));
    }
}
