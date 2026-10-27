using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using OpenScct.Core;

namespace OpenScct.Content.Collision;

/// <summary>
/// The contents of <c>sim/collision.bin</c> (spec/svmap/1.0/collision.md): shared vertex, triangle, material,
/// plane, hull and BSP arrays, the colliders that own ranges of them, actor cylinders and zones. Indexes are
/// absolute positions in the target array. Built by <see cref="CollisionReader"/> or by hand for tests and tools.
/// </summary>
public sealed class CollisionData
{
    /// <summary>The <c>none</c> value of a u32 index field.</summary>
    public const uint None = 0xFFFFFFFFu;

    public static readonly CollisionData Empty = new();

    public ImmutableArray<string> Strings { get; init; } = [];

    public ImmutableArray<FVector> Vertices { get; init; } = [];

    public ImmutableArray<CollisionTriangle> Triangles { get; init; } = [];

    public ImmutableArray<CollisionMaterial> Materials { get; init; } = [];

    public ImmutableArray<CollisionPlane> Planes { get; init; } = [];

    public ImmutableArray<CollisionHull> Hulls { get; init; } = [];

    public ImmutableArray<CollisionBspNode> BspNodes { get; init; } = [];

    public ImmutableArray<Collider> Colliders { get; init; } = [];

    public ImmutableArray<CollisionCylinder> Cylinders { get; init; } = [];

    public ImmutableArray<CollisionZone> Zones { get; init; } = [];

    /// <summary>Chunks of later format versions the reader skipped, in file order.</summary>
    public ImmutableArray<CollisionUnknownChunk> UnknownChunks { get; init; } = [];

    /// <summary>The string at <paramref name="index"/>, or null for <see cref="None"/>.</summary>
    public string? GetString(uint index) => index == None ? null : Strings[checked((int)index)];

    /// <summary>The owning actor id of a collider, or null for the level itself.</summary>
    public string? ActorOf(in Collider collider) => GetString(collider.Actor);

    public ReadOnlySpan<FVector> VerticesOf(in Collider c) => Vertices.AsSpan().Slice((int)c.VertStart, (int)c.VertCount);

    public ReadOnlySpan<CollisionTriangle> TrianglesOf(in Collider c) => Triangles.AsSpan().Slice((int)c.TriStart, (int)c.TriCount);

    public ReadOnlySpan<CollisionBspNode> BspNodesOf(in Collider c) => BspNodes.AsSpan().Slice((int)c.BspStart, (int)c.BspCount);

    public ReadOnlySpan<CollisionHull> HullsOf(in Collider c) => Hulls.AsSpan().Slice((int)c.HullStart, (int)c.HullCount);

    /// <summary>
    /// The collision bound of a BSP node: in converted SVMap 1.1 maps, the convex solid region behind the node (the
    /// original LeafHulls), a hull of no collider with planes pointing out of the region. False when the node has none.
    /// </summary>
    public bool TryGetCollisionBound(in CollisionBspNode node, out CollisionHull hull)
    {
        if ((uint)node.CollisionBound < (uint)Hulls.Length)
        {
            hull = Hulls[node.CollisionBound];
            return true;
        }

        hull = default;
        return false;
    }

    public ReadOnlySpan<CollisionPlane> PlanesOf(in CollisionHull h) => Planes.AsSpan().Slice((int)h.PlaneStart, (int)h.PlaneCount);

    public ReadOnlySpan<FVector> CornersOf(in CollisionHull h) => Vertices.AsSpan().Slice((int)h.VertStart, (int)h.VertCount);

    /// <summary>Unnormalised front-face normal of triangle <paramref name="index"/>: (v1 - v0) x (v2 - v0).</summary>
    public FVector TriangleNormal(int index)
    {
        CollisionTriangle t = Triangles[index];
        FVector v0 = Vertices[(int)t.I0];
        return (Vertices[(int)t.I1] - v0) ^ (Vertices[(int)t.I2] - v0);
    }

    /// <summary>Index of the first collider owned by <paramref name="actorId"/> (ignoring ASCII case), or -1.</summary>
    public int FindCollider(string actorId)
    {
        for (int i = 0; i < Colliders.Length; i++)
        {
            string? owner = GetString(Colliders[i].Actor);
            if (owner is not null && AsciiCaseComparer.AreEqual(owner, actorId))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Range and reference checks (the pipeline's <c>check_collision</c>, plus finite BSP planes). Throws
    /// <see cref="SvMapFormatException"/> on the first problem.
    /// </summary>
    public void Validate(string? file = CollisionReader.RelativePath)
    {
        int ns = Strings.Length;
        int nv = Vertices.Length;
        int nt = Triangles.Length;
        int nm = Materials.Length;
        int np = Planes.Length;
        int nh = Hulls.Length;
        int nb = BspNodes.Length;
        int nc = Colliders.Length;

        void Fail(string message) => throw new SvMapFormatException(file, message);

        void Range(string what, uint start, uint count, int limit)
        {
            if ((ulong)start + count > (ulong)limit)
            {
                Fail($"{what} range {start}+{count} exceeds {limit}");
            }
        }

        void StringRef(string what, uint index)
        {
            if (index != None && index >= (uint)ns)
            {
                Fail($"{what} string index {index} out of range");
            }
        }

        void Finite(string what, FVector v)
        {
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
            {
                Fail($"{what} is not finite");
            }
        }

        void FinitePlane(string what, CollisionPlane p)
        {
            Finite(what, p.Normal);
            if (!float.IsFinite(p.W))
            {
                Fail($"{what} is not finite");
            }
        }

        for (int i = 0; i < nv; i++)
        {
            Finite($"vertex {i}", Vertices[i]);
        }

        for (int i = 0; i < np; i++)
        {
            FinitePlane($"plane {i}", Planes[i]);
        }

        for (int i = 0; i < nt; i++)
        {
            CollisionTriangle t = Triangles[i];
            if (Math.Max(t.I0, Math.Max(t.I1, t.I2)) >= (uint)nv)
            {
                Fail($"triangle {i} vertex index out of range");
            }

            if (t.Collider >= nc)
            {
                Fail($"triangle {i} collider {t.Collider} out of range");
            }

            Collider owner = Colliders[t.Collider];
            if ((uint)i < owner.TriStart || (ulong)i >= (ulong)owner.TriStart + owner.TriCount)
            {
                Fail($"triangle {i} is outside its collider {t.Collider} triangle range");
            }

            if (t.Material >= nm)
            {
                Fail($"triangle {i} material {t.Material} out of range");
            }
        }

        for (int i = 0; i < nm; i++)
        {
            StringRef($"material {i}", Materials[i].Name);
            if (((ushort)Materials[i].Flags >> 5) != 0)
            {
                Fail($"material {i} sets reserved flag bits");
            }
        }

        for (int i = 0; i < nh; i++)
        {
            CollisionHull h = Hulls[i];
            Range($"hull {i} planes", h.PlaneStart, h.PlaneCount, np);
            Range($"hull {i} corners", h.VertStart, h.VertCount, nv);
            if (h.Material >= nm)
            {
                Fail($"hull {i} material out of range");
            }

            Finite($"hull {i} min", h.Min);
            Finite($"hull {i} max", h.Max);
        }

        for (int i = 0; i < nb; i++)
        {
            CollisionBspNode n = BspNodes[i];
            FinitePlane($"bsp node {i} plane", n.Plane);
            foreach (int child in (ReadOnlySpan<int>)[n.Front, n.Back, n.Coplanar])
            {
                if (child < -1 || child >= nb)
                {
                    Fail($"bsp node {i} child {child} out of range");
                }
            }

            if (n.CollisionBound < -1 || n.CollisionBound >= nh)
            {
                Fail($"bsp node {i} collision bound out of range");
            }

            Range($"bsp node {i} triangles", n.TriStart, n.TriCount, nt);
        }

        for (int i = 0; i < nc; i++)
        {
            Collider c = Colliders[i];
            if ((byte)c.Kind > (byte)ColliderKind.Attached)
            {
                Fail($"collider {i} kind {(byte)c.Kind} unknown");
            }

            if (((ushort)c.Flags >> 8) != 0)
            {
                Fail($"collider {i} sets reserved flag bits");
            }

            StringRef($"collider {i} actor", c.Actor);
            StringRef($"collider {i} parent", c.Parent);
            Range($"collider {i} vertices", c.VertStart, c.VertCount, nv);
            Range($"collider {i} triangles", c.TriStart, c.TriCount, nt);
            Range($"collider {i} bsp", c.BspStart, c.BspCount, nb);
            Range($"collider {i} hulls", c.HullStart, c.HullCount, nh);
            Finite($"collider {i} location", c.Location);
            Finite($"collider {i} scale", c.Scale);
            Finite($"collider {i} prePivot", c.PrePivot);
            Finite($"collider {i} aabbMin", c.AabbMin);
            Finite($"collider {i} aabbMax", c.AabbMax);
            if (c.AabbMin.X > c.AabbMax.X || c.AabbMin.Y > c.AabbMax.Y || c.AabbMin.Z > c.AabbMax.Z)
            {
                Fail($"collider {i} bounds are inverted");
            }
        }

        for (int i = 0; i < Cylinders.Length; i++)
        {
            CollisionCylinder c = Cylinders[i];
            StringRef($"cylinder {i} actor", c.Actor);
            if (c.Actor == None)
            {
                Fail($"cylinder {i} has no actor");
            }

            Finite($"cylinder {i} location", c.Location);
            if (!float.IsFinite(c.Radius) || !float.IsFinite(c.Height))
            {
                Fail($"cylinder {i} size is not finite");
            }
        }

        for (int i = 0; i < Zones.Length; i++)
        {
            CollisionZone z = Zones[i];
            StringRef($"zone {i} actor", z.Actor);
            if (((uint)z.Flags >> 1) != 0)
            {
                Fail($"zone {i} sets reserved flag bits");
            }

            Finite($"zone {i} aabbMin", z.AabbMin);
            Finite($"zone {i} aabbMax", z.AabbMax);
        }
    }

    /// <summary>
    /// Actor names (collider owners and parents, cylinders, zones) that are not actor ids of
    /// <paramref name="gameplay"/>, matched exactly as the validator does. Empty when all resolve.
    /// </summary>
    public List<string> CheckActorNames(GameplayDoc gameplay)
    {
        ArgumentNullException.ThrowIfNull(gameplay);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ActorRecord a in gameplay.AllActors)
        {
            ids.Add(a.Id);
        }

        var problems = new List<string>();

        void Check(string what, uint index)
        {
            if (index != None && index < (uint)Strings.Length && !ids.Contains(Strings[(int)index]))
            {
                problems.Add($"{what} names '{Strings[(int)index]}', which is not an actor id");
            }
        }

        for (int i = 0; i < Colliders.Length; i++)
        {
            Check($"collider {i} actor", Colliders[i].Actor);
        }

        for (int i = 0; i < Colliders.Length; i++)
        {
            Check($"collider {i} parent", Colliders[i].Parent);
        }

        for (int i = 0; i < Cylinders.Length; i++)
        {
            Check($"cylinder {i}", Cylinders[i].Actor);
        }

        for (int i = 0; i < Zones.Length; i++)
        {
            Check($"zone {i}", Zones[i].Actor);
        }

        return problems;
    }
}
