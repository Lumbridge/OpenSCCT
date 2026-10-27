using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>The spy's heartbeat sensor: range, cone, no sight or team test (spec/equipment/heartbeat-sensor.md).</summary>
public sealed class HeartbeatSensorTests
{
    private static (CombatScene Scene, SBasePawn Spy, SGadHeartBeatSensor Hbs) Spy(bool Drawn = true, bool Select = true)
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, SBasePawn spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        pc.Rotation = default;
        GadgetKit.Shocker(scene, spy, Drawn);
        SGadHeartBeatSensor hbs = GadgetKit.Gadget(scene, spy, new SGadHeartBeatSensor(), Select);
        return (scene, spy, hbs);
    }

    [Fact]
    public void TheSensorReaches2200UnitsInA60DegreeCone()
    {
        var hbs = new SGadHeartBeatSensor();
        Assert.Equal(2200f, hbs.fDetectionDistance);
        Assert.Equal(0.866f, hbs.fDemiAngle);
        Assert.Equal(-10, hbs.Charge);
    }

    [Fact]
    public void APawnAheadWithinRangeIsMarked()
    {
        (CombatScene scene, _, SGadHeartBeatSensor hbs) = Spy();
        (_, SBasePawn merc) = scene.Player(false, new FVector(1500f, 100f, 100f));
        Assert.True(hbs.CheckForPawn());
        Assert.True(merc.bDetectedByHBS);
        Assert.Equal(1, hbs.StateOfDetect);
    }

    [Fact]
    public void APawnBeyondRangeOrOutsideTheConeIsNotMarked()
    {
        (CombatScene scene, _, SGadHeartBeatSensor hbs) = Spy();
        (_, SBasePawn far) = scene.Player(false, new FVector(2300f, 0f, 100f));
        (_, SBasePawn side) = scene.Player(false, new FVector(700f, 700f, 100f));
        (_, SBasePawn behind) = scene.Player(false, new FVector(-500f, 0f, 100f));
        Assert.True(hbs.CheckForPawn());
        Assert.False(far.bDetectedByHBS);
        Assert.False(side.bDetectedByHBS);
        Assert.False(behind.bDetectedByHBS);
        Assert.Equal(2, hbs.StateOfDetect);
    }

    [Fact]
    public void WallsAndTeamsDoNotMatter()
    {
        (CombatScene scene, _, SGadHeartBeatSensor hbs) = Spy();
        GadgetKit.Walls(scene, GadgetKit.WallAt(500f));
        (_, SBasePawn mate) = scene.Player(true, new FVector(1000f, 0f, 100f));
        Assert.True(hbs.CheckForPawn());
        Assert.True(mate.bDetectedByHBS);
    }

    [Fact]
    public void AMarkClearsWhenThePawnLeavesTheCone()
    {
        (CombatScene scene, _, SGadHeartBeatSensor hbs) = Spy();
        (_, SBasePawn merc) = scene.Player(false, new FVector(1000f, 0f, 100f));
        hbs.CheckForPawn();
        Assert.True(merc.bDetectedByHBS);
        merc.SetLocation(new FVector(-1000f, 0f, 100f));
        hbs.CheckForPawn();
        Assert.False(merc.bDetectedByHBS);
    }

    [Fact]
    public void TheSensorScansOnlyWhenSelectedWithTheShockerDrawn()
    {
        (CombatScene scene, SBasePawn spy, SGadHeartBeatSensor hbs) = Spy(Drawn: false);
        (_, SBasePawn merc) = scene.Player(false, new FVector(1000f, 0f, 100f));
        Assert.False(hbs.CheckForPawn());
        Assert.False(merc.bDetectedByHBS);
        spy.WeaponLoaded = 2;
        spy.SelectedGadget = null;
        Assert.False(hbs.CheckForPawn());
        spy.SelectedGadget = hbs;
        Assert.True(hbs.CheckForPawn());
        Assert.True(merc.bDetectedByHBS);
    }

    [Fact]
    public void TheSensorScansEveryTick()
    {
        (CombatScene scene, _, _) = Spy();
        (_, SBasePawn merc) = scene.Player(false, new FVector(1000f, 0f, 100f));
        scene.World.Step();
        Assert.True(merc.bDetectedByHBS);
    }
}
