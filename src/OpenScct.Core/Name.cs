using System;
using System.Collections.Concurrent;
using System.Threading;

namespace OpenScct.Core;

/// <summary>
/// UE2 FName: a case-insensitive interned string compared by table index.
/// The first spelling interned is the one <see cref="ToString"/> returns.
/// Index 0 is <c>None</c>, so <c>default(Name)</c> is <see cref="None"/>; null and "" also map to None.
/// Indexes are process-local (they depend on intern order), so serialize names as text, never as indexes.
/// </summary>
public readonly struct Name : IEquatable<Name>
{
    private readonly int _index;

    private Name(int index)
    {
        _index = index;
    }

    /// <summary>Interns <paramref name="text"/> (or finds it) and returns its name.</summary>
    public Name(string? text)
    {
        _index = NameTable.Intern(text);
    }

    /// <summary>The None name (index 0).</summary>
    public static Name None => default;

    /// <summary>Table index. Stable only within one process.</summary>
    public int Index => _index;

    public bool IsNone => _index == 0;

    /// <summary>Returns the name for <paramref name="text"/> without interning it, or None if it was never interned.</summary>
    public static Name Find(string? text) => new(NameTable.Find(text));

    /// <summary>Number of names in the table, including None.</summary>
    public static int TableCount => NameTable.Count;

    public static implicit operator Name(string? text) => new(text);

    public static bool operator ==(Name a, Name b) => a._index == b._index;

    public static bool operator !=(Name a, Name b) => a._index != b._index;

    public bool Equals(Name other) => _index == other._index;

    public override bool Equals(object? obj) => obj is Name other && other._index == _index;

    public override int GetHashCode() => _index;

    public override string ToString() => NameTable.Get(_index);

    /// <summary>Process-wide append-only name table. Reads are lock-free; interning a new name takes a lock.</summary>
    private static class NameTable
    {
        private static readonly object s_lock = new();
        private static readonly ConcurrentDictionary<string, int> s_map = new(StringComparer.OrdinalIgnoreCase);
        private static string[] s_entries = CreateInitial();
        private static int s_count = 1;

        private static string[] CreateInitial()
        {
            var entries = new string[4096];
            entries[0] = "None";
            s_map["None"] = 0;
            return entries;
        }

        public static int Count => Volatile.Read(ref s_count);

        public static int Find(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            return s_map.TryGetValue(text, out int index) ? index : 0;
        }

        public static int Intern(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            if (s_map.TryGetValue(text, out int index))
            {
                return index;
            }

            lock (s_lock)
            {
                if (s_map.TryGetValue(text, out index))
                {
                    return index;
                }

                index = s_count;
                string[] entries = s_entries;
                if (index == entries.Length)
                {
                    var grown = new string[entries.Length * 2];
                    Array.Copy(entries, grown, entries.Length);
                    grown[index] = text;
                    Volatile.Write(ref s_entries, grown);
                }
                else
                {
                    entries[index] = text;
                }

                Volatile.Write(ref s_count, index + 1);

                // Publishing the index last means any reader that sees it also sees the entry.
                s_map[text] = index;
                return index;
            }
        }

        public static string Get(int index)
        {
            string[] entries = Volatile.Read(ref s_entries);
            if ((uint)index >= (uint)Volatile.Read(ref s_count))
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Name index is not in the table.");
            }

            return entries[index];
        }
    }
}
