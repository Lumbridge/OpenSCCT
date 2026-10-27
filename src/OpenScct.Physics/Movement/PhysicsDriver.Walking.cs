using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Physics.Movement;

public sealed partial class PhysicsDriver
{
    private static readonly FVector s_gravDir = new(0f, 0f, -1f);

    /// <summary>
    /// PHYS_Walking for an SBasePawn (ASBasePawn::physWalking): the base effects (mover time, drop attack on a pawn
    /// base), the desired move (MycalcVelocity from Acceleration, or vDeltaMove for root-motion walking and
    /// repositioning), the wall pre-probe, then sub-steps of at most 0.05 s: the ledge stop, the move, stepUp on a hit
    /// with the after-step floor check, the 36 uu floor probe with the 1.39 / 1.405 / 1.42 floor band, and the lost
    /// floor (MayFall, the ledge stop, the narrow probe, MyFalling with LeftTime 0). Velocity is the move made.
    /// </summary>
    [UcNative("ASBasePawn::physWalking", Exe = 0x10ac6b40, Editor = 0x112025e0)]
    [UcNative("ASBasePawn::PhysWalkingWithChild", Editor = 0x111f8be0)]
    [PortOf("eng:ASBasePawn::physWalking", Spec = "spec/native/physics-walking.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-walking.md sections 0, 1, 3, 4")]
    public void physWalking(Pawn Pawn, float deltaTime, int Iterations)
    {
        NotNull(Pawn);
        Pawn p = Pawn;
        Controller? ctrl = p.Controller;
        if (ctrl is null)
        {
            return;
        }

        var sb = p as ISBasePawnPhysics;
        var pc = ctrl as ISPlayerControllerPhysics;
        if (p.Base is { bIsAMover: true } && pc is not null)
        {
            pc.fLastTimeOnAMover = p.Level.TimeSeconds;
        }

        if (sb is not null && WalkingDropAttack(p, sb, ctrl))
        {
            return;
        }

        // 1. Desired move.
        FVector accelDir;
        FVector move;
        if (sb is not null && ((p.Physics == EPhysics.PHYS_RootMotion && p.bUseRootMotionAdditionnalPhysics) ||
                               p.Physics == EPhysics.PHYS_Repositionnal))
        {
            move = sb.vDeltaMove;
            if (move.IsZero())
            {
                accelDir = default;
                p.GroundSpeed = 0f;
            }
            else
            {
                accelDir = move.SafeNormal();
            }
        }
        else
        {
            p.Velocity.Z = 0f;
            p.Acceleration.Z = 0f;
            if (p.Acceleration.IsZero())
            {
                accelDir = default;
                p.GroundSpeed = 0f;
            }
            else
            {
                accelDir = p.Acceleration.SafeNormal();
            }

            MycalcVelocity(p, accelDir, deltaTime, p.GroundSpeed, K.GroundFriction, bFluid: false, bBrake: true);
            move = new FVector(p.Velocity.X, p.Velocity.Y, 0f);
        }

        // 3. Wall pre-probe.
        move = WallPreProbe(p, move, deltaTime);
        if (p.bDeleteMe || p.Controller is null)
        {
            return;
        }

        // 4. Sub-steps.
        FVector R = GetCylinderExtent(p);
        float remaining = deltaTime;
        FVector oldLocation = p.Location;
        Actor? oldBase = p.Base;
        bool bCheckedFall = false;
        p.bJustTeleported = false;
        FVector carry = default;
        var down = new FVector(0f, 0f, -K.Walk.FloorProbe);
        while (remaining > 0f && Iterations < K.MaxPhysicsIterations && p.Controller is not null)
        {
            Iterations++;
            float tick = remaining <= K.MaxSubStep ? remaining : SvmMath.Min(K.MaxSubStep, remaining * 0.5f);
            remaining -= tick;
            FVector subLoc = p.Location;
            FVector delta = move * tick;
            bool zeroDelta = SvmMath.Abs(delta.X) < K.Walk.NearlyZero && SvmMath.Abs(delta.Y) < K.Walk.NearlyZero &&
                             SvmMath.Abs(delta.Z) < K.Walk.NearlyZero;
            if (zeroDelta)
            {
                remaining = 0f;
            }
            else
            {
                if (!p.bCanWalkOffLedges)
                {
                    if (sb?.m_AttackTarget is null)
                    {
                        p.bCanWalkOffLedges = true;
                    }
                    else
                    {
                        delta = CheckForLedges(p, accelDir, delta, s_gravDir);
                        if (delta.IsZero())
                        {
                            remaining = 0f;
                        }
                    }
                }

                carry = delta;
                if (delta.SizeSquared() < K.Walk.TinyMoveSq)
                {
                    delta = default;
                }

                carry -= delta;
                MoveActor(p, delta, p.Rotation, out FCheckResult hit);
                if (p.bDeleteMe)
                {
                    return;
                }

                if (hit.bHit)
                {
                    carry = default;
                    FVector desiredDir = delta.SafeNormal();
                    if (p.bCanWalkOffLedges)
                    {
                        FCheckResult stepHit = hit;
                        stepUp(p, s_gravDir, desiredDir, delta * (1f - hit.Time), ref stepHit);
                        if (p.bDeleteMe)
                        {
                            return;
                        }

                        if (p.Physics == EPhysics.PHYS_Walking && !AfterStepFloor(p, R))
                        {
                            FarMoveActor(p, oldLocation);
                            FVector n = new FVector(hit.Normal.X, hit.Normal.Y, 0f).SafeNormal();
                            FVector slide = (delta - n * (delta | n)) * (1f - hit.Time);
                            if (slide.Size() > K.Walk.TinySlide)
                            {
                                FCheckResult slideHit = hit;
                                stepUp(p, s_gravDir, slide.SafeNormal(), slide, ref slideHit);
                                if (p.bDeleteMe)
                                {
                                    return;
                                }

                                if (p.Physics == EPhysics.PHYS_Walking && !AfterStepFloor(p, R))
                                {
                                    FarMoveActor(p, oldLocation);
                                }
                            }
                        }
                    }
                }

                // 6. A stepUp event or the script started a fall.
                if (p.Physics == EPhysics.PHYS_Falling)
                {
                    float moved = (p.Location - subLoc).Size2D();
                    float len = delta.Size();
                    remaining += (1f - SvmMath.Min(1f, len > 0f ? moved / len : 1f)) * tick;
                    p.eventFalling();
                    if (p.Controller is { } c)
                    {
                        c.eventFalling();
                    }

                    if (p.Physics == EPhysics.PHYS_Flying)
                    {
                        p.Velocity = new FVector(0f, 0f, p.AirSpeed);
                        p.Acceleration = new FVector(0f, 0f, p.AccelRate);
                    }

                    startNewPhysics(p, remaining, Iterations);
                    return;
                }
            }

            // 7. Floor probe.
            FVector start = p.Location;
            Collision.LineCheck(out FCheckResult floor, p, start + down, start, R, K.Flags.Floor);
            float floorDist = K.Walk.FloorProbe * floor.Time;
            p.Floor = floor.Normal;
            if (floor.Normal.Z >= K.MINFLOORZ || zeroDelta || (floor.Normal | delta) >= 0f)
            {
                if (floor.bHit && (!ReferenceEquals(floor.Actor, p.Base) || floorDist > K.Walk.FloorRedropAbove))
                {
                    MoveActor(p, down, p.Rotation, out FCheckResult dropped);
                    if (dropped.bHit)
                    {
                        floor = dropped;
                    }

                    if (!p.bDeleteMe && !ReferenceEquals(floor.Actor, p.Base))
                    {
                        p.SetBase(floor.Actor);
                    }
                }
                else if (floor.bHit && floorDist < K.Walk.FloorLiftBelow)
                {
                    FVector realNormal = floor.Normal;
                    MoveActor(p, new FVector(0f, 0f, K.Walk.FloorLiftTo - floorDist), p.Rotation, out _);
                    floor.Time = 0f;
                    floor.Normal = realNormal;
                }
            }
            else
            {
                // A steep surface the move goes into: slide down along it.
                FVector n = floor.Normal;
                var stepDown = new FVector(0f, 0f, K.Walk.SteepSlideStep);
                FVector f = n * (n | stepDown) - stepDown;
                if (f.SizeSquared() < K.Walk.TinyMoveSq)
                {
                    f = default;
                }

                MoveActor(p, f, p.Rotation, out FCheckResult slid);
                floor = slid;
                if (!p.bDeleteMe && slid.bHit && !ReferenceEquals(slid.Actor, p.Base))
                {
                    p.SetBase(slid.Actor);
                }
            }

            if (p.bDeleteMe)
            {
                return;
            }

            // 8. Standing on a walkable floor.
            if (floor.bHit && floor.Normal.Z >= K.MINFLOORZ)
            {
                continue;
            }

            // 9. The floor is lost.
            if (p.bCanJump && !bCheckedFall && pc is not null)
            {
                bCheckedFall = true;
                pc.eventMayFall();
                if (p.bDeleteMe)
                {
                    return;
                }
            }

            if (!p.bJustTeleported && (!p.bCanJump || (!p.bCanWalkOffLedges && (p.bIsWalking || p.bIsCrouched))))
            {
                p.Velocity = default;
                p.Acceleration = default;
                FarMoveActor(p, oldLocation);
                if (oldBase is not null && (oldBase.bStatic || oldBase.bWorldGeometry || !oldBase.bMovable))
                {
                    p.SetBase(oldBase);
                }

                if (p.Controller is { } c)
                {
                    c.MoveTimer = -1f;
                }

                return;
            }

            if (floorDist <= K.Walk.NarrowLedgeDistance)
            {
                var narrow = new FVector(R.X / 2f, R.Y / 2f, R.Z);
                FVector at = p.Location;
                if (Collision.LineCheck(out _, p, at + down, at, narrow, K.Flags.Clearance))
                {
                    continue;
                }
            }

            float lenD = delta.Size();
            float fallTime = zeroDelta || lenD <= 0f
                ? 0f
                : (1f - SvmMath.Min(1f, (p.Location - subLoc).Size2D() / lenD)) * tick + remaining;
            if (!p.bJustTeleported && !p.bNoVelocityUpdate && p.Location != oldLocation && deltaTime - fallTime > 0f)
            {
                p.Velocity = (p.Location - oldLocation) * (1f / (deltaTime - fallTime));
            }

            p.bNoVelocityUpdate = false;
            p.Velocity.Z = 0f;
            StartFalling(p, fallTime, Iterations);
            return;
        }

        // 10. Velocity from the move made.
        if (!p.bJustTeleported && !p.bNoVelocityUpdate)
        {
            p.Velocity = (p.Location + carry - oldLocation) * (1f / deltaTime);
        }

        p.bNoVelocityUpdate = false;
        p.Velocity.Z = 0f;
    }

    /// <summary>
    /// The walking pawn lost its floor. The controlling side's controller gets MyFalling(0) and its script plays
    /// the fall action (PHYS_Falling); the native does not change the mode. A controller without
    /// <see cref="IPhysicsEvents"/> gets the stock UE2 switch: Falling, PHYS_Falling and the rest of the frame.
    /// </summary>
    private void StartFalling(Pawn p, float fallTime, int iterations)
    {
        if (p.Controller is IPhysicsEvents events)
        {
            if (IsControllingSide(p.Controller))
            {
                events.eventMyFalling(0f);
            }

            return;
        }

        p.eventFalling();
        if (p.bDeleteMe)
        {
            return;
        }

        if (p.Physics == EPhysics.PHYS_Walking)
        {
            setPhysics(p, EPhysics.PHYS_Falling);
        }

        startNewPhysics(p, fallTime, iterations);
    }

    /// <summary>Walking rule 0.3: the authority's drop attack on the pawn it stands on. True when physWalking returns.</summary>
    private static bool WalkingDropAttack(Pawn p, ISBasePawnPhysics sb, Controller ctrl)
    {
        if (p.Role != ENetRole.ROLE_Authority || p.Base is not Pawn basePawn || basePawn is not ISBasePawnPhysics baseSb ||
            basePawn.Controller is not ISPlayerControllerPhysics basePc || !basePc.eventCanBeDropAttacked())
        {
            return false;
        }

        if (baseSb.bStickySkinned)
        {
            (ctrl as ISPlayerControllerPhysics)?.eventStickyBlast(basePawn);
            return true;
        }

        if (p.bIsCrouched && !sb.UnCrouch(true))
        {
            return false;
        }

        (ctrl as IPhysicsEvents)?.eventDropAttack(basePawn);
        return true;
    }

    /// <summary>
    /// The walking wall pre-probe (section 3): two thin box probes 0.2 uu either side of the move, lifted 18 uu; a hit
    /// that counts bumps, raises processHitWall and slides the velocity and the move along the wall, then the slid
    /// move is probed again and a second wall stops it. Returns the move to make.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-walking.md section 3: offsets 0.2, lift 18, flags 0xd6; extent (R - 0.2, R - 0.2, H - 18), the sign checked at editor 0x112025e0")]
    private FVector WallPreProbe(Pawn p, FVector move, float dt)
    {
        FVector d = move * dt;
        d.Z = 0f;
        if (d.IsZero())
        {
            return move;
        }

        float lift = K.Walk.FloorProbe * 0.5f;
        FVector s = p.Location + new FVector(0f, 0f, lift);
        FVector r = GetCylinderExtent(p);
        // Extent Z is H + Down.Z / 2 = H - 18: lifted 18, the box spans from 36 above the feet to the head, so steps
        // below the step height are left to stepUp (the spec text reads H + 18; the decompiled sign is minus).
        var e = new FVector(r.X - K.Walk.ProbeOffset, r.Y - K.Walk.ProbeOffset, r.Z - lift);
        FVector dir = d.SafeNormal();
        if (ProbeSides(p, s, e, d, dir, K.Flags.WallProbe, parallelSkip: true, out FCheckResult h) != ProbeResult.Counting)
        {
            return move;
        }

        WallHit(p, ref h);
        if (p.bDeleteMe)
        {
            return move;
        }

        p.Velocity -= h.Normal * (h.Normal | p.Velocity);
        move -= h.Normal * (h.Normal | move);
        d = move * dt;
        d.Z = 0f;
        if (d.IsZero())
        {
            return move;
        }

        if (ProbeSides(p, s, e, d, d.SafeNormal(), K.Flags.WallProbe, parallelSkip: false, out FCheckResult h2) == ProbeResult.Counting)
        {
            WallHit(p, ref h2);
            move = default;
        }

        return move;
    }

    private enum ProbeResult
    {
        None,
        Counting,
        Parallel,
    }

    /// <summary>The two side probes of a pre-probe: to S + L + 0.2 P, then S + L - 0.2 P.</summary>
    private ProbeResult ProbeSides(Pawn p, FVector s, FVector e, FVector d, FVector dir, ETraceFlags flags, bool parallelSkip,
        out FCheckResult counting)
    {
        var perp = new FVector(-dir.Y, dir.X, 0f);
        FVector l = d + dir * K.Walk.ProbeOffset;
        Collision.LineCheck(out FCheckResult a, p, s + l + perp * K.Walk.ProbeOffset, s, e, flags);
        if (parallelSkip && a.bHit && SvmMath.Abs(a.Normal | dir) <= K.Walk.ParallelWall)
        {
            counting = a;
            return ProbeResult.Parallel;
        }

        if (Counts(a, l))
        {
            counting = a;
            return ProbeResult.Counting;
        }

        Collision.LineCheck(out FCheckResult b, p, s + l - perp * K.Walk.ProbeOffset, s, e, flags);
        counting = b;
        return Counts(b, l) ? ProbeResult.Counting : ProbeResult.None;
    }

    private static bool Counts(in FCheckResult h, FVector l) => h.Actor is not null && h.bHit && (h.Normal | l) * h.Time < K.Walk.ProbeOffset;

    /// <summary>A pre-probe wall: bump it (not world geometry, not a base relation), then processHitWall.</summary>
    private void WallHit(Pawn p, ref FCheckResult h)
    {
        if (h.Actor is { } other && !other.bWorldGeometry && !IsBasedOn(p, other))
        {
            NotifyBump(other, p);
            NotifyBump(p, other);
        }

        if (!p.bDeleteMe)
        {
            processHitWall(p, ref h.Normal, h.Actor);
        }
    }

    /// <summary>The after-step floor check: a walkable floor within 1.52 uu below the cylinder.</summary>
    private bool AfterStepFloor(Pawn p, FVector r)
    {
        FVector at = p.Location;
        return Collision.LineCheck(out FCheckResult h, p, at - new FVector(0f, 0f, K.Walk.AfterStepFloorCheck), at, r, K.Flags.Floor) &&
               h.Normal.Z >= K.MINFLOORZ;
    }

    /// <summary>
    /// Ground velocity (ASBasePawn::MycalcVelocity). Braking (bBrake with no Acceleration) decays the velocity by
    /// 2 V f in 0.03 s sub-steps, takes the time average of the sub-step velocities that still point along the start
    /// velocity, and stops it below 10 uu/s or when it turns back; otherwise the velocity turns toward
    /// <paramref name="AccelDir"/> keeping its speed. Then Acceleration dt is added (AccelRate is not used), and the
    /// speed is capped at <paramref name="MaxSpeed"/> (times DesiredSpeed for a pawn that is not human controlled).
    /// </summary>
    [UcNative("ASBasePawn::MycalcVelocity", Exe = 0x10ace4d0, Editor = 0x111fc2d0)]
    [PortOf("eng:ASBasePawn::MycalcVelocity", Spec = "spec/native/physics-walking.md#2-mycalcvelocityacceldir-dt-maxspeed-friction-bfluid-bbrake")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-walking.md section 2; ANSWERS-physics 1")]
    public static void MycalcVelocity(Pawn Pawn, FVector AccelDir, float DeltaTime, float MaxSpeed, float Friction, bool bFluid, bool bBrake)
    {
        System.ArgumentNullException.ThrowIfNull(Pawn);
        FVector v = Pawn.Velocity;
        if (bBrake && Pawn.Acceleration.IsZero())
        {
            // The result is the time average of the sub-step velocities that still point along the start velocity.
            FVector start = v;
            FVector average = default;
            float t = DeltaTime;
            float invDt = DeltaTime > 0f ? 1f / DeltaTime : 0f;
            while (t > K.Walk.BrakeSubStep)
            {
                v -= v * (K.BrakingFactor * Friction * K.Walk.BrakeSubStep);
                if ((start | v) > 0f)
                {
                    average += v * (K.Walk.BrakeSubStep * invDt);
                }

                t -= K.Walk.BrakeSubStep;
            }

            v -= v * (K.BrakingFactor * Friction * t);
            if ((start | v) > 0f)
            {
                average += v * (t * invDt);
            }

            v = DeltaTime > 0f ? average : v;
            if ((v | start) < 0f || v.SizeSquared() < K.Walk.BrakeStopSpeedSq)
            {
                v = default;
            }
        }
        else
        {
            v -= (v - AccelDir * v.Size()) * (DeltaTime * Friction);
        }

        v = v * (1f - (bFluid ? DeltaTime * Friction : 0f)) + Pawn.Acceleration * DeltaTime;
        if (Pawn.Controller is not { bIsAPlayerController: true })
        {
            MaxSpeed *= Pawn.DesiredSpeed;
        }

        if (v.SizeSquared() > MaxSpeed * MaxSpeed)
        {
            v = v.SafeNormal() * MaxSpeed;
        }

        Pawn.Velocity = v;
    }

    /// <summary>The previous name of <see cref="MycalcVelocity"/> (bFluid false).</summary>
    public static void calcVelocity(Pawn Pawn, FVector AccelDir, float DeltaTime, float MaxSpeed, float Friction, bool bBrake) =>
        MycalcVelocity(Pawn, AccelDir, DeltaTime, MaxSpeed, Friction, bFluid: false, bBrake);

    /// <summary>
    /// APawn::stepUp. A near-vertical or walkable hit lifts the pawn 34 and moves on; a sloped one (not walking) moves
    /// along the slope. A hit after that pushes a pushable decoration (human players head-on), takes another step
    /// (near-vertical, Time |Delta|^2 &gt; 144, with the shortened step of the original) or slides along the wall and
    /// its crease. The pawn always ends moved back down by the step.
    /// </summary>
    [UcNative("APawn::stepUp", Exe = 0x10a2c460, Editor = 0x1117d2a0)]
    [PortOf("eng:APawn::stepUp", Spec = "spec/native/physics-walking.md#5-stepupg-desireddir-delta-hit")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-walking.md section 5")]
    internal void stepUp(Pawn p, FVector gravDir, FVector desiredDir, FVector delta, ref FCheckResult hit)
    {
        FVector up = gravDir * K.MAXSTEPHEIGHT;
        if (SvmMath.Abs(hit.Normal.Z) < K.StepUpWallZ || hit.Normal.Z >= K.MINFLOORZ)
        {
            MoveActor(p, -up, p.Rotation, out _);
            if (delta.SizeSquared() < K.Walk.TinyMoveSq)
            {
                delta = default;
            }

            MoveActor(p, delta, p.Rotation, out hit);
        }
        else if (p.Physics != EPhysics.PHYS_Walking)
        {
            FVector m = delta + new FVector(0f, 0f, delta.Size() * hit.Normal.Z);
            if (m.SizeSquared() < K.Walk.TinyMoveSq)
            {
                m = default;
            }

            MoveActor(p, m, p.Rotation, out hit);
        }

        if (p.bDeleteMe)
        {
            return;
        }

        if (hit.bHit)
        {
            bool slide = false;
            if (p.Controller is { bIsAPlayerController: true } && hit.Actor is Decoration { bPushable: true } deco &&
                (hit.Normal | desiredDir) < K.Walk.PushDot)
            {
                p.bNoVelocityUpdate = true;
                p.Velocity *= p.Mass / (deco.Mass + p.Mass);
                processHitWall(p, ref hit.Normal, hit.Actor);
                if (p.Physics == EPhysics.PHYS_Falling || p.bDeleteMe)
                {
                    return;
                }

                slide = true;
            }

            if (!slide && SvmMath.Abs(hit.Normal.Z) < K.StepUpWallZ && hit.Time * delta.SizeSquared() > K.Walk.SecondStepMinSq)
            {
                MoveActor(p, up, p.Rotation, out FCheckResult downHit);
                float t = downHit.Time;
                FCheckResult next = downHit.bHit ? downHit : hit;
                stepUp(p, gravDir * (1f - t), desiredDir, delta * (1f - t), ref next);
                hit = next;
                return;
            }

            processHitWall(p, ref hit.Normal, hit.Actor);
            if (p.Physics == EPhysics.PHYS_Falling || p.bDeleteMe)
            {
                return;
            }

            FVector n = new FVector(hit.Normal.X, hit.Normal.Y, 0f).SafeNormal();
            FVector s = (delta - n * (delta | n)) * (1f - hit.Time);
            if ((s | delta) >= 0f)
            {
                MoveActor(p, s, p.Rotation, out FCheckResult second);
                if (p.bDeleteMe)
                {
                    return;
                }

                if (second.bHit)
                {
                    processHitWall(p, ref second.Normal, second.Actor);
                    if (p.Physics == EPhysics.PHYS_Falling || p.bDeleteMe)
                    {
                        return;
                    }

                    TwoWallAdjust(desiredDir, ref s, second.Normal, n, second.Time);
                    MoveActor(p, s, p.Rotation, out _);
                }
            }
        }

        if (!p.bDeleteMe)
        {
            MoveActor(p, up, p.Rotation, out _);
        }
    }

    /// <summary>
    /// The move left after hitting a second wall (AActor::TwoWallAdjust): along the crease of a closed corner, or along
    /// the new wall of an open one, never back against the desired direction.
    /// </summary>
    [UcNative("AActor::TwoWallAdjust", Exe = 0x109b0630, Editor = 0x110dc0c0)]
    [Provenance(ProvenanceKind.Decoded, "game 0x109b0630: N . Old above 0 slides along N (zero unless it keeps going along DesiredDir), else along N x Old, turned to DesiredDir")]
    public static void TwoWallAdjust(FVector DesiredDir, ref FVector Delta, FVector HitNormal, FVector OldHitNormal, float HitTime)
    {
        if ((OldHitNormal | HitNormal) <= 0f)
        {
            FVector dir = (HitNormal ^ OldHitNormal).SafeNormal();
            Delta = dir * ((Delta | dir) * (1f - HitTime));
            if ((DesiredDir | Delta) < 0f)
            {
                Delta = -Delta;
            }
        }
        else
        {
            Delta = (Delta - HitNormal * (Delta | HitNormal)) * (1f - HitTime);
            if ((Delta | DesiredDir) <= 0f)
            {
                Delta = default;
            }
        }
    }

    /// <summary>
    /// The actor ran into a wall (APawn::processHitWall). Walls only (no actor or a pawn: nothing). Without a
    /// controller, or with bDirectHitWall, the actor gets HitWall. In repositioning the controller's NotifyHitWall
    /// comes first and true ends it. Otherwise, only while Acceleration is non-zero: walking compares horizontal
    /// directions (the caller continues with the horizontal normal), and a nearly head-on hit (velocity . normal at
    /// most Controller.MinHitWall) that NotifyHitWall does not take moves the pawn down a step (unless falling) and
    /// raises HitWall.
    /// </summary>
    [UcNative("APawn::processHitWall", Exe = 0x10a2ced0, Editor = 0x1117d920)]
    [PortOf("eng:APawn::processHitWall", Spec = "spec/native/physics-walking.md#8-processhitwallhitnormal-hitactor-apawn-virtual-called-by-every-mode-above")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-walking.md section 8")]
    [Provenance(ProvenanceKind.Estimated, "The UE2 crouch-walk retry for non-human pawns is not ported (no such pawns in Versus)")]
    public void processHitWall(Actor Actor, FVector HitNormal, Actor? HitActor)
    {
        NotNull(Actor);
        processHitWall(Actor, ref HitNormal, HitActor);
    }

    internal void processHitWall(Actor actor, ref FVector hitNormal, Actor? hitActor)
    {
        if (actor is not Pawn p)
        {
            actor.eventHitWall(hitNormal, hitActor);
            return;
        }

        if (hitActor is null || hitActor is Pawn)
        {
            return;
        }

        Controller? c = p.Controller;
        if (p.bDirectHitWall || c is null)
        {
            p.eventHitWall(hitNormal, hitActor);
            return;
        }

        var events = c as IPhysicsEvents;
        bool repos = p.Physics == EPhysics.PHYS_Repositionnal;
        if (repos && events is not null && events.eventNotifyHitWall(hitNormal, hitActor))
        {
            return;
        }

        if (!repos && p.Acceleration.IsZero())
        {
            return;
        }

        FVector dir = p.Velocity.SafeNormal();
        if (p.Physics == EPhysics.PHYS_Walking)
        {
            hitNormal.Z = 0f;
            dir.Z = 0f;
            hitNormal = hitNormal.SafeNormal();
            dir = dir.SafeNormal();
        }

        if ((dir | hitNormal) <= c.MinHitWall && (events is null || !events.eventNotifyHitWall(hitNormal, hitActor)))
        {
            if (p.Physics != EPhysics.PHYS_Falling && !p.bDeleteMe)
            {
                MoveActor(p, new FVector(0f, 0f, -K.MAXSTEPHEIGHT), p.Rotation, out _);
            }

            if (!p.bDeleteMe)
            {
                p.eventHitWall(hitNormal, hitActor);
            }
        }
    }

    /// <summary>
    /// ASBasePawn::CheckForLedges, the ledge stop while bCanWalkOffLedges is false and an attack target is set: a
    /// front probe, a floor probe below the front point (with the slope allowance), a narrow box floor test, and a
    /// sidestep either way; returns the move to make (zero stops the pawn at the edge).
    /// </summary>
    [UcNative("ASBasePawn::CheckForLedges", Editor = 0x111face0)]
    [PortOf("eng:ASBasePawn::CheckForLedges", Spec = "spec/native/physics-walking.md#6-checkforledgesacceldir-delta-g-only-while-bcanwalkoffledges-is-false-and-m_attacktarget-is-set")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-walking.md section 6")]
    [Provenance(ProvenanceKind.Estimated, "The sidestep tries the left side first and moves from Location; the order is not decoded")]
    internal FVector CheckForLedges(Pawn p, FVector accelDir, FVector delta, FVector gravDir)
    {
        float r = p.CollisionRadius;
        float h = p.CollisionHeight;
        FVector loc = p.Location;
        FVector front = delta + accelDir * (p.bAvoidLedges ? K.Walk.AvoidLedgesReach * r : r);
        if (Collision.LineCheck(out _, p, loc + front, loc, default, K.Flags.Clearance))
        {
            return delta;
        }

        float dlen = delta.Size();
        float depth = SvmMath.Max(K.MAXSTEPHEIGHT, dlen + r) + h + K.Walk.LedgeDepthMargin;
        FVector point = loc + front;
        Collision.LineCheck(out FCheckResult floor, p, point + gravDir * depth, point, default, K.Flags.Floor);
        if (!p.bAvoidLedges)
        {
            point = loc + delta;
        }

        bool bad = !floor.bHit || floor.Normal.Z < K.MINFLOORZ;
        if (!bad)
        {
            float nz = floor.Normal.Z;
            float allowed = SvmMath.Min(K.MAXSTEPHEIGHT, (dlen + r) / nz * SvmMath.Sqrt(SvmMath.Max(0f, 1f - nz * nz))) + h +
                            K.Walk.LedgeDepthMargin;
            bad = allowed < depth * floor.Time;
        }

        if (!bad)
        {
            return delta;
        }

        var box = new FVector(r / K.Walk.LedgeBoxRadiusDivisor, r / K.Walk.LedgeBoxRadiusDivisor, h);
        if (BoxFloorAt(p, loc, point, box, gravDir))
        {
            return delta;
        }

        FVector dir = (point - loc).SafeNormal();
        var perp = new FVector(-dir.Y, dir.X, 0f);
        for (int side = 0; side < 2; side++)
        {
            FVector target = loc + (side == 0 ? perp : -perp) * dlen;
            if (BoxFloorAt(p, loc, target, box, gravDir))
            {
                return (target - loc).SafeNormal() * dlen;
            }
        }

        return default;
    }

    /// <summary>A box trace from <paramref name="from"/> to <paramref name="to"/>, then 38 down: true on a walkable floor.</summary>
    private bool BoxFloorAt(Pawn p, FVector from, FVector to, FVector box, FVector gravDir)
    {
        Collision.LineCheck(out FCheckResult across, p, to, from, box, K.Flags.Clearance);
        FVector at = across.bHit ? across.Location : to;
        return Collision.LineCheck(out FCheckResult down, p, at + gravDir * K.Walk.LedgeBoxDepth, at, box, K.Flags.Clearance) &&
               down.Normal.Z >= K.MINFLOORZ;
    }

    /// <summary>
    /// ASBasePawn::physWalkingSimulated, for pawns without a controller in root motion with walking: the sub-step move
    /// by vDeltaMove (all three axes) with stepUp, the floor probe (Floor takes its normal), then by the floor: on a
    /// walkable one, or with no move, or moving away from it, the lift to 1.405 when it is the Base within 1.39, or the
    /// drop by the probe when it is another actor or farther than 1.42 (no floor: nothing); on a steep surface moved
    /// into, the slide down it. No pre-probe, ledge logic, SetBase or events.
    /// </summary>
    [UcNative("ASBasePawn::physWalkingSimulated", Exe = 0x10ac5db0, Editor = 0x11202080)]
    [PortOf("eng:ASBasePawn::physWalkingSimulated", Spec = "spec/native/physics-walking.md#7-physwalkingsimulateddeltatime-iterations-proxies-in-root-motion-with-walking")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-walking.md section 7; game 0x10ac5db0")]
    internal void physWalkingSimulated(Pawn p, float deltaTime, int iterations)
    {
        PhysWalkingSimulatedCore(p, deltaTime, iterations);
    }

    /// <summary>
    /// <see cref="physWalkingSimulated"/> for a client's proxy (net-proxy.md rule 13): the pawn walks by its
    /// vDeltaMove and its Velocity becomes the displacement over the time.
    /// </summary>
    public void PhysWalkingSimulated(Pawn p, float deltaTime) => PhysWalkingSimulatedCore(p, deltaTime, 0);

    private void PhysWalkingSimulatedCore(Pawn p, float deltaTime, int iterations)
    {
        if (p is not ISBasePawnPhysics sb)
        {
            return;
        }

        FVector start = p.Location;
        FVector move = sb.vDeltaMove;
        FVector r = GetCylinderExtent(p);
        var down = new FVector(0f, 0f, -K.Walk.FloorProbe);
        float remaining = deltaTime;
        while (remaining > 0f && iterations < K.MaxPhysicsIterations)
        {
            iterations++;
            float tick = remaining <= K.MaxSubStep ? remaining : SvmMath.Min(K.MaxSubStep, remaining * 0.5f);
            remaining -= tick;
            FVector delta = move * tick;
            bool noMove = delta.IsNearlyZero();
            if (noMove)
            {
                remaining = 0f;
            }
            else
            {
                MoveActor(p, delta, p.Rotation, out FCheckResult hit);
                if (p.bDeleteMe)
                {
                    return;
                }

                if (hit.bHit)
                {
                    stepUp(p, s_gravDir, delta.SafeNormal(), delta * (1f - hit.Time), ref hit);
                }
            }

            if (p.bDeleteMe)
            {
                return;
            }

            FVector at = p.Location;
            Collision.LineCheck(out FCheckResult floor, p, at + down, at, r, K.Flags.Floor);
            // No hit leaves the check result as it starts: Time 1 and a zero normal.
            float floorDist = floor.bHit ? K.Walk.FloorProbe * floor.Time : K.Walk.FloorProbe;
            FVector n = floor.bHit ? floor.Normal : default;
            p.Floor = n;
            if (n.Z >= K.MINFLOORZ || noMove || (n | delta) >= 0f)
            {
                if (!floor.bHit || (ReferenceEquals(floor.Actor, p.Base) && floorDist <= K.Walk.FloorRedropAbove))
                {
                    if (floorDist < K.Walk.FloorLiftBelow)
                    {
                        MoveActor(p, new FVector(0f, 0f, K.Walk.FloorLiftTo - floorDist), p.Rotation, out _);
                    }
                }
                else
                {
                    MoveActor(p, down, p.Rotation, out _);
                }
            }
            else
            {
                var stepDown = new FVector(0f, 0f, K.Walk.SteepSlideStep);
                MoveActor(p, n * (n | stepDown) - stepDown, p.Rotation, out _);
            }

            if (p.bDeleteMe)
            {
                return;
            }
        }

        p.Velocity = (p.Location - start) * (1f / deltaTime);
    }
}
