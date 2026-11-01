using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Physics.Movement;

public sealed partial class PhysicsDriver
{
    /// <summary>
    /// PHYS_MovingBrush for an <see cref="IMovingBrush"/> mover: while bInterpolating, PhysAlpha advances by
    /// dt x PhysRate (clamped to 1, the rest of the time carried into the next leg), the eased alpha (3a^2 - 2a^3
    /// for MV_GlideByTime) places the mover between OldPos/OldRot and the target key, and the move is made with
    /// encroachment checks. A refused move leaves the alpha where it was this tick; reaching 1 ends the leg and
    /// calls KeyFrameReached.
    /// </summary>
    [UcNative("AActor::physMovingBrush")]
    [PortOf("nat:AActor::physMovingBrush", Spec = "spec/devices/mover.md#interpolation")]
    [Provenance(ProvenanceKind.Estimated, "Mover spec rule 2 (estimated native contract, UE2 lineage)")]
    internal void physMovingBrush(Actor actor, float dt)
    {
        if (actor is not IMovingBrush mover)
        {
            return;
        }

        for (int legs = 0; actor.bInterpolating && dt > 0f && legs < 8 && !actor.bDeleteMe; legs++)
        {
            float alpha = mover.PhysAlpha;
            float newAlpha = alpha + dt * mover.PhysRate;
            if (newAlpha > 1f)
            {
                dt *= (newAlpha - 1f) / (newAlpha - alpha);
                newAlpha = 1f;
            }
            else
            {
                dt = 0f;
            }

            float s = mover.MoverGlideType == EMoverGlideType.MV_GlideByTime
                ? 3f * newAlpha * newAlpha - 2f * newAlpha * newAlpha * newAlpha
                : newAlpha;
            int key = mover.KeyNum;
            FVector target = mover.OldPos + (mover.BasePos + mover.GetKeyPos(key) - mover.OldPos) * s;
            FRotator rotation = mover.OldRot + (mover.BaseRot + mover.GetKeyRot(key) - mover.OldRot) * s;
            if (!MoveActor(actor, target - actor.Location, rotation, out _))
            {
                return;
            }

            mover.PhysAlpha = newAlpha;
            if (newAlpha >= 1f)
            {
                actor.bInterpolating = false;
                mover.eventKeyFrameReached();
            }
        }
    }

    /// <summary>
    /// MoveActor for an actor that collides by mesh or brush colliders (a static-mesh mover). With bCollideActors
    /// (not a test, not bNoFail) the new pose is checked for encroachment first; an encroached pose is refused and
    /// nothing moves. Otherwise the actor takes the pose and its attachments and based pawn riders move along.
    /// </summary>
    [UcNative("ULevel::MoveActor", Exe = 0x10a1b780, Editor = 0x110bc870)]
    [PortOf("eng:ULevel::MoveActor", Spec = "spec/native/collision-movement.md#moveactoractor-delta-newrotation-hit-btest-bignorepawns-bignorebases-bnofail---bool")]
    [Provenance(ProvenanceKind.Decoded, "MoveActor rules 5 and 6 for static-mesh movers")]
    internal bool MoveEncroacher(Actor actor, FVector delta, FRotator newRotation, bool bTest = false, bool bNoFail = false)
    {
        if (delta.IsZero() && newRotation == actor.Rotation)
        {
            return true;
        }

        FVector oldLocation = actor.Location;
        FRotator oldRotation = actor.Rotation;
        FVector newLocation = oldLocation + delta;
        if (actor.bCollideActors && !bTest && !bNoFail && CheckEncroachment(actor, newLocation, newRotation, bTouchNotify: true))
        {
            return false;
        }

        if (actor.bDeleteMe)
        {
            return false;
        }

        actor.Location = newLocation;
        actor.Rotation = newRotation;
        MoveAttached(actor, oldLocation, oldRotation, bTest);
        if (actor.bCollideActors && !bTest)
        {
            UpdateTouching(actor, oldLocation, swept: false);
        }

        return true;
    }

    /// <summary>
    /// ULevel::CheckEncroachment: true when <paramref name="Actor"/> at the test pose is refused. Every colliding
    /// cylinder actor it would overlap and that it blocks is first shoved by the encroacher's displacement (plus 1.5
    /// times the offset its rotation gives the other's position) when the encroacher is a mover with
    /// bTryToMoveEncroachedActor; a shove that leaves it overlapping is undone. What still overlaps goes to the
    /// encroacher's EncroachingOn (true refuses). After an accepted check, touches the actor no longer overlaps end
    /// (with <paramref name="bTouchNotify"/>), every overlapped blocker gets EncroachedBy and the others BeginTouch.
    /// </summary>
    [UcNative("ULevel::CheckEncroachment", Exe = 0x10a1c8d0, Editor = 0x110bda90)]
    [PortOf("eng:ULevel::CheckEncroachment", Spec = "spec/native/collision-movement.md#checkencroachmentactor-location-rotation-btouchnotify---bool-true--refused")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/collision-movement.md CheckEncroachment rules 1 and 2, mask 0x9f, shove factor 1.5")]
    [Provenance(ProvenanceKind.Estimated, "moveSmooth is a MoveActor plus one slide (not decoded); a pawn's own EncroachingOn is reached through IEncroacher only")]
    public bool CheckEncroachment(Actor Actor, FVector TestLocation, FRotator TestRotation, bool bTouchNotify)
    {
        NotNull(Actor);
        if (!Actor.bCollideActors || Actor.bDeleteMe)
        {
            return false;
        }

        bool encroacher = Collision.HasColliders(Actor);
        FVector oldLocation = Actor.Location;
        FRotator oldRotation = Actor.Rotation;
        Actor.Location = TestLocation;
        Actor.Rotation = TestRotation;
        List<Actor> overlapped = RentList();
        try
        {
            CollectEncroached(Actor, encroacher, overlapped);
            FVector displacement = TestLocation - oldLocation;
            bool rotated = TestRotation != oldRotation;
            FCoords oldAxes = rotated ? FCoords.FromRotator(oldRotation) : default;
            FCoords newAxes = rotated ? FCoords.FromRotator(TestRotation) : default;
            for (int i = 0; i < overlapped.Count; i++)
            {
                Actor other = overlapped[i];
                if (other.bDeleteMe)
                {
                    continue;
                }

                bool still = true;
                if (encroacher && Actor.bTryToMoveEncroachedActor)
                {
                    FVector shove = displacement;
                    if (rotated)
                    {
                        FVector rel = other.Location - oldLocation;
                        var local = new FVector(rel | oldAxes.XAxis, rel | oldAxes.YAxis, rel | oldAxes.ZAxis);
                        FVector turned = newAxes.XAxis * local.X + newAxes.YAxis * local.Y + newAxes.ZAxis * local.Z;
                        shove += (turned - rel) * 1.5f;
                    }

                    FVector before = other.Location;
                    MoveSmooth(other, shove + shove.SafeNormal() * K.TraceSkin, Actor);
                    still = OverlapsForEncroach(Actor, encroacher, other);
                    if (still && !other.bDeleteMe)
                    {
                        MoveActor(other, before - other.Location, other.Rotation, out _, Ignore: Actor);
                    }
                }

                if (still && EncroachingOn(Actor, other))
                {
                    return true;
                }

                if (Actor.bDeleteMe)
                {
                    return true;
                }
            }

            if (bTouchNotify)
            {
                for (int i = Actor.Touching.Count - 1; i >= 0; i--)
                {
                    if (i < Actor.Touching.Count && !Collision.IsOverlapping(Actor, Actor.Touching[i]))
                    {
                        Actor.EndTouch(Actor.Touching[i]);
                    }
                }
            }

            for (int i = 0; i < overlapped.Count; i++)
            {
                Actor other = overlapped[i];
                if (other.bDeleteMe || !OverlapsForEncroach(Actor, encroacher, other))
                {
                    continue;
                }

                other.EncroachedBy(Actor);
                (other as IEncroachable)?.eventEncroachedBy(Actor);
            }

            return false;
        }
        finally
        {
            ReturnList(overlapped);
            if (!Actor.bDeleteMe)
            {
                Actor.Location = oldLocation;
                Actor.Rotation = oldRotation;
            }
        }
    }

    /// <summary>The colliding cylinder actors the actor overlaps at its current pose that block it (ActorEncroachmentCheck).</summary>
    private void CollectEncroached(Actor actor, bool encroacher, List<Actor> output)
    {
        List<Actor> cylinders = Collision.CylinderActors;
        for (int i = 0; i < cylinders.Count; i++)
        {
            Actor other = cylinders[i];
            // A mover carries its riders after accepting the pose. Shoving or crushing them here would apply
            // the platform's motion twice (and can refuse rotation when a rider overlaps its rail or floor).
            if (ReferenceEquals(other, actor) || other.bDeleteMe || (actor.bIsAMover && IsBasedOn(other, actor)))
            {
                continue;
            }

            Collision.GetCylinder(other, out _, out _, out bool collide, out bool blockActors, out bool blockPlayers);
            if (!collide || !EncroacherBlocks(actor, encroacher, other, blockActors, blockPlayers) || !OverlapsForEncroach(actor, encroacher, other))
            {
                continue;
            }

            output.Add(other);
        }
    }

    private bool OverlapsForEncroach(Actor actor, bool encroacher, Actor other)
    {
        if (!encroacher)
        {
            return Collision.IsOverlapping(actor, other);
        }

        Collision.GetCylinder(other, out float r, out float h, out _, out _, out _);
        return Collision.OverlapsActorColliders(actor, other.Location, new FVector(r, r, h));
    }

    /// <summary>The encroacher's EncroachingOn: an <see cref="IEncroacher"/>'s answer, a Mover's state dispatch, else false (Actor).</summary>
    private static bool EncroachingOn(Actor actor, Actor other) => actor switch
    {
        IEncroacher e => e.eventEncroachingOn(other),
        Mover m => m.eventEncroachingOn(other),
        _ => false,
    };

    /// <summary>A move that slides once along what it hits (UE2 moveSmooth).</summary>
    [Provenance(ProvenanceKind.Estimated, "AActor::moveSmooth not decoded: a MoveActor, then the rest along the hit surface")]
    private void MoveSmooth(Actor actor, FVector delta, Actor ignore)
    {
        MoveActor(actor, delta, actor.Rotation, out FCheckResult hit, Ignore: ignore);
        if (!hit.bHit || actor.bDeleteMe)
        {
            return;
        }

        FVector slide = (delta - hit.Normal * (delta | hit.Normal)) * (1f - hit.Time);
        if (!slide.IsNearlyZero())
        {
            MoveActor(actor, slide, actor.Rotation, out _, Ignore: ignore);
        }
    }

    /// <summary>True when the encroacher blocks <paramref name="other"/> (IsBlockedBy for an encroacher and an actor).</summary>
    private bool EncroacherBlocks(Actor encroacher, bool hasColliders, Actor other, bool otherBlockActors, bool otherBlockPlayers)
    {
        if (!hasColliders)
        {
            return CollisionWorld.ActorBlocks(encroacher, other, otherBlockActors, otherBlockPlayers);
        }

        bool blockActors = false;
        bool blockPlayers = false;
        for (int ci = Collision.FirstColliderOf(encroacher); ci >= 0; ci = Collision.NextColliderOf(ci))
        {
            ColliderState c = Collision.ColliderAt(ci);
            if (!c.Enabled)
            {
                continue;
            }

            CollisionWorld.GetColliderFlags(c, out _, out bool ba, out bool bp);
            blockActors |= ba;
            blockPlayers |= bp;
        }

        bool encroacherBlocksOther = other.bIsAPawn ? blockPlayers : blockActors;
        return encroacherBlocksOther && (otherBlockActors || otherBlockPlayers);
    }

    private void MoveRiders(Actor baseActor, List<Actor> riders, FVector oldLocation, FRotator oldRotation)
    {
        FVector delta = baseActor.Location - oldLocation;
        bool rotated = baseActor.Rotation != oldRotation;
        FCoords oldAxes = rotated ? FCoords.FromRotator(oldRotation) : default;
        FCoords newAxes = rotated ? FCoords.FromRotator(baseActor.Rotation) : default;
        for (int i = 0; i < riders.Count; i++)
        {
            Actor rider = riders[i];
            if (rider.bDeleteMe || !ReferenceEquals(rider.Base, baseActor))
            {
                continue;
            }

            FVector move = delta;
            FRotator rotation = rider.Rotation;
            if (rotated)
            {
                FVector rel = rider.Location - oldLocation;
                var local = new FVector(rel | oldAxes.XAxis, rel | oldAxes.YAxis, rel | oldAxes.ZAxis);
                FVector moved = newAxes.XAxis * local.X + newAxes.YAxis * local.Y + newAxes.ZAxis * local.Z;
                move = baseActor.Location + moved - rider.Location;
                rotation.Yaw += baseActor.Rotation.Yaw - oldRotation.Yaw;
            }

            MoveActorCore(rider, move, rotation, out _, bTest: false, bIgnorePawns: false, bIgnoreBases: true, bNoFail: false, baseActor);
        }
    }
}
