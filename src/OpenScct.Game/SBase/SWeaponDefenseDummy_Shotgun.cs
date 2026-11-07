using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SWeaponDefenseDummy_Shotgun: the third-person shotgun (spec/equipment/merc-guns.md). The shot count drives
/// muzzle effects. Pellet impacts come from the weapon's actual traces through the impact manager.
/// </summary>
public partial class SWeaponDefenseDummy_Shotgun
{
    /// <summary>
    /// Shot count drives muzzle effects even when repeated shots hit the same surface. Pellet impacts are sent
    /// from the authority's actual traces, with immediate owner prediction, so the dummy creates no extra marks.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "OpenSCCT pellet impacts share authoritative trace geometry")]
    private protected override void TickMuzzle() => base.TickMuzzle();

    /// <summary>Legacy entry point; impact geometry is supplied by the weapon's pellet traces.</summary>
    public void SpawnImpacts(bool bTrace)
    {
    }
}
