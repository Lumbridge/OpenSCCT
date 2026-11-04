using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenScct.Core.Engine;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;

namespace OpenScctGame.UI;

/// <summary>Career presentation uses raw totals, never capped achievement progress.</summary>
public static class StatsModel
{
    public sealed record Row(string Category, string Label, string Value);

    public static readonly string[] Categories = ["Overview", "Spy", "Mercenary", "Gadgets", "Weapons", "Maps", "Progression"];

    public static readonly (string Category, string Key, string Label)[] Counters =
    [
        .. CareerStatCounters.All.Select(c => (c.Category, c.Key, c.Label)),
        ("Spy", FeatKeys.SpyGrabKills, "Grab kills"),
        ("Spy", FeatKeys.SpyGrabKos, "Grab knockouts"),
        ("Spy", FeatKeys.SpyShockerStuns, "Shocker stuns"),
        ("Spy", FeatKeys.SpyDropKos, "Drop knockouts"),
        ("Spy", FeatKeys.SpyNeutralisations, "Neutralisations"),
        ("Overview", FeatKeys.EnemyKills, "Enemy kills"),
        ("Spy", FeatKeys.SpyNeckBreaks, "Neck breaks"),
        ("Spy", FeatKeys.SpyChokes, "Chokes"),
        ("Spy", FeatKeys.SpyDropKills, "Drop kills"),
        ("Spy", FeatKeys.SpyObjectives, "Objectives completed"),
        ("Spy", FeatKeys.SpyShockerHeadshots, "Shocker headshots"),
        ("Spy", FeatKeys.SpyLedgeGrabs, "Ledge grabs"),
        ("Spy", FeatKeys.SpyMineKills, "Mercenaries killed with their mines"),
        ("Mercenary", FeatKeys.MercSniperKills, "Scoped kills"),
        ("Mercenary", FeatKeys.MercHeadshots, "Headshot kills"),
        ("Mercenary", FeatKeys.MercTerminateKills, "Choke kills"),
        ("Mercenary", FeatKeys.MercHipfireKills, "Hip-fire kills"),
        ("Mercenary", FeatKeys.MercChargeHits, "Charge hits"),
        ("Mercenary", FeatKeys.MercBerserkHits, "Berserk hits"),
        ("Gadgets", FeatKeys.SpyStickyKos, "Spy · Sticky camera knockouts"),
        ("Gadgets", FeatKeys.SpySmokeKos, "Spy · Smoke knockouts"),
        ("Gadgets", FeatKeys.SpyStickyCamUses, "Spy · Sticky camera uses"),
        ("Gadgets", FeatKeys.SpyChaffJams, "Spy · Mercenaries jammed by chaff"),
        ("Gadgets", FeatKeys.SpyMineKills, "Spy · Mercenaries killed with their mines"),
        ("Gadgets", FeatKeys.SpyBulletHits, "Spy · Tracking bullet direct hits"),
        ("Gadgets", FeatKeys.SpyFlashes, "Spy · Mercenaries flashed"),
        ("Gadgets", FeatKeys.MercGrenadeKills, "Mercenary · Grenade kills"),
        ("Gadgets", FeatKeys.MercMineKills, "Mercenary · Mine kills"),
        ("Gadgets", FeatKeys.MercMinesPlaced, "Mercenary · Mines placed"),
        ("Gadgets", FeatKeys.MercMarks, "Mercenary · Tracking marks"),
        ("Gadgets", FeatKeys.MercTaserHits, "Mercenary · Taser hits"),
    ];

    public static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    public static string Ratio(long numerator, long denominator, bool percent = false) => denominator <= 0
        ? "—" : ((double)numerator / denominator * (percent ? 100 : 1)).ToString(percent ? "0.0" : "0.00", CultureInfo.InvariantCulture) + (percent ? "%" : "");

    public static IReadOnlyList<Row> Build(AccountInfo account)
    {
        var rows = new List<Row>();
        void Add(string category, string label, long value) => rows.Add(new(category, label, Number(value)));
        AccountStats s = account.Stats;
        long wins = (long)s.SpyWins + s.MercWins;
        Add("Overview", "Rounds played", s.Matches);
        Add("Overview", "Wins", wins);
        Add("Overview", "Losses", s.Losses);
        rows.Add(new("Overview", "Win rate", Ratio(wins, s.Matches, true)));
        Add("Overview", "Kills", s.Kills);
        Add("Overview", "Deaths", s.Deaths);
        rows.Add(new("Overview", "Kill / death ratio", Ratio(s.Kills, s.Deaths)));
        rows.Add(new("Overview", "Kills per round", Ratio(s.Kills, s.Matches)));
        rows.Add(new("Overview", "Deaths per round", Ratio(s.Deaths, s.Matches)));
        Add("Spy", "Rounds won as spy", s.SpyWins);
        Add("Mercenary", "Rounds won as mercenary", s.MercWins);
        foreach (var (category, rounds) in new[] { ("Spy", s.SpyRounds), ("Mercenary", s.MercRounds) })
        {
            rows.Add(new(category, "Rounds played (since tracking began)", rounds is null ? "—" : Number(rounds.Matches)));
            rows.Add(new(category, "Win rate (since tracking began)", rounds is null ? "—" : Ratio(rounds.Wins, rounds.Matches, true)));
        }
        string Feat(string key) => account.Career is { } career
            ? Number(career.Feats.TryGetValue(key, out int value) ? value : 0) : "—";
        foreach (var counter in Counters)
            rows.Add(new(counter.Category, counter.Label, Feat(counter.Key)));
        long Total(string key) => account.Career?.Feats.GetValueOrDefault(key) ?? 0;
        rows.Add(new("Weapons", "Accuracy", account.Career is null ? "—" : Ratio(Total(CareerStatCounters.ShotsHit), Total(CareerStatCounters.ShotsFired), true)));
        var guns = new[] { (Id: "ar", Name: "Assault rifle"), (Id: "smg", Name: "SMG"), (Id: "shotgun", Name: "Shotgun") }
            .Concat(Sidearms.All.Select(g => (g.Id, g.Name)));
        foreach (var gun in guns)
        {
            rows.Add(new("Weapons", gun.Name + " · Kills", Feat(FeatKeys.GunKills(gun.Id))));
            rows.Add(new("Weapons", gun.Name + " · Headshot kills", Feat(FeatKeys.GunHeadshots(gun.Id))));
        }
        var mapNames = CosmeticCatalog.Maps.ToDictionary(m => m.Code, m => m.Name, StringComparer.OrdinalIgnoreCase);
        foreach (string map in mapNames.Keys.Concat(account.Career?.Maps.Select(m => m.Map) ?? []).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(m => m))
        {
            MapCareerStats? totals = account.Career?.Maps.FirstOrDefault(m => string.Equals(m.Map, map, StringComparison.OrdinalIgnoreCase));
            string name = mapNames.GetValueOrDefault(map, map);
            rows.Add(new("Maps", name + " · Spy wins", account.Career is null ? "—" : Number(totals?.SpyWins ?? 0)));
            rows.Add(new("Maps", name + " · Mercenary wins", account.Career is null ? "—" : Number(totals?.MercWins ?? 0)));
        }
        Add("Progression", "Achievements earned", account.Achievements.Count(a => a.Earned is not null));
        Add("Progression", "Cosmetics unlocked", account.Unlocked.Count);
        if (account.Progress is { } p)
        {
            Add("Progression", "Level", p.Level);
            Add("Progression", "Prestige", p.Prestige);
            Add("Progression", "Highest level reached", p.BestLevel);
            Add("Progression", "Lifetime XP", p.TotalXp);
            Add("Progression", "Current prestige XP", p.Xp);
            Add("Progression", "Titles unlocked", p.Titles.Count);
        }
        return rows;
    }
}
