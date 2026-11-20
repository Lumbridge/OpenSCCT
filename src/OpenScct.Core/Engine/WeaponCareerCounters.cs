using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenScct.Core.Engine;

/// <summary>Append-only order for the per-weapon totals in a player's signed join ticket.</summary>
public static class WeaponCareerCounters
{
    private static readonly (string Weapon, string Key)[] Counters =
    [
        ("SBase.SWeaponDefense", "merc.fn2000.kills"),
        ("SBase.SWeaponDefense_Uzi", "merc.smg.kills"),
        ("SBase.SWeaponDefense_Shotgun", "merc.shotgun.kills"),
        ("Svm.M4A1S", "merc.m4a1s.kills"),
        ("Svm.AK47", "merc.ak47.kills"),
        ("Svm.Galil", "merc.galil.kills"),
        ("Svm.Famas", "merc.famas.kills"),
        .. Sidearms.All.Select(p => (p.CarryId, "merc." + p.Id + ".kills")),
        ("Svm.CompactSMG", "merc.compactsmg.kills"),
        ("Svm.PumpShotgun", "merc.pumpshotgun.kills"),
        ("Svm.BoltRifle", "merc.boltrifle.kills"),
        ("Svm.DesignatedMarksman", "merc.designatedmarksman.kills"),
    ];
    private static readonly Dictionary<string, int> Indices = Counters.Select((entry, index) => (entry, index))
        .ToDictionary(pair => pair.entry.Weapon, pair => pair.index, StringComparer.OrdinalIgnoreCase);

    public static int Count => Counters.Length;
    public static int IndexOf(string? weapon) => weapon != null && Indices.TryGetValue(weapon, out int index) ? index : -1;
    public static string Key(int index) => Counters[index].Key;

    // The family feats still drive existing achievements. These guns also need their own exact counters.
    public static readonly string[] NewRifleKeys = [Key(0), Key(3), Key(4), Key(5), Key(6),
        "merc.compactsmg.kills", "merc.pumpshotgun.kills", "merc.boltrifle.kills", "merc.designatedmarksman.kills"];

    public static int[] FromFeats(IReadOnlyDictionary<string, int>? feats)
    {
        var counts = Counters.Select(entry =>
            feats != null && feats.TryGetValue(entry.Key, out int value) ? Math.Max(0, value) : 0).ToArray();
        // Historically the stock Uzi and stock shotgun were the sole members of their families.
        counts[1] = Math.Max(0, counts[1] - counts[IndexOf("Svm.CompactSMG")]);
        counts[2] = Math.Max(0, counts[2] - counts[IndexOf("Svm.PumpShotgun")]);
        return counts;
    }
}
