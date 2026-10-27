using System;
using System.Collections.Generic;

namespace OpenScct.Content;

/// <summary>
/// Compares strings ignoring ASCII case only, the way SVMap matches ids, tags and events (UE2 FName rules).
/// Hash codes are stable across processes.
/// </summary>
public sealed class AsciiCaseComparer : IEqualityComparer<string>, IComparer<string>
{
    public static readonly AsciiCaseComparer Instance = new();

    private AsciiCaseComparer()
    {
    }

    public static bool AreEqual(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null || x.Length != y.Length)
        {
            return false;
        }

        for (int i = 0; i < x.Length; i++)
        {
            if (Lower(x[i]) != Lower(y[i]))
            {
                return false;
            }
        }

        return true;
    }

    public bool Equals(string? x, string? y) => AreEqual(x, y);

    public int GetHashCode(string obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        unchecked
        {
            uint h = 2166136261u;
            foreach (char ch in obj)
            {
                char c = Lower(ch);
                h ^= c;
                h *= 16777619u;
            }

            return (int)h;
        }
    }

    /// <summary>Ordinal order after lowering ASCII letters.</summary>
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int n = Math.Min(x.Length, y.Length);
        for (int i = 0; i < n; i++)
        {
            int d = Lower(x[i]) - Lower(y[i]);
            if (d != 0)
            {
                return d;
            }
        }

        return x.Length - y.Length;
    }

    private static char Lower(char c) => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
}
