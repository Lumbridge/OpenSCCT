using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Physics.Movement;
using OpenScct.Physics.Traversal;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The SPlayerController natives of the vertical pipe neighbours, the ledge beside a pipe, the merc grab from a hang
// and the hand-over-hand legs-over stance (spec/native/natives2-traversal.md rules 4 to 7,
// spec/native/ANSWERS-moveset.md 3), and the script's TestRoomAfterPipe4.
public partial class SPlayerController
{
    /// <summary>
    /// A neighbouring vertical pipe across the current one: the GEs whose bounds meet a 5 uu box at the pipe's far
    /// side (m_PipeArmsZone.X + 16 ahead), the nearest under 10; only a PIPE counts (its points and the normal toward
    /// its climber).
    /// </summary>
    [UcNative("ASPlayerController::execCheckForOtherPipeV", Exe = 0x10aa4110, Editor = 0x111e92a0)]
    [PortOf("nat:SBase.SPlayerController.CheckForOtherPipeV", Spec = "spec/native/natives2-traversal.md#4-checkforotherpipevout-vzipp1-out-vzipp2-out-vnormal---bool")]
    [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 4: Q = L + (zone.X + 16) Fwd, box 5, nearest under 10, PIPE only")]
    [Provenance(ProvenanceKind.Estimated, "The original's k-d tree box query is read as the GE segment bounds meeting the box")]
    public bool CheckForOtherPipeV(out FVector vZipP1, out FVector vZipP2, out FVector vNormal)
    {
        vZipP1 = default;
        vZipP2 = default;
        vNormal = default;
        if (SBPawn is not { } pawn || MovementNatives.Driver(pawn) is not { } driver)
        {
            return false;
        }

        FVector q = pawn.Location + pawn.Rotation.Vector() * (pawn.m_PipeArmsZone.X + MovementK.PipeNeighbourExtra);
        GERecord? best = null;
        float bestDist = MovementK.PipeNeighbourDistance;
        GEIndex ges = driver.GEs;
        byte mask = pawn.Level.GEActivationMask;
        for (int i = 0; i < ges.Count; i++)
        {
            GERecord ge = ges[i];
            if (!ge.IsActive(mask) || !SegmentBoundsMeetBox(ge.P0, ge.P1, q, MovementK.PipeNeighbourBox))
            {
                continue;
            }

            float d = (GEIndex.ClosestPointOnSegment(ge.P0, ge.P1, q, out _) - q).Size();
            if (d < bestDist)
            {
                bestDist = d;
                best = ge;
            }
        }

        if (best is not { Kind: GEKind.PIPE })
        {
            return false;
        }

        vZipP1 = best.P0;
        vZipP2 = best.P1;
        vNormal = (best.Points[2] - best.P0).SafeNormal();
        return true;
    }

    /// <summary>
    /// Room for the swing round a vertical pipe to its far side: a standing box aside by m_PipeArmsZone.X + 8, then
    /// forward 2 m_PipeArmsZone.X + 16, then back in behind the pipe; every leg clear.
    /// </summary>
    [UcNative("ASPlayerController::execCheckRoomToChangePipeV", Exe = 0x10aa3e10, Editor = 0x111e4960)]
    [PortOf("nat:SBase.SPlayerController.CheckRoomToChangePipeV", Spec = "spec/native/natives2-traversal.md#5-checkroomtochangepipevbtoleft---bool")]
    [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 5: boxes (35, 35, 87.5), flags 0x2df, d = zone.X + 8, f = 2 zone.X + 16")]
    public bool CheckRoomToChangePipeV(bool bToLeft)
    {
        if (SBPawn is not { } pawn)
        {
            return false;
        }

        pawn.Rotation.GetAxes(out FVector fwd, out FVector right, out _);
        float d = pawn.m_PipeArmsZone.X + MovementK.PipeChangeAside;
        float f = 2f * pawn.m_PipeArmsZone.X + MovementK.PipeChangeForward;
        FVector s = right * (bToLeft ? -d : d);
        FVector l = pawn.Location;
        return BoxClear(pawn, l, l + s, MovementK.StandingBox) && BoxClear(pawn, l + s, l + s + fwd * f, MovementK.StandingBox) &&
               BoxClear(pawn, l + s + fwd * f, l + fwd * f, MovementK.StandingBox);
    }

    /// <summary>
    /// A ledge beside the pipe on that side: an LG found with the arms zone (40, +-40, 90.3) radius 60 whose ends are
    /// both clear of the reach point, and room to swing to it (down 27 and 45 aside, then 14 forward). Sets
    /// NewZipP1 and NewZipP2 to the ledge.
    /// </summary>
    [UcNative("ASPlayerController::execbCanTakeLedgeFromPipe", Exe = 0x10ab1de0, Editor = 0x111e81e0)]
    [PortOf("nat:SBase.SPlayerController.bCanTakeLedgeFromPipe", Spec = "spec/native/natives2-traversal.md#6-bcantakeledgefrompipebonright---bool")]
    [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 6: zone (40, 40 s, 90.3) r 60, Q = L + 14 Fwd - 45 s Right, ends squared above 35, boxes (35, 35, 87.5) 0x2df")]
    public bool bCanTakeLedgeFromPipe(bool bOnRight)
    {
        if (SBPawn is not { } pawn || MovementNatives.Driver(pawn) is not { } driver)
        {
            return false;
        }

        float s = bOnRight ? -1f : 1f;
        var zone = new FVector(MovementK.PipeLedgeZoneX, MovementK.PipeLedgeZoneY * s, MovementK.PipeLedgeZoneZ);
        driver.FindArmsGE(pawn, zone, MovementK.PipeLedgeZoneRadius, out GERecord? best, out GERecord? second);
        GERecord? g = best is { Kind: GEKind.LG } ? best : second is { Kind: GEKind.LG } ? second : null;
        if (g is null)
        {
            return false;
        }

        var yaw = new FRotator(0, pawn.Rotation.Yaw, 0);
        yaw.GetAxes(out FVector fwd, out FVector right, out _);
        FVector l = pawn.Location;
        FVector q = l + fwd * MovementK.PipeLedgeForward - right * (MovementK.PipeLedgeAside * s);
        FVector c = PhysicsDriver.GEClosestPoint(g, q, out _);
        if (!((g.P0 - c).SizeSquared() > MovementK.PipeLedgeEndMarginSq) || !((g.P1 - c).SizeSquared() > MovementK.PipeLedgeEndMarginSq))
        {
            return false;
        }

        FVector e = new FVector(l.X, l.Y, l.Z - MovementK.PipeLedgeDown) - right * (MovementK.PipeLedgeAside * s);
        if (!BoxClear(pawn, l, e, MovementK.StandingBox) || !BoxClear(pawn, e, e + fwd * MovementK.PipeLedgeForward, MovementK.StandingBox))
        {
            return false;
        }

        pawn.NewZipP1 = g.P0;
        pawn.NewZipP2 = g.P1;
        return true;
    }

    /// <summary>
    /// A second ledge 90 to 102 above or below the current one, lined up with it within 10, and wide enough for the
    /// controller's spot 20 either side: the geometry for grabbing a merc from the hang. Writes nothing.
    /// </summary>
    [UcNative("ASPlayerController::execbTestGrabLedgeGeo", Exe = 0x10aac090, Editor = 0x111e6980)]
    [PortOf("nat:SBase.SPlayerController.bTestGrabLedgeGeo", Spec = "spec/native/natives2-traversal.md#7-btestgrabledgegeo---bool")]
    [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 7: zone (37, 0, 210), window 90..102, alignment squared 100, 20 aside")]
    public bool bTestGrabLedgeGeo()
    {
        if (SBPawn is not { } pawn || MovementNatives.Driver(pawn) is not { } driver)
        {
            return false;
        }

        driver.FindArmsGE(pawn, MovementK.GrabLedgeGeoZone, pawn.m_CurrentArmsRadius, out GERecord? best, out GERecord? second);
        GERecord? g = best is { Kind: GEKind.LG } ? best : second is { Kind: GEKind.LG } ? second : null;
        if (g is null)
        {
            return false;
        }

        float dz = SvmMath.Abs(pawn.ZipP1.Z - g.P0.Z);
        if (!(dz > MovementK.GrabLedgeGeoMin && dz < MovementK.GrabLedgeGeoMax))
        {
            return false;
        }

        FVector a = GEIndex.ClosestPointOnSegment(pawn.ZipP1, pawn.ZipP2, pawn.Location, out _);
        FVector b = GEIndex.ClosestPointOnSegment(g.P0, g.P1, pawn.Location, out _);
        float hx = a.X - b.X;
        float hy = a.Y - b.Y;
        if (!(hx * hx + hy * hy < MovementK.GrabLedgeGeoAlignSq))
        {
            return false;
        }

        pawn.Rotation.GetAxes(out _, out FVector right, out _);
        FVector cl = Location;
        return StrictlyInside(g, cl - right * MovementK.GrabLedgeGeoAside) && StrictlyInside(g, cl + right * MovementK.GrabLedgeGeoAside);
    }

    /// <summary>
    /// Room for the legs-over stance on the bar: the hands' point 95 ahead must still be on the bar, and a standing box
    /// 70 ahead clear (spec/native/ANSWERS-moveset.md 3).
    /// </summary>
    [UcNative("ASPlayerController::execbCanGoInPipe4", Exe = 0x10aab310, Editor = 0x111e6680)]
    [PortOf("nat:SBase.SPlayerController.bCanGoInPipe4", Spec = "spec/native/ANSWERS-moveset.md#3-bcangoinpipe4")]
    [Provenance(ProvenanceKind.Decoded, "ANSWERS-moveset.md 3: P = L + m_HandOverHandArmsZone + 95 Fwd strictly between the bar ends along Fwd; box (35, 35, 87.5) 70 ahead, 0x2df")]
    public bool bCanGoInPipe4()
    {
        if (SBPawn is not { } pawn)
        {
            return false;
        }

        FVector fwd = pawn.Rotation.Vector();
        FVector p = pawn.Location + pawn.m_HandOverHandArmsZone + fwd * MovementK.HohLegsOverReach;
        FVector c = ProjectOnLine(p, pawn.ZipP1, pawn.ZipP2);
        if (!(((c - pawn.ZipP2) | fwd) * ((c - pawn.ZipP1) | fwd) < 0f))
        {
            return false;
        }

        FVector l = pawn.Location;
        return BoxClear(pawn, l, l + fwd * MovementK.HohLegsOverRoom, MovementK.StandingBox);
    }

    /// <summary>
    /// Room to leave the legs-over stance: a (35, 35, 27.5) box 70 ahead, then down 120, then from 60 up there a
    /// standing box back 70 must all be clear (script traces with actors).
    /// </summary>
    [Uc("SBase.SPlayerController.TestRoomAfterPipe4", Sha1 = "c19c743ab91027289f8e1b996414e87aa5c0eefc")]
    [PortOf("uc:SBase.SPlayerController:TestRoomAfterPipe4", Spec = "spec/movement/pipe-horizontal.md#stance-4-legs-over-the-pipe")]
    public bool TestRoomAfterPipe4()
    {
        if (Pawn is not { } pawn)
        {
            return false;
        }

        pawn.Rotation.GetAxes(out FVector x, out _, out _);
        var legs = new FVector(MovementK.StandingBox.X, MovementK.StandingBox.Y, MovementK.Pipe4BodyHalfHeight);
        FVector start = pawn.Location;
        FVector end = start + x * MovementK.Pipe4RoomAhead;
        if (MovementNatives.Trace(pawn, out _, out _, end, start, true, legs) is not null)
        {
            return false;
        }

        FVector below = end - new FVector(0f, 0f, MovementK.Pipe4RoomDown);
        if (MovementNatives.Trace(pawn, out _, out _, below, end, true, legs) is not null)
        {
            return false;
        }

        FVector up = below + new FVector(0f, 0f, MovementK.Pipe4RoomUp);
        return MovementNatives.Trace(pawn, out _, out _, up - x * MovementK.Pipe4RoomAhead, up, true, MovementK.StandingBox) is null;
    }

    /// <summary>The projection of <paramref name="p"/> on the line through a and b (not clamped to the segment).</summary>
    private static FVector ProjectOnLine(FVector p, FVector a, FVector b)
    {
        FVector d = b - a;
        float len2 = d.SizeSquared();
        return len2 < 1e-8f ? a : a + d * (((p - a) | d) / len2);
    }

    /// <summary>The point projects strictly inside the GE's segment (parameter in (0, 1)).</summary>
    private static bool StrictlyInside(GERecord g, FVector p)
    {
        FVector d = g.P1 - g.P0;
        float len2 = d.SizeSquared();
        if (len2 < 1e-8f)
        {
            return false;
        }

        float t = ((p - g.P0) | d) / len2;
        return t > 0f && t < 1f;
    }

    private static bool SegmentBoundsMeetBox(FVector a, FVector b, FVector c, float half) =>
        SvmMath.Min(a.X, b.X) <= c.X + half && SvmMath.Max(a.X, b.X) >= c.X - half &&
        SvmMath.Min(a.Y, b.Y) <= c.Y + half && SvmMath.Max(a.Y, b.Y) >= c.Y - half &&
        SvmMath.Min(a.Z, b.Z) <= c.Z + half && SvmMath.Max(a.Z, b.Z) >= c.Z - half;
}
