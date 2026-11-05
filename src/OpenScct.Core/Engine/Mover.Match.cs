namespace OpenScct.Core.Engine;

// OpenSCCT's match-size rules (spec/additions/match-size-rules.md).
public partial class Mover
{
    /// <summary>
    /// Held at a key by the map's match-size rules for this round: locked in s_TemporaryInactive, and UnLock (an
    /// alarm ending, a merc in an alarm zone) leaves it there. RE+'s rules used the alarm lock alone, which any alarm
    /// on the mover's lock list undid.
    /// </summary>
    public bool MatchLocked { get; set; }
}
