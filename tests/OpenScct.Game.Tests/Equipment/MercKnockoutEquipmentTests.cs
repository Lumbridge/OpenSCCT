using OpenScct.Core;
using OpenScct.Game.Animation;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class MercKnockoutEquipmentTests
{
    [Theory]
    [InlineData("s_StandUp")]
    [InlineData("s_Reanimating")]
    public void WakingRestoresTheRifleEvenWithoutTheBodyDrawNotify(string wakeState)
    {
        var scene = GadgetKit.Scene();
        var (pc, pawn) = GadgetKit.Local(scene, false, new FVector(0, 0, 100));
        var rifle = scene.Give(new SWeaponDefense(), pawn);
        rifle.GiveTo(pawn);
        pc.SPawnType = pawn.SPawnType;
        pc.GotoState(new Name(wakeState));
        pawn.WeaponLoaded = 0;
        rifle.bDrawWeapon = false;
        pc.GotoWaitingState();
        scene.Run(2);
        Assert.Equal(2, pawn.WeaponLoaded);
        Assert.True(rifle.bDrawWeapon);
        Assert.False(rifle.IsInState(new Name("s_IdleInHolster")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FallingUnconsciousOnlyRemovesAnAlreadyWornGasMask(bool initiallyWorn)
    {
        var scene = GadgetKit.Scene();
        var (pc, pawn) = GadgetKit.Local(scene, false, new FVector(0, 0, 100));
        var mask = GadgetKit.Gadget(scene, pawn, new SGadGasMask());
        scene.Run(1);
        if (initiallyWorn) mask.Use(1);
        float air = mask.MaskDuration;

        pc.GotoState(new Name("s_LyingOnGround"));

        Assert.False(pawn.bGasMask);
        scene.Run(1);
        Assert.Equal(air, mask.MaskDuration);
    }

    [Theory]
    [InlineData("NotifyDrawUndrawWeapon", "NotifyDrawUndrawWeapon")]
    [InlineData("NotifyUnDrawWeapon_GRAB", "NotifyDrawWeapon_GRAB")]
    public void MercBodyAnimationNotifiesLowerAndRestoreTheWeapon(string lower, string raise)
    {
        var scene = GadgetKit.Scene();
        var (_, pawn) = GadgetKit.Local(scene, false, new FVector(0, 0, 100));
        var rifle = scene.Give(new SWeaponDefense(), pawn);
        rifle.GiveTo(pawn);
        pawn.WeaponLoaded = 2;
        MovementNotifies.Install(scene.Level);

        PawnAnimNotifies.RunScript(pawn, new Name(lower));
        Assert.Equal(0, pawn.WeaponLoaded);
        Assert.False(rifle.ViewAnim.Sequence(0).IsNone);

        PawnAnimNotifies.RunScript(pawn, new Name(raise));
        Assert.Equal(2, pawn.WeaponLoaded);
        Assert.True(rifle.bDrawWeapon);
    }
}
