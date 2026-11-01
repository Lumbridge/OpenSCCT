using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Physics.Movement;

public sealed partial class PhysicsDriver
{
    // ---- PHYS_OnPipe ---------------------------------------------------------------------------------------

    /// <summary>
    /// PHYS_OnPipe (APawn::physOnPipe): the pipe swap, the hold (snap at 500 uu/s through the world, turning toward
    /// the pipe at 16384 per second), the climb (Acceleration.Z, or the slide with |Acceleration.X| &gt; 10), the
    /// bottom (TopSynchro), the junctions to a hand-over-hand bar or a zip line, and the top exits (TopPipe,
    /// TopPipeCrouch or Ledge). A hit on a floor or slope raises NotifyHitWall.
    /// </summary>
    [UcNative("APawn::physOnPipe", Exe = 0x10ad10c0, Editor = 0x1120a470)]
    [PortOf("eng:APawn::physOnPipe", Spec = "spec/native/physics-traversal.md#physonpipedt-6")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-traversal.md physOnPipe rules 1 to 7; ANSWERS-physics 15, 16")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ad11d8 to 0x10ad12a0: the swap normal runs from the new point nearer the current pipe's line (3D) to the other, flipped when the lower end of the current pipe is nearer Location.Z + 20")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ad10c0 (REA 2026-10-26): only the second GE of each search is tested; the zip dz is the closest point Z - (Location.Z + zone.Z), its XY test against Location plus the unturned zone, the HOH dz is from the zone point before the move; any HOH cancels the top test; the top reference is the hold target Z; the ledge is measured from Q; a climb-down HOH sets Delta.Z from the new Acceleration.Z")]
    public void physOnPipe(Pawn Pawn, float dt)
    {
        NotNull(Pawn);
        Pawn p = Pawn;
        if (p is not ISBasePawnPhysics sb)
        {
            return;
        }

        p.bSimulateGravity = false;
        var pc = p.Controller as ISPlayerControllerPhysics;
        bool controlling = IsControllingSide(p.Controller);
        FVector zone = sb.m_PipeArmsZone;
        FVector q = ArmsZonePoint(p, zone);
        FVector c = ProjectOnLine(q, sb.ZipP1, sb.ZipP2);

        // 2. Pipe swap.
        bool swapping = DiffersXY(sb.NewZipP1, sb.ZipP1, K.Traversal.SwapTolerance) || DiffersXY(sb.NewZipP2, sb.ZipP2, K.Traversal.SwapTolerance);
        if (swapping)
        {
            FVector a = sb.NewZipP1;
            FVector b = sb.NewZipP2;
            // 3D distances of the new points to the current pipe's line (game helper 0x10952350); a tie goes P1 - P2.
            float da = (ProjectOnLine(a, sb.ZipP1, sb.ZipP2) - a).Size();
            float db = (ProjectOnLine(b, sb.ZipP1, sb.ZipP2) - b).Size();
            FVector g = da < db ? (b - a).SafeNormal() : (a - b).SafeNormal();
            FVector lower = sb.ZipP1.Z <= sb.ZipP2.Z ? sb.ZipP1 : sb.ZipP2;
            FVector upper = sb.ZipP1.Z <= sb.ZipP2.Z ? sb.ZipP2 : sb.ZipP1;
            float z = p.Location.Z + K.Traversal.PipeSwapHeight;
            if (SvmMath.Abs(lower.Z - z) < SvmMath.Abs(upper.Z - z))
            {
                g = -g;
            }

            sb.m_geoNormal = g;
        }

        // 3. Hold.
        FVector n = sb.m_geoNormal;
        FVector target = c + n * zone.X + (new FVector(0f, 0f, 1f) ^ n) * zone.Y - new FVector(0f, 0f, zone.Z);
        FVector hold = Snap(target - p.Location, dt, out _);
        FRotator face = Mask4((-n).Rotation());
        int newYaw = FixedTurn(p.Rotation.Yaw, face.Yaw, SvmMath.Trunc(K.Traversal.PipeYawRate * dt));
        bool collideWorld = p.bCollideWorld;
        p.bCollideWorld = false;
        MoveActor(p, hold, new FRotator(face.Pitch, newYaw, face.Roll), out _);
        p.bCollideWorld = collideWorld;
        if (p.bDeleteMe)
        {
            return;
        }

        // The velocity is the move after the hold: the snap toward the GE does not show in it.
        FVector start = p.Location;

        if (swapping && SameYaw(newYaw, face.Yaw))
        {
            sb.ZipP1 = sb.NewZipP1;
            sb.ZipP2 = sb.NewZipP2;
            pc?.eventTopSynchro();
            if (p.bDeleteMe || p.Physics != EPhysics.PHYS_OnPipe)
            {
                return;
            }
        }

        // 4. Vertical move.
        FVector delta;
        bool sliding = SvmMath.Abs(p.Acceleration.X) > K.Traversal.InputThreshold;
        if (sliding)
        {
            p.Velocity.Z = SvmMath.Clamp(p.Velocity.Z + p.Acceleration.X * dt, K.Traversal.PipeSlideMinZ, 0f);
            delta = p.Velocity * dt;
        }
        else
        {
            delta = new FVector(0f, p.Acceleration.Y * dt, p.Acceleration.Z * dt);
        }

        // The top reference is the hold target's Z before the zone's Z is taken off (game 0x10ad13b2).
        float handsZ = target.Z + zone.Z;
        float bottomZ = SvmMath.Min(sb.ZipP1.Z, sb.ZipP2.Z);
        if (delta.Z <= 0f)
        {
            // 5. Going down.
            if (sliding)
            {
                if (sb.m_HandOverHandArmsZone.Z + p.Location.Z - bottomZ < K.Traversal.PipeBottom)
                {
                    pc?.eventTopSynchro();
                }
            }
            else if (FindArmsGE(p, K.Traversal.PipeClimbDownZone, sb.m_CurrentArmsRadius, out _, out GERecord? second))
            {
                // Only the second GE of the search is looked at (the first is the pipe itself, game 0x10ad1d9e).
                GERecord? hoh = second?.Kind == GEKind.HOH ? second : null;
                FVector hohZone = ArmsZonePoint(p, sb.m_HandOverHandArmsZone);
                if (hoh is not null)
                {
                    FVector proj = GEClosestPoint(hoh, hohZone, out _);
                    float dx = proj.X - hohZone.X;
                    float dy = proj.Y - hohZone.Y;
                    if (proj.Z - hohZone.Z > K.Traversal.PipeHohDownMinDz && dx * dx + dy * dy < K.Traversal.PipeHohDistSq)
                    {
                        p.Acceleration.Z = (proj.Z + K.Traversal.PipeHohDown - sb.m_HandOverHandArmsZone.Z - p.Location.Z) / dt;
                        delta.Z = p.Acceleration.Z * dt;
                        if (controlling)
                        {
                            FVector loc = p.Location;
                            FVector down = loc - new FVector(0f, 0f, K.Traversal.PipeHohDown);
                            FVector box = K.LedgeClearanceExtent;
                            if (Clear(p, loc, down, box, K.Flags.PawnsAndMovers) &&
                                Clear(p, down, down + YawForward(p.Rotation) * K.Traversal.PipeHohForward, box, K.Flags.PawnsAndMovers))
                            {
                                sb.NewZipP1 = hoh.P0;
                                sb.NewZipP2 = hoh.P1;
                                pc?.eventHandOverHand();
                            }
                        }
                    }
                }
            }
        }
        else if (!sliding)
        {
            // 6. Going up. Only the second GE of the search is looked at (game 0x10ad188f).
            bool topTest = true;
            if (FindArmsGE(p, K.Traversal.PipeClimbZone, sb.m_CurrentArmsRadius, out _, out GERecord? ge) && ge is not null)
            {
                if (ge.Kind == GEKind.ZL)
                {
                    // The zip test measures from Location plus the zone offset as it is (not turned with the pawn),
                    // before this call's move (game 0x10ad1908 to 0x10ad1990).
                    FVector zipLocal = sb.m_ZipLineArmsZone;
                    FVector cl = GEClosestPoint(ge, ArmsZonePoint(p, zipLocal), out _);
                    float dz = cl.Z - p.Location.Z - zipLocal.Z;
                    float dx = cl.X - p.Location.X - zipLocal.X;
                    float dy = cl.Y - p.Location.Y - zipLocal.Y;
                    if (dz < K.Traversal.PipeZipMaxDz && dx * dx + dy * dy < K.Traversal.PipeZipDistSq)
                    {
                        delta.Z -= dz - K.Traversal.PipeZipOffset;
                        if (controlling)
                        {
                            sb.ZipP1 = ge.P0;
                            sb.ZipP2 = ge.P1;
                            pc?.eventZipLine(default, default);
                        }
                    }
                }
                else if (ge.Kind == GEKind.HOH)
                {
                    // A HOH as the second GE cancels the top test this call, joined or not (game 0x10ad1b21).
                    topTest = false;
                    FVector hohZone = ArmsZonePoint(p, sb.m_HandOverHandArmsZone);
                    FVector cl = GEClosestPoint(ge, hohZone, out _);
                    float dz = cl.Z - hohZone.Z;
                    float dx = cl.X - hohZone.X;
                    float dy = cl.Y - hohZone.Y;
                    if (dz < K.Traversal.PipeHohUpMaxDz && dx * dx + dy * dy < K.Traversal.PipeHohDistSq)
                    {
                        delta.Z += dz + K.Traversal.PipeHohUpAdd;
                        if (controlling)
                        {
                            sb.NewZipP1 = ge.P0;
                            sb.NewZipP2 = ge.P1;
                            pc?.eventHandOverHand();
                        }
                    }
                }
            }

            if (p.bDeleteMe)
            {
                return;
            }

            float top = SvmMath.Max(sb.ZipP1.Z, sb.ZipP2.Z);
            if (topTest && top < handsZ + delta.Z && p.Acceleration.Z > K.Traversal.InputThreshold)
            {
                delta.Z = top - handsZ;
                if (controlling && pc is not null)
                {
                    if (TestTopPipe(p, delta.Z, false))
                    {
                        pc.eventTopPipe(sb.bIsLeftPart);
                    }
                    else if (TestTopPipe(p, delta.Z, true))
                    {
                        pc.eventTopPipeCrouch(sb.bIsLeftPart);
                    }
                    else
                    {
                        // The ledge is measured from Q, the pipe arms zone point at the start of the call, and only
                        // the second GE of the search is looked at (game 0x10ad1c90 to 0x10ad1d11).
                        if (FindArmsGE(p, K.Traversal.PipeClimbZone, sb.m_CurrentArmsRadius, out _, out GERecord? s2))
                        {
                            GERecord? lg = s2?.Kind == GEKind.LG ? s2 : null;
                            if (lg is not null)
                            {
                                FVector cl = GEClosestPoint(lg, q, out _);
                                float dx = cl.X - q.X;
                                float dy = cl.Y - q.Y;
                                if (dx * dx + dy * dy < K.Traversal.PipeTopLedgeDistSq && SvmMath.Abs(cl.Z - q.Z) < K.Traversal.PipeTopLedgeDz)
                                {
                                    pc.eventLedge();
                                }
                            }
                        }
                    }
                }
            }
        }

        if (p.bDeleteMe || p.Physics != EPhysics.PHYS_OnPipe)
        {
            return;
        }

        // 7. Move.
        MoveActor(p, delta, p.Rotation, out FCheckResult hit);
        if (!p.bDeleteMe && hit.bHit && hit.Normal.Z > 0f)
        {
            (p.Controller as IPhysicsEvents)?.eventNotifyHitWall(hit.Normal, hit.Actor);
        }

        if (!p.bDeleteMe)
        {
            p.Velocity = (p.Location - start) * (1f / dt);
        }
    }

    /// <summary>
    /// ASBasePawn::TestTopPipe: room to climb off a pipe top: four box traces (standing (35, 35, 87.5) or crouched
    /// (35, 35, CrouchHeight)) from Location + dz: 30 to the right, up 191.5 (crouched 156.9), 15 more to the right,
    /// then 79.4 (crouched 92.7) forward.
    /// </summary>
    [UcNative("ASBasePawn::TestTopPipe", Exe = 0x10ae2f70, Editor = 0x111fe5e0)]
    [PortOf("eng:ASBasePawn::TestTopPipe", Spec = "spec/native/physics-traversal.md#testtoppipeunused-dz-bcrouch")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-traversal.md TestTopPipe; ANSWERS-physics 16")]
    public bool TestTopPipe(Pawn Pawn, float dz, bool bCrouch)
    {
        NotNull(Pawn);
        Pawn.Rotation.GetAxes(out FVector fwd, out FVector right, out _);
        var box = new FVector(35f, 35f, bCrouch ? Pawn.CrouchHeight : K.StandingCollisionHeight);
        FVector a = Pawn.Location + new FVector(0f, 0f, dz);
        FVector b = a + right * K.Traversal.TopPipeRight;
        FVector c = b + new FVector(0f, 0f, bCrouch ? K.Traversal.TopPipeUpCrouched : K.Traversal.TopPipeUp);
        FVector d = c + right * K.Traversal.TopPipeRight2;
        FVector e = d + fwd * (bCrouch ? K.Traversal.TopPipeForwardCrouched : K.Traversal.TopPipeForward);
        return Clear(Pawn, a, b, box) && Clear(Pawn, b, c, box) && Clear(Pawn, c, d, box) && Clear(Pawn, d, e, box);
    }

    // ---- PHYS_OnPipeH --------------------------------------------------------------------------------------

    /// <summary>
    /// PHYS_OnPipeH, hand over hand (APawn::physOnPipeH): the hold under the bar (TopSynchro when settled, NoGE when
    /// blocked), the move along the bar at Acceleration.X, the end stops (27, or 95 legs over), the zip line join, the
    /// legs-over pawn probe, and the chains to a perpendicular bar (OrthoPipeH) or a pipe (Pipe).
    /// </summary>
    [UcNative("APawn::physOnPipeH", Exe = 0x10acff50, Editor = 0x112092c0)]
    [PortOf("eng:APawn::physOnPipeH", Spec = "spec/native/physics-traversal.md#physonpipehdt-14-hand-over-hand")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-traversal.md physOnPipeH rules 1 to 6; ANSWERS-physics 28")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10acff50: OrthoPipeH(bToLeft) from (Fwd x (far end - near end)).Z <= 0")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10acff50 (REA 2026-10-26): TopSynchro before the hold move on the old yaw; GE search only near an end; zip join at e1 < 3; zero move skips the chains; ortho needs both GEs HOH and fast input, no perpendicular test; pipe reach horizontal, way down when an end is at or below Location.Z")]
    public void physOnPipeH(Pawn Pawn, float dt)
    {
        NotNull(Pawn);
        Pawn p = Pawn;
        if (p is not ISBasePawnPhysics sb)
        {
            return;
        }

        p.bSimulateGravity = false;
        var pc = p.Controller as ISPlayerControllerPhysics;
        bool controlling = IsControllingSide(p.Controller);
        if (DiffersXY(sb.NewZipP1, sb.ZipP1, K.Traversal.SwapTolerance) || DiffersXY(sb.NewZipP2, sb.ZipP2, K.Traversal.SwapTolerance))
        {
            sb.ZipP1 = sb.NewZipP1;
            sb.ZipP2 = sb.NewZipP2;
        }

        float lower = p.CollisionHeight - sb.DefaultCollisionHeight;
        FVector zone = sb.m_HandOverHandArmsZone;
        FVector q = p.Location + zone + new FVector(0f, 0f, lower);
        FVector fwd = p.Rotation.Vector();
        FVector dir = (sb.ZipP1 - sb.ZipP2).SafeNormal();
        if ((dir | fwd) < 0f)
        {
            dir = -dir;
        }

        FVector c = ProjectOnLine(q, sb.ZipP1, sb.ZipP2);

        // 3. Hold.
        FVector hold = Snap(c - new FVector(0f, 0f, zone.Z + lower) - p.Location, dt, out bool arrived);
        int targetYaw = dir.Rotation().Yaw & 0xFFFC;
        // TopSynchro comes before the hold move, when the hold arrives and the yaw from the last call already faces
        // along the bar (game 0x10ad043b); NoGE follows a blocked hold move. Neither stops the rest of the call.
        if (arrived && SameYaw(p.Rotation.Yaw, targetYaw))
        {
            pc?.eventTopSynchro();
            if (p.bDeleteMe)
            {
                return;
            }
        }

        int newYaw = FixedTurn(p.Rotation.Yaw, targetYaw, K.Traversal.HoldTurnRate);
        MoveActor(p, hold, new FRotator(0, newYaw, 0), out FCheckResult holdHit);
        if (p.bDeleteMe)
        {
            return;
        }

        // The velocity is the move after the hold: the snap toward the GE does not show in it.
        FVector start = p.Location;

        if (holdHit.bHit)
        {
            pc?.eventNoGE();
            if (p.bDeleteMe)
            {
                return;
            }
        }

        // 4. Ends.
        float ax = p.Acceleration.X;
        float e0 = EndDistance(c, sb.ZipP1, sb.ZipP2);
        FVector c1 = ProjectOnLine(q + dir * (ax * dt), sb.ZipP1, sb.ZipP2);
        float e1 = EndDistance(c1, sb.ZipP1, sb.ZipP2);
        // The GE search only runs when the move nears an end (game 0x10ad061e); without it there is no chain below.
        GERecord? best = null;
        GERecord? second = null;
        bool slowInput = SvmMath.Abs(ax) < K.Traversal.HohLegsOverInput;
        if (e1 < e0)
        {
            FindArmsGE(p, K.Traversal.PipeClimbZone, sb.m_CurrentArmsRadius, out best, out second);
            bool legsOver = ax > 0f && ax < K.Traversal.HohLegsOverInput;
            float limit = legsOver ? K.Traversal.HohEndLegsOver : K.Traversal.HohEnd;
            if (!slowInput && second is { Kind: GEKind.ZL } zl)
            {
                // A zip line as the second GE takes the end stop away; the join needs the move to end within 3 of the
                // bar's end (game 0x10ad0679 to 0x10ad06f6).
                limit = 0f;
                if (controlling && e1 < K.Traversal.HohZipJoin)
                {
                    sb.ZipP1 = zl.P0;
                    sb.ZipP2 = zl.P1;
                    pc?.eventZipLine(default, default);
                    if (p.bDeleteMe || p.Physics != EPhysics.PHYS_OnPipeH)
                    {
                        return;
                    }
                }
            }

            if (e0 > limit && e1 < limit)
            {
                p.Acceleration *= (limit - e0) / (e1 - e0);
            }
            else if (e0 <= limit && e1 < limit)
            {
                p.Acceleration = default;
            }
        }

        // 5. Move along the bar.
        ax = p.Acceleration.X;
        FVector delta = dir * (ax * dt);
        // A zero move skips the move, the wall event and the chains (game 0x10ad083d).
        if (delta.IsZero())
        {
            p.Velocity = (p.Location - start) * (1f / dt);
            return;
        }

        if (ax > 0f && ax < K.Traversal.HohLegsOverInput && LegsBlocked(p, dir, delta))
        {
            delta = default;
        }

        MoveActor(p, delta, p.Rotation, out FCheckResult hit);
        if (p.bDeleteMe)
        {
            return;
        }

        if (controlling)
        {
            if (hit.bHit)
            {
                (p.Controller as IPhysicsEvents)?.eventNotifyHitWall(hit.Normal, hit.Actor);
            }

            if (second is not null && !p.bDeleteMe)
            {
                HohChain(p, sb, pc, best, second, q, slowInput);
            }
        }

        if (!p.bDeleteMe)
        {
            p.Velocity = (p.Location - start) * (1f / dt);
        }
    }

    private static float EndDistance(FVector c, FVector a, FVector b) => SvmMath.Min(FVector.Dist(c, a), FVector.Dist(c, b));

    private static FVector NearerEnd(FVector c, FVector a, FVector b) => FVector.DistSquared(c, a) <= FVector.DistSquared(c, b) ? a : b;

    /// <summary>
    /// The legs-over probe (flags 0x201) from Location to Location + Delta + 140 along the bar: a hit is blocking when it
    /// is a pawn hanging legs over (zone configuration 3) that faces us; any other hit is re-tested at 70.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game physOnPipeH 0x10acff50: both probes end at Location + Delta + k Dir; any hit (Time < 1) is tested, not only pawns")]
    private bool LegsBlocked(Pawn p, FVector dir, FVector delta)
    {
        FVector loc = p.Location;
        if (!Collision.LineCheck(out FCheckResult far, p, loc + delta + dir * K.Traversal.HohLegsProbe, loc, default, K.Flags.HohLegs))
        {
            return false;
        }

        bool legsOver = far.Actor is ISBasePawnPhysics osb && osb.ZoneConfig == K.Traversal.HohLegsOverZoneConfig;
        if (legsOver && (far.Actor!.Rotation.Vector() | dir) <= 0f)
        {
            return true;
        }

        return Collision.LineCheck(out _, p, loc + delta + dir * K.Traversal.HohLegsProbeNear, loc, default, K.Flags.HohLegs);
    }

    /// <summary>
    /// The chains after a HOH move (game 0x10ad0aca to 0x10ad103d). A PIPE as the first GE is used in place of the
    /// second. A perpendicular bar: both GEs HOH and fast input; when the second is the current bar the first is used;
    /// its closest point within horizontal 31 of the zone point gives OrthoPipeH. A pipe: see the pipe rules.
    /// </summary>
    private void HohChain(Pawn p, ISBasePawnPhysics sb, ISPlayerControllerPhysics? pc, GERecord? best, GERecord second, FVector q, bool slowInput)
    {
        if (best is { Kind: GEKind.PIPE })
        {
            second = best;
        }

        if (second.Kind == GEKind.HOH && best is { Kind: GEKind.HOH } && !slowInput)
        {
            if (second.P0 == sb.ZipP1 && second.P1 == sb.ZipP2)
            {
                second = best;
            }

            FVector cl = GEClosestPoint(second, q, out _);
            float dx = cl.X - q.X;
            float dy = cl.Y - q.Y;
            if (dx * dx + dy * dy < K.Traversal.HohOrthoDistSq)
            {
                sb.NewZipP1 = second.P0;
                sb.NewZipP2 = second.P1;
                // bToLeft: V runs from the bar's end nearer the pawn (in XY) to the other end; true when V is not to
                // the right of the facing ((Fwd x V).Z <= 0, game 0x10ad0c62).
                p.Rotation.GetAxes(out FVector fwd, out _, out _);
                FVector loc = p.Location;
                float d0 = (loc.X - second.P0.X) * (loc.X - second.P0.X) + (loc.Y - second.P0.Y) * (loc.Y - second.P0.Y);
                float d1 = (loc.X - second.P1.X) * (loc.X - second.P1.X) + (loc.Y - second.P1.Y) * (loc.Y - second.P1.Y);
                FVector v = d0 < d1 ? second.P1 - second.P0 : second.P0 - second.P1;
                pc?.eventOrthoPipeH(fwd.X * v.Y - fwd.Y * v.X <= 0f);
                if (p.bDeleteMe)
                {
                    return;
                }
            }
        }

        if (second.Kind != GEKind.PIPE)
        {
            return;
        }

        FVector zone = sb.m_PipeArmsZone;
        FVector q2 = ArmsZonePoint(p, zone);
        FVector cp = GEClosestPoint(second, q2, out FVector pn);
        // The reach is horizontal; an end at or below Location.Z is the way down (game 0x10ad0d3f to 0x10ad0d8f).
        float reach = (cp - q2).Size2D();
        bool goingDown = SvmMath.Min(second.P0.Z, second.P1.Z) <= p.Location.Z;
        bool ok;
        if (goingDown)
        {
            ok = reach < zone.X - K.Traversal.HohPipeBelowReach;
        }
        else
        {
            FVector loc = p.Location;
            FVector ahead = loc + YawForward(p.Rotation) * K.Traversal.HohPipeForward;
            FVector up = ahead + new FVector(0f, 0f, K.Traversal.HohPipeUp);
            ok = reach < K.Traversal.HohPipeAboveReach - zone.X && Clear(p, loc, ahead, K.LedgeClearanceExtent, K.Flags.PawnsAndMovers) &&
                 Clear(p, ahead, up, K.LedgeClearanceExtent, K.Flags.PawnsAndMovers);
        }

        if (ok)
        {
            sb.m_geoNormal = pn;
            sb.ZipP1 = second.P0;
            sb.ZipP2 = second.P1;
            pc?.eventPipe(cp, default);
        }
    }

    // ---- PHYS_OnLadder -------------------------------------------------------------------------------------

    /// <summary>
    /// PHYS_OnLadder (APawn::physOnLadder): the horizontal hold to the ladder line facing it (1000 per call), then the
    /// climb at Acceleration.Z, the stop on the nearest 64 uu rung (|Acceleration.Y| &gt; 10) or the slide
    /// (|Acceleration.X| &gt; 10, with TopSynchro at each rung when slowing), and TopLadder when the hands pass the top.
    /// </summary>
    [UcNative("APawn::physOnLadder", Exe = 0x10ad2a40, Editor = 0x1120b5d0)]
    [PortOf("eng:APawn::physOnLadder", Spec = "spec/native/physics-traversal.md#physonladderdt-13")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-traversal.md physOnLadder rules 1 to 5; ANSWERS-physics 19")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ad2a40: the rung dead band zeroes a rung speed below 2 uu/s; the hand height is the hold target's Z plus 2; the rung index is truncated (helper 0x10b83aa0, _ftol); the N flip test is horizontal (REA 2026-10-26)")]
    public void physOnLadder(Pawn Pawn, float dt)
    {
        NotNull(Pawn);
        Pawn p = Pawn;
        if (p is not ISBasePawnPhysics sb)
        {
            return;
        }

        p.bSimulateGravity = false;
        var pc = p.Controller as ISPlayerControllerPhysics;
        FVector zone = sb.m_NarrowLadderArmsZone;
        FVector q = ArmsZonePoint(p, zone);
        FVector c = ProjectOnLine(q, sb.ZipP1, sb.ZipP2);
        FVector n = sb.m_geoNormal;
        // The flip test is horizontal: N is turned when (C - Location) . N in XY is above 0 (game 0x10ad2af1).
        if ((c.X - p.Location.X) * n.X + (c.Y - p.Location.Y) * n.Y > 0f)
        {
            n = -n;
        }

        FVector target = c + n * zone.X + (new FVector(0f, 0f, 1f) ^ n) * zone.Y;
        FVector hold = target - p.Location;
        hold.Z = 0f;
        hold = Snap(hold, dt, out _);
        // The hand height is the hold target's Z (before the horizontal cut) plus 2 (game 0x10ad2bff).
        float hz = target.Z + K.Traversal.LadderHandHeight;
        FRotator face = Mask4((-n).Rotation());
        int newYaw = FixedTurn(p.Rotation.Yaw, face.Yaw, K.Traversal.HoldTurnRate);
        MoveActor(p, hold, new FRotator(face.Pitch, newYaw, face.Roll), out _);
        if (p.bDeleteMe)
        {
            return;
        }

        // The velocity is the move after the hold: the snap toward the GE does not show in it.
        FVector start = p.Location;

        float bottom = SvmMath.Min(sb.ZipP1.Z, sb.ZipP2.Z);
        float top = SvmMath.Max(sb.ZipP1.Z, sb.ZipP2.Z);
        float h = p.Location.Z - bottom - K.Traversal.RungOffset;
        // The rung index is truncated (the float to int helper rounds toward zero), so above the first rung off is
        // the height over the rung below, 0 to 64.
        float off = h - K.Traversal.RungSpacing * SvmMath.Trunc(h / K.Traversal.RungSpacing);
        FVector delta;
        if (SvmMath.Abs(p.Acceleration.Y) > K.Traversal.InputThreshold)
        {
            if (off > K.Traversal.RungSpacing * 0.5f)
            {
                off -= K.Traversal.RungSpacing;
            }

            p.Velocity = default;
            float vz = SvmMath.Abs(off) < K.Traversal.RungSnapSpeed * dt
                ? -off / dt
                : off >= 0f ? -K.Traversal.RungSnapSpeed : K.Traversal.RungSnapSpeed;
            p.Velocity.Z = SvmMath.Abs(vz) < K.Traversal.RungDeadBand ? 0f : vz;

            delta = new FVector(0f, 0f, p.Velocity.Z * dt);
        }
        else if (SvmMath.Abs(p.Acceleration.X) > K.Traversal.InputThreshold)
        {
            float ax = p.Acceleration.X;
            float vz = p.Velocity.Z + ax * dt;
            if (ax < K.Traversal.LadderSlideSplit && vz > K.Traversal.LadderSlideFast)
            {
                vz = K.Traversal.LadderSlideFast;
            }

            vz = SvmMath.Max(vz, K.Traversal.LadderSlideMin);
            if (ax > 0f)
            {
                vz = SvmMath.Min(vz, K.Traversal.LadderSlideSlow);
                if (off < -dt * vz)
                {
                    vz = -off / dt;
                    pc?.eventTopSynchro();
                }
            }

            // Only Velocity.Z is written; the move is the whole velocity (X and Y are last frame's, normally 0).
            p.Velocity.Z = vz;
            delta = p.Velocity * dt;
        }
        else
        {
            delta = new FVector(0f, 0f, p.Acceleration.Z * dt);
        }

        if (p.bDeleteMe)
        {
            return;
        }

        // The top test and the move only run for a non-zero move.
        if (!delta.IsZero())
        {
            if (top < delta.Z + hz - K.Traversal.LadderHandHeight && p.Acceleration.Z > K.Traversal.LadderTopInput)
            {
                delta.Z = top - hz + K.Traversal.LadderHandHeight;
                pc?.eventTopLadder();
                if (p.bDeleteMe)
                {
                    return;
                }
            }

            MoveActor(p, delta, p.Rotation, out FCheckResult hit);
            if (!p.bDeleteMe && hit.bHit)
            {
                (p.Controller as IPhysicsEvents)?.eventNotifyHitWall(hit.Normal, hit.Actor);
            }
        }

        if (!p.bDeleteMe)
        {
            p.Velocity = (p.Location - start) * (1f / dt);
        }
    }

    // ---- PHYS_Linear (zip line) -----------------------------------------------------------------------------

    /// <summary>
    /// PHYS_Linear, the zip line (APawn::physLinear): the hold under the cable (snap, facing along ZipP2 to ZipP1 at
    /// 3000 per call; a blocked hold drop-attacks a pawn or raises NoGE), the speed along the cable (stopped against
    /// the slope, eased down to GroundSpeed by 0.03 (GroundSpeed - s) above it, MycalcVelocity below), and the move; a
    /// hit (the low end) raises NotifyHitWall. Velocity is the second move over dt: the hold snap is not part of it, so
    /// the drop to the hang height after a grab moves the pawn without showing in Velocity.
    /// </summary>
    [UcNative("APawn::physLinear", Exe = 0x10acf860, Editor = 0x11208af0)]
    [PortOf("eng:APawn::physLinear", Spec = "spec/native/physics-traversal.md#physlineardt-7-zip-line-with-setzipspeed-and-unzipspeed")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-traversal.md physLinear rules 1 to 4; ANSWERS-physics 17")]
    [Provenance(ProvenanceKind.Decoded, "physLinear 0x11208af0: the velocity's start location is read after the hold move; the facing is ZipP1 - ZipP2 flattened (ANSWERS-fidelity F8)")]
    public void physLinear(Pawn Pawn, float dt)
    {
        NotNull(Pawn);
        Pawn p = Pawn;
        if (p is not ISBasePawnPhysics sb)
        {
            return;
        }

        p.bSimulateGravity = false;
        var pc = p.Controller as ISPlayerControllerPhysics;
        FVector zone = sb.m_ZipLineArmsZone;
        if (sb.bIsZipSpeed)
        {
            zone.Z -= sb.DefaultCollisionHeight - sb.ZipHeight;
        }

        // The speed-pose offset is already in zone. ToWorld here is the actor transform, without the mesh PrePivot.
        FVector c = ProjectOnLine(ToWorld(p, zone), sb.ZipP1, sb.ZipP2);
        FVector down = (sb.ZipP1.Z <= sb.ZipP2.Z ? sb.ZipP1 - sb.ZipP2 : sb.ZipP2 - sb.ZipP1).SafeNormal();
        FVector cable = sb.ZipP1 - sb.ZipP2;
        var horizontal = new FVector(cable.X, cable.Y, 0f);
        if (horizontal.Size() > 0f)
        {
            FVector hold = Snap(c - new FVector(0f, 0f, zone.Z) - p.Location, dt, out _);
            FRotator face = Mask4(horizontal.SafeNormal().Rotation());
            int newYaw = FixedTurn(p.Rotation.Yaw, face.Yaw, K.RootMotion.TurnRate);
            MoveActor(p, hold, new FRotator(p.Rotation.Pitch, newYaw, p.Rotation.Roll), out FCheckResult holdHit);
            if (p.bDeleteMe)
            {
                return;
            }

            if (holdHit.bHit)
            {
                if (holdHit.Actor is Pawn other && other is ISBasePawnPhysics)
                {
                    (p.Controller as IPhysicsEvents)?.eventDropAttack(other);
                }
                else
                {
                    pc?.eventNoGE();
                }

                if (p.bDeleteMe || p.Physics != EPhysics.PHYS_Linear)
                {
                    return;
                }
            }
        }

        FVector start = p.Location;
        if ((p.Velocity | down) < 0f)
        {
            p.Velocity = default;
        }

        float s = p.Velocity.Size();
        if (s >= p.GroundSpeed)
        {
            p.Acceleration = p.Velocity * (K.Traversal.ZipEase * (p.GroundSpeed - s));
            p.Velocity += p.Acceleration * dt;
        }
        else
        {
            MycalcVelocity(p, p.Acceleration.SafeNormal(), dt, p.GroundSpeed, K.GroundFriction, bFluid: false, bBrake: true);
        }

        MoveActor(p, p.Velocity * dt, p.Rotation, out FCheckResult hit);
        if (p.bDeleteMe)
        {
            return;
        }

        if (hit.bHit)
        {
            p.bSimulateGravity = true;
            (p.Controller as IPhysicsEvents)?.eventNotifyHitWall(hit.Normal, hit.Actor);
        }

        if (!p.bDeleteMe)
        {
            p.Velocity = (p.Location - start) * (1f / dt);
        }
    }

    /// <summary>
    /// ASBasePawn::SetZipSpeed: the zip speed pose, a (CrouchRadius, ZipHeight) cylinder that keeps the top of the
    /// standing one (Location raised by H - ZipHeight, PrePivot lowered by it), bIsZipSpeed and zone configuration 2.
    /// </summary>
    [UcNative("ASBasePawn::SetZipSpeed", Editor = 0x111fbb80)]
    [PortOf("eng:ASBasePawn::SetZipSpeed", Spec = "spec/native/physics-traversal.md#physlineardt-7-zip-line-with-setzipspeed-and-unzipspeed")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-traversal.md SetZipSpeed; ANSWERS-physics 18")]
    public void SetZipSpeed(Pawn Pawn)
    {
        NotNull(Pawn);
        if (Pawn is not ISBasePawnPhysics sb || Pawn.CollisionHeight == sb.ZipHeight || Pawn.Role == ENetRole.ROLE_SimulatedProxy)
        {
            return;
        }

        float d = Pawn.CollisionHeight - sb.ZipHeight;
        SetCollisionSize(Pawn, Pawn.CrouchRadius, sb.ZipHeight);
        FarMoveActor(Pawn, Pawn.Location + new FVector(0f, 0f, d));
        Pawn.PrePivot = sb.DefaultPrePivot - new FVector(0f, 0f, d);
        sb.bIsZipSpeed = true;
        sb.ZoneConfig = 2;
        sb.ZoneConfigForClient = 2;
    }

    /// <summary>
    /// ASBasePawn::UnZipSpeed: back to the default cylinder, moving down by the height difference (FarMoveActor). A
    /// successful real change restores PrePivot, clears bIsZipSpeed and sets zone configuration 0; a failure, or any
    /// test, puts the zip cylinder and location back. Returns whether the move fits for a test, else bIsZipSpeed.
    /// </summary>
    [UcNative("ASBasePawn::UnZipSpeed", Editor = 0x111fbcf0)]
    [PortOf("eng:ASBasePawn::UnZipSpeed", Spec = "spec/native/physics-traversal.md#physlineardt-7-zip-line-with-setzipspeed-and-unzipspeed")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-traversal.md UnZipSpeed; ANSWERS-physics 18")]
    public bool UnZipSpeed(Pawn Pawn, bool bTest)
    {
        NotNull(Pawn);
        if (Pawn is not ISBasePawnPhysics sb)
        {
            return false;
        }

        float d = Pawn.Role == ENetRole.ROLE_SimulatedProxy ? 0f : sb.DefaultCollisionHeight - Pawn.CollisionHeight;
        float oldR = Pawn.CollisionRadius;
        float oldH = Pawn.CollisionHeight;
        FVector oldLocation = Pawn.Location;
        bool oldJustTeleported = Pawn.bJustTeleported;
        Actor? oldBase = Pawn.Base;
        SetCollisionSize(Pawn, sb.DefaultCollisionRadius, sb.DefaultCollisionHeight);
        bool fits = FarMoveActor(Pawn, Pawn.Location - new FVector(0f, 0f, d), bTest);
        if (fits && !bTest)
        {
            Pawn.PrePivot = sb.DefaultPrePivot;
            sb.bIsZipSpeed = false;
            sb.ZoneConfig = 0;
            sb.ZoneConfigForClient = 0;
            return sb.bIsZipSpeed;
        }

        SetCollisionSize(Pawn, oldR, oldH);
        Pawn.Location = oldLocation;
        Pawn.bJustTeleported = oldJustTeleported;
        if (!ReferenceEquals(Pawn.Base, oldBase))
        {
            Pawn.SetBase(oldBase);
        }

        return bTest ? fits : sb.bIsZipSpeed;
    }

    /// <summary>SBasePawn.CanUnZipSpeed: UnZipSpeed as a test.</summary>
    [UcNative("ASBasePawn::execCanUnZipSpeed", Exe = 0x10acce10, Editor = 0x11206b70)]
    [PortOf("nat:SBase.SBasePawn.CanUnZipSpeed", Spec = "spec/native/physics-traversal.md#physlineardt-7-zip-line-with-setzipspeed-and-unzipspeed")]
    public bool CanUnZipSpeed(Pawn Pawn) => UnZipSpeed(Pawn, bTest: true);

    /// <summary>
    /// ASBasePawn::SetColForPipe4: the legs-over cylinder on a hand-over-hand bar (stance 4). Once (bIsPipe4Decal),
    /// not on a simulated proxy: the cylinder becomes (R, Pipe4Height) keeping its top (moved up by the height
    /// change, a FarMoveActor test move that keeps the spot), PrePivot is the default lowered by Pipe4DecalCol, and the
    /// zone configuration 3.
    /// </summary>
    [UcNative("ASBasePawn::SetColForPipe4", Editor = 0x111fc0e0)]
    [PortOf("eng:ASBasePawn::SetColForPipe4", Spec = "spec/native/ANSWERS-moveset.md#4-setcolforpipe4-setcolafterpipe4-cansetcolafterpipe4")]
    [Provenance(ProvenanceKind.Decoded, "ANSWERS-moveset.md 4: editor 0x111fc0e0")]
    public void SetColForPipe4(Pawn Pawn)
    {
        NotNull(Pawn);
        if (Pawn is not ISBasePawnPhysics sb || sb.bIsPipe4Decal || Pawn.Role == ENetRole.ROLE_SimulatedProxy)
        {
            return;
        }

        sb.bIsPipe4Decal = true;
        if (Pawn.CollisionHeight == sb.Pipe4Height)
        {
            return;
        }

        float d = Pawn.CollisionHeight - sb.Pipe4Height;
        SetCollisionSize(Pawn, Pawn.CollisionRadius, sb.Pipe4Height);
        FarMoveActor(Pawn, Pawn.Location + new FVector(0f, 0f, d), bTest: true, bNoCheck: false, bAttachedMove: true);
        Pawn.PrePivot = sb.DefaultPrePivot - new FVector(0f, 0f, sb.Pipe4DecalCol);
        sb.ZoneConfig = K.Traversal.HohLegsOverZoneConfig;
        sb.ZoneConfigForClient = K.Traversal.HohLegsOverZoneConfig;
    }

    /// <summary>
    /// ASBasePawn::SetColAfterPipe4: back from the legs-over cylinder to the default one, moved down by the height
    /// change so the top stays. Without the legs-over cylinder (or with another height) it is already done (true); a
    /// simulated proxy does nothing (false). A real change that fits restores PrePivot, clears bIsPipe4Decal and
    /// sets zone configuration 0; a test, or a change that does not fit, puts the cylinder and the spot back. Returns
    /// whether it fits for a test, else whether the legs-over cylinder is off.
    /// </summary>
    [UcNative("ASBasePawn::SetColAfterPipe4", Editor = 0x111fbed0)]
    [PortOf("eng:ASBasePawn::SetColAfterPipe4", Spec = "spec/native/ANSWERS-moveset.md#4-setcolforpipe4-setcolafterpipe4-cansetcolafterpipe4")]
    [Provenance(ProvenanceKind.Decoded, "ANSWERS-moveset.md 4: editor 0x111fbed0")]
    public bool SetColAfterPipe4(Pawn Pawn, bool bTest)
    {
        NotNull(Pawn);
        if (Pawn is not ISBasePawnPhysics sb || !sb.bIsPipe4Decal || Pawn.CollisionHeight != sb.Pipe4Height)
        {
            return true;
        }

        if (Pawn.Role == ENetRole.ROLE_SimulatedProxy)
        {
            return false;
        }

        float oldH = Pawn.CollisionHeight;
        float d = Pawn.CollisionHeight - sb.DefaultCollisionHeight;
        FVector oldLocation = Pawn.Location;
        SetCollisionSize(Pawn, Pawn.CollisionRadius, sb.DefaultCollisionHeight);
        bool fits = FarMoveActor(Pawn, Pawn.Location + new FVector(0f, 0f, d), bTest: true, bNoCheck: false, bAttachedMove: true);
        if (fits && !bTest)
        {
            Pawn.PrePivot = sb.DefaultPrePivot;
            sb.bIsPipe4Decal = false;
            sb.ZoneConfig = 0;
            sb.ZoneConfigForClient = 0;
            return true;
        }

        SetCollisionSize(Pawn, Pawn.CollisionRadius, oldH);
        FarMoveActor(Pawn, oldLocation, bTest: true, bNoCheck: false, bAttachedMove: true);
        return bTest ? fits : !sb.bIsPipe4Decal;
    }

    /// <summary>SBasePawn.CanSetColAfterPipe4: SetColAfterPipe4 as a test.</summary>
    [UcNative("ASBasePawn::execCanSetColAfterPipe4", Exe = 0x10acd360, Editor = 0x111fc240)]
    [PortOf("nat:SBase.SBasePawn.CanSetColAfterPipe4", Spec = "spec/native/ANSWERS-moveset.md#4-setcolforpipe4-setcolafterpipe4-cansetcolafterpipe4")]
    public bool CanSetColAfterPipe4(Pawn Pawn) => SetColAfterPipe4(Pawn, bTest: true);

    // ---- PHYS_OnVerticalPlane (fence) -----------------------------------------------------------------------

    /// <summary>
    /// PHYS_OnVerticalPlane, the fence (ASBasePawn::physOnVerticalPlane): climbing tests the fence top (FenceTop stops
    /// the climb); the pawn moves only while CheckFence at the target finds a climbable face, held R + 2 off it and
    /// facing it; a floor hit raises MyNotifyLanded.
    /// </summary>
    [UcNative("ASBasePawn::physOnVerticalPlane", Exe = 0x10acf3e0, Editor = 0x11208670)]
    [PortOf("eng:ASBasePawn::physOnVerticalPlane", Spec = "spec/native/physics-traversal.md#physonverticalplanedt-8-fence")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-traversal.md physOnVerticalPlane rules 1 to 3; ANSWERS-physics 22")]
    [Provenance(ProvenanceKind.Decoded, "ANSWERS-moveset.md 6: the face snap is taken from the target, the top test reads the best GE only and runs wherever the pawn has a controller")]
    public void physOnVerticalPlane(Pawn Pawn, float dt)
    {
        NotNull(Pawn);
        Pawn p = Pawn;
        if (p is not ISBasePawnPhysics sb)
        {
            return;
        }

        FVector start = p.Location;
        FVector target = p.Location + p.Acceleration * dt;
        var pc = p.Controller as ISPlayerControllerPhysics;
        if (p.Acceleration.Z > 0f && FindArmsGE(p, out GERecord? best, out _) && best is { Kind: GEKind.FENCE } fence && pc is not null)
        {
            if (CheckFenceTopArms(p, sb, pc, fence))
            {
                p.Acceleration.Z = 0f;
            }

            if (p.bDeleteMe)
            {
                return;
            }
        }

        if (CheckFence(p, out FVector n, out _, target, out FVector face) == 1)
        {
            // The snap that keeps the pawn R + 2 off the face is measured from the target, so the move along the face
            // is counted once.
            float r = p.CollisionRadius;
            var s = new FVector(face.X + (r + K.Traversal.FenceGap) * n.X - target.X, face.Y + (r + K.Traversal.FenceGap) * n.Y - target.Y, 0f);
            if (s.Size2D() < K.Traversal.SnapTolerance)
            {
                s = default;
            }

            FVector delta = s + p.Acceleration * dt;
            MoveActor(p, delta, (-n).Rotation(), out FCheckResult hit);
            if (!p.bDeleteMe && hit.bHit && hit.Normal.Z > K.Traversal.FenceLandZ)
            {
                (p.Controller as IPhysicsEvents)?.eventMyNotifyLanded(hit.Normal, (1f - hit.Time) * dt, hit.Actor);
            }
        }
        else
        {
            p.Acceleration = default;
        }

        if (!p.bDeleteMe)
        {
            p.Velocity = (p.Location - start) * (1f / dt);
        }
    }

    // ---- PHYS_Ledge ----------------------------------------------------------------------------------------

    /// <summary>
    /// PHYS_Ledge (APawn::physLedge): the hold on the hang target of TypeLedge (snap along the ledge normal and in
    /// height, facing the wall at 3000 per call, TopSynchro when settled, NoGE when blocked), the shimmy along a NORMAL
    /// ledge with the 36 uu end stop and the head room test, the "still on a ledge" check (NoGE), and the corners
    /// (ChangeLedge), merges and the ledge-to-pipe chain (TopPipe).
    /// </summary>
    [UcNative("APawn::physLedge", Exe = 0x10ac9400, Editor = 0x11204870)]
    [PortOf("eng:APawn::physLedge", Spec = "spec/native/physics-traversal.md#physledgedt-9")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-traversal.md physLedge rules 1 to 9; ANSWERS-physics 13")]
    [Provenance(ProvenanceKind.Estimated, "Ledge-to-pipe windows measured along the pawn axes from the pipe zone point to the pipe line")]
    public void physLedge(Pawn Pawn, float dt)
    {
        NotNull(Pawn);
        Pawn p = Pawn;
        if (p is not ISBasePawnPhysics sb)
        {
            return;
        }

        FVector start = p.Location;
        var pc = p.Controller as ISPlayerControllerPhysics;
        FVector l = sb.ZipP1 - sb.ZipP2;
        l.Z = 0f;
        if (l.IsZero())
        {
            return;
        }

        // 1. Side and facing.
        FVector fwd = p.Rotation.Vector();
        var n = new FVector(-l.Y, l.X, 0f).SafeNormal();
        FVector behind = p.Location - fwd * K.Traversal.LedgeBehind;
        if ((n | (behind - sb.ZipP1)) < 0f)
        {
            n = -n;
        }

        int face = (-n).Rotation().Yaw & ~3;

        // 2. Hands.
        byte type = sb.TypeLedge;
        FVector zone = type == 3 ? sb.m_NormalFeetZone : sb.m_LedgeGrabArmsZone;
        FVector q = p.Location - n * zone.X;
        q.Z = p.Location.Z + zone.Z;
        FVector c = ProjectOnLine(q, sb.ZipP1, sb.ZipP2);
        float e0 = EndDistance(c, sb.ZipP1, sb.ZipP2);

        // 3. Hang target.
        FVector loc = p.Location;
        FVector t;
        switch (type)
        {
            case 0:
                t = c + n * K.Traversal.LedgeOut;
                t.Z = c.Z - sb.m_LedgeGrabArmsZone.Z;
                break;
            case 1:
                t = WaistLedgeHoldTarget(p, new FVector(loc.X, loc.Y, c.Z - K.Traversal.LedgeWaistDown));
                break;
            case 2:
                t = new FVector(loc.X, loc.Y, c.Z - loc.Z < K.Traversal.LedgeBarrierDown ? loc.Z : c.Z - K.Traversal.LedgeBarrierDown);
                break;
            case 3:
                t = new FVector(loc.X, loc.Y, c.Z + K.Traversal.LedgeFeetUp);
                break;
            default:
                p.Velocity = default;
                return;
        }

        // 4. Hold.
        FVector d = n * ((t - loc) | n);
        d.Z = t.Z - loc.Z;
        bool floorLimitedWaist = type == 1 && d.Z < 0f && t.Z > c.Z - K.Traversal.LedgeWaistDown;
        float step = (floorLimitedWaist ? K.Traversal.LedgeWaistFloorSnapSpeed : K.Traversal.SnapSpeed) * dt;
        float dlen = d.Size();
        bool arrived = dlen <= step || dlen <= K.Traversal.SnapTolerance;
        FVector hold = arrived ? d : d.SafeNormal() * step;

        // TopSynchro comes before the hold move, when the hold arrives and the yaw from the last call already faces
        // the wall; NoGE follows a blocked hold move, so both can be raised in one call (editor 0x11204870). Once a
        // detect state accepts TopSynchro and enters its climb, that climb ignores the NoGE of the same call.
        if (arrived && SameYaw(p.Rotation.Yaw, face))
        {
            pc?.eventTopSynchro();
            if (p.bDeleteMe)
            {
                return;
            }
        }

        int newYaw = FixedTurn(p.Rotation.Yaw, face, K.RootMotion.TurnRate);
        MoveActor(p, hold, new FRotator(0, newYaw, 0), out FCheckResult holdHit);
        if (p.bDeleteMe)
        {
            return;
        }

        p.Rotation.Pitch = 0;
        p.Rotation.Roll = 0;
        if (holdHit.bHit)
        {
            pc?.eventNoGE();
            if (p.bDeleteMe)
            {
                return;
            }
        }

        // 5. Only the NORMAL hang shimmies; the other holds stop here whatever the events did.
        if (type != 0)
        {
            p.Velocity = default;
            return;
        }

        if (p.Physics != EPhysics.PHYS_Ledge)
        {
            return;
        }

        // 6. Shimmy.
        FVector c1 = ProjectOnLine(q + p.Acceleration * dt, sb.ZipP1, sb.ZipP2);
        float e1 = EndDistance(c1, sb.ZipP1, sb.ZipP2);
        float stop = K.Traversal.LedgeEndStop;
        if (e1 >= e0)
        {
            if (e1 == e0 && e1 < stop)
            {
                FVector nearEnd = NearerEnd(c, sb.ZipP1, sb.ZipP2);
                FVector away = (c - nearEnd).SafeNormal();
                away.Z = 0f;
                MoveActor(p, away * (stop - e1), p.Rotation, out _);
                if (p.bDeleteMe)
                {
                    return;
                }
            }
        }
        else if (e1 < stop && stop < e0)
        {
            p.Acceleration *= (stop - e0) / (e1 - e0);
        }
        else if (e1 < stop && e0 < stop)
        {
            p.Acceleration = default;
        }

        p.Velocity = default;
        FVector delta = p.Acceleration * dt;
        FVector moved = p.Location + delta;
        if (!Clear(p, moved, moved + new FVector(0f, 0f, K.Traversal.LedgeHeadRoom), K.LedgeClearanceExtent))
        {
            delta = default;
        }

        MoveActor(p, delta, p.Rotation, out FCheckResult hit);
        if (p.bDeleteMe)
        {
            return;
        }

        if (hit.bHit)
        {
            p.bSimulateGravity = true;
            if (p.Controller is IPhysicsEvents events)
            {
                if (hit.Normal.Z <= K.Traversal.LedgeLandZ)
                {
                    events.eventNotifyHitWall(hit.Normal, hit.Actor);
                }
                else
                {
                    events.eventMyNotifyLanded(hit.Normal, (1f - hit.Time) * dt, hit.Actor);
                }
            }

            if (p.bDeleteMe || p.Physics != EPhysics.PHYS_Ledge)
            {
                return;
            }
        }

        // 7. Still on a ledge.
        if (IsControllingSide(p.Controller) && pc is not null)
        {
            sb.m_CurrentArmsZone = sb.m_LedgeGrabArmsZone;
            sb.m_CurrentArmsRadius = sb.m_LedgeGrabArmsRadius;
            FindArmsGE(p, out GERecord? best, out GERecord? second);
            if (!ContainsHands(best, c1) && !ContainsHands(second, c1))
            {
                pc.eventNoGE();
                return;
            }

            // 8. Corners and chaining.
            LedgeCorners(p, sb, pc, n, e0, e1);
            if (p.bDeleteMe || p.Physics != EPhysics.PHYS_Ledge)
            {
                return;
            }
        }

        // 9. End of the call.
        p.Rotation.Roll = 0;
        p.Velocity = (p.Location - start) * (1f / dt);
    }

    private static bool ContainsHands(GERecord? ge, FVector c) => ge is { Kind: GEKind.LG } && InsideSegment(c, ge.P0, ge.P1);

    // A low waist ledge can put the authored hold below the floor (MoonD GE531). Keep the pawn's cylinder
    // above its supporting floor while the grab finishes; otherwise the downward snap aborts the climb
    // before AnimEnd and turns a jump into a short hop. The entry test uses the same raised hold for clearance.
    private FVector WaistLedgeHoldTarget(Pawn pawn, FVector target)
    {
        FVector loc = pawn.Location;
        if (target.Z < loc.Z && TraceMoveIgnoringBases(pawn, loc, target - loc, false, false, null, out FCheckResult floor)
            && floor.Normal.Z > K.Traversal.LedgeLandZ)
        {
            target.Z = floor.Location.Z + K.Traversal.SnapTolerance;
        }

        return target;
    }

    private void LedgeCorners(Pawn p, ISBasePawnPhysics sb, ISPlayerControllerPhysics pc, FVector n, float e0, float e1)
    {
        p.Rotation.GetAxes(out FVector fwd, out FVector right, out _);
        float input = p.Acceleration | right;
        float s = input > 0f ? 1f : input < 0f ? -1f : 0f;
        FVector cornerZone = K.Traversal.LedgeCornerZone;
        cornerZone.Y *= s;
        if (!FindArmsGE(p, cornerZone, sb.m_CurrentArmsRadius, out GERecord? best, out GERecord? second) || best is null)
        {
            return;
        }

        bool currentIsBest = best.Kind == GEKind.LG && IsCurrentLedge(best, sb);
        GERecord? other = currentIsBest ? second : IsCurrentLedge(second, sb) ? best : null;
        bool toEnd = !p.Acceleration.IsZero() && e1 < K.Traversal.LedgeCornerEnd && e1 < e0;
        if (toEnd && other is { Kind: GEKind.LG } && SharedEnd(sb.ZipP1, sb.ZipP2, other, out FVector shared, out FVector curFar, out FVector otherFar))
        {
            FVector u = curFar - shared;
            u.Z = 0f;
            u = u.SafeNormal();
            FVector v = otherFar - shared;
            v.Z = 0f;
            v = v.SafeNormal();
            // The native orders the far ends relative to the wall normal before testing the turn
            // (game 0x10ac9040, called at 0x10aca38b). Keeping the current ledge first on both sides
            // reverses inside/outside for left turns and selects the wrong clip and clearance test.
            if ((u ^ n).Z > 0f)
            {
                (u, v) = (v, u);
            }

            float cross = (u ^ v).Z;
            if (SvmMath.Abs(cross) < K.Traversal.LedgeCollinear)
            {
                sb.ZipP1 = curFar;
                sb.ZipP2 = otherFar;
                return;
            }

            if (SvmMath.Abs(u | v) >= K.Traversal.LedgePerpendicular)
            {
                return;
            }

            int type = (cross < 0f ? 0 : 2) + (s < 0f ? 1 : 0);
            if (type >= 2)
            {
                // Room to swing round: 75 on along the ledge the way the pawn shimmies (its right axis, reversed
                // for type 3), then 75 back toward the wall.
                FVector along = type == 3 ? -right : right;
                along.Z = 0f;

                var box = new FVector(35f, 35f, p.CollisionHeight);
                FVector a = p.Location + along * K.Traversal.LedgeCornerRoom;
                FVector b = a - n * K.Traversal.LedgeCornerRoom;
                if (!Clear(p, p.Location, a, box) || !Clear(p, a, b, box))
                {
                    return;
                }
            }

            pc.eventChangeLedge(type, other.P0, other.P1);
            return;
        }

        GERecord? pipe = best.Kind == GEKind.PIPE ? best : second?.Kind == GEKind.PIPE ? second : null;
        GERecord? lg = best.Kind == GEKind.LG ? best : second?.Kind == GEKind.LG ? second : null;
        if (pipe is null || lg is null || !IsCurrentLedge(lg, sb))
        {
            return;
        }

        FVector q2 = ArmsZonePoint(p, sb.m_PipeArmsZone);
        FVector onPipe = ProjectOnLine(q2, pipe.P0, pipe.P1);
        FVector off = onPipe - q2;
        float lowZ = SvmMath.Min(pipe.P0.Z, pipe.P1.Z) - K.Traversal.LedgePipeDz;
        float highZ = SvmMath.Max(pipe.P0.Z, pipe.P1.Z) + K.Traversal.LedgePipeDz;
        if (SvmMath.Abs(off | fwd) <= K.Traversal.LedgePipeForward && onPipe.Z >= lowZ && onPipe.Z <= highZ &&
            SvmMath.Abs(off | right) <= K.Traversal.LedgePipeSide)
        {
            sb.m_geoNormal = (pipe.Points[2] - pipe.P0).SafeNormal();
            sb.ZipP1 = pipe.P0;
            sb.ZipP2 = pipe.P1;
            pc.eventTopPipe(s < 0f);
        }
    }

    private static bool IsCurrentLedge(GERecord? ge, ISBasePawnPhysics sb) =>
        ge is { Kind: GEKind.LG } &&
        ((Near(ge.P0, sb.ZipP1, K.GE.ConnectTolerance) && Near(ge.P1, sb.ZipP2, K.GE.ConnectTolerance)) ||
         (Near(ge.P0, sb.ZipP2, K.GE.ConnectTolerance) && Near(ge.P1, sb.ZipP1, K.GE.ConnectTolerance)));

    /// <summary>The end the current ledge (a-b) shares with <paramref name="other"/> within 2 uu, and the far ends of both.</summary>
    private static bool SharedEnd(FVector a, FVector b, GERecord other, out FVector shared, out FVector curFar, out FVector otherFar)
    {
        float tol = K.Traversal.LedgeSharedEnd;
        shared = default;
        curFar = default;
        otherFar = default;
        if (Near(a, other.P0, tol))
        {
            (shared, curFar, otherFar) = (a, b, other.P1);
        }
        else if (Near(a, other.P1, tol))
        {
            (shared, curFar, otherFar) = (a, b, other.P0);
        }
        else if (Near(b, other.P0, tol))
        {
            (shared, curFar, otherFar) = (b, a, other.P1);
        }
        else if (Near(b, other.P1, tol))
        {
            (shared, curFar, otherFar) = (b, a, other.P0);
        }
        else
        {
            return false;
        }

        return true;
    }
}
