using OpenScct.Core.Uc;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Svm;

/// <summary>The existing SCCT trace, recoil, reload, melee and ownership states with an authored rifle profile.</summary>
public abstract class MercRifle : SWeaponDefense
{
    public const float AimSpeedScale = .25f;
    public abstract MercRifleDefinition Definition { get; }
    public bool AimRequested;
    public override float ShotInterval => Definition.BurstDefault && ROFMode == SRateOfFireMode.ROF_Burst ? .075f : RateOfFire;

    public void SetAiming(SPlayerController pc, bool requested)
    {
        bool aiming = requested && CanAim(pc);
        if (Role == ENetRole.ROLE_Authority)
            GadgetNet.ToOwner(pc, new CombatCall(new Name("RifleAimState")) { Target = this, Bool1 = aiming });
        if (AimRequested == aiming) return;
        AimRequested = aiming;
        GadgetNet.ToServer(this, pc, new CombatCall(new Name("SetRifleAim")) { Target = this, Bool1 = aiming });
    }

    private bool CanAim(SPlayerController pc) =>
        Instigator is SBasePawn { Health: > 0, bWeaponTransit: false, bStickyShocking: false } pawn &&
        ReferenceEquals(pc.SBPawn, pawn) && ReferenceEquals(pawn.MyWeapon, this) &&
        !pc.bIsInInventoryMode && pc.iDontUpdatePadInput == 0 && pc.FreeCameraFlying == 0 &&
        (IsInState("s_Selected") || IsInState("s_Firing"));

    protected override void Tick(float deltaTime)
    {
        base.Tick(deltaTime);
        if (AimRequested && (OwnerPC is not { } pc || !CanAim(pc))) AimRequested = false;
    }

    protected override void SetDefaults()
    {
        base.SetDefaults();
        var d = Definition;
        ItemName = ItemLongName = d.Name;
        ItemDescription = d.Description;
        Mesh = new(d.Asset + "_first");
        DummyClass = new(d.ClassPath + "Dummy");
        BaseDamage = d.Damage;
        ClipAmmo = ClipMaxAmmo = d.Magazine;
        Ammo = MaxAmmo = d.Magazine + d.Reserve;
        RateOfFire = d.Interval;
        bCanSnipe = false;
        CMax = CoefCaC = d.CloseFactor;
        CoefMed = d.MediumFactor;
        CoefMin = d.FarFactor;
        MinDispersion = d.MinSpread;
        MaxDispersion = d.MaxSpread;
        ValeurReculVer = d.VerticalRecoil;
        ValeurReculHor = d.HorizontalRecoil;
        ROFMode = d.BurstDefault ? SRateOfFireMode.ROF_Burst : SRateOfFireMode.ROF_Auto;
        FireSingleShotSound = new("OpenScctRifles." + d.Id.ToLowerInvariant() + "_fire");
        ReloadSound = new("OpenScctRifles." + d.Id.ToLowerInvariant() + "_reload");
        Skins.Clear();
    }

    protected override float AuthoritativeReloadSeconds() => Definition.ReloadSeconds;

    protected override AssetRef ReloadAudio() => this is M4A1S && ClipAmmo > 0
        ? new AssetRef("OpenScctRifles.m4a1s_reload_tactical") : base.ReloadAudio();

    internal static UcClassInfo RifleClass<T>(string path) where T : MercRifle, new() =>
        new(path, typeof(T), SWeaponDefense.UcMeta.Class, (UcClassFlags)0x36u, null, 0, static () => new T(), [], [], [], null);

    internal static UcClassInfo DummyClassInfo<T>(string path) where T : SWeaponDefenseDummy, new() =>
        new(path, typeof(T), SWeaponDefenseDummy.UcMeta.Class, (UcClassFlags)0x36u, null, 0, static () => new T(), [], [], [], null);
}

public sealed class M4A1S : MercRifle
{
    public static readonly UcClassInfo ClassInfo = RifleClass<M4A1S>("Svm.M4A1S");
    public override MercRifleDefinition Definition => MercRifles.All[0];
    public override UcClassInfo GetUcClass() => ClassInfo;
}
public sealed class AK47 : MercRifle
{
    public static readonly UcClassInfo ClassInfo = RifleClass<AK47>("Svm.AK47");
    public override MercRifleDefinition Definition => MercRifles.All[1];
    public override UcClassInfo GetUcClass() => ClassInfo;
}
public sealed class Galil : MercRifle
{
    public static readonly UcClassInfo ClassInfo = RifleClass<Galil>("Svm.Galil");
    public override MercRifleDefinition Definition => MercRifles.All[2];
    public override UcClassInfo GetUcClass() => ClassInfo;
}
public sealed class Famas : MercRifle
{
    public static readonly UcClassInfo ClassInfo = RifleClass<Famas>("Svm.Famas");
    public override MercRifleDefinition Definition => MercRifles.All[3];
    public override UcClassInfo GetUcClass() => ClassInfo;
}

public sealed class M4A1SDummy : SWeaponDefenseDummy
{
    public static readonly UcClassInfo ClassInfo = MercRifle.DummyClassInfo<M4A1SDummy>("Svm.M4A1SDummy");
    protected override void SetDefaults() { base.SetDefaults(); Mesh = new(MercRifles.All[0].Asset); }
    public override UcClassInfo GetUcClass() => ClassInfo;
}
public sealed class AK47Dummy : SWeaponDefenseDummy
{
    public static readonly UcClassInfo ClassInfo = MercRifle.DummyClassInfo<AK47Dummy>("Svm.AK47Dummy");
    protected override void SetDefaults() { base.SetDefaults(); Mesh = new(MercRifles.All[1].Asset); }
    public override UcClassInfo GetUcClass() => ClassInfo;
}
public sealed class GalilDummy : SWeaponDefenseDummy
{
    public static readonly UcClassInfo ClassInfo = MercRifle.DummyClassInfo<GalilDummy>("Svm.GalilDummy");
    protected override void SetDefaults() { base.SetDefaults(); Mesh = new(MercRifles.All[2].Asset); }
    public override UcClassInfo GetUcClass() => ClassInfo;
}
public sealed class FamasDummy : SWeaponDefenseDummy
{
    public static readonly UcClassInfo ClassInfo = MercRifle.DummyClassInfo<FamasDummy>("Svm.FamasDummy");
    protected override void SetDefaults() { base.SetDefaults(); Mesh = new(MercRifles.All[3].Asset); }
    public override UcClassInfo GetUcClass() => ClassInfo;
}
