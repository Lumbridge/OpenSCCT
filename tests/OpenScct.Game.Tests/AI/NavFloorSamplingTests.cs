using System.Collections.Generic;
using System.Linq;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Game.Tests.AI;

public sealed class NavFloorSamplingTests
{
    [Theory]
    [InlineData(false, false, 48f)]
    [InlineData(false, false, 80f)]
    [InlineData(false, false, 128f)]
    [InlineData(false, true, 48f)]
    [InlineData(false, true, 80f)]
    [InlineData(false, true, 128f)]
    [InlineData(true, false, 48f)]
    [InlineData(true, false, 80f)]
    [InlineData(true, false, 128f)]
    [InlineData(true, true, 48f)]
    [InlineData(true, true, 80f)]
    [InlineData(true, true, 128f)]
    public void ContinuousStepDoesNotInventTreadsInsideASolidRiser(bool mesh, bool merc, float height)
    {
        var level = new CollisionDataBuilder();
        level.AddBox(new FVector(-800f, -800f, -64f), new FVector(800f, 1000f, 0f));
        var low = new FVector(-100f, 400f, 0f);
        var high = new FVector(100f, 600f, height);
        if (mesh)
            level.AddBoxMeshCollider(ColliderKind.MeshPoly, null, CollisionDataBuilder.WorldFlags, [(low, high)]);
        else
            level.AddBox(low, high);
        NavGraph graph = NavBuilder.Build(CollisionWorld.Build(level.Build()),
            new FVector(-640f, -640f, -100f), new FVector(640f, 960f, 400f), merc ? NavProfile.Merc : NavProfile.Spy);
        int start = graph.NearestNode(new FVector(0f, -400f, 0f));
        int top = graph.NearestNode(new FVector(0f, 500f, height));
        Assert.InRange(graph.Node(top).Floor.Z, height, height + 1f);
        Assert.False(graph.FindPath(start, top, new List<NavStep>()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealCloselySpacedTreadsStayConnectedBothWays(bool merc)
    {
        var level = new CollisionDataBuilder();
        level.AddBox(new FVector(-800f, -800f, -64f), new FVector(800f, 800f, 0f));
        level.AddBox(new FVector(-200f, -300f, 0f), new FVector(200f, 0f, 240f));
        for (int step = 0; step < 10; step++)
            level.AddBox(new FVector(-200f, step * 24f, 0f), new FVector(200f, (step + 1) * 24f, 240f - step * 24f));
        NavGraph graph = NavBuilder.Build(CollisionWorld.Build(level.Build()),
            new FVector(-128f, -256f, -100f), new FVector(128f, 512f, 500f), merc ? NavProfile.Merc : NavProfile.Spy);
        int top = graph.NearestNode(new FVector(0f, -160f, 240f));
        int bottom = graph.NearestNode(new FVector(0f, 400f, 0f));
        var path = new List<NavStep>();
        Assert.True(graph.FindPath(top, bottom, path));
        Assert.True(graph.FindPath(bottom, top, path));
        Assert.All(path, step => Assert.Equal(NavEdgeKind.Walk, step.Kind));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearingAnUpperFloorPreservesTheRotatedPassageBelow(bool merc)
    {
        var level = new CollisionDataBuilder();
        level.AddBox(new FVector(-1000f, -1000f, -32f), new FVector(1000f, 1000f, 0f));
        level.AddBox(new FVector(-100f, -100f, 268f), new FVector(100f, 100f, 300f));
        level.AddBoxMeshCollider(ColliderKind.MeshPoly, null, CollisionDataBuilder.WorldFlags,
            [(new FVector(-200f, -500f, 0f), new FVector(-45f, 500f, 260f)),
             (new FVector(45f, -500f, 0f), new FVector(200f, 500f, 260f))], rotation: new FRotator(0, 8192, 0));
        NavGraph graph = NavBuilder.Build(CollisionWorld.Build(level.Build()),
            new FVector(0f, 0f, -100f), new FVector(0f, 0f, 500f), merc ? NavProfile.Merc : NavProfile.Spy);

        Assert.Equal(2, graph.NodeCount);
        Assert.True(graph.Node(graph.NearestNode(new FVector(0f, 0f, 1f))).Standing);
    }

    [Fact]
    public void OrdinaryWalkChainsPreserveShortestRoutesWithoutLongDuplicateEdges()
    {
        var level = new CollisionDataBuilder();
        level.AddBox(new FVector(-1000f, -1000f, -100f), new FVector(1000f, 1000f, 0f));
        NavGraph graph = NavBuilder.Build(CollisionWorld.Build(level.Build()),
            new FVector(-160f, -160f, -100f), new FVector(160f, 160f, 300f), NavProfile.Spy);
        int start = graph.NearestNode(new FVector(-160f, -160f, 1f));
        int end = graph.NearestNode(new FVector(160f, 160f, 1f));
        var path = new List<NavStep>();
        Assert.True(graph.FindPath(start, end, path));
        Assert.InRange(graph.CostsFrom(start)[end], 452.5f, 452.6f);
        for (int n = 0; n < graph.NodeCount; n++)
        {
            foreach (var edge in graph.EdgesOf(n))
            {
                FVector delta = graph.Node(edge.To).Floor - graph.Node(n).Floor;
                Assert.InRange(delta.Size(), 0f, 45.3f);
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void WideFootprintClearsLedgeBeforeSamplingTheFloorBelow(bool mesh, bool merc)
    {
        var level = new CollisionDataBuilder();
        level.AddBox(new FVector(-400f, -400f, -32f), new FVector(400f, 400f, 0f));
        var raisedMin = new FVector(0f, -400f, 0f);
        var raisedMax = new FVector(400f, 400f, 32f);
        if (mesh)
        {
            level.AddBoxMeshCollider(ColliderKind.MeshPoly, null, CollisionDataBuilder.WorldFlags, [(raisedMin, raisedMax)]);
        }
        else
        {
            level.AddBox(raisedMin, raisedMax);
        }

        // A second usable storey must survive clearing the upper ledge's footprint.
        level.AddBox(new FVector(-400f, -400f, -240f), new FVector(400f, 400f, -208f));
        NavGraph graph = NavBuilder.Build(CollisionWorld.Build(level.Build()),
            new FVector(-16f, 0f, -300f), new FVector(-16f, 0f, 300f), merc ? NavProfile.Merc : NavProfile.Spy);

        // The centre is beside the raised step, while the pawn's footprint rests on it. A point-only
        // clearance loop used to make several phantom nodes at 4 uu intervals inside that same step.
        Assert.Equal(2, graph.NodeCount);
        float[] floors = Enumerable.Range(0, graph.NodeCount).Select(i => graph.Node(i).Floor.Z).Order().ToArray();
        Assert.InRange(floors[0], -208.1f, -207f);
        Assert.InRange(floors[1], 32f, 33.1f);
    }
}
