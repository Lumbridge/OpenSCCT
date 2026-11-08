namespace OpenScct.Core.Engine;

// OpenSCCT's per-key mover timing (spec/additions/mover-legs.md).
public partial class Mover
{
    /// <summary>
    /// The map's own time and easing per key (SVMap 1.2 <c>sim/movers.json</c>), or null for the game's timing: one
    /// MoveTime and one glide type for every leg. Set at load on the server and on every client from the same file.
    /// </summary>
    public MoverLegs? Legs { get; set; }

    // The ends of the leg in progress that ease (from Legs).
    private bool _legSmoothStart;
    private bool _legSmoothEnd;

    /// <summary>
    /// The seconds of the leg just set up from PreviousKey to CurrentKey: the key's own time when the map gives one (also on
    /// a mover whose MoveTime is 0), else <paramref name="seconds"/>. A move that is not between neighbouring keys,
    /// such as the match-size rules' hold at the key the mover is at, keeps <paramref name="seconds"/>. Also picks the
    /// leg's easing.
    /// </summary>
    private float LegSeconds(float seconds)
    {
        if (Legs is not { } legs)
        {
            return seconds;
        }

        PickLegEasing(legs);
        return legs.LegTime(PreviousKey, CurrentKey) ?? seconds;
    }

    /// <summary>
    /// Client: the rate of the leg the server started from PreviousKey to CurrentKey. A leg with its own time runs at that
    /// time from the map file, as on the server: the replicated rate is rounded to a whole number x 100 on the wire,
    /// which would make a long leg end at a different time on the client.
    /// </summary>
    private float ClientLegRate(float replicatedRate)
    {
        if (Legs is not { } legs)
        {
            return replicatedRate;
        }

        PickLegEasing(legs);
        return legs.LegTime(PreviousKey, CurrentKey) is { } own ? 1f / SvmMath.Max(own, MoverK.MinLegTime) : replicatedRate;
    }

    /// <summary>The longest single leg this mover can take: its MoveTime, or a key's own longer time.</summary>
    public float LongestLegTime => Legs is { } legs ? legs.Longest(MoveTime) : MoveTime;

    private void PickLegEasing(MoverLegs legs)
    {
        bool glides = MoverGlideType == EMoverGlideType.MV_GlideByTime;
        _legSmoothStart = legs.SmoothAt(PreviousKey, glides);
        _legSmoothEnd = legs.SmoothAt(CurrentKey, glides);
    }

    /// <summary>How far along the leg the mover is at <paramref name="alpha"/> of its time.</summary>
    private double LegEase(double alpha) => Legs is null
        ? MoverGlideType == EMoverGlideType.MV_GlideByTime ? 3.0 * alpha * alpha - 2.0 * alpha * alpha * alpha : alpha
        : MoverLegs.Ease(alpha, _legSmoothStart, _legSmoothEnd);
}
