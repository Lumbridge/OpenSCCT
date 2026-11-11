using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render.Rd;

/// <summary>
/// The zones a camera can see through the map's portals (the world model's zone portals, LightingDoc.FogPortals):
/// from the camera's zone, a neighbour is reached when the portal between them shows inside the part of the screen
/// the walk has left, as the original's renderer walks its zones. Conservative: a portal that crosses the near plane
/// keeps the whole of that part, and a camera whose zone is not known sees everything.
/// </summary>
internal sealed class RdZoneVisibility
{
    private const int MaxZones = 64;

    private readonly record struct Portal(Vector3[] Vertices, int Back, int Front);

    private readonly Portal[] _portals;
    private readonly List<int>[] _byZone = new List<int>[MaxZones];
    private readonly Rect2[] _seen = new Rect2[MaxZones];
    private readonly bool[] _reached = new bool[MaxZones];
    private readonly Stack<(int Zone, Rect2 Rect)> _pending = new();

    /// <summary>The visible zones of the last <see cref="Walk"/>, one bit each.</summary>
    public ulong Visible { get; private set; }

    public RdZoneVisibility(LightingDoc lighting)
    {
        var portals = new List<Portal>();
        for (int z = 0; z < MaxZones; z++) _byZone[z] = [];
        foreach (Post.FogPortal portal in lighting.FogPortals)
        {
            if (portal.Back == portal.Front || portal.Back is < 0 or >= MaxZones || portal.Front is < 0 or >= MaxZones || portal.Vertices.Length < 3) continue;
            var vertices = new Vector3[portal.Vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                System.Numerics.Vector3 v = portal.Vertices[i];
                vertices[i] = UeSpace.ToGodot(new FVector(v.X, v.Y, v.Z));
            }

            _byZone[portal.Back].Add(portals.Count);
            _byZone[portal.Front].Add(portals.Count);
            portals.Add(new Portal(vertices, portal.Back, portal.Front));
        }

        _portals = [.. portals];
    }

    public bool HasPortals => _portals.Length > 0;

    /// <summary>
    /// Walks the portals from the camera's zone and from the zones just around the camera (<paramref name="near"/>:
    /// a camera pressed against a wall can stand a hand's width inside the next room's zone). False when nothing can
    /// be culled: no portals, or a camera zone that is not one of the map's numbered zones (zone 0 is the space
    /// outside every zone).
    /// </summary>
    public bool Walk(int zone, ReadOnlySpan<int> near, in Projection clip)
    {
        Visible = ~0UL;
        if (_portals.Length == 0 || zone is <= 0 or >= MaxZones || _byZone[zone].Count == 0) return false;
        Array.Clear(_reached);
        ulong visible = 0;
        var screen = new Rect2(-1f, -1f, 2f, 2f);
        _pending.Clear();
        _pending.Push((zone, screen));
        foreach (int other in near)
        {
            if (other is > 0 and < MaxZones && other != zone) _pending.Push((other, screen));
        }

        int steps = 0;
        while (_pending.Count > 0)
        {
            // A map whose walk does not settle is drawn whole.
            if (++steps > 4096) return false;
            (int at, Rect2 rect) = _pending.Pop();
            if (_reached[at])
            {
                // Seen through this part of the screen already.
                if (_seen[at].Encloses(rect)) continue;
                rect = _seen[at].Merge(rect);
            }

            _reached[at] = true;
            _seen[at] = rect;
            visible |= 1UL << at;
            foreach (int index in _byZone[at])
            {
                Portal portal = _portals[index];
                int other = portal.Back == at ? portal.Front : portal.Back;
                if (!Shows(portal.Vertices, clip, rect, out Rect2 through)) continue;
                if (_reached[other] && _seen[other].Encloses(through)) continue;
                _pending.Push((other, through));
            }
        }

        Visible = visible;
        return true;
    }

    /// <summary>
    /// Where the portal shows inside <paramref name="rect"/> (normalised device coordinates). A portal with a vertex
    /// at or behind the eye's plane keeps all of the rectangle when any of it lies in front.
    /// </summary>
    private static bool Shows(Vector3[] vertices, in Projection clip, Rect2 rect, out Rect2 through)
    {
        through = rect;
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        bool front = false, behind = false;
        foreach (Vector3 v in vertices)
        {
            Vector4 c = clip * new Vector4(v.X, v.Y, v.Z, 1f);
            if (!float.IsFinite(c.X + c.Y + c.W)) return true;
            // Half a metre of margin in front of the eye: a camera standing in a doorway sees both of its zones.
            if (c.W < 0.5f)
            {
                behind = true;
                if (c.W > -0.5f) front = true;
                continue;
            }

            front = true;
            float x = c.X / c.W, y = c.Y / c.W;
            minX = MathF.Min(minX, x);
            maxX = MathF.Max(maxX, x);
            minY = MathF.Min(minY, y);
            maxY = MathF.Max(maxY, y);
        }

        if (!front) return false;
        if (behind) return true;
        // A texel of margin and more: the test must never lose a portal the eye can see through.
        const float Margin = 0.01f;
        var box = new Rect2(minX - Margin, minY - Margin, (maxX - minX) + (2f * Margin), (maxY - minY) + (2f * Margin));
        if (!box.Intersects(rect)) return false;
        through = box.Intersection(rect);
        return true;
    }
}
