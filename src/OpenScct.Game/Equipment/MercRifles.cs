using System;
using System.Collections.Generic;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Equipment;

/// <summary>Gameplay profiles for custom mercenary primaries. Precision profiles specify final body and head damage.</summary>
public sealed record MercRifleDefinition(string Id, string Name, string Role, string Description,
    int Damage, int Magazine, int Reserve, float Interval, float ReloadSeconds,
    float CloseFactor, float MediumFactor, float FarFactor, float MinSpread, float MaxSpread,
    float VerticalRecoil, float HorizontalRecoil, bool Suppressed = false, bool BurstDefault = false,
    float RangeModifier = .98f, float EyeRelief = .18f, int Level = 1, float? EmptyReloadSeconds = null,
    bool IsStandard = false, float NativeOpticPower = 1f, int HeadDamage = 0,
    string? FireCue = null, string? ReloadCue = null)
{
    public string ClassPath => "Svm." + Id;
    public string Asset => "OpenScctRifles." + Id.ToLowerInvariant();
    public int CloseDamage => (int)(Damage * CloseFactor);
    public int Rpm => (int)MathF.Round(60f / Interval);
    public float ReloadDuration(int remainingRounds) =>
        remainingRounds == 0 ? EmptyReloadSeconds ?? ReloadSeconds : ReloadSeconds;
}

public static class MercRifles
{
    public static readonly MercRifleDefinition CompactSmg = new("CompactSMG", "Compact SMG", "MOBILITY / AUTOMATIC",
        "Fast automatic fire with a short reload. Carries a 20-round magazine and 100 rounds in reserve.",
        12, WeaponAttachments.StandardCapacity, 100, .077f, 2.05f, 1.2f, .8f, .48f, .19f, .95f, 2.1f, 3f,
        RangeModifier: .9f, IsStandard: true,
        FireCue: "Armes_impacts.Armes.SE_Uzi", ReloadCue: "Armes_impacts.Armes.UZIReload1");
    public static readonly MercRifleDefinition PumpShotgun = new("PumpShotgun", "SPAS-12", "PUMP / SPREAD",
        "Fires a group of pellets with each pump cycle. Holds six shells and reloads only the shells it needs.",
        17, 6, 44, .85f, 3f, 7f, 1.57f, .43f, .2f, 1f, 5f, 4f, RangeModifier: .7f, IsStandard: true);
    public static readonly MercRifleDefinition BoltRifle = new("BoltRifle", "Bolt-Action Sniper", "BOLT ACTION / 6×",
        "A five-round magazine, a built-in 6× scope and a manual bolt cycle. A body hit deals 70 damage and a head hit deals 100.",
        70, 5, 40, 1.05f, 3.1f, 1f, 1f, 1f, .05f, .7f, 6f, 3f,
        RangeModifier: 1f, EyeRelief: .36f, NativeOpticPower: 6f, HeadDamage: 100,
        FireCue: "OpenScctRifles.ak47_fire", ReloadCue: "OpenScctRifles.ak47_reload");
    public static readonly MercRifleDefinition DesignatedMarksman = new("DesignatedMarksman", "Semi-Auto DMR", "SEMI-AUTO / 4×",
        "A built-in 4× scope and one shot per trigger press. A body hit deals 35 damage and a head hit deals 60.",
        35, WeaponAttachments.StandardCapacity, 60, .27f, 2.7f, 1f, 1f, 1f, .07f, .73f, 3.8f, 2.5f,
        RangeModifier: 1f, EyeRelief: .30f, NativeOpticPower: 4f, HeadDamage: 60,
        FireCue: "OpenScctRifles.galil_fire", ReloadCue: "OpenScctRifles.galil_reload");

    public static readonly IReadOnlyList<MercRifleDefinition> All =
    [
        new("M4A1S", "M4A1-S", "CONTROL / ACCURACY",
            "An accurate automatic rifle with a 20-round magazine. An extended magazine holds 30 rounds.",
            // Ammo becomes usable at 70% of the calm 01 montage, played at the equipped 1.15x rate.
            14, WeaponAttachments.StandardCapacity, 80, .09f, (103f / 30f) * .7f / 1.15f, 1.3f, .76f, .53f, .11f, .58f, 1.6f, 3f,
            RangeModifier: .94f, EyeRelief: .18f, Level: 30, EmptyReloadSeconds: (125f / 30f) * .7f / 1.15f),
        new("AK47", "AK-47", "POWER / RECOIL",
            "Hard-hitting fire with strong vertical and horizontal recoil and iron sights. Short bursts are effective; sustained fire loses precision.",
            22, WeaponAttachments.StandardCapacity, 90, .1f, 2.8f, 1.32f, .82f, .53f, .18f, .88f, 4.4f, 6.2f, EyeRelief: .11f, Level: 20),
        new("Galil", "GALIL", "RESERVE / SUSTAIN",
            "A large ammunition reserve supports sustained defence. Lower damage, wider spread and a long reload give spies a chance to escape.",
            14, WeaponAttachments.StandardCapacity, 105, .09f, 3f, 1.32f, .79f, .63f, .2f, .8f, 1.6f, 3f, EyeRelief: .12f, Level: 1),
        new("Famas", "FAMAS", "BURST / PRECISION",
            "A compact bullpup that starts in three-round burst mode. Tight spread rewards measured bursts. The fire-mode key switches between burst and full auto.",
            14, WeaponAttachments.StandardCapacity, 100, .09f, 2.65f, 1.3f, .8f, .59f, .09f, .54f, 1.6f, 3f, BurstDefault: true, RangeModifier: .96f, EyeRelief: .13f, Level: 5),
        CompactSmg, PumpShotgun, BoltRifle, DesignatedMarksman,
    ];

    public static MercRifleDefinition? Find(string? path)
    {
        foreach (var rifle in All)
            if (string.Equals(rifle.ClassPath, path, StringComparison.OrdinalIgnoreCase)) return rifle;
        return null;
    }

    public static float NoiseScale(SWeaponDefense weapon) => weapon is MercSidearm { Kind: 0 } ? .25f :
        (weapon.AttachmentMask & WeaponAttachments.Suppressor) != 0 ? .4f : 1f;
}
