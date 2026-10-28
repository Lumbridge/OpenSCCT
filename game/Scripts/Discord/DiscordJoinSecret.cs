using System;

namespace OpenScctGame.Discord;

/// <summary>Where a Discord Join goes: a server listed on the master, a server by address, or a hub by address.</summary>
public enum DiscordJoinKind
{
    Master,
    Address,
    Hub,
}

/// <summary>
/// The join secret on the player's Discord status (docs/DISCORD.md): "oscct1|m|&lt;master server id&gt;",
/// "oscct1|a|&lt;host:port&gt;" or "oscct1|h|&lt;host:port&gt;". Discord hands it only to friends the player let in.
/// A listed server goes by its master id so friends reach it the way the browser does (NAT punch, relay, tickets).
/// A server with a password carries "mp" or "ap" instead (<see cref="NeedsPassword"/>): the password itself never goes
/// to Discord; the friend's game asks for it. Older builds do not know those kinds and ignore the Join.
/// </summary>
public sealed record DiscordJoinSecret(DiscordJoinKind Kind, string Value, bool NeedsPassword = false)
{
    private const string Prefix = "oscct1";

    /// <summary>Discord's limit on a join secret.</summary>
    private const int MaxLength = 128;

    public override string ToString() => Prefix + "|" + Kind switch
    {
        DiscordJoinKind.Master => "m",
        DiscordJoinKind.Hub => "h",
        _ => "a",
    } + (NeedsPassword && Kind != DiscordJoinKind.Hub ? "p" : string.Empty) + "|" + Value;

    /// <summary>The secret's text, or null when it would not fit Discord's limit.</summary>
    public string? Format()
    {
        string text = ToString();
        return text.Length <= MaxLength ? text : null;
    }

    public static DiscordJoinSecret? Parse(string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return null;
        }

        string[] parts = secret.Split('|', 3);
        if (parts.Length != 3 || parts[0] != Prefix || parts[2].Trim().Length == 0)
        {
            return null;
        }

        DiscordJoinKind? kind = parts[1] switch
        {
            "m" or "mp" => DiscordJoinKind.Master,
            "a" or "ap" => DiscordJoinKind.Address,
            "h" => DiscordJoinKind.Hub,
            _ => null,
        };
        return kind is { } k ? new DiscordJoinSecret(k, parts[2].Trim(), parts[1].EndsWith('p')) : null;
    }

    /// <summary>False for addresses only this PC can reach (a friend could never join them).</summary>
    public static bool Shareable(string address) =>
        address.Length > 0 &&
        !address.StartsWith("127.", StringComparison.Ordinal) &&
        !address.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) &&
        !address.StartsWith("[::1]", StringComparison.Ordinal);
}
