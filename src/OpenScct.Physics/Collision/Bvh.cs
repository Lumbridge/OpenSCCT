using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScct.Physics.Collision;

/// <summary>
/// A bounding volume hierarchy node. A leaf (<see cref="Count"/> &gt; 0) covers items [Start, Start + Count) of
/// the owner's item array; an inner node's children are nodes <see cref="Start"/> and <see cref="Start"/> + 1,
/// which always come after their parent, so a reverse walk over the array refits bottom up.
/// </summary>
internal struct BvhNode
{
    public FVector Min;
    public FVector Max;
    public int Start;
    public int Count;

    public readonly bool IsLeaf => Count > 0;
}

/// <summary>
/// Builds BVHs with a binned surface-area heuristic. The build is deterministic: it depends only on the input
/// bounds and their order.
/// </summary>
internal static class Bvh
{
    public const int LeafSize = 4;
    private const int MaxDepth = 48;
    private const int BinCount = 12;

    /// <summary>
    /// Builds a hierarchy over <paramref name="count"/> items with the given bounds. <paramref name="order"/>
    /// receives the item permutation: leaf ranges index into it. An empty input gives one empty leaf.
    /// </summary>
    public static BvhNode[] Build(ReadOnlySpan<FVector> mins, ReadOnlySpan<FVector> maxs, out int[] order)
    {
        int count = mins.Length;
        order = new int[count];
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }

        if (count == 0)
        {
            return [new BvhNode { Min = default, Max = default, Start = 0, Count = 0 }];
        }

        var centroids = new FVector[count];
        for (int i = 0; i < count; i++)
        {
            centroids[i] = (mins[i] + maxs[i]) * 0.5f;
        }

        var nodes = new List<BvhNode>(Math.Max(1, count / 2)) { default };
        var builder = new Builder(mins.ToArray(), maxs.ToArray(), centroids, order, nodes);
        builder.BuildNode(0, 0, count, 0);
        return nodes.ToArray();
    }

    private sealed class Builder
    {
        private readonly FVector[] _mins;
        private readonly FVector[] _maxs;
        private readonly FVector[] _centroids;
        private readonly int[] _order;
        private readonly List<BvhNode> _nodes;
        private readonly int[] _binCount = new int[BinCount];
        private readonly FVector[] _binMin = new FVector[BinCount];
        private readonly FVector[] _binMax = new FVector[BinCount];

        public Builder(FVector[] mins, FVector[] maxs, FVector[] centroids, int[] order, List<BvhNode> nodes)
        {
            _mins = mins;
            _maxs = maxs;
            _centroids = centroids;
            _order = order;
            _nodes = nodes;
        }

        public void BuildNode(int nodeIndex, int start, int end, int depth)
        {
            FVector min = _mins[_order[start]];
            FVector max = _maxs[_order[start]];
            FVector cmin = _centroids[_order[start]];
            FVector cmax = cmin;
            for (int i = start + 1; i < end; i++)
            {
                int item = _order[i];
                min = CollisionMath.MinV(min, _mins[item]);
                max = CollisionMath.MaxV(max, _maxs[item]);
                cmin = CollisionMath.MinV(cmin, _centroids[item]);
                cmax = CollisionMath.MaxV(cmax, _centroids[item]);
            }

            int n = end - start;
            if (n <= LeafSize)
            {
                _nodes[nodeIndex] = new BvhNode { Min = min, Max = max, Start = start, Count = n };
                return;
            }

            FVector extent = cmax - cmin;
            int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
            float lo = Component(cmin, axis);
            float span = Component(extent, axis);
            int mid = span > 0f && depth < MaxDepth ? SahSplit(start, end, axis, lo, span) : -1;
            if (mid <= start || mid >= end)
            {
                mid = MedianSplit(start, end, axis);
            }

            int left = _nodes.Count;
            _nodes.Add(default);
            _nodes.Add(default);
            _nodes[nodeIndex] = new BvhNode { Min = min, Max = max, Start = left, Count = 0 };
            BuildNode(left, start, mid, depth + 1);
            BuildNode(left + 1, mid, end, depth + 1);
        }

        private int SahSplit(int start, int end, int axis, float lo, float span)
        {
            Array.Clear(_binCount);
            float scale = BinCount / span;
            for (int i = start; i < end; i++)
            {
                int item = _order[i];
                int b = Bin(Component(_centroids[item], axis), lo, scale);
                if (_binCount[b]++ == 0)
                {
                    _binMin[b] = _mins[item];
                    _binMax[b] = _maxs[item];
                }
                else
                {
                    _binMin[b] = CollisionMath.MinV(_binMin[b], _mins[item]);
                    _binMax[b] = CollisionMath.MaxV(_binMax[b], _maxs[item]);
                }
            }

            // Sweep the split planes: cost = leftArea * leftCount + rightArea * rightCount.
            Span<float> rightCost = stackalloc float[BinCount];
            int rightCount = 0;
            FVector rmin = default;
            FVector rmax = default;
            for (int b = BinCount - 1; b > 0; b--)
            {
                Accumulate(b, ref rightCount, ref rmin, ref rmax);
                rightCost[b] = rightCount == 0 ? 0f : Area(rmin, rmax) * rightCount;
            }

            int leftCount = 0;
            FVector lmin = default;
            FVector lmax = default;
            float bestCost = float.PositiveInfinity;
            int bestBin = -1;
            int total = end - start;
            for (int b = 0; b < BinCount - 1; b++)
            {
                Accumulate(b, ref leftCount, ref lmin, ref lmax);
                if (leftCount == 0 || leftCount == total)
                {
                    continue;
                }

                float cost = Area(lmin, lmax) * leftCount + rightCost[b + 1];
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestBin = b;
                }
            }

            if (bestBin < 0)
            {
                return -1;
            }

            // Partition in place: items in bins 0..bestBin go left.
            int i0 = start;
            int i1 = end - 1;
            while (i0 <= i1)
            {
                if (Bin(Component(_centroids[_order[i0]], axis), lo, scale) <= bestBin)
                {
                    i0++;
                }
                else
                {
                    (_order[i0], _order[i1]) = (_order[i1], _order[i0]);
                    i1--;
                }
            }

            return i0;
        }

        private void Accumulate(int b, ref int count, ref FVector min, ref FVector max)
        {
            if (_binCount[b] == 0)
            {
                return;
            }

            if (count == 0)
            {
                min = _binMin[b];
                max = _binMax[b];
            }
            else
            {
                min = CollisionMath.MinV(min, _binMin[b]);
                max = CollisionMath.MaxV(max, _binMax[b]);
            }

            count += _binCount[b];
        }

        private int MedianSplit(int start, int end, int axis)
        {
            FVector[] centroids = _centroids;
            Array.Sort(_order, start, end - start, Comparer<int>.Create((x, y) =>
            {
                int c = Component(centroids[x], axis).CompareTo(Component(centroids[y], axis));
                return c != 0 ? c : x.CompareTo(y);
            }));
            return start + (end - start) / 2;
        }

        private static int Bin(float value, float lo, float scale)
        {
            int b = (int)((value - lo) * scale);
            return b < 0 ? 0 : b >= BinCount ? BinCount - 1 : b;
        }

        private static float Area(FVector min, FVector max)
        {
            FVector d = max - min;
            return d.X * d.Y + d.Y * d.Z + d.Z * d.X;
        }
    }

    private static float Component(FVector v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
}
