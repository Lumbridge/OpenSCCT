using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Svm;

public sealed class CompactSMG : MercRifle
{
    public static readonly UcClassInfo ClassInfo = RifleClass<CompactSMG>("Svm.CompactSMG");
    public override MercRifleDefinition Definition => MercRifles.CompactSmg;
    public override UcClassInfo GetUcClass() => ClassInfo;
}

public sealed class PumpShotgun : SWeaponDefense_Shotgun
{
    public static readonly UcClassInfo ClassInfo = new("Svm.PumpShotgun", typeof(PumpShotgun), UcMeta.Class,
        (UcClassFlags)0x36u, null, 0, static () => new PumpShotgun(), [], [], [], null);
    public override UcClassInfo GetUcClass() => ClassInfo;

    protected override void SetDefaults()
    {
        base.SetDefaults();
        var profile = MercRifles.PumpShotgun;
        ItemName = ItemLongName = profile.Name;
        ItemDescription = profile.Description;
        Mesh = new(profile.Asset + "_first");
        DummyClass = new(profile.ClassPath + "Dummy");
        RateOfFire = profile.Interval;
        ROFMode = PreviousROFMode = SRateOfFireMode.ROF_Single;
        BaseDamage = profile.Damage;
        CMax = profile.CloseFactor;
        CoefMed = profile.MediumFactor;
        CoefMin = profile.FarFactor;
        MinDispersion = profile.MinSpread;
        MaxDispersion = profile.MaxSpread;
        ValeurReculVer = profile.VerticalRecoil;
        ValeurReculHor = profile.HorizontalRecoil;
        ClipAmmo = ClipMaxAmmo = profile.Magazine;
        Ammo = MaxAmmo = profile.Magazine + profile.Reserve;
        Skins.Clear();
    }

    public override bool RequiresTriggerRelease => true;
    public override string GetROFName() => "Pump Action";
    protected override bool SupportsFireMode(SRateOfFireMode mode) => mode == SRateOfFireMode.ROF_Single;
}

public abstract class PrecisionRifle : MercRifle
{
    public override bool RequiresTriggerRelease => true;

    protected override void SetDefaults()
    {
        base.SetDefaults();
        ROFMode = PreviousROFMode = SRateOfFireMode.ROF_Single;
    }

    protected override bool SupportsFireMode(SRateOfFireMode mode) => mode == SRateOfFireMode.ROF_Single;
}

public sealed class BoltRifle : PrecisionRifle
{
    public static readonly UcClassInfo ClassInfo = RifleClass<BoltRifle>("Svm.BoltRifle");
    public override MercRifleDefinition Definition => MercRifles.BoltRifle;
    public override string GetROFName() => "Bolt Action";
    public override UcClassInfo GetUcClass() => ClassInfo;
}

public sealed class DesignatedMarksman : PrecisionRifle
{
    public static readonly UcClassInfo ClassInfo = RifleClass<DesignatedMarksman>("Svm.DesignatedMarksman");
    public override MercRifleDefinition Definition => MercRifles.DesignatedMarksman;
    public override string GetROFName() => "Semi-Auto";
    public override UcClassInfo GetUcClass() => ClassInfo;
}

public sealed class CompactSMGDummy : SWeaponDefenseDummy
{
    public static readonly UcClassInfo ClassInfo = MercRifle.DummyClassInfo<CompactSMGDummy>("Svm.CompactSMGDummy");
    public override UcClassInfo GetUcClass() => ClassInfo;
    protected override void SetDefaults() { base.SetDefaults(); Mesh = new(MercRifles.CompactSmg.Asset); }
}

public sealed class PumpShotgunDummy : SWeaponDefenseDummy_Shotgun
{
    public static readonly UcClassInfo ClassInfo = new("Svm.PumpShotgunDummy", typeof(PumpShotgunDummy), UcMeta.Class,
        (UcClassFlags)0x36u, null, 0, static () => new PumpShotgunDummy(), [], [], [], null);
    public override UcClassInfo GetUcClass() => ClassInfo;
    protected override void SetDefaults() { base.SetDefaults(); Mesh = new(MercRifles.PumpShotgun.Asset); }
}

public sealed class BoltRifleDummy : SWeaponDefenseDummy
{
    public static readonly UcClassInfo ClassInfo = MercRifle.DummyClassInfo<BoltRifleDummy>("Svm.BoltRifleDummy");
    public override UcClassInfo GetUcClass() => ClassInfo;
    protected override void SetDefaults() { base.SetDefaults(); Mesh = new(MercRifles.BoltRifle.Asset); }
}

public sealed class DesignatedMarksmanDummy : SWeaponDefenseDummy
{
    public static readonly UcClassInfo ClassInfo = MercRifle.DummyClassInfo<DesignatedMarksmanDummy>("Svm.DesignatedMarksmanDummy");
    public override UcClassInfo GetUcClass() => ClassInfo;
    protected override void SetDefaults() { base.SetDefaults(); Mesh = new(MercRifles.DesignatedMarksman.Asset); }
}
