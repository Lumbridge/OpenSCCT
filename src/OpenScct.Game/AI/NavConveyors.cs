using OpenScct.Core;
using OpenScct.Physics.Conveyors;
using OpenScct.Provenance;

namespace OpenScct.Game.AI;

/// <summary>
/// What the level's running conveyors do to a walking bot (spec/additions/conveyors.md#bots): the planner weighs a
/// walk with the belt under it as quicker and one against it as slower (a belt faster than the bot runs is all but
/// shut that way), and the follower aims so its walk and the belt together head for the next waypoint. Conveyors
/// switch and reverse during a round, so this reads their state at the time of the plan or the step; the graph itself
/// knows nothing of them.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "OpenSCCT addition: spec/additions/conveyors.md#bots")]
public static class NavConveyors
{
    /// <summary>The cost factor of a way the bot cannot make headway along (a belt runs against it too fast).</summary>
    public const float Against = 100f;

    /// <summary>
    /// How much longer (above 1) or shorter (below 1) walking from floor point <paramref name="from"/> to
    /// <paramref name="to"/> takes at <paramref name="speed"/> with the belt under its middle; 1 off the conveyors.
    /// </summary>
    public static float Factor(ConveyorField field, FVector from, FVector to, float speed)
    {
        FVector mid = (from + to) * 0.5f;
        var way = new FVector(to.X - from.X, to.Y - from.Y, 0f);
        if (way.SizeSquared2D() < 1f || field.FindCarrier(mid, AiK.PawnRadius, ConveyorCargo.Pawns, out FVector belt) is null)
        {
            return 1f;
        }

        float made = Headway(way.SafeNormal(), belt, speed);
        return made <= 0f ? Against : SvmMath.Min(Against, speed / made);
    }

    /// <summary>
    /// The way to walk at <paramref name="speed"/> so that walking plus <paramref name="belt"/> heads along
    /// <paramref name="direction"/> (flat, unit); when the belt is too fast across it, straight against the belt's
    /// sideways part. Flat and unit.
    /// </summary>
    public static FVector Heading(FVector direction, FVector belt, float speed)
    {
        float made = Headway(direction, belt, speed);
        FVector flatBelt = new(belt.X, belt.Y, 0f);
        FVector walk = made > 0f ? direction * made - flatBelt : -(flatBelt - direction * (flatBelt | direction));
        return walk.SizeSquared2D() > 1e-6f ? walk.SafeNormal() : direction;
    }

    /// <summary>
    /// The speed along <paramref name="direction"/> of walking at <paramref name="speed"/> on <paramref name="belt"/>,
    /// steering so the belt's sideways push is cancelled; 0 or less when no heading makes way.
    /// </summary>
    private static float Headway(FVector direction, FVector belt, float speed)
    {
        float along = belt.X * direction.X + belt.Y * direction.Y;
        float acrossSq = belt.X * belt.X + belt.Y * belt.Y - along * along;
        float room = speed * speed - acrossSq;
        return room <= 0f ? 0f : along + SvmMath.Sqrt(room);
    }
}
