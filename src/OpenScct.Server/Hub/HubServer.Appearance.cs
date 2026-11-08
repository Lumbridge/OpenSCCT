using System.Collections.Generic;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;

namespace OpenScct.Server.Hub;

public sealed partial class HubServer
{
    internal void ApplyAccountAppearances(IReadOnlyDictionary<long, LiveAccountAppearance> updates)
    {
        foreach (var player in _players)
        {
            if (player.AccountId is not { } id || !updates.TryGetValue(id, out var update)) continue;
            if (player.Slot.Controller?.PlayerReplicationInfo is { } identity)
                identity.SvmPronouns = OpenScct.Core.Engine.PlayerPronouns.Normalize(update.Pronouns);
            if (player.AppearanceRevision > update.Appearance.Revision) continue;
            player.Unlocks = new CosmeticUnlocks(update.Unlocks);
            if (player.AppearanceRevision >= update.Appearance.Revision) continue;
            player.AppearanceRevision = update.Appearance.Revision;
            player.SpyLook = CosmeticLook.FromIds(CosmeticSide.Spy, update.Appearance.Spy).Validate(CosmeticSide.Spy, player.Unlocks);
            player.MercLook = CosmeticLook.FromIds(CosmeticSide.Merc, update.Appearance.Merc).Validate(CosmeticSide.Merc, player.Unlocks);
            if (player.Slot.Controller?.PlayerReplicationInfo is { } pri) pri.SvmRank = update.Rank;
            WearLook(player);
        }
    }
}
