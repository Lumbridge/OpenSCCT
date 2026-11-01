using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>spec/devices/cam-network.md: the camera actor, the ring, the turn limits and the shared use.</summary>
public sealed class CamNetworkTests
{
    [Fact]
    public void DisabledLensStaysOffWhenMercChangesVisionAndRestoresOnRecovery()
    {
        DeviceScene s = DeviceScene.Create();
        SCamNetwork cam = s.Add(new SCamNetwork());
        s.Begin();
        cam.SwitchToVision(1);
        cam.eventTakeDamage(0, new SBasePawn { SPawnType = PawnType.SPAWNOFFENSE }, default, default, SDamageType.SDTStickyShocker);
        Assert.Equal(cam.mOff, cam.Skins[1]);
        cam.SwitchToVision(2);
        Assert.Equal(cam.mOff, cam.Skins[1]);
        s.StepSeconds(30.1f);
        Assert.Equal(cam.mEMF, cam.Skins[1]);
    }

    [Fact]
    public void SpyShotsDisableTheFeedForThirtySecondsAndRefreshTheCountdown()
    {
        DeviceScene s = DeviceScene.Create();
        SCamNetwork cam = s.Add(new SCamNetwork());
        SBasePawn spy = s.Add(new SBasePawn { SPawnType = PawnType.SPAWNOFFENSE });
        s.Begin();
        cam.eventTakeDamage(0, spy, cam.Location, default, SDamageType.SDTStickyShocker);
        Assert.Equal(30, cam.DisabledSeconds);
        s.StepSeconds(10);
        Assert.InRange(cam.DisabledSeconds, 20, 21);
        cam.eventTakeDamage(0, spy, cam.Location, default, SDamageType.SDTStickyShocker);
        Assert.Equal(30, cam.DisabledSeconds);
        s.StepSeconds(29);
        Assert.True(cam.IsShotDisabled);
        s.StepSeconds(1.1f);
        Assert.False(cam.IsShotDisabled);
        Assert.False(cam.bDeleteMe);
    }

    [Fact]
    public void MercShotsAndOtherDamageCannotDisableTheCamera()
    {
        DeviceScene s = DeviceScene.Create();
        SCamNetwork cam = s.Add(new SCamNetwork());
        SBasePawn merc = s.Add(new SPAWNDEFENSE());
        SBasePawn spy = s.Add(new SBasePawn { SPawnType = PawnType.SPAWNOFFENSE });
        s.Begin();
        cam.eventTakeDamage(10, merc, default, default, SDamageType.SDTStickyShocker);
        cam.eventTakeDamage(100, spy, default, default, SDamageType.SDamage_Explosion);
        cam.eventTakeDamage(10, null, default, default, SDamageType.SDTStickyShocker);
        Assert.False(cam.IsShotDisabled);
        cam.Role = ENetRole.ROLE_SimulatedProxy;
        cam.eventTakeDamage(0, spy, default, default, SDamageType.SDTStickyShocker);
        Assert.False(cam.IsShotDisabled);
    }

    [Theory]
    [InlineData(1000, 2000, -13000, 13000, 2000)]
    [InlineData(1000, 20000, -13000, 13000, 14000)]
    [InlineData(1000, 64000, -13000, 13000, 64000)]
    [InlineData(1000, 50000, -13000, 13000, 53536)]
    [InlineData(5000, 1000, -3000, 3000, 2000)]
    [InlineData(65000, 1000, -8000, 8000, 1000)]
    [InlineData(1000, 1000, -8000, 8000, 1000)]
    [InlineData(-7828, -7828, -8000, 8000, -7828)]
    public void ClampRotationKeepsAnAngleWithinItsLimitsTheShortWayRound(int centre, int target, int min, int max, int expected)
    {
        Assert.Equal(expected & 65535, SPlayerController.ClampRotation(centre, target, min, max));
    }

    [Fact]
    public void TheVisionCycleSetsTheLensSkin()
    {
        var cam = new SCamNetwork();
        cam.SwitchToVision(3);
        Assert.Equal(1, cam.iCurrentVisionMode);
        Assert.Equal(cam.mMotion, cam.Skins[1]);
        cam.SwitchToVision(3);
        Assert.Equal(2, cam.iReplicatedVisionMode);
        Assert.Equal(cam.mEMF, cam.Skins[1]);
        cam.SwitchToVision(3);
        Assert.Equal(-2, cam.iChangeToThisVisionMode);
        Assert.Equal(cam.mOn, cam.Skins[1]);

        // Asking for the mode it is in resets it: on for any mode but -1, which is off.
        cam.SwitchToVision(-2);
        Assert.Equal(0, cam.iCurrentVisionMode);
        Assert.Equal(cam.mOn, cam.Skins[1]);
        cam.SwitchToVision(-1);
        Assert.Equal(cam.mOff, cam.Skins[1]);
        Assert.Equal(-1, cam.iCurrentVisionMode);
        cam.SwitchToVision(-1);
        Assert.Equal(0, cam.iCurrentVisionMode);
        Assert.Equal(cam.mOff, cam.Skins[1]);
    }

    [Fact]
    public void ANetworkCameraCentresItsLimitsOnItsPlacementAndPlaysItsActivationSound()
    {
        DeviceScene s = DeviceScene.Create();
        SCamNetwork cam = s.Add(new SCamNetwork { ActivationSound = new AssetRef("CamNet_In") }, default, new FRotator(-2124, -7828, 0));
        s.Begin();
        Assert.Equal(-2124f, cam.PitchConstraint.X);
        Assert.Equal(-7828f, cam.YawConstraint.X);
        Assert.Equal(-2124, cam.iPitch);
        Assert.Equal(-7828, cam.iReplicatedYaw);
        Assert.False(cam.AcceptStickableGPO());
        SimEvent e = Assert.Single(s.World.Events.Current.ToArray(), x => x.Kind == SimEventKind.PlaySound);
        Assert.Equal(100f, e.Radius);
        Assert.Equal(new Name("CamNet_In"), e.What);
    }

    [Fact]
    public void TheEntryCameraIsTheFirstFlaggedOrElseTheLast()
    {
        DeviceScene s = DeviceScene.Create();
        SCamNetwork a = s.Add(new SCamNetwork());
        SCamNetwork b = s.Add(new SCamNetwork { bFirstCam = true });
        SCamNetwork c = s.Add(new SCamNetwork());
        s.Begin();
        Assert.Same(b, SCamNetwork.FindFirstCamera(s.Level));
        b.bFirstCam = false;
        Assert.Same(c, SCamNetwork.FindFirstCamera(s.Level));
        Assert.NotSame(a, SCamNetwork.FindFirstCamera(s.Level));
    }

    [Fact]
    public void LeavingACameraHandsItToAnotherWatchingMercOrFreesIt()
    {
        DeviceScene s = DeviceScene.Create();
        SCamNetwork cam = s.Add(new SCamNetwork());
        s.Begin();
        TestMerc first = s.Player<TestMerc>(default);
        TestMerc second = s.Player<TestMerc>(default);
        TestSpy spy = s.Player<TestSpy>(default);
        var firstPc = (SPlayerController)first.Controller!;
        firstPc.DefaultFOV = 85f;
        first.UsedObject = cam;
        second.UsedObject = cam;
        spy.UsedObject = cam;
        cam.aUsedByPawn = first;
        cam.fVisionFOV = 40f;

        firstPc.FreeCurrentCamera();
        Assert.Same(second, cam.aUsedByPawn);
        Assert.Equal(40f, cam.fVisionFOV);

        second.UsedObject = null;
        var secondPc = (SPlayerController)second.Controller!;
        secondPc.DefaultFOV = 90f;
        second.UsedObject = cam;
        spy.UsedObject = cam;
        first.UsedObject = null;
        secondPc.FreeCurrentCamera();
        Assert.Null(cam.aUsedByPawn);
        Assert.Equal(90f, cam.fVisionFOV);
        Assert.Equal(90f, cam.fOldVisionFOV);
        Assert.Equal(-1, cam.iCurrentVisionMode);
    }

    [Fact]
    public void ShotsCountForTheMuzzleFlash()
    {
        var cam = new SCamNetwork();
        cam.AddMuzzleCount();
        cam.AddMuzzleCount();
        Assert.Equal(2, cam.MuzzleCount);
    }
}
