using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

/// <summary>An account's level row: prestige, XP into it, the best level ever reached, lifetime XP, the last round's award.</summary>
internal sealed record ProgressRow(int Prestige, int Xp, int BestLevel, long TotalXp, XpAward? LastAward)
{
    public static ProgressRow Fresh => new(0, 0, 1, 0, null);

    public PlayerRank Rank => PlayerRank.FromXp(Prestige, Xp);
}

/// <summary>How a prestige request went.</summary>
internal enum PrestigeResult
{
    Done,
    NotAtCap,
    AtLastPrestige,
}

// Levels and prestige (schema v5, spec/additions/progression.md).
internal sealed partial class AccountStore
{
    private void MigrateVersion5()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS progression (
                account_id INTEGER PRIMARY KEY REFERENCES accounts(id),
                prestige INTEGER NOT NULL DEFAULT 0,
                xp INTEGER NOT NULL DEFAULT 0,
                best_level INTEGER NOT NULL DEFAULT 1,
                total_xp INTEGER NOT NULL DEFAULT 0,
                last_award TEXT,
                updated INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS progression_rank ON progression(prestige, xp);
            """);
    }

    public ProgressRow ProgressionOf(long accountId)
    {
        lock (_gate)
        {
            return ProgressionLocked(accountId);
        }
    }

    private ProgressRow ProgressionLocked(long accountId) =>
        One("SELECT prestige, xp, best_level, total_xp, last_award FROM progression WHERE account_id = $a;",
            r => new ProgressRow(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt64(3),
                r.IsDBNull(4) ? null : ReadAward(r.GetString(4))), ("$a", accountId)) ?? ProgressRow.Fresh;

    private static XpAward? ReadAward(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<XpAward>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The titles the account has unlocked.</summary>
    public IReadOnlyList<TitleItem> Titles(long accountId)
    {
        lock (_gate)
        {
            ProgressRow p = ProgressionLocked(accountId);
            PlayerRank rank = p.Rank;
            return TitleCatalog.Unlocked(rank.Prestige, rank.Level, p.BestLevel, Earned(accountId).Keys);
        }
    }

    /// <summary>What the game reads: rank, XP, titles and the last award.</summary>
    public ProgressInfo ProgressInfo(long accountId)
    {
        lock (_gate)
        {
            ProgressRow p = ProgressionLocked(accountId);
            PlayerRank rank = p.Rank;
            return new ProgressInfo
            {
                Prestige = rank.Prestige, Level = rank.Level, Xp = p.Xp, TotalXp = p.TotalXp, BestLevel = Math.Max(p.BestLevel, rank.Level),
                Titles = Titles(accountId).Select(t => t.Id).ToList(), LastAward = p.LastAward,
            };
        }
    }

    /// <summary>
    /// Adds a counted round's XP (and that of the achievements it earned) to the account; inside ApplyResults' transaction.
    /// </summary>
    private void AwardLocked(long accountId, ResultsReportRow row, ResultsReport report, IReadOnlyList<Achievement> newlyEarned,
        ISet<string> earnedNow, long now)
    {
        ProgressRow before = ProgressionLocked(accountId);
        PlayerRank rankBefore = before.Rank;
        var titlesBefore = TitleCatalog.Unlocked(rankBefore.Prestige, rankBefore.Level, before.BestLevel,
            earnedNow.Except(newlyEarned.Select(a => a.Id))).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

        RoundXp round = Progression.ForRound(row, report);
        int achievementXp = newlyEarned.Sum(Progression.XpOf);
        int total = round.Total + achievementXp;
        int xp = Math.Min(before.Xp + total, PlayerRank.CapXp);
        PlayerRank rankAfter = PlayerRank.FromXp(before.Prestige, xp);
        int best = Math.Max(before.BestLevel, rankAfter.Level);
        List<string> newTitles = TitleCatalog.Unlocked(rankAfter.Prestige, rankAfter.Level, best, earnedNow)
            .Select(t => t.Id).Where(id => !titlesBefore.Contains(id)).ToList();

        var award = new XpAward
        {
            When = DateTimeOffset.FromUnixTimeSeconds(now), Map = report.Map, Score = round.Score, Finish = round.Finish, Win = round.Win,
            Objectives = round.Objectives, Achievements = achievementXp, Total = total, LevelBefore = rankBefore.Level,
            LevelAfter = rankAfter.Level, Prestige = rankAfter.Prestige, NewTitles = newTitles,
        };
        Exec("INSERT INTO progression (account_id, prestige, xp, best_level, total_xp, last_award, updated) " +
            "VALUES ($a, $p, $x, $b, $t, $l, $now) ON CONFLICT(account_id) DO UPDATE SET " +
            "xp = $x, best_level = $b, total_xp = total_xp + $add, last_award = $l, updated = $now;",
            ("$a", accountId), ("$p", before.Prestige), ("$x", xp), ("$b", best), ("$t", (long)total), ("$add", (long)total),
            ("$l", JsonSerializer.Serialize(award)), ("$now", now));
    }

    /// <summary>Moves a level 55 account to the next prestige, back to level 1.</summary>
    public PrestigeResult Prestige(long accountId)
    {
        lock (_gate)
        {
            ProgressRow p = ProgressionLocked(accountId);
            PlayerRank rank = p.Rank;
            if (!rank.AtCap)
            {
                return PrestigeResult.NotAtCap;
            }

            if (rank.Prestige >= ProgressionK.MaxPrestige)
            {
                return PrestigeResult.AtLastPrestige;
            }

            long now = Now;
            return InTransaction(() =>
            {
                Exec("UPDATE progression SET prestige = prestige + 1, xp = 0, last_award = NULL, updated = $now WHERE account_id = $a;",
                    ("$a", accountId), ("$now", now));
                AuditLocked("prestige", $"account {accountId}: prestige {rank.Prestige} -> {rank.Prestige + 1}");
                return PrestigeResult.Done;
            });
        }
    }

    /// <summary>Sets an account's prestige and level (admins); the XP is the start of that level.</summary>
    public void SetRank(long accountId, int prestige, int level)
    {
        prestige = Math.Clamp(prestige, 0, ProgressionK.MaxPrestige);
        level = Math.Clamp(level, 1, ProgressionK.MaxLevel);
        int xp = PlayerRank.XpToReach(level);
        lock (_gate)
        {
            long now = Now;
            InTransaction(() =>
            {
                ProgressRow before = ProgressionLocked(accountId);
                Exec("INSERT INTO progression (account_id, prestige, xp, best_level, total_xp, updated) VALUES ($a, $p, $x, $b, 0, $now) " +
                    "ON CONFLICT(account_id) DO UPDATE SET prestige = $p, xp = $x, best_level = MAX(best_level, $b), updated = $now;",
                    ("$a", accountId), ("$p", prestige), ("$x", xp), ("$b", level), ("$now", now));
                AuditLocked("rank", $"account {accountId}: prestige {before.Prestige} level {before.Rank.Level} -> prestige {prestige} level {level}");
                return 0;
            });
        }
    }

    /// <summary>The ranks of many accounts at once (the portal's player list).</summary>
    public Dictionary<long, PlayerRank> Ranks(IEnumerable<long> accountIds)
    {
        lock (_gate)
        {
            var ranks = new Dictionary<long, PlayerRank>();
            foreach (long id in accountIds.Distinct())
            {
                ranks[id] = ProgressionLocked(id).Rank;
            }

            return ranks;
        }
    }
}
