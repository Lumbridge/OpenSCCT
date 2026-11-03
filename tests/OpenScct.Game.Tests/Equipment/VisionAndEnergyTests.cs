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
    public void EnergyRechargesContinuouslyAndTheCloakDrainsIt()
    {
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy; // the controlling machine's own player
        var spy = (SPawnAttaque)pawn;
        spy.EnergyLevel = 50f;
        spy.PerturbationStartTime = -1f;
        spy.PerturbationDuration = 0f;
        SpyEnergy.ComputeEnergyLevel(spy, 0.6f);
        Assert.Equal(54.2f, spy.EnergyLevel, 3);
        SpyEnergy.ComputeEnergyLevel(spy, 0.6f);
        Assert.Equal(58.4f, spy.EnergyLevel, 3);
        spy.GlassEffect = 255;
        spy.DesiredGlassEffect = 255;
        float before = spy.EnergyLevel;
        SpyEnergy.ComputeEnergyLevel(spy, 0.5f);
        Assert.Equal(before - 0.5f * spy.GlassEffectEnergyUsePerTick, spy.EnergyLevel, 3);
    }

    [Fact]
    public void RechargeStopsExactlyAtTheMaximum()
    {
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy; // the controlling machine's own player
        var spy = (SPawnAttaque)pawn;
        spy.EnergyLevel = spy.EnergyLevelMax - 5f;
        spy.PerturbationStartTime = -1f;
        spy.PerturbationDuration = 0f;
        SpyEnergy.ComputeEnergyLevel(spy, 1f);
        Assert.Equal(spy.EnergyLevelMax, spy.EnergyLevel);
        SpyEnergy.ComputeEnergyLevel(spy, 1f);
        Assert.Equal(spy.EnergyLevelMax, spy.EnergyLevel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(255)]
    public void DrainContinuesThroughoutActivationAndActiveFlicker(byte amount)
    {
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy; // the controlling machine's own player
        var spy = (SPawnAttaque)pawn;
        spy.EnergyLevel = 50f;
        spy.DesiredGlassEffect = 255;
        spy.GlassEffect = amount;
        spy.BGlassEffect = 1f;
        spy.PerturbationStartTime = spy.Level.TimeSeconds;
        spy.PerturbationDuration = 1f;
        SpyEnergy.ComputeEnergyLevel(spy, 0.2f);
        Assert.Equal(48.4f, spy.EnergyLevel, 3);
    }

    [Fact]
    public void TurningOffHoldsEnergyUntilTheFadeAndFlickerFinish()
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(true, default);
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        var spy = (SPawnAttaque)pawn;
        spy.Level.TimeSeconds = 10f;
        spy.EnergyLevel = 50f;
        SpyCloak.SetActive(spy, true);
        spy.Level.TimeSeconds = 12f;
        PawnVision.TickGlass(spy);
        SpyCloak.SetActive(spy, false);
        Assert.True(SpyCloak.IsEngaged(spy));
        SpyEnergy.ComputeEnergyLevel(spy, 0.25f);
        Assert.Equal(50f, spy.EnergyLevel);
        spy.GlassEffect = 0;
        SpyEnergy.ComputeEnergyLevel(spy, 0.25f);
        Assert.Equal(50f, spy.EnergyLevel);
        spy.Level.TimeSeconds = 13f;
        SpyEnergy.ComputeEnergyLevel(spy, 0.5f);
        Assert.Equal(53.5f, spy.EnergyLevel);
    }

    [Fact]
    public void ReversingTheFadeRepeatedlyNeverCreatesFreeEnergyWindows()
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(true, default);
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        var spy = (SPawnAttaque)pawn;
        spy.Level.TimeSeconds = 10f;
        for (int i = 0; i < 20; i++)
        {
            SpyCloak.Toggle(spy);
            spy.Level.TimeSeconds += 0.1f;
            PawnVision.TickGlass(spy);
            SpyEnergy.ComputeEnergyLevel(spy, 0.1f);
        }
        Assert.Equal(92f, spy.EnergyLevel, 3); // Ten 0.1 s on intervals; off fades neither spend nor refill.
    }

    [Fact]
    public void ExhaustionClampsAtZeroWithoutRestartingTheForcedFade()
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(true, default);
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        var spy = (SPawnAttaque)pawn;
        spy.Level.TimeSeconds = 10f;
        spy.EnergyLevel = 0.25f;
        SpyCloak.SetActive(spy, true);
        PawnVision.TickGlass(spy);
        SpyEnergy.ComputeEnergyLevel(spy, 0.1f);
        Assert.Equal(0f, spy.EnergyLevel);
        Assert.Equal(0, spy.DesiredGlassEffect);
        float slope = spy.SlopeGlassEffect;
        float rampBase = spy.BGlassEffect;
        float start = spy.PerturbationStartTime;
        int events = scene.World.Events.Current.Length;
        spy.Level.TimeSeconds += 0.1f;
        SpyEnergy.ComputeEnergyLevel(spy, 0.1f);
        Assert.Equal(0f, spy.EnergyLevel);
        Assert.Equal(slope, spy.SlopeGlassEffect);
        Assert.Equal(rampBase, spy.BGlassEffect);
        Assert.Equal(start, spy.PerturbationStartTime);
        Assert.Equal(events, scene.World.Events.Current.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void RechargeAndDrainHaveTheSameRatesAcrossTickSizes(int hz)
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(true, default);
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        var spy = (SPawnAttaque)pawn;
        spy.EnergyLevel = 40f;
        for (int i = 0; i < hz * 2; i++) SpyEnergy.ComputeEnergyLevel(spy, 1f / hz);
        Assert.InRange(spy.EnergyLevel, 53.995f, 54.005f);
        spy.DesiredGlassEffect = 255;
        for (int i = 0; i < hz * 2; i++) SpyEnergy.ComputeEnergyLevel(spy, 1f / hz);
        Assert.InRange(spy.EnergyLevel, 37.995f, 38.005f);
    }

    [Fact]
    public void ShockerCanSpendTheContinuouslyRechargingSharedBattery()
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(true, default);
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        var spy = (SPawnAttaque)pawn;
        spy.WeaponLoaded = 2;
        spy.EnergyLevel = 14f;
        SpyEnergy.ComputeEnergyLevel(spy, 0.125f);
        Assert.False(SpyEnergy.AuthorizeEnergy(spy, 15));
        SpyEnergy.ComputeEnergyLevel(spy, 0.125f);
        Assert.True(SpyEnergy.AuthorizeEnergy(spy, 15));
        Assert.Equal(0.75f, spy.EnergyLevel);
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
