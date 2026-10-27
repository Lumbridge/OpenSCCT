using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Camera;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>spec/devices/shaker.md and spec/devices/ge-trigger.md.</summary>
public sealed class ShakerAndGETriggerTests
{
    [Fact]
    public void AShakerShakesEveryPlayerForItsListedSendersOnly()
    {
        DeviceScene s = DeviceScene.Create();
        Sender crane = s.Add(new Sender());
        Sender other = s.Add(new Sender());
        SShaker shaker = s.Add(new SShaker(), tag: "blast01");
        shaker.ShakeEvents.Add(new FShakeEvent { EventSender = crane, strenght = 1000, speed = 10000, FadeOut = 1000, bTilt = true, bShake = true });
        shaker.ShakeEvents.Add(new FShakeEvent { EventSender = crane, strenght = 800, speed = 10000, FadeOut = 2000, bTilt = true });
        s.Begin();
        TestSpy spy = s.Player<TestSpy>(default);
        TestMerc merc = s.Player<TestMerc>(default);

        s.Level.TriggerEvent("blast01", other, null);
        s.Level.UntriggerEvent("blast01", crane, null);
        Assert.Empty(((TestController)spy.Controller!).Shakes);

        s.Level.TriggerEvent("blast01", crane, null);
        foreach (Pawn p in new Pawn[] { spy, merc })
        {
            var pc = (TestController)p.Controller!;
            Assert.Equal([(1000, 10000, 1000, true, true), (800, 10000, 2000, true, false)], pc.Shakes);

            // Each call overwrites the camera's values: the last tilt and the last shake stay.
            SPlayerCam cam = pc.m_camera!;
            Assert.Equal(800, cam.m_tiltPitch);
            Assert.Equal(800, cam.m_tiltTargetPitch);
            Assert.Equal(10000, cam.m_tiltSpeed);
            Assert.Equal(2000, cam.m_tiltFadeOut);
            Assert.Equal(1000, cam.m_shakeRoll);
            Assert.Equal(1000, cam.m_shakeTargetRoll);
            Assert.Equal(10000, cam.m_shakeSpeed);
            Assert.Equal(1000, cam.m_shakeFadeOut);

            // Next view: the values from before the step show (the shake replaces the roll, the tilt adds to the pitch),
            // and both reached targets shrink by their fade-out and flip sign (game 0x10a96280).
            float dt = s.Level.Info.m_dT;
            FRotator view = cam.UpdateView(new FRotator(0, 0, 0));
            Assert.Equal(1000, view.Roll);
            Assert.Equal(800, view.Pitch);
            Assert.Equal(-CameraMath.ApproachInt(1000, 0, 1000 * dt), cam.m_shakeTargetRoll);
            Assert.Equal(-CameraMath.ApproachInt(800, 0, 2000 * dt), cam.m_tiltTargetPitch);        }
    }

    [Fact]
    public void AZoneShakeReachesOnlyPawnsInThatZone()
    {
        DeviceScene s = DeviceScene.Create();
        Sender sender = s.Add(new Sender());
        var zone = s.Add(new ZoneInfo());
        SShaker shaker = s.Add(new SShaker(), tag: "boom");
        shaker.ShakeEvents.Add(new FShakeEvent { EventSender = sender, strenght = 300, speed = 10000, FadeOut = 1000, bShake = true, Zone = zone });
        s.Begin();
        TestSpy inside = s.Player<TestSpy>(default);
        TestMerc outside = s.Player<TestMerc>(default);
        inside.Region = new FPointRegion { Zone = zone };
        s.Level.TriggerEvent("boom", sender, null);
        Assert.Single(((TestController)inside.Controller!).Shakes);
        Assert.Empty(((TestController)outside.Controller!).Shakes);
    }

    [Fact]
    public void CameraEffectsSetAndReset()
    {
        var cam = new SPlayerCam();
        cam.Hit(500, 300);
        cam.Tilt(1, 2, 3);
        cam.Shake(4, 5, 6);
        Assert.Equal(500, cam.m_hitRoll);
        Assert.Equal(300, cam.m_hitFadeOut);
        cam.ResetEffects();
        Assert.Equal(0, cam.m_hitRoll);
        Assert.Equal(0, cam.m_tiltPitch);
        Assert.Equal(0, cam.m_tiltTargetPitch);
        Assert.Equal(0, cam.m_shakeRoll);
        Assert.Equal(0, cam.m_shakeTargetRoll);
    }

    private static (DeviceScene S, SGETrigger T) GE(byte mask, byte geMask, bool toggle)
    {
        DeviceScene s = DeviceScene.Create();
        SGETrigger t = s.Add(new SGETrigger { GEMask = geMask, bToggle = toggle }, tag: "ge");
        s.Begin();
        s.Level.Info.GEActivationMask = mask;
        return (s, t);
    }

    [Fact]
    public void ATriggerSetsTheMaskBits()
    {
        (DeviceScene s, _) = GE(1, 4, false);
        s.Level.TriggerEvent("ge", null, null);
        Assert.Equal(5, s.Level.Info.GEActivationMask);
        s.Level.TriggerEvent("ge", null, null);
        Assert.Equal(5, s.Level.Info.GEActivationMask);
    }

    [Fact]
    public void AToggleClearsBitsThatAreAllSetAndSetsThemOtherwise()
    {
        (DeviceScene s, _) = GE(4, 4, true);
        s.Level.TriggerEvent("ge", null, null);
        Assert.Equal(0, s.Level.Info.GEActivationMask);
        s.Level.TriggerEvent("ge", null, null);
        Assert.Equal(4, s.Level.Info.GEActivationMask);

        // A multi-bit mask partly set switches all of its bits on.
        (DeviceScene s2, _) = GE(0b0010, 0b0110, true);
        s2.Level.TriggerEvent("ge", null, null);
        Assert.Equal(0b0110, s2.Level.Info.GEActivationMask);
    }

    [Fact]
    public void AnUnTriggerClearsTheBits()
    {
        (DeviceScene s, _) = GE(0b1111, 0b0101, false);
        s.Level.UntriggerEvent("ge", null, null);
        Assert.Equal(0b1010, s.Level.Info.GEActivationMask);
    }
}
