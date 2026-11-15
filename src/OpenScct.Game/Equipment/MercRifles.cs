using System;
using System.Collections.Generic;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Equipment;

/// <summary>OpenSCCT sidegrades tuned against the stock rifle's 25 close damage every 0.2 seconds.
/// Damage below is before SCCT's existing hit-zone multipliers. These are design estimates, not retail facts.</summary>
public sealed record MercRifleDefinition(string Id, string Name, string Role, string Description,
    int Damage, int Magazine, int Reserve, float Interval, float ReloadSeconds,
    float CloseFactor, float MediumFactor, float FarFactor, float MinSpread, float MaxSpread,
    float VerticalRecoil, float HorizontalRecoil, bool Suppressed = false, bool BurstDefault = false)
{
    public string ClassPath => "Svm." + Id;
    public string Asset => "OpenScctRifles." + Id.ToLowerInvariant();
    public int CloseDamage => (int)(Damage * CloseFactor);
    public int Rpm => (int)MathF.Round(60f / Interval);
}

public static class MercRifles
{
    public static readonly IReadOnlyList<MercRifleDefinition> All =
    [
        new("M4A1S", "M4A1-S", "SUPPRESSED / CONTROL",
            "A suppressed M4A1 with a quiet report and steady handling. Its 20-round magazine and lower damage reward careful shots. Hip fire only.",
            17, 20, 80, .175f, 2.4f, 1.3f, .76f, .53f, .11f, .58f, 1.6f, 3f, Suppressed: true),
        new("AK47", "AK-47", "POWER / RECOIL",
            "Hard-hitting, deliberate fire with strong vertical and horizontal recoil. Short bursts are effective; sustained fire loses precision. Hip fire only.",
            22, 30, 90, .22f, 2.8f, 1.32f, .82f, .53f, .18f, .88f, 4.4f, 6.2f),
        new("Galil", "GALIL", "CAPACITY / SUSTAIN",
            "A 35-round magazine for sustained defence. Lower damage, wider spread and the longest reload leave room for a Spy to escape or close the distance. Hip fire only.",
            16, 35, 105, .185f, 3f, 1.32f, .79f, .63f, .2f, .8f, 2.7f, 4.8f),
        new("Famas", "FAMAS", "BURST / PRECISION",
            "A compact bullpup that starts in three-round burst mode. Tight spread rewards measured bursts; a 25-round magazine limits sustained fire. The fire-mode key switches burst and auto. Hip fire only.",
            17, 25, 100, .175f, 2.65f, 1.3f, .8f, .59f, .09f, .54f, 2.1f, 3.5f, BurstDefault: true),
    ];

    public static MercRifleDefinition? Find(string? path)
    {
        foreach (var rifle in All)
            if (string.Equals(rifle.ClassPath, path, StringComparison.OrdinalIgnoreCase)) return rifle;
        return null;
    }

    public static float NoiseScale(SWeaponDefense weapon) => weapon is Svm.MercRifle { Definition.Suppressed: true } ? .4f :
        weapon is MercSidearm { Kind: 0 } ? .25f : 1f;
}
