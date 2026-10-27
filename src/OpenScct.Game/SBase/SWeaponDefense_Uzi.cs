using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SWeaponDefense_Uzi: the merc Uzi (spec/equipment/merc-guns.md). Everything is the rifle's with the Uzi's
/// defaults (60-round clip, 0.1 s rate of fire, 10 damage, its own spread, recoil and bands, no scope); only its
/// muzzle effects differ, which the view draws by the weapon's class.
/// </summary>
public partial class SWeaponDefense_Uzi
{
    /// <summary>The first-person muzzle flash and casing, without the rifle's barrel flash (a view effect).</summary>
    [Uc("SBase.SWeaponDefense_Uzi.SpawnSFX", Sha1 = "ac1c02c777002b75605551a6e6711acb9af894db")]
    [PortOf("uc:SBase.SWeaponDefense_Uzi:SpawnSFX", Spec = "spec/equipment/merc-guns.md#rules")]
    public override void SpawnSFX() => base.SpawnSFX();

    /// <summary>The barrel smoke after a burst, 13 uu further back on the FX bone (a view effect).</summary>
    [Uc("SBase.SWeaponDefense_Uzi.CreateSmoke2", Sha1 = "71bac8a7f0a73d87b0f423903c6005371e9b83e9")]
    [PortOf("uc:SBase.SWeaponDefense_Uzi:CreateSmoke2", Spec = "spec/equipment/merc-guns.md#rules")]
    public override void CreateSmoke2() => base.CreateSmoke2();
}
