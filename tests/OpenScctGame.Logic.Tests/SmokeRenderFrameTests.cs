using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Render;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class SmokeRenderFrameTests
{
    [Fact]
    public void DisabledCaptureDoesNotCreateSmokeServices()
    {
        var world = new World(1, ENetMode.NM_Client);
        world.Level.Spawn<GameReplicationInfo>();
        Assert.Same(SmokeRenderFrame.Disabled, SmokeRenderCache.Capture(world.Level));
        Assert.Null(world.Level.GetService<ResponsiveSmokeWorld>());
    }

    [Fact]
    public void UnchangedCloudIsReusedWhileFrameTimeIsPublishedIndependently()
    {
        var (world, smoke, cloud, snapshot) = Fixture();
        smoke.ApplySnapshot(cloud, snapshot);
        smoke.SynchronizeTime(2);
        SmokeRenderFrame first = SmokeRenderCache.Capture(world.Level);
        smoke.SynchronizeTime(3);
        SmokeRenderFrame next = SmokeRenderCache.Capture(world.Level);
        Assert.Same(first.Clouds[0], next.Clouds[0]);
        Assert.Equal(2, first.CurrentTime);
        Assert.Equal(3, next.CurrentTime);
        cloud.Destroy();
        Assert.Empty(SmokeRenderCache.Capture(world.Level).Clouds);
        Assert.Single(first.Clouds); // A queued frame remains valid after live actor destruction.
    }

    [Fact]
    public void LateGeometryBaselineInvalidatesRenderCacheWithoutUndoingNewerCarves()
    {
        var (world, smoke, cloud, snapshot) = Fixture();
        smoke.ApplySnapshot(cloud, snapshot);
        var mask = new byte[ResponsiveSmokeField.CellCount];
        Array.Fill(mask, (byte)1);
        smoke.ApplyCarves(cloud, 20, 2,
            [new(1, ResponsiveSmokeCarveKind.Blast, default, default, 300, 25, 2, .6f, 1.4f, mask)]);
        SmokeRenderCloud before = Assert.Single(SmokeRenderCache.Capture(world.Level).Clouds);
        Assert.Equal((20u, 10u), before.ContentStamp);
        var cells = (byte[])snapshot.Cells.Clone();
        cells[4] = 0;
        smoke.ApplySnapshot(cloud, snapshot with { Revision = 15, Cells = cells });
        SmokeRenderCloud after = Assert.Single(SmokeRenderCache.Capture(world.Level).Clouds);
        Assert.Equal((20u, 15u), after.ContentStamp);
        Assert.Same(before.Identity, after.Identity);
        Assert.NotSame(before, after);
        Assert.Equal(1, before.Cells[4]);
        Assert.Equal(0, after.Cells[4]);
        Assert.Single(after.Carves);
        Assert.Equal(ResponsiveSmokeCarveKind.Blast, after.Carves[0].Kind);
        // Neither incoming payload mutation nor a subsequent live carve replacement changes a published frame.
        Array.Clear(mask);
        smoke.ApplyCarves(cloud, 21, 2, []);
        Assert.Equal(1, before.Carves[0].AffectedCells!.Value.Span[4]);
        Assert.Single(after.Carves);
        Assert.Empty(Assert.Single(SmokeRenderCache.Capture(world.Level).Clouds).Carves);
    }

    [Fact]
    public void PublishedBoundaryPlanesDoNotAliasIncomingPayloadOrLiveField()
    {
        var (world, smoke, cloud, snapshot) = Fixture();
        var original = new ResponsiveSmokePlane(new FVector(1, 0, 0), 2);
        var ceiling = new ResponsiveSmokePlane(new FVector(0, 0, 1), -3);
        var planes = new[] { original, ceiling };
        var boundaries = new[] { new ResponsiveSmokeBoundaryCell(4, planes) };
        snapshot.Cells[4] = 0;
        smoke.ApplySnapshot(cloud, snapshot with { Boundaries = boundaries });

        // A decoded/network payload remains caller-owned after ApplySnapshot.
        planes[0] = new(new FVector(0, 1, 0), 8);
        boundaries[0] = new(5, []);
        SmokeRenderCloud published = Assert.Single(SmokeRenderCache.Capture(world.Level).Clouds);
        SmokeRenderBoundary boundary = Assert.Single(published.Boundaries);
        Assert.Equal((ushort)4, boundary.Cell);
        Assert.Equal(2, boundary.Planes.Length);
        Assert.Equal(original, boundary.Planes[0]);
        Assert.Equal(ceiling, boundary.Planes[1]);

        // The simulation may subsequently replace or edit its boundary storage.
        ResponsiveSmokeField field = smoke.Fields[cloud];
        field.Boundaries[4][0] = new(new FVector(0, 1, 0), -5);
        cloud.Destroy();
        Assert.Empty(SmokeRenderCache.Capture(world.Level).Clouds);
        Assert.Equal(original, boundary.Planes[0]);
        Assert.Equal(ceiling, boundary.Planes[1]);
    }

    [Fact]
    public void LateBoundaryOnlyBaselinePublishesNewPlanesWithoutUndoingNewerCarves()
    {
        var (world, smoke, cloud, snapshot) = Fixture();
        var firstPlane = new ResponsiveSmokePlane(new FVector(1, 0, 0), -2);
        var nextPlane = new ResponsiveSmokePlane(new FVector(1, 0, 0), 3);
        snapshot.Cells[4] = 0;
        snapshot = snapshot with { Boundaries = [new(4, [firstPlane])] };
        smoke.ApplySnapshot(cloud, snapshot);
        smoke.ApplyCarves(cloud, 20, 2,
            [new(1, ResponsiveSmokeCarveKind.Bullet, default, new FVector(100, 0, 0), 12, 6, 2, .1f, .4f, null)]);
        SmokeRenderCloud before = Assert.Single(SmokeRenderCache.Capture(world.Level).Clouds);

        // Occupancy and common revision stay unchanged; only the reliable geometry stamp advances.
        smoke.ApplySnapshot(cloud, snapshot with { Revision = 15, Boundaries = [new(4, [nextPlane])] });
        SmokeRenderCloud after = Assert.Single(SmokeRenderCache.Capture(world.Level).Clouds);
        Assert.Equal((20u, 10u), before.ContentStamp);
        Assert.Equal((20u, 15u), after.ContentStamp);
        Assert.Same(before.Identity, after.Identity);
        Assert.NotSame(before, after);
        Assert.Equal(before.Cells.ToArray(), after.Cells.ToArray());
        Assert.Equal(firstPlane, Assert.Single(before.Boundaries).Planes[0]);
        Assert.Equal(nextPlane, Assert.Single(after.Boundaries).Planes[0]);
        Assert.Equal(ResponsiveSmokeCarveKind.Bullet, Assert.Single(after.Carves).Kind);

        smoke.ApplySnapshot(cloud, snapshot with { Revision = 21, Boundaries = [] });
        Assert.Empty(Assert.Single(SmokeRenderCache.Capture(world.Level).Clouds).Boundaries);
        Assert.Equal(firstPlane, Assert.Single(before.Boundaries).Planes[0]);
        Assert.Equal(nextPlane, Assert.Single(after.Boundaries).Planes[0]);
    }

    private static (World World, ResponsiveSmokeWorld Smoke, SSmokeEmitter Cloud, ResponsiveSmokeSnapshot Snapshot) Fixture()
    {
        var world = new World(1, ENetMode.NM_Client);
        world.Level.Spawn<GameReplicationInfo>()!.ResponsiveSmokeEnabled = true;
        SSmokeEmitter cloud = world.Level.Spawn<SSmokeEmitter>()!;
        var template = new ResponsiveSmokeField(default, 0);
        var cells = new byte[ResponsiveSmokeField.CellCount];
        Array.Fill(cells, (byte)1);
        var snapshot = new ResponsiveSmokeSnapshot(10, 0, 2, template.Origin, template.Center,
            cells, new ushort[ResponsiveSmokeField.CellCount], []);
        return (world, ResponsiveSmokeWorld.For(world.Level), cloud, snapshot);
    }
}
