using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The shotgun's third-person reload (spec/equipment/merc-guns.md rule 5): the body clip ReloStSpFd2S carries one notify
// per shell (EventReloadAmmoNb1 to 5); a reload of n shells skips the clip to its end after shell n, as the first-person
// clip does (SWeaponDefense_Shotgun.EventReloadFirstN), so the body does not keep loading shells it has not got.
public partial class SBasePawn
{
    /// <summary>The body reload clip's end frame the shotgun skips to (frame 41.1 of ReloStSpFd2S's 46).</summary>
    [Provenance(ProvenanceKind.Decoded, "ASBasePawn::execFinishReloadAnimation game 0x10ae72b0: SetAnimFrame(21, 41.1, 1)")]
    public const float ShotgunReloadEndFrame = 41.1f;

    /// <summary>
    /// With a shotgun (the weapon, or for another player's merc the third-person gun) the weapon channel's reload
    /// clip jumps to its end frame.
    /// </summary>
    [UcNative("ASBasePawn::execFinishReloadAnimation", Exe = 0x10ae72b0)]
    [PortOf("nat:SBase.SBasePawn.FinishReloadAnimation", Spec = "spec/equipment/merc-guns.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ae72b0: MyWeapon IsA SWeaponDefense_Shotgun, else the dummy at +0xb64 IsA SWeaponDefenseDummy_Shotgun; MeshInstance SetAnimFrame(21, 41.1, 1)")]
    public void FinishReloadAnimation()
    {
        if (MyWeapon is SWeaponDefense_Shotgun || (this as SPAWNDEFENSE)?.WeaponDummy is SWeaponDefenseDummy_Shotgun)
        {
            PawnAnim.SetAnimFrame(WEAPONCHANNEL, ShotgunReloadEndFrame, unitFlag: true);
        }
    }

    private void EventReloadAmmoNb(int n)
    {
        if (iNbAmmoToReload == n)
        {
            FinishReloadAnimation();
        }
    }

    [Uc("SBase.SBasePawn.EventReloadAmmoNb1", Sha1 = "4780ed988329ecac8e4ac6b5a3d73970d8241a4b")]
    [PortOf("uc:SBase.SBasePawn:EventReloadAmmoNb1", Spec = "spec/equipment/merc-guns.md#rules")]
    public void EventReloadAmmoNb1() => EventReloadAmmoNb(1);

    [Uc("SBase.SBasePawn.EventReloadAmmoNb2", Sha1 = "7980f44044e5d47d47f2551447360e2ae10ad95d")]
    [PortOf("uc:SBase.SBasePawn:EventReloadAmmoNb2", Spec = "spec/equipment/merc-guns.md#rules")]
    public void EventReloadAmmoNb2() => EventReloadAmmoNb(2);

    [Uc("SBase.SBasePawn.EventReloadAmmoNb3", Sha1 = "722637340925fce7a762c46c71d6e128ec956e51")]
    [PortOf("uc:SBase.SBasePawn:EventReloadAmmoNb3", Spec = "spec/equipment/merc-guns.md#rules")]
    public void EventReloadAmmoNb3() => EventReloadAmmoNb(3);

    [Uc("SBase.SBasePawn.EventReloadAmmoNb4", Sha1 = "1581e150afbd52928ad98ad3c25c1a148233adac")]
    [PortOf("uc:SBase.SBasePawn:EventReloadAmmoNb4", Spec = "spec/equipment/merc-guns.md#rules")]
    public void EventReloadAmmoNb4() => EventReloadAmmoNb(4);

    [Uc("SBase.SBasePawn.EventReloadAmmoNb5", Sha1 = "217e1e152cda892c500a426af05d6d2d6743f7b1")]
    [PortOf("uc:SBase.SBasePawn:EventReloadAmmoNb5", Spec = "spec/equipment/merc-guns.md#rules")]
    public void EventReloadAmmoNb5() => EventReloadAmmoNb(5);
}
