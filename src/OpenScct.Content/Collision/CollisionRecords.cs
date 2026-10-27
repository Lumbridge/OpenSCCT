using System;
using OpenScct.Core;

namespace OpenScct.Content.Collision;

/// <summary>Collider kinds (<c>COLL</c> kind byte).</summary>
public enum ColliderKind : byte
{
    /// <summary>The level's world BSP model.</summary>
    WorldBsp = 0,

    /// <summary>Brush actors and volumes (BlockingVolume).</summary>
    Brush = 1,

    /// <summary>Static mesh per-polygon collision.</summary>
    MeshPoly = 2,

    /// <summary>Static mesh simplified collision (convex hulls or its collision BSP).</summary>
    MeshHull = 3,

    /// <summary>Mover and subclasses.</summary>
    Mover = 4,

    /// <summary>Breakable glass (SVitre).</summary>
    Glass = 5,

    /// <summary>Gameplay objects (SGamePlayObject and similar swappable or breakable meshes).</summary>
    Gpo = 6,

    /// <summary>Collision attached to another actor (<see cref="Collider.Parent"/>).</summary>
    Attached = 7,
}

/// <summary>Collider and cylinder flags, from the owning actor's properties.</summary>
[Flags]
public enum ColliderFlags : ushort
{
    None = 0,
    CollideActors = 1 << 0,
    BlockActors = 1 << 1,
    BlockPlayers = 1 << 2,
    BlockZeroExtent = 1 << 3,
    BlockNonZeroExtent = 1 << 4,
    BlockCamera = 1 << 5,
    BulletGoesThrough = 1 << 6,
    StartsDisabled = 1 << 7,
}

/// <summary>Collision material flags (<c>TFLG</c>).</summary>
[Flags]
public enum CollisionMaterialFlags : ushort
{
    None = 0,

    /// <summary>Spy climb surfaces.</summary>
    Climbable = 1 << 0,

    /// <summary>Split jump not allowed against it.</summary>
    NoSplitJump = 1 << 1,

    /// <summary>Sticky cameras do not attach.</summary>
    NoStickyCam = 1 << 2,

    /// <summary>UE2 PF_Semisolid.</summary>
    Semisolid = 1 << 3,

    /// <summary>UE2 PF_Invisible: collides but is not drawn.</summary>
    Invisible = 1 << 4,
}

/// <summary>Zone flags (<c>ZONE</c>).</summary>
[Flags]
public enum CollisionZoneFlags : uint
{
    None = 0,
    SkyZone = 1 << 0,
}

/// <summary>How a collider collides (collision.md, geometry rule).</summary>
public enum ColliderGeometry : byte
{
    Triangles,
    Bsp,
    Hulls,
}

/// <summary>A triangle (<c>TRIS</c>): vertex indexes, owning collider and material. Front face is (v1 - v0) x (v2 - v0).</summary>
public readonly record struct CollisionTriangle(uint I0, uint I1, uint I2, ushort Collider, ushort Material);

/// <summary>A collision material (<c>TFLG</c>). <see cref="Name"/> is a string index or <see cref="CollisionData.None"/>.</summary>
public readonly record struct CollisionMaterial(byte SurfaceType, CollisionMaterialFlags Flags, uint Name);

/// <summary>A plane (<c>PLNS</c>), unit normal and <c>w = n . p</c>; the signed distance is <c>n . p - w</c>.</summary>
public readonly record struct CollisionPlane(FVector Normal, float W)
{
    public float PlaneDot(FVector p) => (Normal | p) - W;
}

/// <summary>A convex hull (<c>HULL</c>): planes pointing out, optional corners, material and local bounds.</summary>
public readonly record struct CollisionHull(uint PlaneStart, uint PlaneCount, uint VertStart, uint VertCount, ushort Material,
    FVector Min, FVector Max);

/// <summary>A BSP node (<c>BSPN</c>, provisional layout). Child indexes are -1 for none.</summary>
public readonly record struct CollisionBspNode(CollisionPlane Plane, int Front, int Back, int Coplanar, int CollisionBound,
    uint TriStart, uint TriCount, ushort NodeFlags, byte ZoneFront, byte ZoneBack);

/// <summary>
/// A collider (<c>COLL</c>): geometry ranges in the shared chunks, the owning actor and the initial transform.
/// Vertices, planes and hull bounds are in the collider's local space.
/// </summary>
public readonly record struct Collider(
    ColliderKind Kind,
    ColliderFlags Flags,
    uint Actor,
    uint Parent,
    uint VertStart,
    uint VertCount,
    uint TriStart,
    uint TriCount,
    uint BspStart,
    uint BspCount,
    uint HullStart,
    uint HullCount,
    FVector Location,
    FRotator Rotation,
    FVector Scale,
    FVector PrePivot,
    FVector AabbMin,
    FVector AabbMax)
{
    /// <summary>Hulls when it has any, else BSP nodes when it has any, else triangles.</summary>
    public ColliderGeometry Geometry => HullCount > 0 ? ColliderGeometry.Hulls : BspCount > 0 ? ColliderGeometry.Bsp : ColliderGeometry.Triangles;

    /// <summary>True when the transform is the identity (location 0, rotation 0, scale 1, prePivot 0).</summary>
    public bool HasIdentityTransform =>
        Location.IsZero() && Rotation == default && Scale == new FVector(1f, 1f, 1f) && PrePivot.IsZero();
}

/// <summary>An initial actor collision cylinder (<c>CYLS</c>). Height is the half height, as UE2.</summary>
public readonly record struct CollisionCylinder(uint Actor, ColliderFlags Flags, FVector Location, float Radius, float Height);

/// <summary>A zone (<c>ZONE</c>). Record 0 is the default zone (the LevelInfo).</summary>
public readonly record struct CollisionZone(uint Actor, CollisionZoneFlags Flags, FVector AabbMin, FVector AabbMax)
{
    public bool Contains(FVector p) =>
        p.X >= AabbMin.X && p.X <= AabbMax.X && p.Y >= AabbMin.Y && p.Y <= AabbMax.Y && p.Z >= AabbMin.Z && p.Z <= AabbMax.Z;
}

/// <summary>A chunk the reader did not know and skipped (kept so tools can report it).</summary>
public sealed record CollisionUnknownChunk(string FourCC, uint Version, uint Count, byte[] Payload);
