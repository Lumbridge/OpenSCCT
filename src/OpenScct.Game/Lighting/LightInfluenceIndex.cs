using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScct.Game.Lighting;

/// <summary>A conservative influence used only to reject lights before the original scoring rules run.</summary>
public readonly record struct LightInfluence(FVector Center, float Radius, bool Unbounded = false);

/// <summary>
/// Immutable BVH of map-light influence boxes. Queries return source indices; the caller restores source order
/// before scoring, so the existing light ranking and tie handling are unchanged. Moved and spawned lights can be
/// evaluated separately without rebuilding the index. Nothing here chooses lights or changes their brightness.
/// </summary>
public sealed class LightInfluenceIndex
{
    // Large/nonfinite inputs retain the full scan: float distance/reach squares can overflow in the original score.
    private const float SafeMagnitude = 1e18f;
    private const int LeafSize = 8;
    private readonly Entry[] _entries;
    private readonly Node[] _nodes;
    private readonly int[] _unbounded;
    private readonly int _count;
    private int _used;

    public LightInfluenceIndex(IReadOnlyList<LightInfluence> lights)
    {
        ArgumentNullException.ThrowIfNull(lights);
        _count = lights.Count;
        var bounded = new List<Entry>(lights.Count);
        var unbounded = new List<int>();
        for (int i = 0; i < lights.Count; i++)
        {
            LightInfluence light = lights[i];
            if (light.Unbounded || !Safe(light.Center) || !Safe(light.Radius))
            {
                unbounded.Add(i);
                continue;
            }

            bounded.Add(new Entry(i, light.Center, Box.Around(light.Center, Math.Abs((double)light.Radius))));
        }

        _entries = bounded.ToArray();
        _unbounded = unbounded.ToArray();
        _nodes = new Node[Math.Max(1, _entries.Length * 2)];
        if (_entries.Length > 0) Build(0, _entries.Length);
    }

    /// <summary>
    /// Replaces <paramref name="into"/> with a conservative set of candidate indices (not sorted). Expands both
    /// boxes beyond float rounding error; all accepted candidates still go through the unchanged exact score.
    /// </summary>
    public void Query(FVector center, float radius, List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        if (!Safe(center) || !Safe(radius))
        {
            for (int i = 0; i < _count; i++) into.Add(i);
            return;
        }

        into.AddRange(_unbounded);
        if (_entries.Length > 0) Query(0, Box.Around(center, Math.Abs((double)radius)), into);
    }

    private static bool Safe(float value) => float.IsFinite(value) && MathF.Abs(value) < SafeMagnitude;

    private static bool Safe(FVector value) => Safe(value.X) && Safe(value.Y) && Safe(value.Z);

    private int Build(int start, int count)
    {
        int index = _used++;
        Box box = _entries[start].Bounds;
        for (int i = start + 1; i < start + count; i++) box = box.Merge(_entries[i].Bounds);
        if (count <= LeafSize)
        {
            _nodes[index] = new Node(box, start, count, 0, 0);
            return index;
        }

        double x = box.MaxX - box.MinX, y = box.MaxY - box.MinY, z = box.MaxZ - box.MinZ;
        int axis = x >= y && x >= z ? 0 : y >= z ? 1 : 2;
        Array.Sort(_entries, start, count, axis switch { 0 => ByX, 1 => ByY, _ => ByZ });
        int half = count / 2;
        int left = Build(start, half), right = Build(start + half, count - half);
        _nodes[index] = new Node(box, 0, 0, left, right);
        return index;
    }

    private void Query(int index, Box query, List<int> into)
    {
        Node node = _nodes[index];
        if (!node.Bounds.Intersects(query)) return;
        if (node.Count > 0)
        {
            for (int i = node.Start; i < node.Start + node.Count; i++)
                if (_entries[i].Bounds.Intersects(query)) into.Add(_entries[i].Index);
            return;
        }

        Query(node.Left, query, into);
        Query(node.Right, query, into);
    }

    private static readonly IComparer<Entry> ByX = Comparer<Entry>.Create((a, b) => a.Center.X.CompareTo(b.Center.X));
    private static readonly IComparer<Entry> ByY = Comparer<Entry>.Create((a, b) => a.Center.Y.CompareTo(b.Center.Y));
    private static readonly IComparer<Entry> ByZ = Comparer<Entry>.Create((a, b) => a.Center.Z.CompareTo(b.Center.Z));

    private readonly record struct Entry(int Index, FVector Center, Box Bounds);
    private readonly record struct Node(Box Bounds, int Start, int Count, int Left, int Right);

    private readonly record struct Box(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
    {
        public static Box Around(FVector center, double radius)
        {
            // This also covers subtraction/addition/squared-distance rounding at the influence boundary. The
            // margin only admits extra candidates; it never replaces the original radius, cone or score tests.
            double magnitude = Math.Max(Math.Abs((double)center.X), Math.Max(Math.Abs((double)center.Y), Math.Abs((double)center.Z)));
            double reach = radius + 1.0 + (magnitude + radius) * 0.00001;
            return new Box(center.X - reach, center.Y - reach, center.Z - reach,
                center.X + reach, center.Y + reach, center.Z + reach);
        }

        public Box Merge(Box other) => new(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Min(MinZ, other.MinZ),
            Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY), Math.Max(MaxZ, other.MaxZ));

        public bool Intersects(Box other) => MinX <= other.MaxX && MaxX >= other.MinX
            && MinY <= other.MaxY && MaxY >= other.MinY && MinZ <= other.MaxZ && MaxZ >= other.MinZ;
    }
}
