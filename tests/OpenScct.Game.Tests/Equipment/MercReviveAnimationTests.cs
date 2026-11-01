using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class MercReviveAnimationTests
{
    private static (CombatScene Scene, SWeaponDefense Rifle) Merc()
    {
        var scene = new CombatScene();
        (_, SBasePawn pawn) = scene.Player(false, new FVector(0f, 0f, 100f));
        SWeaponDefense rifle = scene.Give(new SWeaponDefense(), pawn);
        rifle.GiveTo(pawn);
        pawn.WeaponLoaded = 2;
        scene.Run(0.1f);
        return (scene, rifle);
    }

    [Fact]
    public void FinishedReviveDoesNotHoldItsPoseOverTheIdleRifle()
    {
        (CombatScene scene, SWeaponDefense rifle) = Merc();
        rifle.AnimateReanimate();
        scene.Run(1.2f);

        Assert.True(rifle.IsInState(new Name("s_Selected")));
        Assert.False(rifle.ViewAnim.IsAnimating(SWeaponDefense.ACTIONCHANNEL));
        Assert.Equal(0f, rifle.ViewAnim.Alpha(SWeaponDefense.ACTIONCHANNEL));
    }

    [Fact]
    public void RevivePlaysItsAuthoredDurationBeforeReleasingTheArms()
    {
        (CombatScene scene, SWeaponDefense rifle) = Merc();
        rifle.AnimateReanimate();
        // SWeaponFirst/Default.anim.json: 16 frames at 15 fps, ending at frame 15.
        Assert.Equal(1f, rifle.ViewAnim.Remaining(SWeaponDefense.ACTIONCHANNEL));
        scene.Run(0.5f);
        Assert.True(rifle.ViewAnim.IsAnimating(SWeaponDefense.ACTIONCHANNEL));
        Assert.Equal(1f, rifle.ViewAnim.Alpha(SWeaponDefense.ACTIONCHANNEL));
        scene.Run(0.6f);
        Assert.False(rifle.ViewAnim.IsAnimating(SWeaponDefense.ACTIONCHANNEL));
        Assert.Equal(0f, rifle.ViewAnim.Alpha(SWeaponDefense.ACTIONCHANNEL));
    }
}
