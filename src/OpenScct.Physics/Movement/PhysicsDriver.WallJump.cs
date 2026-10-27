using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Traversal;
using OpenScct.Provenance;

namespace OpenScct.Physics.Movement;

// The barrier vault, split jump and wall jump natives (spec/native/natives2-traversal.md rules 10 to 12, which
// complete physics-ge-queries.md rules 11 and 12).
public sealed partial class PhysicsDriver
{
    /// <summary>
    /// SPlayerController.CheckBarrier: an LG found with the current arms zone, seen from the ledge-grab zone point: in
    /// front, faced within 0.7, its top 5 below to 40 above the pawn's height reference, the pawn not backing off and
    /// within 30 of the spot R + 2 off it; then room over the top (a (35, 35, 22.5) box from above the pawn 140 beyond
    /// and 20 aside, and down 85.2). Sets ZipP to the ledge.
    /// </summary>
    [UcNative("ASPlayerController::execCheckBarrier", Exe = 0x10aa5390, Editor = 0x111ea600)]
    [UcNative("ASPlayerController::CheckBarrierArms", Exe = 0x10aa3540, Editor = 0x111e8d70)]
    [PortOf("nat:SBase.SPlayerController.CheckBarrier", Spec = "spec/native/natives2-traversal.md#10-checkbarrier-completes-physics-ge-queriesmd-rule-11")]
    [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 10: R + 2, window -5..40 from the reference, 900, 23.5, 140, 20, 85.2, box Z 22.5, flags 0x2df")]
    public bool CheckBarrier(Pawn Pawn)
    {
        NotNull(Pawn);
        Pawn p = Pawn;
        if (p is not ISBasePawnPhysics sb || !FindArmsGE(p, out GERecord? best, out _) || best is not { Kind: GEKind.LG })
        {
            return false;
        }

        FVector zone = ArmsZonePoint(p, sb.m_LedgeGrabArmsZone);
        FVector c = GEClosestPoint(best, zone, out FVector dir);
        FVector fwd = p.Rotation.Vector().SafeNormal();
        FVector loc = p.Location;
        if (!((fwd | (c - loc).SafeNormal()) > 0f))
        {
            return false;
        }

        var n = new FVector(dir.Y, -dir.X, dir.Z);
        if ((fwd | n) > 0f)
        {
            n = -n;
        }

        FVector g = c + n * (p.CollisionRadius + K.GE.WallGap);
        FVector refPoint = loc + PrePivotChange(p, sb);
        float gx = g.X - loc.X;
        float gy = g.Y - loc.Y;
        if (!((n | fwd) < K.GE.GrabFacing) || !(g.Z - K.GE.BarrierHigh < refPoint.Z) || !(refPoint.Z < g.Z - K.GE.BarrierLow) ||
            !(LocalVelocity(p).X > K.GE.MinForwardSpeed) || !(gx * gx + gy * gy < K.GE.BarrierDistSq))
        {
            return false;
        }

        var box = new FVector(35f, 35f, K.GE.BarrierBoxZ);
        var s = new FVector(refPoint.X, refPoint.Y, c.Z + K.GE.BarrierAbove);
        FVector e = s - n * K.GE.BarrierBeyond + (n ^ new FVector(0f, 0f, 1f)) * K.GE.BarrierAside;
        if (!Clear(p, s, e, box) || !Clear(p, e, e - new FVector(0f, 0f, K.GE.BarrierDown), box))
        {
            return false;
        }

        sb.ZipP1 = best.P0;
        sb.ZipP2 = best.P1;
        return true;
    }

    /// <summary>
    /// SPlayerController.CheckWallJump. First jump (nJumpNumber 1): short probes 18 beyond the cylinder (R - 1) at
    /// -0.4 H pick the wall (exactly one side wall, else a wall ahead with neither side); then from 153 - 0.4 H up two
    /// 200 probes along the wall normal both ways must hit, one of them within R + 3, the two summing to [182, 200).
    /// Second jump (nJumpNumber 2): from 0.4 H down, probes 200 along the right axis both ways must hit, a near one
    /// gives HitNormal (the second when both are near), the sum in (180, 198); vVandammePos becomes the midpoint of
    /// the two walls raised by 0.4 H. A probe counts on a static surface only, never on world geometry with a
    /// no-split-jump material.
    /// </summary>
    [UcNative("ASPlayerController::execCheckWallJump", Exe = 0x10aa5a10, Editor = 0x111ea780)]
    [PortOf("nat:SBase.SPlayerController.CheckWallJump", Spec = "spec/native/natives2-traversal.md#11-checkwalljump-helpers-completes-physics-ge-queriesmd-rule-12")]
    [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 11: probe 0x111e4ca0 (flags 0x1084, PHYS_None, no bNoSplitJump BSP), picker 0x111e4df0 (R - 1, 18, -0.4 H), 153, 200, R + 3, [182, 200) and (180, 198)")]
    public bool CheckWallJump(Pawn Pawn, FVector X, FVector Y, FVector Z, ref FVector HitNormal)
    {
        NotNull(Pawn);
        if (Pawn is not ISBasePawnPhysics sb)
        {
            return false;
        }

        float r = Pawn.CollisionRadius;
        float h = Pawn.CollisionHeight;
        float near = r + K.GE.WallJumpNear;
        FVector change = PrePivotChange(Pawn, sb);
        if (sb.nJumpNumber == 2)
        {
            FVector s2 = Pawn.Location - Z * (K.GE.WallJumpHeightH * h) + change;
            if (!SplitWallProbe(Pawn, s2, s2 + Y * K.GE.WallJumpProbe, out FVector l1, out FVector n1) ||
                !SplitWallProbe(Pawn, s2, s2 - Y * K.GE.WallJumpProbe, out FVector l2, out FVector n2))
            {
                return false;
            }

            float d1 = (l1 - s2).Size();
            float d2 = (l2 - s2).Size();
            if (d2 >= near)
            {
                if (d1 >= near)
                {
                    return false;
                }

                HitNormal = new FVector(n1.X, n1.Y, 0f);
            }
            else
            {
                HitNormal = new FVector(n2.X, n2.Y, 0f);
            }

            float sum2 = d1 + d2;
            if (!(sum2 > K.GE.WallJumpSecondMin && sum2 < K.GE.WallJumpSecondMax))
            {
                return false;
            }

            sb.vVandammePos = (l1 + l2) * 0.5f + Z * (K.GE.WallJumpHeightH * h);
            return true;
        }

        if (sb.nJumpNumber != 1)
        {
            return false;
        }

        bool r1 = SplitPick(Pawn, X, out FVector nx);
        bool r2 = SplitPick(Pawn, Y, out FVector ny);
        bool r3 = SplitPick(Pawn, -Y, out FVector nmy);
        bool result = false;
        if (r2 != r3)
        {
            FVector side = r2 ? ny : nmy;
            HitNormal = new FVector(side.X, side.Y, 0f);
            result = true;
        }
        else if (!r2 && r1)
        {
            HitNormal = new FVector(nx.X, nx.Y, 0f);
            result = true;
        }

        FVector s = Pawn.Location + Z * (K.GE.WallJumpStart - K.GE.WallJumpHeightH * h) + change;
        if (!SplitWallProbe(Pawn, s, s + HitNormal * K.GE.WallJumpProbe, out FVector a, out _) ||
            !SplitWallProbe(Pawn, s, s - HitNormal * K.GE.WallJumpProbe, out FVector b, out _))
        {
            return false;
        }

        float da = (a - s).Size();
        float db = (b - s).Size();
        float sum = da + db;
        if ((da >= near && db >= near) || sum >= K.GE.WallJumpFirstMax || sum < K.GE.WallJumpFirstMin)
        {
            return false;
        }

        return result;
    }

    /// <summary>
    /// The direction picker of CheckWallJump (editor 0x111e4df0): a probe from just inside the cylinder (R - 1 along
    /// <paramref name="d"/>, 0.4 H down) 18 further; the hit's normal.
    /// </summary>
    private bool SplitPick(Pawn p, FVector d, out FVector normal)
    {
        FVector start = p.Location + d * (p.CollisionRadius - K.GE.WallJumpPickInset) - new FVector(0f, 0f, K.GE.WallJumpHeightH * p.CollisionHeight);
        return SplitWallProbe(p, start, start + d * K.GE.WallJumpPickReach, out _, out normal);
    }

    /// <summary>
    /// The wall probe of CheckWallJump (editor 0x111e4ca0): a line (flags 0x1084) that counts on an actor in PHYS_None,
    /// not on world geometry whose material forbids the split jump; the normal faces the start.
    /// </summary>
    private bool SplitWallProbe(Pawn p, FVector start, FVector end, out FVector location, out FVector normal)
    {
        location = end;
        normal = default;
        if (!Collision.LineCheck(out FCheckResult hit, p, end, start, default, K.Flags.SplitJumpProbe) ||
            hit.Actor is not { Physics: EPhysics.PHYS_None } actor ||
            (actor is LevelInfo && (hit.MaterialFlags & CollisionMaterialFlags.NoSplitJump) != 0))
        {
            return false;
        }

        location = hit.Location;
        normal = (hit.Normal | (end - start)) > 0f ? -hit.Normal : hit.Normal;
        return true;
    }

    /// <summary>
    /// SPlayerController.CheckGEForWallJump: a flat, static, non-climbable wall 105 to fDistanceForContextualJump ahead
    /// (four probes: at the pawn, 160 up within 10, and 35 either side 72.5 up within 12.75), room to run to it, floor
    /// all the way, and a ledge GE across the wall more than 87.5 above the probes (the lowest one in a 300 high box);
    /// then room to climb from the wall foot to that ledge (or 200 up when it is lower than 100). GEZPosition is the
    /// ledge's height, or the side probes' height when there is none.
    /// </summary>
    [UcNative("ASPlayerController::execCheckGEForWallJump", Exe = 0x10aa6250, Editor = 0x111eaf20)]
    [PortOf("nat:SBase.SPlayerController.CheckGEForWallJump", Spec = "spec/native/natives2-traversal.md#12-checkgeforwalljump-room-boxes-completes-physics-ge-queriesmd-rule-12")]
    [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 12: 105, 160, 72.5, 35, 10, 12.75, boxes 50 / 55 / 130 / 210 and (35, 35, 60) down 50, 40, 100, 200")]
    [Provenance(ProvenanceKind.Estimated, "Probes 3 and 4 take probe 2's tests (static, facing) with their 12.75 window; the LG box search reads the GE segment bounds against the box")]
    public bool CheckGEForWallJump(Pawn Pawn, FVector X, FVector Y, FVector Z, out float GEZPosition)
    {
        NotNull(Pawn);
        GEZPosition = 0f;
        Pawn p = Pawn;
        float k = p.Controller is ISPlayerControllerPhysics pc ? pc.fDistanceForContextualJump : K.GE.GEWallDefaultReach;
        FVector d = X;
        FVector s = Z ^ X;
        FVector l = p.Location;
        if (!GEWallProbe(p, l, d, k, material: true, out FCheckResult first))
        {
            return false;
        }

        float d1 = HorizontalDistance(l, first.Location);
        if (d1 < K.GE.GEWallMin || d1 > k)
        {
            return false;
        }

        var high = new FVector(l.X, l.Y, l.Z + K.GE.GEWallHigh);
        if (!GEWallProbe(p, high, d, k, material: false, out FCheckResult second) ||
            !(SvmMath.Abs(HorizontalDistance(high, second.Location) - d1) <= K.GE.GEWallConsistency))
        {
            return false;
        }

        FVector mid = new FVector(l.X, l.Y, l.Z + K.GE.GEWallMidHeight) + s * K.GE.GEWallSide;
        if (!GEWallProbe(p, mid, d, k, material: false, out FCheckResult third) ||
            !(SvmMath.Abs(HorizontalDistance(mid, third.Location) - d1) <= K.GE.GEWallConsistencySide))
        {
            return false;
        }

        FVector other = mid - s * (2f * K.GE.GEWallSide);
        if (!GEWallProbe(p, other, d, k, material: false, out FCheckResult fourth) ||
            !(SvmMath.Abs(HorizontalDistance(other, fourth.Location) - d1) <= K.GE.GEWallConsistencySide))
        {
            return false;
        }

        if (!Clear(p, l, l + d * (d1 - K.GE.GEWallRoomShort), K.LedgeClearanceExtent))
        {
            return false;
        }

        var floorBox = new FVector(35f, 35f, K.GE.GEWallFloorBoxZ);
        if (!FloorAt(p, l + d * (d1 - K.GE.GEWallFloorNear), floorBox) ||
            (d1 > K.GE.GEWallFloorMid && !FloorAt(p, l + d * (d1 - K.GE.GEWallFloorMid), floorBox)) ||
            (d1 > K.GE.GEWallFloorFar && !FloorAt(p, l + d * (d1 - K.GE.GEWallFloorFar), floorBox)))
        {
            return false;
        }

        FVector q = fourth.Location + s * K.GE.GEWallSide;
        float gez = q.Z;
        float bestAbove = K.GE.GEWallLedgeBox.Z;
        FVector pawnFwd = p.Rotation.Vector();
        byte mask = p.Level.GEActivationMask;
        for (int i = 0; i < GEs.Count; i++)
        {
            GERecord ge = GEs[i];
            if (ge.Kind != GEKind.LG || !ge.IsActive(mask) || !SegmentMeetsBox(ge.P0, ge.P1, q - new FVector(K.GE.GEWallLedgeSearch, K.GE.GEWallLedgeSearch, 0f),
                    q + new FVector(K.GE.GEWallLedgeSearch, K.GE.GEWallLedgeSearch, K.GE.GEWallLedgeBox.Z)))
            {
                continue;
            }

            FVector along = ge.P0 - ge.P1;
            float len = along.Size();
            float above = ge.P0.Z - q.Z;
            if (len <= 0f || !(SvmMath.Abs(along | pawnFwd) / len < K.GE.GEWallLedgeCos) || !(above > K.GE.GEWallLedgeAbove) || !(above < bestAbove))
            {
                continue;
            }

            FVector cp = GEIndex.ClosestPointOnSegment(ge.P0, ge.P1, l, out _);
            if (!(SvmMath.Abs(HorizontalDistance(l, cp) - d1) <= K.GE.GEWallConsistencySide))
            {
                continue;
            }

            bestAbove = above;
            gez = ge.P0.Z;
        }

        FVector t = l + d * (d1 - K.GE.GEWallClimbBack);
        float top = gez - t.Z >= K.GE.GEWallClimbMin ? gez : t.Z + K.GE.GEWallNoLedgeZ;
        if (!Clear(p, t, new FVector(t.X, t.Y, top), K.LedgeClearanceExtent))
        {
            return false;
        }

        GEZPosition = gez;
        return true;
    }

    /// <summary>A CheckGEForWallJump probe: a static, wall-facing surface (and, for the first one, not climbable).</summary>
    private bool GEWallProbe(Pawn p, FVector start, FVector dir, float length, bool material, out FCheckResult hit)
    {
        if (!Collision.LineCheck(out hit, p, start + dir * length, start, default, K.Flags.WallJumpGE) ||
            hit.Actor is not { Physics: EPhysics.PHYS_None })
        {
            return false;
        }

        if (material && (hit.MaterialFlags & CollisionMaterialFlags.Climbable) != 0)
        {
            return false;
        }

        return (hit.Normal | dir) <= K.GE.GEWallFacing;
    }

    /// <summary>A (35, 35, 60) box dropped 50 from <paramref name="at"/> finds a floor.</summary>
    private bool FloorAt(Pawn p, FVector at, FVector box) =>
        Collision.LineCheck(out _, p, at - new FVector(0f, 0f, K.GE.GEWallFloorDrop), at, box, K.Flags.Clearance);

    private static float HorizontalDistance(FVector a, FVector b)
    {
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        return SvmMath.Sqrt(dx * dx + dy * dy);
    }

    private static bool SegmentMeetsBox(FVector a, FVector b, FVector min, FVector max) =>
        SvmMath.Min(a.X, b.X) <= max.X && SvmMath.Max(a.X, b.X) >= min.X && SvmMath.Min(a.Y, b.Y) <= max.Y &&
        SvmMath.Max(a.Y, b.Y) >= min.Y && SvmMath.Min(a.Z, b.Z) <= max.Z && SvmMath.Max(a.Z, b.Z) >= min.Z;
}
