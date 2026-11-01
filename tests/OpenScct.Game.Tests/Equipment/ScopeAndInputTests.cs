using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>The fire key through the controller's states and the merc scope (merc-rifle.md rule 1, merc-scope.md).</summary>
public sealed class ScopeAndInputTests
{
    private static readonly Name WalkingSt2H = new("s_PlayerWalkingSt2H");
    private static readonly Name Scope = new("s_Binoculars_Def");

    private static (CombatScene Scene, SPlayerController PC, SBasePawn Merc, SWeaponDefense Rifle) Merc()
    {
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        SWeaponDefense rifle = scene.Give(new SWeaponDefense(), merc);
        rifle.GiveTo(merc);
        merc.WeaponLoaded = 2;
        scene.Run(0.1f);
        pc.GotoState(WalkingSt2H);
        return (scene, pc, merc, rifle);
    }

    [Fact]
    public void TheFireKeyFiresTheDrawnRifleInArmedWalking()
    {
        (_, SPlayerController pc, _, SWeaponDefense rifle) = Merc();
        pc.eventFire(1f);
        Assert.Equal(24, rifle.ClipAmmo);
    }

    [Fact]
    public void TheFireKeyIsRefusedWhileShockedOrHolstered()
    {
        (_, SPlayerController pc, SBasePawn merc, SWeaponDefense rifle) = Merc();
        merc.bStickyShocking = true;
        pc.eventFire(1f);
        merc.bStickyShocking = false;
        merc.WeaponLoaded = 0;
        pc.eventFire(1f);
        Assert.Equal(25, rifle.ClipAmmo);
    }

    [Fact]
    public void AStateThatIgnoresFireDropsTheKey()
    {
        (_, SPlayerController pc, _, SWeaponDefense rifle) = Merc();
        pc.GotoState(new Name("s_GrabbedByPlayer"));
        pc.eventFire(1f);
        Assert.Equal(25, rifle.ClipAmmo);
    }

    [Fact]
    public void TheScopeRaisesTheRifleInSingleShotAndRefusesShotsDuringTheRaise()
    {
        (CombatScene scene, SPlayerController pc, _, SWeaponDefense rifle) = Merc();
        pc.SwitchBinocularVision();
        Assert.Equal(Scope, pc.GetStateName());
        Assert.True(pc.bBinocularVision);
        Assert.Equal(new Name("s_Sniping"), rifle.GetStateName());
        Assert.Equal(SRateOfFireMode.ROF_Single, rifle.ROFMode);
        Assert.Equal(30f, pc.fBinocularVisionFOV);

        pc.eventFire(1f);
        Assert.Equal(25, rifle.ClipAmmo);
        Assert.True(rifle.bDrawWeapon);
        scene.Run(0.25f);
        Assert.True(rifle.bDrawWeapon);
        scene.Run(0.05f);
        Assert.False(rifle.bDrawWeapon);
        pc.eventFire(1f);
        Assert.Equal(24, rifle.ClipAmmo);
        Assert.True(pc.bBinocularKick);
        pc.eventFire(1f);
        Assert.Equal(24, rifle.ClipAmmo);
    }

    [Fact]
    public void ZoomGoesThirtyTwentyTenAndBack()
    {
        (_, SPlayerController pc, _, _) = Merc();
        pc.SwitchBinocularVision();
        pc.eventZoomIn();
        Assert.Equal(20f, pc.fBinocularVisionFOV);
        pc.eventZoomIn();
        Assert.Equal(10f, pc.fBinocularVisionFOV);
        pc.eventZoomIn();
        Assert.Equal(10f, pc.fBinocularVisionFOV);
        pc.eventWheelDown();
        Assert.Equal(20f, pc.fBinocularVisionFOV);
        Assert.Equal(0.66f, pc.AudioZoomFactor);
    }

    [Fact]
    public void LeavingTheScopeRestoresTheModeAndTheWalkingState()
    {
        (_, SPlayerController pc, _, SWeaponDefense rifle) = Merc();
        pc.SwitchBinocularVision();
        pc.SwitchBinocularVision();
        Assert.False(pc.bBinocularVision);
        pc.eventPlayerMove(Sim.DeltaTime);
        Assert.Equal(WalkingSt2H, pc.GetStateName());
        Assert.Equal(SRateOfFireMode.ROF_Auto, rifle.ROFMode);
        Assert.Equal(new Name("s_Selected"), rifle.GetStateName());
        Assert.Equal(pc.DefaultFOV, pc.FovAngle);
    }

    [Fact]
    public void ScopedReloadKeepsSlowMovementAndStillBlocksFire()
    {
        var (scene, pc, merc, rifle) = Merc();
        pc.SwitchBinocularVision();
        pc.aForward = 6000f;
        pc.eventPlayerMove(Sim.DeltaTime);
        float scopedInput = pc.aForward;
        rifle.ClipAmmo = 0;
        pc.eventFire(1f);
        pc.aForward = 6000f;
        pc.bPressedJump = true;
        pc.eventPlayerMove(Sim.DeltaTime);
        Assert.Equal(scopedInput, pc.aForward);
        Assert.InRange(pc.aForward, 1f, 5999f);
        Assert.False(pc.bPressedJump);
        Assert.Equal(new Name("s_Binoculars_Def_Reloading"), pc.GetStateName());
        pc.eventFire(1f);
        Assert.Equal(0, rifle.ClipAmmo);
        scene.Run(3f);
        Assert.Equal(rifle.ClipMaxAmmo, rifle.ClipAmmo);
    }

    [Fact]
    public void AnEmptyClipInTheScopeReloadsInsideIt()
    {
        (_, SPlayerController pc, _, SWeaponDefense rifle) = Merc();
        pc.SwitchBinocularVision();
        rifle.ClipAmmo = 0;
        pc.eventFire(1f);
        Assert.Equal(new Name("s_Binoculars_Def_Reloading"), pc.GetStateName());
        Assert.Equal(new Name("s_Reloading"), rifle.GetStateName());
    }
}
