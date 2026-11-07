using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenScctGame.Render;

/// <summary>Deterministic, impact-centred Voronoi cells clipped to the pane's actual triangles, including holes.</summary>
public static class GlassFracture
{
    public readonly record struct Vertex(Vector2 Point, Vector2 Uv);
    public readonly record struct Triangle(Vertex A, Vertex B, Vertex C);
    public sealed record Fragment(Vector2 Center, List<List<Vertex>> Polygons);

    public static List<Fragment> Break(IReadOnlyList<Triangle> triangles, Vector2 impact, uint seed, int count = 56)
    {
        var result = new List<Fragment>();
        if (triangles.Count == 0) return result;
        Vector2 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (Triangle t in triangles)
        foreach (Vertex v in new[] { t.A, t.B, t.C }) { min = Vector2.Min(min, v.Point); max = Vector2.Max(max, v.Point); }
        Vector2 size = max - min;
        if (size.X < 1e-5f || size.Y < 1e-5f) return result;
        impact = Vector2.Clamp(impact, min, max);
        seed = seed == 0 ? 1 : seed;
        float Next() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xffffff) / 16777216f; }
        count = Math.Clamp(count, 12, 80);
        var sites = new List<Vector2> { impact };
        for (int i = 1; i < count; i++)
        {
            // Small chips at the impact, larger irregular pieces across the rest of the pane.
            float angle = Next() * MathF.Tau, radius = MathF.Min(size.X, size.Y) * (0.035f + Next() * .18f);
            sites.Add(i < count / 3 ? Vector2.Clamp(impact + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius, min, max)
                : min + new Vector2(Next() * size.X, Next() * size.Y));
        }
        for (int i = 0; i < sites.Count; i++)
        {
            var pieces = new List<List<Vertex>>();
            Vector2 centroid = default; float area = 0;
            foreach (Triangle t in triangles)
            {
                var poly = new List<Vertex> { t.A, t.B, t.C };
                for (int j = 0; j < sites.Count && poly.Count >= 3; j++)
                {
                    if (i == j) continue;
                    Vector2 normal = sites[j] - sites[i];
                    if (normal.LengthSquared() < 1e-12f) { if (j < i) poly.Clear(); continue; }
                    float limit = (sites[j].LengthSquared() - sites[i].LengthSquared()) * .5f;
                    poly = Clip(poly, normal, limit);
                }
                if (poly.Count < 3) continue;
                float a = Area(poly);
                if (a < 1e-10f) continue;
                pieces.Add(poly);
                // Area-weighted triangle centroids, valid for each convex clipped polygon.
                for (int k = 1; k + 1 < poly.Count; k++)
                {
                    float ta = MathF.Abs(Cross(poly[k].Point - poly[0].Point, poly[k + 1].Point - poly[0].Point)) * .5f;
                    centroid += (poly[0].Point + poly[k].Point + poly[k + 1].Point) * (ta / 3); area += ta;
                }
            }
            if (area > 1e-9f) result.Add(new Fragment(centroid / area, pieces));
        }
        return result;
    }

    public static float Area(IReadOnlyList<Vertex> p)
    {
        float a = 0;
        for (int i = 1; i + 1 < p.Count; i++) a += MathF.Abs(Cross(p[i].Point - p[0].Point, p[i + 1].Point - p[0].Point)) * .5f;
        return a;
    }

    /// <summary>Cancel shared triangle edges before extruding, so a shard has no internal walls or striped layers.</summary>
    public static List<(Vertex A, Vertex B)> Boundary(Fragment fragment)
    {
        var edges = new Dictionary<((long,long) A,(long,long) B),(Vertex A,Vertex B,int Count)>();
        static (long,long) Key(Vector2 p) => ((long)Math.Round(p.X*100000),(long)Math.Round(p.Y*100000));
        foreach (var polygon in fragment.Polygons) for (int i=0;i<polygon.Count;i++)
        {
            Vertex a=polygon[i],b=polygon[(i+1)%polygon.Count]; var ak=Key(a.Point);var bk=Key(b.Point);
            if (ak==bk) continue;
            var key=ak.CompareTo(bk)<0 ? (ak,bk) : (bk,ak);
            if (edges.TryGetValue(key,out var edge)) edges[key]=(edge.A,edge.B,edge.Count+1);
            else edges[key]=(a,b,1);
        }
        var result=new List<(Vertex,Vertex)>();
        foreach (var edge in edges.Values) if ((edge.Count&1)!=0) result.Add((edge.A,edge.B));
        return result;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    private static List<Vertex> Clip(List<Vertex> p, Vector2 n, float limit)
    {
        var clipped = new List<Vertex>();
        Vertex a = p[^1]; float da = Vector2.Dot(a.Point, n) - limit;
        foreach (Vertex b in p)
        {
            float db = Vector2.Dot(b.Point, n) - limit;
            if ((da <= 0) != (db <= 0))
            {
                float t = da / (da - db);
                clipped.Add(new Vertex(Vector2.Lerp(a.Point, b.Point, t), Vector2.Lerp(a.Uv, b.Uv, t)));
            }
            if (db <= 0) clipped.Add(b);
            (a, da) = (b, db);
        }
        return clipped;
    }
}
