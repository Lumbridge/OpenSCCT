using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;

namespace OpenScct.Game.SBase;

public partial class SWeaponDefense
{
    private float _nextCarriedShot;
    private float _carriedReloadUntil;
    private int _seenAmmoRevision;
    public int CarriedClip;
    public int CarriedAmmo;
    public int CarriedShot;
    public int CarriedAmmoRevision;

    public void PublishCarriedAmmo()
    {
        if (Role != ENetRole.ROLE_Authority) return;
        CarriedClip = ClipAmmo; CarriedAmmo = Ammo; CarriedShot = ShotCounter; CarriedAmmoRevision++;
    }

    public void TickCarriedAmmo()
    {
        if (Instigator is not SBasePawn { SidearmWeapon: not null }) return;
        if (Role == ENetRole.ROLE_Authority)
        {
            if (CarriedAmmoRevision == 0) PublishCarriedAmmo();
            if (_carriedReloadUntil > 0 && Level.TimeSeconds >= _carriedReloadUntil)
            {
                ClipAmmo = System.Math.Min(ClipMaxAmmo, Ammo);
                _carriedReloadUntil = 0;
                PublishCarriedAmmo();
            }
        }
        else if (CarriedAmmoRevision > 0 && CarriedAmmoRevision != _seenAmmoRevision)
        {
            _seenAmmoRevision = CarriedAmmoRevision;
            int pending = Modes.PracticeRules.IsOn(this) ? 0 : System.Math.Max(0, ShotCounter - CarriedShot);
            ClipAmmo = System.Math.Max(0, CarriedClip - pending);
            Ammo = System.Math.Max(0, CarriedAmmo - pending);
        }
    }

    public void CancelCarriedReload() => _carriedReloadUntil = 0;

    public void RequestCarriedReload()
    {
        if (Instigator is not SBasePawn { SidearmWeapon: not null } || this is MercSidearm) return;
        if (Role < ENetRole.ROLE_Authority && OwnerPC is { } pc)
            CombatServices.Net(this).ServerCall(pc, new CombatCall(new Name("ReloadCarriedPrimary")) { Target = this });
        else if (SidearmEquipment.Ready(this) && ClipAmmo < ClipMaxAmmo && Ammo > ClipAmmo)
        {
            _carriedReloadUntil = Level.TimeSeconds + ViewModelClips.DataOf(AFirstReloSt).Seconds(1f);
        }
    }
    private bool AcceptCarriedShot()
    {
        if (Instigator is not SBasePawn { SidearmWeapon: not null } pawn) return true;
        bool accept = SidearmEquipment.Ready(this) && ClipAmmo > 0 && Ammo > 0 &&
            Level.TimeSeconds + Sim.DeltaTime * 2 >= _nextCarriedShot && _carriedReloadUntil <= 0 &&
            (this is not MercSidearm pistol || pistol.ReloadUntil <= 0);
        if (accept)
        {
            _nextCarriedShot = System.Math.Max(Level.TimeSeconds, _nextCarriedShot) + RateOfFire;
            if (OwnerPC is { IsRemotePlayer: true } && !Modes.PracticeRules.IsOn(this)) { ClipAmmo--; Ammo--; }
        }
        if (OwnerPC is { IsRemotePlayer: true } pc)
        {
            PublishCarriedAmmo();
            CombatServices.Net(this).ClientCall(pc, new CombatCall(new Name("WeaponAmmoState"))
                { Target = this, Int1 = ClipAmmo, Int2 = Ammo, Int3 = ShotCounter });
        }
        return accept;
    }
}
