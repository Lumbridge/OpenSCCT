using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace OpenScct.Net.Discovery;

/// <summary>
/// Rules for player names (docs/ONLINE.md, "Accounts"): what a claimed account name may be, the folded key that
/// keeps look-alike names apart, and the <c>Name#3fa2</c> form guests get.
/// </summary>
public static class PlayerNames
{
    public const int MinClaimLength = 3;

    public const int MaxClaimLength = 16;

    /// <summary>The part of a guest's own name kept before <c>#tag</c>, so the whole fits the game's 20 characters.</summary>
    public const int MaxGuestBaseLength = 15;

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "admin", "administrator", "mod", "moderator", "server", "guest", "player", "host", "system", "console",
        "openscct", "official", "staff", "support", "everyone", "nobody", "unknown",
    };

    /// <summary>Why <paramref name="name"/> cannot be claimed, or null when it can.</summary>
    public static string? ClaimProblem(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Pick a name.";
        }

        if (name.Length < MinClaimLength || name.Length > MaxClaimLength)
        {
            return $"Names are {MinClaimLength} to {MaxClaimLength} characters.";
        }

        if (!char.IsAsciiLetterOrDigit(name[0]))
        {
            return "Names start with a letter or a digit.";
        }

        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.'))
            {
                return "Names use letters, digits, _ - and . only.";
            }
        }

        string key = Key(name);
        if (key.Length < 2)
        {
            return "That name is too short.";
        }

        foreach (string word in Reserved)
        {
            if (key == Key(word))
            {
                return "That name is reserved.";
            }
        }

        return null;
    }

    /// <summary>
    /// The folded form two names are compared by: lower case, separators dropped and look-alike characters merged
    /// (0/o, 1/i/l, 5/s, 3/e, 4/a, 7/t, 8/b), so "Ry_an" and "RYAN" and "ryan" are the same name.
    /// </summary>
    public static string Key(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var sb = new StringBuilder(name.Length);
        foreach (char raw in name)
        {
            char c = char.ToLowerInvariant(raw);
            switch (c)
            {
                case '_' or '-' or '.' or ' ':
                    continue;
                case '0':
                    c = 'o';
                    break;
                case '1' or 'i':
                    c = 'l';
                    break;
                case '5':
                    c = 's';
                    break;
                case '3':
                    c = 'e';
                    break;
                case '4':
                    c = 'a';
                    break;
                case '7':
                    c = 't';
                    break;
                case '8':
                    c = 'b';
                    break;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>The four hex digits a guest's name carries, from its install id (the same on every server).</summary>
    public static string GuestTag(string? installId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(installId ?? string.Empty));
        return Convert.ToHexString(hash, 0, 2).ToLowerInvariant();
    }

    /// <summary>A guest's shown name: its own name (cleaned, cut to 15) then <c>#tag</c>.</summary>
    public static string GuestName(string? name, string? installId)
    {
        string own = (name ?? string.Empty).Replace("#", string.Empty, StringComparison.Ordinal).Trim();
        if (own.Length == 0)
        {
            own = "Player";
        }

        if (own.Length > MaxGuestBaseLength)
        {
            own = own[..MaxGuestBaseLength].TrimEnd();
        }

        return own + "#" + GuestTag(installId);
    }
}
