using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Traversal;
using OpenScct.Provenance;

namespace OpenScct.Physics.Movement;

public sealed partial class PhysicsDriver
{
    private const int AllGEKinds = 0x7F;

    private readonly List<GEHit> _geHits = new(32);

    // ---- GE element rules ------------------------------------------------------------------------------------

    /// <summary>
    /// The closest point of a GE's segment to <paramref name="p"/> and its direction: LG, FENCE, HOH the horizontal
    /// unit of P1 - P2; LADDER, PIPE the unit toward the climber (P3 - P1); ZL from the high end to the low one; POLE up.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "GE element ClosestPoint (spec/native/physics-ge-queries.md Data)")]
    public static FVector GEClosestPoint(GERecord GE, FVector p, out FVector Dir)
    {
        System.ArgumentNullException.ThrowIfNull(GE);
        FVector c = GEIndex.ClosestPointOnSegment(GE.P0, GE.P1, p, out _);
        switch (GE.Kind)
        {
            case GEKind.LADDER or GEKind.PIPE:
                Dir = (GE.Points[2] - GE.P0).SafeNormal();
                break;
            case GEKind.ZL:
                Dir = (GE.P0.Z >= GE.P1.Z ? GE.P1 - GE.P0 : GE.P0 - GE.P1).SafeNormal();
                break;
            case GEKind.POLE:
                Dir = new FVector(0f, 0f, 1f);
                break;
            default:
                FVector d = GE.P0 - GE.P1;
                d.Z = 0f;
                Dir = d.SafeNormal();
                break;
        }

        return c;
    }

    /// <summary>Two-point GEs connect when some end of each is within 2 uu on every axis; ladders and pipes never do.</summary>
    [Provenance(ProvenanceKind.Decoded, "GE element Connected (spec/native/physics-ge-queries.md Data)")]
    public static bool GEConnected(GERecord A, GERecord B)
    {
        System.ArgumentNullException.ThrowIfNull(A);
        System.ArgumentNullException.ThrowIfNull(B);
        if (A.Points.Length != 2 || B.Points.Length != 2)
        {
            return false;
        }

        return Near(A.P0, B.P0, K.GE.ConnectTolerance) || Near(A.P0, B.P1, K.GE.ConnectTolerance) ||
               Near(A.P1, B.P0, K.GE.ConnectTolerance) || Near(A.P1, B.P1, K.GE.ConnectTolerance);
    }

    /// <summary>Same kind and every point within 0.1 uu on every axis.</summary>
    [Provenance(ProvenanceKind.Decoded, "GE element Equals (spec/native/physics-ge-queries.md Data)")]
    public static bool GEEquals(GERecord A, GERecord B)
    {
        System.ArgumentNullException.ThrowIfNull(A);
        System.ArgumentNullException.ThrowIfNull(B);
        if (A.Kind != B.Kind || A.Points.Length != B.Points.Length)
        {
            return false;
        }

        for (int i = 0; i < A.Points.Length; i++)
        {
            if (!Near(A.Points[i], B.Points[i], K.GE.EqualTolerance))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Near(FVector a, FVector b, float tol) =>
        SvmMath.Abs(a.X - b.X) <= tol && SvmMath.Abs(a.Y - b.Y) <= tol && SvmMath.Abs(a.Z - b.Z) <= tol;

    private static bool DiffersXY(FVector a, FVector b, float tol) => SvmMath.Abs(a.X - b.X) > tol || SvmMath.Abs(a.Y - b.Y) > tol;

    // ---- zone points ---------------------------------------------------------------------------------------

    /// <summary>An offset in pawn space moved to world space with the full rotation (the actor's local-to-world transform).</summary>
    private static FVector ToWorld(Pawn p, FVector local)
    {
        p.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
        return p.Location + x * local.X + y * local.Y + z * local.Z;
    }

    private static FVector ToWorldYaw(Pawn p, FVector local)
    {
        new FRotator(0, p.Rotation.Yaw, 0).GetAxes(out FVector x, out FVector y, out FVector z);
        return p.Location + x * local.X + y * local.Y + z * local.Z;
    }

    private static FVector PrePivotChange(Pawn p, ISBasePawnPhysics sb) => p.PrePivot - sb.DefaultPrePivot;

    /// <summary>The arms zone point: Location + Rotation applied to (offset + PrePivot - Default.PrePivot).</summary>
    [Provenance(ProvenanceKind.Decoded, "Zone points (spec/native/physics-ge-queries.md Data)")]
    public static FVector ArmsZonePoint(Pawn Pawn, FVector Offset)
    {
        System.ArgumentNullException.ThrowIfNull(Pawn);
        return Pawn is ISBasePawnPhysics sb ? ToWorld(Pawn, Offset + PrePivotChange(Pawn, sb)) : ToWorld(Pawn, Offset);
    }

    /// <summary>The feet zone point: as the arms point with the yaw only.</summary>
    public static FVector FeetZonePoint(Pawn Pawn, FVector Offset)
    {
        System.ArgumentNullException.ThrowIfNull(Pawn);
        return Pawn is ISBasePawnPhysics sb ? ToWorldYaw(Pawn, Offset + PrePivotChange(Pawn, sb)) : ToWorldYaw(Pawn, Offset);
    }

    /// <summary>Location.Z shifted to the standing equivalent: lowered by ZCDecal[ZoneConfig * 4].</summary>
    private static float StandingZ(Pawn p, ISBasePawnPhysics sb)
    {
        int index = sb.ZoneConfig * 4;
        float decal = sb.ZCDecal is { } table && (uint)index < (uint)table.Length ? table[index] : 0f;
        return p.Location.Z - decal;
    }

    /// <summary>Velocity in pawn space (X forward, Y right, Z up).</summary>
    private static FVector LocalVelocity(Pawn p)
    {
        p.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
        return new FVector(p.Velocity | x, p.Velocity | y, p.Velocity | z);
    }

    // ---- search --------------------------------------------------------------------------------------------

    /// <summary>
    /// The active GEs whose segment is closer than <paramref name="radius"/> to <paramref name="center"/> (GetGEInBox
    /// then the Distance test), in GE list order.
    /// </summary>
    [UcNative("ULevel::GetGEInBox", Editor = 0x1111fc00)]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rule 1 (box query, then Distance below the radius)")]
    [Provenance(ProvenanceKind.Estimated, "Candidates come in GE list order, not the stored k-d tree order: only exact distance ties can differ")]
    private List<GEHit> SearchGEs(Pawn p, FVector center, float radius)
    {
        GEs.QuerySphere(center, radius, AllGEKinds, p.Level.GEActivationMask, _geHits);
        for (int i = _geHits.Count - 1; i >= 0; i--)
        {
            if (_geHits[i].Distance >= radius)
            {
                _geHits.RemoveAt(i);
            }
        }

        return _geHits;
    }

    /// <summary>
    /// ASBasePawn::FindArmsGE with the current arms zone and radius: the best GE (on a pipe or a hand-over-hand
    /// bar the nearest of that kind first, else the nearest of any kind) and the second GE that chains with it.
    /// False when nothing is in range.
    /// </summary>
    [UcNative("ASBasePawn::FindArmsGE", Exe = 0x10ac90a0, Editor = 0x11204510)]
    [PortOf("eng:ASBasePawn::FindArmsGE", Spec = "spec/native/physics-ge-queries.md#2-findarmsgeout-best-out-second-pawn-method")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rule 2")]
    public bool FindArmsGE(Pawn Pawn, out GERecord? Best, out GERecord? Second)
    {
        NotNull(Pawn);
        Best = null;
        Second = null;
        return Pawn is ISBasePawnPhysics sb && FindArmsGE(Pawn, sb.m_CurrentArmsZone, sb.m_CurrentArmsRadius, out Best, out Second);
    }

    /// <summary>FindArmsGE with another zone offset and radius (the traversal modes' own searches).</summary>
    public bool FindArmsGE(Pawn Pawn, FVector ZoneOffset, float Radius, out GERecord? Best, out GERecord? Second)
    {
        NotNull(Pawn);
        Best = null;
        Second = null;
        if (Radius == 0f)
        {
            return false;
        }

        List<GEHit> hits = SearchGEs(Pawn, ArmsZonePoint(Pawn, ZoneOffset), Radius);
        int best = -1;
        GEKind prefer = Pawn.Physics switch
        {
            EPhysics.PHYS_OnPipe => GEKind.PIPE,
            EPhysics.PHYS_OnPipeH => GEKind.HOH,
            _ => 0,
        };
        if (prefer != 0)
        {
            best = Nearest(hits, prefer);
        }

        if (best < 0)
        {
            best = Nearest(hits, 0);
        }

        if (best < 0)
        {
            return false;
        }

        GERecord b = hits[best].GE;
        Best = b;
        for (int i = 0; i < hits.Count; i++)
        {
            if (i == best)
            {
                continue;
            }

            GERecord c = hits[i].GE;
            if (b.Kind == GEKind.PIPE && c.Kind is GEKind.HOH or GEKind.ZL or GEKind.LG)
            {
                Second = c;
            }
            else if (b.Kind == GEKind.HOH && c.Kind is GEKind.ZL or GEKind.PIPE)
            {
                Second = c;
                break;
            }
            else if (b.Kind == GEKind.LG && c.Kind == GEKind.PIPE)
            {
                Second = c;
                break;
            }
            else if (c.Kind == b.Kind && (GEConnected(b, c) || b.Kind == GEKind.PIPE))
            {
                Second = c;
            }
        }

        return true;
    }

    private static int Nearest(List<GEHit> hits, GEKind kind)
    {
        int best = -1;
        for (int i = 0; i < hits.Count; i++)
        {
            if ((kind == 0 || hits[i].GE.Kind == kind) && (best < 0 || hits[i].Distance < hits[best].Distance))
            {
                best = i;
            }
        }

        return best;
    }

    // ---- CheckArmsZone -------------------------------------------------------------------------------------

    /// <summary>
    /// SPlayerController.CheckArmsZone: FindArmsGE, ZipP1 / ZipP2 from the best GE (not for POLE and FENCE), then the
    /// kind's arm test, which raises at most one event (HandOverHand, Ledge, WaistLedge, AutoFeetLedge,
    /// NarrowLadder, Pipe, ZipLine).
    /// </summary>
    [UcNative("ASPlayerController::execCheckArmsZone", Exe = 0x10aa54d0, Editor = 0x111f2330)]
    [PortOf("nat:SBase.SPlayerController.CheckArmsZone", Spec = "spec/native/physics-ge-queries.md#3-checkarmszone")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rules 3 to 5")]
    public void CheckArmsZone(Pawn Pawn)
    {
        NotNull(Pawn);
        if (Pawn is not ISBasePawnPhysics sb || Pawn.Controller is not ISPlayerControllerPhysics pc ||
            !FindArmsGE(Pawn, out GERecord? best, out GERecord? second) || best is null)
        {
            return;
        }

        if (best.Kind is not (GEKind.POLE or GEKind.FENCE))
        {
            sb.ZipP1 = best.P0;
            sb.ZipP2 = best.P1;
        }

        FVector z = ArmsZonePoint(Pawn, sb.m_CurrentArmsZone);
        switch (best.Kind)
        {
            case GEKind.HOH:
                CheckHOHArms(Pawn, sb, pc, best, z);
                break;
            case GEKind.LG:
                CheckLedgeArms(Pawn, sb, pc, best);
                break;
            case GEKind.LADDER:
                CheckLadderArms(Pawn, sb, pc, best, z);
                break;
            case GEKind.PIPE:
                CheckPipeArms(Pawn, sb, pc, best, z);
                break;
            case GEKind.ZL:
                CheckZipArms(Pawn, sb, pc, best, z);
                break;
        }

        _ = second;
    }

    /// <summary>The wall normal of a ledge as the ledge test uses it: (Dir.Y, -Dir.X, 0), turned to point from the wall to the pawn.</summary>
    private static FVector LedgeNormal(FVector dir, FVector fwd)
    {
        var n = new FVector(dir.Y, -dir.X, 0f);
        return (fwd | n) > 0f ? -n : n;
    }

    /// <summary>
    /// The LG arm test (game 0x10aa4420): while walking the auto feet ledge (a ledge 40 to 64 above the feet, running
    /// at it); otherwise the waist ledge (standing room first, then crouched) and the hang (Ledge), each with its
    /// position, facing, speed and clearance rules. Reloaded patches this test: the auto feet ledge never fires, the
    /// waist window is 24 below to 23 above (stock 18 and 22), and in s_WallJumpSalto the hang window starts 20 below
    /// (stock 5); <see cref="ReloadedLedgeTest"/> picks Reloaded's numbers.
    /// </summary>
    [UcNative("ASPlayerController::CheckLedgeArms", Exe = 0x10aa4420, Editor = 0x111e95d0)]
    [PortOf("eng:ASPlayerController::CheckLedgeArms", Spec = "spec/native/physics-ge-queries.md#4-checkledgearmsge-second-ledge-waistledge-autofeetledge")]
    [PortOf("rl:SBase.ASPlayerController.CheckLedgeArms", Spec = "spec/movement/ledge.md#reloaded-ledge-assist")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rule 4; ANSWERS-physics 11, 26")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core start-up 0x1001c350: jump at game 0x10aa470b to the no-grab exit (no AutoFeetLedge); doubles 0x10c10ff8 18 -> 24 and 0x10c10ff0 22 -> 23; 0x10aa4e8b reads 0x10360ed8, set by 0x1001c2a0 to 20 in s_WallJumpSalto, else 5")]
    private void CheckLedgeArms(Pawn p, ISBasePawnPhysics sb, ISPlayerControllerPhysics pc, GERecord ge)
    {
        FVector loc = p.Location;
        FVector q = loc + new FVector(0f, 0f, sb.m_LedgeGrabArmsZone.Z);
        FVector c = GEClosestPoint(ge, q, out FVector dir);
        FVector fwd = p.Rotation.Vector();
        if ((fwd | (c - loc).SafeNormal()) <= 0f || (p.bIsCrouched && p.Physics == EPhysics.PHYS_Walking))
        {
            return;
        }

        FVector n = LedgeNormal(dir, fwd);
        float r = p.CollisionRadius;
        float h = p.CollisionHeight;
        float off = r + K.GE.WallGap;
        FVector localV = LocalVelocity(p);
        FVector box = K.LedgeClearanceExtent;
        bool facing = (fwd | n) < K.GE.GrabFacing;
        if (p.Physics == EPhysics.PHYS_Walking)
        {
            if (ReloadedLedgeTest)
            {
                return;
            }

            float top = c.Z + h;
            float dx = c.X + off * n.X - loc.X;
            float dy = c.Y + off * n.Y - loc.Y;
            float above = top - loc.Z;
            if (facing && loc.Z > top - K.GE.AutoFeetHigh && loc.Z < top - K.GE.AutoFeetLow && localV.X > K.GE.AutoFeetMinSpeed &&
                dx * dx + dy * dy < above * above * K.GE.AutoFeetDistFactor)
            {
                var up = new FVector(loc.X, loc.Y, top + K.GE.AutoFeetTopMargin);
                if (Clear(p, loc, up, box) && Clear(p, up, up - n * K.GE.FeetOver, box))
                {
                    pc.eventAutoFeetLedge();
                }
            }

            return;
        }

        // Waist ledge.
        var hp = new FVector(c.X + off * n.X, c.Y + off * n.Y, c.Z - K.GE.WaistDown);
        float lz = StandingZ(p, sb);
        float wx = hp.X - loc.X;
        float wy = hp.Y - loc.Y;
        float waistLow = ReloadedLedgeTest ? K.GE.WaistWindowLowReloaded : K.GE.WaistWindowLow;
        float waistHigh = ReloadedLedgeTest ? K.GE.WaistWindowHighReloaded : K.GE.WaistWindowHigh;
        if (facing && p.Velocity.Z > K.GE.WaistMinVelocityZ && p.Velocity.Z < K.GE.WaistMaxVelocityZ && lz > hp.Z + waistLow &&
            lz < hp.Z + waistHigh && localV.X > K.GE.MinForwardSpeed && wx * wx + wy * wy < K.GE.WaistDistSq && !pc.bTestBackFeetGrab)
        {
            var l = new FVector(loc.X, loc.Y, lz);
            var standUp = new FVector(l.X, l.Y, hp.Z + K.GE.WaistUp);
            if (Clear(p, l, standUp, box) && Clear(p, standUp, standUp - n * K.GE.WaistOver, box))
            {
                pc.eventWaistLedge(true);
                return;
            }

            var crouchBox = new FVector(box.X, box.Y, p.CrouchHeight);
            var crouchStart = new FVector(l.X, l.Y, l.Z - (h - p.CrouchHeight));
            FVector crouchUp = crouchStart + new FVector(0f, 0f, K.GE.WaistUpCrouched);
            if (Clear(p, crouchStart, crouchUp, crouchBox) && Clear(p, crouchUp, crouchUp - n * K.GE.WaistOverCrouched, crouchBox))
            {
                pc.eventWaistLedge(false);
                return;
            }
        }

        // Hang.
        float hangLow = ReloadedLedgeTest && pc.bInWallJumpSalto ? K.GE.LedgeWindowLowSalto : K.GE.LedgeWindowLow;
        if (!LedgeHangAllowed(p, sb, ge, c, n, hangLow, out FVector from, out FVector hang))
        {
            return;
        }

        if (Clear(p, from, hang, box) && Clear(p, hang, hang + new FVector(0f, 0f, K.LedgeClearanceRaise), box))
        {
            pc.eventLedge();
        }
    }

    /// <summary>The hang rules of the ledge test before the clearance traces; gives the trace start and the hang spot.</summary>
    private static bool LedgeHangAllowed(Pawn p, ISBasePawnPhysics sb, GERecord ge, FVector c, FVector n, float windowLow, out FVector from,
        out FVector hang)
    {
        FVector loc = p.Location;
        FVector nh = new FVector(n.X, n.Y, 0f).SafeNormal();
        hang = new FVector(c.X + sb.m_LedgeGrabArmsZone.X * nh.X, c.Y + sb.m_LedgeGrabArmsZone.X * nh.Y, c.Z - sb.m_LedgeGrabArmsZone.Z);
        float refZ = loc.Z + p.CollisionHeight - K.StandingCollisionHeight;
        from = new FVector(loc.X, loc.Y, refZ);
        float endDist = SvmMath.Min(FVector.Dist(c, ge.P0), FVector.Dist(c, ge.P1));
        float hx = hang.X - loc.X;
        float hy = hang.Y - loc.Y;
        return endDist >= K.GE.LedgeEndMargin && p.Velocity.Z > K.GE.LedgeMinVelocityZ && refZ > hang.Z + windowLow &&
               refZ < hang.Z + K.GE.LedgeWindowHigh && hx * hx + hy * hy < K.GE.LedgeDistSq;
    }

    /// <summary>A clearance box trace (flags 0x2df) that hits nothing.</summary>
    private bool Clear(Pawn p, FVector from, FVector to, FVector extent, ETraceFlags flags = K.Flags.Clearance) =>
        !Collision.LineCheck(out _, p, to, from, extent, flags);

    /// <summary>The HOH arm test: while falling, between the ends, rising slowly, under the bar: HandOverHand.</summary>
    [UcNative("ASPlayerController::CheckHOHArms", Exe = 0x10aa8f90, Editor = 0x111ed7a0)]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rule 5 (CheckHOHArms)")]
    private static void CheckHOHArms(Pawn p, ISBasePawnPhysics sb, ISPlayerControllerPhysics pc, GERecord ge, FVector z)
    {
        if (p.Physics != EPhysics.PHYS_Falling)
        {
            return;
        }

        FVector c = GEClosestPoint(ge, z, out FVector dir);
        if ((dir | p.Rotation.Vector()) < 0f)
        {
            dir = -dir;
        }

        float hz = c.Z - (sb.m_HandOverHandArmsZone.Z + PrePivotChange(p, sb).Z);
        FVector loc = p.Location;
        float dx = loc.X - c.X;
        float dy = loc.Y - c.Y;
        if ((ge.P0 - c).Size2D() > K.GE.HohEndMargin && (ge.P1 - c).Size2D() > K.GE.HohEndMargin && hz - K.GE.HohHeightMargin < loc.Z &&
            p.Velocity.Z > 0f && p.Velocity.Z < K.GE.HohMaxVelocityZ && dx * dx + dy * dy < K.GE.HohDistSq)
        {
            sb.m_geoNormal = dir;
            pc.eventHandOverHand();
        }
    }

    /// <summary>The ladder arm test: NarrowLadder from either side of the ladder when facing and pushing (or falling) toward it.</summary>
    [UcNative("ASPlayerController::CheckLadderArms", Exe = 0x10aa7650, Editor = 0x111ec210)]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rule 5 (CheckLadderArms)")]
    private static void CheckLadderArms(Pawn p, ISBasePawnPhysics sb, ISPlayerControllerPhysics pc, GERecord ge, FVector z)
    {
        GEClosestPoint(ge, z, out FVector n);
        sb.m_geoNormal = n;
        FVector loc = p.Location;
        FVector bottom = ge.P0;
        FVector top = ge.P1;
        if (top.Z <= loc.Z + p.CollisionHeight)
        {
            return;
        }

        if (p.bIsCrouched && !sb.UnCrouch(true))
        {
            return;
        }

        FVector fwd = p.Rotation.Vector();
        FVector acc = p.Acceleration.SafeNormal();
        FVector vel = new FVector(p.Velocity.X, p.Velocity.Y, 0f).SafeNormal();
        bool airborne = p.Physics is EPhysics.PHYS_Falling or EPhysics.PHYS_RootMotion;
        for (int side = 0; side < 2; side++)
        {
            float sgn = side == 0 ? 1f : -1f;
            var spot = new FVector(bottom.X + sgn * K.GE.LadderSpot * n.X, bottom.Y + sgn * K.GE.LadderSpot * n.Y, loc.Z);
            if ((spot - loc).Size2D() >= K.GE.LadderSpotRadius)
            {
                continue;
            }

            float nf = sgn * (n | fwd);
            if ((nf < -K.GE.LadderFacing && sgn * (n | acc) < -K.GE.LadderFacing) ||
                (airborne && sgn * (n | vel) < -K.GE.LadderFacing && nf < -K.GE.LadderFacingAir))
            {
                pc.eventNarrowLadder();
                return;
            }
        }
    }

    /// <summary>The pipe arm test: enough pipe above and below, at the grab spot, facing and pushing toward it: Pipe.</summary>
    [UcNative("ASPlayerController::CheckPipeArms", Exe = 0x10aa7360, Editor = 0x111ebf10)]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rule 5 (CheckPipeArms)")]
    private static void CheckPipeArms(Pawn p, ISBasePawnPhysics sb, ISPlayerControllerPhysics pc, GERecord ge, FVector z)
    {
        GEClosestPoint(ge, z, out FVector n);
        FVector loc = p.Location;
        FVector bottom = ge.P0;
        FVector top = ge.P1;
        float crouchLift = p.bIsCrouched ? K.StandingCollisionHeight - p.CollisionHeight : 0f;
        if (top.Z - loc.Z < K.GE.PipeMinAbove || loc.Z + crouchLift - bottom.Z < K.GE.PipeMinAbove)
        {
            return;
        }

        sb.m_geoNormal = n;
        var spot = new FVector(bottom.X + K.GE.PipeSpot * n.X, bottom.Y + K.GE.PipeSpot * n.Y, loc.Z);
        FVector fwd = p.Rotation.Vector();
        if ((spot - loc).Size2D() < K.GE.PipeSpotRadius && (n | fwd) < K.GE.GrabFacing &&
            ((n | p.Acceleration.SafeNormal()) < K.GE.GrabFacing || p.Physics == EPhysics.PHYS_RootMotion))
        {
            FVector end = bottom + n * sb.m_PipeArmsZone.X + new FVector(0f, 0f, p.CollisionHeight + K.GE.PipeHandsAbove);
            pc.eventPipe(end, Mask4((-n).Rotation()));
        }
    }

    /// <summary>The zip arm test: falling at a moderate speed with the hang point inside the zip zone: ZipLine.</summary>
    [UcNative("ASPlayerController::CheckZipArms", Exe = 0x10aa7140, Editor = 0x111ebcd0)]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rule 5 (CheckZipArms)")]
    private static void CheckZipArms(Pawn p, ISBasePawnPhysics sb, ISPlayerControllerPhysics pc, GERecord ge, FVector z)
    {
        FVector c = GEClosestPoint(ge, z, out FVector dir);
        FVector hang = c - new FVector(0f, 0f, sb.m_ZipLineArmsZone.Z);
        float r = sb.m_ZipLineArmsRadius;
        if (p.Physics == EPhysics.PHYS_Falling && p.Velocity.Z > K.GE.ZipMinVelocityZ && p.Velocity.Z < K.GE.ZipMaxVelocityZ &&
            (hang - p.Location).SizeSquared() < r * r)
        {
            sb.m_geoNormal = dir;
            pc.eventZipLine(hang, Mask4(new FVector(dir.X, dir.Y, 0f).SafeNormal().Rotation()));
        }
    }

    /// <summary>
    /// The fence top test (physOnVerticalPlane while climbing): the fence top below the arms zone and room up 127.5,
    /// over 75 and down 198.25 with a (35, 35, 20) box: FenceTop. True when raised.
    /// </summary>
    [UcNative("ASPlayerController::CheckFenceTopArms", Exe = 0x10aa4ff0, Editor = 0x111ea1e0)]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rule 5 (CheckFenceTopArms)")]
    private bool CheckFenceTopArms(Pawn p, ISBasePawnPhysics sb, ISPlayerControllerPhysics pc, GERecord ge)
    {
        FVector z = ArmsZonePoint(p, sb.m_CurrentArmsZone);
        FVector c = GEClosestPoint(ge, z, out FVector dir);
        FVector n = LedgeNormal(dir, p.Rotation.Vector());
        if (c.Z >= z.Z)
        {
            return false;
        }

        FVector box = K.GE.FenceTopBox;
        FVector a = p.Location;
        FVector b = a + new FVector(0f, 0f, K.GE.FenceTopUp);
        FVector cc = b - n * K.GE.FenceTopOver;
        FVector d = cc - new FVector(0f, 0f, K.GE.FenceTopDown);
        if (!Clear(p, a, b, box) || !Clear(p, b, cc, box) || !Clear(p, cc, d, box))
        {
            return false;
        }

        FVector zone = sb.m_CurrentArmsZone;
        pc.eventFenceTop(c + n * zone.X - new FVector(0f, 0f, zone.Z), Mask4((-n).Rotation()));
        return true;
    }

    /// <summary>A rotator with every component masked to a multiple of 4.</summary>
    private static FRotator Mask4(FRotator r) => new(r.Pitch & ~3, r.Yaw & ~3, r.Roll & ~3);

    // ---- CheckFeetZone and CheckBackFeetZone --------------------------------------------------------------

    /// <summary>
    /// SPlayerController.CheckFeetZone: the nearest GE in the feet zone; ZipP from it, then LG: FeetLedge, LADDER:
    /// TopLadder from the top, PIPE: TopPipe from the top.
    /// </summary>
    [UcNative("ASPlayerController::execCheckFeetZone", Exe = 0x10aa7a80, Editor = 0x111f26c0)]
    [PortOf("nat:SBase.SPlayerController.CheckFeetZone", Spec = "spec/native/physics-ge-queries.md#6-checkfeetzone")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rule 6")]
    public void CheckFeetZone(Pawn Pawn)
    {
        NotNull(Pawn);
        if (Pawn is not ISBasePawnPhysics sb || Pawn.Controller is not ISPlayerControllerPhysics pc || sb.m_CurrentFeetRadius == 0f)
        {
            return;
        }

        FVector z = FeetZonePoint(Pawn, sb.m_CurrentFeetZone);
        List<GEHit> hits = SearchGEs(Pawn, z, sb.m_CurrentFeetRadius);
        int best = Nearest(hits, 0);
        if (best < 0)
        {
            return;
        }

        GERecord ge = hits[best].GE;
        sb.ZipP1 = ge.P0;
        sb.ZipP2 = ge.P1;
        switch (ge.Kind)
        {
            case GEKind.LG:
                CheckLedgeGrabFeet(Pawn, sb, pc, ge, z);
                break;
            case GEKind.LADDER:
                CheckLadderFeet(Pawn, sb, pc, ge, z);
                break;
            case GEKind.PIPE:
                CheckPipeFeet(Pawn, sb, pc, ge, z);
                break;
        }
    }

    /// <summary>The LG feet test: a low ledge ahead at the feet while not falling fast, with room up and over: FeetLedge.</summary>
    [UcNative("ASPlayerController::CheckLedgeGrabFeet", Exe = 0x10aa8b00, Editor = 0x111ed2f0)]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rule 6 (CheckLedgeGrabFeet)")]
    private void CheckLedgeGrabFeet(Pawn p, ISBasePawnPhysics sb, ISPlayerControllerPhysics pc, GERecord ge, FVector z)
    {
        if (p.Velocity.Z < K.GE.FeetMinVelocityZ)
        {
            return;
        }

        FVector loc = p.Location;
        FVector fwd = p.Rotation.Vector();
        FVector c = GEClosestPoint(ge, z, out FVector dir);
        bool ahead = (fwd | (c - loc)) > 0f;
        var n = new FVector(dir.Y, -dir.X, 0f);
        if (ahead && (fwd | n) > 0f)
        {
            n = -n;
        }

        var l = new FVector(loc.X, loc.Y, StandingZ(p, sb));
        float top = c.Z + K.GE.FeetTop;
        float dx = c.X - l.X;
        float dy = c.Y - l.Y;
        if ((fwd | n) >= K.GE.GrabFacing || !ahead || p.Velocity.Z <= K.GE.FeetMaxFallZ || l.Z < top || l.Z >= top + K.GE.FeetWindow ||
            LocalVelocity(p).X <= K.GE.MinForwardSpeed || dx * dx + dy * dy >= K.GE.FeetDistSq)
        {
            return;
        }

        FVector box = K.LedgeClearanceExtent;
        if (sb.SPawnType == PawnType.SPAWNDEFENSE &&
            !Collision.LineCheck(out _, p, l - new FVector(0f, 0f, K.GE.FeetMercFloorProbe), l, box, K.Flags.Clearance))
        {
            return;
        }

        var up = new FVector(l.X, l.Y, top + K.GE.FeetUp);
        if (Clear(p, l, up, box) && Clear(p, up, up - n * K.GE.FeetOver, box))
        {
            pc.eventFeetLedge();
        }
    }

    /// <summary>The ladder feet test: at a ladder top facing and pushing away from the climbing side, with room: TopLadder.</summary>
    [UcNative("ASPlayerController::CheckLadderFeet", Exe = 0x10aa8640, Editor = 0x111ecf30)]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rule 6 (CheckLadderFeet)")]
    [Provenance(ProvenanceKind.Estimated, "The flags of the two (35, 35, 87.5) segments are not given; the clearance flags 0x2df are used")]
    private void CheckLadderFeet(Pawn p, ISBasePawnPhysics sb, ISPlayerControllerPhysics pc, GERecord ge, FVector z)
    {
        GEClosestPoint(ge, z, out FVector n);
        FVector fwd = p.Rotation.Vector();
        if ((n | fwd) <= K.GE.LadderTopFacing || (n | p.Acceleration.SafeNormal()) <= K.GE.LadderTopFacing ||
            ge.P0.Z >= z.Z + K.GE.LadderTopAbove || ge.P1.Z >= z.Z + K.GE.LadderTopAbove)
        {
            return;
        }

        FVector top = ge.P1.Z >= ge.P0.Z ? ge.P1 : ge.P0;
        float back = K.GE.LadderTopSpot - sb.m_NarrowLadderArmsZone.X;
        var spot = new FVector(top.X - back * n.X, top.Y - back * n.Y, p.Location.Z + PrePivotChange(p, sb).Z);
        FVector loc = p.Location;
        var narrow = new FVector(35f, 35f, K.GE.LadderTopBoxZ);
        var lift = new FVector(0f, 0f, K.GE.LadderTopLift);
        if (!Clear(p, new FVector(loc.X, loc.Y, spot.Z) + lift, spot + lift, narrow, K.Flags.PawnsAndMovers))
        {
            return;
        }

        FVector box = K.LedgeClearanceExtent;
        FVector outPoint = spot + n * K.GE.LadderTopSpot;
        if (!Clear(p, spot, outPoint, box) || !Clear(p, outPoint, outPoint - new FVector(0f, 0f, K.GE.LadderTopDown), box))
        {
            return;
        }

        sb.m_geoNormal = n;
        sb.vVandammePos = spot;
        pc.eventTopLadder();
    }

    /// <summary>The pipe feet test: at a pipe top, facing and pushing over it, with room down the pipe: TopPipe.</summary>
    [UcNative("ASPlayerController::CheckPipeFeet", Exe = 0x10aa8080, Editor = 0x111ec970)]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rule 6 (CheckPipeFeet)")]
    [Provenance(ProvenanceKind.Estimated, "Side choice and segment order read from a confused decompile (open question 2): right side first, forward then side then down")]
    private void CheckPipeFeet(Pawn p, ISBasePawnPhysics sb, ISPlayerControllerPhysics pc, GERecord ge, FVector z)
    {
        GEClosestPoint(ge, z, out FVector n);
        FVector fwd = p.Rotation.Vector();
        FVector acc = p.Acceleration;
        if (acc.SizeSquared2D() >= K.GE.PipeTopStickSq || (n | fwd) <= K.GE.PipeTopFacing || (n | acc.SafeNormal()) <= K.GE.PipeTopFacing ||
            ge.P0.Z >= z.Z || ge.P1.Z >= z.Z)
        {
            return;
        }

        FVector top = ge.P1.Z >= ge.P0.Z ? ge.P1 : ge.P0;
        float back = p.CollisionRadius + K.GE.PipeTopGap;
        var spot = new FVector(top.X - back * n.X, top.Y - back * n.Y, p.Location.Z + K.GE.PipeTopLift);
        float h = p.CollisionHeight;
        // The exit offsets (C_DOWNTOPPIPE) are in the frame of a pawn facing out over the edge, along N.
        new FRotator(0, n.Rotation().Yaw, 0).GetAxes(out FVector x, out FVector y, out _);
        for (int side = 0; side < 2; side++)
        {
            FVector offset = side == 0 ? K.GE.PipeTopRight : K.GE.PipeTopLeft;
            FVector a = spot + x * offset.X;
            FVector b = a + y * offset.Y;
            FVector c = b + new FVector(0f, 0f, offset.Z);
            if (Clear(p, spot, a, new FVector(K.GE.PipeTopNarrowBox, K.GE.PipeTopNarrowBox, h)) && Clear(p, a, b, new FVector(35f, 35f, h)) &&
                Clear(p, b, c, K.LedgeClearanceExtent))
            {
                sb.m_geoNormal = n;
                sb.vVandammePos = spot;
                pc.eventTopPipe(side == 1);
                return;
            }
        }
    }

    /// <summary>
    /// SPlayerController.CheckBackFeetZone: the nearest GE to the back-feet point (the feet zone mirrored behind, yaw
    /// only, at the standing-equivalent height); for an LG, ZipP and vBackFeetNormal (the ledge's horizontal normal by
    /// the facing rule) are written and the result is true.
    /// </summary>
    [UcNative("ASPlayerController::execCheckBackFeetZone", Exe = 0x10aa7e00, Editor = 0x111ec6d0)]
    [UcNative("ASPlayerController::CheckBackLedgeGrabFeet", Editor = 0x111e4ef0)]
    [PortOf("nat:SBase.SPlayerController.CheckBackFeetZone", Spec = "spec/native/physics-ge-queries.md#7-checkbackfeetzone---bool")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rule 7; ANSWERS-physics 14")]
    public bool CheckBackFeetZone(Pawn Pawn)
    {
        NotNull(Pawn);
        if (Pawn is not ISBasePawnPhysics sb || sb.m_CurrentFeetRadius == 0f)
        {
            return false;
        }

        FVector o = sb.m_CurrentFeetZone;
        FVector point = FeetZonePoint(Pawn, new FVector(-o.X, o.Y, o.Z));
        if (sb.ZoneConfig != 0)
        {
            point.Z = point.Z - Pawn.Location.Z + StandingZ(Pawn, sb);
        }

        List<GEHit> hits = SearchGEs(Pawn, point, sb.m_CurrentFeetRadius);
        int best = Nearest(hits, 0);
        if (best < 0 || hits[best].GE.Kind != GEKind.LG)
        {
            return false;
        }

        GERecord ge = hits[best].GE;
        sb.ZipP1 = ge.P0;
        sb.ZipP2 = ge.P1;
        GEClosestPoint(ge, point, out FVector dir);
        var n = new FVector(dir.Y, -dir.X, 0f);
        sb.vBackFeetNormal = (Pawn.Rotation.Vector() | n) <= 0f ? n : -n;
        return true;
    }

    // ---- top-exit tests ------------------------------------------------------------------------------------

    /// <summary>
    /// SPlayerController.TestTopLedge: 1 when the hanging pawn can stand on the ledge (up 210.8, forward 57.8), 2 when
    /// it can crouch there (up 206.1, forward 75.7), 3 when it can also vault over the top, 4 for a barrier above the
    /// ledge (crouched), 0 otherwise. bCrouchOnly returns the crouch result at once.
    /// </summary>
    [UcNative("ASBasePawn::TestTopLedge", Exe = 0x10ae3270, Editor = 0x1120f610)]
    [PortOf("nat:SBase.SPlayerController.TestTopLedge", Spec = "spec/native/physics-ge-queries.md#8-testtopledgebcrouchonly---int")]
    [PortOf("nat:SBase.SPlayerController.TestTopLedge", Spec = "spec/native/natives2-traversal.md#9-testtopledge-result-4-completes-physics-ge-queriesmd-rule-8")]
    [Provenance(ProvenanceKind.Decoded, "physics-ge-queries.md rule 8, natives2-traversal.md rule 9, ANSWERS-moveset.md correction 1")]
    public int TestTopLedge(Pawn Pawn, bool bCrouchOnly)
    {
        NotNull(Pawn);
        Pawn p = Pawn;
        p.Rotation.GetAxes(out FVector fwd, out FVector right, out _);
        FVector loc = p.Location;
        float h = p.CollisionHeight;
        FVector box = K.LedgeClearanceExtent;
        var crouchBox = new FVector(box.X, box.Y, p.CrouchHeight);
        int result = 0;
        if (!bCrouchOnly)
        {
            FVector up = loc + new FVector(0f, 0f, K.GE.TopStandUp);
            if (Clear(p, loc, up, box) && Clear(p, up, up + fwd * K.GE.TopStandForward, box))
            {
                result = 1;
            }
        }

        if (result == 0)
        {
            var start = new FVector(loc.X, loc.Y, loc.Z - (h - p.CrouchHeight));
            FVector up = start + new FVector(0f, 0f, K.GE.TopCrouchUp);
            if (Clear(p, start, up, crouchBox) && Clear(p, up, up + fwd * K.GE.TopCrouchForward, crouchBox))
            {
                result = 2;
            }

            if (bCrouchOnly)
            {
                return result;
            }
        }

        ISBasePawnPhysics? sb = p as ISBasePawnPhysics;
        if (result != 0)
        {
            return VaultClear(p, sb, loc, fwd, K.Flags.Clearance, dropAlongForward: false) ? 3 : result;
        }

        if (sb is null || !FindArmsGE(p, K.GE.TopBarrierZone, sb.m_CurrentArmsRadius, out GERecord? best, out GERecord? second))
        {
            return 0;
        }

        return BarrierAboveClear(p, loc, fwd, right, sb.ZipP1, sb.ZipP2, best, second, K.Flags.Clearance) ? 4 : 0;
    }

    /// <summary>
    /// The vault test of TestTopLedge and CheckIfBarrier: box (35, 35, 44) from L.Z + m_LedgeGrabArmsZone.Z + 45 95
    /// forward, then down to L.Z + 78; both clear.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 9: the rise is m_LedgeGrabArmsZone.Z + 45 (117 + 45 by default)")]
    private bool VaultClear(Pawn p, ISBasePawnPhysics? sb, FVector loc, FVector fwd, ETraceFlags flags, bool dropAlongForward)
    {
        FVector box = K.LedgeClearanceExtent;
        var vaultBox = new FVector(box.X, box.Y, K.GE.TopVaultBoxZ);
        float rise = sb is null ? K.GE.TopVaultRise : sb.m_LedgeGrabArmsZone.Z + K.GE.TopVaultRiseAboveArms;
        var high = new FVector(loc.X, loc.Y, loc.Z + rise);
        FVector over = high + fwd * K.GE.TopVaultForward;
        // CheckIfBarrier measures the drop from the forward point (its Z moves with a pitched rotation).
        float dropBase = dropAlongForward ? loc.Z + fwd.Z * K.GE.TopVaultForward : loc.Z;
        var drop = new FVector(over.X, over.Y, dropBase + K.GE.TopVaultDrop);
        return Clear(p, high, over, vaultBox, flags) && Clear(p, over, drop, vaultBox, flags);
    }

    /// <summary>
    /// TestTopLedge result 4 (natives2-traversal.md rule 9 steps 2 to 7): G is Best when an LG, else Second when an
    /// LG, 90 to 102 above the current ledge (<paramref name="ledgeA"/>, <paramref name="ledgeB"/>); the two side
    /// points inside the ledges; then the four boxes up to G, aside, forward from the top of the first box and up to
    /// L.Z + 173.
    /// </summary>
    private bool BarrierAboveClear(Pawn p, FVector loc, FVector fwd, FVector right, FVector ledgeA, FVector ledgeB,
        GERecord? best, GERecord? second, ETraceFlags flags)
    {
        GERecord? upper = best is { Kind: GEKind.LG } ? best : second is { Kind: GEKind.LG } ? second : null;
        if (upper is null)
        {
            return false;
        }

        float dz = SvmMath.Abs(ledgeA.Z - upper.P0.Z);
        if (!(dz > K.GE.TopBarrierLow && dz < K.GE.TopBarrierHigh) ||
            !InsideSegment(loc - right * K.GE.TopBarrierLeft, upper.P0, upper.P1) ||
            !InsideSegment(loc + right * K.GE.TopBarrierRight, ledgeA, ledgeB))
        {
            return false;
        }

        float h = p.CollisionHeight;
        FVector box = K.LedgeClearanceExtent;
        var standBox = new FVector(box.X, box.Y, h);
        var crouchBox = new FVector(box.X, box.Y, p.CrouchHeight);
        var a = new FVector(loc.X, loc.Y, upper.P0.Z + 2f * (p.CrouchHeight + 1f) - h);
        FVector b = a + right * (K.GE.TopBarrierRight - p.CollisionRadius);
        var aTop = new FVector(a.X, a.Y, a.Z + (h - p.CrouchHeight));
        FVector c = aTop + fwd * K.GE.TopBarrierForward;
        var d = new FVector(c.X, c.Y, loc.Z + K.GE.TopBarrierTop);
        return Clear(p, loc, a, standBox, flags) && Clear(p, a, b, standBox, flags) && Clear(p, aTop, c, crouchBox, flags) &&
               Clear(p, c, d, crouchBox, flags);
    }

    /// <summary>True when <paramref name="p"/> projects strictly inside the segment a-b: 0 &lt; (p - a) . (b - a) &lt; |b - a|^2.</summary>
    private static bool InsideSegment(FVector p, FVector a, FVector b)
    {
        FVector ab = b - a;
        float t = (p - a) | ab;
        return t > 0f && t < ab.SizeSquared();
    }

    /// <summary>
    /// SPlayerController.bTestTopLadder: room to climb off the ladder top (spy up 192 then forward 73.9, merc up 196
    /// then 68.8); <paramref name="iCanUncrouch"/> is 1 when a thin box from 20 below to 79.5 above the exit is clear.
    /// </summary>
    [UcNative("ASPlayerController::bTestTopLadder", Exe = 0x10ab0f10, Editor = 0x111e7c60)]
    [PortOf("nat:SBase.SPlayerController.bTestTopLadder", Spec = "spec/native/physics-ge-queries.md#9-btesttopladderout-icanuncrouch---bool")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rule 9; ANSWERS-physics 20")]
    public bool bTestTopLadder(Pawn Pawn, out int iCanUncrouch)
    {
        NotNull(Pawn);
        iCanUncrouch = 0;
        bool merc = Pawn is ISBasePawnPhysics { SPawnType: PawnType.SPAWNDEFENSE };
        float up = merc ? K.GE.LadderExitUpMerc : K.GE.LadderExitUpSpy;
        float forward = merc ? K.GE.LadderExitForwardMerc : K.GE.LadderExitForwardSpy;
        FVector loc = Pawn.Location;
        FVector top = loc + new FVector(0f, 0f, up);
        FVector exit = top + YawForward(Pawn.Rotation) * forward;
        FVector box = K.LedgeClearanceExtent;
        if (!Clear(Pawn, loc, top, box) || !Clear(Pawn, top, exit, box, K.Flags.PawnsAndMovers))
        {
            return false;
        }

        var thin = new FVector(box.X, box.Y, K.GE.LadderUncrouchBoxZ);
        iCanUncrouch = Clear(Pawn, exit - new FVector(0f, 0f, K.GE.LadderUncrouchBelow), exit + new FVector(0f, 0f, K.GE.LadderUncrouchAbove), thin)
            ? 1
            : 0;
        return true;
    }

    // ---- CheckFence, CheckBarrier, wall jumps ---------------------------------------------------------------

    /// <summary>
    /// SBasePawn.CheckFence: 1 when a climbable flat face is in front of <paramref name="Pos"/> (two opposite probes
    /// and four corner probes on one plane), 2 for a flat face that is not climbable, 0 for none. Writes the face
    /// normal (toward the pawn) and the wall actor. Pure geometry and material, no GE.
    /// </summary>
    [UcNative("ASBasePawn::CheckFence", Exe = 0x10aceb90, Editor = 0x11207de0)]
    [PortOf("nat:SBase.SBasePawn.CheckFence", Spec = "spec/native/physics-ge-queries.md#10-checkfenceout-normal-out-wall-pos---int")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-ge-queries.md rule 10; ANSWERS-physics 21")]
    public int CheckFence(Pawn Pawn, out FVector Normal, out Actor? Wall, FVector Pos) =>
        CheckFence(Pawn, out Normal, out Wall, Pos, out _);

    private int CheckFence(Pawn p, out FVector normal, out Actor? wall, FVector pos, out FVector faceHit)
    {
        NotNull(p);
        normal = default;
        wall = null;
        faceHit = default;
        float r = p.CollisionRadius;
        float h = p.CollisionHeight;
        float reach = r + K.GE.FenceReach;
        FVector fwd = YawForward(p.Rotation);
        FVector P = pos;
        if (p is ISBasePawnPhysics sb)
        {
            P.Z = pos.Z - p.Location.Z + StandingZ(p, sb);
        }

        if (!Collision.LineCheck(out FCheckResult first, p, P + fwd * reach, P, default, K.Flags.Fence) || first.Material < 0)
        {
            return 0;
        }

        FVector n = (first.Normal | fwd) > 0f ? -first.Normal : first.Normal;
        int result = (first.MaterialFlags & OpenScct.Content.Collision.CollisionMaterialFlags.Climbable) != 0 ? 1 : 2;
        if (!FenceProbe(p, P, -n, reach, n, ref result))
        {
            return 0;
        }

        var side = new FVector(-n.Y, n.X, 0f);
        side = side.SafeNormal();
        float s = r + K.GE.FenceCornerSide;
        float u = h - K.GE.FenceCornerHeight;
        for (int i = 0; i < 4; i++)
        {
            FVector corner = P + side * ((i & 1) == 0 ? s : -s) + new FVector(0f, 0f, (i & 2) == 0 ? u : -u);
            if (!FenceProbe(p, corner, -n, reach, n, ref result))
            {
                return 0;
            }
        }

        normal = n;
        wall = first.Actor;
        faceHit = first.Location;
        return result;
    }

    private bool FenceProbe(Pawn p, FVector from, FVector dir, float reach, FVector n, ref int result)
    {
        if (!Collision.LineCheck(out FCheckResult h, p, from + dir * reach, from, default, K.Flags.Fence) || h.Material < 0)
        {
            return false;
        }

        FVector hn = (h.Normal | n) < 0f ? -h.Normal : h.Normal;
        if (SvmMath.Abs(hn.X - n.X) > K.GE.FenceNormalTolerance || SvmMath.Abs(hn.Y - n.Y) > K.GE.FenceNormalTolerance ||
            SvmMath.Abs(hn.Z - n.Z) > K.GE.FenceNormalTolerance)
        {
            return false;
        }

        if ((h.MaterialFlags & OpenScct.Content.Collision.CollisionMaterialFlags.Climbable) == 0)
        {
            result = 2;
        }

        return true;
    }
}
