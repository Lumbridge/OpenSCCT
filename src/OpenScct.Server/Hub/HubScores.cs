using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OpenScct.Server.Hub;

/// <summary>One board row: who, the value (a time in ms or a score) and the detail the board shows after it.</summary>
public sealed record HubScore(string Name, int Value, string Detail, long When);

/// <summary>
/// One score table of the hub (an OpenSCCT addition): every player's best by name, and the top rows from those. Lower
/// is better for times (<see cref="LowerIsBetter"/>), higher for scores.
/// </summary>
public sealed class HubTable
{
    private readonly Dictionary<string, HubScore> _best = new(StringComparer.OrdinalIgnoreCase);

    public HubTable(bool lowerIsBetter) => LowerIsBetter = lowerIsBetter;

    public bool LowerIsBetter { get; }

    public IReadOnlyCollection<HubScore> Bests => _best.Values;

    public HubScore? Best(string name) => _best.GetValueOrDefault(name);

    /// <summary>Records <paramref name="score"/>: true when it is the player's new best.</summary>
    public bool Submit(HubScore score)
    {
        if (_best.TryGetValue(score.Name, out HubScore? old) && !Better(score.Value, old.Value))
        {
            return false;
        }

        _best[score.Name] = score;
        if (_best.Count > HubPlayK.MaxBests)
        {
            // Forget the weakest best (far down the board).
            HubScore? worst = null;
            foreach (HubScore s in _best.Values)
            {
                if (worst is null || Better(worst.Value, s.Value))
                {
                    worst = s;
                }
            }

            _best.Remove(worst!.Name);
        }

        return true;
    }

    /// <summary>The top <paramref name="count"/> bests, best first (earlier first on a tie).</summary>
    public List<HubScore> Top(int count = HubPlayK.BoardRows)
    {
        var all = new List<HubScore>(_best.Values);
        all.Sort(Compare);
        if (all.Count > count)
        {
            all.RemoveRange(count, all.Count - count);
        }

        return all;
    }

    /// <summary>The 1-based place of <paramref name="name"/>'s best on the whole table, or 0.</summary>
    public int Rank(string name)
    {
        if (!_best.TryGetValue(name, out HubScore? mine))
        {
            return 0;
        }

        int rank = 1;
        foreach (HubScore s in _best.Values)
        {
            if (!ReferenceEquals(s, mine) && Compare(s, mine) < 0)
            {
                rank++;
            }
        }

        return rank;
    }

    internal void Load(HubScore score) => _best[score.Name] = score;

    internal void Clear() => _best.Clear();

    private bool Better(int a, int b) => LowerIsBetter ? a < b : a > b;

    private int Compare(HubScore a, HubScore b)
    {
        int c = LowerIsBetter ? a.Value.CompareTo(b.Value) : b.Value.CompareTo(a.Value);
        return c != 0 ? c : a.When.CompareTo(b.When);
    }
}

/// <summary>
/// The hub's score tables (the course's times, the range's scores, the plaza ball's goals), kept in a small JSON file
/// next to the hub server so the boards survive a restart. With no path nothing is written (tests, in-game hubs).
/// </summary>
public sealed class HubScores
{
    public HubTable Course { get; } = new(lowerIsBetter: true);

    public HubTable Range { get; } = new(lowerIsBetter: false);

    public HubTable Goals { get; } = new(lowerIsBetter: false);

    public string? Path { get; private init; }

    /// <summary>The hub map layout version the course's times were set on (0: unknown, a file from before it was kept).</summary>
    public int CourseLayout { get; private set; }

    /// <summary>
    /// Clears the course's times when they were set on another layout (a rebuilt course: old times would be on a
    /// different route) and saves; true when it cleared them.
    /// </summary>
    public bool KeepCourseLayout(int layoutVersion)
    {
        if (layoutVersion <= 0 || CourseLayout == layoutVersion)
        {
            return false;
        }

        bool had = Course.Bests.Count > 0;
        Course.Clear();
        CourseLayout = layoutVersion;
        Save();
        return had;
    }

    /// <summary>The last save or load error, or null.</summary>
    public string? Error { get; private set; }

    public static HubScores Open(string? path)
    {
        var scores = new HubScores { Path = path };
        if (path is null || !File.Exists(path))
        {
            return scores;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            ReadTable(doc.RootElement, "course", scores.Course);
            if (doc.RootElement.TryGetProperty("courseLayout", out JsonElement cl) && cl.ValueKind == JsonValueKind.Number)
            {
                scores.CourseLayout = cl.GetInt32();
            }

            ReadTable(doc.RootElement, "range", scores.Range);
            ReadTable(doc.RootElement, "goals", scores.Goals);
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            scores.Error = e.Message;
        }

        return scores;
    }

    /// <summary>Writes the tables (to a temporary file, then over the old one).</summary>
    public void Save()
    {
        if (Path is null)
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteString("format", "openscct.hubscores/1");
                w.WriteNumber("courseLayout", CourseLayout);
                WriteTable(w, "course", Course);
                WriteTable(w, "range", Range);
                WriteTable(w, "goals", Goals);
                w.WriteEndObject();
            }

            string temp = Path + ".tmp";
            File.WriteAllBytes(temp, stream.ToArray());
            File.Move(temp, Path, overwrite: true);
            Error = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
        }
    }

    private static void ReadTable(JsonElement root, string name, HubTable table)
    {
        if (!root.TryGetProperty(name, out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement r in rows.EnumerateArray())
        {
            string who = r.GetProperty("name").GetString() ?? string.Empty;
            if (who.Length == 0)
            {
                continue;
            }

            table.Load(new HubScore(who, r.GetProperty("value").GetInt32(),
                r.TryGetProperty("detail", out JsonElement d) ? d.GetString() ?? string.Empty : string.Empty,
                r.TryGetProperty("when", out JsonElement t) ? t.GetInt64() : 0L));
        }
    }

    private static void WriteTable(Utf8JsonWriter w, string name, HubTable table)
    {
        w.WriteStartArray(name);
        foreach (HubScore s in table.Bests)
        {
            w.WriteStartObject();
            w.WriteString("name", s.Name);
            w.WriteNumber("value", s.Value);
            w.WriteString("detail", s.Detail);
            w.WriteNumber("when", s.When);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    /// <summary>A time as the boards show it: m:ss.cc.</summary>
    public static string Time(int ms)
    {
        ms = Math.Max(0, ms);
        int minutes = ms / 60000;
        int seconds = ms / 1000 % 60;
        int hundredths = ms / 10 % 100;
        return string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}.{hundredths:00}");
    }

    /// <summary>A board's text: its title, then up to ten rows "n. name value detail" (or a line saying it is empty).</summary>
    public static string BoardText(string title, HubTable table, Func<HubScore, string> value)
    {
        var sb = new StringBuilder(title);
        List<HubScore> top = table.Top();
        if (top.Count == 0)
        {
            sb.Append("\nNo scores yet. Be the first.");
        }

        for (int i = 0; i < top.Count; i++)
        {
            HubScore s = top[i];
            sb.Append('\n').Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(". ").Append(s.Name).Append("  ").Append(value(s));
            if (s.Detail.Length > 0)
            {
                sb.Append("  ").Append(s.Detail);
            }
        }

        return sb.ToString();
    }
}
