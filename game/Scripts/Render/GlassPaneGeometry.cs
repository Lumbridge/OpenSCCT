using System;
using System.Collections.Generic;
using Godot;
using N2 = System.Numerics.Vector2;

namespace OpenScctGame.Render;

/// <summary>Reads the actual planar pane, rather than filling the mesh bounds. Keeps its original texture coordinates.</summary>
public sealed class GlassPaneGeometry
{
    public Vector3 Origin { get; private init; }
    public Vector3 U { get; private init; }
    public Vector3 V { get; private init; }
    public Vector3 Normal { get; private init; }
    public float Thickness { get; private init; }
    public int DepthSign { get; private init; }
    public N2 Center { get; private set; }
    public Material? Material { get; private init; }
    public List<GlassFracture.Triangle> Triangles { get; } = [];
    public Vector3 Point(N2 p) => Origin + U * p.X + V * p.Y;
    public N2 Project(Vector3 p) => new((p - Origin).Dot(U), (p - Origin).Dot(V));

    public static GlassPaneGeometry? Read(Mesh? mesh)
    {
        if (mesh is not ArrayMesh array) return null;
        var triangles = new List<(Vector3 A, Vector3 B, Vector3 C, Vector2 Ua, Vector2 Ub, Vector2 Uc, Material? Mat)>();
        Vector3 normal = default, origin = default; float largest = 0;
        for (int s = 0; s < array.GetSurfaceCount(); s++)
        {
            var a = array.SurfaceGetArrays(s);
            Vector3[] p = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Vector2[] uv = a[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            int[] indices = a[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (indices.Length == 0) { indices = new int[p.Length]; for (int i = 0; i < p.Length; i++) indices[i] = i; }
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int x = indices[i], y = indices[i + 1], z = indices[i + 2];
                Vector3 n = (p[y] - p[x]).Cross(p[z] - p[x]); float area = n.Length();
                if (area < 1e-10f) continue;
                triangles.Add((p[x], p[y], p[z], uv.Length == p.Length ? uv[x] : default,
                    uv.Length == p.Length ? uv[y] : default, uv.Length == p.Length ? uv[z] : default, array.SurfaceGetMaterial(s)));
                if (area > largest) { largest = area; normal = n / area; origin = p[x]; }
            }
        }
        if (largest == 0) return null;
        Vector3 axis = MathF.Abs(normal.Y) < .9f ? Vector3.Up : Vector3.Right;
        Vector3 u = normal.Cross(axis).Normalized(), v = normal.Cross(u).Normalized();
        float min = 0, max = 0;
        foreach (var t in triangles)
        foreach (Vector3 p in new[] { t.A, t.B, t.C }) { float d = (p - origin).Dot(normal); min = MathF.Min(min, d); max = MathF.Max(max, d); }
        // A thick or nonplanar prop is not a pane: keep its authored break effect.
        if (max - min > MathF.Sqrt(largest) * .35f) return null;
        var pane = new GlassPaneGeometry { Origin = origin, U = u, V = v, Normal = normal,
            Thickness = Math.Clamp(max - min, .002f, .15f), DepthSign = max > -min ? 1 : -1,
            Material = triangles.Find(t => (t.A - origin).LengthSquared() < 1e-8f).Mat };
        foreach (var t in triangles)
        {
            if (MathF.Abs((t.A - origin).Dot(normal)) > .0002f || MathF.Abs((t.B - origin).Dot(normal)) > .0002f
                || MathF.Abs((t.C - origin).Dot(normal)) > .0002f) continue;
            pane.Triangles.Add(new(new(pane.Project(t.A), new N2(t.Ua.X, t.Ua.Y)),
                new(pane.Project(t.B), new N2(t.Ub.X, t.Ub.Y)), new(pane.Project(t.C), new N2(t.Uc.X, t.Uc.Y))));
        }
        if (pane.Triangles.Count == 0) return null;
        N2 lo = new(float.MaxValue), hi = new(float.MinValue);
        foreach (var t in pane.Triangles) foreach (var vertex in new[] { t.A, t.B, t.C })
        { lo = N2.Min(lo, vertex.Point); hi = N2.Max(hi, vertex.Point); }
        pane.Center = (lo + hi) * .5f;
        return pane;
    }

    public ArrayMesh MeshFor(GlassFracture.Fragment fragment, Transform3D world, Material? material)
    {
        Vector3 center = world * Point(fragment.Center);
        var tool = new SurfaceTool(); tool.Begin(Mesh.PrimitiveType.Triangles); tool.SetMaterial(material);
        void Triangle(Vector3 a, Vector3 b, Vector3 c, N2 ua, N2 ub, N2 uc)
        {
            Vector3 n = (b - a).Cross(c - a).Normalized();
            foreach (var pair in new[] { (a, ua), (b, ub), (c, uc) })
            { tool.SetNormal(n); tool.SetUV(new Vector2(pair.Item2.X, pair.Item2.Y)); tool.AddVertex(pair.Item1 - center); }
        }
        Vector3 depth = world.Basis * Normal * (Thickness * -DepthSign);
        foreach (var poly in fragment.Polygons)
        {
            Vector3 At(int i) => world * Point(poly[i].Point);
            for (int i = 1; i + 1 < poly.Count; i++)
            {
                Triangle(At(0), At(i), At(i + 1), poly[0].Uv, poly[i].Uv, poly[i + 1].Uv);
                Triangle(At(i + 1) - depth, At(i) - depth, At(0) - depth, poly[i + 1].Uv, poly[i].Uv, poly[0].Uv);
            }
        }
        foreach (var edge in GlassFracture.Boundary(fragment))
        {
            Vector3 a=world*Point(edge.A.Point),b=world*Point(edge.B.Point);
            Triangle(a,b,b-depth,edge.A.Uv,edge.B.Uv,edge.B.Uv);
            Triangle(a,b-depth,a-depth,edge.A.Uv,edge.B.Uv,edge.A.Uv);
        }
        return tool.Commit();
    }
}
