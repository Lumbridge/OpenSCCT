using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using OpenScct.Content.Collision;
using OpenScct.Core;

namespace OpenScct.Physics.Collision;

/// <summary>
/// Builds <see cref="CollisionData"/> by hand, for tests, tools and native maps: world triangles (collider 0, a
/// world BSP collided by its triangles), boxes and ramps made of them, and extra colliders with their own
/// transform (box hulls, triangle meshes). The result passes <see cref="CollisionData.Validate"/>.
/// </summary>
public sealed class CollisionDataBuilder
{
    /// <summary>Flags of level geometry: collides with and blocks everything.</summary>
    public const ColliderFlags WorldFlags = ColliderFlags.CollideActors | ColliderFlags.BlockActors | ColliderFlags.BlockPlayers |
        ColliderFlags.BlockZeroExtent | ColliderFlags.BlockNonZeroExtent | ColliderFlags.BlockCamera;

    private readonly List<string> _strings = new();
    private readonly List<CollisionMaterial> _materials = new();
    private readonly List<FVector> _worldVerts = new();
    private readonly List<(int A, int B, int C, ushort Material)> _worldTris = new();
    private readonly List<PendingCollider> _colliders = new();
    private readonly List<CollisionZone> _zones = new();
    private readonly List<CollisionCylinder> _cylinders = new();

    private sealed class PendingCollider
    {
        public ColliderKind Kind;
        public ColliderFlags Flags;
        public string? Actor;
        public FVector Location;
        public FRotator Rotation;
        public FVector Scale = new(1f, 1f, 1f);
        public FVector PrePivot;
        public List<FVector> Verts = new();
        public List<(int A, int B, int C, ushort Material)> Tris = new();
        public List<(FVector Min, FVector Max, ushort Material, bool Corners)> Boxes = new();
    }

    public CollisionDataBuilder()
    {
        AddMaterial();
    }

    /// <summary>Adds a material; material 0 (plain, no flags) always exists.</summary>
    public ushort AddMaterial(byte surfaceType = 0, CollisionMaterialFlags flags = CollisionMaterialFlags.None, string? name = null)
    {
        _materials.Add(new CollisionMaterial(surfaceType, flags, name is null ? CollisionData.None : String(name)));
        return (ushort)(_materials.Count - 1);
    }

    /// <summary>Adds a world triangle; its front face is (b - a) x (c - a).</summary>
    public void AddTriangle(FVector a, FVector b, FVector c, ushort material = 0)
    {
        int i = _worldVerts.Count;
        _worldVerts.Add(a);
        _worldVerts.Add(b);
        _worldVerts.Add(c);
        _worldTris.Add((i, i + 1, i + 2, material));
    }

    /// <summary>Adds a quad (a, b, c, d in order) as two world triangles.</summary>
    public void AddQuad(FVector a, FVector b, FVector c, FVector d, ushort material = 0)
    {
        AddTriangle(a, b, c, material);
        AddTriangle(a, c, d, material);
    }

    /// <summary>Adds a solid axis-aligned box to the world as 12 outward-facing triangles.</summary>
    public void AddBox(FVector min, FVector max, ushort material = 0)
    {
        if (!(min.X < max.X && min.Y < max.Y && min.Z < max.Z))
        {
            throw new ArgumentException("A box needs min < max on every axis.");
        }

        foreach ((FVector a, FVector b, FVector c, FVector d) in BoxFaces(min, max))
        {
            AddQuad(a, b, c, d, material);
        }
    }

    /// <summary>
    /// Adds a solid ramp rising along +X: its sloped top goes from height <paramref name="z0"/> at x0 to
    /// <paramref name="z0"/> + <paramref name="rise"/> at x1, over y0..y1, down to <paramref name="z0"/>.
    /// </summary>
    public void AddRamp(float x0, float x1, float y0, float y1, float z0, float rise, ushort material = 0)
    {
        var p000 = new FVector(x0, y0, z0);
        var p100 = new FVector(x1, y0, z0);
        var p010 = new FVector(x0, y1, z0);
        var p110 = new FVector(x1, y1, z0);
        var p101 = new FVector(x1, y0, z0 + rise);
        var p111 = new FVector(x1, y1, z0 + rise);
        AddQuad(p000, p010, p110, p100, material);  // bottom, facing -Z
        AddTriangle(p000, p100, p101, material);    // side y0, facing -Y
        AddTriangle(p010, p111, p110, material);    // side y1, facing +Y
        AddQuad(p100, p110, p111, p101, material);  // back x1, facing +X
        AddQuad(p000, p101, p111, p010, material);  // slope, facing up and -X
    }

    /// <summary>
    /// Adds a collider made of one box hull (local min/max) with its own transform, owned by
    /// <paramref name="actorId"/>. Returns its collider index.
    /// </summary>
    public int AddBoxHullCollider(ColliderKind kind, string? actorId, ColliderFlags flags, FVector localMin, FVector localMax,
        FVector location = default, FRotator rotation = default, ushort material = 0, bool writeCorners = true, FVector? scale = null)
    {
        var c = NewCollider(kind, actorId, flags, location, rotation, scale ?? new FVector(1f, 1f, 1f), default);
        c.Boxes.Add((localMin, localMax, material, writeCorners));
        return _colliders.Count;
    }

    /// <summary>Adds a collider made of local-space boxes as triangles (per-polygon mesh collision). Returns its index.</summary>
    public int AddBoxMeshCollider(ColliderKind kind, string? actorId, ColliderFlags flags, IReadOnlyList<(FVector Min, FVector Max)> localBoxes,
        FVector location = default, FRotator rotation = default, FVector? scale = null, FVector prePivot = default, ushort material = 0)
    {
        ArgumentNullException.ThrowIfNull(localBoxes);
        var c = NewCollider(kind, actorId, flags, location, rotation, scale ?? new FVector(1f, 1f, 1f), prePivot);
        foreach ((FVector min, FVector max) in localBoxes)
        {
            foreach ((FVector a, FVector b, FVector cc, FVector d) in BoxFaces(min, max))
            {
                int i = c.Verts.Count;
                c.Verts.Add(a);
                c.Verts.Add(b);
                c.Verts.Add(cc);
                c.Verts.Add(d);
                c.Tris.Add((i, i + 1, i + 2, material));
                c.Tris.Add((i, i + 2, i + 3, material));
            }
        }

        return _colliders.Count;
    }

    /// <summary>Adds a zone with its bounds; zone 0 (the default zone) always comes first.</summary>
    public int AddZone(string? actorId, FVector min, FVector max)
    {
        if (_zones.Count == 0)
        {
            _zones.Add(new CollisionZone(CollisionData.None, CollisionZoneFlags.None, default, default));
        }

        _zones.Add(new CollisionZone(actorId is null ? CollisionData.None : String(actorId), CollisionZoneFlags.None, min, max));
        return _zones.Count - 1;
    }

    /// <summary>Adds an initial actor cylinder (CYLS).</summary>
    public void AddCylinder(string actorId, ColliderFlags flags, FVector location, float radius, float height) =>
        _cylinders.Add(new CollisionCylinder(String(actorId), flags, location, radius, height));

    private PendingCollider NewCollider(ColliderKind kind, string? actorId, ColliderFlags flags, FVector location, FRotator rotation,
        FVector scale, FVector prePivot)
    {
        var c = new PendingCollider
        {
            Kind = kind,
            Flags = flags,
            Actor = actorId,
            Location = location,
            Rotation = rotation,
            Scale = scale,
            PrePivot = prePivot,
        };
        _colliders.Add(c);
        return c;
    }

    private uint String(string s)
    {
        int i = _strings.IndexOf(s);
        if (i < 0)
        {
            _strings.Add(s);
            i = _strings.Count - 1;
        }

        return (uint)i;
    }

    /// <summary>The six faces of a box, each wound so its normal points out.</summary>
    private static IEnumerable<(FVector, FVector, FVector, FVector)> BoxFaces(FVector min, FVector max)
    {
        var c000 = new FVector(min.X, min.Y, min.Z);
        var c100 = new FVector(max.X, min.Y, min.Z);
        var c010 = new FVector(min.X, max.Y, min.Z);
        var c110 = new FVector(max.X, max.Y, min.Z);
        var c001 = new FVector(min.X, min.Y, max.Z);
        var c101 = new FVector(max.X, min.Y, max.Z);
        var c011 = new FVector(min.X, max.Y, max.Z);
        var c111 = new FVector(max.X, max.Y, max.Z);
        yield return (c000, c010, c110, c100); // -Z
        yield return (c001, c101, c111, c011); // +Z
        yield return (c000, c100, c101, c001); // -Y
        yield return (c010, c011, c111, c110); // +Y
        yield return (c000, c001, c011, c010); // -X
        yield return (c100, c110, c111, c101); // +X
    }

    public CollisionData Build()
    {
        var verts = new List<FVector>(_worldVerts);
        var tris = new List<CollisionTriangle>();
        var planes = new List<CollisionPlane>();
        var hulls = new List<CollisionHull>();
        var colliders = new List<Collider>();

        foreach ((int a, int b, int c, ushort m) in _worldTris)
        {
            tris.Add(new CollisionTriangle((uint)a, (uint)b, (uint)c, 0, m));
        }

        (FVector wmin, FVector wmax) = Bounds(_worldVerts);
        colliders.Add(new Collider(ColliderKind.WorldBsp, WorldFlags, CollisionData.None, CollisionData.None, 0, (uint)_worldVerts.Count, 0,
            (uint)tris.Count, 0, 0, 0, 0, default, default, new FVector(1f, 1f, 1f), default, wmin, wmax));

        foreach (PendingCollider p in _colliders)
        {
            int index = colliders.Count;
            uint vertStart = (uint)verts.Count;
            uint triStart = (uint)tris.Count;
            uint hullStart = (uint)hulls.Count;
            var worldPoints = new List<FVector>();
            var xf = new ColliderTransform(p.Location, p.Rotation, p.Scale, p.PrePivot);
            foreach (FVector v in p.Verts)
            {
                verts.Add(v);
                worldPoints.Add(xf.TransformPoint(v));
            }

            foreach ((int a, int b, int c, ushort m) in p.Tris)
            {
                tris.Add(new CollisionTriangle(vertStart + (uint)a, vertStart + (uint)b, vertStart + (uint)c, (ushort)index, m));
            }

            foreach ((FVector min, FVector max, ushort m, bool corners) in p.Boxes)
            {
                uint planeStart = (uint)planes.Count;
                planes.Add(new CollisionPlane(new FVector(-1f, 0f, 0f), -min.X));
                planes.Add(new CollisionPlane(new FVector(1f, 0f, 0f), max.X));
                planes.Add(new CollisionPlane(new FVector(0f, -1f, 0f), -min.Y));
                planes.Add(new CollisionPlane(new FVector(0f, 1f, 0f), max.Y));
                planes.Add(new CollisionPlane(new FVector(0f, 0f, -1f), -min.Z));
                planes.Add(new CollisionPlane(new FVector(0f, 0f, 1f), max.Z));
                uint cornerStart = (uint)verts.Count;
                uint cornerCount = 0;
                for (int k = 0; k < 8; k++)
                {
                    var corner = new FVector((k & 1) != 0 ? max.X : min.X, (k & 2) != 0 ? max.Y : min.Y, (k & 4) != 0 ? max.Z : min.Z);
                    worldPoints.Add(xf.TransformPoint(corner));
                    if (corners)
                    {
                        verts.Add(corner);
                        cornerCount++;
                    }
                }

                hulls.Add(new CollisionHull(planeStart, 6, corners ? cornerStart : 0, cornerCount, m, min, max));
            }

            (FVector amin, FVector amax) = Bounds(worldPoints);
            colliders.Add(new Collider(p.Kind, p.Flags, p.Actor is null ? CollisionData.None : String(p.Actor), CollisionData.None, vertStart,
                (uint)p.Verts.Count, triStart, (uint)p.Tris.Count, 0, 0, hullStart, (uint)(hulls.Count - (int)hullStart), p.Location,
                p.Rotation, p.Scale, p.PrePivot, amin, amax));
        }

        var zones = new List<CollisionZone>(_zones);
        if (zones.Count == 0)
        {
            zones.Add(new CollisionZone(CollisionData.None, CollisionZoneFlags.None, wmin, wmax));
        }
        else
        {
            zones[0] = new CollisionZone(CollisionData.None, CollisionZoneFlags.None, wmin, wmax);
        }

        var data = new CollisionData
        {
            Strings = [.. _strings],
            Vertices = [.. verts],
            Triangles = [.. tris],
            Materials = [.. _materials],
            Planes = [.. planes],
            Hulls = [.. hulls],
            BspNodes = ImmutableArray<CollisionBspNode>.Empty,
            Colliders = [.. colliders],
            Cylinders = [.. _cylinders],
            Zones = [.. zones],
        };
        data.Validate(file: null);
        return data;
    }

    private static (FVector Min, FVector Max) Bounds(List<FVector> points)
    {
        if (points.Count == 0)
        {
            return (default, default);
        }

        FVector min = points[0];
        FVector max = points[0];
        foreach (FVector p in points)
        {
            min = CollisionMath.MinV(min, p);
            max = CollisionMath.MaxV(max, p);
        }

        return (min, max);
    }
}
