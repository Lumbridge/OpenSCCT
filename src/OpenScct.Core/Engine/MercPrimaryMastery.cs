using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenScct.Core.Engine;

public sealed record PrimaryMasteryWeapon(string ClassPath, string Id, string Name, string Family, bool HasSkinRewards);

/// <summary>Persistent achievement identities for the playable mercenary primaries. Family totals retain their original keys.</summary>
public static class MercPrimaryMastery
{
    public static readonly IReadOnlyList<PrimaryMasteryWeapon> All =
    [
        new("Svm.M4A1S", "m4a1s", "M4A1-S", "ar", true),
        new("Svm.AK47", "ak47", "AK-47", "ar", true),
        new("Svm.Galil", "galil", "GALIL", "ar", true),
        new("Svm.Famas", "famas", "FAMAS", "ar", true),
        new("Svm.CompactSMG", "compactsmg", "Compact SMG", "smg", false),
        new("Svm.PumpShotgun", "pumpshotgun", "SPAS-12", "shotgun", false),
        new("Svm.BoltRifle", "boltrifle", "Bolt-Action Sniper", "ar", false),
        new("Svm.DesignatedMarksman", "designatedmarksman", "Semi-Auto DMR", "ar", false),
    ];

    private static readonly Dictionary<string, PrimaryMasteryWeapon> ByClass =
        All.ToDictionary(g => g.ClassPath, StringComparer.OrdinalIgnoreCase);

    public static PrimaryMasteryWeapon? Find(string? classPath) =>
        classPath != null && ByClass.TryGetValue(classPath, out var gun) ? gun : null;
}
