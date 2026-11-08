using System.Collections.Generic;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;

namespace OpenScct.Server.Online;

public sealed partial class OnlineServer
{
    internal void ApplyAccountAppearances(IReadOnlyDictionary<long, LiveAccountAppearance> updates)
    {
        foreach (var player in _players)
        {
            if (player.AccountId is not { } id || !updates.TryGetValue(id, out var update)) continue;
            player.Pronouns = OpenScct.Core.Engine.PlayerPronouns.Normalize(update.Pronouns);
            if (player.Slot?.Controller?.PlayerReplicationInfo is { } identity) identity.SvmPronouns = player.Pronouns;
            if (player.AppearanceRevision > update.Appearance.Revision) continue;
            player.Unlocks = new CosmeticUnlocks(update.Unlocks);
            if (player.AppearanceRevision >= update.Appearance.Revision) continue;
            player.AppearanceRevision = update.Appearance.Revision;
            player.Rank = update.Rank;
            player.SpyLook = CosmeticLook.FromIds(CosmeticSide.Spy, update.Appearance.Spy).Validate(CosmeticSide.Spy, player.Unlocks);
            player.MercLook = CosmeticLook.FromIds(CosmeticSide.Merc, update.Appearance.Merc).Validate(CosmeticSide.Merc, player.Unlocks);
            if (player.Slot?.Controller?.PlayerReplicationInfo is { } pri)
            {
                pri.SvmRank = player.Rank;
                var look = player.Team == 1 ? player.MercLook : player.SpyLook;
                (pri.SvmLookLow, pri.SvmLookHigh) = look.Split();
                pri.SvmLookExtraLow = (int)look.Extra;
                pri.SvmLookExtraHigh = (int)(look.Extra >> 32);
            }
            Send(null, RankLine(player));
        }
    }
}
