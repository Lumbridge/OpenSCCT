using System;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;

namespace OpenScct.Server;

/// <summary>
/// The server's check of a traversal point a client proposes (ZipP1, ZipP2, NewZipP1, NewZipP2 of a ledge, pipe,
/// ladder, fence, hand over hand or zip line). The points travel as vectors and arrive rounded to whole units
/// (spec/net/model.md rule 11), so the server's copy of the line would sit up to 0.87 uu off the client's and its
/// pawn drift from the client's along it. A proposed point within rounding of a point of one of the map's GEs is
/// taken as that exact point; any other point is used as it arrived, as the original does.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "addition of the rewrite (spec/net/move-protocol.md rule 14): the original applies the rounded points without a check")]
public static class GEPointCheck
{
    /// <summary>How far a rounded point can be from the point it was rounded from (half a unit on each axis).</summary>
    public const float RoundingReach = 0.8661f;

    /// <summary>Half a unit on one axis, with room for the float error of the GE's coordinates.</summary>
    public const float AxisReach = 0.501f;

    /// <summary>The GE point <paramref name="proposed"/> was rounded from, or <paramref name="proposed"/> itself.</summary>
    public static FVector Exact(Actor? pawn, FVector proposed)
    {
        if (pawn?.XLevel?.PhysicsDriver is not PhysicsDriver driver)
        {
            return proposed;
        }

        var ges = driver.GEs;
        float best = RoundingReach * RoundingReach;
        FVector found = proposed;
        for (int i = 0; i < ges.Count; i++)
        {
            GERecord ge = ges[i];
            for (int k = 0; k < ge.Points.Length; k++)
            {
                FVector p = ge.Points[k];
                float d = (p - proposed).SizeSquared();
                if (d <= best && Math.Abs(p.X - proposed.X) <= AxisReach && Math.Abs(p.Y - proposed.Y) <= AxisReach &&
                    Math.Abs(p.Z - proposed.Z) <= AxisReach)
                {
                    best = d;
                    found = p;
                }
            }
        }

        return found;
    }
}
