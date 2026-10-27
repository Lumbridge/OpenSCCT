using System;
using System.Collections.Generic;
using CsCheck;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Traversal;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

/// <summary>The BVH walks must give exactly what testing every primitive gives.</summary>
public sealed class BvhPropertyTests
{
    private static float F(Random r, float lo, float hi) => lo + (float)r.NextDouble() * (hi - lo);

    private static FVector P(Random r, float half) => V(F(r, -half, half), F(r, -half, half), F(r, -half, half));

    /// <summary>A random soup of triangles, axis-aligned boxes and turned box hulls (static and dynamic).</summary>
    private static CollisionWorld RandomWorld(int seed, out Random r)
    {
        r = new Random(seed);
        var b = new CollisionDataBuilder();
        int tris = 150 + r.Next(150);
        for (int i = 0; i < tris; i++)
        {
            FVector c = P(r, 1000f);
            b.AddTriangle(c + P(r, 120f), c + P(r, 120f), c + P(r, 120f));
        }

        for (int i = 0; i < 20; i++)
        {
            FVector c = P(r, 1000f);
            FVector h = V(F(r, 5f, 100f), F(r, 5f, 100f), F(r, 5f, 100f));
            b.AddBox(c - h, c + h);
        }

        for (int i = 0; i < 25; i++)
        {
            FVector h = V(F(r, 5f, 80f), F(r, 5f, 80f), F(r, 5f, 80f));
            var rot = new FRotator(r.Next(65536), r.Next(65536), r.Next(65536));
            ColliderKind kind = i % 3 == 0 ? ColliderKind.Gpo : ColliderKind.MeshHull;
            b.AddBoxHullCollider(kind, null, CollisionDataBuilder.WorldFlags, -h, h, P(r, 1000f), rot, writeCorners: i % 2 == 0);
        }

        return CollisionWorld.Build(b.Build());
    }

    private static void Same(in FCheckResult a, in FCheckResult b, bool hitA, bool hitB)
    {
        Assert.Equal(hitA, hitB);
        Assert.Equal(a.Time, b.Time);
        Assert.Equal(a.Location, b.Location);
        Assert.Equal(a.Normal, b.Normal);
        Assert.Equal(a.Collider, b.Collider);
        Assert.Equal(a.Item, b.Item);
        Assert.Equal(a.bStartPenetrating, b.bStartPenetrating);
    }

    [Fact]
    public void SweepsMatchBruteForce()
    {
        Gen.Int[0, int.MaxValue].Sample(seed =>
        {
            CollisionWorld w = RandomWorld(seed, out Random r);
            for (int q = 0; q < 200; q++)
            {
                FVector start = P(r, 1200f);
                FVector end = q % 4 == 0 ? start + P(r, 60f) : P(r, 1200f);
                FVector extent = q % 3 == 0 ? default : V(F(r, 1f, 60f), F(r, 1f, 60f), F(r, 1f, 90f));
                bool hb = w.Trace(out FCheckResult bvh, null, null, -1, end, start, extent, ETraceFlags.TRACE_AllColliding, bruteForce: false);
                bool hf = w.Trace(out FCheckResult brute, null, null, -1, end, start, extent, ETraceFlags.TRACE_AllColliding, bruteForce: true);
                Same(bvh, brute, hb, hf);
            }
        }, iter: 40);
    }

    [Fact]
    public void OverlapsMatchBruteForce()
    {
        Gen.Int[0, int.MaxValue].Sample(seed =>
        {
            CollisionWorld w = RandomWorld(seed, out Random r);
            for (int q = 0; q < 200; q++)
            {
                FVector p = P(r, 1100f);
                FVector extent = q % 5 == 0 ? default : V(F(r, 1f, 80f), F(r, 1f, 80f), F(r, 1f, 100f));
                bool hb = w.Overlap(out FCheckResult bvh, null, null, -1, p, extent, ETraceFlags.TRACE_AllColliding, bruteForce: false);
                bool hf = w.Overlap(out FCheckResult brute, null, null, -1, p, extent, ETraceFlags.TRACE_AllColliding, bruteForce: true);
                Same(bvh, brute, hb, hf);
            }
        }, iter: 40);
    }

    [Fact]
    public void SweepHitsAgreeWithOverlaps()
    {
        // Wherever a box sweep from outside stops, the box at the stop point overlaps nothing.
        Gen.Int[0, int.MaxValue].Sample(seed =>
        {
            CollisionWorld w = RandomWorld(seed, out Random r);
            for (int q = 0; q < 200; q++)
            {
                FVector start = P(r, 1200f);
                FVector end = P(r, 1200f);
                FVector extent = V(F(r, 1f, 60f), F(r, 1f, 60f), F(r, 1f, 90f));
                if (w.PointCheck(out _, null, start, extent, ETraceFlags.TRACE_AllColliding))
                {
                    continue;
                }

                if (w.LineCheck(out FCheckResult hit, null, end, start, extent, ETraceFlags.TRACE_AllColliding))
                {
                    Assert.False(w.PointCheck(out FCheckResult inside, null, hit.Location, extent * 0.999f, ETraceFlags.TRACE_AllColliding),
                        $"stopped inside collider {inside.Collider} at {hit.Location}");
                }
            }
        }, iter: 25);
    }

    [Fact]
    public void GEQueriesMatchBruteForce()
    {
        Gen.Int[0, int.MaxValue].Sample(seed =>
        {
            var r = new Random(seed);
            var ges = new List<GERecord>();
            GEKind[] kinds = [GEKind.POLE, GEKind.FENCE, GEKind.HOH, GEKind.LG, GEKind.LADDER, GEKind.PIPE, GEKind.ZL];
            for (int i = 0; i < 250; i++)
            {
                GEKind kind = kinds[r.Next(kinds.Length)];
                FVector a = P(r, 2000f);
                var points = new List<FVector> { a, a + P(r, 300f) };
                if (GERecord.PointCount(kind) == 3)
                {
                    points.Add(a + V(10f, 0f, 0f));
                }

                ges.Add(new GERecord(i, $"GE{i}", kind, points, (byte)(r.Next(4) == 0 ? 4 : 0), GEPlatform.Common, GESource.Synthetic()));
            }

            var index = new GEIndex(ges);
            var fast = new List<GEHit>();
            var slow = new List<GEHit>();
            for (int q = 0; q < 100; q++)
            {
                FVector c = P(r, 2100f);
                float radius = F(r, 10f, 400f);
                int mask = 1 + r.Next(127);
                byte activation = (byte)(r.Next(2) * 4);
                index.QuerySphere(c, radius, mask, activation, fast);
                index.QuerySphereBruteForce(c, radius, mask, activation, slow);
                Assert.Equal(slow, fast);
            }
        }, iter: 30);
    }
}
