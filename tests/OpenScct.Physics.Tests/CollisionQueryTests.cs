using System.Collections.Generic;
using System.Collections.Immutable;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

public sealed class CollisionQueryTests
{
    private const ETraceFlags World = ETraceFlags.TRACE_World;
    private static readonly FVector Zero = default;
    private static readonly FVector Pawn = new(35f, 35f, 87.5f);

    private static CollisionWorld BoxWorld()
    {
        var b = new CollisionDataBuilder();
        b.AddBox(V(-50f, -50f, -10f), V(50f, 50f, 0f));
        return CollisionWorld.Build(b.Build());
    }

    [Fact]
    public void ZeroExtentLineHitsTheTopFace()
    {
        CollisionWorld w = BoxWorld();
        Assert.True(w.LineCheck(out FCheckResult hit, null, V(10f, 5f, -100f), V(10f, 5f, 100f), Zero, World));
        // A BSP line hit is pulled back half a unit along the trace.
        Assert.Equal(0.5f - 0.5f / 200f, hit.Time, 5);
        Assert.Equal(0.5f, hit.Location.Z, 4);
        Near(V(0f, 0f, 1f), hit.Normal);
        Assert.Equal(0, hit.Collider);
        Assert.Equal(0, hit.Material);
        Assert.False(hit.bStartPenetrating);
    }

    [Fact]
    public void ZeroExtentLinesHitFrontFacesOnly()
    {
        CollisionWorld w = BoxWorld();
        // From below: the bottom face faces down, toward the line start.
        Assert.True(w.LineCheck(out FCheckResult below, null, V(0f, 0f, 100f), V(0f, 0f, -100f), Zero, World));
        Near(V(0f, 0f, -1f), below.Normal);
        Assert.Equal(-10.5f, below.Location.Z, 4);

        // From inside the solid the line leaves through back faces: no hit.
        Assert.False(w.LineCheck(out _, null, V(0f, 0f, 100f), V(0f, 0f, -5f), Zero, World));
    }

    [Fact]
    public void BoxSweepPullsBackByTheBspRule()
    {
        CollisionWorld w = BoxWorld();
        var extent = V(10f, 10f, 20f);
        // A 200 uu box trace: Time -= min(0.1 / 200, 0.01), a tenth of a unit.
        Assert.True(w.LineCheck(out FCheckResult hit, null, V(0f, 0f, -100f), V(0f, 0f, 100f), extent, World));
        Near(V(0f, 0f, 1f), hit.Normal);
        Assert.Equal(20.1f, hit.Location.Z, 3);
        Assert.Equal(0.4f - 0.1f / 200f, hit.Time, 5);

        // A 5 uu trace: Time -= 0.01 (0.1 / 5 is above the 0.01 cap).
        Assert.True(w.LineCheck(out FCheckResult shortHit, null, V(0f, 0f, 19f), V(0f, 0f, 24f), extent, World));
        Assert.Equal(0.8f - 0.01f, shortHit.Time, 5);

        // Below 1 uu: Time -= 0.01 / |D|.
        Assert.True(w.LineCheck(out FCheckResult tiny, null, V(0f, 0f, 19.9f), V(0f, 0f, 20.4f), extent, World));
        Assert.Equal(0.8f - 0.01f / 0.5f, tiny.Time, 4);
    }

    [Fact]
    public void BoxSweepCatchesTheBoxEdgeWhereALineMisses()
    {
        CollisionWorld w = BoxWorld();
        // A line passing 5 uu beside the box misses; a box of half size 10 on the same path hits the side.
        Assert.False(w.LineCheck(out _, null, V(200f, 55f, -5f), V(-200f, 55f, -5f), Zero, World));
        Assert.True(w.LineCheck(out FCheckResult hit, null, V(200f, 55f, -5f), V(-200f, 55f, -5f), V(10f, 10f, 10f), World));
        Near(V(-1f, 0f, 0f), hit.Normal);
        Assert.Equal(-60.1f, hit.Location.X, 3);
    }

    [Fact]
    public void SweepAlongASlopeReportsItsNormal()
    {
        var b = new CollisionDataBuilder();
        b.AddRamp(0f, 100f, -100f, 100f, 0f, 100f); // 45 degrees
        CollisionWorld w = CollisionWorld.Build(b.Build());
        Assert.True(w.LineCheck(out FCheckResult hit, null, V(50f, 0f, -50f), V(50f, 0f, 300f), V(10f, 10f, 10f), World));
        Assert.Equal(-0.70710677f, hit.Normal.X, 5);
        Assert.Equal(0.70710677f, hit.Normal.Z, 5);
    }

    [Fact]
    public void StartingInsideOnlyBlocksMovesThatGoDeeper()
    {
        CollisionWorld w = BoxWorld();
        var extent = V(10f, 10f, 20f);
        FVector inside = V(0f, 0f, 15f); // 5 uu into the top face
        Assert.True(w.LineCheck(out FCheckResult down, null, inside + V(0f, 0f, -10f), inside, extent, World));
        Assert.Equal(0f, down.Time);
        Assert.True(down.bStartPenetrating);
        Near(V(0f, 0f, 1f), down.Normal);

        Assert.False(w.LineCheck(out _, null, inside + V(0f, 0f, 10f), inside, extent, World));
        Assert.False(w.LineCheck(out _, null, inside + V(30f, 0f, 0f), inside, extent, World));
    }

    [Fact]
    public void PointCheckTreatsTrianglesAsSurfacesAndHullsAsSolids()
    {
        var b = new CollisionDataBuilder();
        b.AddBox(V(-100f, -100f, -100f), V(100f, 100f, 100f));
        b.AddBoxHullCollider(ColliderKind.MeshHull, null, CollisionDataBuilder.WorldFlags, V(-100f, -100f, -100f), V(100f, 100f, 100f),
            V(1000f, 0f, 0f));
        CollisionWorld w = CollisionWorld.Build(b.Build());
        var small = V(10f, 10f, 10f);

        // The normal is the shortest way out of the surface: the box centre is below the top face.
        Assert.True(w.PointCheck(out FCheckResult face, null, V(0f, 0f, 95f), small, ETraceFlags.TRACE_Everything));
        Near(V(0f, 0f, -1f), face.Normal);
        Assert.False(w.PointCheck(out _, null, V(0f, 0f, 0f), small, ETraceFlags.TRACE_Everything));
        Assert.False(w.PointCheck(out _, null, V(0f, 0f, 111f), small, ETraceFlags.TRACE_Everything));

        Assert.True(w.PointCheck(out FCheckResult solid, null, V(1000f, 0f, 0f), small, ETraceFlags.TRACE_Everything));
        Assert.Equal(1, solid.Collider);
        Assert.True(w.PointCheck(out _, null, V(1000f, 0f, 0f), Zero, ETraceFlags.TRACE_Everything));
    }

    [Fact]
    public void MultiLineCheckListsCollidersInOrderAndStopsAtTheWorld()
    {
        var b = new CollisionDataBuilder();
        b.AddBox(V(600f, -50f, -50f), V(700f, 50f, 50f));
        b.AddBox(V(900f, -50f, -50f), V(1000f, 50f, 50f));
        int gpo1 = b.AddBoxHullCollider(ColliderKind.Gpo, null, CollisionDataBuilder.WorldFlags, V(-10f, -50f, -50f), V(10f, 50f, 50f),
            V(400f, 0f, 0f));
        int gpo2 = b.AddBoxHullCollider(ColliderKind.Gpo, null, CollisionDataBuilder.WorldFlags, V(-10f, -50f, -50f), V(10f, 50f, 50f),
            V(200f, 0f, 0f));
        CollisionWorld w = CollisionWorld.Build(b.Build());
        var hits = new List<FCheckResult>();
        int n = w.MultiLineCheck(hits, null, V(2000f, 0f, 0f), V(0f, 0f, 0f), Zero, ETraceFlags.TRACE_AllColliding);
        Assert.Equal(3, n);
        Assert.Equal(gpo2, hits[0].Collider);
        Assert.Equal(gpo1, hits[1].Collider);
        Assert.Equal(0, hits[2].Collider);
        Assert.Equal(599.5f, hits[2].Location.X, 3);
        // Gameplay object meshes: the static mesh pull-back, 1 uu on a 2000 uu line.
        Assert.Equal(189f, hits[0].Location.X, 3);
        Assert.True(hits[0].Time < hits[1].Time && hits[1].Time < hits[2].Time);
    }

    [Fact]
    public void FastTraceReportsLineOfSight()
    {
        CollisionWorld w = BoxWorld();
        Assert.False(w.FastTrace(V(0f, 0f, -100f), V(0f, 0f, 100f)));
        Assert.True(w.FastTrace(V(500f, 0f, 100f), V(-500f, 0f, 100f)));
    }

    [Fact]
    public void ExtentChannelsCameraAndBulletFlags()
    {
        var b = new CollisionDataBuilder();
        const ColliderFlags volume = ColliderFlags.CollideActors | ColliderFlags.BlockActors | ColliderFlags.BlockPlayers |
            ColliderFlags.BlockNonZeroExtent;
        b.AddBoxHullCollider(ColliderKind.Brush, null, volume, V(-10f, -100f, -100f), V(10f, 100f, 100f), V(100f, 0f, 0f));
        const ColliderFlags glass = CollisionDataBuilder.WorldFlags | ColliderFlags.BulletGoesThrough;
        b.AddBoxHullCollider(ColliderKind.Glass, null, glass & ~ColliderFlags.BlockCamera, V(-10f, -100f, -100f), V(10f, 100f, 100f),
            V(300f, 0f, 0f));
        CollisionWorld w = CollisionWorld.Build(b.Build());
        FVector start = V(0f, 0f, 0f);
        FVector end = V(500f, 0f, 0f);

        // The blocking volume only stops box traces.
        Assert.True(w.LineCheck(out FCheckResult first, null, end, start, Zero, ETraceFlags.TRACE_AllColliding));
        Assert.Equal(2, first.Collider);
        Assert.True(w.LineCheck(out FCheckResult boxed, null, end, start, V(5f, 5f, 5f), ETraceFlags.TRACE_AllColliding));
        Assert.Equal(1, boxed.Collider);

        // Bullets pass bulletGoesThrough glass; cameras pass what does not block them.
        Assert.False(w.LineCheck(out _, null, end, start, Zero, ETraceFlags.TRACE_AllColliding | ETraceFlags.TRACE_Bullet));
        Assert.False(w.LineCheck(out _, null, end, start, V(5f, 5f, 5f), ETraceFlags.TRACE_AllColliding | ETraceFlags.TRACE_Camera));
    }

    [Fact]
    public void BlockingTracesFollowPlayersAndActorsFlags()
    {
        var b = new CollisionDataBuilder();
        const ColliderFlags playersOnly = ColliderFlags.CollideActors | ColliderFlags.BlockPlayers | ColliderFlags.BlockZeroExtent |
            ColliderFlags.BlockNonZeroExtent;
        b.AddBoxHullCollider(ColliderKind.Gpo, null, playersOnly, V(-10f, -100f, -100f), V(10f, 100f, 100f), V(100f, 0f, 0f));
        var world = new World(1);
        CollisionWorld w = CollisionWorld.Build(b.Build());
        w.BindLevel(world.Level);
        TestPawn pawn = world.Level.SpawnActor(new TestPawn(), V(0f, 0f, -1000f), default)!;
        TestTrigger thing = world.Level.SpawnActor(new TestTrigger { bCollideWorld = true, bBlockActors = true }, V(0f, 0f, 500f), default)!;

        Assert.True(w.LineCheck(out _, pawn, V(500f, 0f, 0f), V(0f, 0f, 0f), V(10f, 10f, 10f), ETraceFlags.TRACE_AllBlocking));
        Assert.False(w.LineCheck(out _, thing, V(500f, 0f, 0f), V(0f, 0f, 0f), V(10f, 10f, 10f), ETraceFlags.TRACE_AllBlocking));
        Assert.True(w.LineCheck(out _, thing, V(500f, 0f, 0f), V(0f, 0f, 0f), V(10f, 10f, 10f), ETraceFlags.TRACE_AllColliding));
    }

    [Fact]
    public void DisabledAndStartsDisabledCollidersDoNotCollide()
    {
        var b = new CollisionDataBuilder();
        int a = b.AddBoxHullCollider(ColliderKind.Gpo, null, CollisionDataBuilder.WorldFlags, V(-10f, -100f, -100f), V(10f, 100f, 100f),
            V(100f, 0f, 0f));
        int c = b.AddBoxHullCollider(ColliderKind.Gpo, null, CollisionDataBuilder.WorldFlags | ColliderFlags.StartsDisabled,
            V(-10f, -100f, -100f), V(10f, 100f, 100f), V(200f, 0f, 0f));
        CollisionWorld w = CollisionWorld.Build(b.Build());
        FVector end = V(500f, 0f, 0f);
        Assert.True(w.LineCheck(out FCheckResult hit, null, end, default, Zero, ETraceFlags.TRACE_AllColliding));
        Assert.Equal(a, hit.Collider);
        w.SetColliderEnabled(a, false);
        Assert.False(w.LineCheck(out _, null, end, default, Zero, ETraceFlags.TRACE_AllColliding));
        w.SetColliderEnabled(c, true);
        Assert.True(w.LineCheck(out hit, null, end, default, Zero, ETraceFlags.TRACE_AllColliding));
        Assert.Equal(c, hit.Collider);
    }

    [Fact]
    public void DynamicCollidersMoveWithTheirTransformAndActor()
    {
        var b = new CollisionDataBuilder();
        int door = b.AddBoxHullCollider(ColliderKind.Mover, "Door", CollisionDataBuilder.WorldFlags, V(-8f, -64f, 0f), V(8f, 64f, 256f),
            V(1000f, 0f, 0f));
        var world = new World(1);
        TestMover mover = world.Level.SpawnActor(new TestMover(), V(1000f, 0f, 0f), default)!;
        CollisionWorld w = CollisionWorld.Build(b.Build());
        w.BindLevel(world.Level, id => id == "Door" ? mover : null);

        Assert.True(w.LineCheck(out FCheckResult hit, null, V(2000f, 0f, 100f), V(0f, 0f, 100f), Zero, World));
        Assert.Equal(991f, hit.Location.X, 3);
        Assert.Same(mover, hit.Actor);

        mover.Location = V(500f, 0f, 0f);
        Assert.True(w.LineCheck(out hit, null, V(2000f, 0f, 100f), V(0f, 0f, 100f), Zero, World));
        Assert.Equal(491f, hit.Location.X, 3);

        mover.Location = V(500f, 0f, 300f);
        Assert.False(w.LineCheck(out _, null, V(2000f, 0f, 100f), V(0f, 0f, 100f), Zero, World));

        // A yawed collider: a quarter turn makes the door's long side face X.
        mover.Location = V(1000f, 0f, 0f);
        mover.Rotation = new FRotator(0, 16384, 0);
        Assert.True(w.LineCheck(out hit, null, V(2000f, 0f, 100f), V(0f, 0f, 100f), Zero, World));
        Assert.Equal(935f, hit.Location.X, 2);
        Assert.Equal(door, hit.Collider);
    }

    [Fact]
    public void RotatedHullReportsATurnedNormal()
    {
        var b = new CollisionDataBuilder();
        b.AddBoxHullCollider(ColliderKind.MeshHull, null, CollisionDataBuilder.WorldFlags, V(-50f, -50f, -50f), V(50f, 50f, 50f),
            V(500f, 0f, 0f), new FRotator(0, 8192, 0), writeCorners: false);
        CollisionWorld w = CollisionWorld.Build(b.Build());
        // Hitting the diagonal face of a box turned 45 degrees from off-centre.
        Assert.True(w.LineCheck(out FCheckResult hit, null, V(1000f, -20f, 0f), V(0f, -20f, 0f), Zero, World));
        Assert.Equal(-0.70710677f, hit.Normal.X, 4);
        Assert.Equal(-0.70710677f, hit.Normal.Y, 4);
        ColliderInfo info = w.GetCollider(1);
        Assert.Equal(500f - 70.71068f, info.Min.X, 2);
    }

    [Fact]
    public void MirroredMeshKeepsItsFacesPointingOut()
    {
        var b = new CollisionDataBuilder();
        b.AddBoxMeshCollider(ColliderKind.MeshPoly, null, CollisionDataBuilder.WorldFlags, [(V(0f, -50f, -50f), V(100f, 50f, 50f))],
            V(500f, 0f, 0f), scale: V(-1f, 1f, 1f));
        CollisionWorld w = CollisionWorld.Build(b.Build());
        // Mirrored in X the box spans x 400..500; a zero-extent line still hits its outer face.
        Assert.True(w.LineCheck(out FCheckResult hit, null, V(1000f, 0f, 0f), V(0f, 0f, 0f), Zero, World));
        Assert.Equal(399f, hit.Location.X, 3);
        Near(V(-1f, 0f, 0f), hit.Normal);
    }

    [Fact]
    public void ActorCylindersBlockLinesAndBoxes()
    {
        CollisionWorld w = BoxWorld();
        var world = new World(1);
        w.BindLevel(world.Level);
        TestPawn target = world.Level.SpawnActor(new TestPawn(), V(500f, 0f, 200f), default)!;
        TestPawn source = world.Level.SpawnActor(new TestPawn(), V(0f, 0f, 200f), default)!;
        w.NotifyCollisionChanged();

        Assert.True(w.LineCheck(out FCheckResult line, source, V(1000f, 0f, 200f), V(0f, 0f, 200f), Zero, ETraceFlags.TRACE_AllColliding));
        Assert.Same(target, line.Actor);
        Assert.Equal(-1, line.Collider);
        // A cylinder hit loses 0.001 of the trace time.
        Assert.Equal(464f, line.Location.X, 2);
        Near(V(-1f, 0f, 0f), line.Normal);

        Assert.True(w.LineCheck(out FCheckResult box, source, V(1000f, 0f, 200f), V(0f, 0f, 200f), Pawn, ETraceFlags.TRACE_AllBlocking));
        Assert.Same(target, box.Actor);
        // A box widens the cylinder by its extent: radius 35 + 35.
        Assert.Equal(429f, box.Location.X, 2);

        // Without TRACE_Pawns the pawn is not tested; the source never hits itself.
        Assert.False(w.LineCheck(out _, source, V(1000f, 0f, 200f), V(0f, 0f, 200f), Zero, ETraceFlags.TRACE_World));
        Assert.False(w.LineCheck(out _, target, V(1000f, 0f, 200f), V(0f, 0f, 200f), Zero, ETraceFlags.TRACE_AllColliding));
    }

    [Fact]
    public void CylindersOverlapWhenBothSumsExceedTheDistances()
    {
        CollisionWorld w = BoxWorld();
        var world = new World(1);
        w.BindLevel(world.Level);
        TestPawn a = world.Level.SpawnActor(new TestPawn(), V(0f, 0f, 0f), default)!;
        TestTrigger t = world.Level.SpawnActor(new TestTrigger(), V(98f, 0f, 0f), default)!; // radii 35 + 64 = 99
        Assert.True(w.IsOverlapping(a, t));
        t.Location = V(99f, 0f, 0f);
        Assert.False(w.IsOverlapping(a, t));
        t.Location = V(0f, 0f, 151f); // heights 87.5 + 64 = 151.5
        Assert.True(w.IsOverlapping(a, t));
        t.Location = V(0f, 0f, 152f);
        Assert.False(w.IsOverlapping(a, t));
    }

    [Fact]
    public void ZonesByBoundsAndByBsp()
    {
        var b = Floor(1000f);
        b.AddZone("ZoneA", V(-1000f, -1000f, -100f), V(0f, 1000f, 500f));
        b.AddZone("ZoneB", V(0f, -1000f, -100f), V(1000f, 1000f, 500f));
        var world = new World(1);
        world.Level.BeginLoad();
        ZoneInfo zoneA = world.Level.SpawnActor(new ZoneInfo(), default, default)!;
        world.Level.EndLoad();
        CollisionWorld w = CollisionWorld.Build(b.Build());
        w.BindLevel(world.Level, id => id == "ZoneA" ? zoneA : null);
        Assert.Equal(1, w.GetZoneIndex(V(-10f, 0f, 0f)));
        Assert.Equal(2, w.GetZoneIndex(V(10f, 0f, 0f)));
        Assert.Equal(0, w.GetZoneIndex(V(5000f, 0f, 0f)));
        Assert.Same(zoneA, w.GetZoneInfo(V(-10f, 0f, 0f)));
        Assert.Same(world.Level.Info, w.GetZoneInfo(V(5000f, 0f, 0f)));

        // One BSP node splitting at x = 0: front (x >= 0) is zone 2, back is zone 1.
        CollisionData flat = b.Build();
        Collider c0 = flat.Colliders[0];
        var withBsp = new CollisionData
        {
            Strings = flat.Strings,
            Vertices = flat.Vertices,
            Triangles = flat.Triangles,
            Materials = flat.Materials,
            Planes = flat.Planes,
            Hulls = flat.Hulls,
            BspNodes = ImmutableArray.Create(new CollisionBspNode(new CollisionPlane(V(1f, 0f, 0f), 0f), -1, -1, -1, -1, 0, 0, 0, 2, 1)),
            Colliders = flat.Colliders.SetItem(0, c0 with { BspStart = 0, BspCount = 1 }),
            Cylinders = flat.Cylinders,
            Zones = flat.Zones,
        };
        CollisionWorld bsp = CollisionWorld.Build(withBsp);
        Assert.Equal(2, bsp.GetZoneIndex(V(5000f, 0f, 0f)));
        Assert.Equal(1, bsp.GetZoneIndex(V(-5000f, 0f, 0f)));
        Assert.Equal(2, bsp.GetZoneIndex(V(0f, 0f, 0f)));
    }

    [Fact]
    public void TestBoxGeometry()
    {
        CollisionWorld w = CollisionWorld.Build(CollisionReader.Load(System.IO.Path.Combine(TestPaths.TestBox, "sim", "collision.bin")));
        Assert.Equal(548, w.TriangleCount);
        Assert.Equal(2, w.HullCount);

        // Spy spawn: the floor is at z 0.
        Assert.True(w.LineCheck(out FCheckResult floor, null, V(128f, 128f, -500f), V(128f, 128f, 100f), Pawn, World));
        Assert.Equal(87.6f, floor.Location.Z, 3);

        // The 30 degree ramp in room A.
        Assert.True(w.LineCheck(out FCheckResult ramp, null, V(640f, 896f, -100f), V(640f, 896f, 400f), Zero, World));
        Assert.Equal(0.8660254f, ramp.Normal.Z, 4);
        Assert.Equal(CollisionMaterialFlags.NoSplitJump, ramp.MaterialFlags);

        // The ledge block is climbable; its top is at 224.
        Assert.True(w.LineCheck(out FCheckResult ledge, null, V(256f, 900f, 0f), V(256f, 900f, 400f), Zero, World));
        Assert.Equal(224.5f, ledge.Location.Z, 3);
        Assert.Equal(CollisionMaterialFlags.Climbable, ledge.MaterialFlags);

        // The BlockingVolume (yaw 16384: x 2496..2560, y 832..1024, z 280..512) stops boxes but not lines.
        FVector from = V(2400f, 928f, 396f);
        FVector to = V(2700f, 928f, 396f);
        Assert.True(w.LineCheck(out FCheckResult vol, null, to, from, V(5f, 5f, 5f), World));
        Assert.Equal(2, vol.Collider);
        Assert.Equal(2496f - 5f - 0.1f, vol.Location.X, 2);
        Assert.Equal(CollisionMaterialFlags.Invisible, vol.MaterialFlags);
        Assert.True(w.LineCheck(out FCheckResult wall, null, to, from, Zero, World));
        Assert.Equal(0, wall.Collider);
        Assert.Equal(2559.5f, wall.Location.X, 2);
    }
}
