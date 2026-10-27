using System.Collections.Generic;
using System.IO;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Traversal;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

public sealed class TraversalTests
{
    private static TraversalDoc TestBoxGEs() => TraversalDoc.Load(Path.Combine(TestPaths.TestBox, "sim", "traversal.json"));

    [Fact]
    public void ZonesTurnWithThePawnYaw()
    {
        FVector c = GEZones.NormalFeet.CenterFor(V(100f, 200f, 300f), new FRotator(0, 16384, 0));
        Near(V(98f, 239f, 215f), c, 1e-3f);
        FVector straight = GEZones.LedgeGrabArms.CenterFor(V(0f, 0f, 0f), default);
        Near(V(39f, 0f, 117f), straight);
        Assert.Equal(139.7f, GEZones.NormalArms.Radius);
        Assert.Equal(new FVector(0f, 0f, 116f), GEZones.ZipLineArms.Offset);
    }

    [Fact]
    public void QueriesFilterByKindAndActivationMask()
    {
        var index = new GEIndex(TestBoxGEs());
        var hits = new List<GEHit>();
        // GE1, the mezzanine edge ledge, has ID 4: active only while the mask has bit 4.
        FVector onGe1 = V(1900f, 832f, 280f);
        Assert.Equal(0, index.QuerySphere(onGe1, 20f, (int)GEKind.LG, 0, hits));
        Assert.Equal(1, index.QuerySphere(onGe1, 20f, (int)GEKind.LG, 4, hits));
        Assert.Equal("GE1", hits[0].GE.Id);
        Assert.Equal(0f, hits[0].Distance, 3);
        Near(onGe1, hits[0].ClosestPoint);

        // The zip line is a ZL: an LG query does not see it, a ZL query does; mercs take neither.
        FVector onZip = V(2000f, 544f, 275f);
        Assert.Equal(0, index.QuerySphere(onZip, 30f, (int)GEKind.LG, 4, hits));
        Assert.Equal(1, index.QuerySphere(onZip, 30f, K.SpyGEKinds, 4, hits));
        Assert.Equal(GEKind.ZL, hits[0].GE.Kind);
        Assert.Equal(0, index.QuerySphere(onZip, 30f, K.MercGEKinds, 4, hits));

        // Results come in GE list order.
        Assert.Equal(3, index.QuerySphere(V(1300f, 600f, 250f), 2000f, 0x7F, 4, hits));
        Assert.Equal(["GE0", "GE1", "GE2"], hits.ConvertAll(h => h.GE.Id));
    }

    private static (GEQueries Queries, CollisionWorld World, GERecord Ledge) LedgeScene(bool shelfUnderLip)
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(0f, 832f, 0f), V(512f, 1024f, 224f)); // the ledge block of TestBox room A, a bit wider
        if (shelfUnderLip)
        {
            b.AddBox(V(200f, 780f, 60f), V(320f, 832f, 110f)); // something under the lip where the body hangs
        }

        CollisionWorld world = CollisionWorld.Build(b.Build());
        var ledge = new GERecord(0, "GE0", GEKind.LG, [V(0f, 832f, 224f), V(512f, 832f, 224f)], 0, GEPlatform.Common, GESource.Synthetic());
        return (new GEQueries(new GEIndex([ledge]), world), world, ledge);
    }

    [Fact]
    public void HangSpotSitsBelowTheLedgeOnThePawnsSide()
    {
        (_, _, GERecord ledge) = LedgeScene(false);
        // The decoded hang spot: 39 out from the ledge on the pawn's side, 117 below it (m_LedgeGrabArmsZone).
        FVector spot = GEQueries.LedgeHangSpot(ledge, V(256f, 700f, 150f), new FVector(39f, 0f, 117f));
        Near(V(256f, 832f - 39f, 224f - 117f), spot, 1e-3f);
        Near(V(0f, -1f, 0f), GEQueries.LedgeOutNormal(ledge, V(256f, 700f, 150f)));
        Near(V(0f, 1f, 0f), GEQueries.LedgeOutNormal(ledge, V(256f, 900f, 300f)));
    }

    [Fact]
    public void LedgeClearanceNeedsBothBoxTracesClear()
    {
        FVector from = V(256f, 740f, 160f);
        (GEQueries clear, _, GERecord ledge) = LedgeScene(false);
        FVector spot = GEQueries.LedgeHangSpot(ledge, from, new FVector(39f, 0f, 117f));
        Assert.True(clear.TestLedgeClearance(null, from, spot));

        (GEQueries blocked, _, _) = LedgeScene(true);
        Assert.False(blocked.TestLedgeClearance(null, from, spot));

        // The ledge grab arms zone of a spy hanging there, facing the wall, holds the ledge.
        var hits = new List<GEHit>();
        Assert.Equal(1, clear.FindInZone(spot, new FRotator(0, 16384, 0), GEZones.LedgeGrabArms, (int)GEKind.LG, 0, hits));
        Assert.True(hits[0].Distance < GEZones.LedgeGrabArms.Radius);
    }

    [Fact]
    public void FindInZoneReadsTheLevelsActivationMask()
    {
        var index = new GEIndex(TestBoxGEs());
        var queries = new GEQueries(index, CollisionWorld.Build(new CollisionDataBuilder().Build()));
        var world = new World(1);
        TestPawn pawn = world.Level.SpawnActor(new TestPawn(), V(1900f, 790f, 170f), new FRotator(0, 16384, 0))!;
        var hits = new List<GEHit>();
        world.Level.Info.GEActivationMask = 4;
        Assert.Equal(1, queries.FindInZone(pawn, GEZones.NormalArms, K.SpyGEKinds, hits));
        world.Level.Info.GEActivationMask = 0;
        Assert.Equal(0, queries.FindInZone(pawn, GEZones.NormalArms, K.SpyGEKinds, hits));
    }
}
