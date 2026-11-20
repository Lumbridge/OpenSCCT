using System;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Svm;

namespace OpenScct.Game.Equipment;

public static class WeaponAttachments
{
    public const int Laser = 1, RedDot = 2, Suppressor = 4, ExtendedMagazine = 8, Acog = 16;
    public const int Optics = RedDot | Acog, Budget = 3;
    public const int StandardCapacity = 20, ExtendedCapacity = 30;
    public const string M4 = "Svm.M4A1S";

    public static int Points(int attachment) => attachment switch
    {
        Laser or RedDot or ExtendedMagazine => 1, Suppressor or Acog => 2, _ => 0,
    };

    public static int KillsRequired(int attachment) => attachment switch
    {
        RedDot => 10, Laser => 20, Suppressor => 30, ExtendedMagazine => 40, Acog => 50, _ => 0,
    };

    public static string? Key(string? weaponClass)
    {
        if (weaponClass is null) return null;
        foreach (string stock in StockGuns)
            if (string.Equals(stock, weaponClass, StringComparison.OrdinalIgnoreCase)) return stock;
        return MercRifles.Find(weaponClass)?.ClassPath ?? Sidearms.Find(weaponClass)?.CarryId;
    }

    private static readonly string[] StockGuns = ["SBase.SWeaponDefense", "SBase.SWeaponDefense_Uzi", "SBase.SWeaponDefense_Shotgun"];

    public static string? Key(SWeaponDefense weapon) => weapon switch
    {
        MercSidearm pistol => pistol.Definition.CarryId,
        MercRifle rifle => rifle.Definition.ClassPath,
        PumpShotgun => MercRifles.PumpShotgun.ClassPath,
        SWeaponDefense_Uzi => StockGuns[1],
        SWeaponDefense_Shotgun => StockGuns[2],
        _ when weapon.GetType() == typeof(SWeaponDefense) => StockGuns[0],
        _ => null,
    };

    public static int Supported(string? weaponClass)
    {
        string? key = Key(weaponClass);
        if (key == null) return 0;
        int mask = Laser | Optics;
        var pistol = Sidearms.Find(key);
        if (pistol?.Kind != SidearmKind.Suppressed) mask |= Suppressor;
        if (pistol == null && key != StockGuns[2] && key != MercRifles.PumpShotgun.ClassPath && key != MercRifles.BoltRifle.ClassPath)
            mask |= ExtendedMagazine;
        return mask;
    }

    public static int Available(string? weaponClass, int kills)
    {
        int supported = Supported(weaponClass), available = 0;
        for (int bit = Laser; bit <= Acog; bit <<= 1)
            if ((supported & bit) != 0 && kills >= KillsRequired(bit)) available |= bit;
        return available;
    }

    public static int Cost(int mask)
    {
        int cost = 0;
        for (int bit = Laser; bit <= Acog; bit <<= 1)
            if ((mask & bit) != 0) cost += Points(bit);
        return cost;
    }

    public static bool CanCombine(int mask) => (mask & Optics) != Optics;

    public static bool IsValid(string? weaponClass, int mask, int kills) =>
        Key(weaponClass) != null && (mask & ~Available(weaponClass, kills)) == 0 &&
        CanCombine(mask) && Cost(mask) <= Budget;

    // A saved selection can lose an unlock when thresholds or the current career change.
    // Grant requests still go through IsValid with the owning player's authoritative kills.
    public static int EarnedSelection(string? weaponClass, int mask, int kills)
    {
        // Older bolt-action kits could equip the extended magazine. Keep other earned items from
        // a previously legal three-point kit. Authoritative grant requests still validate the whole mask.
        if ((mask & ExtendedMagazine) != 0 && Key(weaponClass) == MercRifles.BoltRifle.ClassPath && Cost(mask) <= Budget)
            mask &= ~ExtendedMagazine;
        return mask != 0 && IsValid(weaponClass, mask, int.MaxValue) ? mask & Available(weaponClass, kills) : 0;
    }

    public static bool SupportsExtendedMagazine(Type weaponType) =>
        weaponType == typeof(SWeaponDefense) || weaponType == typeof(SWeaponDefense_Uzi) ||
        (weaponType != typeof(BoltRifle) && typeof(MercRifle).IsAssignableFrom(weaponType));

    private static int RifleCapacity(int mask) => (mask & ExtendedMagazine) != 0 ? ExtendedCapacity : StandardCapacity;

    public static int MagazineCapacity(string? weaponClass, int mask)
    {
        if (Key(weaponClass) is not { } key) return 0;
        if (Sidearms.Find(key) is { } pistol) return pistol.Magazine;
        if (key == MercRifles.PumpShotgun.ClassPath) return MercRifles.PumpShotgun.Magazine;
        if (key == MercRifles.BoltRifle.ClassPath) return MercRifles.BoltRifle.Magazine;
        return key == StockGuns[2] ? ShotgunDefaults.Capacity : RifleCapacity(mask);
    }

    private static class ShotgunDefaults
    {
        internal static readonly int Capacity = new SWeaponDefense_Shotgun().ClipMaxAmmo;
    }

    public static int MagazineCapacity(MercRifleDefinition rifle, int mask) => MagazineCapacity(rifle.ClassPath, mask);

    // Initializes a fresh firearm, before the server publishes its loaded rounds and total ammo.
    public static void Apply(SWeaponDefense weapon, int mask)
    {
        weapon.AttachmentMask = mask != 0 && !IsValid(Key(weapon), mask, int.MaxValue) ? 0 : mask;
        if (!SupportsExtendedMagazine(weapon.GetType())) return;
        int reserve = weapon.MaxAmmo - weapon.ClipMaxAmmo;
        weapon.ClipAmmo = weapon.ClipMaxAmmo = RifleCapacity(weapon.AttachmentMask);
        weapon.Ammo = weapon.MaxAmmo = weapon.ClipMaxAmmo + reserve;
    }

    public static float OpticPower(SWeaponDefense? weapon)
    {
        int fitted = weapon?.AttachmentMask ?? 0;
        if ((fitted & Acog) != 0) return 4f;
        if ((fitted & RedDot) != 0) return 1f;
        return weapon is MercRifle rifle ? rifle.Definition.NativeOpticPower : 1f;
    }

    public static bool HasLaser(SBasePawn? pawn) => pawn?.MyWeapon is SWeaponDefense gun && (gun.AttachmentMask & Laser) != 0;

    public static int Granted(SBasePawn pawn, string? weaponClass, int requested) =>
        requested != 0 && Key(weaponClass) is { } key &&
        IsValid(key, requested, WeaponCareer.GetKills(pawn.Controller, key)) ? requested : 0;
}
