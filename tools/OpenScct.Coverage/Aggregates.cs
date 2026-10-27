using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenScct.Coverage;

/// <summary>Unit and byte totals of a group of units, per status. "At least" counts are cumulative: a verified
/// unit also counts as ported and specced.</summary>
internal sealed class Bucket
{
    private readonly int[] _unitsAtLeast = new int[4];
    private readonly long[] _bytesAtLeast = new long[4];

    public Bucket(string key)
    {
        Key = key;
    }

    public string Key { get; }

    public int Units { get; private set; }

    public long Bytes { get; private set; }

    public int NaUnits { get; private set; }

    public long NaBytes { get; private set; }

    public int InScopeUnits => Units - NaUnits;

    public long InScopeBytes => Bytes - NaBytes;

    public void Add(UnitState s)
    {
        long w = s.Unit?.Weight ?? 0;
        Units++;
        Bytes += w;
        if (s.Status == UnitStatus.NotApplicable)
        {
            NaUnits++;
            NaBytes += w;
            return;
        }

        for (int i = 0; i <= (int)s.Status; i++)
        {
            _unitsAtLeast[i]++;
            _bytesAtLeast[i] += w;
        }
    }

    public int UnitsAtLeast(UnitStatus s) => _unitsAtLeast[(int)s];

    public long BytesAtLeast(UnitStatus s) => _bytesAtLeast[(int)s];

    /// <summary>Share of in-scope bytes with at least this status, or null when the group has no in-scope bytes.</summary>
    public double? Weighted(UnitStatus s) => InScopeBytes > 0 ? (double)BytesAtLeast(s) / InScopeBytes : null;

    /// <summary>Share of in-scope units with at least this status, or null when the group has none in scope.</summary>
    public double? ByUnits(UnitStatus s) => InScopeUnits > 0 ? (double)UnitsAtLeast(s) / InScopeUnits : null;

    public static string Pct(double? v) => v is { } x ? (x * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "-";

    public static string PctPlain(double? v) => v is { } x ? (x * 100).ToString("0.00", CultureInfo.InvariantCulture) : "";

    /// <summary>Groups units by a key, sorted by key.</summary>
    public static List<Bucket> GroupBy(IEnumerable<UnitState> units, Func<UnitState, string> key)
    {
        var map = new SortedDictionary<string, Bucket>(StringComparer.Ordinal);
        foreach (UnitState s in units)
        {
            string k = key(s);
            if (!map.TryGetValue(k, out Bucket? b))
            {
                b = new Bucket(k);
                map[k] = b;
            }

            b.Add(s);
        }

        return map.Values.ToList();
    }

    public static Bucket Total(string key, IEnumerable<UnitState> units)
    {
        var b = new Bucket(key);
        foreach (UnitState s in units)
        {
            b.Add(s);
        }

        return b;
    }
}
