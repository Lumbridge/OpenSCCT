using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenScctGame.Render.Post;

public readonly record struct FogZone(float Start, float End, Vector3 Color, bool Enabled)
{
    public static float Rate(float start, float end) => end > start ? 1f / (end - start) : 1e6f;
    public float Coverage(float distance) => Enabled ? Math.Max(distance, Start) * Rate(Start, End) : 0f;
}

public sealed record FogPortal(Vector4 Plane, int Back, int Front, Vector3[] Vertices)
{
    public Vector4[] Edges { get; } = BuildEdges(Plane, Vertices);
    public Vector3[] CandidateVertices { get; } = BuildCandidateVertices(Plane, Vertices);

    private static Vector4[] BuildEdges(Vector4 plane, Vector3[] vertices)
    {
        var result = new List<Vector4>();
        Vector3 center = Vector3.Zero;
        foreach (Vector3 vertex in vertices) center += vertex;
        center /= Math.Max(1, vertices.Length);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 a = vertices[i], b = vertices[(i + 1) % vertices.Length];
            Vector3 normal = Vector3.Cross(b - a, new Vector3(plane.X, plane.Y, plane.Z));
            if (normal.LengthSquared() < .00000001f) continue;
            normal = Vector3.Normalize(normal);
            if (Vector3.Dot(normal, center - a) < 0f) normal = -normal;
            result.Add(new(normal, Vector3.Dot(normal, a) - .25f));
        }
        return result.ToArray();
    }

    // Tile selection includes the widened polygon, including long mitres at acute corners.
    private static Vector3[] BuildCandidateVertices(Vector4 plane, Vector3[] vertices)
    {
        Vector4[] edges = BuildEdges(plane, vertices);
        var result = new List<Vector3>(vertices);
        var normal = new Vector3(plane.X, plane.Y, plane.Z);
        for (int i = 0; i < edges.Length; i++)
        {
            Vector4 a = edges[i], b = edges[(i + 1) % edges.Length];
            Vector3 na = new(a.X, a.Y, a.Z), nb = new(b.X, b.Y, b.Z);
            Vector3 cross = Vector3.Cross(na, nb);
            float divisor = Vector3.Dot(normal, cross);
            if (MathF.Abs(divisor) < 1e-9f) continue;
            Vector3 point = (a.W * Vector3.Cross(nb, normal) + b.W * Vector3.Cross(normal, na) + plane.W * cross) / divisor;
            if (float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z)) result.Add(point);
        }
        return result.ToArray();
    }

    public bool Crossing(Vector3 origin, Vector3 direction, float distance, out float at, out int from, out int to)
    {
        float along = Vector3.Dot(new(Plane.X, Plane.Y, Plane.Z), direction);
        from = along > 0f ? Back : Front;
        to = along > 0f ? Front : Back;
        at = 0f;
        if (MathF.Abs(along) < 1e-7f) return false;
        at = (Plane.W - Vector3.Dot(new(Plane.X, Plane.Y, Plane.Z), origin)) / along;
        if (at <= 0f || at >= distance) return false;
        Vector3 hit = origin + direction * at;
        foreach (Vector4 edge in Edges)
            if (Vector3.Dot(new(edge.X, edge.Y, edge.Z), hit) < edge.W) return false;
        return true;
    }
}

/// <summary>Independent reference evaluator for spec/native/enhanced-zone-fog.md.</summary>
public static class ZoneFogRules
{
    public static Vector4 Evaluate(FogZone[] zones, IReadOnlyList<FogPortal> portals, int cameraZone, Vector3 origin, Vector3 target)
    {
        Vector3 ray = target - origin;
        float distance = ray.Length();
        if (distance <= 0f) return new(0f, 0f, 0f, 1f);
        Vector3 direction = ray / distance;
        float coverage = 0f, lastDistance = 0f;
        Vector3 color = Vector3.Zero;
        int lastZone = cameraZone;
        var passed = new List<(int From, int To, float At)>();
        void Add(int zone, float at, float sign)
        {
            if (zone < 0 || zone >= zones.Length) return;
            float part = sign * zones[zone].Coverage(at);
            coverage += part;
            color += part * zones[zone].Color;
        }
        Add(cameraZone, 0f, -1f);
        foreach (FogPortal portal in portals)
        {
            if (!portal.Crossing(origin, direction, distance, out float at, out int from, out int to)) continue;
            if (passed.Exists(p => p.From == from && p.To == to && MathF.Abs(p.At - at) < .001f * Math.Max(at, 1f))) continue;
            passed.Add((from, to, at));
            Add(from, at, 1f);
            Add(to, at, -1f);
            if (at > lastDistance) { lastDistance = at; lastZone = to; }
        }
        Add(lastZone, distance, 1f);
        float opacity = Math.Clamp(coverage, 0f, 1f);
        return new(coverage > 0f ? color * (opacity / coverage) : Vector3.Zero, 1f - opacity);
    }
}
