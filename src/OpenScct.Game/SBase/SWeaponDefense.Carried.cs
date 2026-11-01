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
    private bool _hasClientShotClock;
    private float _clientShotOrigin, _serverShotOrigin, _lastClientShotTime, _nextClientShotTime;
    // A reliable batch may recover one second of previously fired shots. The client's rate still has to
    // match the weapon, and its total clock advance may never run arbitrarily ahead of real server time.
    private const float MaximumShotClockLead = 1f;
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
        if (Instigator is not SBasePawn pawn) return;
        if (Role == ENetRole.ROLE_Authority)
        {
            if (_carriedReloadUntil > 0 && !CanUseOwnedWeapon(pawn)) CancelCarriedReload();
            if (_carriedReloadUntil > 0 && Level.TimeSeconds >= _carriedReloadUntil)
            {
                ClipAmmo = System.Math.Min(ClipMaxAmmo, Ammo);
                _carriedReloadUntil = 0;
                PublishCarriedAmmo();
            }
            if (CarriedAmmoRevision == 0 || CarriedClip != ClipAmmo || CarriedAmmo != Ammo || CarriedShot != ShotCounter)
                PublishCarriedAmmo();
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

    private bool CanUseOwnedWeapon(SBasePawn pawn) => pawn.Health > 0 && !pawn.bDeleteMe &&
        !pawn.bSleeping && !pawn.bLyingOnGround && !pawn.bStickyShocking && pawn.m_AttackedBy is null &&
        ReferenceEquals(pawn.MyWeapon, this) && SidearmEquipment.Ready(this);

    protected virtual float AuthoritativeReloadSeconds() => ViewModelClips.DataOf(AFirstReloSt).Seconds(1f);

    public void RequestCarriedReload()
    {
        if (Instigator is not SBasePawn pawn || this is MercSidearm) return;
        if (Role < ENetRole.ROLE_Authority && OwnerPC is { } pc)
            CombatServices.Net(this).ServerCall(pc, new CombatCall(new Name("ReloadCarriedPrimary")) { Target = this });
        else if (CanUseOwnedWeapon(pawn) && _carriedReloadUntil <= 0 && ClipAmmo < ClipMaxAmmo && Ammo > ClipAmmo)
        {
            _carriedReloadUntil = Level.TimeSeconds + AuthoritativeReloadSeconds();
        }
    }
    private bool AcceptCarriedShot()
    {
        if (Instigator is not SBasePawn pawn) return false;
        bool accept = CanUseOwnedWeapon(pawn) && ClipAmmo > 0 && Ammo > 0 &&
            _carriedReloadUntil <= 0 && (this is not MercSidearm pistol || pistol.ReloadUntil <= 0) && AcceptShotClock();
        if (accept)
        {
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

    private bool AcceptShotClock()
    {
        float now = XLevel.WorldTimeSeconds;
        float rate = System.Math.Max(Sim.DeltaTime, RateOfFire);
        if (ActiveClientShotTime is not { } stamp)
        {
            if (now + Sim.DeltaTime * 2 < _nextCarriedShot) return false;
            _nextCarriedShot = System.Math.Max(now, _nextCarriedShot) + rate;
            return true;
        }
        if (!float.IsFinite(stamp) || stamp < 0) return false;
        if (_hasClientShotClock)
        {
            if (stamp <= _lastClientShotTime || stamp + Sim.DeltaTime * 2 < _nextClientShotTime ||
                stamp - (double)_clientShotOrigin > now - (double)_serverShotOrigin + MaximumShotClockLead)
                return false;
        }
        else
        {
            _hasClientShotClock = true;
            _clientShotOrigin = stamp;
            _serverShotOrigin = now;
        }
        _lastClientShotTime = stamp;
        _nextClientShotTime = System.Math.Max(stamp, _nextClientShotTime) + rate;
        return true;
    }
}
