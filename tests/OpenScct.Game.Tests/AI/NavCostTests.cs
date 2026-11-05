using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Game.AI;
using Xunit;

namespace OpenScct.Game.Tests.AI;

public sealed class NavCostTests(ITestOutputHelper output)
{
    [Fact]
    public void ReusedCostsRecomputeDirectedRoutesAndChangedBlockers()
    {
        NavGraph graph = Routes();
        var costs = new float[graph.NodeCount];
        float inf = float.PositiveInfinity;
        var blockedLinks = new HashSet<int>();
        var blockedEdges = new HashSet<int>();

        graph.CostsFrom(0, costs.AsSpan(), badLinks: blockedLinks, badEdges: blockedEdges);
        Assert.Equal([0f, 4f, 2f, 5f, 8f, inf], costs);
        blockedLinks.Add(0);
        graph.CostsFrom(0, costs.AsSpan(), badLinks: blockedLinks, badEdges: blockedEdges);
        Assert.Equal([0f, 8f, 2f, 9f, 12f, inf], costs);
        blockedLinks.Clear();
        blockedEdges.Add(2); // The one-way link from node 1 to node 3.
        graph.CostsFrom(0, costs.AsSpan(), badLinks: blockedLinks, badEdges: blockedEdges);
        Assert.Equal([0f, 4f, 2f, 12f, 15f, inf], costs);
        blockedEdges.Clear();
        graph.CostsFrom(0, costs.AsSpan(), maxCost: 5f);
        Assert.Equal([0f, 4f, 2f, 5f, inf, inf], costs);
        graph.CostsFrom(4, costs.AsSpan());
        Assert.Equal([inf, 3f, 1f, 4f, 0f, inf], costs);
        graph.CostsFrom(-1, costs.AsSpan());
        Assert.All(costs, cost => Assert.Equal(inf, cost));
        graph.CostsFrom(5, costs.AsSpan());
        Assert.Equal([inf, inf, inf, inf, inf, 0f], costs);
    }

    [Fact]
    public void SearchesKeepCallerOwnedResultsAndDoNotDisturbAnActivePathSearch()
    {
        NavGraph graph = Routes();
        float[] first = graph.CostsFrom(0);
        float[] second = graph.CostsFrom(4);
        float[] savedFirst = (float[])first.Clone();
        float[] savedSecond = (float[])second.Clone();
        var scratch = new float[graph.NodeCount];
        var path = new List<NavStep>();
        // Dijkstra has its own queue: a path penalty may query another reachability field.
        Assert.True(graph.FindPath(0, 4, path, penalty: node =>
        {
            graph.CostsFrom(node, scratch.AsSpan());
            return 0f;
        }));
        Assert.Equal(8f, graph.LastCost);
        Assert.Equal(new[] { 2, 1, 3, 4 }, path.ConvertAll(step => step.Node));
        Assert.Equal(savedFirst, first);
        Assert.Equal(savedSecond, second);
    }

    [Fact]
    public void ReusedCostStorageMustMatchTheGraph()
    {
        NavGraph graph = Routes();
        Assert.Throws<ArgumentException>(() => graph.CostsFrom(0, new float[graph.NodeCount - 1].AsSpan()));
        Assert.Throws<ArgumentException>(() => graph.CostsFrom(0, new float[graph.NodeCount + 1].AsSpan()));
        var empty = new NavGraph(NavProfile.Spy, default, [], [0], [], [], [], [], []);
        empty.CostsFrom(-1, Span<float>.Empty);
        Assert.Empty(empty.CostsFrom(-1));
    }

    [Fact]
    public void WarmReachabilityAcross6400NodesDoesNotAllocate()
    {
        const int side = 80;
        NavGraph graph = Grid(side);
        var costs = new float[graph.NodeCount];
        for (int i = 0; i < 8; i++) graph.CostsFrom(i & 1, costs.AsSpan());
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++) graph.CostsFrom(i & 1, costs.AsSpan());
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(2f * side - 3f, costs[^1]);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++) graph.CostsFrom(i & 1);
        long ownedAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"32 searches across {graph.NodeCount} nodes: reused {allocated} bytes; owned arrays {ownedAllocation} bytes.");
    }

    private static NavGraph Routes() => new(NavProfile.Spy, default,
        [new(default, true), new(default, true), new(default, true), new(default, true), new(default, true), new(default, true)],
        [0, 2, 3, 5, 6, 7, 7], [1, 2, 3, 1, 3, 4, 2],
        [NavEdgeKind.Walk, NavEdgeKind.Walk, NavEdgeKind.LadderUp, NavEdgeKind.LedgeClimb,
            NavEdgeKind.Walk, NavEdgeKind.Walk, NavEdgeKind.Drop],
        [8f, 2f, 1f, 2f, 10f, 3f, 1f], [-1, -1, 1, 0, -1, -1, -1], []);

    private static NavGraph Grid(int side)
    {
        var nodes = new NavNode[side * side];
        var starts = new int[nodes.Length + 1];
        var to = new List<int>();
        for (int n = 0; n < nodes.Length; n++)
        {
            int x = n % side, y = n / side;
            nodes[n] = new NavNode(new FVector(x, y, 0f), true);
            starts[n] = to.Count;
            if (x > 0) to.Add(n - 1);
            if (x + 1 < side) to.Add(n + 1);
            if (y > 0) to.Add(n - side);
            if (y + 1 < side) to.Add(n + side);
        }
        starts[^1] = to.Count;
        var costs = new float[to.Count];
        Array.Fill(costs, 1f);
        var links = new int[to.Count];
        Array.Fill(links, -1);
        return new NavGraph(NavProfile.Spy, default, nodes, starts, to.ToArray(), new NavEdgeKind[to.Count], costs, links, []);
    }
}
