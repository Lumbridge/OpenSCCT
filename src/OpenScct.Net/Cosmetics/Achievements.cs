using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core.Engine;
using OpenScct.Net.Discovery;

namespace OpenScct.Net.Cosmetics;

/// <summary>Achievement groups, in the order the game shows them (the names go to the game as text).</summary>
public enum AchievementCategory
{
    General,
    Spy,
    Merc,
    Weapons,
    Gadgets,
    Map,
}

/// <summary>
/// An account's totals as the achievements read them: the stats, the feats summed over all counted rounds, the base
/// maps won per side, and the achievements already earned (for the ones that need others).
/// </summary>
public sealed class AccountProgress
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
public sealed record Achievement(
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

/// <summary>
/// Every achievement, in the order the game lists them (shared: the master grants them, the game lists them even
/// when its master is older). Thresholds are in the tables at the top.
/// </summary>
public static class Achievements
{
    // The merc gun camo ladder, CoD4/MW2 style: (camo, kills or headshots, count, title).
    private static readonly (string Camo, bool Headshots, int Count, string Title)[] s_gunLadder =
    [
        ("woodland", false, 50, "Woodland"),
        ("desert", false, 150, "Desert"),
        ("digital", false, 300, "Digital"),
        ("red_tiger", true, 75, "Red Tiger"),
        ("blue_tiger", true, 200, "Blue Tiger"),
    ];

    private static readonly (string Key, string Name)[] s_guns = [("ar", "Assault rifle"), ("smg", "SMG"), ("shotgun", "Shotgun")];

    /// <summary>Wins a map achievement needs on each side.</summary>
    private const int MapWinsPerSide = 3;

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

    private static string[] Guns(string camo) => ["spy.pistol." + camo, "merc.ar." + camo, "merc.smg." + camo, "merc.shotgun." + camo];

    private static List<Achievement> Build()
    {
        const AchievementCategory G = AchievementCategory.General;
        const AchievementCategory S = AchievementCategory.Spy;
        const AchievementCategory M = AchievementCategory.Merc;
        const AchievementCategory W = AchievementCategory.Weapons;
        const AchievementCategory D = AchievementCategory.Gadgets;
        var list = new List<Achievement>
        {
            // ---- General: tiers of matches, wins and kills; each gives a colour tint for both sides.
            Count("first_match", "Boots on the Ground", "Finish 5 matches on an official server.", G, p => p.Stats.Matches, 5, Both("body.urban")),
            Count("matches_25", "Regular", "Finish 25 matches.", G, p => p.Stats.Matches, 25, Both("body.sand")),
            Count("matches_100", "Dedicated", "Finish 100 matches.", G, p => p.Stats.Matches, 100, Both("body.olive")),
            Count("matches_300", "Lifer", "Finish 300 matches.", G, p => p.Stats.Matches, 300, Both("body.midnight")),
            Count("wins_25", "Veteran", "Win 25 matches.", G, Wins, 25, Both("body.arctic")),
            Count("wins_100", "Elite", "Win 100 matches.", G, Wins, 100, Both("body.crimson")),
            Count("kills_50", "Bloodied", "Take down 50 opponents.", G, p => p.Stats.Kills, 50, Both("body.navy")),
            Count("kills_200", "Centurion", "Take down 200 opponents.", G, p => p.Stats.Kills, 200, Both("body.rust")),
            Count("kills_750", "Reaper", "Take down 750 opponents.", G, p => p.Stats.Kills, 750, Both("body.ash")),

            // ---- Spy
            Count("spy_win", "Ghost Protocol", "Win 5 matches as a spy.", S, p => p.Stats.SpyWins, 5, "spy.body.desert"),
            Count("spy_wins_15", "Infiltrator", "Win 15 matches as a spy.", S, p => p.Stats.SpyWins, 15, "spy.body.woodland", "spy.light.blue"),
            Count("spy_wins_40", "Shadow Operative", "Win 40 matches as a spy.", S, p => p.Stats.SpyWins, 40, "spy.body.digital"),
            Count("spy_wins_100", "Phantom", "Win 100 matches as a spy.", S, p => p.Stats.SpyWins, 100, "spy.body.red_tiger", "spy.light.magenta"),
            Once("objectives", "Clean Sweep", "Win as a spy by taking every objective.", S,
                (_, row, r) => row.Team == 0 && r.WinningTeam == 0 && r.ObjectivesNeeded > 0 && r.ObjectivesTaken >= r.ObjectivesNeeded,
                "spy.pistol.woodland"),
            Count("spy_objectives_25", "Data Thief", "Take 25 objectives.", S, p => p.Feat(FeatKeys.SpyObjectives), 25,
                "spy.pistol.digital", "spy.light.cyan"),
            Count("spy_objectives_100", "Ghost in the Machine", "Take 100 objectives.", S, p => p.Feat(FeatKeys.SpyObjectives), 100,
                "spy.body.static", "spy.pistol.static"),
            Count("spy_necks_25", "Snap", "Break 25 necks.", S, p => p.Feat(FeatKeys.SpyNeckBreaks), 25, "spy.pistol.desert", "spy.light.red"),
            Count("spy_necks_100", "Breaker", "Break 100 necks.", S, p => p.Feat(FeatKeys.SpyNeckBreaks), 100, "spy.body.breaker"),
            Count("spy_drop_10", "Death from Above", "Kill 10 mercs with drop attacks.", S, p => p.Feat(FeatKeys.SpyDropKills), 10,
                "spy.pistol.red_tiger"),
            Once("spy_grab_3", "Triple Threat", "Kill 3 mercs with grabs in one round.", S,
                (_, row, _) => row.Team == 0 && RowFeat(row, FeatKeys.SpyGrabKills) >= 3, "spy.body.blue_tiger", "spy.pistol.blue_tiger",
                "spy.light.amber"),
            Count("spy_sticky_25", "Shockwave", "Knock out 25 mercs with sticky cam gas.", S, p => p.Feat(FeatKeys.SpyStickyKos), 25,
                "spy.pistol.shockwave"),
            Once("spy_untouchable", "Untouchable", "Win as a spy without dying.", S,
                (_, row, r) => row.Team == 0 && Won(row, r) && row.Deaths == 0, "spy.body.night_ops", "spy.light.violet"),

            // ---- Merc
            Count("merc_win", "Lockdown", "Win 5 matches as a mercenary.", M, p => p.Stats.MercWins, 5, "merc.body.desert"),
            Count("merc_wins_15", "Guard Duty", "Win 15 matches as a mercenary.", M, p => p.Stats.MercWins, 15, "merc.body.woodland",
                "merc.light.blue"),
            Count("merc_wins_40", "Enforcer", "Win 40 matches as a mercenary.", M, p => p.Stats.MercWins, 40, "merc.body.digital"),
            Count("merc_wins_100", "Warden", "Win 100 matches as a mercenary.", M, p => p.Stats.MercWins, 100, "merc.body.red_tiger",
                "merc.light.magenta"),
            Once("merc_wall", "The Wall", "Win as a mercenary without losing an objective.", M,
                (_, row, r) => row.Team == 1 && Won(row, r) && r.ObjectivesNeeded > 0 && r.ObjectivesTaken == 0, "merc.body.lockdown"),
            Count("merc_marks_50", "Painted", "Mark 50 spies with the laser.", M, p => p.Feat(FeatKeys.MercMarks), 50,
                "merc.body.laser_grid", "merc.ar.laser_grid", "merc.smg.laser_grid", "merc.shotgun.laser_grid", "merc.light.cyan"),
            Count("merc_charge_25", "Battering Ram", "Hit 25 spies with a charge.", M, p => p.Feat(FeatKeys.MercChargeHits), 25,
                "merc.light.violet"),

            // ---- Weapons: the feats that, with the gun ladders, lead to the golden guns.
            Count("merc_sniper_25", "Marksman", "Kill 25 spies through the scope.", W, p => p.Feat(FeatKeys.MercSniperKills), 25,
                "merc.light.green"),
            Count("merc_sniper_75", "Deadeye", "Kill 75 spies through the scope.", W, p => p.Feat(FeatKeys.MercSniperKills), 75,
                "merc.body.blue_tiger"),
            Count("merc_terminate_50", "Executioner", "Finish off 50 downed spies.", W, p => p.Feat(FeatKeys.MercTerminateKills), 50,
                "merc.light.red"),

            // ---- Gadgets: the spy kit, then the merc kit (the taser and the other Change Gear gadgets included).
            Count("spy_smoke_15", "Smoke Screen", "Knock out 15 mercs with smoke grenades.", D, p => p.Feat(FeatKeys.SpySmokeKos), 15,
                "spy.body.smoke"),
            Count("spy_cam_50", "Eye in the Sky", "Use the sticky cam 50 times.", D, p => p.Feat(FeatKeys.SpyStickyCamUses), 50,
                "spy.light.teal"),
            Count("spy_chaff_40", "Static Field", "Jam 40 mercs with chaff.", D, p => p.Feat(FeatKeys.SpyChaffJams), 40, "spy.pistol.chaff"),
            Count("spy_chaff_150", "Blackout", "Jam 150 mercs with chaff.", D, p => p.Feat(FeatKeys.SpyChaffJams), 150, "spy.body.chaff"),
            Count("spy_headshot_25", "Lights Out", "Land 25 Shocker headshots.", D, p => p.Feat(FeatKeys.SpyShockerHeadshots), 25,
                "spy.light.orange"),
            Count("spy_ledge_15", "Over the Edge", "Grab 15 mercs from a ledge.", D, p => p.Feat(FeatKeys.SpyLedgeGrabs), 15,
                "spy.pistol.smoke"),
            Count("spy_mine_3", "Own Goal", "Kill 3 mercs by setting off a merc mine.", D, p => p.Feat(FeatKeys.SpyMineKills), 3,
                "spy.pistol.turnabout"),
            Count("spy_mine_10", "Turnabout", "Kill 10 mercs by setting off a merc mine.", D, p => p.Feat(FeatKeys.SpyMineKills), 10,
                "spy.body.turnabout"),
            Count("spy_mine_25", "Hoist by Their Own Petard", "Kill 25 mercs by setting off a merc mine.", D,
                p => p.Feat(FeatKeys.SpyMineKills), 25, "spy.body.minefield"),
            Once("spy_gadgeteer", "Gadgeteer", "In one round: knock out a merc with smoke, one with sticky cam gas, and jam one with chaff.", D,
                (_, row, _) => row.Team == 0 && RowFeat(row, FeatKeys.SpySmokeKos) > 0 && RowFeat(row, FeatKeys.SpyStickyKos) > 0 &&
                    RowFeat(row, FeatKeys.SpyChaffJams) > 0,
                "spy.body.gadgeteer", "spy.pistol.gadgeteer"),
            Count("merc_taser_25", "Live Wire", "Tase 25 spies.", D, p => p.Feat(FeatKeys.MercTaserHits), 25, "merc.light.yellow"),
            Count("merc_taser_100", "High Voltage", "Tase 100 spies.", D, p => p.Feat(FeatKeys.MercTaserHits), 100,
                "merc.body.voltage", "merc.ar.voltage", "merc.smg.voltage", "merc.shotgun.voltage"),
            Count("merc_mines_placed_75", "Minefield", "Place 75 mines.", D, p => p.Feat(FeatKeys.MercMinesPlaced), 75, "merc.body.minefield"),
            Count("merc_mine_15", "Tripwire", "Kill 15 spies with mines.", D, p => p.Feat(FeatKeys.MercMineKills), 15,
                "merc.ar.frag", "merc.smg.frag", "merc.shotgun.frag"),
            Count("merc_mine_50", "Killing Field", "Kill 50 spies with mines.", D, p => p.Feat(FeatKeys.MercMineKills), 50,
                "merc.ar.minefield", "merc.smg.minefield", "merc.shotgun.minefield"),
            Count("merc_grenade_15", "Fire in the Hole", "Kill 15 spies with frag grenades.", D, p => p.Feat(FeatKeys.MercGrenadeKills), 15,
                "merc.body.frag"),
            Count("merc_grenade_50", "Shrapnel", "Kill 50 spies with frag grenades.", D, p => p.Feat(FeatKeys.MercGrenadeKills), 50,
                "merc.light.orange"),
            Count("merc_berserk_25", "Berserker", "Land 25 berserk hits.", D, p => p.Feat(FeatKeys.MercBerserkHits), 25, "merc.light.amber"),
            Once("merc_demolitions", "Demolitions", "Kill 2 spies with frag grenades or mines in one round.", D,
                (_, row, _) => row.Team == 1 && RowFeat(row, FeatKeys.MercGrenadeKills) + RowFeat(row, FeatKeys.MercMineKills) >= 2,
                "merc.body.gadgeteer", "merc.ar.gadgeteer", "merc.smg.gadgeteer", "merc.shotgun.gadgeteer"),
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

        string[] gadgetIds = list.Where(a => a.Category == D).Select(a => a.Id).ToArray();
        list.Add(new Achievement("gadget_master", "Gadget Master", "Finish every gadget achievement, spy and merc: the Mastercraft finish.", D,
            p => (gadgetIds.Count(p.Earned.Contains), gadgetIds.Length),
            ["spy.body.mastercraft", "spy.pistol.mastercraft", "merc.body.mastercraft", "merc.ar.mastercraft", "merc.smg.mastercraft",
                "merc.shotgun.mastercraft"],
            (p, _, _) => gadgetIds.All(p.Earned.Contains)));

        // ---- The painted gun skins: each gives its skin for the spy pistol and all three merc guns. Listed after the
        // gilded ones so they are not part of what those need.
        list.Add(Once("hot_streak", "Pyre", "Take down 10 opponents in one match.", G, (_, row, _) => row.Kills >= 10, Guns("pyre")));
        list.Add(new Achievement("kaido", "Kaido", "Win 40 matches as a spy and 40 as a mercenary.", G,
            p => (Math.Min(p.Stats.SpyWins, 40) + Math.Min(p.Stats.MercWins, 40), 80), Guns("kaido")));
        list.Add(Count("kills_1500", "Cryo", "Take down 1500 opponents.", G, p => p.Stats.Kills, 1500, Guns("cryo")));

        foreach ((string code, string name, _) in CosmeticCatalog.Maps)
        {
            string camo = "body.map_" + code.ToLowerInvariant();
            list.Add(new Achievement("map_" + code.ToLowerInvariant(), name,
                $"Win on {name} {MapWinsPerSide} times as a spy and {MapWinsPerSide} times as a mercenary.", AchievementCategory.Map,
                p => (Math.Min(p.MapWin(code, 0), MapWinsPerSide) + Math.Min(p.MapWin(code, 1), MapWinsPerSide), 2 * MapWinsPerSide),
                Both(camo)));
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
