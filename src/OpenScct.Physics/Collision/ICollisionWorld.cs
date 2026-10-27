using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Physics.Collision;

/// <summary>
/// Collision queries with UE2 semantics. A zero <c>Extent</c> traces a line; a non-zero one sweeps an axis-aligned
/// box with those half sizes (pawns use (CollisionRadius, CollisionRadius, CollisionHeight)). Queries ignore the
/// <c>SourceActor</c> itself, and with <see cref="ETraceFlags.TRACE_Blocking"/> only test what blocks it.
/// Results are deterministic: the nearest hit wins and ties go to the lowest collider, then primitive index.
/// </summary>
public interface ICollisionWorld
{
    /// <summary>
    /// First hit of a line or box trace from <paramref name="Start"/> to <paramref name="End"/> (UE2
    /// SingleLineCheck, with the result inverted: true means something was hit).
    /// </summary>
    bool LineCheck(out FCheckResult Hit, Actor? SourceActor, FVector End, FVector Start, FVector Extent, ETraceFlags TraceFlags);

    /// <summary>
    /// Every collider and actor the trace touches, one result each (its first contact), sorted by time; nothing
    /// beyond the first world BSP hit. Fills <paramref name="Hits"/> (cleared first) and returns the count.
    /// </summary>
    int MultiLineCheck(List<FCheckResult> Hits, Actor? SourceActor, FVector End, FVector Start, FVector Extent, ETraceFlags TraceFlags);

    /// <summary>True when a box of <paramref name="Extent"/> at <paramref name="Location"/> overlaps something.</summary>
    bool PointCheck(out FCheckResult Hit, Actor? SourceActor, FVector Location, FVector Extent, ETraceFlags TraceFlags);

    /// <summary>True when a zero-extent line from <paramref name="Start"/> to <paramref name="End"/> hits no world geometry (UnrealScript FastTrace).</summary>
    bool FastTrace(FVector End, FVector Start);

    /// <summary>Zone index of a point (ZONE record; 0 is the default zone).</summary>
    int GetZoneIndex(FVector Location);

    /// <summary>The ZoneInfo actor of a point's zone, or null when unbound.</summary>
    ZoneInfo? GetZoneInfo(FVector Location);
}
