namespace OpenScct.Server.Online;

/// <summary>
/// A listen server's own signed-in account: its id on the master, its claimed name and the cosmetics it has unlocked
/// (as the master last told the host's game), and its rank (OpenScct.Net.Cosmetics.PlayerRank packed, no title) and titles.
/// </summary>
public sealed record HostAccount(long AccountId, string Name, System.Collections.Generic.IReadOnlyList<string>? Unlocked = null,
    int Rank = 0, System.Collections.Generic.IReadOnlyList<string>? Titles = null);
