using System.Linq;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;

namespace OpenScct.Server.Online;

/// <summary>The rank a joining player shows (spec/additions/progression.md), checked against what the master signed.</summary>
public static class PlayerRanks
{
    /// <summary>
    /// The rank from the master's ticket (or the listen host's own account) with <paramref name="titleId"/> when that
    /// account has unlocked it, packed; 0 for guests.
    /// </summary>
    public static int Checked(string? titleId, PlayerTicket? ticket, HostAccount? host = null)
    {
        PlayerRank rank = PlayerRank.Unpack(ticket?.Rank ?? host?.Rank ?? 0);
        if (rank.IsNone)
        {
            return 0;
        }

        int titleBit = -1;
        if (TitleCatalog.Find(titleId) is { } title &&
            (ticket is not null ? TitleCatalog.Has(ticket.Titles, title) : host?.Titles?.Contains(title.Id) == true))
        {
            titleBit = title.Bit;
        }

        return rank.WithTitle(titleBit).Pack();
    }
}
