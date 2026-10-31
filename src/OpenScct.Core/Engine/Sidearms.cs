using System;
using System.Collections.Generic;

namespace OpenScct.Core.Engine;

/// <summary>Stable identities shared by equipment, the master and cosmetics. Values are append-only.</summary>
public enum SidearmKind { Suppressed, Automatic, Magnum44, DesertEagle }

public sealed record SidearmDefinition(SidearmKind Kind, string Id, string Name, int Level, int Magazine,
    int Spare, int Damage, float Interval, float ReloadSeconds)
{
    public string CarryId => "OpenScct.Sidearm_" + Id;
    public string Asset => "OpenScctSidearms." + Id;
    public bool Automatic => Kind == SidearmKind.Automatic;
    public string Description => $"{Name}: {Magazine} rounds, {Spare} spare. Requires level {Level}. " +
        "Occupies one gadget slot. Use Swap Weapon to draw it.";
}

public static class Sidearms
{
    public const float SwitchSeconds = 0.5f;
    public static IReadOnlyList<SidearmDefinition> All { get; } =
    [
        new(SidearmKind.Suppressed, "suppressed", "Suppressed pistol", 1, 12, 48, 20, 0.25f, 2f),
        new(SidearmKind.Automatic, "automatic", "Automatic pistol", 15, 20, 80, 12, 0.1f, 2.3f),
        new(SidearmKind.Magnum44, "magnum44", ".44 Magnum", 30, 6, 24, 55, 0.6f, 2.8f),
        new(SidearmKind.DesertEagle, "desert_eagle", "Desert Eagle", 45, 7, 21, 50, 0.4f, 2.6f),
    ];
    public static SidearmDefinition Get(SidearmKind kind) => All[(int)kind];
    public static SidearmDefinition? Find(string? id)
    {
        foreach (var item in All)
            if (string.Equals(item.CarryId, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) return item;
        return null;
    }
    public static bool Available(SidearmKind kind, int currentLevel, bool unrestricted) =>
        unrestricted || Math.Max(1, currentLevel) >= Get(kind).Level;
}
