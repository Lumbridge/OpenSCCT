using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace OpenScct.Server.Online;

/// <summary>
/// One ban: any of the player's master account, install id, address or name, with a reason and an end (null: for
/// good).
/// </summary>
public sealed record BanEntry
{
    /// <summary>The signed-in player's master account id, or null for a guest.</summary>
    public long? AccountId { get; init; }

    public string? InstallId { get; init; }

    public string? Address { get; init; }

    /// <summary>A name that is banned (guests' bans and bans made before accounts); null for a signed-in player's ban.</summary>
    public string? Name { get; init; }

    /// <summary>Who the ban was for, for the admin to read; never matched against joining players.</summary>
    public string? Label { get; init; }

    public string Reason { get; init; } = string.Empty;

    public DateTimeOffset? Until { get; init; }

    public DateTimeOffset Added { get; init; }
}

/// <summary>
/// The server's bans, kept in a JSON file (FEATURES rl:ac.bans: bans by player id and address, no hardware
/// fingerprints). The player id is the client's install id until signed player keys exist. The wall clock comes from
/// the host process, as the simulation libraries never read it.
/// </summary>
public sealed class BanList
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly List<BanEntry> _entries = new();
    private readonly string? _path;
    private readonly Func<DateTimeOffset> _now;

    public BanList(string? path, Func<DateTimeOffset> now)
    {
        _path = path;
        _now = now ?? throw new ArgumentNullException(nameof(now));
        if (path is not null && File.Exists(path))
        {
            try
            {
                _entries.AddRange(JsonSerializer.Deserialize<List<BanEntry>>(File.ReadAllText(path), Json) ?? []);
            }
            catch (JsonException)
            {
                // A damaged file is kept for the admin to look at and replaced on the next ban.
            }
        }
    }

    public IReadOnlyList<BanEntry> Entries => _entries;

    /// <summary>The ban matching any of the given identities, or null.</summary>
    public BanEntry? Find(string? installId, string? address, string? name, long? accountId = null)
    {
        DateTimeOffset now = _now();
        string? ip = IpOf(address);
        foreach (BanEntry e in _entries)
        {
            if (e.Until is { } until && until < now)
            {
                continue;
            }

            if ((e.AccountId is { } account && account == accountId) ||
                (e.InstallId is { Length: > 0 } id && id == installId) ||
                (e.Address is { Length: > 0 } a && a == ip) ||
                (e.Name is { Length: > 0 } n && string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            {
                return e;
            }
        }

        return null;
    }

    /// <summary>
    /// Bans a player for <paramref name="minutes"/> (0: for good) and saves the list. A signed-in player
    /// (<paramref name="accountId"/>) is banned by account, install id and address; his name is kept as the label
    /// only, as the account already covers it.
    /// </summary>
    public BanEntry Add(string? installId, string? address, string? name, string reason, int minutes, long? accountId = null)
    {
        DateTimeOffset now = _now();
        string? cleanName = string.IsNullOrEmpty(name) ? null : name;
        var entry = new BanEntry
        {
            AccountId = accountId,
            InstallId = string.IsNullOrEmpty(installId) ? null : installId,
            Address = IpOf(address),
            Name = accountId is null ? cleanName : null,
            Label = cleanName,
            Reason = reason,
            Until = minutes > 0 ? now.AddMinutes(minutes) : null,
            Added = now,
        };
        _entries.Add(entry);
        Save();
        return entry;
    }

    /// <summary>Removes bans matching an account id, install id, address, name or label; returns how many.</summary>
    public int Remove(string what)
    {
        long? account = long.TryParse(what, NumberStyles.None, CultureInfo.InvariantCulture, out long a) ? a : null;
        int n = _entries.RemoveAll(e => (account is not null && e.AccountId == account) || e.InstallId == what || e.Address == what ||
            string.Equals(e.Name, what, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Label, what, StringComparison.OrdinalIgnoreCase));
        if (n > 0)
        {
            Save();
        }

        return n;
    }

    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        DateTimeOffset now = _now();
        _entries.RemoveAll(e => e.Until is { } until && until < now);
        File.WriteAllText(_path, JsonSerializer.Serialize(_entries, Json));
    }

    /// <summary>The IP part of "ip:port", "[v6]:port" or "relay ip:port".</summary>
    public static string? IpOf(string? address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return null;
        }

        string a = address.StartsWith("relay ", StringComparison.Ordinal) ? address[6..] : address;
        if (a.StartsWith('['))
        {
            int close = a.IndexOf(']', StringComparison.Ordinal);
            return close > 0 ? a[1..close] : a;
        }

        int colon = a.LastIndexOf(':');
        return colon > 0 && a.IndexOf(':', StringComparison.Ordinal) == colon ? a[..colon] : a;
    }
}
