using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;

namespace OpenScct.Game.SBase;

/// <summary>Merc pistols use the existing trace, prediction, hit zones and gadget presentation, with independent triggers.</summary>
public sealed class MercSidearm : SWeaponDefense
{
    public int Kind;
    public SRateOfFireMode SidearmFireMode = SRateOfFireMode.ROF_Auto;
    public float ReloadUntil;
    public float NextShotAt;
    private int _configured = -1;
    private bool _triggerDown;
    public SidearmDefinition Definition => Sidearms.Get((SidearmKind)Math.Clamp(Kind, 0, Sidearms.All.Count - 1));
    private bool AutomaticFire => Definition.Automatic && SidearmFireMode == SRateOfFireMode.ROF_Auto;
    private static readonly StateTable States = new(SWeaponDefense.ClassStates, new Idle());
    public override StateTable GetStateTable() => States;

    public void Configure(bool fill = false)
    {
        ROFMode = AutomaticFire ? SRateOfFireMode.ROF_Auto : SRateOfFireMode.ROF_Single;
        if (_configured == Kind && !fill) return;
        _configured = Kind;
        var d = Definition;
        ItemName = ItemLongName = d.Name;
        ItemDescription = d.Description;
        Mesh = new(d.Asset + "_first");
        DummyClass = default;
        BaseDamage = d.Damage;
        RateOfFire = d.Interval;
        ClipMaxAmmo = d.Magazine;
        MaxAmmo = d.Magazine + d.Spare;
        if (fill) { ClipAmmo = ClipMaxAmmo; Ammo = MaxAmmo; }
        bCanSnipe = false;
        CMax = CoefCaC = 1f; CoefMed = 0.75f; CoefMin = 0.5f;
        DistCac = d.Automatic ? 500f : 1000f;
        DistLoin = d.Automatic ? 2000f : 3000f;
        DistMed = (DistCac + DistLoin) * 0.5f;
        bool heavy = d.Kind is SidearmKind.Magnum44 or SidearmKind.DesertEagle;
        ValeurReculVer = d.Kind == SidearmKind.Magnum44 ? 12f : d.Automatic ? 6f : heavy ? 4f : 2f;
        ValeurReculHor = d.Automatic ? 4.5f : heavy ? 8f : 4f;
        MinDispersion = d.Automatic ? 0.1f : 0.15f;
        MaxDispersion = d.Automatic ? 0.35f : 0.7f;
        FireSingleShotSound = new("OpenScctSidearms." + d.Id + "_fire");
        ReloadSound = new("OpenScctSidearms." + d.Id + "_reload");
        Skins.Clear();
    }

    public override void GiveTo(Pawn other)
    {
        Configure(true);
        SWeapon? active = (other as SBasePawn)?.MyWeapon;
        base.GiveTo(other);
        if (other is not SBasePawn pawn) return;
        pawn.MyWeapon = active;
        pawn.SidearmWeapon = this;
        bDrawWeapon = false;
        PublishCarriedAmmo();
        pawn.GearSidearmStartAmmo = Ammo;
        pawn.GearSidearmStartClip = ClipAmmo;
        if (Role == ENetRole.ROLE_Authority && other is SPAWNDEFENSE merc)
        {
            Dummy = XLevel.Spawn<MercSidearmDummy>(pawn.Location, pawn.Rotation, pawn, pawn);
            if (Dummy is MercSidearmDummy dummy) { dummy.Kind = Kind; dummy.Instigator = pawn; dummy.SetBase(pawn); }
            merc.WeaponDummy = (active as SWeaponDefense)?.Dummy;
        }
    }

    protected override void Destroyed()
    {
        Dummy?.Destroy();
        if (SBPawn is { } pawn && ReferenceEquals(pawn.SidearmWeapon, this))
        {
            pawn.SidearmWeapon = null;
            if (ReferenceEquals(pawn.MyWeapon, this)) { pawn.MyWeapon = pawn.PrimaryWeapon; pawn.ActiveWeaponSlot = 0; }
        }
        base.Destroyed();
    }

    public void CancelReload()
    {
        if (ReloadUntil > 0)
        {
            Instigator?.StopSound(ReloadSound);
            OwnerPC?.RallumeLaserEteint();
        }
        ReloadUntil = 0; ViewAnim.Stop(ACTIONCHANNEL);
    }
    public override void SwitchROF()
    {
        var mode = AutomaticFire ? SRateOfFireMode.ROF_Single : SRateOfFireMode.ROF_Auto;
        if (!SetSidearmFireMode(mode)) return;
        ServerSound(SwitchROFSound);
        ServerSetROF(mode);
    }

    public bool SetSidearmFireMode(SRateOfFireMode mode)
    {
        if (!Definition.Automatic || mode is not (SRateOfFireMode.ROF_Single or SRateOfFireMode.ROF_Auto) ||
            !SidearmEquipment.Ready(this) || ReloadUntil > 0) return false;
        SidearmFireMode = ROFMode = mode;
        return true;
    }
    public override void Fire(float value)
    {
        if (_triggerDown && !AutomaticFire) return;
        _triggerDown = true;
        Shoot();
    }
    private void Shoot()
    {
        if (!SidearmEquipment.Ready(this) || ReloadUntil > 0 || Level.TimeSeconds + 0.0001f < NextShotAt ||
            OwnerPC?.CheckIfCanFire() == false) return;
        if (ClipAmmo <= 0) { Reload(); return; }
        NextShotAt = Level.TimeSeconds + RateOfFire;
        BulletInARow++;
        BulletsToFire = 1;
        base.FireWeapon();
    }
    public override void FireWeapon() => Shoot();

    public override bool Reload()
    {
        if (ReloadUntil > 0 || ClipAmmo >= ClipMaxAmmo || Ammo <= ClipAmmo || !SidearmEquipment.Ready(this) || OwnerPC?.IsHudPerturbed == true)
            return false;
        ReloadUntil = Level.TimeSeconds + Definition.ReloadSeconds;
        OwnerPC?.EteintLaser();
        ViewAnim.SetAlpha(ACTIONCHANNEL, 1f);
        var clip = ViewModelClips.DataOf(AFirstReloSt);
        ViewAnim.Play(ACTIONCHANNEL, AFirstReloSt, clip, clip.Seconds(1f) / Definition.ReloadSeconds);
        // Start the owner's sound on the same tick as the reload clip. The server's
        // accepted reload relays it to other players, without a separate sound RPC
        // that can arrive independently of the reload or survive its cancellation.
        Instigator?.PlayOwnedSound(ReloadSound);
        if (Role < ENetRole.ROLE_Authority && OwnerPC is { } pc)
            CombatServices.Net(this).ServerCall(pc, new CombatCall(new Name("ReloadSidearm")) { Target = this });
        return true;
    }
    public override bool FullReload()
    {
        CancelReload(); Ammo = MaxAmmo; ClipAmmo = ClipMaxAmmo; PublishCarriedAmmo(); return true;
    }

    private void Step(float delta)
    {
        NativeTickSWeaponDefense(delta);
        Configure();
        if (!ViewAnim.IsAnimating(MVTCHANNEL) || ViewAnim.Sequence(MVTCHANNEL) == ViewModelClips.FirstWaitStFd2 ||
            ViewAnim.Sequence(MVTCHANNEL) == ViewModelClips.FirstWalkStFd2)
        {
            AnimateWaiting();
        }
        if (!ViewAnim.IsAnimating(ACTIONCHANNEL)) ViewAnim.SetAlpha(ACTIONCHANNEL, 0);
        SidearmEquipment.Tick(this);
        if (!FireHeld) _triggerDown = false;
        if (ReloadUntil > 0 && Level.TimeSeconds >= ReloadUntil)
        {
            ClipAmmo = Math.Min(ClipMaxAmmo, Ammo);
            CancelReload();
            PublishCarriedAmmo();
        }
        if (!ReferenceEquals((Instigator as SBasePawn)?.MyWeapon, this)) return;
        if (OwnerPC is { IsRemotePlayer: true } && Role == ENetRole.ROLE_Authority) return;
        if (AutomaticFire && FireHeld) Shoot();
    }

    private sealed class Idle : ActorState, IFire, IReload, ISwitchROF
    {
        public Idle() : base("MercSidearm", "s_Selected", isAuto: true) { }
        public override void Tick(Actor self, float delta) => ((MercSidearm)self).Step(delta);
        public void Fire(SWeapon self, float value) => ((MercSidearm)self).Fire(value);
        public bool Reload(SWeapon self) => ((MercSidearm)self).Reload();
        public void SwitchROF(SWeaponDefense self) => self.SwitchROF();
    }
}

public sealed class MercSidearmDummy : SWeaponDefenseDummy
{
    public int Kind;
    protected override void Tick(float delta)
    {
        Mesh = new(Sidearms.Get((SidearmKind)Math.Clamp(Kind, 0, Sidearms.All.Count - 1)).Asset);
        bHidden = Instigator is SPAWNDEFENSE pawn && !ReferenceEquals(pawn.WeaponDummy, this);
        base.Tick(delta);
    }
}
