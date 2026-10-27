using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>The goggles' mode switching and the spy energy bar (vision-goggles.md, spy-energy.md).</summary>
public sealed class VisionAndEnergyTests
{
    private static readonly Name GoggleUp = new("GoggleUp");
    private static readonly Name GoggleDown = new("GoggleDown");

    private static (CombatScene Scene, SPlayerController PC, SGoggle Goggle) Player(bool spy)
    {
        var scene = new CombatScene();
        (SPlayerController pc, _) = scene.Player(spy, new FVector(0f, 0f, 100f));
        SGoggle goggle = scene.Level.SpawnActor(new SGoggle(), pc.Location, default)!;
        goggle.SetOwner(pc);
        goggle.SPC = pc;
        pc.Goggle = goggle;
        return (scene, pc, goggle);
    }

    [Fact]
    public void TheGogglesStartRaisedInNormalView()
    {
        (_, _, SGoggle goggle) = Player(false);
        Assert.Equal(SGoggle.REN_DynLight, goggle.CurrentMode);
        Assert.Equal(GoggleUp, goggle.GetStateName());
    }

    [Fact]
    public void TheMercFirstModeIsMotionTrackingAndTakesEffectWhenTheGogglesComeDown()
    {
        (_, SPlayerController pc, SGoggle goggle) = Player(false);
        goggle.SwitchMode(1);
        Assert.Equal(SGoggle.REN_MotionTrackingVision, goggle.CurrentMode);
        Assert.Equal(GoggleUp, goggle.GetStateName());
        goggle.eventDown();
        Assert.Equal(GoggleDown, goggle.GetStateName());
        Assert.Equal(SGoggle.REN_MotionTrackingVision, pc.RendMap);
    }

    [Fact]
    public void TheSameKeyAgainReturnsToNormalView()
    {
        (_, SPlayerController pc, SGoggle goggle) = Player(false);
        goggle.SwitchMode(1);
        goggle.eventDown();
        goggle.SwitchMode(1);
        Assert.Equal(SGoggle.REN_DynLight, goggle.CurrentMode);
        goggle.eventUp();
        Assert.Equal(GoggleUp, goggle.GetStateName());
        Assert.Equal(SGoggle.REN_DynLight, pc.RendMap);
    }

    [Fact]
    public void SwitchingBetweenTwoModesWhileDownIsImmediate()
    {
        (_, SPlayerController pc, SGoggle goggle) = Player(false);
        goggle.SwitchMode(1);
        goggle.eventDown();
        goggle.SwitchMode(2);
        Assert.Equal(SGoggle.REN_EMFVision, goggle.CurrentMode);
        Assert.Equal(SGoggle.REN_EMFVision, pc.RendMap);
        Assert.Equal(GoggleDown, goggle.GetStateName());
    }

    [Fact]
    public void TheCycleKeyGoesNormalThenFirstThenSecondThenNormal()
    {
        (_, _, SGoggle goggle) = Player(true);
        goggle.SwitchMode(3);
        Assert.Equal(SGoggle.REN_NightVision, goggle.CurrentMode);
        goggle.eventDown();
        goggle.SwitchMode(3);
        Assert.Equal(SGoggle.REN_ThermalVision, goggle.CurrentMode);
        goggle.SwitchMode(3);
        Assert.Equal(SGoggle.REN_DynLight, goggle.CurrentMode);
    }

    [Fact]
    public void ThermalCanBeWithheld()
    {
        (_, _, SGoggle goggle) = Player(true);
        goggle.bNoThermalAvailable = true;
        goggle.SwitchMode(2);
        Assert.Equal(SGoggle.REN_DynLight, goggle.CurrentMode);
    }

    [Fact]
    public void ResettingWithoutTheClipIsImmediate()
    {
        (_, SPlayerController pc, SGoggle goggle) = Player(false);
        goggle.SwitchMode(2);
        goggle.eventDown();
        goggle.ResetVisionMode(true);
        Assert.Equal(SGoggle.REN_DynLight, goggle.CurrentMode);
        Assert.Equal(GoggleUp, goggle.GetStateName());
        Assert.Equal(SGoggle.REN_DynLight, pc.RendMap);
    }

    [Fact]
    public void AShockerShotCosts15Energy()
    {
        var scene = new CombatScene();
        (_, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        var spy = (SPawnAttaque)pawn;
        spy.EnergyLevel = 100f;
        Assert.True(SpyEnergy.AuthorizeEnergy(spy, 15));
        Assert.Equal(85f, spy.EnergyLevel);
        spy.EnergyLevel = 10f;
        Assert.False(SpyEnergy.AuthorizeEnergy(spy, 15));
        Assert.Equal(10f, spy.EnergyLevel);
    }

    [Fact]
    public void EnergyRefillsByWholeSecondsAndTheCloakDrainsIt()
    {
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy; // the controlling machine's own player
        var spy = (SPawnAttaque)pawn;
        spy.EnergyLevel = 50f;
        spy.EnergyRefill = 0f;
        spy.PerturbationStartTime = -1f;
        spy.PerturbationDuration = 0f;
        SpyEnergy.ComputeEnergyLevel(spy, 0.6f);
        Assert.Equal(50f, spy.EnergyLevel, 3);
        SpyEnergy.ComputeEnergyLevel(spy, 0.6f);
        Assert.Equal(50f + spy.EnergyRefillAmount, spy.EnergyLevel, 3);
        spy.GlassEffect = 255;
        float before = spy.EnergyLevel;
        SpyEnergy.ComputeEnergyLevel(spy, 0.5f);
        Assert.Equal(before - 0.5f * spy.GlassEffectEnergyUsePerTick, spy.EnergyLevel, 3);
    }

    [Fact]
    public void ARefillStepIsNotClampedAtTheMaximum()
    {
        // game 0x10af5fa0: one EnergyRefillAmount step while below the maximum, with no clamp after it.
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy; // the controlling machine's own player
        var spy = (SPawnAttaque)pawn;
        spy.EnergyLevel = spy.EnergyLevelMax - 5f;
        spy.EnergyRefill = 0.9f;
        spy.PerturbationStartTime = -1f;
        spy.PerturbationDuration = 0f;
        SpyEnergy.ComputeEnergyLevel(spy, 0.2f);
        Assert.Equal(spy.EnergyLevelMax - 5f + spy.EnergyRefillAmount, spy.EnergyLevel, 3);
    }

    [Fact]
    public void NoDrainOrRefillAtTheEndOfTheFlickerWindow()
    {
        // game 0x10af5fa0: runs only when now - PerturbationStartTime is strictly above PerturbationDuration.
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy; // the controlling machine's own player
        var spy = (SPawnAttaque)pawn;
        spy.EnergyLevel = 50f;
        spy.EnergyRefill = 0.9f;
        spy.PerturbationStartTime = spy.Level.TimeSeconds;
        spy.PerturbationDuration = 0f;
        SpyEnergy.ComputeEnergyLevel(spy, 0.2f);
        Assert.Equal(50f, spy.EnergyLevel, 3);
    }

    [Fact]
    public void NoEnergyTickForAControllerOwnedByARemoteClient()
    {
        // game 0x10af5fa0: a controller whose RemoteRole is AutonomousProxy (the server copy of a client player)
        // does not compute the bar.
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
        var spy = (SPawnAttaque)pawn;
        spy.EnergyLevel = 50f;
        spy.EnergyRefill = 0.9f;
        spy.PerturbationStartTime = -1f;
        spy.PerturbationDuration = 0f;
        SpyEnergy.ComputeEnergyLevel(spy, 0.2f);
        Assert.Equal(50f, spy.EnergyLevel, 3);
    }
}
