using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core.Engine;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

/// <summary>Achievement groups, in the order the game shows them (the names go to the game as text).</summary>
internal enum AchievementCategory
{
    General,
    Spy,
    Merc,
    Weapons,
    Map,
}

/// <summary>
/// An account's totals as the achievements read them: the stats, the feats summed over all counted rounds, the base
/// maps won per side, and the achievements already earned (for the ones that need others).
/// </summary>
internal sealed class AccountProgress
{
    public AccountStats Stats { get; init; } = new();

    public IReadOnlyDictionary<string, int> Feats { get; init; } = new Dictionary<string, int>();

    /// <summary>Wins per base map code ("Stee") and team (0 spies, 1 mercs).</summary>
    public IReadOnlyDictionary<(string Map, int Team), int> MapWins { get; init; } = new Dictionary<(string, int), int>();

    public ISet<string> Earned { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public int Feat(string key) => Feats.TryGetValue(key, out int v) ? v : 0;

    public int MapWin(string map, int team) => MapWins.TryGetValue((map, team), out int v) ? v : 0;
}

/// <summary>
/// One achievement: its id (kept in the database, never renamed), what the game shows, its group, its progress
/// (current, target), the cosmetics it unlocks, and for one-round feats the test of that round (otherwise it is earned
/// once the progress reaches the target).
/// </summary>
internal sealed record Achievement(
    string Id,
    string Title,
    string Description,
    AchievementCategory Category,
    Func<AccountProgress, (int Current, int Target)> Goal,
    IReadOnlyList<string> Rewards,
    Func<AccountProgress, ResultsReportRow, ResultsReport, bool>? Round = null)
{
    public bool Earned(AccountProgress p, ResultsReportRow row, ResultsReport report)
    {
        if (Round is not null)
        {
            return Round(p, row, report);
        }

        (int current, int target) = Goal(p);
        return current >= target;
    }
}

/// <summary>Every achievement, in the order the game lists them. Thresholds are in the tables at the top.</summary>
internal static class Achievements
{
    // The merc gun camo ladder, CoD4/MW2 style: (camo, kills or headshots, count, title).
    private static readonly (string Camo, bool Headshots, int Count, string Title)[] s_gunLadder =
    [
        ("woodland", false, 25, "Woodland"),
        ("desert", false, 75, "Desert"),
        ("digital", false, 150, "Digital"),
        ("red_tiger", true, 50, "Red Tiger"),
        ("blue_tiger", true, 150, "Blue Tiger"),
    ];

    private static readonly (string Key, string Name)[] s_guns = [("ar", "Assault rifle"), ("smg", "SMG"), ("shotgun", "Shotgun")];

    public static IReadOnlyList<Achievement> All { get; } = Build();

    /// <summary>The cosmetics an account has from its earned achievements.</summary>
    public static IReadOnlyList<string> Unlocked(IEnumerable<string> earned)
    {
        var ids = new HashSet<string>(earned, StringComparer.Ordinal);
        return All.Where(a => ids.Contains(a.Id)).SelectMany(a => a.Rewards).Distinct(StringComparer.Ordinal).ToList();
    }

    private static Achievement Count(string id, string title, string description, AchievementCategory category,
        Func<AccountProgress, int> value, int target, params string[] rewards) =>
        new(id, title, description, category, p => (Math.Min(value(p), target), target), rewards);

    private static Achievement Once(string id, string title, string description, AchievementCategory category,
        Func<AccountProgress, ResultsReportRow, ResultsReport, bool> round, params string[] rewards) =>
        new(id, title, description, category, p => (p.Earned.Contains(id) ? 1 : 0, 1), rewards, round);

    private static int Wins(AccountProgress p) => p.Stats.SpyWins + p.Stats.MercWins;

    private static bool Won(ResultsReportRow row, ResultsReport r) => row.Team == r.WinningTeam;

    private static int RowFeat(ResultsReportRow row, string key) => row.Feats.TryGetValue(key, out int v) ? v : 0;

    private static string[] Both(string item) => ["spy." + item, "merc." + item];

    private static List<Achievement> Build()
    {
        const AchievementCategory G = AchievementCategory.General;
        const AchievementCategory S = AchievementCategory.Spy;
        const AchievementCategory M = AchievementCategory.Merc;
        const AchievementCategory W = AchievementCategory.Weapons;
        var list = new List<Achievement>
        {
            // ---- General: tiers of matches, wins and kills; each gives a colour tint for both sides.
            Count("first_match", "First Mission", "Finish a match on an official server.", G, p => p.Stats.Matches, 1, Both("body.urban")),
            Count("matches_10", "Regular", "Finish 10 matches.", G, p => p.Stats.Matches, 10, Both("body.sand")),
            Count("matches_50", "Dedicated", "Finish 50 matches.", G, p => p.Stats.Matches, 50, Both("body.olive")),
            Count("matches_200", "Lifer", "Finish 200 matches.", G, p => p.Stats.Matches, 200, Both("body.midnight")),
            Count("wins_10", "Veteran", "Win 10 matches.", G, Wins, 10, Both("body.arctic")),
            Count("wins_50", "Elite", "Win 50 matches.", G, Wins, 50, Both("body.crimson")),
            Count("kills_25", "Bloodied", "Take down 25 opponents.", G, p => p.Stats.Kills, 25, Both("body.navy")),
            Count("kills_100", "Centurion", "Take down 100 opponents.", G, p => p.Stats.Kills, 100, Both("body.rust")),
            Count("kills_500", "Reaper", "Take down 500 opponents.", G, p => p.Stats.Kills, 500, Both("body.ash")),

            // ---- Spy
            Count("spy_win", "Ghost Protocol", "Win a match as a spy.", S, p => p.Stats.SpyWins, 1, "spy.body.desert"),
            Count("spy_wins_5", "Infiltrator", "Win 5 matches as a spy.", S, p => p.Stats.SpyWins, 5, "spy.body.woodland", "spy.light.blue"),
            Count("spy_wins_25", "Shadow Operative", "Win 25 matches as a spy.", S, p => p.Stats.SpyWins, 25, "spy.body.digital"),
            Count("spy_wins_100", "Phantom", "Win 100 matches as a spy.", S, p => p.Stats.SpyWins, 100, "spy.body.red_tiger", "spy.light.magenta"),
            Once("objectives", "Clean Sweep", "Win as a spy by taking every objective.", S,
                (_, row, r) => row.Team == 0 && r.WinningTeam == 0 && r.ObjectivesNeeded > 0 && r.ObjectivesTaken >= r.ObjectivesNeeded,
                "spy.pistol.woodland"),
            Count("spy_objectives_10", "Data Thief", "Take 10 objectives.", S, p => p.Feat(FeatKeys.SpyObjectives), 10,
                "spy.pistol.digital", "spy.light.cyan"),
            Count("spy_objectives_50", "Ghost in the Machine", "Take 50 objectives.", S, p => p.Feat(FeatKeys.SpyObjectives), 50,
                "spy.body.static", "spy.pistol.static"),
            Count("spy_necks_10", "Snap", "Break 10 necks.", S, p => p.Feat(FeatKeys.SpyNeckBreaks), 10, "spy.pistol.desert", "spy.light.red"),
            Count("spy_necks_50", "Breaker", "Break 50 necks.", S, p => p.Feat(FeatKeys.SpyNeckBreaks), 50, "spy.body.breaker"),
            Count("spy_drop_kill", "Death from Above", "Kill a merc with a drop attack.", S, p => p.Feat(FeatKeys.SpyDropKills), 1,
                "spy.pistol.red_tiger"),
            Once("spy_grab_3", "Triple Threat", "Kill 3 mercs with grabs in one round.", S,
                (_, row, _) => row.Team == 0 && RowFeat(row, FeatKeys.SpyGrabKills) >= 3, "spy.body.blue_tiger", "spy.pistol.blue_tiger",
                "spy.light.amber"),
            Count("spy_sticky_25", "Shockwave", "Knock out 25 mercs with the sticky shocker.", S, p => p.Feat(FeatKeys.SpyStickyKos), 25,
                "spy.pistol.shockwave"),
            Once("spy_untouchable", "Untouchable", "Win as a spy without dying.", S,
                (_, row, r) => row.Team == 0 && Won(row, r) && row.Deaths == 0, "spy.body.night_ops", "spy.light.violet"),

            // ---- Merc
            Count("merc_win", "Lockdown", "Win a match as a mercenary.", M, p => p.Stats.MercWins, 1, "merc.body.desert"),
            Count("merc_wins_5", "Guard Duty", "Win 5 matches as a mercenary.", M, p => p.Stats.MercWins, 5, "merc.body.woodland", "merc.light.blue"),
            Count("merc_wins_25", "Enforcer", "Win 25 matches as a mercenary.", M, p => p.Stats.MercWins, 25, "merc.body.digital"),
            Count("merc_wins_100", "Warden", "Win 100 matches as a mercenary.", M, p => p.Stats.MercWins, 100, "merc.body.red_tiger",
                "merc.light.magenta"),
            Once("merc_wall", "The Wall", "Win as a mercenary without losing an objective.", M,
                (_, row, r) => row.Team == 1 && Won(row, r) && r.ObjectivesNeeded > 0 && r.ObjectivesTaken == 0, "merc.body.lockdown"),
            Count("merc_marks_50", "Painted", "Mark 50 spies with the laser.", M, p => p.Feat(FeatKeys.MercMarks), 50,
                "merc.body.laser_grid", "merc.ar.laser_grid", "merc.smg.laser_grid", "merc.shotgun.laser_grid", "merc.light.cyan"),
            Count("merc_charge_25", "Battering Ram", "Hit 25 spies with a charge.", M, p => p.Feat(FeatKeys.MercChargeHits), 25,
                "merc.light.violet"),

            // ---- Weapons: the feats that, with the gun ladders, lead to the golden guns.
            Count("merc_sniper_10", "Marksman", "Kill 10 spies through the scope.", W, p => p.Feat(FeatKeys.MercSniperKills), 10,
                "merc.light.green"),
            Count("merc_sniper_50", "Deadeye", "Kill 50 spies through the scope.", W, p => p.Feat(FeatKeys.MercSniperKills), 50,
                "merc.body.blue_tiger"),
            Count("merc_terminate_25", "Executioner", "Finish off 25 downed spies.", W, p => p.Feat(FeatKeys.MercTerminateKills), 25,
                "merc.light.red"),
            Count("merc_grenade_kill", "Fire in the Hole", "Kill a spy with a frag grenade.", W, p => p.Feat(FeatKeys.MercGrenadeKills), 1,
                "merc.body.frag"),
            Count("merc_mine_kill", "Tripwire", "Kill a spy with a mine.", W, p => p.Feat(FeatKeys.MercMineKills), 1,
                "merc.ar.frag", "merc.smg.frag", "merc.shotgun.frag"),
        };

        foreach ((string gun, string gunName) in s_guns)
        {
            foreach ((string camo, bool headshots, int count, string title) in s_gunLadder)
            {
                string key = headshots ? FeatKeys.GunHeadshots(gun) : FeatKeys.GunKills(gun);
                string what = headshots ? "headshot kills" : "kills";
                list.Add(Count($"{gun}_{camo}", $"{gunName}: {title}", $"Get {count} {what} with the {gunName.ToLowerInvariant()}.", W,
                    p => p.Feat(key), count, $"merc.{gun}.{camo}"));
            }
        }

        string[] weaponIds = list.Where(a => a.Category == W).Select(a => a.Id).ToArray();
        list.Add(new Achievement("gilded", "Gilded", "Finish every weapon achievement: the golden camo for all three merc guns.", W,
            p => (weaponIds.Count(p.Earned.Contains), weaponIds.Length), ["merc.ar.golden", "merc.smg.golden", "merc.shotgun.golden"],
            (p, _, _) => weaponIds.All(p.Earned.Contains)));

        string[] spyIds = list.Where(a => a.Category == S).Select(a => a.Id).ToArray();
        list.Add(new Achievement("spy_gilded", "Gilded Ghost", "Finish every spy achievement: the golden pistol.", S,
            p => (spyIds.Count(p.Earned.Contains), spyIds.Length), ["spy.pistol.golden"], (p, _, _) => spyIds.All(p.Earned.Contains)));

        foreach ((string code, string name, _) in CosmeticCatalog.Maps)
        {
            string camo = "body.map_" + code.ToLowerInvariant();
            list.Add(new Achievement("map_" + code.ToLowerInvariant(), name, $"Win on {name} as a spy and as a mercenary.",
                AchievementCategory.Map, p => ((p.MapWin(code, 0) > 0 ? 1 : 0) + (p.MapWin(code, 1) > 0 ? 1 : 0), 2), Both(camo)));
        }

        foreach (string reward in list.SelectMany(a => a.Rewards))
        {
            if (CosmeticCatalog.Find(reward) is null)
            {
                throw new InvalidOperationException($"Achievement reward {reward} is not in the cosmetic catalogue.");
            }
        }

        return list;
    }
}
