using System.Collections.Generic;

namespace OpenScct.Core.Engine;

// OpenSCCT's own per-round feat counts for achievements (FeatKeys), beside the retail statistics.
public partial class USPlayerStats
{
    /// <summary>This round's feats by <see cref="FeatKeys"/> name.</summary>
    public Dictionary<string, int> Feats { get; } = new();

    /// <summary>Adds to one feat.</summary>
    public void AddFeat(string key, int amount = 1)
    {
        Feats[key] = Feats.GetValueOrDefault(key) + amount;
    }
}
