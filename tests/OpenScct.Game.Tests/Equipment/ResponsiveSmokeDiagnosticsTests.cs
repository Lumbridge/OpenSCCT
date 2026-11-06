using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class ResponsiveSmokeDiagnosticsTests
{
    [Fact]
    public void MeasurementsRemainOffByDefaultAndStopWhenDisabled()
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        scene.Level.SpawnActor(new GameReplicationInfo { ResponsiveSmokeEnabled = true }, default, default);
        var world = ResponsiveSmokeWorld.For(scene.Level);
        Assert.False(world.DiagnosticsEnabled);
        var first = scene.Level.SpawnActor(new SSmokeEmitter(), default, default)!;
        scene.Run(.1f); _ = world.Capture(first);
        Assert.Equal(default, world.Diagnostics);

        world.DiagnosticsEnabled = true;
        var second = scene.Level.SpawnActor(new SSmokeEmitter(), new FVector(125, 0, 0), default)!;
        scene.Run(.2f);
        ResponsiveSmokeSnapshot snapshot = world.Capture(second);
        world.ApplySnapshot(second, snapshot);
        world.ApplyCarves(second, snapshot.Revision, snapshot.CapturedAt, snapshot.Carves);
        ResponsiveSmokeWorldDiagnostics measured = world.Diagnostics;
        Assert.Equal(1, measured.Register.Calls);
        Assert.True(measured.Register.AllocatedBytes >= ResponsiveSmokeField.CellCount * 7);
        Assert.True(measured.GeometryRefresh.Calls > 0);
        Assert.True(measured.ClassifyFront.Calls > 0);
        Assert.True(measured.Exposure.Calls > 0);
        Assert.Equal(1, measured.Capture.Calls);
        Assert.True(measured.Capture.AllocatedBytes >= ResponsiveSmokeField.CellCount * 3);
        Assert.Equal(2, measured.Apply.Calls);
        Assert.True(measured.Register.Milliseconds > 0);
        Assert.True(measured.Capture.Milliseconds > 0);

        world.DiagnosticsEnabled = false;
        _ = scene.Level.SpawnActor(new SSmokeEmitter(), new FVector(-125, 0, 0), default);
        scene.Run(.1f); _ = world.Capture(first);
        world.ApplySnapshot(second, snapshot);
        Assert.Equal(measured, world.Diagnostics);
    }
}
