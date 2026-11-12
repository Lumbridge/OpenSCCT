using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

/// <summary>A sign-in the game started and polls, until the browser finishes it with Discord.</summary>
internal sealed class PendingLink
{
    /// <summary>The code as shown (<c>XXXX-XXXX</c>).</summary>
    public required string Code { get; init; }

    public required string PollSecret { get; init; }

    /// <summary>Unix seconds.</summary>
    public required long Expires { get; init; }

    public LinkStatus Status { get; set; } = LinkStatus.Pending;

    /// <summary>The browser came back and the master is talking to Discord (a second callback is refused).</summary>
    public bool Busy { get; set; }

    public string? SessionToken { get; set; }

    public long AccountId { get; set; }

    public string? Error { get; set; }

    /// <summary>Who started it (the address the per-address limit counts by), or null.</summary>
    public System.Net.IPAddress? From { get; init; }
}

/// <summary>Sign-ins in progress, in memory, ten minutes each.</summary>
internal sealed class PendingLinks(TimeProvider time)
{
    /// <summary>Seconds a sign-in may take.</summary>
    public const int LifetimeSeconds = 600;

    /// <summary>Seconds an ended sign-in is kept so the game's next poll still hears how it ended.</summary>
    private const int KeepSeconds = 300;

    private const int MaxLinks = 5000;

    /// <summary>Sign-ins one address may have open; a new one beyond that replaces its oldest.</summary>
    private const int MaxLinksPerAddress = 8;

    /// <summary>No 0/O, 1/I/L: the code may be read off the screen and typed.</summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private readonly Dictionary<string, PendingLink> _links = new(StringComparer.Ordinal);
    private readonly TimeProvider _time = time;

    private long Now => _time.GetUtcNow().ToUnixTimeSeconds();

    /// <summary>A new sign-in, or null when too many are open.</summary>
    public PendingLink? Start(System.Net.IPAddress? from = null)
    {
        lock (_links)
        {
            if (from is not null)
            {
                int open = 0;
                string? oldestKey = null;
                long oldestExpires = long.MaxValue;
                foreach (KeyValuePair<string, PendingLink> entry in _links)
                {
                    if (entry.Value.From is { } other && other.Equals(from) && entry.Value.Status == LinkStatus.Pending && !entry.Value.Busy)
                    {
                        open++;
                        if (entry.Value.Expires < oldestExpires)
                        {
                            oldestExpires = entry.Value.Expires;
                            oldestKey = entry.Key;
                        }
                    }
                }

                if (open >= MaxLinksPerAddress && oldestKey is not null)
                {
                    _links.Remove(oldestKey);
                }
            }

            if (_links.Count >= MaxLinks)
            {
                return null;
            }

            string key;
            do
            {
                key = RandomNumberGenerator.GetString(Alphabet, 8);
            }
            while (_links.ContainsKey(key));

            byte[] secret = RandomNumberGenerator.GetBytes(24);
            var link = new PendingLink
            {
                Code = key[..4] + "-" + key[4..],
                PollSecret = Convert.ToBase64String(secret).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
                Expires = Now + LifetimeSeconds,
                From = from,
            };
            _links[key] = link;
            return link;
        }
    }

    /// <summary>The sign-in <paramref name="code"/> names (any case, with or without the dash), with its status brought up to date.</summary>
    public PendingLink? Find(string? code)
    {
        string? key = Key(code);
        if (key is null)
        {
            return null;
        }

        lock (_links)
        {
            if (!_links.TryGetValue(key, out PendingLink? link))
            {
                return null;
            }

            if (link.Status == LinkStatus.Pending && !link.Busy && Now > link.Expires)
            {
                link.Status = LinkStatus.Expired;
            }

            return link;
        }
    }

    /// <summary>True when <paramref name="secret"/> is the sign-in's poll secret.</summary>
    public static bool SecretMatches(PendingLink link, string? secret) =>
        secret is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(link.PollSecret), Encoding.UTF8.GetBytes(secret));

    /// <summary>Takes the sign-in for one browser callback; false when it is not waiting any more (used, expired).</summary>
    public bool TryBegin(PendingLink link)
    {
        lock (_links)
        {
            if (link.Status != LinkStatus.Pending || link.Busy || Now > link.Expires)
            {
                return false;
            }

            link.Busy = true;
            return true;
        }
    }

    public void Complete(PendingLink link, string sessionToken, long accountId)
    {
        lock (_links)
        {
            link.SessionToken = sessionToken;
            link.AccountId = accountId;
            link.Status = LinkStatus.Done;
            link.Busy = false;
        }
    }

    public void Refuse(PendingLink link, string error)
    {
        lock (_links)
        {
            link.Error = error;
            link.Status = LinkStatus.Refused;
            link.Busy = false;
        }
    }

    public void Remove(PendingLink link)
    {
        lock (_links)
        {
            if (Key(link.Code) is { } key)
            {
                _links.Remove(key);
            }
        }
    }

    /// <summary>Drops sign-ins well past their expiry (an unread token goes with them).</summary>
    public void Sweep()
    {
        lock (_links)
        {
            long now = Now;
            var old = new List<string>();
            foreach (KeyValuePair<string, PendingLink> e in _links)
            {
                if (now > e.Value.Expires + KeepSeconds && !e.Value.Busy)
                {
                    old.Add(e.Key);
                }
            }

            foreach (string key in old)
            {
                _links.Remove(key);
            }
        }
    }

    private static string? Key(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > 16)
        {
            return null;
        }

        var sb = new StringBuilder(8);
        foreach (char c in code)
        {
            if (c is '-' or ' ')
            {
                continue;
            }

            char u = char.ToUpperInvariant(c);
            if (!Alphabet.Contains(u, StringComparison.Ordinal))
            {
                return null;
            }

            sb.Append(u);
        }

        return sb.Length == 8 ? sb.ToString() : null;
    }
}
