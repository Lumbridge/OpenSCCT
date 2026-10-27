using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace OpenScct.Launcher.Versioning;

/// <summary>
/// A Semantic Versioning 2.0.0 version: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD].
/// A leading "v" (release tags like v0.2.0) is accepted. Build metadata is kept but ignored when comparing.
/// </summary>
public sealed class SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
{
    private SemVersion(int major, int minor, int patch, IReadOnlyList<string> prerelease, string build)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        Build = build;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The dot separated prerelease identifiers, empty for a release.</summary>
    public IReadOnlyList<string> Prerelease { get; }

    /// <summary>Build metadata after "+", empty when there is none.</summary>
    public string Build { get; }

    public bool IsPrerelease => Prerelease.Count > 0;

    public static SemVersion Parse(string text) =>
        TryParse(text, out SemVersion? version) ? version : throw new FormatException($"Not a semantic version: '{text}'.");

    public static bool TryParse(string? text, [NotNullWhen(true)] out SemVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
        {
            s = s[1..];
        }

        string build = string.Empty;
        int plus = s.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            build = s[(plus + 1)..];
            s = s[..plus];
            if (!ValidIdentifiers(build, numericNoLeadingZero: false))
            {
                return false;
            }
        }

        var prerelease = new List<string>();
        int dash = s.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            string pre = s[(dash + 1)..];
            s = s[..dash];
            if (!ValidIdentifiers(pre, numericNoLeadingZero: true))
            {
                return false;
            }

            prerelease.AddRange(pre.Split('.'));
        }

        string[] core = s.Split('.');
        if (core.Length != 3
            || !TryCore(core[0], out int major)
            || !TryCore(core[1], out int minor)
            || !TryCore(core[2], out int patch))
        {
            return false;
        }

        version = new SemVersion(major, minor, patch, prerelease, build);
        return true;
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int c = Major.CompareTo(other.Major);
        if (c != 0)
        {
            return c;
        }

        c = Minor.CompareTo(other.Minor);
        if (c != 0)
        {
            return c;
        }

        c = Patch.CompareTo(other.Patch);
        if (c != 0)
        {
            return c;
        }

        // A release ranks above any of its prereleases.
        if (Prerelease.Count == 0 || other.Prerelease.Count == 0)
        {
            return other.Prerelease.Count.CompareTo(Prerelease.Count);
        }

        int n = Math.Min(Prerelease.Count, other.Prerelease.Count);
        for (int i = 0; i < n; i++)
        {
            c = CompareIdentifier(Prerelease[i], other.Prerelease[i]);
            if (c != 0)
            {
                return c;
            }
        }

        return Prerelease.Count.CompareTo(other.Prerelease.Count);
    }

    public bool Equals(SemVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', Prerelease));

    /// <summary>The version without the "v" prefix and without build metadata, as used for folder names.</summary>
    public override string ToString() =>
        Prerelease.Count == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}-{string.Join('.', Prerelease)}");

    public static bool operator ==(SemVersion? a, SemVersion? b) => a is null ? b is null : a.Equals(b);

    public static bool operator !=(SemVersion? a, SemVersion? b) => !(a == b);

    public static bool operator <(SemVersion? a, SemVersion? b) => a is null ? b is not null : a.CompareTo(b) < 0;

    public static bool operator >(SemVersion? a, SemVersion? b) => a is not null && a.CompareTo(b) > 0;

    public static bool operator <=(SemVersion? a, SemVersion? b) => !(a > b);

    public static bool operator >=(SemVersion? a, SemVersion? b) => !(a < b);

    private static int CompareIdentifier(string a, string b)
    {
        bool aNum = IsNumeric(a);
        bool bNum = IsNumeric(b);
        if (aNum && bNum)
        {
            // Compare by length first so long numeric identifiers never overflow.
            int len = a.Length.CompareTo(b.Length);
            return len != 0 ? len : string.CompareOrdinal(a, b);
        }

        if (aNum != bNum)
        {
            return aNum ? -1 : 1;
        }

        return Math.Sign(string.CompareOrdinal(a, b));
    }

    private static bool TryCore(string part, out int value)
    {
        value = 0;
        if (part.Length == 0 || !IsNumeric(part) || (part.Length > 1 && part[0] == '0'))
        {
            return false;
        }

        return int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool ValidIdentifiers(string text, bool numericNoLeadingZero)
    {
        if (text.Length == 0)
        {
            return false;
        }

        foreach (string id in text.Split('.'))
        {
            if (id.Length == 0)
            {
                return false;
            }

            foreach (char ch in id)
            {
                if (!(char.IsAsciiLetterOrDigit(ch) || ch == '-'))
                {
                    return false;
                }
            }

            if (numericNoLeadingZero && IsNumeric(id) && id.Length > 1 && id[0] == '0')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNumeric(string s)
    {
        foreach (char ch in s)
        {
            if (!char.IsAsciiDigit(ch))
            {
                return false;
            }
        }

        return s.Length > 0;
    }
}
