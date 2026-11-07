using System;
using System.IO;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class ResponsiveSmokeBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoorwayPassesSmokeIntoTheNextRoomButItsSolidJambAndHeaderDoNot(bool sealedOpening)
    {
        var builder = new CollisionDataBuilder();
        builder.AddBox(new FVector(-1000,-1000,-50),new FVector(1000,1500,0));
        builder.AddBox(new FVector(-1000,512,0),new FVector(-28,544,1000));
        builder.AddBox(new FVector(100,512,0),new FVector(1000,544,1000));
        builder.AddBox(new FVector(-28,512,224),new FVector(100,544,1000));
        if (sealedOpening) builder.AddBox(new FVector(-28,512,0),new FVector(100,544,224));
        CombatScene scene = Scene(builder);
        SSmokeEmitter cloud = scene.Level.SpawnActor(new SSmokeEmitter(),new FVector(36,630,5),default)!;
        scene.Run(3);
        ResponsiveSmokeField field = Field(scene,cloud);
        Assert.Equal(0f,field.SampleDensity(new FVector(-75,528,80),scene.Time));
        Assert.Equal(0f,field.SampleDensity(new FVector(36,528,230),scene.Time));
        float nearRoom = field.SampleDensity(new FVector(36,480,145),scene.Time);
        if (sealedOpening) Assert.Equal(0f,nearRoom);
        else Assert.True(nearRoom > .99f, $"doorway density={nearRoom}, center={field.Center}");
    }

    private static CombatScene Scene(CollisionDataBuilder builder)
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        scene.Level.SpawnActor(new GameReplicationInfo { ResponsiveSmokeEnabled = true }, default, default);
        scene.Level.SetService<ICollisionWorld>(CollisionWorld.Build(builder.Build()));
        return scene;
    }
    private static ResponsiveSmokeField Field(CombatScene scene, SSmokeEmitter cloud)
    { Assert.True(ResponsiveSmokeWorld.For(scene.Level).TryGet(cloud, out var field)); return field; }

    [Theory]
    [InlineData(0f)]
    [InlineData(7f)]
    [InlineData(13f)]
    public void ActualGroundedSmokeGrenadeBloomsUpwardAndTouchesItsFloor(float floor)
    {
        var builder = new CollisionDataBuilder();
        builder.AddBox(new FVector(-1000, -1000, floor - 50), new FVector(1000, 1000, floor));
        CombatScene scene = Scene(builder);
        var grenade = scene.Level.SpawnActor(new SProjSmoke { QuickLaunch = true }, new FVector(0, 0, floor + 2), default)!;
        grenade.Explode(grenade.Location, new FVector(0, 0, 1));
        SSmokeEmitter cloud = Assert.IsType<SSmokeEmitter>(grenade.Smoke);
        scene.Run(2);
        ResponsiveSmokeField field = Field(scene, cloud);
        Assert.Equal(cloud.Location, field.SourcePosition);
        Assert.Equal(cloud.Location + new FVector(0, 0, 140), field.Center);
        Assert.InRange(field.SampleDensity(new FVector(0, 0, floor + .1f), scene.Time), .99f, 1f);
        Assert.Equal(0f, field.SampleDensity(new FVector(0, 0, floor - .1f), scene.Time));
        Assert.InRange(field.SampleDensity(new FVector(0, 0, floor + 200), scene.Time), .99f, 1f);
        Assert.InRange(field.SampleDensity(new FVector(0, 0, floor + 350), scene.Time), .55f, 1f);
        Assert.True(ResponsiveSmokeWorld.For(scene.Level).OpticalDepth(new FVector(-400, 0, floor + 200), new FVector(400, 0, floor + 200)) > 6f);
        Assert.Equal(0f, field.SampleDensity(field.Center + new FVector(301, 0, 0), scene.Time));
        Assert.Equal(0f, field.SampleDensity(field.Center, field.CreatedAt + 10));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(7f)]
    public void ThinWallBothSidesFillToTheSurfaceWithoutSharingArrival(float wall)
    {
        var builder = new CollisionDataBuilder();
        builder.AddQuad(new FVector(wall, -1000, -1000), new FVector(wall, 1000, -1000),
            new FVector(wall, 1000, 1000), new FVector(wall, -1000, 1000));
        CombatScene scene = Scene(builder);
        SSmokeEmitter left = scene.Level.SpawnActor(new SSmokeEmitter(), new FVector(wall - 100, 0, 0), default)!;
        SSmokeEmitter right = scene.Level.SpawnActor(new SSmokeEmitter(), new FVector(wall + 100, 0, 0), default)!;
        scene.Run(2);
        ResponsiveSmokeField a = Field(scene, left), b = Field(scene, right);
        FVector near = new(wall - .1f, 0, 0), far = new(wall + .1f, 0, 0);
        Assert.True(a.SampleDensity(near, scene.Time) > .99f);
        Assert.Equal(0f, a.SampleDensity(far, scene.Time));
        Assert.True(b.SampleDensity(far, scene.Time) > .99f);
        Assert.Equal(0f, b.SampleDensity(near, scene.Time));
        Assert.Equal(0f, a.SampleDensity(new FVector(wall, 0, 0), scene.Time));
    }

    [Fact]
    public void AngledZeroThicknessWallClipsBothAirRegionsInsideOneVoxel()
    {
        var builder = new CollisionDataBuilder();
        builder.AddQuad(new FVector(-1000, 1010, -1000), new FVector(1010, -1000, -1000),
            new FVector(1010, -1000, 1000), new FVector(-1000, 1010, 1000));
        CombatScene scene = Scene(builder);
        SSmokeEmitter left = scene.Level.SpawnActor(new SSmokeEmitter(), new FVector(-100, -100, 0), default)!;
        SSmokeEmitter right = scene.Level.SpawnActor(new SSmokeEmitter(), new FVector(100, 100, 0), default)!;
        scene.Run(2);
        ResponsiveSmokeField a = Field(scene, left), b = Field(scene, right);
        FVector near = new(4.9f, 4.9f, 0), far = new(5.1f, 5.1f, 0);
        Assert.Equal(a.CellIndex(near), a.CellIndex(far));
        Assert.True(a.SampleDensity(near, scene.Time) > .99f);
        Assert.Equal(0f, a.SampleDensity(far, scene.Time));
        Assert.True(b.SampleDensity(far, scene.Time) > .99f);
        Assert.Equal(0f, b.SampleDensity(near, scene.Time));
    }

    [Fact]
    public void GroundedThreeSurfaceCornerSeedsAndClearsItsBoundaryAir()
    {
        var builder = new CollisionDataBuilder();
        builder.AddBox(new FVector(-1000, -1000, -50), new FVector(1000, 1000, 0));
        builder.AddQuad(new FVector(0, -1000, -1000), new FVector(0, 1000, -1000), new FVector(0, 1000, 1000), new FVector(0, -1000, 1000));
        builder.AddQuad(new FVector(-1000, 0, -1000), new FVector(1000, 0, -1000), new FVector(1000, 0, 1000), new FVector(-1000, 0, 1000));
        CombatScene scene = Scene(builder);
        var grenade = scene.Level.SpawnActor(new SProjSmoke { QuickLaunch = true }, new FVector(2, 2, 2), default)!;
        grenade.Explode(grenade.Location, new FVector(0, 0, 1));
        SSmokeEmitter cloud = Assert.IsType<SSmokeEmitter>(grenade.Smoke); scene.Run(2);
        ResponsiveSmokeField field = Field(scene, cloud);
        FVector point = new(.1f, .1f, .1f);
        Assert.True(field.SampleDensity(point, scene.Time) > .99f);
        Assert.Equal(0f, field.SampleDensity(new FVector(-.1f, .1f, .1f), scene.Time));
        ResponsiveSmokeWorld.Blast(cloud, new FVector(2, 2, 2), 300);
        Assert.Equal(0f, field.SampleDensity(point, scene.Time));
        scene.Run(2.1f);
        Assert.True(field.SampleDensity(point, scene.Time) > .99f);
    }

    [Fact]
    public void RotatedMeshUsesItsActualSurfaceInsteadOfExpandedLocalBoxBounds()
    {
        var builder = new CollisionDataBuilder();
        builder.AddBoxMeshCollider(ColliderKind.MeshPoly, null, CollisionDataBuilder.WorldFlags,
            [(new FVector(-1, -1000, -1000), new FVector(1, 1000, 1000))], rotation: new FRotator(0, 8192, 0));
        CombatScene scene = Scene(builder);
        SSmokeEmitter cloud = scene.Level.SpawnActor(new SSmokeEmitter(), new FVector(-100, -100, 0), default)!;
        scene.Run(2); ResponsiveSmokeField field = Field(scene, cloud);
        Assert.True(field.SampleDensity(new FVector(-.8f, -.8f, 0), scene.Time) > .99f);
        Assert.Equal(0f, field.SampleDensity(default, scene.Time));
        Assert.Equal(0f, field.SampleDensity(new FVector(.8f, .8f, 0), scene.Time));
    }

    [Fact]
    public void RenormalizationRetainsUnreachedFreeCellWeightsAtTheGrowthFront()
    {
        var field = new ResponsiveSmokeField(default, 0);
        Array.Fill(field.Cells, (byte)1);
        int center = field.CellIndex(default); field.ActivationTicks[center] = 0;
        FVector midpoint = field.CellCenter(center) + new FVector(12.5f, 0, 0);
        Assert.InRange(field.SampleDensity(midpoint, 2), .499f, .501f);
    }

    [Fact]
    public void SurfacePlanesRoundTripInFullAndSparseSnapshotsWithoutAliasing()
    {
        var builder = new CollisionDataBuilder(); builder.AddBox(new FVector(-1000, -1000, -50), new FVector(1000, 1000, 0));
        CombatScene scene = Scene(builder);
        SSmokeEmitter cloud = scene.Level.SpawnActor(new SSmokeEmitter(), new FVector(0, 0, 5), default)!; scene.Run(2);
        ResponsiveSmokeField field = Field(scene, cloud);
        ResponsiveSmokeSnapshot before = field.Capture(scene.Time);
        Assert.NotEmpty(before.Boundaries!);
        ResponsiveSmokeSnapshot decoded = ResponsiveSmokeCodec.Decode(ResponsiveSmokeCodec.Encode(before));
        Assert.Equal(before.Boundaries![0].Planes, decoded.Boundaries![0].Planes);
        var empty = before with { Revision = before.Revision + 1, Boundaries = [] };
        var removed = ResponsiveSmokeCodec.DecodeDelta(before, ResponsiveSmokeCodec.EncodeDelta(before, empty));
        Assert.Empty(removed.Boundaries!);
        var added = before with { Revision = empty.Revision + 1 };
        var restored = ResponsiveSmokeCodec.DecodeDelta(empty, ResponsiveSmokeCodec.EncodeDelta(empty, added));
        Assert.Equal(before.Boundaries.Length, restored.Boundaries!.Length);
        Assert.Equal(before.Boundaries[0].Planes, restored.Boundaries[0].Planes);
        ResponsiveSmokePlane original = field.Boundaries[before.Boundaries[0].Cell][0];
        before.Boundaries[0].Planes[0] = new(default, 0);
        Assert.Equal(original, field.Boundaries[before.Boundaries[0].Cell][0]);
        Assert.Equal(original, decoded.Boundaries[0].Planes[0]);
        Assert.Throws<InvalidDataException>(() => ResponsiveSmokeCodec.Encode(before));
    }
}
