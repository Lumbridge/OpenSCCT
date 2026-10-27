using System;
using System.Collections.Generic;
using OpenScct.Core.Engine;
using OpenScct.Net.Discovery;

namespace OpenScct.Net.Cosmetics;

/// <summary>
/// Levels and prestige (spec/additions/progression.md, an OpenSCCT addition): the cap, the XP curve and what a counted
/// round is worth. Shared: the master awards with it, the game draws bars and badges with it.
/// </summary>
public static class ProgressionK
{
    public const int MaxLevel = 55;

    public const int MaxPrestige = 10;

    /// <summary>XP from level 1 to 2; each later level costs <see cref="LevelStepXp"/> more.</summary>
    public const int FirstLevelXp = 200;

    public const int LevelStepXp = 50;

    /// <summary>XP per point of round score (a kill is 2 points in the retail table).</summary>
    public const int ScoreXp = 10;

    /// <summary>Round score counted at most (higher scores are clamped, negative ones count as 0).</summary>
    public const int MaxRoundScore = 100;

    public const int FinishXp = 100;

    public const int WinXp = 150;

    /// <summary>XP per objective a spy takes (objectives score 0 points in the retail table).</summary>
    public const int ObjectiveXp = 50;

    public const int MaxObjectivesCounted = 10;

    /// <summary>XP for an achievement: a total to reach, a one-round feat, and a "finish them all" one.</summary>
    public const int AchievementXp = 250;

    public const int RoundAchievementXp = 500;

    public const int MetaAchievementXp = 1000;
}

/// <summary>
/// A player's rank: prestige (0 none, 1 to 10), level (1 to 55), the XP into this level and what the next one costs
/// (0 at the cap). Packs into one int with the shown title for the PRI and the lobby (level 8 bits, prestige 8 bits,
/// title bit + 1 in the top 16 bits; 0 means "no rank", as for guests).
/// </summary>
public readonly record struct PlayerRank(int Prestige, int Level, int XpIntoLevel, int XpForNext, int TitleBit = -1)
{
    public static PlayerRank None => default;

    public bool IsNone => Level <= 0;

    public bool AtCap => Level >= ProgressionK.MaxLevel;

    /// <summary>Level 55 at the last prestige: the master prestige emblem.</summary>
    public bool IsMaster => Prestige >= ProgressionK.MaxPrestige && AtCap;

    public bool CanPrestige => AtCap && Prestige < ProgressionK.MaxPrestige;

    /// <summary>Progress through this level, 0 to 1 (1 at the cap).</summary>
    public float Fraction => XpForNext <= 0 ? 1f : Math.Clamp((float)XpIntoLevel / XpForNext, 0f, 1f);

    public TitleItem? Title => TitleBit >= 0 ? TitleCatalog.ByBit(TitleBit) : null;

    /// <summary>"P3 42", or "42" before the first prestige (the kill feed's tag).</summary>
    public string Tag => IsNone ? string.Empty : Prestige > 0 ? $"P{Prestige} {Level}" : Level.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>XP it costs to go from <paramref name="level"/> to the next one.</summary>
    public static int CostOf(int level) => ProgressionK.FirstLevelXp + ProgressionK.LevelStepXp * (Math.Max(level, 1) - 1);

    /// <summary>XP needed from level 1 to reach <paramref name="level"/>.</summary>
    public static int XpToReach(int level)
    {
        int total = 0;
        for (int l = 1; l < Math.Min(level, ProgressionK.MaxLevel); l++)
        {
            total += CostOf(l);
        }

        return total;
    }

    /// <summary>The XP of a full prestige (level 1 to the cap); an account's XP is never kept above it.</summary>
    public static int CapXp => XpToReach(ProgressionK.MaxLevel);

    public static PlayerRank FromXp(int prestige, int xp, int titleBit = -1)
    {
        xp = Math.Clamp(xp, 0, CapXp);
        int level = 1;
        while (level < ProgressionK.MaxLevel && xp >= CostOf(level))
        {
            xp -= CostOf(level);
            level++;
        }

        int next = level >= ProgressionK.MaxLevel ? 0 : CostOf(level);
        return new PlayerRank(Math.Clamp(prestige, 0, ProgressionK.MaxPrestige), level, level >= ProgressionK.MaxLevel ? 0 : xp, next, titleBit);
    }

    public PlayerRank WithTitle(int titleBit) => this with { TitleBit = titleBit };

    public int Pack() => IsNone ? 0 :
        Math.Clamp(Level, 1, ProgressionK.MaxLevel) | (Math.Clamp(Prestige, 0, ProgressionK.MaxPrestige) << 8) | ((TitleBit + 1) << 16);

    /// <summary>A packed rank (no XP: XpIntoLevel and XpForNext are 0).</summary>
    public static PlayerRank Unpack(int packed)
    {
        int level = packed & 0xFF;
        if (level <= 0 || level > ProgressionK.MaxLevel)
        {
            return None;
        }

        int prestige = Math.Min((packed >> 8) & 0xFF, ProgressionK.MaxPrestige);
        int title = ((packed >> 16) & 0xFFFF) - 1;
        return new PlayerRank(prestige, level, 0, 0, title);
    }
}

/// <summary>What a counted round gave one player.</summary>
public sealed record RoundXp(int Score, int Finish, int Win, int Objectives)
{
    public int Total => Score + Finish + Win + Objectives;
}

public static class Progression
{
    /// <summary>The XP of one counted round for one player row (achievement XP is added by the master).</summary>
    public static RoundXp ForRound(ResultsReportRow row, ResultsReport report)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(report);
        int score = Math.Clamp(row.Score, 0, ProgressionK.MaxRoundScore) * ProgressionK.ScoreXp;
        int win = row.Team == report.WinningTeam ? ProgressionK.WinXp : 0;
        int objectives = row.Team == 0 && row.Feats.TryGetValue(FeatKeys.SpyObjectives, out int taken)
            ? Math.Clamp(taken, 0, ProgressionK.MaxObjectivesCounted) * ProgressionK.ObjectiveXp
            : 0;
        return new RoundXp(score, ProgressionK.FinishXp, win, objectives);
    }

    /// <summary>The XP an achievement is worth.</summary>
    public static int XpOf(Achievement achievement)
    {
        ArgumentNullException.ThrowIfNull(achievement);
        if (achievement.Id is "gilded" or "spy_gilded")
        {
            return ProgressionK.MetaAchievementXp;
        }

        return achievement.Round is not null && achievement.Category != AchievementCategory.Map
            ? ProgressionK.RoundAchievementXp
            : ProgressionK.AchievementXp;
    }

    /// <summary>The prestige emblem names, by prestige (index 0 unused; the master emblem is last).</summary>
    public static IReadOnlyList<string> PrestigeNames { get; } =
    [
        string.Empty, "Prestige 1", "Prestige 2", "Prestige 3", "Prestige 4", "Prestige 5", "Prestige 6", "Prestige 7",
        "Prestige 8", "Prestige 9", "Prestige 10", "Master Prestige",
    ];
}
