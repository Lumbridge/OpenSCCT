using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Traversal;
using OpenScct.Provenance;

namespace OpenScct.Physics.Movement;

// The co-op natives: the safe-area test, the mate checks, the short-ladder spot and the hoist climb type
// (spec/native/ANSWERS-moveset.md M1 to M6, completing spec/movement/coop.md).
public sealed partial class PhysicsDriver
{
    /// <summary>
    /// SPlayerController.AreaIsSafeToInteract: the climber's box reaches the spot 70 before the mate (on the mate's
    /// floor) and finds a floor within 15 under it.
    /// </summary>
    [UcNative("ASPlayerController::execAreaIsSafeToInteract", Exe = 0x10aad9f0, Editor = 0x111e6d20)]
    [PortOf("nat:SBase.SPlayerController.AreaIsSafeToInteract", Spec = "spec/native/ANSWERS-moveset.md#m1-areaissafetointeracttarget---bool-editor-exec-0x111e6d20")]
    [Provenance(ProvenanceKind.Decoded, "ANSWERS-moveset.md M1: 70 before the mate, floor 15, box (R, R, H), flags 0x80086")]
    public bool AreaIsSafeToInteract(Pawn Pawn, Actor Target)
    {
        NotNull(Pawn);
        NotNull(Target);
        var extent = new FVector(Pawn.CollisionRadius, Pawn.CollisionRadius, Pawn.CollisionHeight);
        FVector d = (Target.Location - Pawn.Location).SafeNormal();
        FVector spot = Target.Location - d * K.GE.CoopSafeSpot;
        spot.Z = Target.Location.Z - Target.CollisionHeight + Pawn.CollisionHeight;
        if (Collision.LineCheck(out _, Pawn, spot, Pawn.Location, extent, K.GE.CoopSafeFlags))
        {
            return false;
        }

        return Collision.LineCheck(out _, Pawn, spot - new FVector(0f, 0f, K.GE.CoopSafeFloor), spot, extent, K.GE.CoopSafeFlags);
    }

    /// <summary>
    /// The boost-helper test of CheckIfCanInteractWithMate (M2 rule 1, without the spot): the mate at the same height
    /// within <paramref name="MaxDist"/>, the two facing each other, and a clear line from the mate to the pawn.
    /// </summary>
    [UcNative("ASPlayerController::CheckIfCanInteractWithMate", Exe = 0x10aae290, Editor = 0x111effc0)]
    [PortOf("nat:SBase.SPlayerController.CheckIfCanInteractWithMate", Spec = "spec/native/ANSWERS-moveset.md#m2-checkifcaninteractwithmateout-mate---byte-editor-0x111effc0-exec-passes-a-second-argument-0")]
    [Provenance(ProvenanceKind.Decoded, "ANSWERS-moveset.md M2 rule 1: Z within mate R + 15, facing dots 0.75, zero extent trace flags 0x86")]
    public bool CoopBoostMateInReach(Pawn Pawn, Pawn Mate, float MaxDist)
    {
        NotNull(Pawn);
        NotNull(Mate);
        FVector me = Pawn.Location;
        FVector m = Mate.Location;
        float dz = m.Z - me.Z;
        float zWindow = Mate.CollisionRadius + K.GE.CoopBoostZOverRadius;
        float dx = m.X - me.X;
        float dy = m.Y - me.Y;
        if (!(dz > -zWindow && dz < zWindow) || !(dx * dx + dy * dy < MaxDist * MaxDist))
        {
            return false;
        }

        FVector toMate = new FVector(dx, dy, 0f).SafeNormal();
        FVector f = Flat(Pawn.Rotation.Vector());
        FVector fm = Flat(Mate.Rotation.Vector());
        if (!((f | toMate) > K.GE.CoopBoostFacing) || !(-(f | fm) > K.GE.CoopBoostFacing))
        {
            return false;
        }

        return !Collision.LineCheck(out FCheckResult hit, Pawn, me, m, default, K.Flags.MoveWorld) || hit.Actor is null ||
               ReferenceEquals(hit.Actor, Mate);
    }

    /// <summary>
    /// The hanging-helper test of CheckIfCanInteractWithMate (M2 rule 2): the mate 237.36 to 356.04 above within 300,
    /// facing the same way and in front, a hoist climb type under him, and room for the pawn's box to the climb spot.
    /// </summary>
    [UcNative("ASPlayerController::CheckIfCanInteractWithMate", Exe = 0x10aae290, Editor = 0x111effc0)]
    [PortOf("nat:SBase.SPlayerController.CheckIfCanInteractWithMate", Spec = "spec/native/ANSWERS-moveset.md#m2-checkifcaninteractwithmateout-mate---byte-editor-0x111effc0-exec-passes-a-second-argument-0")]
    [Provenance(ProvenanceKind.Decoded, "ANSWERS-moveset.md M2 rule 2: 40 ahead, 237.36 to 356.04, 300, dots 0.2 and 0, box (R, R, 0.85 H) to 130.7 X 296.7 Z, flags 0x286")]
    public bool CoopHoistMateInReach(Pawn Pawn, Pawn Mate)
    {
        NotNull(Pawn);
        NotNull(Mate);
        FVector me = Pawn.Location;
        FVector m = Mate.Location;
        FVector fm = Flat(Mate.Rotation.Vector());
        FVector q = m + fm * K.GE.CoopHoistAhead;
        FVector toQ = new FVector(q.X - me.X, q.Y - me.Y, 0f).SafeNormal();
        FVector f = Flat(Pawn.Rotation.Vector());
        float dz = m.Z - me.Z;
        float dx = m.X - me.X;
        float dy = m.Y - me.Y;
        if (!(dz > K.GE.CoopHoistZLow && dz < K.GE.CoopHoistZHigh) || !(dx * dx + dy * dy < K.GE.CoopHoistReachSquared) ||
            !((f | toQ) > K.GE.CoopHoistFacing) || !((f | fm) > 0f))
        {
            return false;
        }

        if (FindHoistClimbType(Mate, null, m - new FVector(0f, 0f, K.GE.CoopHoistDown), -Mate.Rotation.Vector(), bIgnorePawn: false,
                bQuickCheckBarrier: true) == 0)
        {
            return false;
        }

        Mate.Rotation.GetAxes(out FVector x, out _, out FVector z);
        FVector spot = m - x * K.GE.CoopHoistBack - z * K.GE.CoopHoistDown;
        var extent = new FVector(Pawn.CollisionRadius, Pawn.CollisionRadius, Pawn.CollisionHeight * K.GE.CoopHoistBoxHeightScale);
        return !Collision.LineCheck(out _, Pawn, spot, me, extent, K.Flags.ClearanceNoPawns);
    }

    /// <summary>
    /// SBasePawn.CheckIfCanStartSHL: an LG 502 to 512 above the feet (searched 512 above the feet within 150) with a
    /// boost spot under it; the nearer spot of Best and Second. Loc is the spot, Dir the direction away from the
    /// wall, P1 and P2 the ends of the LG.
    /// </summary>
    [UcNative("ASBasePawn::execCheckIfCanStartSHL", Exe = 0x10ae6ff0, Editor = 0x112125a0)]
    [PortOf("nat:SBase.SBasePawn.CheckIfCanStartSHL", Spec = "spec/native/ANSWERS-moveset.md#m3-checkifcanstartshlout-loc-out-dir---bool-editor-exec-0x112125a0-body-0x112122e0")]
    [Provenance(ProvenanceKind.Decoded, "ANSWERS-moveset.md M3: zone (0, 0, 512 - H), radius 150, the nearer of Best and Second")]
    public bool CheckIfCanStartSHL(Pawn Pawn, bool bIgnorePawn, out FVector Loc, out FVector Dir, out FVector P1, out FVector P2)
    {
        NotNull(Pawn);
        Loc = default;
        Dir = default;
        P1 = default;
        P2 = default;
        var zone = new FVector(0f, 0f, K.GE.ShlSearchHeight - Pawn.CollisionHeight);
        FindArmsGE(Pawn, zone, K.GE.ShlSearchRadius, out GERecord? best, out GERecord? second);
        if (best is not null && ShortLadderSpot(Pawn, best, bIgnorePawn, out Loc, out Dir))
        {
            P1 = best.P0;
            P2 = best.P1;
            if (second is not null && ShortLadderSpot(Pawn, second, bIgnorePawn, out FVector loc2, out FVector dir2) &&
                HorizontalDistanceSquared(loc2, Pawn.Location) < HorizontalDistanceSquared(Loc, Pawn.Location))
            {
                Loc = loc2;
                Dir = dir2;
                P1 = second.P0;
                P2 = second.P1;
            }

            return true;
        }

        if (second is not null && ShortLadderSpot(Pawn, second, bIgnorePawn, out Loc, out Dir))
        {
            P1 = second.P0;
            P2 = second.P1;
            return true;
        }

        return false;
    }

    /// <summary>The short-ladder spot under one LG (M4): the closest point kept off the ends, R + 10 out, 510 under.</summary>
    [Provenance(ProvenanceKind.Decoded, "ANSWERS-moveset.md M4: flat 225, window 502 to 512, across 250 (unit normal) and along 18086 (unscaled), short 192, R + 10, 510")]
    private bool ShortLadderSpot(Pawn p, GERecord ge, bool bIgnorePawn, out FVector Loc, out FVector Dir)
    {
        Loc = default;
        Dir = default;
        FVector g1 = ge.P0;
        FVector g2 = ge.P1;
        float flat = g1.Z - g2.Z;
        float above = g1.Z - p.Location.Z + p.CollisionHeight;
        if (ge.Kind != GEKind.LG || !(flat * flat < K.GE.ShlFlatSquared) || !(above > K.GE.ShlLedgeLow && above < K.GE.ShlLedgeHigh))
        {
            return false;
        }

        // Across: the distance from the ledge line (the unit normal); along: the unscaled product with the ledge.
        var v = new FVector(g2.X - g1.X, g2.Y - g1.Y, 0f);
        var w = new FVector(g2.X - p.Location.X, g2.Y - p.Location.Y, 0f);
        float len = v.Size();
        float across = ((-v.Y / len) * w.X) + ((v.X / len) * w.Y);
        float along = (w.X * v.X) + (w.Y * v.Y);
        if (!(across > -K.GE.ShlAcross && across < K.GE.ShlAcross) || !(along > -K.GE.ShlAlong) ||
            !(along < v.SizeSquared() + K.GE.ShlAlong))
        {
            return false;
        }

        FVector c = ClosestPointOnSegment(p.Location, g1, g2);
        float length = SvmMath.Sqrt(HorizontalDistanceSquared(g1, g2));
        if (length < K.GE.ShlShortLedge)
        {
            c = g1 + (g2 - g1).SafeNormal() * (length * 0.5f);
        }

        float r = p.CollisionRadius;
        if (SvmMath.Sqrt(HorizontalDistanceSquared(c, g1)) < r)
        {
            c = g1 + (g2 - g1).SafeNormal() * r;
        }

        if (SvmMath.Sqrt(HorizontalDistanceSquared(c, g2)) < r)
        {
            c = g2 + (g1 - g2).SafeNormal() * r;
        }

        FVector d = g2 - g1;
        FVector n = new FVector(-d.Y, d.X, 0f).SafeNormal();
        if ((n | (g2 - p.Location).SafeNormal()) >= 0f)
        {
            n = -n;
        }

        FVector extent = GetCylinderExtent(p);
        float out_ = extent.X + K.GE.ShlSpotOut;
        float z = g1.Z - K.GE.ShlSpotDown + extent.Z;
        var s = new FVector(c.X + n.X * out_, c.Y + n.Y * out_, z);
        if (ShortLadderRoom(p, ge, s, n, bIgnorePawn) != 0)
        {
            Loc = s;
            Dir = n;
            return true;
        }

        FVector back = -n;
        s = new FVector(c.X + back.X * out_, c.Y + back.Y * out_, z);
        if (ShortLadderRoom(p, ge, s, back, bIgnorePawn) != 0)
        {
            Loc = s;
            Dir = back;
            return true;
        }

        return false;
    }

    /// <summary>The room test of a short-ladder spot (M4 rule 6): a hoist type there and the pawn's box reaching it.</summary>
    [Provenance(ProvenanceKind.Decoded, "ANSWERS-moveset.md M4 rule 6: the standing centre, flags 0x284 or 0x2df")]
    private byte ShortLadderRoom(Pawn p, GERecord ge, FVector spot, FVector dir, bool bIgnorePawn)
    {
        float defH = p is ISBasePawnPhysics sb ? sb.DefaultCollisionHeight : p.CollisionHeight;
        spot.Z += defH - p.CollisionHeight;
        byte type = FindHoistClimbType(p, ge, spot, dir, bIgnorePawn, bQuickCheckBarrier: false);
        if (type == 0)
        {
            return 0;
        }

        var extent = new FVector(p.CollisionRadius, p.CollisionRadius, p.CollisionHeight);
        ETraceFlags flags = bIgnorePawn ? K.GE.ShlRoomIgnorePawns : K.Flags.Clearance;
        return Collision.LineCheck(out _, p, spot, p.Location, extent, flags) ? (byte)0 : type;
    }

    /// <summary>
    /// SBasePawn.FindHoistClimbType (the script form): the climb under this hanging pawn, from the spot 296.7 under
    /// it, away from the wall.
    /// </summary>
    [UcNative("ASBasePawn::execFindHoistClimbType", Exe = 0x10ae5150, Editor = 0x11215680)]
    [PortOf("nat:SBase.SBasePawn.FindHoistClimbType", Spec = "spec/native/ANSWERS-moveset.md#m5-findhoistclimbtype-editor-body-0x11211130-exec-0x11215680")]
    [Provenance(ProvenanceKind.Decoded, "ANSWERS-moveset.md M5: no GE, P = L - 296.7 Up, D = -vector(Rotation)")]
    public byte FindHoistClimbType(Pawn Pawn, bool bIgnorePawn, bool bQuickCheckBarrier)
    {
        NotNull(Pawn);
        return FindHoistClimbType(Pawn, null, Pawn.Location - new FVector(0f, 0f, K.GE.CoopHoistDown), -Pawn.Rotation.Vector(),
            bIgnorePawn, bQuickCheckBarrier);
    }

    /// <summary>
    /// ASBasePawn::FindHoistClimbType: SHL_Standard (1), SHL_Barrier (2), SHL_StandardNoWall (3) or SHL_BarrierNoWall
    /// (4) for a climber standing at <paramref name="P"/> with <paramref name="D"/> pointing away from the wall; 0 when
    /// there is no floor, no room or no top.
    /// </summary>
    [UcNative("ASBasePawn::FindHoistClimbType", Exe = 0x10ae5150, Editor = 0x11211130)]
    [PortOf("nat:SBase.SBasePawn.FindHoistClimbType", Spec = "spec/native/ANSWERS-moveset.md#m5-findhoistclimbtype-editor-body-0x11211130-exec-0x11215680")]
    [Provenance(ProvenanceKind.Decoded, "ANSWERS-moveset.md M5: floor 83 to 97 (box Z 5), room 126, column 456 (0.96) and 446, quick zone (37, 0, 210) radius 40, hang 8 in, walls 75 at 69 / 230 / 345")]
    public byte FindHoistClimbType(Pawn Pawn, GERecord? Ge, FVector P, FVector D, bool bIgnorePawn, bool bQuickCheckBarrier)
    {
        NotNull(Pawn);
        Pawn p = Pawn;
        ISBasePawnPhysics? sb = p as ISBasePawnPhysics;
        float defR = sb?.DefaultCollisionRadius ?? p.CollisionRadius;
        float defH = sb?.DefaultCollisionHeight ?? p.CollisionHeight;
        var standing = new FVector(defR, defR, defH);
        var thin = new FVector(defR, defR, K.GE.HoistFloorBoxZ);
        var up = new FVector(0f, 0f, 1f);
        if (!Collision.LineCheck(out _, p, P - up * K.GE.HoistFloorTo, P - up * K.GE.HoistFloorFrom, thin, K.Flags.ClearanceNoPawns))
        {
            return 0;
        }

        FVector behind = P + D * K.GE.HoistRoomBehind;
        if (!bIgnorePawn)
        {
            if (Collision.LineCheck(out FCheckResult hit, p, behind, P, standing, K.Flags.ClearanceWithPawns) &&
                hit.Actor is not ISBasePawnPhysics { SPawnType: PawnType.SPAWNOFFENSE })
            {
                return 0;
            }
        }
        else if (Collision.LineCheck(out _, p, behind, P, standing, K.Flags.ClearanceNoPawns))
        {
            return 0;
        }

        FVector top = P + up * K.GE.HoistColumnTop;
        Collision.LineCheck(out FCheckResult column, p, P - up * K.GE.HoistColumnBottom, top, standing, K.Flags.ClearanceNoPawns);
        if (!(column.Time >= K.GE.HoistColumnTime) ||
            Collision.LineCheck(out _, p, top - up * K.GE.HoistColumnBottom, P, standing, K.Flags.ClearanceNoPawns))
        {
            return 0;
        }

        int v;
        if (p.Physics == EPhysics.PHYS_Ledge)
        {
            if (bQuickCheckBarrier && sb is not null)
            {
                // The native sets the radius for this search and leaves it.
                sb.m_CurrentArmsRadius = K.GE.HoistQuickRadius;
                FindArmsGE(p, K.GE.HoistQuickZone, sb.m_CurrentArmsRadius, out GERecord? best, out GERecord? second);
                v = best is { Kind: GEKind.LG } || second is { Kind: GEKind.LG } ? 3 : 1;
            }
            else
            {
                v = TestTopLedge(p, false);
            }
        }
        else
        {
            if (Ge is null)
            {
                return 0;
            }

            var hang = new FVector(P.X - D.X * K.GE.HoistHangIn, P.Y - D.Y * K.GE.HoistHangIn,
                Ge.P0.Z - (sb?.m_LedgeGrabArmsZone.Z ?? 0f));
            v = CheckIfBarrier(p, Ge, hang, (-D).Rotation(), bIgnorePawn);
        }

        if (v == 0)
        {
            return 0;
        }

        bool barrier = v > 2;
        bool wall = false;
        FVector wallPoint = P - D * K.GE.HoistWallReach;
        foreach (float h in HoistWallHeights)
        {
            if (Collision.LineCheck(out _, p, wallPoint + up * h, P + up * h, K.GE.HoistWallBox, K.Flags.ClearanceNoPawns))
            {
                wall = true;
                break;
            }
        }

        return barrier ? (wall ? (byte)2 : (byte)4) : (wall ? (byte)1 : (byte)3);
    }

    /// <summary>
    /// ASBasePawn::CheckIfBarrier: TestTopLedge for a virtual hang at <paramref name="Loc"/> with
    /// <paramref name="Rot"/> on <paramref name="Ge"/>: 2 crouch, 3 vault, 4 barrier above, 0 none (no standing test).
    /// </summary>
    [UcNative("ASBasePawn::CheckIfBarrier", Editor = 0x112108f0)]
    [PortOf("eng:ASBasePawn::CheckIfBarrier", Spec = "spec/native/ANSWERS-moveset.md#m6-checkifbarrierg-l-rot-bignorepawn---byte-editor-0x112108f0")]
    [Provenance(ProvenanceKind.Decoded, "ANSWERS-moveset.md M6: flags 0x286 / 0x287, radius 150 left set, barrier zone (0, 0, 512 - defH + 96)")]
    public int CheckIfBarrier(Pawn Pawn, GERecord? Ge, FVector Loc, FRotator Rot, bool bIgnorePawn)
    {
        NotNull(Pawn);
        if (Ge is null)
        {
            return 0;
        }

        Pawn p = Pawn;
        ISBasePawnPhysics? sb = p as ISBasePawnPhysics;
        ETraceFlags flags = bIgnorePawn ? K.Flags.ClearanceNoPawns : K.Flags.ClearanceWithPawns;
        Rot.GetAxes(out FVector fwd, out FVector right, out _);
        if (sb is not null)
        {
            sb.m_CurrentArmsRadius = K.GE.BarrierSearchRadius;
        }

        float defH = sb?.DefaultCollisionHeight ?? p.CollisionHeight;
        FVector box = K.LedgeClearanceExtent;
        var crouchBox = new FVector(box.X, box.Y, p.CrouchHeight);
        var start = new FVector(Loc.X, Loc.Y, Loc.Z - (defH - p.CrouchHeight));
        FVector up = start + new FVector(0f, 0f, K.GE.TopCrouchUp);
        if (Clear(p, start, up, crouchBox, flags) && Clear(p, up, up + fwd * K.GE.TopCrouchForward, crouchBox, flags))
        {
            return VaultClear(p, sb, Loc, fwd, flags, dropAlongForward: true) ? 3 : 2;
        }

        if (sb is null)
        {
            return 0;
        }

        var zone = new FVector(0f, 0f, K.GE.ShlSearchHeight - defH + K.GE.BarrierSearchAbove);
        FindArmsGE(p, zone, sb.m_CurrentArmsRadius, out GERecord? best, out GERecord? second);
        return BarrierAboveClear(p, Loc, fwd, right, Ge.P0, Ge.P1, best, second, flags) ? 4 : 0;
    }

    private static readonly float[] HoistWallHeights = [K.GE.HoistWallLow, K.GE.HoistWallMid, K.GE.HoistWallHigh];

    private static FVector Flat(FVector v) => new FVector(v.X, v.Y, 0f).SafeNormal();

    private static float HorizontalDistanceSquared(FVector a, FVector b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>The point of segment a-b closest to p (the native's segment helper, editor 0x10fde7a0).</summary>
    private static FVector ClosestPointOnSegment(FVector p, FVector a, FVector b)
    {
        FVector ab = b - a;
        float t = ab | (p - a);
        if (t <= 0f)
        {
            return a;
        }

        float len = ab.SizeSquared();
        return len <= t ? b : a + ab * (t / len);
    }
}
