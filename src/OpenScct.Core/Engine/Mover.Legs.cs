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
    /// The seconds of the leg just set up from PrevKeyNum to KeyNum: the key's own time when the map gives one,
    /// else <paramref name="seconds"/>. A zero-time leg (a jump to a key) stays a jump. Also picks the leg's easing.
    /// </summary>
    private float LegSeconds(float seconds)
    {
        if (Legs is not { } legs)
        {
            return seconds;
        }

        PickLegEasing(legs);
        return seconds > 0f && legs.LegTime(PrevKeyNum, KeyNum) is { } own ? own : seconds;
    }

    private void PickLegEasing(MoverLegs legs)
    {
        bool glides = MoverGlideType == EMoverGlideType.MV_GlideByTime;
        _legSmoothStart = legs.SmoothAt(PrevKeyNum, glides);
        _legSmoothEnd = legs.SmoothAt(KeyNum, glides);
    }

    /// <summary>How far along the leg the mover is at <paramref name="alpha"/> of its time.</summary>
    private double LegEase(double alpha) => Legs is null
        ? MoverGlideType == EMoverGlideType.MV_GlideByTime ? 3.0 * alpha * alpha - 2.0 * alpha * alpha * alpha : alpha
        : MoverLegs.Ease(alpha, _legSmoothStart, _legSmoothEnd);
}
