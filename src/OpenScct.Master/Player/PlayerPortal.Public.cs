using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using OpenScct.Core.Engine;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private sealed record PublicServer(string Name, string Map, int Mode, string Region, int Players, int Maximum,
        bool Password, bool Accounts, bool Trusted, ServerKind Kind, SessionPhase Phase);

    private List<PublicServer> LiveServers()
    {
        lock (_registry.Gate)
        {
            return _registry.Snapshot().Where(r => r.Last.Listed).Select(r =>
            {
                HeartbeatRequest h = r.Last;
                return new PublicServer(h.Name, h.Map, h.Mode, h.Region, h.Players, h.MaxPlayers, h.Password, h.RequireAccounts, r.Trusted, h.Kind, h.Phase);
            }).OrderByDescending(r => r.Trusted).ThenByDescending(r => r.Players).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    private IResult Home(HttpContext c)
    {
        List<PublicServer> servers = LiveServers();
        Dictionary<string, string> settings = _store.Settings();
        string welcome = settings.GetValueOrDefault("site.welcome", "Find a game, track your stats, and manage your OpenSCCT account.");
        string announcement = settings.GetValueOrDefault("site.announcement", string.Empty);
        var body = new StringBuilder();
        if (announcement.Length > 0) body.Append("<aside class=\"announcement\"><strong>Announcement</strong><p>").Append(H(announcement)).Append("</p></aside>");
        body.Append("<section class=\"hero\"><div><p class=\"eyebrow\">OPENSCCT PLAYER HUB</p><h1>Spies vs.<br><span>Mercenaries.</span></h1><p class=\"lead\">")
            .Append(H(welcome)).Append("</p><div class=\"button-row\">").Append(Link("/download", "Get OpenSCCT ↗", "button"))
            .Append(Link("/servers", "Find a session →", "button secondary"))
            .Append("</div><p class=\"hero-note\">Free community project · Windows &amp; Linux · Optional Discord sign-in</p></div>")
            .Append($"<div class=\"hero-art\" aria-hidden=\"true\"><div class=\"radar\"><span class=\"radar-label top\">SIGNAL ACQUIRED</span><img class=\"radar-logo\" src=\"{H(AssetUrl("/web/assets/mark.svg"))}\" alt=\"\">")
            .Append("<i class=\"radar-point one\"></i><i class=\"radar-point two\"></i><i class=\"radar-point three\"></i><span class=\"radar-label bottom\">SPY / MERC</span></div><span class=\"art-caption\">OPENSCCT</span></div></section>")
            .Append("<div data-live=\"home-stats\"><div class=\"stats-grid\">")
            .Append(Stat("Players online", N(servers.Sum(s => s.Players)), "Across listed sessions"))
            .Append(Stat("Live sessions", N(servers.Count), "Games and community hubs"))
            .Append(Stat("Public players", N(_store.PublicPlayers(null, "name", 0, 1).Total), "Profiles you can browse"))
            .Append(Stat("Rounds today", N(_store.CountMatches(_store.Now / 86400 * 86400)), "Recorded by trusted servers"))
            .Append("</div></div>");
        body.Append(Section("Live sessions", "<div data-live=\"home-servers\">" + ServerTable(servers.Take(4)) + "</div>",
            "<div class=\"button-row\">" + LiveStatus + Link("/servers", "All sessions →", "text-link") + "</div>"));
        body.Append(Section("Player features", "<div class=\"grid-three\">" +
            Feature("01", "Your stats", "View your stats, achievements, level, and prestige.", "/account", "Open your account →") +
            Feature("02", "Players and leaderboards", "Browse public profiles and compare lifetime XP, wins, and kills.", "/players", "Browse players →") +
            Feature("03", "Match history", "Check recent rounds, team scores, and your match history.", "/matches", "View matches →") + "</div>"));
        return Show(c, "Player hub", "home", body.ToString());
    }

    private static string Feature(string step, string title, string description, string url, string label) =>
        $"<article class=\"feature-card\"><span class=\"step\">{H(step)}</span><h3>{H(title)}</h3><p>{H(description)}</p>{Link(url, label, "text-link")}</article>";

    private IResult Download(HttpContext c) => Show(c, "Get OpenSCCT", "download",
        PageHeader("GET STARTED", "Download OpenSCCT", "The launcher installs the game, downloads the files you need, and keeps everything up to date.") +
        "<div class=\"grid-two\"><section class=\"panel download-card\"><p class=\"eyebrow\">OPENSCCT FOR WINDOWS</p><h2>Windows launcher</h2>" +
        "<p class=\"lead\">Put the launcher in a folder of your choice. It will guide you through installation and the first game-file download.</p>" +
        Link("https://github.com/Lumbridge/OpenSCCT/releases/latest/download/OpenSCCT.Launcher.exe", "Download launcher ↗", "button") +
        "<span class=\"platform\">Windows 64-bit · Automatic game updates</span><p class=\"account-side-note\">You do not need your own copy of Splinter Cell: Chaos Theory. " +
        "If you already own it, you can choose to prepare game files from your installation instead.</p>" +
        Link("/changelog", "See what's new →", "text-link") + "</section>" +
        "<section class=\"panel\"><h2>Install and play</h2><ol class=\"download-steps\"><li><strong>Install with the launcher</strong><p>Run OpenSCCT.Launcher.exe and choose Install. Keep the launcher in a writable folder.</p></li>" +
        "<li><strong>Get the game files</strong><p>Follow Download game files. The launcher checks and prepares the files before your first launch.</p></li>" +
        "<li><strong>Start playing</strong><p>Launch the game and open Find a Session to join a game or hub. You can also practice offline. Sign in through Account to save your progress.</p></li></ol></section></div>" +
        Section("Linux & Steam Deck", "<div class=\"panel download-card\"><p class=\"eyebrow\">EXPERIMENTAL · LINUX X86-64</p><h2>Linux and Steam Deck</h2>" +
            "<p class=\"lead\">Install the Flatpak on Linux, or use Steam Deck Desktop Mode. Steam Deck hardware validation is still pending.</p><div class=\"button-row\">" +
            Link("https://github.com/Lumbridge/OpenSCCT/releases/latest/download/OpenSCCT-linux-x86_64.flatpak", "Download Flatpak ↗", "button secondary") +
            Link("https://github.com/Lumbridge/OpenSCCT/blob/main/docs/LINUX.md", "Linux setup guide ↗", "text-link") + "</div></div>") +
        Section("Getting started", "<div class=\"grid-three\">" +
            Feature("01", "Discord is optional", "You can play offline and on open servers without an account. Sign in to claim a name and keep your progress.", "/account", "Set up your account →") +
            Feature("02", "Find an open session", "The server browser shows games, hubs, modes, and available slots. Individual servers may require an account or password.", "/servers", "Browse sessions →") +
            Feature("03", "Help and setup", "The project guide covers setup, controls, and common issues.", "https://github.com/Lumbridge/OpenSCCT#readme", "Read the project guide ↗") + "</div>"));

    private IResult Servers(HttpContext c)
    {
        List<PublicServer> all = LiveServers();
        string q = Query(c, "q"), map = Query(c, "map"), mode = Query(c, "mode"), region = Query(c, "region"), kind = Query(c, "kind");
        bool space = Query(c, "space") == "1";
        IEnumerable<PublicServer> rows = all.Where(s => s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
            (map.Length == 0 || s.Map == map) && (mode.Length == 0 || s.Mode.ToString(CultureInfo.InvariantCulture) == mode) &&
            (region.Length == 0 || s.Region == region) && (kind.Length == 0 || (kind == "hub") == (s.Kind == ServerKind.Hub)) && (!space || s.Players < s.Maximum));
        string filters = "<form method=\"get\" action=\"/servers\" class=\"filters\"><label class=\"grow\">Search sessions<input type=\"search\" name=\"q\" maxlength=\"64\" placeholder=\"Server name\" value=\"" + H(q) + "\"></label>" +
            FilterSelect("kind", "Session", [("", "All sessions"), ("game", "Games"), ("hub", "Hubs")], kind) +
            FilterSelect("map", "Map", new[] { ("", "All maps") }.Concat(all.Select(s => s.Map).Where(m => m.Length > 0).Distinct().Order().Select(m => (m, MapName(m)))), map) +
            FilterSelect("mode", "Mode", new[] { ("", "All modes") }.Concat(all.Where(s => s.Kind != ServerKind.Hub).Select(s => s.Mode).Distinct().Order().Select(m => (m.ToString(CultureInfo.InvariantCulture), ModeName(m)))), mode) +
            FilterSelect("region", "Region", new[] { ("", "All regions") }.Concat(all.Select(s => s.Region).Where(r => r.Length > 0).Distinct().Order().Select(r => (r, r))), region) +
            FilterSelect("space", "Availability", [("", "Any"), ("1", "Has space")], space ? "1" : "") + "<button class=\"button\">Filter</button>" + Link("/servers", "Reset", "text-link") + "</form>";
        return Show(c, "Live sessions", "servers", PageHeader("SERVERS", "Live sessions", "Open Find a Session in the game and look for the server name below.", LiveStatus) +
            filters + "<div data-live=\"servers\">" + ServerTable(rows) + "</div><p class=\"inline-notice\">Only listed sessions appear here. Player counts update as servers report in.</p>");
    }

    private static string FilterSelect(string name, string label, IEnumerable<(string Value, string Label)> values, string selected) =>
        $"<label>{H(label)}<select name=\"{H(name)}\">" + string.Concat(values.Select(v => $"<option value=\"{H(v.Value)}\"{(v.Value == selected ? " selected" : "")}>{H(v.Label)}</option>")) + "</select></label>";

    private static string ServerTable(IEnumerable<PublicServer> source) => Table(["Session", "Map / mode", "Players", "Status", "Access"], source.Select(s => new[]
    {
        H(s.Name) + "<span class=\"table-sub\">" + H(s.Kind == ServerKind.Hub ? "Community hub" : s.Trusted ? "Trusted game server" : "Community game server") + (s.Region.Length > 0 ? " · " + H(s.Region) : "") + "</span>",
        H(MapName(s.Map)) + "<span class=\"table-sub\">" + H(s.Kind == ServerKind.Hub ? "Social hub" : ModeName(s.Mode)) + "</span>",
        $"<span class=\"numeric\">{s.Players} <span class=\"muted\">/ {s.Maximum}</span></span>",
        Badge(s.Phase switch { SessionPhase.Match => "In round", SessionPhase.Loading => "Loading", SessionPhase.Results => "Results", _ => "Lobby" }, s.Phase == SessionPhase.Match ? "ok" : ""),
        (s.Password ? Badge("Password", "warn") + " " : "") + (s.Accounts ? Badge("Account needed") : Badge("Guests welcome")),
    }), "No sessions match right now. Try clearing the filters or check back in a moment.");

    private IResult Players(HttpContext c)
    {
        string q = Query(c, "q"); int page = PageOf(c);
        (List<PublicPlayerRow> rows, int total) = _store.PublicPlayers(q, "name", page, PageSize);
        string body = PageHeader("COMMUNITY", "Players", "Browse public player profiles, stats, and match history.") +
            "<form method=\"get\" action=\"/players\" class=\"filters\"><label class=\"grow\">Player name<input type=\"search\" name=\"q\" maxlength=\"64\" placeholder=\"Search public player profiles\" value=\"" + H(q) +
            "\"></label><button class=\"button\">Search</button>" + Link("/players", "Reset", "text-link") + "</form><p class=\"muted\">" + N(total) + " public player" + (total == 1 ? "" : "s") + "</p>" +
            PlayerTable(rows, false, page) + Pager("/players?q=" + U(q), page, (page + 1) * PageSize < total);
        return Show(c, "Players", "players", body);
    }

    private IResult Leaderboards(HttpContext c)
    {
        string sort = Query(c, "sort") switch { "wins" => "wins", "kills" => "kills", _ => "xp" };
        int page = PageOf(c);
        (List<PublicPlayerRow> rows, int total) = _store.PublicPlayers(null, sort, page, PageSize);
        return Show(c, "Leaderboards", "leaderboards", PageHeader("ALL TIME", "Leaderboards", "Lifetime XP, wins, and kills from counted rounds on trusted servers.") +
            "<form method=\"get\" action=\"/leaderboards\" class=\"filters\">" + FilterSelect("sort", "Rank players by", [("xp", "Lifetime XP"), ("wins", "Wins"), ("kills", "Kills")], sort) +
            "<button class=\"button\">Apply</button><span class=\"field-help\">Public profiles · All time</span></form>" + PlayerTable(rows, true, page) +
            Pager("/leaderboards?sort=" + sort, page, (page + 1) * PageSize < total));
    }

    private static string PlayerTable(IEnumerable<PublicPlayerRow> rows, bool ranks, int page)
    {
        int place = page * PageSize;
        return Table(ranks ? ["Rank", "Player", "Level", "Lifetime XP", "Wins", "Kills"] : ["Player", "Level", "Rounds", "Lifetime XP", "Wins", "Kills"], rows.Select(r =>
        {
            string name = Link("/players/" + r.Id.ToString(CultureInfo.InvariantCulture), r.Name);
            string rank = Rank(PlayerRank.FromXp(r.Prestige, r.Xp));
            return ranks ? new[] { "<span class=\"rank-number\">" + (++place).ToString(CultureInfo.InvariantCulture) + "</span>", name, rank, N(r.TotalXp), N(r.Wins), N(r.Kills) }
                : new[] { name, rank, N(r.Matches), N(r.TotalXp), N(r.Wins), N(r.Kills) };
        }), "No public players match this view yet.");
    }

    private IResult Profile(HttpContext c, long id)
    {
        if (!_store.IsProfilePublic(id) || _store.FindById(id) is not { } player) return NotFound(c);
        // Recheck visibility after constructing the projection so a concurrent privacy change is respected.
        string body = ProfileBody(player, false, PageOf(c));
        return _store.IsProfilePublic(id) ? Show(c, player.Name ?? "Player", "players", body) : NotFound(c);
    }

    private string ProfileBody(AccountRow account, bool owner, int page = 0)
    {
        AccountProgress progress = _store.Progress(account.Id);
        AccountStats stats = progress.Stats;
        ProgressInfo level = _store.ProgressInfo(account.Id);
        PlayerRank rank = PlayerRank.FromXp(level.Prestige, level.Xp);
        Dictionary<string, long> earned = _store.Earned(account.Id);
        var body = new StringBuilder("<div class=\"profile-heading\"><div class=\"profile-avatar\" aria-hidden=\"true\">");
        string name = account.Name ?? "New player";
        body.Append(H(name[..1].ToUpperInvariant())).Append("</div><div><p class=\"eyebrow\">").Append(owner ? "YOUR CAREER" : "PLAYER PROFILE").Append(owner ? "</p><h2>" : "</p><h1>").Append(H(name)).Append(owner ? "</h2><p>" : "</h1><p>")
            .Append(Rank(rank)).Append(" · ").Append(N(level.TotalXp)).Append(" lifetime XP</p></div>");
        if (owner) body.Append("<div class=\"button-row\">").Append(Link("/account/appearance", "Customize appearance →", "button secondary compact")).Append("</div>");
        body.Append("</div><div class=\"stats-grid\">")
            .Append(Stat("Rounds played", N(stats.Matches), "Counted rounds"))
            .Append(Stat("Wins", N((long)stats.SpyWins + stats.MercWins), Ratio((long)stats.SpyWins + stats.MercWins, stats.Matches, true) + " win rate"))
            .Append(Stat("Kills / deaths", N(stats.Kills) + " / " + N(stats.Deaths), Ratio(stats.Kills, stats.Deaths) + " K/D"))
            .Append(Stat("Achievements", N(earned.Count) + " / " + N(Achievements.All.Count), N(_store.Unlocked(account.Id).Count) + " cosmetics unlocked"))
            .Append("</div>");
        body.Append(Section("Career detail", CareerDetails(progress, level)));
        List<MatchRow> matches = _store.Matches(page, PageSize, account.Id, lookAhead: true);
        string matchesUrl = owner ? "/account" : "/players/" + account.Id.ToString(CultureInfo.InvariantCulture);
        body.Append(Section("Recent matches", MatchTable(matches.Take(PageSize)) + Pager(matchesUrl, page, matches.Count > PageSize)));
        var achievements = new StringBuilder();
        foreach (IGrouping<AchievementCategory, Achievement> group in Achievements.All.GroupBy(a => a.Category))
        {
            int count = group.Count(a => earned.ContainsKey(a.Id));
            achievements.Append("<details").Append(group.Key == AchievementCategory.General ? " open" : "").Append("><summary>").Append(H(group.Key.ToString())).Append(" · ").Append(count).Append(" / ").Append(group.Count()).Append(" earned</summary><div class=\"achievement-grid\">");
            foreach (Achievement achievement in group)
            {
                bool done = earned.TryGetValue(achievement.Id, out long at);
                (int current, int target) = achievement.Goal(progress);
                target = Math.Max(1, target); current = done ? target : Math.Clamp(current, 0, target);
                achievements.Append("<article class=\"achievement").Append(done ? " earned" : "").Append("\"><p class=\"category\">").Append(done ? "ACHIEVEMENT EARNED" : "IN PROGRESS")
                    .Append("</p><h3>").Append(H(achievement.Title)).Append("</h3><p>").Append(H(achievement.Description)).Append("</p><progress max=\"").Append(target).Append("\" value=\"").Append(current)
                    .Append("\" aria-label=\"").Append(H(achievement.Title)).Append("\"></progress><div class=\"progress-label\"><span>").Append(N(current)).Append(" / ").Append(N(target)).Append("</span><span>")
                    .Append(done ? DateTimeOffset.FromUnixTimeSeconds(at).ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "Not yet earned").Append("</span></div>");
                if (achievement.Rewards.Count > 0) achievements.Append("<div class=\"reward\">Unlocks ").Append(H(CosmeticCatalog.DescribeAll(achievement.Rewards))).Append("</div>");
                achievements.Append("</article>");
            }
            achievements.Append("</div></details>");
        }
        body.Append(Section("Achievements", achievements.ToString()));
        var unlocks = new StringBuilder("<div class=\"cosmetic-list\">");
        foreach (string id in _store.Unlocked(account.Id))
            if (CosmeticCatalog.Find(id) is { } item) unlocks.Append("<span class=\"cosmetic-chip\">").Append(Swatch(item)).Append(H(item.Name)).Append("</span>");
        unlocks.Append("</div>");
        body.Append(Section("Unlocked cosmetics", unlocks.ToString()));
        string titles = string.Concat(level.Titles.Select(id => TitleCatalog.Find(id)).OfType<TitleItem>()
            .Select(title => "<span class=\"cosmetic-chip\">" + H(title.Text) + "</span>"));
        body.Append(Section("Unlocked titles", titles.Length > 0 ? "<div class=\"cosmetic-list\">" + titles + "</div>" :
            "<p class=\"muted\">Titles unlock through levels, prestige, and achievements.</p>"));
        return body.ToString();
    }

    private static string CareerDetails(AccountProgress progress, ProgressInfo level)
    {
        var groups = new StringBuilder("<div class=\"grid-two\"><div class=\"panel\"><h3>Round performance</h3><div class=\"metric-list\">");
        foreach ((string label, string value) in new[] { ("Spy wins", N(progress.Stats.SpyWins)), ("Mercenary wins", N(progress.Stats.MercWins)),
            ("Losses", N(progress.Stats.Losses)), ("Kills per round", Ratio(progress.Stats.Kills, progress.Stats.Matches)),
            ("Best level reached", N(level.BestLevel)), ("Current prestige", N(level.Prestige)) }) groups.Append(Metric(label, value));
        groups.Append("</div></div><div class=\"panel\"><h3>Detailed stats</h3><div class=\"metric-list\">");
        int displayed = 0;
        foreach ((string key, string label) in CareerCounters)
            if (progress.Feats.TryGetValue(key, out int value)) { groups.Append(Metric(label, N(value))); displayed++; }
        if (displayed == 0) groups.Append("<p class=\"muted\">Detailed stats appear as you play counted rounds.</p>");
        groups.Append("</div></div></div>");
        var weapons = new[] { (Id: "ar", Name: "Assault rifle"), (Id: "smg", Name: "SMG"), (Id: "shotgun", Name: "Shotgun") }
            .Concat(Sidearms.All.Select(g => (g.Id, g.Name))).ToArray();
        if (weapons.Any(g => progress.Feats.ContainsKey(FeatKeys.GunKills(g.Id)) || progress.Feats.ContainsKey(FeatKeys.GunHeadshots(g.Id))))
            groups.Append("<div class=\"section\">").Append(Table(["Weapon", "Kills", "Headshot kills"], weapons.Select(g => new[]
                { H(g.Name), N(progress.Feat(FeatKeys.GunKills(g.Id))), N(progress.Feat(FeatKeys.GunHeadshots(g.Id))) }))).Append("</div>");
        if (progress.MapWins.Count > 0)
            groups.Append("<div class=\"section\">").Append(Table(["Map", "Spy wins", "Mercenary wins"], progress.MapWins.Keys.Select(k => k.Map).Distinct().Order().Select(map => new[]
                { H(MapName(map)), N(progress.MapWin(map, 0)), N(progress.MapWin(map, 1)) }))).Append("</div>");
        return groups.ToString();
    }

    private static string Metric(string label, string value) => "<div><span>" + H(label) + "</span><strong>" + H(value) + "</strong></div>";
    private static readonly (string Key, string Label)[] CareerCounters =
    [
        (FeatKeys.SpyGrabKills, "Spy · Grab kills"), (FeatKeys.SpyNeckBreaks, "Spy · Neck breaks"), (FeatKeys.SpyChokes, "Spy · Chokes"),
        (FeatKeys.SpyDropKills, "Spy · Drop kills"), (FeatKeys.SpyObjectives, "Spy · Objectives completed"), (FeatKeys.SpyShockerHeadshots, "Spy · Shocker headshots"),
        (FeatKeys.SpyLedgeGrabs, "Spy · Ledge grabs"), (FeatKeys.MercSniperKills, "Merc · Scoped kills"), (FeatKeys.MercHeadshots, "Merc · Headshot kills"),
        (FeatKeys.MercTerminateKills, "Merc · Termination kills"), (FeatKeys.MercHipfireKills, "Merc · Hip-fire kills"), (FeatKeys.MercChargeHits, "Merc · Charge hits"),
        (FeatKeys.MercBerserkHits, "Merc · Berserk hits"), (FeatKeys.SpyStickyKos, "Sticky camera knockouts"), (FeatKeys.SpySmokeKos, "Smoke knockouts"),
        (FeatKeys.SpyStickyCamUses, "Sticky camera uses"), (FeatKeys.SpyChaffJams, "Mercenaries jammed by chaff"), (FeatKeys.SpyMineKills, "Spy mine kills"),
        (FeatKeys.SpyBulletHits, "Tracking bullet hits"), (FeatKeys.SpyFlashes, "Mercenaries flashed"), (FeatKeys.MercGrenadeKills, "Grenade kills"),
        (FeatKeys.MercMineKills, "Merc mine kills"), (FeatKeys.MercMinesPlaced, "Mines placed"), (FeatKeys.MercMarks, "Tracking marks"), (FeatKeys.MercTaserHits, "Taser hits"),
    ];

    private IResult Matches(HttpContext c)
    {
        int page = PageOf(c);
        List<MatchRow> matches = _store.Matches(page, PageSize, lookAhead: true);
        return Show(c, "Recent matches", "matches", PageHeader("MATCH HISTORY", "Recent matches", "Results from trusted servers. Counted rounds add to your stats and progression.", LiveStatus) +
            "<div data-live=\"matches\">" + MatchTable(matches.Take(PageSize)) + Pager("/matches", page, matches.Count > PageSize) + "</div>");
    }

    private static string MatchTable(IEnumerable<MatchRow> matches) => Table(["Round", "Server", "Winner", "Duration", "Result"], matches.Select(m => new[]
    {
        Link("/matches/" + m.Id.ToString(CultureInfo.InvariantCulture), MapName(m.Map)) + "<span class=\"table-sub\">" + H(ModeName(m.Mode)) + " · " + Time(m.Time) + "</span>",
        H(m.ServerName), Badge(Winner(m.Winner), m.Winner == 0 ? "spy" : m.Winner == 1 ? "merc" : ""), H(Duration(m.DurationSeconds)),
        Badge(m.Counted ? "Counted" : "Uncounted", m.Counted ? "ok" : ""),
    }), "No rounds have been recorded yet.");

    private IResult Match(HttpContext c, long id)
    {
        if (_store.Match(id) is not { } found) return NotFound(c, "Round unavailable");
        MatchRow m = found.Match;
        long? owner = Current(c)?.Account.Id;
        string body = PageHeader("ROUND REPORT", MapName(m.Map), ModeName(m.Mode) + " · " + m.ServerName, Link("/matches", "← All matches", "button secondary compact")) +
            "<div class=\"stats-grid\">" + Stat("Winner", Winner(m.Winner)) + Stat("Duration", Duration(m.DurationSeconds)) +
            Stat("Objectives", m.ObjectivesTaken + " / " + m.ObjectivesNeeded) + Stat("Progression", m.Counted ? "Counted" : "Uncounted", AccountStore.Format(m.Time)) + "</div>";
        foreach (int side in found.Rows.Select(r => r.Team).Distinct().Order())
            body += Section(side == 0 ? "Spies" : side == 1 ? "Mercenaries" : "Other participants", Table(["Player", "Score", "Kills", "Deaths"],
                found.Rows.Where(r => r.Team == side).Select(r => new[] { MatchIdentity(r, owner), N(r.Score), N(r.Kills), N(r.Deaths) })));
        body += "<p class=\"inline-notice\">Players with private or hidden profiles appear anonymously. This does not change the original game recording.</p>";
        return Show(c, "Round " + id, "matches", body);
    }

    private string MatchIdentity(MatchPlayerRow row, long? owner)
    {
        if (row.Bot) return "Bot " + Badge("AI");
        if (row.AccountId is not { } id) return "Guest player";
        bool visible = _store.IsProfilePublic(id);
        if (!visible && id != owner) return "<span class=\"muted\">Private player</span>";
        AccountRow? account = _store.FindById(id);
        if (account?.Name is not { } name) return "Guest player";
        return visible ? Link("/players/" + id.ToString(CultureInfo.InvariantCulture), name) : H(name) + " " + Badge("You · private");
    }

    private static string Rank(PlayerRank rank) => "<span class=\"rank-mark\">" + H(rank.IsMaster ? "Master · Level " + rank.Level : rank.Prestige > 0 ? "P" + rank.Prestige + " · Level " + rank.Level : "Level " + rank.Level) + "</span>";
    private static string Ratio(long numerator, long denominator, bool percentage = false) => denominator <= 0 ? "—" :
        ((double)numerator / denominator * (percentage ? 100 : 1)).ToString(percentage ? "0.0" : "0.00", CultureInfo.InvariantCulture) + (percentage ? "%" : "");
    private static string ModeName(int mode) => mode switch { 5 => "Story", 4 => "Disk Hunt", 6 => "Deathmatch", 7 => "Merc Deathmatch", 1 => "Extraction", 2 => "Sabotage", 3 => "Neutralization", _ => "Custom mode" };
    private static string Winner(int team) => team switch { 0 => "Spies", 1 => "Mercenaries", _ => "No winner" };
    private static string MapName(string map) => CosmeticCatalog.Maps.FirstOrDefault(m => string.Equals(m.Code, CosmeticCatalog.BaseMap(map), StringComparison.OrdinalIgnoreCase)).Name ?? (map.Length > 0 ? map : "Community hub");
}
