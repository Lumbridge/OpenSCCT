using System;
using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Physics.Traversal;

/// <summary>
/// GE and geometry helpers over a map's GE index: zone searches by kind, the ledge clearance test and the hang spot.
/// The zone natives themselves (CheckArmsZone, CheckFeetZone and the per-kind tests) are on <see cref="Movement.PhysicsDriver"/>.
/// </summary>
public sealed class GEQueries
{
    public GEQueries(GEIndex index, ICollisionWorld collision)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(collision);
        Index = index;
        Collision = collision;
    }

    public GEIndex Index { get; }

    public ICollisionWorld Collision { get; }

    /// <summary>
    /// GEs in a pawn's search zone (the zone's sphere centred at its offset turned by the pawn yaw), of the kinds
    /// in <paramref name="kindMask"/> and active under the level's GEActivationMask, in GE list order.
    /// </summary>
    public int FindInZone(FVector location, FRotator rotation, GEZone zone, int kindMask, byte activationMask, List<GEHit> results) =>
        Index.QuerySphere(zone.CenterFor(location, rotation), zone.Radius, kindMask, activationMask, results);

    /// <summary><see cref="FindInZone(FVector, FRotator, GEZone, int, byte, List{GEHit})"/> for a pawn in its level.</summary>
    public int FindInZone(Pawn pawn, GEZone zone, int kindMask, List<GEHit> results)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        return FindInZone(pawn.Location, pawn.Rotation, zone, kindMask, pawn.Level.GEActivationMask, results);
    }

    /// <summary>
    /// The ledge clearance test: two box traces with extent (35, 35, 87.5) and flags 0x2DF, from
    /// <paramref name="from"/> to the hang spot and to the hang spot raised by 50. True when both hit nothing,
    /// which a Ledge event requires.
    /// </summary>
    [UcNative("ASPlayerController::CheckLedgeArms", Exe = 0x10AA4420, Editor = 0x111e95d0)]
    [Provenance(ProvenanceKind.Decoded, "The two clearance traces of CheckLedgeArms (spec/native/physics-ge-queries.md 4)")]
    public bool TestLedgeClearance(Actor? pawn, FVector from, FVector hangSpot)
    {
        FVector extent = K.LedgeClearanceExtent;
        if (Collision.LineCheck(out _, pawn, hangSpot, from, extent, K.LedgeClearanceTraceFlags))
        {
            return false;
        }

        FVector raised = hangSpot + new FVector(0f, 0f, K.LedgeClearanceRaise);
        return !Collision.LineCheck(out _, pawn, raised, from, extent, K.LedgeClearanceTraceFlags);
    }

    /// <summary>
    /// Horizontal unit normal of a ledge's wall, pointing to the side <paramref name="pawnLocation"/> is on (the
    /// side a spy hangs from). Zero for a vertical or zero-length GE.
    /// </summary>
    public static FVector LedgeOutNormal(GERecord ledge, FVector pawnLocation)
    {
        ArgumentNullException.ThrowIfNull(ledge);
        FVector d = ledge.P1 - ledge.P0;
        FVector n = new FVector(-d.Y, d.X, 0f).SafeNormal();
        if (n.IsZero())
        {
            return n;
        }

        FVector closest = GEIndex.ClosestPointOnSegment(ledge.P0, ledge.P1, pawnLocation, out _);
        return ((pawnLocation - closest) | n) >= 0f ? n : -n;
    }

    /// <summary>
    /// Where a spy hangs from a ledge (the Ledge test's hang spot): the ledge's closest point to the hands, out by the
    /// ledge grab zone's X (39) along the wall normal on the pawn's side, and its Z (117) below.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "CheckLedgeArms game 0x10aa4420: Hang = (C.XY + 39 Nh, C.Z - 117) (spec/native/physics-ge-queries.md 4)")]
    public static FVector LedgeHangSpot(GERecord ledge, FVector pawnLocation, FVector ledgeGrabArmsZone)
    {
        ArgumentNullException.ThrowIfNull(ledge);
        FVector hands = pawnLocation + new FVector(0f, 0f, ledgeGrabArmsZone.Z);
        FVector closest = GEIndex.ClosestPointOnSegment(ledge.P0, ledge.P1, hands, out _);
        FVector n = LedgeOutNormal(ledge, pawnLocation);
        return new FVector(closest.X + ledgeGrabArmsZone.X * n.X, closest.Y + ledgeGrabArmsZone.X * n.Y, closest.Z - ledgeGrabArmsZone.Z);
    }
}
