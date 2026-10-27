using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>
/// The reload and rate-of-fire buttons of weapon walking (merc-rifle.md rules 16 and 17): the held input bytes the R and
/// F keys set (<c>Button bReload</c>, <c>Button bROF</c>), read by s_PlayerWalkingWeapon.PlayerMove every frame.
/// </summary>
public sealed class WeaponButtonsTests
{
    private static readonly Name WalkingSt2H = new("s_PlayerWalkingSt2H");

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

    /// <summary>Ticks with the armed walk's PlayerMove each frame, as a local player's PlayerTick runs it.</summary>
    private static void Walk(CombatScene scene, SPlayerController pc, float seconds)
    {
        for (int i = 0; i < (int)(seconds * Sim.Hz + 0.5f); i++)
        {
            scene.Run(1f / Sim.Hz);
            pc.eventPlayerMove(Sim.DeltaTime);
        }
    }

    [Fact]
    public void HoldingTheRateOfFireButtonSwitchesOnceUntilItIsReleased()
    {
        (CombatScene scene, SPlayerController pc, _, SWeaponDefense rifle) = Merc();
        Assert.Equal(SRateOfFireMode.ROF_Auto, rifle.ROFMode);
        pc.bROF = 1;
        Walk(scene, pc, 0.5f);
        Assert.Equal(SRateOfFireMode.ROF_Burst, rifle.ROFMode);
        Assert.True(pc.bChangeROF);

        pc.bROF = 0;
        Walk(scene, pc, 0.05f);
        Assert.False(pc.bChangeROF);
        pc.bROF = 1;
        Walk(scene, pc, 0.05f);
        Assert.Equal(SRateOfFireMode.ROF_Auto, rifle.ROFMode);
    }

    [Fact]
    public void TheReloadButtonReloadsAPartClip()
    {
        (CombatScene scene, SPlayerController pc, _, SWeaponDefense rifle) = Merc();
        pc.eventFire(1f);
        Walk(scene, pc, 0.3f);
        Assert.True(rifle.ClipAmmo < rifle.ClipMaxAmmo);
        pc.bFire = 0;
        pc.bReload = 1;
        Walk(scene, pc, 0.05f);
        pc.bReload = 0;
        Assert.Equal(new Name("s_Reloading"), rifle.GetStateName());
        Walk(scene, pc, 2.5f);
        Assert.Equal(rifle.ClipMaxAmmo, rifle.ClipAmmo);
    }

    [Fact]
    public void TheButtonsDoNothingInInventoryMode()
    {
        (CombatScene scene, SPlayerController pc, _, SWeaponDefense rifle) = Merc();
        pc.bIsInInventoryMode = true;
        pc.bROF = 1;
        Walk(scene, pc, 0.2f);
        Assert.Equal(SRateOfFireMode.ROF_Auto, rifle.ROFMode);
    }
}
