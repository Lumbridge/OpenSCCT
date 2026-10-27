using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SStickyShocker: the spy's Shocker pistol (spec/equipment/spy-shocker.md rule 8). Every shot costs
/// EnergyNeeded (15) of the spy's energy bar; no ammunition is used.
/// </summary>
public partial class SStickyShocker
{
    private static readonly Name CNAME_GameEnded = new("GameEnded");

    /// <summary>
    /// The trigger: drawn and not in transit, a clear muzzle, then the energy; without energy an empty click at most
    /// once per ReloadTime.
    /// </summary>
    [Uc("SBase.SStickyShocker.Fire", Sha1 = "74ff2e3708df431f5ec622b511d7329163f88ea0")]
    [PortOf("uc:SBase.SStickyShocker:Fire", Spec = "spec/equipment/spy-shocker.md#rules")]
    [PortOf("uc:SBase.SStickyShocker:Fire", Spec = "spec/equipment/spy-energy.md#rules")]
    public override void Fire(float Value)
    {
        if (Controller is { } c && c.GetStateName() == CNAME_GameEnded)
        {
            return;
        }

        if (Instigator is not SPawnAttaque spy || spy.WeaponLoaded == 0 || spy.bWeaponTransit)
        {
            return;
        }

        if (!(OwnerPC?.CheckIfCanFire() ?? true))
        {
            return;
        }

        if (SpyEnergy.AuthorizeEnergy(spy, EnergyNeeded))
        {
            FireWeapon();
            return;
        }

        float now = Level.TimeSeconds;
        if (LastShot + ReloadTime < now)
        {
            LastShot = now;
            ServerSound(EmptySound, ESoundSlot.SLOT_None, (int)K.WeaponSoundRadius);
        }
    }

    /// <summary>The eye point (unused by the shot, which starts at the pistol).</summary>
    [Uc("SBase.SStickyShocker.GetFireStart", Sha1 = "f919ccce476fc2a7cc695b3021a92cb51adc8b3b")]
    [PortOf("uc:SBase.SStickyShocker:GetFireStart", Spec = "spec/equipment/spy-shocker.md#rules")]
    public override FVector GetFireStart() => base.GetFireStart();
}
