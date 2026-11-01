using System.Collections.Generic;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Physics.Collision;

public sealed partial class CollisionWorld
{
    private readonly List<Actor> _cylinderActors = new(64);
    private long _cylinderTick = long.MinValue;
    private int _cylinderCount = -1;
    private bool _cylinderDirty = true;

    /// <summary>
    /// An actor's collision cylinder and flags. Actors placed by the map keep the CYLS values while their own
    /// fields still hold what they had when the level was bound (class defaults are not decoded for every class);
    /// once script changes them, the live fields apply.
    /// </summary>
    public void GetCylinder(Actor a, out float radius, out float height, out bool collideActors, out bool blockActors, out bool blockPlayers)
    {
        int id = a.ActorId;
        if ((uint)id < (uint)_slots.Length)
        {
            ref ActorSlot s = ref _slots[id];
            if (s.HasCylinder && a.CollisionRadius == s.BaseRadius && a.CollisionHeight == s.BaseHeight &&
                a.bCollideActors == s.BaseCollideActors && a.bBlockActors == s.BaseBlockActors && a.bBlockPlayers == s.BaseBlockPlayers)
            {
                radius = s.Radius;
                height = s.Height;
                collideActors = (s.CylinderFlags & ColliderFlags.CollideActors) != 0;
                blockActors = (s.CylinderFlags & ColliderFlags.BlockActors) != 0;
                blockPlayers = (s.CylinderFlags & ColliderFlags.BlockPlayers) != 0;
                return;
            }
        }

        radius = a.CollisionRadius;
        height = a.CollisionHeight;
        collideActors = a.bCollideActors;
        blockActors = a.bBlockActors;
        blockPlayers = a.bBlockPlayers;
    }

    /// <summary>
    /// The trace flags of an actor's cylinder (collide, block, which extents it blocks, camera): the CYLS record's
    /// while the actor keeps the values it had when the level was bound, else its live fields.
    /// </summary>
    public ColliderFlags GetCylinderFlags(Actor a)
    {
        int id = a.ActorId;
        if ((uint)id < (uint)_slots.Length)
        {
            ref ActorSlot s = ref _slots[id];
            if (s.HasCylinder && a.CollisionRadius == s.BaseRadius && a.CollisionHeight == s.BaseHeight &&
                a.bCollideActors == s.BaseCollideActors && a.bBlockActors == s.BaseBlockActors && a.bBlockPlayers == s.BaseBlockPlayers)
            {
                return s.CylinderFlags;
            }
        }

        ColliderFlags f = ColliderFlags.None;
        if (a.bCollideActors)
        {
            f |= ColliderFlags.CollideActors;
        }

        if (a.bBlockActors)
        {
            f |= ColliderFlags.BlockActors;
        }

        if (a.bBlockPlayers)
        {
            f |= ColliderFlags.BlockPlayers;
        }

        if (a.bBlockZeroExtentTraces)
        {
            f |= ColliderFlags.BlockZeroExtent;
        }

        if (a.bBlockNonZeroExtentTraces)
        {
            f |= ColliderFlags.BlockNonZeroExtent;
        }

        if (a.bBlockCamera)
        {
            f |= ColliderFlags.BlockCamera;
        }

        if (a.bBulletGoTru)
        {
            f |= ColliderFlags.BulletGoesThrough;
        }

        return f;
    }

    /// <summary>
    /// True when <paramref name="other"/> blocks <paramref name="source"/> (UE2 IsBlockedBy for two actors): both
    /// must block, players by bBlockPlayers and everything else by bBlockActors.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 AActor::IsBlockedBy for actor pairs; SCCT keeps bBlockPlayers")]
    public static bool ActorBlocks(Actor? source, Actor other, bool otherBlockActors, bool otherBlockPlayers)
    {
        if (source is null)
        {
            return otherBlockActors || otherBlockPlayers;
        }

        bool otherBlocksSource = source.bIsAPawn ? otherBlockPlayers : otherBlockActors;
        bool sourceBlocksOther = other.bIsAPawn ? source.bBlockPlayers : source.bBlockActors;
        return otherBlocksSource && sourceBlocksOther;
    }

    /// <summary>
    /// True when two actors overlap for Touch: cylinder against cylinder, or an actor's box against the colliders
    /// of a mesh or brush actor.
    /// </summary>
    [UcNative("AActor::IsOverlapping")]
    [Provenance(ProvenanceKind.Decoded, "Cylinder overlap is UPrimitive::PointCheck: |dz| < H1 + H2 and |dxy|^2 < (R1 + R2)^2 (spec/native/collision-primitives.md)")]
    [Provenance(ProvenanceKind.Estimated, "AActor::IsOverlapping itself (vtable 0xbc) is used by the natives but not decoded")]
    public bool IsOverlapping(Actor a, Actor b)
    {
        if (ReferenceEquals(a, b) || a.bDeleteMe || b.bDeleteMe)
        {
            return false;
        }

        bool aMesh = HasColliders(a);
        bool bMesh = HasColliders(b);
        if (aMesh && bMesh)
        {
            return false;
        }

        if (bMesh)
        {
            GetCylinder(a, out float ra, out float ha, out _, out _, out _);
            return OverlapsActorColliders(b, a.Location, new FVector(ra, ra, ha));
        }

        if (aMesh)
        {
            GetCylinder(b, out float rb, out float hb, out _, out _, out _);
            return OverlapsActorColliders(a, b.Location, new FVector(rb, rb, hb));
        }

        GetCylinder(a, out float r1, out float h1, out _, out _, out _);
        GetCylinder(b, out float r2, out float h2, out _, out _, out _);
        float dz = a.Location.Z - b.Location.Z;
        float dx = a.Location.X - b.Location.X;
        float dy = a.Location.Y - b.Location.Y;
        float hs = h1 + h2;
        float rs = r1 + r2;
        return dz * dz < hs * hs && dx * dx + dy * dy < rs * rs;
    }

    /// <summary>True when a box overlaps any enabled collider of <paramref name="owner"/>.</summary>
    public bool OverlapsActorColliders(Actor owner, FVector location, FVector extent)
    {
        for (int ci = FirstColliderOf(owner); ci >= 0; ci = _nextColliderOfActor[ci])
        {
            if (OverlapsCollider(ci, location, extent))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a box overlaps collider <paramref name="index"/> (enabled ones only, no flag filters).</summary>
    public bool OverlapsCollider(int index, FVector location, FVector extent)
    {
        ColliderState c = _colliders[index];
        if (!c.Enabled)
        {
            return false;
        }

        // Rotating movers test every blocking cylinder for encroachment. Reject distant boxes using the
        // conservative current-pose bounds before rebuilding the mover's triangles and BVH.
        if (c.IsDynamic && !SyncColliderForQuery(c, location, location, extent))
        {
            return false;
        }

        if (!CollisionMath.BoxesTouch(c.Min - extent, c.Max + extent, location, location))
        {
            return false;
        }

        return Overlap(out _, null, null, index, location, extent, ETraceFlags.TRACE_Everything, bruteForce: false);
    }

    /// <summary>The colliding actors of the bound level that collide as cylinders (refreshed once per tick).</summary>
    internal List<Actor> CylinderActors
    {
        get
        {
            RefreshCylinderCache();
            return _cylinderActors;
        }
    }

    private void RefreshCylinderCache()
    {
        Level? level = _level;
        if (level is null)
        {
            _cylinderActors.Clear();
            return;
        }

        if (!_cylinderDirty && level.TickCount == _cylinderTick && level.ActorCount == _cylinderCount)
        {
            return;
        }

        _cylinderActors.Clear();
        IReadOnlyList<Actor> actors = level.Actors;
        for (int i = 0; i < actors.Count; i++)
        {
            Actor a = actors[i];
            if (a.bDeleteMe || a is LevelInfo || HasColliders(a))
            {
                continue;
            }

            GetCylinder(a, out _, out _, out bool collide, out _, out _);
            if (collide)
            {
                _cylinderActors.Add(a);
            }
        }

        _cylinderTick = level.TickCount;
        _cylinderCount = level.ActorCount;
        _cylinderDirty = false;
    }

    /// <summary>
    /// The actor cylinders of a sweep (FCollisionHash::ActorLineCheck): candidates that block this extent and pass
    /// ShouldTrace (pawns by TRACE_Pawns, other actors by TRACE_Others with OnlyProjActor and Blocking, the actor
    /// gates), tested as cylinders widened by the box (UPrimitive::LineCheck).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "FCollisionHash::ActorLineCheck 0x1115f5a0, AActor / APawn ShouldTrace (spec/native/collision-traces.md)")]
    private void SweepActors(ref Query q)
    {
        List<Actor> list = CylinderActors;
        if (list.Count == 0)
        {
            return;
        }

        FVector smin = CollisionMath.MinV(q.S, q.S + q.D);
        FVector smax = CollisionMath.MaxV(q.S, q.S + q.D);
        for (int i = 0; i < list.Count; i++)
        {
            Actor a = list[i];
            if (a.bDeleteMe || ReferenceEquals(a, q.Ignore))
            {
                continue;
            }

            GetCylinder(a, out float r, out float h, out bool collide, out bool blockActors, out bool blockPlayers);
            var half = new FVector(r + q.E.X, r + q.E.Y, h + q.E.Z);
            FVector c = a.Location;
            // Reject distant cylinders before their owner chain and detailed trace flags. Read the live shape
            // here: a crouch, teleport, root-motion probe or rewind can change it between queries in one tick.
            if (!CollisionMath.BoxesTouch(c - half, c + half, smin, smax) ||
                IsSourceOrOwnerOf(a, q.Source) || !ActorPasses(a, ref q, collide, blockActors, blockPlayers))
            {
                continue;
            }

            if (CollisionMath.CylinderCheck(c, r, h, q.S, q.D, q.E, q.Limit, out float time, out FVector n, out bool inside))
            {
                Consider(ref q, time, ActorKey(a), n, inside, -1, -1, -1, a);
                if (q.Done)
                {
                    return;
                }
            }
        }
    }

    /// <summary>ShouldTrace and the extent rule for an actor cylinder whose current shape has been read.</summary>
    private bool ActorPasses(Actor a, ref Query q, bool collide, bool blockActors, bool blockPlayers)
    {
        if (!collide)
        {
            return false;
        }

        ColliderFlags flags = GetCylinderFlags(a);
        if (!q.IsOverlap && (flags & (q.Zero ? ColliderFlags.BlockZeroExtent : ColliderFlags.BlockNonZeroExtent)) == 0)
        {
            return false;
        }

        Actor? src = q.Source;
        if ((q.Flags & ETraceFlags.TRACE_ShadowCast) != 0 && !a.bShadowCast)
        {
            return false;
        }

        if (src is not null && src.bOnlyAffectPawns && !a.bIsAPawn)
        {
            return false;
        }

        if (a.bOnlyAffectPawns && (src is null || !src.bIsAPawn))
        {
            return false;
        }

        if ((q.Flags & ETraceFlags.TRACE_Camera) != 0 && (flags & ColliderFlags.BlockCamera) == 0)
        {
            return false;
        }

        if ((q.Flags & ETraceFlags.TRACE_Bullet) != 0 && (flags & ColliderFlags.BulletGoesThrough) != 0)
        {
            return false;
        }

        if ((q.Flags & ETraceFlags.TRACE_AcceptProjectors) != 0)
        {
            return a.bAcceptsProjectors;
        }

        bool blocking = (q.Flags & ETraceFlags.TRACE_Blocking) != 0;
        if (a.bIsAPawn)
        {
            if ((q.Flags & ETraceFlags.TRACE_Pawns) == 0)
            {
                return false;
            }

            return !blocking || ActorBlocks(src, a, blockActors, blockPlayers);
        }

        if ((q.Flags & ETraceFlags.TRACE_Others) == 0)
        {
            return false;
        }

        if ((q.Flags & ETraceFlags.TRACE_OnlyProjActor) != 0 && !a.bProjTarget && !(blockActors && blockPlayers))
        {
            return false;
        }

        return !blocking || ActorBlocks(src, a, blockActors, blockPlayers);
    }

    private void OverlapActors(ref Query q)
    {
        List<Actor> list = CylinderActors;
        for (int i = 0; i < list.Count; i++)
        {
            Actor a = list[i];
            if (a.bDeleteMe || ReferenceEquals(a, q.Ignore))
            {
                continue;
            }

            // UPrimitive::PointCheck: within the summed half heights and the summed radii.
            GetCylinder(a, out float r, out float h, out bool collide, out bool blockActors, out bool blockPlayers);
            float dz = q.S.Z - a.Location.Z;
            float dx = q.S.X - a.Location.X;
            float dy = q.S.Y - a.Location.Y;
            float hs = q.E.Z + h;
            float rs = q.E.X + r;
            if (SvmMath.Abs(dz) < hs && dx * dx + dy * dy < rs * rs &&
                !IsSourceOrOwnerOf(a, q.Source) && ActorPasses(a, ref q, collide, blockActors, blockPlayers))
            {
                FVector n = (q.S - a.Location).SafeNormal();
                ConsiderOverlap(ref q, ActorKey(a), n, -1, -1, -1, a);
            }
        }
    }
}
