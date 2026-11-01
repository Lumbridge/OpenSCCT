using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

public sealed class GameplayClockTests
{
    [Fact]
    public void InterruptedHackOnlyKeepsTimeActuallySpentHacking()
    {
        var s = DeviceScene.Create();
        var terminal = s.Add(new SComputerObjectiveTrigger { CaptureTime = 30 });
        s.Begin();
        s.StepSeconds(10);
        using (s.Level.CommandTime(300, s.Time - 5, Sim.DeltaTime))
            terminal.eventTriggering(true);
        s.StepSeconds(2.1f);
        terminal.eventTriggering(false);
        Assert.Equal(28, terminal.CaptureTime);
    }

    [Fact]
    public void CameraDamageKeepsTheFullDisableDuration()
    {
        var s = DeviceScene.Create();
        var camera = s.Add(new SCamNetwork());
        s.Begin();
        s.StepSeconds(10);
        using (s.Level.CommandTime(300, s.Time - 5, Sim.DeltaTime))
            camera.eventTakeDamage(0, new SBasePawn { SPawnType = PawnType.SPAWNOFFENSE }, default, default, SDamageType.SDTStickyShocker);
        s.StepSeconds(26);
        Assert.True(camera.IsShotDisabled);
        s.StepSeconds(5);
        Assert.False(camera.IsShotDisabled);
    }

    [Fact]
    public void SpawnedEffectsAndMinesStartAtWorldTime()
    {
        var s = DeviceScene.Create();
        s.Begin();
        s.StepSeconds(10);
        float now = s.Time;
        using (s.Level.CommandTime(300, now - 5, Sim.DeltaTime))
        {
            Assert.Equal(now, s.Spawn(new SFlareEmitter()).fDateEmission);
            Assert.Equal(now, s.Spawn(new SMineProx()).fSpawnedTime);
            Assert.Equal(now - 5, s.Time);
        }
    }

    [Fact]
    public void InteractionUsesTheStationsWorldClock()
    {
        var s = DeviceScene.Create();
        var kit = s.Add(new SInfirmary());
        s.Begin();
        var pawn = s.Player<TestSpy>(default);
        s.StepSeconds(10);
        pawn.Health = 40;
        float now = s.Time;
        using (s.Level.CommandTime(300, now - 5, Sim.DeltaTime))
            kit.InteractionObj!.eventInitInteract((SPlayerController)pawn.Controller!, 0);
        Assert.Equal(100, pawn.Health);
        Assert.Equal(now, kit.LastUseTime);
    }

    [Fact]
    public void DamageHistoryAndSpyMarksUseWorldTime()
    {
        var s = new CombatScene();
        var (pc, spy) = s.Player(true, new FVector(0, 0, 100));
        var (_, merc) = s.Player(false, new FVector(300, 0, 100));
        s.Run(10);
        float now = s.Time;
        using (s.Level.CommandTime(300, now - 5, Sim.DeltaTime))
        {
            pc.PawnTakeDamage(25, merc, spy.Location, default, SDamageType.SDTBase, 6, 70);
            spy.HitBySpyBullet(1);
        }
        Assert.Equal((int)now, pc.LastDamages[0].Time);
        Assert.Equal(now, spy.fTimeSpy);
        using (s.Level.CommandTime(300, now - 5, Sim.DeltaTime))
            spy.DetectedByMarqueurSpy(1, true);
        Assert.Equal(now, spy.fTimeSpy);
    }
}
