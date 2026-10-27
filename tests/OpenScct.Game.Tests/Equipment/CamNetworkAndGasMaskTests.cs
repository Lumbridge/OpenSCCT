using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>
/// The merc's camera network gadget (spec/equipment/camera-network-gadget.md) and gas mask (spec/equipment/gas-mask.md).
/// </summary>
public sealed class CamNetworkAndGasMaskTests
{
    // ---- the camera network ---------------------------------------------------------------------------------

    private static (CombatScene Scene, SPlayerController PC, SBasePawn Merc, SGadCamNetwork Gad, SCamNetwork A, SCamNetwork B) Network()
    {
        CombatScene scene = GadgetKit.Scene();
        // Network cameras are map actors (bNoDelete): placed during the map load.
        scene.Level.BeginLoad();
        SCamNetwork a = scene.Level.SpawnActor(new SCamNetwork(), new FVector(500f, 0f, 300f), default)!;
        SCamNetwork b = scene.Level.SpawnActor(new SCamNetwork(), new FVector(-500f, 0f, 300f), default)!;
        scene.Level.EndLoad();
        (SPlayerController pc, SBasePawn merc) = GadgetKit.Local(scene, false, new FVector(0f, 0f, 100f));
        merc.SetPhysics(EPhysics.PHYS_Walking);
        a.bFirstCam = true;
        a.NextCam = b;
        a.PrevCam = b;
        b.NextCam = a;
        b.PrevCam = a;
        SGadCamNetwork gad = GadgetKit.Gadget(scene, merc, new SGadCamNetwork());
        scene.Run(1f);
        return (scene, pc, merc, gad, a, b);
    }

    [Fact]
    public void ACameraRefillsFiveEnergyPerWholeSecondUpToItsMaximum()
    {
        var cam = new SCamNetwork { EnergyLevel = 50f };
        cam.ComputeEnergyLevel(0.6f);
        Assert.Equal(50f, cam.EnergyLevel);
        cam.ComputeEnergyLevel(0.6f);
        Assert.Equal(55f, cam.EnergyLevel);
        cam.EnergyLevel = 100f;
        cam.ComputeEnergyLevel(1f);
        Assert.Equal(100f, cam.EnergyLevel);
    }

    [Fact]
    public void AShotNeedsFiftyEnergy()
    {
        var cam = new SCamNetwork { EnergyLevel = 100f };
        Assert.True(cam.AuthorizeEnergy(50));
        Assert.Equal(50f, cam.EnergyLevel);
        cam.EnergyLevel = 40f;
        Assert.False(cam.AuthorizeEnergy(50));
        Assert.Equal(40f, cam.EnergyLevel);
    }

    [Fact]
    public void TheGadgetTakesTheMercBehindTheFirstCamera()
    {
        (_, SPlayerController pc, SBasePawn merc, SGadCamNetwork gad, SCamNetwork a, _) = Network();
        gad.Use(1f);
        Assert.Same(a, merc.UsedObject);
        Assert.Same(merc, a.aUsedByPawn);
        Assert.Same(a, gad.FirstNetworkCam);
        Assert.Equal(GadgetStates.s_UseObject_CamNetwork, pc.GetStateName());
        Assert.True(GadgetStates.InStickyCamViewState(pc));
    }

    [Fact]
    public void TheNextAndPreviousKeysWalkTheRingAndFreeTheCameraLeft()
    {
        (_, SPlayerController pc, SBasePawn merc, SGadCamNetwork gad, SCamNetwork a, SCamNetwork b) = Network();
        gad.Use(1f);
        pc.UseCamNetwork(2);
        Assert.Same(b, merc.UsedObject);
        Assert.Null(a.aUsedByPawn);
        Assert.Same(merc, b.aUsedByPawn);
        pc.UseCamNetwork(1);
        Assert.Same(a, merc.UsedObject);
    }

    [Fact]
    public void LeavingTheNetworkClearsTheUsedCamera()
    {
        (_, SPlayerController pc, SBasePawn merc, SGadCamNetwork gad, SCamNetwork a, _) = Network();
        gad.Use(1f);
        pc.UseCamNetwork(0, true);
        Assert.Null(merc.UsedObject);
        Assert.Null(a.aUsedByPawn);
    }

    [Fact]
    public void AJammedHudKeepsTheMercOutOfTheNetwork()
    {
        (CombatScene scene, SPlayerController pc, SBasePawn merc, SGadCamNetwork gad, _, _) = Network();
        pc.myHUD = scene.Level.SpawnActor(new SHud(), default, default);
        pc.SetHudPerturbationLength(2f);
        gad.Use(1f);
        Assert.Null(merc.UsedObject);
    }

    [Fact]
    public void TheGadgetIsNotSpent()
    {
        var gad = new SGadCamNetwork();
        Assert.Equal(-10, gad.Charge);
        Assert.Equal(1f, gad.RefireRate);
    }

    // ---- the gas mask -------------------------------------------------------------------------------------------

    private static (CombatScene Scene, SPlayerController PC, SBasePawn Merc, SGadGasMask Mask) Masked()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, SBasePawn merc) = GadgetKit.Local(scene, false, new FVector(0f, 0f, 100f));
        SGadGasMask mask = GadgetKit.Gadget(scene, merc, new SGadGasMask());
        scene.Run(1f);
        return (scene, pc, merc, mask);
    }

    [Fact]
    public void TheMaskGivesThirtySecondsOfAir()
    {
        var mask = new SGadGasMask();
        Assert.Equal(30f, mask.MaskDuration);
        Assert.Equal(-10, mask.Charge);
    }

    [Fact]
    public void AltFireTogglesTheMaskWithTheBreathingSound()
    {
        (_, SPlayerController pc, SBasePawn merc, SGadGasMask mask) = Masked();
        mask.Use(1f);
        Assert.True(merc.bGasMask);
        Assert.Equal(mask.Snd_Respiration, merc.AmbientSound);
        Assert.Equal(mask.Snd_Respiration, pc.AmbientSound);
        mask.Use(1f);
        Assert.False(merc.bGasMask);
        Assert.True(merc.AmbientSound.IsNone);
    }

    [Fact]
    public void TheAirRunsOutWhileWornAndTheMaskComesOff()
    {
        (CombatScene scene, _, SBasePawn merc, SGadGasMask mask) = Masked();
        mask.Use(1f);
        scene.Run(10f);
        Assert.InRange(mask.MaskDuration, 19.8f, 20.2f);
        scene.Run(21f);
        Assert.Equal(0f, mask.MaskDuration);
        Assert.False(merc.bGasMask);
        mask.Use(1f);
        Assert.False(merc.bGasMask);
    }

    [Fact]
    public void UnderSevenAndAHalfSecondsTheMercChokes()
    {
        (CombatScene scene, _, SBasePawn merc, SGadGasMask mask) = Masked();
        mask.MaskDuration = 8f;
        mask.Use(1f);
        Assert.Equal(mask.Snd_Respiration, merc.AmbientSound);
        scene.Run(1f);
        Assert.True(merc.bGasMask);
        Assert.Equal(mask.Snd_Etouffement, merc.AmbientSound);
    }

    [Fact]
    public void TheAirIsNotUsedWhileTheMaskIsOff()
    {
        (CombatScene scene, _, _, SGadGasMask mask) = Masked();
        scene.Run(5f);
        Assert.Equal(30f, mask.MaskDuration);
    }

    [Fact]
    public void SelectingAnotherGadgetTakesTheMaskOff()
    {
        (_, _, SBasePawn merc, SGadGasMask mask) = Masked();
        mask.Use(1f);
        mask.Unload();
        Assert.False(merc.bGasMask);
    }

    [Fact]
    public void ASmokeCloudDoesNotReachAMaskedMerc()
    {
        (CombatScene scene, SPlayerController pc, _, SGadGasMask mask) = Masked();
        (SPlayerController barePc, _) = GadgetKit.Local(scene, false, new FVector(100f, 0f, 100f));
        scene.Run(3f);
        pc.fLastTimeWakeUp = -10f;
        barePc.fLastTimeWakeUp = -10f;
        mask.Use(1f);
        scene.Level.SpawnActor(new SSmokeEmitter(), new FVector(50f, 0f, 100f), default);
        scene.World.Step();
        Assert.False(pc.bInSmoke);
        Assert.True(barePc.bInSmoke);
    }
}
