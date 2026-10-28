using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Physics.Movement;

public sealed partial class PhysicsDriver
{
    /// <summary>
    /// PHYS_Falling for an SBasePawn (ASBasePawn::physFalling): the out-of-world test, air control (Acceleration.XY
    /// clamped to AirControl x AccelRate), the wall pre-probe, the authority's drop-attack homing and trigger, then
    /// sub-steps with the half-step integrator under a fixed gravity of -1500, the homing pull, the 417 uu/s horizontal
    /// cap, wall slides, landing through bCanLand, the narrow-top side probes, the velocity rewrite with the
    /// extrapolated Z and the 2500 uu/s cap. The apex is raised in fZMaxFalling; Acceleration is restored.
    /// </summary>
    [UcNative("ASBasePawn::physFalling", Exe = 0x10ad3dc0, Editor = 0x11213090)]
    [PortOf("eng:ASBasePawn::physFalling", Spec = "spec/native/physics-falling.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-falling.md sections 1 to 8; ANSWERS-physics 3, 4, 5")]
    public void physFalling(Pawn Pawn, float deltaTime, int Iterations)
    {
        NotNull(Pawn);
        Pawn p = Pawn;
        var sb = p as ISBasePawnPhysics;
        Controller? ctrl = p.Controller;

        // 1. Out of the world.
        float death = p.Level.fDeathPawnHeight;
        if (death != 0f && p.Location.Z < death && IsControllingSide(ctrl))
        {
            (ctrl as IPhysicsEvents)?.eventFellOutOfWorld();
            if (p.bDeleteMe)
            {
                return;
            }
        }

        // 2. Air control.
        FVector a0 = p.Acceleration;
        p.Acceleration.Z = 0f;
        float maxAccel = p.AirControl * p.AccelRate;
        float s2d = p.Velocity.Size2D();
        if (s2d < K.Fall.AirControlBoostSpeed && p.AirControl > 0f)
        {
            maxAccel += (K.Fall.AirControlBoostSpeed - s2d) / deltaTime;
        }
        else if (p.AirControl < K.Fall.AirControlMin)
        {
            maxAccel = 0f;
        }

        if (p.Acceleration.SizeSquared() > maxAccel * maxAccel)
        {
            p.Acceleration = p.Acceleration.SafeNormal() * maxAccel;
        }

        // 3. Wall pre-probe.
        FallingPreProbe(p, deltaTime, out bool bHitWall, out FVector wallNormal, out bool bSecondWall);
        if (p.bDeleteMe)
        {
            return;
        }

        // 4. Drop-attack homing and trigger.
        if (sb is not null && p.Role == ENetRole.ROLE_Authority)
        {
            DropAttackScan(p, sb, rootMotion: false);
            if (p.bDeleteMe)
            {
                return;
            }
        }

        // 5. Sub-steps.
        float remaining = deltaTime;
        bool bSlidAgain = false;
        while (remaining > 0f && Iterations < K.MaxPhysicsIterations)
        {
            Iterations++;
            float tick = remaining <= K.MaxSubStep ? remaining : SvmMath.Min(K.MaxSubStep, remaining * 0.5f);
            remaining -= tick;
            FVector oldLocation = p.Location;
            p.bJustTeleported = false;
            float vz0 = p.Velocity.Z;

            FVector v = p.Velocity + (p.Acceleration + new FVector(0f, 0f, K.DefaultGravity)) * (0.5f * tick);
            if (sb?.m_AttackTarget is { bDeleteMe: false } target)
            {
                FVector pull = target.Location - p.Location;
                float plen = pull.Size();
                if (plen > K.Fall.HomingPull)
                {
                    pull.X *= K.Fall.HomingPull / plen;
                    pull.Y *= K.Fall.HomingPull / plen;
                }

                float k = SvmMath.Min(K.Fall.HomingGainCap, K.Fall.HomingGainScale * (target.Velocity.Size() + K.Fall.HomingGainAdd));
                v.X = 0.5f * v.X + 0.5f * k * pull.X;
                v.Y = 0.5f * v.Y + 0.5f * k * pull.Y;
            }

            float vxy = SvmMath.Sqrt(v.X * v.X + v.Y * v.Y);
            if (vxy > K.Fall.AirSpeedCap)
            {
                v.X *= K.Fall.AirSpeedCap / vxy;
                v.Y *= K.Fall.AirSpeedCap / vxy;
            }

            p.Velocity = v;
            FVector delta = v * tick;
            FVector carry = default;
            if (delta.X * delta.X + delta.Y * delta.Y < K.Walk.TinyMoveSq)
            {
                carry = new FVector(delta.X, delta.Y, 0f);
                delta.X = 0f;
                delta.Y = 0f;
            }

            float vzBefore = p.Velocity.Z;
            MoveActor(p, delta, p.Rotation, out FCheckResult hit);
            if (p.bDeleteMe)
            {
                return;
            }

            if (hit.bHit)
            {
                if (hit.Actor is ISBasePawnPhysics && p.Velocity.Z != vzBefore)
                {
                    return;
                }

                if (hit.Normal.Z < K.MINFLOORZ)
                {
                    if (FallingWallSlide(p, ref hit, delta, ref bHitWall, ref wallNormal, bSecondWall, ref bSlidAgain, Iterations))
                    {
                        return;
                    }
                }
                else
                {
                    float left = (1f - hit.Time) * tick + remaining;
                    if (!p.bJustTeleported && hit.Time > K.Fall.VelocityRewriteMinFraction && tick * hit.Time > K.Fall.VelocityRewriteMinTime)
                    {
                        p.Velocity = (p.Location - oldLocation) * (1f / (tick * hit.Time));
                    }

                    // The time after the hit stays in the loop: without a landing the fall goes on with it.
                    remaining = left;
                    p.GroundSpeed = p.Velocity.Size2D();
                    if (bCanLand(p, hit.Normal, hit.Actor, left, Iterations))
                    {
                        return;
                    }

                    if (NarrowTopProbes(p, hit, left, Iterations, bHitWall, wallNormal))
                    {
                        return;
                    }
                }

                if (p.bDeleteMe)
                {
                    return;
                }
            }

            // 7. Velocity of the step.
            if (!p.bJustTeleported)
            {
                p.Velocity = (p.Location + carry - oldLocation) * (1f / tick);
                if (p.Velocity.Z < vz0 || vz0 >= 0f)
                {
                    p.Velocity.Z = 2f * p.Velocity.Z - vz0;
                }

                if (p.Velocity.SizeSquared() > K.TerminalVelocity * K.TerminalVelocity)
                {
                    p.Velocity = p.Velocity.SafeNormal() * K.TerminalVelocity;
                }
            }
        }

        // 8. Apex and the script's acceleration.
        if (p.Location.Z > ZMaxFalling(p))
        {
            SetZMaxFalling(p, p.Location.Z);
        }

        p.Acceleration = a0;
    }

    /// <summary>
    /// The falling wall pre-probe (section 3): like walking's, unlifted, with the box (R - 0.2, R - 0.2, H - 10) and no
    /// parallel-wall skip. The first wall removes its normal part from Acceleration and Velocity; a second one stops
    /// the horizontal motion.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-falling.md section 3")]
    private void FallingPreProbe(Pawn p, float dt, out bool bHitWall, out FVector wallNormal, out bool bSecondWall)
    {
        bHitWall = false;
        bSecondWall = false;
        wallNormal = default;
        FVector d = (p.Velocity + p.Acceleration * (0.5f * dt)) * dt;
        d.Z = 0f;
        if (d.IsZero())
        {
            return;
        }

        FVector r = GetCylinderExtent(p);
        var e = new FVector(r.X - K.Walk.ProbeOffset, r.Y - K.Walk.ProbeOffset, r.Z - K.Fall.WallProbeHeightShrink);
        FVector s = p.Location;
        if (ProbeSides(p, s, e, d, d.SafeNormal(), K.Flags.WallProbe, parallelSkip: false, out FCheckResult h) != ProbeResult.Counting)
        {
            return;
        }

        WallHit(p, ref h);
        if (p.bDeleteMe)
        {
            return;
        }

        bHitWall = true;
        wallNormal = h.Normal;
        p.Acceleration -= h.Normal * (h.Normal | p.Acceleration);
        p.Velocity -= h.Normal * (h.Normal | p.Velocity);
        d = (p.Velocity + p.Acceleration * (0.5f * dt)) * dt;
        d.Z = 0f;
        if (d.IsZero())
        {
            return;
        }

        if (ProbeSides(p, s, e, d, d.SafeNormal(), K.Flags.WallProbe, parallelSkip: false, out FCheckResult h2) == ProbeResult.Counting)
        {
            WallHit(p, ref h2);
            p.Velocity.X = 0f;
            p.Velocity.Y = 0f;
            p.Acceleration.X = 0f;
            p.Acceleration.Y = 0f;
            bSecondWall = true;
        }
    }

    /// <summary>A wall hit while falling (section 5.6): processHitWall and the slides. True when the call is over (landed).</summary>
    private bool FallingWallSlide(Pawn p, ref FCheckResult hit, FVector delta, ref bool bHitWall, ref FVector wallNormal, bool bSecondWall,
        ref bool bSlidAgain, int iterations)
    {
        processHitWall(p, ref hit.Normal, hit.Actor);
        if (p.bDeleteMe)
        {
            return true;
        }

        FVector n = hit.Normal;
        FVector slide = (delta - n * (delta | n)) * (1f - hit.Time);
        if (bHitWall && ((n | wallNormal) < 0f || bSecondWall))
        {
            slide = delta;
            TwoWallAdjust(delta, ref slide, n, wallNormal, hit.Time);
        }

        bHitWall = true;
        wallNormal = n;
        if ((slide | delta) < 0f)
        {
            return false;
        }

        FVector m = slide;
        if (m.X * m.X + m.Y * m.Y < K.Walk.TinyMoveSq)
        {
            m.X = 0f;
            m.Y = 0f;
        }

        MoveActor(p, m, p.Rotation, out FCheckResult second);
        if (p.bDeleteMe)
        {
            return true;
        }

        if (!second.bHit)
        {
            return false;
        }

        if (second.Normal.Z >= K.MINFLOORZ)
        {
            processLanded(p, second.Normal, second.Actor, 0f, iterations);
            return true;
        }

        processHitWall(p, ref second.Normal, second.Actor);
        if (p.bDeleteMe)
        {
            return true;
        }

        if (!bSlidAgain)
        {
            bSlidAgain = true;
            FVector oldN = n;
            TwoWallAdjust(delta.SafeNormal(), ref slide, second.Normal, oldN, second.Time);
            MoveActor(p, slide, p.Rotation, out FCheckResult third);
            if (p.bDeleteMe)
            {
                return true;
            }

            bool trough = second.Normal.Z > 0f && oldN.Z > 0f && (second.Normal | oldN) < 0f && slide.Z == 0f;
            if ((third.bHit && third.Normal.Z >= K.MINFLOORZ) || trough)
            {
                FCheckResult landed = third.bHit ? third : second;
                processLanded(p, landed.Normal, landed.Actor, 0f, iterations);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The narrow-top side probes (section 6), when bCanLand found no ground under the feet: probes a little to each
    /// side for a steep surface to slide off. True when the call is over.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-falling.md section 6: start 0.2 H + 8 down, box 0.9 (R, R, H), reach 0.2 R, slope 0.3, push 0.1 R, flags 0x97")]
    private bool NarrowTopProbes(Pawn p, in FCheckResult floor, float remaining, int iterations, bool bHitWall, FVector wallNormal)
    {
        FVector r = GetCylinderExtent(p);
        FVector box = r * K.Fall.LandingBoxScale;
        FVector s = p.Location - new FVector(0f, 0f, K.Fall.LandingDepthScaleH * r.Z + K.Fall.LandingDepthAdd);
        FVector u;
        if (bHitWall)
        {
            u = -wallNormal;
        }
        else
        {
            var vxy = new FVector(p.Velocity.X, p.Velocity.Y, 0f);
            u = vxy.IsZero() ? new FVector(1f, 0f, 0f) : -vxy.SafeNormal();
        }

        var perp = new FVector(-u.Y, u.X, 0f);
        float reach = K.Fall.SideProbeReachR * r.X;
        for (int i = 0; i < 4; i++)
        {
            FVector dir = i switch
            {
                0 => u,
                1 => -u,
                2 => perp,
                _ => -perp,
            };
            if (!Collision.LineCheck(out FCheckResult side, p, s + dir * reach, s, box, K.Flags.SideProbe) ||
                side.Normal.Z >= K.Fall.SideProbeSlopeZ)
            {
                continue;
            }

            MoveActor(p, side.Normal * (K.Fall.SideProbePushR * r.X), p.Rotation, out FCheckResult push);
            if (p.bDeleteMe)
            {
                return true;
            }

            if (push.bHit && push.Time < K.Fall.SideProbeBlockedTime)
            {
                processLanded(p, floor.Normal, floor.Actor, remaining, iterations);
                return true;
            }

            return false;
        }

        processLanded(p, floor.Normal, floor.Actor, remaining, iterations);
        return true;
    }

    /// <summary>
    /// ASBasePawn::bCanLand: a box 0.9 (R, R, H) probe 0.2 H + 8 below the pawn must hit. On the controlling side the
    /// controller gets MyNotifyLanded (its script plays the landing, which sets PHYS_Walking); a walking pawn's
    /// Acceleration becomes unit length, and the rest of the frame runs in the new mode unless it is RootMotion.
    /// Returns true whenever the probe hits (the server copy of a remote player stays in Falling on the floor).
    /// A controller without <see cref="IPhysicsEvents"/> lands the stock way (Landed, PHYS_Walking).
    /// </summary>
    [UcNative("ASBasePawn::bCanLand", Exe = 0x10ae4390, Editor = 0x11210450)]
    [PortOf("eng:ASBasePawn::bCanLand", Spec = "spec/native/physics-falling.md#7-bcanlandhitnormal-hitactor-remaining-iterations-and-processlanded")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-falling.md section 7; ANSWERS-physics 5")]
    internal bool bCanLand(Pawn p, FVector hitNormal, Actor? hitActor, float remaining, int iterations)
    {
        if (!LandingProbe(p))
        {
            return false;
        }

        Controller? ctrl = p.Controller;
        if (ctrl is IPhysicsEvents events)
        {
            if (IsControllingSide(ctrl))
            {
                p.Floor = hitNormal;
                events.eventMyNotifyLanded(hitNormal, remaining, hitActor);
                AfterLanded(p, remaining, iterations);
            }

            return true;
        }

        StockLanded(p, hitNormal, hitActor, remaining, iterations);
        return true;
    }

    /// <summary>
    /// ASBasePawn::processLanded (the wall-slide landings): when the landing probe misses, FindSpot with 1.1 times the
    /// cylinder may move the pawn instead (no landing then). Otherwise Floor, the controller's MyNotifyLanded (no
    /// role test), unit Acceleration when walking and the rest of the frame unless RootMotion.
    /// </summary>
    [UcNative("ASBasePawn::processLanded", Exe = 0x10ae4500, Editor = 0x11210660)]
    [PortOf("eng:ASBasePawn::processLanded", Spec = "spec/native/physics-falling.md#7-bcanlandhitnormal-hitactor-remaining-iterations-and-processlanded")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-falling.md section 7")]
    internal void processLanded(Pawn p, FVector hitNormal, Actor? hitActor, float remaining, int iterations)
    {
        if (!LandingProbe(p))
        {
            FVector spot = p.Location;
            FVector extent = GetCylinderExtent(p) * K.Fall.FindSpotScale;
            if (FindSpot(p, ref spot, extent) && spot != p.Location)
            {
                FVector before = p.Location;
                FarMoveActor(p, spot);
                if (p.Location != before)
                {
                    return;
                }
            }
        }

        if (p.Controller is IPhysicsEvents events)
        {
            p.Floor = hitNormal;
            events.eventMyNotifyLanded(hitNormal, remaining, hitActor);
            AfterLanded(p, remaining, iterations);
            return;
        }

        StockLanded(p, hitNormal, hitActor, remaining, iterations);
    }

    private bool LandingProbe(Pawn p)
    {
        FVector r = GetCylinderExtent(p);
        FVector at = p.Location;
        return Collision.LineCheck(out _, p, at - new FVector(0f, 0f, K.Fall.LandingDepthScaleH * r.Z + K.Fall.LandingDepthAdd), at,
            r * K.Fall.LandingBoxScale, K.Flags.Landing);
    }

    private void AfterLanded(Pawn p, float remaining, int iterations)
    {
        if (p.bDeleteMe)
        {
            return;
        }

        if (p.Physics == EPhysics.PHYS_Walking)
        {
            p.Acceleration = p.Acceleration.SafeNormal();
        }

        if (p.Physics != EPhysics.PHYS_RootMotion)
        {
            startNewPhysics(p, remaining, iterations);
        }
    }

    /// <summary>The stock UE2 landing for a pawn whose controller takes no SCCT events: Landed, PHYS_Walking, the rest of the frame.</summary>
    [Provenance(ProvenanceKind.Estimated, "Stock UE2 APawn::processLanded, kept for pawns outside the SBase port")]
    private void StockLanded(Pawn p, FVector hitNormal, Actor? hitActor, float remaining, int iterations)
    {
        p.eventLanded(hitNormal);
        if (p.bDeleteMe)
        {
            return;
        }

        if (p.Physics == EPhysics.PHYS_Falling)
        {
            setPhysics(p, EPhysics.PHYS_Walking, hitActor, hitNormal);
        }

        p.bJustLanded = true;
        if (p.Physics == EPhysics.PHYS_Walking)
        {
            p.Velocity.Z = 0f;
        }

        startNewPhysics(p, remaining, iterations);
    }

    /// <summary>
    /// The drop-attack scan (section 4, and the root-motion variant): every SBasePawn of the other team that its
    /// controller lets be drop-attacked, last to first. Homing picks the target best lined up with the fall
    /// (cosine at least 0.92, in sight); the trigger, when the fall would not kill, raises StickyBlast (sticky-skinned
    /// target, the scan goes on) or DropAttack. True when a trigger ends the scan. In the plain fall Reloaded's fix
    /// (rl:fix.jump_lockon) applies: homing ignores targets 750 or more below, and both sight lines ignore volumes and
    /// other non-world actors.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-falling.md section 4; physics-rootmotion-repositioning.md physFallingInRootMotion 2")]
    [PortOf("rl:fix.jump_lockon", Spec = "spec/native/physics-falling.md#4-drop-attack-homing-and-trigger-authority-only")]
    [Provenance(ProvenanceKind.Estimated, "StickyBlast receives this pawn's Base as the spec reads; a DropAttack ends the falling scan (the spec does not say)")]
    private bool DropAttackScan(Pawn p, ISBasePawnPhysics sb, bool rootMotion)
    {
        if (!rootMotion)
        {
            sb.m_AttackTarget = null;
        }

        float best = K.Fall.HomingCos;
        Controller? ctrl = p.Controller;
        FVector fwd = p.Rotation.Vector();
        float h = p.CollisionHeight;
        ETraceFlags sightFlags = rootMotion ? K.Flags.DropAttackSight : K.Flags.DropAttackSightReloaded;
        IReadOnlyList<Actor> actors = p.XLevel.Actors;
        for (int i = actors.Count - 1; i >= 0 && !p.bDeleteMe; i--)
        {
            if (i >= actors.Count || actors[i] is not Pawn other || other is not ISBasePawnPhysics otherSb || ReferenceEquals(other, p) ||
                other.bDeleteMe || other.Controller is not ISPlayerControllerPhysics otherPc || otherSb.SPawnType == sb.SPawnType ||
                !otherPc.eventCanBeDropAttacked())
            {
                continue;
            }

            FVector loc = p.Location;
            FVector oloc = other.Location;
            if (!rootMotion && oloc.Z + K.Fall.HomingHeight < loc.Z && p.Velocity.Z < 0f &&
                (SvmMath.Abs(p.Velocity.X) > K.Fall.HomingMinSpeed || SvmMath.Abs(p.Velocity.Y) > K.Fall.HomingMinSpeed) &&
                loc.Z - oloc.Z < K.Fall.HomingMaxDrop)
            {
                FVector t = (oloc - loc).SafeNormal();
                FVector v = p.Velocity.SafeNormal() + fwd;
                v.Z -= K.Fall.HomingDown;
                v = v.SafeNormal();
                float cos = t | v;
                if (cos >= best && !Collision.LineCheck(out _, p, oloc + new FVector(0f, 0f, K.Fall.HomingSightRaise), loc, default,
                        sightFlags))
                {
                    best = cos;
                    sb.m_AttackTarget = other;
                }
            }

            if (!FallWouldNotKill(p, sb))
            {
                continue;
            }

            float dz = loc.Z - oloc.Z;
            bool inWindow = rootMotion
                ? dz >= K.Fall.TriggerAboveRootMotion && (loc.Z - h) - oloc.Z + other.CollisionHeight > K.Fall.TriggerOverlapRootMotion
                : dz < K.Fall.TriggerAboveH * h && (loc.Z - h) - oloc.Z + other.CollisionHeight > K.Fall.TriggerOverlap;
            if (FVector.Dist(loc, oloc) >= K.Fall.TriggerReachH * h || (fwd | (oloc - loc)) <= K.Fall.TriggerFacing || !inWindow ||
                p.Velocity.Z >= K.Fall.TriggerSpeedZ)
            {
                continue;
            }

            if (Collision.LineCheck(out FCheckResult sight, p, loc, oloc, default, sightFlags) && !ReferenceEquals(sight.Actor, other))
            {
                continue;
            }

            if (otherSb.bStickySkinned)
            {
                (ctrl as ISPlayerControllerPhysics)?.eventStickyBlast(p.Base);
                if (rootMotion)
                {
                    return true;
                }

                continue;
            }

            (ctrl as IPhysicsEvents)?.eventDropAttack(other);
            return true;
        }

        return false;
    }

    /// <summary>The drop attack is allowed when the fall so far would not kill (fall damage below the remaining health).</summary>
    private bool FallWouldNotKill(Pawn p, ISBasePawnPhysics sb)
    {
        float fall = ZMaxFalling(p) - p.Location.Z;
        float min = sb.Fell_MinHeightForDmg;
        float max = sb.Fell_MaxHeightForDmg;
        if (fall < min)
        {
            return true;
        }

        return max > min && (fall - min) / (max - min) * sb.DefaultHealth < p.Health;
    }

    /// <summary>
    /// PHYS_Falling for actors that are not pawns (AActor::physFalling): each step (at most 0.05 s, or half the time
    /// left) adds half of (Acceleration + gravity 1500 down) times the step to the velocity and moves by the new
    /// velocity times the step, so a grenade's velocity falls by 750 uu/s per second; a walkable floor lands them at
    /// rest (Landed, PHYS_None), anything else raises HitWall and slides.
    /// </summary>
    [UcNative("AActor::physFalling", Exe = 0x10a2df70)]
    [Provenance(ProvenanceKind.Decoded, "AActor::physFalling game 0x10a2e0f9..0x10a2e1c9: Velocity += 0.5 (Acceleration + (0, 0, -1500)) dt, delta = Velocity dt; no speed cap")]
    [Provenance(ProvenanceKind.Measured, "the original's frags lose 12.48 uu/s of Z velocity per 1/60 s tick (750 uu/s^2; local/reticle/capture2.py)")]
    [Provenance(ProvenanceKind.Estimated, "The hit handling after the move is the stock UE2 one")]
    internal void physFallingActor(Actor actor, float dt, int iterations)
    {
        float gravityZ = GetGravityZ(actor);
        float remaining = dt;
        while (remaining > 0f && iterations < K.MaxPhysicsIterations)
        {
            iterations++;
            float tick = remaining <= K.MaxSubStep ? remaining : SvmMath.Min(K.MaxSubStep, remaining * 0.5f);
            remaining -= tick;
            actor.Velocity += (actor.Acceleration + new FVector(0f, 0f, gravityZ)) * (0.5f * tick);
            FVector delta = actor.Velocity * tick;
            MoveActor(actor, delta, actor.Rotation, out FCheckResult hit);
            if (actor.bDeleteMe || actor.Physics != EPhysics.PHYS_Falling)
            {
                return;
            }

            if (!hit.bHit)
            {
                continue;
            }

            // A bouncing actor (grenades) takes every hit, floors included, as HitWall and ends the step there
            // (stock UE2 AActor::physFalling): the bounce rules of its HitWall decide how it leaves.
            if (actor.bBounce)
            {
                actor.eventHitWall(hit.Normal, hit.Actor);
                return;
            }

            if (hit.Normal.Z >= K.MINFLOORZ)
            {
                actor.eventLanded(hit.Normal);
                if (!actor.bDeleteMe && actor.Physics == EPhysics.PHYS_Falling)
                {
                    setPhysics(actor, EPhysics.PHYS_None, hit.Actor, hit.Normal);
                }

                return;
            }

            actor.eventHitWall(hit.Normal, hit.Actor);
            if (actor.bDeleteMe || actor.Physics != EPhysics.PHYS_Falling)
            {
                return;
            }

            float vn = actor.Velocity | hit.Normal;
            if (vn < 0f)
            {
                actor.Velocity -= hit.Normal * vn;
            }

            FVector slide = (delta - hit.Normal * (delta | hit.Normal)) * (1f - hit.Time);
            if (!slide.IsNearlyZero())
            {
                MoveActor(actor, slide, actor.Rotation, out _);
            }
        }
    }

    /// <summary>PHYS_Projectile: straight line at Velocity; a blocking hit raises HitWall.</summary>
    [UcNative("AActor::physProjectile")]
    [Provenance(ProvenanceKind.Estimated, "Stock UE2 physProjectile without bounce")]
    internal void physProjectile(Actor actor, float dt)
    {
        MoveActor(actor, actor.Velocity * dt, actor.Rotation, out FCheckResult hit);
        if (hit.bHit && !actor.bDeleteMe)
        {
            processHitWall(actor, hit.Normal, hit.Actor);
        }
    }
}
