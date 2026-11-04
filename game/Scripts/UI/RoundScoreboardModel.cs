using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenScct.Core.Engine;
using OpenScct.Net.Scoreboards;

namespace OpenScctGame.UI;

/// <summary>Shared, deterministic labels and unavailable/pending semantics for every scoreboard surface.</summary>
public static class RoundScoreboardModel
{
    public sealed record Column(string Label, string Description, Func<RoundScoreboard, ScoreboardPlayer, string> Read, bool Connection = false);
    public sealed record Detail(string Group, string Label, string Value);

    public static string Number(int value) => Math.Max(0, value).ToString("N0", CultureInfo.InvariantCulture);
    public static string Stat(RoundScoreboard board, ScoreboardPlayer player, string key)
    {
        if (player.Stats is null) return !board.Completed && !board.Partial && player.StatsVersion > 0 ? "0" : "—";
        if (player.Stats.TryGetValue(key, out int value)) return Number(value);
        return player.StatsVersion > 0 ? "0" : "—";
    }

    public static string Lives(ScoreboardPlayer player) => player.Lives < -1 ? "—" : player.Lives == -1 || player.Lives >= 100 ? "∞" : Number(player.Lives);
    public static string Objectives(ScoreboardPlayer player) => player.Objectives < 0 ? "—" : Number(player.Objectives);
    public static string Timing(RoundScoreboard board, double snapshotAge = 0) => board.Completed ? "Final totals"
        : board.Partial && board.EndedUtc is not null && board.AsOfSeconds >= board.ElapsedSeconds ? "Round stopped · final totals" : board.Partial
        ? board.AsOfSeconds is { } released
            ? $"Partial record · combat totals through {Clock(released)}"
            : "Partial record · no combat sample was released before leaving"
        : board.SchemaVersion > 0 ? LiveTiming(board, snapshotAge) : "Detailed stats unavailable on this server";

    private static string LiveTiming(RoundScoreboard board, double snapshotAge)
    {
        // Snapshots arrive once per second; the first combat sample must first age for 30 seconds.
        double remaining = (board.AsOfSeconds is null ? Math.Max(1, 30 - board.ElapsedSeconds) : 1)
            - Math.Max(0, snapshotAge);
        return remaining > 0
            ? "Combat stats delayed 30s · Next update " + (Math.Ceiling(remaining * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture) + "s"
            : "Combat stats delayed 30s · Awaiting update…";
    }
    public static string Clock(double seconds)
    {
        int total = (int)Math.Clamp(double.IsFinite(seconds) ? seconds : 0, 0, int.MaxValue);
        return string.Create(CultureInfo.InvariantCulture, $"{total / 60:00}:{total % 60:00}");
    }
    public static int FirstTeam(RoundScoreboard board, int local) => board.Rows.FirstOrDefault(p => p.ParticipantId == local)?.Team == 1 ? 1 : 0;
    public static IReadOnlyList<ScoreboardPlayer> Team(RoundScoreboard board, int team) => board.Rows.Where(p => p.Team == team)
        .OrderBy(p => p.LeftEarly).ThenBy(p => p.ParticipantId).ToArray();
    /// <summary>The passive board has room for the maximum active roster; departed rows fill spare slots.</summary>
    public static IReadOnlyList<ScoreboardPlayer> LiveRows(RoundScoreboard board) => board.Rows.Where(p => p.Team is 0 or 1)
        .OrderBy(p => p.LeftEarly).ThenBy(p => p.ParticipantId).Take(24).ToArray();

    private static Column Counter(string label, string description, string key) => new(label, description, (b, p) => Stat(b, p, key));
    private static readonly Column Ping = new("Ping", "Current latency in milliseconds; not retained as combat information.", (_, p) => p.Bot || p.LeftEarly || p.PingMs < 0 ? "—" : Number(p.PingMs), Connection: true);
    private static readonly Column RemainingLives = new("Lives", "Remaining lives; ∞ means unlimited.", (_, p) => Lives(p));
    private static readonly Column Kd = new("K / D", "Public kills and deaths. Detailed combat totals are delayed separately.", (_, p) => $"{Number(p.Kills)} / {Number(p.Deaths)}");
    public static IReadOnlyList<Column> Columns(int team) => team == 0 ? SpyColumns : MercColumns;
    private static readonly Column[] SpyColumns =
    [
        RemainingLives, new("Obj", "Objectives completed.", (_, p) => Objectives(p)), Kd,
        Counter("Neutr.", "Merc neutralisations: successful knockouts or enemy kills; shock stuns and flashes are separate.", "spy.neutralisations"),
        Counter("Cam sleep", "Mercs put to sleep by sticky-camera gas.", FeatKeys.SpyStickyKos),
        Counter("Stuns", "Successful shocker stuns, including headshot stuns.", "spy.shocker_stuns"),
        Counter("Flashes", "Mercs successfully affected by flash grenades, once per target per grenade.", FeatKeys.SpyFlashes),
        Ping,
    ];
    private static readonly Column[] MercColumns =
    [
        RemainingLives, Kd,
        Counter("Damage", "Enemy health actually removed; excludes overkill and friendly damage.", "career.damagegiven"),
        Counter("Mines", "Enemy kills caused by mines.", FeatKeys.MercMineKills),
        Counter("Nades", "Enemy kills caused by grenades.", FeatKeys.MercGrenadeKills),
        Counter("Headshots", "Headshot kills; also included in weapon kill totals.", FeatKeys.MercHeadshots),
        Ping,
    ];

    public static IReadOnlyList<Detail> Details(RoundScoreboard board, ScoreboardPlayer player)
    {
        var rows = new List<Detail>();
        void Add(string group, string label, string key) => rows.Add(new(group, label, Stat(board, player, key)));
        if (player.Team == 0)
        {
            rows.Add(new("Objectives", "Objectives completed", Objectives(player)));
            Add("Objectives", "Objective capture time (seconds)", "career.spy_objectivetimecaptured");
            Add("Neutralisations", "Total merc neutralisations", "spy.neutralisations");
            Add("Neutralisations", "Sticky-camera sleeps", FeatKeys.SpyStickyKos);
            Add("Neutralisations", "Smoke knockouts", FeatKeys.SpySmokeKos);
            Add("Neutralisations", "Head-jump hits", "career.dropattacks");
            Add("Neutralisations", "Head-jump knockouts", "spy.drop_kos");
            Add("Neutralisations", "Head-jump kills", FeatKeys.SpyDropKills);
            Add("Neutralisations", "Neck-grab knockouts", "spy.grab_kos");
            Add("Neutralisations", "Neck snaps", FeatKeys.SpyNeckBreaks);
            Add("Neutralisations", "Choke kills", FeatKeys.SpyChokes);
            Add("Neutralisations", "Ledge-grab kills", FeatKeys.SpyLedgeGrabs);
            Add("Neutralisations", "Merc mine kills triggered", FeatKeys.SpyMineKills);
            Add("Disruption", "Successful shocker stuns", "spy.shocker_stuns");
            Add("Disruption", "Headshot stuns", FeatKeys.SpyShockerHeadshots);
            Add("Disruption", "Successful flashes", FeatKeys.SpyFlashes);
            Add("Disruption", "Chaff jams", FeatKeys.SpyChaffJams);
            Add("Disruption", "Direct spy-bullet hits", FeatKeys.SpyBulletHits);
        }
        else
        {
            Add("Combat", "Damage dealt", "career.damagegiven");
            Add("Combat", "Mine kills", FeatKeys.MercMineKills);
            Add("Combat", "Grenade kills", FeatKeys.MercGrenadeKills);
            Add("Combat", "Headshot kills", FeatKeys.MercHeadshots);
            Add("Combat", "Scoped kills", FeatKeys.MercSniperKills);
            Add("Combat", "Hip-fire kills", FeatKeys.MercHipfireKills);
            Add("Combat", "Finishers", FeatKeys.MercTerminateKills);
            Add("Combat", "Assault-rifle kills", FeatKeys.GunKills("ar"));
            Add("Combat", "SMG kills", FeatKeys.GunKills("smg"));
            Add("Combat", "Shotgun kills", FeatKeys.GunKills("shotgun"));
            Add("Disruption", "Taser hits", FeatKeys.MercTaserHits);
            Add("Disruption", "Charge hits", FeatKeys.MercChargeHits);
            Add("Disruption", "Berserk hits", FeatKeys.MercBerserkHits);
            Add("Disruption", "Spies marked", FeatKeys.MercMarks);
            Add("Disruption", "Spy-trap targets", "career.merc_spytraptargets");
            Add("Defense & support", "Bombs secured", "career.merc_securedbombs");
            Add("Defense & support", "Disks returned", "career.merc_securedisks");
            Add("Defense & support", "Backpacks given", "career.merc_backpacksgiven");
            Add("Defense & support", "Backpacks taken", "career.merc_backpackstaken");
        }
        Add("Round totals", "Damage taken", "career.damagetaken");
        Add("Round totals", "Environmental kills", "career.environmentalkills");
        Add("Round totals", "Shots fired", CareerStatCounters.ShotsFired);
        Add("Round totals", "Shots hit", CareerStatCounters.ShotsHit);
        string accuracy = "—";
        if (player.Stats is null) accuracy = Stat(board, player, CareerStatCounters.ShotsFired);
        else if (player.Stats.TryGetValue(CareerStatCounters.ShotsFired, out int fired) && fired > 0
            && (player.Stats.ContainsKey(CareerStatCounters.ShotsHit) || player.StatsVersion > 0))
            accuracy = (100d * Math.Clamp(player.Stats.GetValueOrDefault(CareerStatCounters.ShotsHit), 0, fired) / fired).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        rows.Add(new("Round totals", "Accuracy", accuracy));
        Add("Round totals", "Medkits used", "career.medkitsused");
        Add("Round totals", "Team kills", "career.teamkills");
        Add("Round totals", "Suicides", "career.suicides");
        return rows;
    }
}
