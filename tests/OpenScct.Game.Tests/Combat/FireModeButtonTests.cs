using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>
/// The spy's fire-mode button (spy-shocker.md rule 1, key E): holstered, a tap draws the Shocker with the laser and a
/// 0.5 s hold draws it without; a press that holsters the Shocker does not draw it again when it is let go.
/// </summary>
public sealed class FireModeButtonTests
{
    private static (CombatScene Scene, SPlayerController PC, SBasePawn Spy) Spy(byte weaponLoaded)
    {
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        spy.WeaponLoaded = weaponLoaded;
        scene.Run(0.1f);
        return (scene, pc, spy);
    }

    /// <summary>Ticks with the key held or not, written before every tick as the input layer does.</summary>
    private static void Ticks(CombatScene scene, SPlayerController pc, int n, byte fireMode)
    {
        for (int i = 0; i < n; i++)
        {
            pc.bFireMode = fireMode;
            scene.Run(1f / Sim.Hz);
            pc.eventPlayerTick(Sim.DeltaTime);
        }
    }

    [Fact]
    public void ATapWhileHolsteredDrawsWithTheLaser()
    {
        (CombatScene scene, SPlayerController pc, _) = Spy(0);
        Ticks(scene, pc, 2, 0);
        Ticks(scene, pc, 6, 1);
        Assert.False(pc.bForceFireMode);
        Ticks(scene, pc, 1, 0);
        Assert.True(pc.bForceFireMode);
        Assert.True(pc.bForceLaserFireMode);
    }

    [Fact]
    public void AHalfSecondHoldWhileHolsteredDrawsWithoutTheLaser()
    {
        (CombatScene scene, SPlayerController pc, _) = Spy(0);
        Ticks(scene, pc, 2, 0);
        Ticks(scene, pc, 40, 1);
        Assert.True(pc.bForceFireMode);
        Assert.False(pc.bForceLaserFireMode);
    }

    [Fact]
    public void LettingGoOfThePressThatHolsteredDoesNotDrawAgain()
    {
        (CombatScene scene, SPlayerController pc, SBasePawn spy) = Spy(2);
        Ticks(scene, pc, 2, 0);

        // The press holsters (the armed state clears bTestBoutonLache); the holster ends while the key is still down.
        Ticks(scene, pc, 2, 1);
        pc.bTestBoutonLache = false;
        spy.WeaponLoaded = 0;
        Ticks(scene, pc, 3, 1);
        Ticks(scene, pc, 10, 0);
        Assert.False(pc.bForceFireMode);
        Assert.True(pc.bTestBoutonLache);
    }

    [Fact]
    public void AHolsterThatEndsAfterTheReleaseDoesNotDrawAgain()
    {
        (CombatScene scene, SPlayerController pc, SBasePawn spy) = Spy(2);
        Ticks(scene, pc, 2, 0);
        Ticks(scene, pc, 2, 1);
        pc.bTestBoutonLache = false;
        Ticks(scene, pc, 2, 0);
        spy.WeaponLoaded = 0;
        Ticks(scene, pc, 10, 0);
        Assert.False(pc.bForceFireMode);
    }
}
