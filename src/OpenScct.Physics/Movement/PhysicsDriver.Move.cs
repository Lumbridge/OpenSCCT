using System.Collections.Generic;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Physics.Movement;

public sealed partial class PhysicsDriver
{
    /// <summary>
    /// Trace flags of an actor's own moves (ULevel::MoveActor): pawns, volumes and others with bCollideActors
    /// (without pawns for bIgnorePawns), movers, the level and level geometry with bCollideWorld, plus the blocking
    /// filter that stands for the native's IsBlockedBy pick.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "MoveActor mask (bCollideActors ? (bIgnorePawns ? 0x18 : 0x19) : 0) | (bCollideWorld ? 0x86 : 0)")]
    public static ETraceFlags MoveTraceFlags(Actor actor, bool bIgnorePawns = false)
    {
        NotNull(actor);
        ETraceFlags flags = ETraceFlags.TRACE_Blocking;
        if (actor.bCollideActors)
        {
            flags |= bIgnorePawns ? K.Flags.MoveActorsNoPawns : K.Flags.MoveActors;
        }

        if (actor.bCollideWorld)
        {
            flags |= K.Flags.MoveWorld;
        }

        return flags;
    }

    private static void NotNull(Actor actor) => System.ArgumentNullException.ThrowIfNull(actor);

    /// <summary>The box an actor moves as: (CollisionRadius, CollisionRadius, CollisionHeight).</summary>
    public FVector GetCylinderExtent(Actor actor)
    {
        NotNull(actor);
        Collision.GetCylinder(actor, out float r, out float h, out _, out _, out _);
        return new FVector(r, r, h);
    }

    /// <summary>The sweep an actor's move would make, without moving it. True when something blocks it.</summary>
    public bool TraceMove(Actor actor, FVector start, FVector delta, out FCheckResult hit, Actor? ignore = null, bool bIgnorePawns = false)
    {
        NotNull(actor);
        if (!actor.bCollideWorld && !actor.bCollideActors)
        {
            hit = FCheckResult.NoHit(start + delta);
            return false;
        }

        return Collision.Trace(out hit, actor, ignore, -1, start + delta, start, GetCylinderExtent(actor), MoveTraceFlags(actor, bIgnorePawns),
            bruteForce: false);
    }

    /// <summary>
    /// Moves an actor by <paramref name="Delta"/> with collision (ULevel::MoveActor). See the full form.
    /// <paramref name="Ignore"/> is skipped like the actor itself (an encroacher shoving what it moves into).
    /// </summary>
    public bool MoveActor(Actor Actor, FVector Delta, FRotator NewRotation, out FCheckResult Hit, bool bIgnorePawns = false,
        Actor? Ignore = null) =>
        MoveActorCore(Actor, Delta, NewRotation, out Hit, bTest: false, bIgnorePawns, bIgnoreBases: false, bNoFail: false, Ignore);

    /// <summary>MoveActor without a rotation change or a result.</summary>
    public bool MoveActor(Actor Actor, FVector Delta) => MoveActor(Actor, Delta, Actor.Rotation, out _);

    /// <summary>
    /// ULevel::MoveActor. A zero move only turns the actor; an actor that collides with nothing just moves. Otherwise
    /// the cylinder box sweeps to the first blocking hit that is not a base relation and the move is cut there (unless
    /// <paramref name="bNoFail"/>); encroachers (actors with mesh or brush colliders) are checked for encroachment at
    /// the new pose instead and refuse the move when encroached. Attached actors that are not pawns move along.
    /// Unless <paramref name="bTest"/> the blocking hit is bumped (the other actor first) and touches are updated.
    /// Returns Hit.Time &gt; 0 (true for a partial move), false for a refused encroacher move.
    /// </summary>
    [UcNative("ULevel::MoveActor", Exe = 0x10a1b780, Editor = 0x110bc870)]
    [PortOf("eng:ULevel::MoveActor", Spec = "spec/native/collision-movement.md#moveactoractor-delta-newrotation-hit-btest-bignorepawns-bignorebases-bnofail---bool")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/collision-movement.md MoveActor rules 1 to 8")]
    [Provenance(ProvenanceKind.Estimated, "Touch order and the swept touch test follow stock UE2; the native's touch list is the multi-hit list")]
    public bool MoveActor(Actor Actor, FVector Delta, FRotator NewRotation, out FCheckResult Hit, bool bTest, bool bIgnorePawns,
        bool bIgnoreBases, bool bNoFail) =>
        MoveActorCore(Actor, Delta, NewRotation, out Hit, bTest, bIgnorePawns, bIgnoreBases, bNoFail, null);

    private bool MoveActorCore(Actor actor, FVector delta, FRotator newRotation, out FCheckResult hit, bool bTest, bool bIgnorePawns,
        bool bIgnoreBases, bool bNoFail, Actor? ignore)
    {
        FVector before = actor.Location;
        bool moved = MoveActorBody(actor, delta, newRotation, out hit, bTest, bIgnorePawns, bIgnoreBases, bNoFail, ignore);
        if (!bTest && actor.Location != before)
        {
            ZoneRegions.SetActorZone(actor); // ULevel::MoveActor ends with SetActorZone (spec/devices/zone-info.md rule 2)
        }

        return moved;
    }

    private bool MoveActorBody(Actor actor, FVector delta, FRotator newRotation, out FCheckResult hit, bool bTest, bool bIgnorePawns,
        bool bIgnoreBases, bool bNoFail, Actor? ignore)
    {
        NotNull(actor);
        hit = FCheckResult.NoHit(actor.Location + delta);
        if (actor.bDeleteMe)
        {
            return false;
        }

        bool encroacher = Collision.HasColliders(actor);
        if (delta.IsZero())
        {
            if (newRotation == actor.Rotation)
            {
                return true;
            }

            if (!encroacher && actor.Attached.Count == 0)
            {
                actor.Rotation = newRotation;
                return true;
            }
        }

        if (encroacher)
        {
            return MoveEncroacher(actor, delta, newRotation, bTest, bNoFail);
        }

        FVector start = actor.Location;
        FRotator oldRotation = actor.Rotation;
        if (!actor.bCollideActors && !actor.bCollideWorld)
        {
            actor.Location = start + delta;
            actor.Rotation = newRotation;
            MoveAttached(actor, start, oldRotation, bTest);
            return true;
        }

        if (!delta.IsNearlyZero() && TraceMoveIgnoringBases(actor, start, delta, bIgnorePawns, bIgnoreBases, ignore, out hit) && !bNoFail)
        {
            actor.Location = hit.Location;
        }
        else
        {
            actor.Location = start + delta;
        }

        actor.Rotation = newRotation;
        MoveAttached(actor, start, oldRotation, bTest);
        if (!bTest)
        {
            if (hit.bHit && hit.Actor is { } other && !IsWorldHit(hit) && !IsBaseRelation(actor, other))
            {
                NotifyBump(other, actor);
                NotifyBump(actor, other);
            }

            if (actor.bCollideActors && !actor.bDeleteMe)
            {
                UpdateTouching(actor, start);
            }
        }

        return hit.Time > 0f;
    }

    /// <summary>The move sweep, skipping the actor's base relations (always for an actor based on it, with bIgnoreBases for its own base).</summary>
    private bool TraceMoveIgnoringBases(Actor actor, FVector start, FVector delta, bool bIgnorePawns, bool bIgnoreBases, Actor? ignore,
        out FCheckResult hit)
    {
        FVector extent = GetCylinderExtent(actor);
        ETraceFlags flags = MoveTraceFlags(actor, bIgnorePawns);
        Actor? skip = ignore;
        for (int pass = 0; pass < 4; pass++)
        {
            if (!Collision.Trace(out hit, actor, skip, -1, start + delta, start, extent, flags, bruteForce: false))
            {
                return false;
            }

            Actor? other = hit.Actor;
            bool baseRelation = other is not null && ((bIgnoreBases && IsBasedOn(actor, other)) || (!other.bWorldGeometry && ReferenceEquals(other.Base, actor)));
            if (!baseRelation || other is null || ReferenceEquals(other, skip))
            {
                return true;
            }

            skip = other;
        }

        return Collision.Trace(out hit, actor, skip, -1, start + delta, start, extent, flags, bruteForce: false);
    }

    private static bool IsBaseRelation(Actor actor, Actor other) => ReferenceEquals(actor.Base, other) || ReferenceEquals(other.Base, actor);

    /// <summary>Moves the non-pawn actors attached to <paramref name="actor"/> along (based pawns keep their own physics).</summary>
    private void MoveAttached(Actor actor, FVector oldLocation, FRotator oldRotation, bool bTest)
    {
        if (actor.Attached.Count == 0 || bTest)
        {
            return;
        }

        List<Actor> riders = RentList();
        try
        {
            for (int i = 0; i < actor.Attached.Count; i++)
            {
                Actor a = actor.Attached[i];
                if (!a.bDeleteMe && !a.bIsAPawn && a.AttachmentBone.IsNone && ReferenceEquals(a.Base, actor))
                {
                    riders.Add(a);
                }
            }

            MoveRiders(actor, riders, oldLocation, oldRotation);
        }
        finally
        {
            ReturnList(riders);
        }
    }

    /// <summary>
    /// ULevel::FarMoveActor: a teleport. Unless <paramref name="bNoCheck"/>, an actor that collides with the world
    /// gets FindSpot (which may nudge the destination; failure refuses the move), and unless
    /// <paramref name="bTest"/> the destination is checked for encroachment. A good move sets bJustTeleported
    /// (not for a test), leaves the base unless <paramref name="bAttachedMove"/> and moves the attached actors. The
    /// location is set even for a test: the caller puts it back.
    /// </summary>
    [UcNative("ULevel::FarMoveActor", Editor = 0x110bed60)]
    [PortOf("eng:ULevel::FarMoveActor", Spec = "spec/native/collision-movement.md#farmoveactoractor-dest-btest-bnocheck-battachedmove---bool")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/collision-movement.md FarMoveActor rules 1 to 5")]
    public bool FarMoveActor(Actor Actor, FVector DestLocation, bool bTest = false, bool bNoCheck = false, bool bAttachedMove = false)
    {
        NotNull(Actor);
        if (Actor.bDeleteMe)
        {
            return false;
        }

        FVector dest = DestLocation;
        bool ok = true;
        if (!bNoCheck && (Actor.bCollideWorld || (Actor.bCollideWhenPlacing && Actor.Level.NetMode != ENetMode.NM_Client)))
        {
            ok = FindSpot(Actor, ref dest);
        }

        if (ok && !bTest && !bNoCheck)
        {
            ok = !CheckEncroachment(Actor, dest, Actor.Rotation, bTouchNotify: true);
        }

        if (!ok)
        {
            return false;
        }

        FVector start = Actor.Location;
        if (!bTest)
        {
            Actor.bJustTeleported = true;
            if (!bAttachedMove && Actor.Base is not null)
            {
                Actor.SetBase(null);
            }
        }

        Actor.Location = dest;
        MoveAttached(Actor, start, Actor.Rotation, bTest);
        if (!bTest && Actor.bCollideActors && !Collision.HasColliders(Actor))
        {
            UpdateTouching(Actor, start, swept: false);
        }

        if (!bTest)
        {
            ZoneRegions.SetActorZone(Actor);
        }

        return true;
    }

    /// <summary>SBasePawn.MovePawn: moves the pawn by <paramref name="vDelta"/> with collision (client corrections).</summary>
    [UcNative("ASBasePawn::execMovePawn", Exe = 0x10ae2da0, Editor = 0x111fe4a0)]
    [PortOf("nat:SBase.SBasePawn.MovePawn", Spec = "spec/movement/native-contracts.md#per-native-contracts")]
    [Provenance(ProvenanceKind.Estimated, "Contract inferred from script use (a MoveActor of the delta); the native is not decoded")]
    public bool MovePawn(Pawn Pawn, FVector vDelta)
    {
        System.ArgumentNullException.ThrowIfNull(Pawn);
        return MoveActor(Pawn, vDelta, Pawn.Rotation, out _);
    }

    /// <summary>
    /// SBasePawn.CanUnCrouch: a standing cylinder (the class default) fits with its feet where the crouched pawn's
    /// feet are.
    /// </summary>
    [UcNative("ASBasePawn::execCanUnCrouch", Exe = 0x10acc930, Editor = 0x11206900)]
    [PortOf("nat:SBase.SBasePawn.CanUnCrouch", Spec = "spec/movement/native-contracts.md#per-native-contracts")]
    [Provenance(ProvenanceKind.Estimated, "Contract inferred from script use; the native is not decoded")]
    public bool CanUnCrouch(Pawn Pawn)
    {
        System.ArgumentNullException.ThrowIfNull(Pawn);
        float standH = Pawn is ISBasePawnPhysics sb ? sb.DefaultCollisionHeight : Pawn.CollisionHeight;
        float standR = Pawn is ISBasePawnPhysics sb2 ? sb2.DefaultCollisionRadius : Pawn.CollisionRadius;
        FVector at = Pawn.Location + new FVector(0f, 0f, standH - Pawn.CollisionHeight);
        return !Collision.PointCheck(out _, Pawn, at, new FVector(standR, standR, standH), MoveTraceFlags(Pawn));
    }

    /// <summary>
    /// Changes an actor's location without collision checks beyond the blocking test (UnrealScript SetLocation): fails,
    /// leaving it where it was, when its box would overlap something that blocks it at the new spot.
    /// </summary>
    [UcNative("AActor::execSetLocation", Exe = 0x10a26e10)]
    [PortOf("nat:Engine.Actor.SetLocation", Spec = "docs/PHYSICS.md#moveactor")]
    [Provenance(ProvenanceKind.Estimated, "The script native ends in FarMoveActor in UE2; kept as a strict overlap test until execSetLocation is decoded")]
    public bool SetLocation(Actor Actor, FVector NewLocation)
    {
        NotNull(Actor);
        if ((Actor.bCollideWorld || Actor.bCollideActors) && !Collision.HasColliders(Actor) &&
            Collision.PointCheck(out _, Actor, NewLocation, GetCylinderExtent(Actor), MoveTraceFlags(Actor)))
        {
            return false;
        }

        FVector start = Actor.Location;
        Actor.Location = NewLocation;
        if (Actor.bCollideActors)
        {
            UpdateTouching(Actor, start, swept: false);
        }

        ZoneRegions.SetActorZone(Actor);
        return true;
    }

    /// <summary>
    /// Moves <paramref name="Location"/> to the nearest spot where the actor's box overlaps no blocking world geometry
    /// (ULevel::FindSpot): up first, then out in eight directions, then both, in steps of 4 uu up to
    /// <paramref name="MaxShift"/>. False (location unchanged) when no spot is found.
    /// </summary>
    [UcNative("ULevel::FindSpot", Editor = 0x110bea20)]
    [PortOf("eng:ULevel::FindSpot", Spec = "spec/native/collision-movement.md#open-questions")]
    [Provenance(ProvenanceKind.Estimated, "FindSpot internals are not decoded (collision-movement.md open question 1); the search order here is our own")]
    public bool FindSpot(Actor Actor, ref FVector Location, float MaxShift = 40f) =>
        FindSpot(Actor, ref Location, GetCylinderExtent(Actor), MaxShift);

    /// <summary>FindSpot for a box of <paramref name="Extent"/> (processLanded uses 1.1 times the cylinder).</summary>
    public bool FindSpot(Actor Actor, ref FVector Location, FVector Extent, float MaxShift = 40f)
    {
        NotNull(Actor);
        ETraceFlags flags = MoveTraceFlags(Actor);
        if (!Collision.PointCheck(out _, Actor, Location, Extent, flags))
        {
            return true;
        }

        for (float step = 4f; step <= MaxShift; step += 4f)
        {
            if (TrySpot(Actor, ref Location, new FVector(0f, 0f, step), Extent, flags))
            {
                return true;
            }

            for (int dir = 0; dir < 8; dir++)
            {
                FVector side = new FRotator(0, dir * 8192, 0).Vector() * step;
                if (TrySpot(Actor, ref Location, side, Extent, flags) ||
                    TrySpot(Actor, ref Location, side + new FVector(0f, 0f, step), Extent, flags))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool TrySpot(Actor actor, ref FVector location, FVector offset, FVector extent, ETraceFlags flags)
    {
        if (Collision.PointCheck(out _, actor, location + offset, extent, flags))
        {
            return false;
        }

        location += offset;
        return true;
    }

    /// <summary>Changes an actor's collision flags (UnrealScript SetCollision); turning collision off ends its touches.</summary>
    [UcNative("AActor::execSetCollision", Exe = 0x10a24bf0)]
    [PortOf("nat:Engine.Actor.SetCollision", Spec = "docs/PHYSICS.md#moveactor")]
    public void SetCollision(Actor Actor, bool NewColActors, bool NewBlockActors, bool NewBlockPlayers)
    {
        NotNull(Actor);
        Actor.bCollideActors = NewColActors;
        Actor.bBlockActors = NewBlockActors;
        Actor.bBlockPlayers = NewBlockPlayers;
        Collision.NotifyCollisionChanged();
        if (!NewColActors)
        {
            EndAllTouches(Actor);
        }
        else
        {
            UpdateTouching(Actor, Actor.Location, swept: false);
        }
    }

    /// <summary>Changes an actor's cylinder (UnrealScript SetCollisionSize) and updates its touches.</summary>
    [UcNative("AActor::execSetCollisionSize", Exe = 0x10a24d50)]
    [PortOf("nat:Engine.Actor.SetCollisionSize", Spec = "docs/PHYSICS.md#moveactor")]
    public bool SetCollisionSize(Actor Actor, float NewRadius, float NewHeight)
    {
        NotNull(Actor);
        Actor.CollisionRadius = NewRadius;
        Actor.CollisionHeight = NewHeight;
        Collision.NotifyCollisionChanged();
        if (Actor.bCollideActors)
        {
            UpdateTouching(Actor, Actor.Location, swept: false);
        }

        return true;
    }

    private bool IsWorldHit(in FCheckResult hit)
    {
        if (hit.Actor is LevelInfo || hit.Actor is { bWorldGeometry: true })
        {
            return true;
        }

        if (hit.Collider < 0)
        {
            return false;
        }

        ColliderKind kind = Collision.GetCollider(hit.Collider).Kind;
        return kind is ColliderKind.WorldBsp or ColliderKind.Brush or ColliderKind.MeshPoly or ColliderKind.MeshHull;
    }

    // ---- touches ------------------------------------------------------------------------------------------

    /// <summary>
    /// After a move: begin touching the non-blocking colliding actors the actor overlaps or swept through, then end
    /// the touches it no longer overlaps (MoveActor touch pass).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Stock UE2 MoveActor touch and untouch order")]
    private void UpdateTouching(Actor actor, FVector start, bool swept = true)
    {
        CollisionWorld cw = Collision;
        cw.GetCylinder(actor, out float r, out float h, out bool collide, out _, out _);
        if (!collide)
        {
            return;
        }

        List<Actor> candidates = RentList();
        try
        {
            candidates.AddRange(cw.CylinderActors);
            candidates.AddRange(cw.TouchColliderOwners);
            BeginTouches(actor, candidates, start, r, h, swept);
        }
        finally
        {
            ReturnList(candidates);
        }

        if (actor.bDeleteMe)
        {
            return;
        }

        for (int i = actor.Touching.Count - 1; i >= 0; i--)
        {
            if (i >= actor.Touching.Count)
            {
                continue;
            }

            Actor other = actor.Touching[i];
            if (other.bDeleteMe || !cw.IsOverlapping(actor, other))
            {
                actor.EndTouch(other);
            }
        }
    }

    private void BeginTouches(Actor actor, List<Actor> candidates, FVector start, float r, float h, bool swept)
    {
        CollisionWorld cw = Collision;
        FVector d = actor.Location - start;
        bool sweepTest = swept && !d.IsZero() && !cw.HasColliders(actor);
        for (int i = 0; i < candidates.Count; i++)
        {
            Actor other = candidates[i];
            if (ReferenceEquals(other, actor) || other.bDeleteMe || actor.Touching.Contains(other))
            {
                continue;
            }

            bool otherMesh = cw.HasColliders(other);
            bool otherCollide = true;
            bool otherBlockActors = false;
            bool otherBlockPlayers = false;
            float r2 = 0f;
            float h2 = 0f;
            if (!otherMesh)
            {
                cw.GetCylinder(other, out r2, out h2, out otherCollide, out otherBlockActors, out otherBlockPlayers);
            }

            // A volume from TouchColliderOwners collides and blocks nothing.
            if (!otherCollide || CollisionWorld.ActorBlocks(actor, other, otherBlockActors, otherBlockPlayers))
            {
                continue;
            }

            // Swept through: the move enters the other's cylinder (the same primitive test as the move's own sweep,
            // UPrimitive::LineCheck), so the touch pass and the end-of-move overlap test agree on the cylinder; a box
            // round it touched and untouched a pawn walking through its corners on every move.
            bool touch = cw.IsOverlapping(actor, other);
            if (!touch && sweepTest && !otherMesh)
            {
                touch = CollisionMath.CylinderCheck(other.Location, r2, h2, start, d, new FVector(r, r, h), 1f, out _, out _, out _);
            }

            if (touch)
            {
                actor.BeginTouch(other);
                if (actor.bDeleteMe)
                {
                    return;
                }
            }
        }
    }

    private static void EndAllTouches(Actor actor)
    {
        for (int i = actor.Touching.Count - 1; i >= 0; i--)
        {
            if (i < actor.Touching.Count)
            {
                actor.EndTouch(actor.Touching[i]);
            }
        }
    }
}
