using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenScct.Net.Cosmetics;

/// <summary>How a title is unlocked.</summary>
public enum TitleSource
{
    /// <summary>Reaching a prestige (Value: the prestige; 11 is the master prestige).</summary>
    Prestige,

    /// <summary>Reaching a level in any prestige (Value: the level).</summary>
    Level,

    /// <summary>Earning an achievement (Achievement: its id).</summary>
    Achievement,
}

/// <summary>
/// A title shown under a player's name: its id (kept in settings, never renamed), its bit (append only, like the
/// cosmetic bits), its text and how it is unlocked.
/// </summary>
public sealed record TitleItem(string Id, int Bit, string Text, TitleSource Source, int Value = 0, string? Achievement = null)
{
    /// <summary>How the game describes the way to unlock it.</summary>
    public string HowToUnlock => Source switch
    {
        TitleSource.Prestige when Value > ProgressionK.MaxPrestige => $"Reach level {ProgressionK.MaxLevel} at prestige {ProgressionK.MaxPrestige}.",
        TitleSource.Prestige => $"Reach prestige {Value}.",
        TitleSource.Level => $"Reach level {Value}.",
        _ => $"Earn the achievement {Achievements.All.FirstOrDefault(a => a.Id == Achievement)?.Title ?? Achievement}.",
    };
}

/// <summary>Every title. Bits only ever get appended.</summary>
public static class TitleCatalog
{
    public static IReadOnlyList<TitleItem> All { get; } = Build();

    private static readonly Dictionary<string, TitleItem> s_byId = All.ToDictionary(t => t.Id, StringComparer.Ordinal);

    public static TitleItem? Find(string? id) => id is not null && s_byId.TryGetValue(id, out TitleItem? t) ? t : null;

    public static TitleItem? ByBit(int bit) => bit >= 0 && bit < All.Count ? All[bit] : null;

    /// <summary>The titles an account has: by its prestige, the best level it ever reached, and its achievements.</summary>
    public static IReadOnlyList<TitleItem> Unlocked(int prestige, int level, int bestLevel, IEnumerable<string> earned)
    {
        var ids = new HashSet<string>(earned, StringComparer.Ordinal);
        bool master = prestige >= ProgressionK.MaxPrestige && level >= ProgressionK.MaxLevel;
        return All.Where(t => t.Source switch
        {
            TitleSource.Prestige => t.Value > ProgressionK.MaxPrestige ? master : prestige >= t.Value,
            TitleSource.Level => Math.Max(bestLevel, level) >= t.Value || prestige > 0 && t.Value <= ProgressionK.MaxLevel,
            _ => t.Achievement is not null && ids.Contains(t.Achievement),
        }).ToList();
    }

    /// <summary>A bit set of titles (what the ticket carries), trimmed after the last set byte.</summary>
    public static byte[] ToBytes(IEnumerable<TitleItem> titles)
    {
        byte[] bits = new byte[(All.Count + 7) / 8];
        foreach (TitleItem t in titles)
        {
            bits[t.Bit / 8] |= (byte)(1 << (t.Bit % 8));
        }

        int length = bits.Length;
        while (length > 0 && bits[length - 1] == 0)
        {
            length--;
        }

        return bits.AsSpan(0, length).ToArray();
    }

    public static bool Has(byte[]? bits, TitleItem title) =>
        bits is not null && title.Bit / 8 < bits.Length && (bits[title.Bit / 8] & (1 << (title.Bit % 8))) != 0;

    private static List<TitleItem> Build()
    {
        var list = new List<TitleItem>();
        void Add(string id, string text, TitleSource source, int value = 0, string? achievement = null) =>
            list.Add(new TitleItem(id, list.Count, text, source, value, achievement));

        // Prestige titles, one per prestige, then the master prestige.
        string[] prestige =
        [
            "Operative", "Specialist", "Field Agent", "Handler", "Shadow", "Spectre", "Wraith", "Third Echelon",
            "Fifth Freedom", "Legend",
        ];
        for (int p = 0; p < prestige.Length; p++)
        {
            Add($"prestige_{p + 1}", prestige[p], TitleSource.Prestige, p + 1);
        }

        Add("prestige_master", "Splinter Cell", TitleSource.Prestige, ProgressionK.MaxPrestige + 1);

        // Level milestones (any prestige; a prestiged account has them all).
        Add("level_10", "Recruit", TitleSource.Level, 10);
        Add("level_25", "Regular", TitleSource.Level, 25);
        Add("level_40", "Veteran", TitleSource.Level, 40);
        Add("level_55", "Elite", TitleSource.Level, 55);

        // Achievement titles.
        Add("ach_gilded", "Golden Gun", TitleSource.Achievement, achievement: "gilded");
        Add("ach_spy_gilded", "Gilded Ghost", TitleSource.Achievement, achievement: "spy_gilded");
        Add("ach_untouchable", "Untouchable", TitleSource.Achievement, achievement: "spy_untouchable");
        Add("ach_wall", "The Wall", TitleSource.Achievement, achievement: "merc_wall");
        Add("ach_reaper", "Reaper", TitleSource.Achievement, achievement: "kills_750");
        Add("ach_lifer", "Lifer", TitleSource.Achievement, achievement: "matches_300");
        Add("ach_phantom", "Phantom", TitleSource.Achievement, achievement: "spy_wins_100");
        Add("ach_warden", "Warden", TitleSource.Achievement, achievement: "merc_wins_100");
        Add("ach_deadeye", "Deadeye", TitleSource.Achievement, achievement: "merc_sniper_75");
        Add("ach_ghost_machine", "Ghost in the Machine", TitleSource.Achievement, achievement: "spy_objectives_100");
        Add("ach_gadget_master", "Mastercraft", TitleSource.Achievement, achievement: "gadget_master");
        Add("ach_petard", "Petard", TitleSource.Achievement, achievement: "spy_mine_25");
        Add("ach_high_voltage", "High Voltage", TitleSource.Achievement, achievement: "merc_taser_100");

        foreach (TitleItem t in list.Where(t => t.Source == TitleSource.Achievement))
        {
            if (!Achievements.All.Any(a => a.Id == t.Achievement))
            {
                throw new InvalidOperationException($"Title {t.Id} names an unknown achievement {t.Achievement}.");
            }
        }

        return list;
    }
}
