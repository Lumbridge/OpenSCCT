using System;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Svm;

namespace OpenScct.Game.Equipment;

/// <summary>Shared gear-picker and authority checks for the optional rifles and spy gadgets.</summary>
public static class EquipmentUnlocks
{
    public static int RequiredLevel(string? path) => MercRifles.Find(path)?.Level ??
        (string.Equals(path, MineHackGadget.ClassPath, StringComparison.OrdinalIgnoreCase) ? 5 :
         string.Equals(path, CloneGadget.ClassPath, StringComparison.OrdinalIgnoreCase) ? 10 : 0);

    public static bool LevelAllows(string? path, int level, bool unrestricted) =>
        unrestricted || Math.Max(1, level) >= RequiredLevel(path);

    public static bool Available(PlayerController? pc, string? path) =>
        (!MercPrimarySelection.RequiresRifleOption(path) || pc?.GameReplicationInfo?.RiflesEnabled == true) &&
        LevelAllows(path, (pc?.PlayerReplicationInfo?.SvmRank ?? 0) & 255,
            pc is not null && (pc.Level.NetMode == ENetMode.NM_Standalone ||
            pc.PlayerReplicationInfo?.SvmSidearmsUnrestricted == true || Modes.PracticeRules.IsOn(pc)));
}
