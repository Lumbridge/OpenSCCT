namespace OpenScct.Core.Engine;

/// <summary>Additional round counters introduced in Enhanced 4.0 Beta 5.</summary>
public partial class USPlayerStats
{
    public int Merc_AmmoStocksUsed;
    public int Spy_SpyBulletTargets;

    /// <summary>The last loadout actually granted by the authority, not the client's requested profile.</summary>
    public string Weapon = string.Empty;
    public string[] Gadgets = [];
}
