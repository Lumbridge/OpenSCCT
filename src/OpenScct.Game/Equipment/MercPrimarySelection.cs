using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Equipment;

/// <summary>Primary weapons offered to players. Original class references also serve scripted inventory and replays.</summary>
public static class MercPrimarySelection
{
    public const string Smg = "Svm.CompactSMG";
    public const string Shotgun = "Svm.PumpShotgun";

    private static readonly Dictionary<string, string> Replacements = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SBase.SWeaponDefense"] = WeaponAttachments.M4,
        ["SBase.SWeaponDefense_Uzi"] = Smg,
        ["SBase.SWeaponDefense_Shotgun"] = Shotgun,
    };

    public static bool IsLegacy(string? path) => path is not null && Replacements.ContainsKey(path);

    public static string SavedPreference(string? path)
    {
        string chosen = path?.Trim() ?? "";
        if (chosen.Length == 0) return WeaponAttachments.M4;
        return Replacements.GetValueOrDefault(chosen, chosen);
    }

    public static bool RequiresRifleOption(string? path) => MercRifles.Find(path) is { IsStandard: false };

    public static string[] Catalogue(IEnumerable<string> teamWeapons, bool riflesEnabled) =>
        teamWeapons.Where(w => !string.IsNullOrEmpty(w) && !IsLegacy(w) && (riflesEnabled || !RequiresRifleOption(w)))
            .OrderBy(RequiresRifleOption).ThenBy(EquipmentUnlocks.RequiredLevel).ToArray();

    /// <summary>Keep the player's selection or its replacement, then prefer an eligible M4 or stock SMG.</summary>
    public static string Choose(string? requested, IEnumerable<string> candidates, Func<string, bool> allowed)
    {
        requested = SavedPreference(requested);
        string fallback = "";
        int priority = int.MaxValue;
        foreach (string path in candidates)
        {
            if (string.IsNullOrEmpty(path) || IsLegacy(path) || !allowed(path)) continue;
            if (string.Equals(requested, path, StringComparison.OrdinalIgnoreCase)) return path;
            int rank = string.Equals(path, WeaponAttachments.M4, StringComparison.OrdinalIgnoreCase) ? 0 :
                string.Equals(path, Smg, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
            if (rank < priority)
            {
                fallback = path;
                priority = rank;
            }
        }
        return fallback;
    }

    public static ClassRef Resolve(ClassRef requested, STeamInfo team, PlayerController? controller)
    {
        bool Allowed(string path) => EquipmentUnlocks.Available(controller, path);
        string chosen = Choose(requested.Path, team.ClassWeapon.Select(w => w.Path ?? ""), Allowed);
        if (chosen.Length > 0) return new(chosen);

        // A custom team may explicitly contain only a legacy weapon.
        if (!requested.IsNone && team.ClassWeapon.Contains(requested) && Allowed(requested.Path!)) return requested;
        return team.ClassWeapon.FirstOrDefault(w => !w.IsNone && Allowed(w.Path!));
    }
}
