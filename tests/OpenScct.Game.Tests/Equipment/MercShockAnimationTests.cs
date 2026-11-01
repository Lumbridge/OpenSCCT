using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class MercShockAnimationTests
{
    private static (PawnRig Rig, SWeaponDefense Rifle) MercWithRifle()
    {
        PawnRig rig = Rig(PawnTeam.Merc);
        var rifle = rig.Level.SpawnActor(new SWeaponDefense(), rig.Pawn.Location, rig.Pawn.Rotation)!;
        rifle.GiveTo(rig.Pawn);
        rig.Pawn.WeaponLoaded = 2;
        rifle.GotoState(new Name("s_Selected"));
        rig.Hold(new PlayerInputFrame(), 10);
        return (rig, rifle);
    }

    [Theory]
    [InlineData("s_Selected")]
    [InlineData("s_Firing")]
    [InlineData("s_Charging_End")]
    [InlineData("s_IdleInHolster")]
    public void LeavingElectricForAnotherWeaponStateClearsItsViewModelLoop(string nextState)
    {
        (_, SWeaponDefense rifle) = MercWithRifle();
        rifle.GotoState(new Name("s_Dmg_Electric"));
        Assert.Equal(ViewModelClips.FirstElecStFd2, rifle.ViewAnim.Sequence(SWeaponDefense.PAINCHANNEL));
        Assert.Equal(1f, rifle.ViewAnim.Alpha(SWeaponDefense.PAINCHANNEL));
        Assert.True(rifle.ViewAnim.IsAnimating(SWeaponDefense.PAINCHANNEL));

        rifle.GotoState(new Name(nextState));

        Assert.Equal(0f, rifle.ViewAnim.Alpha(SWeaponDefense.PAINCHANNEL));
        Assert.False(rifle.ViewAnim.IsAnimating(SWeaponDefense.PAINCHANNEL));
    }

    [Fact]
    public void ChargeRecoveryCannotLeaveTheElectricLoopOverTheWorkingRifle()
    {
        (PawnRig rig, SWeaponDefense rifle) = MercWithRifle();
        rig.Controller.GotoState(new Name("s_PlayerCharging"));
        rig.Controller.ClientStickyShock(2, true);
        Assert.True(rifle.IsInState(new Name("s_Dmg_Electric")));

        // The charge's own timer changes the weapon to its run-out state before
        // the shock timers expire. That exit must remove the electric overlay.
        rig.Hold(new PlayerInputFrame(), 65);
        Assert.False(rifle.IsInState(new Name("s_Dmg_Electric")));
        Assert.Equal(0f, rifle.ViewAnim.Alpha(SWeaponDefense.PAINCHANNEL));
        Assert.False(rifle.ViewAnim.IsAnimating(SWeaponDefense.PAINCHANNEL));

        rig.Hold(new PlayerInputFrame(), 300);
        Assert.False(rig.Pawn.bStickyShocking);
        Assert.Equal(0, rig.Controller.iDontUpdatePadInput);
        int ammo = rifle.ClipAmmo;
        rig.Controller.eventFire(1f);
        Assert.Equal(ammo - 1, rifle.ClipAmmo);
        Assert.Equal(0f, rifle.ViewAnim.Alpha(SWeaponDefense.PAINCHANNEL));
    }

    [Fact]
    public void HolsteringDuringShockCannotRestoreTheElectricLoopAfterRecovery()
    {
        (PawnRig rig, SWeaponDefense rifle) = MercWithRifle();
        rig.Controller.ClientStickyShock(2, true);

        // A traversal/interaction can holster the rifle before the shock ends.
        // Its holster state then outlives the controller's one-shot cleanup.
        rig.Pawn.WeaponLoaded = 0;
        rifle.eventAnimateUnDraw();
        rig.Hold(new PlayerInputFrame(), 360);
        Assert.False(rig.Pawn.bStickyShocking);
        Assert.Equal(0, rig.Controller.iDontUpdatePadInput);
        Assert.True(rifle.IsInState(new Name("s_IdleInHolster")));

        rig.Pawn.WeaponLoaded = 2;
        rifle.eventAnimateDraw();
        rig.Hold(new PlayerInputFrame(), 120);
        Assert.True(rifle.IsInState(new Name("s_Selected")));
        int ammo = rifle.ClipAmmo;
        rig.Controller.eventFire(1f);
        Assert.Equal(ammo - 1, rifle.ClipAmmo);
        Assert.Equal(0f, rifle.ViewAnim.Alpha(SWeaponDefense.PAINCHANNEL));
        Assert.False(rifle.ViewAnim.IsAnimating(SWeaponDefense.PAINCHANNEL));
    }

    [Fact]
    public void LeavingElectricPreservesAPainClipThatAlreadyReplacedTheLoop()
    {
        (_, SWeaponDefense rifle) = MercWithRifle();
        rifle.GotoState(new Name("s_Dmg_Electric"));
        rifle.AnimateBlast();
        rifle.GotoState(new Name("s_Selected"));

        Assert.Equal(ViewModelClips.FirstStrkStFd2, rifle.ViewAnim.Sequence(SWeaponDefense.PAINCHANNEL));
        Assert.Equal(1f, rifle.ViewAnim.Alpha(SWeaponDefense.PAINCHANNEL));
        Assert.True(rifle.ViewAnim.IsAnimating(SWeaponDefense.PAINCHANNEL));
    }
}
