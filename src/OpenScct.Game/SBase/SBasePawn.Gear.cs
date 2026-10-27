namespace OpenScct.Game.SBase;

// Server-only state of the mid-round gear change (an OpenSCCT addition, spec/additions/gear-change.md); not replicated.
public partial class SBasePawn
{
    /// <summary>Until this level time the server accepts the owner's spawn requests for the kit it asked for.</summary>
    public float GearGrantUntil;

    /// <summary>Gadgets still accepted under the open grant.</summary>
    public int GearGrantGadgets;

    /// <summary>Whether the open grant still accepts a weapon.</summary>
    public bool GearGrantWeapon;

    /// <summary>Whether the open grant still accepts a bomb (spies in bomb modes).</summary>
    public bool GearGrantBomb;

    /// <summary>The weapon's ammo when the server gave it (the "nothing used" test).</summary>
    public int GearStartAmmo;

    /// <summary>The merc rifle's clip when the server gave it (the "nothing used" test).</summary>
    public int GearStartClipAmmo;
}
