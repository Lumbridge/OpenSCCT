namespace OpenScct.Core.Engine;

// OpenSCCT's match-size rules (spec/additions/match-size-rules.md).
public abstract partial class Actor
{
    /// <summary>
    /// Left out of this round by the map's match-size rules: an objective never comes back into play (a Reset
    /// deactivates it instead), any other actor is hidden without collision; none of them ticks or hears events. Set
    /// at the round start.
    /// </summary>
    public bool MatchExcluded { get; set; }
}
