using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed partial class ResponsiveSmokeNetworkTests
{
    [Fact]
    public void CodecMeasurementsAreOptInAndSeparateBaselineDeltaAndClearingWork()
    {
        using var rig = Create("A");
        Level level = rig.Server.World.Level;
        level.Spawn<GameReplicationInfo>()!.ResponsiveSmokeEnabled = true;
        Step(rig, 120);
        _ = SpawnCloud(level); Step(rig, 60);
        var sender = ResponsiveSmokeReplication.For(rig.Server.Driver);
        Assert.False(sender.DiagnosticsEnabled);
        Assert.Equal(default, sender.Diagnostics);
        foreach (var client in rig.Clients)
            Assert.Equal(default, ResponsiveSmokeReplication.For(client.Driver).Diagnostics);

        sender.DiagnosticsEnabled = true;
        foreach (var client in rig.Clients) ResponsiveSmokeReplication.For(client.Driver).DiagnosticsEnabled = true;
        SSmokeEmitter cloud = SpawnCloud(level); Step(rig, 60);
        var world = ResponsiveSmokeWorld.For(level);
        ResponsiveSmokeSnapshot snapshot = world.Capture(cloud);
        snapshot.Cells[0] = 0;
        world.ApplySnapshot(cloud, snapshot with { Revision = snapshot.Revision + 1 });
        Step(rig, 60);
        ResponsiveSmokeWorld.Bullet(cloud, cloud.Location, cloud.Location + new OpenScct.Core.FVector(100, 0, 0));
        Step(rig, 12);
        Assert.True(sender.Diagnostics.Flush.Calls > 0);
        Assert.True(sender.Diagnostics.BaselineEncode.Calls > 0);
        Assert.True(sender.Diagnostics.DeltaEncode.Calls > 0);
        Assert.True(sender.Diagnostics.CarveEncode.Calls > 0);
        Assert.True(sender.Diagnostics.BaselineEncode.AllocatedBytes > 0);
        Assert.True(sender.Diagnostics.BaselineEncode.Milliseconds > 0);
        foreach (var client in rig.Clients)
        {
            var receiver = ResponsiveSmokeReplication.For(client.Driver);
            Assert.True(receiver.Diagnostics.BaselineDecode.Calls > 0);
            Assert.True(receiver.Diagnostics.DeltaDecode.Calls > 0);
            Assert.True(receiver.Diagnostics.CarveDecode.Calls > 0);
            Assert.True(receiver.Diagnostics.BaselineDecode.AllocatedBytes > 0);
            Assert.Equal(0, receiver.InvalidPayloads);
        }
        ResponsiveSmokeReplicationDiagnostics measured = sender.Diagnostics;
        sender.DiagnosticsEnabled = false;
        Step(rig, 35);
        Assert.Equal(measured, sender.Diagnostics);
    }
}
