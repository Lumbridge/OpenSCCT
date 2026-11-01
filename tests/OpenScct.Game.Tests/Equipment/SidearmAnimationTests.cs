using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class SidearmAnimationTests
{
    private static (CombatScene Scene, SPlayerController PC, SBasePawn Pawn, SWeaponDefense Primary) Rig()
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(false, new FVector(0, 0, 100));
        var primary = scene.Give(new SWeaponDefense(), pawn);
        primary.GiveTo(pawn);
        SidearmEquipment.Give(pawn, SidearmKind.Suppressed);
        pawn.WeaponLoaded = 2;
        pc.GotoState(new Name("s_PlayerWalkingSt2H"));
        scene.Run(.1f);
        return (scene, pc, pawn, primary);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SwitchingDuringPainDoesNotRestoreAFrozenPose(bool sidearm, bool blast)
    {
        var (scene, pc, pawn, primary) = Rig();
        if (sidearm) { pc.SwapWeapon(); scene.Run(.6f); }
        var weapon = Assert.IsAssignableFrom<SWeaponDefense>(pawn.MyWeapon);
        if (blast) weapon.AnimateBlast(); else weapon.AnimatePain();
        Assert.True(weapon.ViewAnim.IsAnimating(SWeaponDefense.PAINCHANNEL));
        Assert.Equal(1f, weapon.ViewAnim.Alpha(SWeaponDefense.PAINCHANNEL));

        pc.SwapWeapon();
        Assert.NotSame(weapon, pawn.MyWeapon);
        scene.Run(.6f);
        pc.SwapWeapon();
        scene.Run(.6f);

        Assert.Same(weapon, pawn.MyWeapon);
        Assert.Equal(0f, weapon.ViewAnim.Alpha(SWeaponDefense.PAINCHANNEL));
        Assert.False(weapon.ViewAnim.IsAnimating(SWeaponDefense.PAINCHANNEL));
        Assert.True(weapon.ViewAnim.IsAnimating(SWeaponDefense.MVTCHANNEL));
        Assert.Equal(ViewModelClips.FirstWaitStFd2, weapon.ViewAnim.Sequence(SWeaponDefense.MVTCHANNEL));
    }

    [Fact]
    public void SwitchingOutOfScopeDoesNotRestoreTheStoppedScopePose()
    {
        var (scene, pc, pawn, primary) = Rig();
        pc.SwitchBinocularVision();
        scene.Run(.4f);
        Assert.True(pc.bBinocularVision);
        Assert.False(primary.bDrawWeapon);

        // Switching leaves scope and starts its lowering clip, then cancels that
        // clip as the primary is put away. It must also remove the overlay.
        pc.SwapWeapon();
        Assert.Same(pawn.SidearmWeapon, pawn.MyWeapon);
        Assert.False(pc.bBinocularVision);
        scene.Run(.6f);
        pc.SwapWeapon();
        scene.Run(.6f);

        Assert.Same(primary, pawn.MyWeapon);
        Assert.True(primary.bDrawWeapon);
        Assert.Equal(0f, primary.ViewAnim.Alpha(SWeaponDefense.SNIPECHANNEL));
        Assert.False(primary.ViewAnim.IsAnimating(SWeaponDefense.SNIPECHANNEL));
    }
}
