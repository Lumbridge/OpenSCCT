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
    private sealed record PublicServer(string Id, string Name, string Map, int Mode, string Region, int Players, int Maximum,
        bool Password, bool Accounts, bool Trusted, ServerKind Kind, SessionPhase Phase);

    private List<PublicServer> LiveServers()
    {
        lock (_registry.Gate)
        {
            return _registry.Snapshot().Where(r => r.Last.Listed).Select(r =>
            {
                HeartbeatRequest h = r.Last;
                return new PublicServer(r.Id, h.Name, h.Map, h.Mode, h.Region, h.Players, h.MaxPlayers, h.Password, h.RequireAccounts, r.Trusted, h.Kind, h.Phase);
            }).OrderByDescending(r => r.Trusted).ThenByDescending(r => r.Players).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    private IResult Home(HttpContext c)
    {
        List<PublicServer> servers = LiveServers();
        Dictionary<string, string> settings = _store.Settings();
        string welcome = settings.GetValueOrDefault("site.welcome", DefaultWelcome);
        string announcement = settings.GetValueOrDefault("site.announcement", string.Empty);
        var body = new StringBuilder();
        if (announcement.Length > 0) body.Append("<aside class=\"announcement\"><strong>Announcement</strong><p>").Append(H(announcement)).Append("</p></aside>");
        body.Append("<section class=\"hero community-hero\"><div><p class=\"eyebrow\">OPENSCCT</p><h1>Spies vs.<br><span>Mercenaries</span></h1><p class=\"lead\">")
            .Append(H(welcome)).Append("</p><div class=\"button-row\">").Append(Link("/download", "Download", "button"))
            .Append(Link("/servers", "Servers", "button secondary"))
            .Append("</div><p class=\"hero-note\">Windows &amp; Linux · No original game required</p><p class=\"hero-guide\">")
            .Append(Link("/wiki/getting-started", "Getting started", "text-link")).Append("</p></div>")
            .Append(HomeShowcase()).Append("</section>")
            .Append("<div data-live=\"home-stats\"><div class=\"stats-grid\">")
            .Append(Stat("Players online", N(servers.Sum(s => s.Players)), "In the games and hubs listed here"))
            .Append(Stat("Games and hubs", N(servers.Count), "Currently online"))
            .Append(Stat("Player profiles", N(_store.PublicPlayers(null, "name", 0, 1).Total), "Public profiles you can browse"))
            .Append(Stat("Rounds today", N(_store.CountMatches(_store.Now / 86400 * 86400)), "Results received by the website"))
            .Append("</div></div>");
        body.Append(Section("Roles", "<div class=\"grid-two role-introductions\">" +
            "<article class=\"panel role-introduction spy-introduction\"><p class=\"eyebrow\">THIRD PERSON</p><h3>Spies</h3><p>Spies use a built-in cloak, gadgets and traversal routes to slip past defenders. In Story mode, they complete the map's objectives.</p>" +
            Link("/wiki/spy", "Spy guide", "text-link") + "</article>" +
            "<article class=\"panel role-introduction merc-introduction\"><p class=\"eyebrow\">FIRST PERSON</p><h3>Mercenaries</h3><p>Mercenaries use firearms, detection equipment and traps to defend objectives in Story mode. Hosts can enable a dedicated sidearm slot.</p>" +
            Link("/wiki/mercenary", "Mercenary guide", "text-link") + "</article></div>"));
        body.Append(Section("Games and hubs online", "<div data-live=\"home-servers\">" + ServerTable(servers.Take(4)) + "</div>",
            "<div class=\"button-row\">" + LiveStatus + Link("/servers", "All games and hubs", "text-link") + "</div>"));
        body.Append(Section("Getting started", "<div class=\"grid-three\">" +
            Feature("01", "Install", "Download the launcher for your platform. It installs the game and content and checks for updates.", "/download", "Downloads") +
            Feature("02", "Practice", "Offline bots and practice settings are available for testing controls, gadgets and map objectives.", "/wiki/practice", "Tutorials and practice") +
            Feature("03", "Join a session", "The server browser lists games and social hubs. Sign in inside the game when a server requires an account.", "/servers", "Games and hubs") + "</div>"));
        body.Append(ImprovementsPreview());
        body.Append(Section("Players and results", "<div class=\"grid-three\">" +
            Feature("01", "Account", "View your level and achievements, choose your appearance, and manage your privacy.", "/account", "Account") +
            Feature("02", "Leaderboards", "Rank public player profiles by XP, wins, combat stats or objectives completed.", "/leaderboards", "Leaderboards") +
            Feature("03", "Match results", "Recorded rounds include the winner, participating players and their scores.", "/matches", "Match history") + "</div>"));
        body.Append(Section("Documentation", "<div class=\"panel community-wiki-callout\"><div><p class=\"eyebrow\">WIKI</p><h3>OpenSCCT wiki</h3><p>Articles about gameplay, server hosting and the map editor.</p></div>" + Link("/wiki", "Wiki", "button secondary") + "</div>"));
        return Show(c, "Home", "home", body.ToString());
    }

    private static string Feature(string step, string title, string description, string url, string label) =>
        $"<article class=\"feature-card\"><span class=\"step\">{H(step)}</span><h3>{H(title)}</h3><p>{H(description)}</p>{Link(url, label, "text-link")}</article>";

    private IResult Servers(HttpContext c)
    {
        List<PublicServer> all = LiveServers();
        string q = Query(c, "q"), map = Query(c, "map"), mode = Query(c, "mode"), region = Query(c, "region"), kind = Query(c, "kind");
        bool space = Query(c, "space") == "1";
        IEnumerable<PublicServer> rows = all.Where(s => s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
            (map.Length == 0 || s.Map == map) && (mode.Length == 0 || s.Mode.ToString(CultureInfo.InvariantCulture) == mode) &&
            (region.Length == 0 || s.Region == region) && (kind.Length == 0 || (kind == "hub") == (s.Kind == ServerKind.Hub)) && (!space || s.Players < s.Maximum));
        string filters = "<form method=\"get\" action=\"/servers\" class=\"filters\"><label class=\"grow\">Server name<input type=\"search\" name=\"q\" maxlength=\"64\" placeholder=\"Search by name\" value=\"" + H(q) + "\"></label>" +
            FilterSelect("kind", "Type", [("", "Games and hubs"), ("game", "Games"), ("hub", "Social hubs")], kind) +
            FilterSelect("map", "Map", new[] { ("", "All maps") }.Concat(all.Select(s => s.Map).Where(m => m.Length > 0).Distinct().Order().Select(m => (m, MapName(m)))), map) +
            FilterSelect("mode", "Mode", new[] { ("", "All modes") }.Concat(all.Where(s => s.Kind != ServerKind.Hub).Select(s => s.Mode).Distinct().Order().Select(m => (m.ToString(CultureInfo.InvariantCulture), ModeName(m)))), mode) +
            FilterSelect("region", "Region", new[] { ("", "All regions") }.Concat(all.Select(s => s.Region).Where(r => r.Length > 0).Distinct().Order().Select(r => (r, r))), region) +
            FilterSelect("space", "Player spaces", [("", "Show all"), ("1", "Not full")], space ? "1" : "") + "<button class=\"button\">Apply filters</button>" + Link("/servers", "Clear filters", "text-link") + "</form>";
        return Show(c, "Games and hubs", "servers", PageHeader("SERVERS", "Games and hubs online", "Choose Join to open the game. Hubs are social spaces where you can meet other players between matches.", LiveStatus) +
            filters + "<aside id=\"join-status\" class=\"notice\" role=\"status\" hidden><strong>Opening OpenSCCT…</strong> " +
            "Allow your browser to open the launcher. If nothing opens, " + Link("/download", "install or update the launcher") +
            ", run it once, then try Join again. Sign in inside the game if prompted.</aside>" +
            "<div data-live=\"servers\">" + ServerTable(rows, join: true) + "</div><p class=\"inline-notice\">This list shows servers that have chosen to appear publicly. Player counts refresh automatically and may take a moment to catch up.</p>" +
            Section("Joining from your browser", "<div class=\"panel\"><p>On Windows, install or update the launcher and open it once to enable Join links. " +
                "On Linux, run install.sh from the native launcher archive to enable Join links, or install the Flatpak. On Steam Deck, start in Desktop Mode. Your browser may ask permission to open OpenSCCT. Website Join links use your Discord sign-in inside the game; signing in on the website does not sign you into the game.</p>" +
                "<p>If the game is already running, Join uses that window. If you are in a match, it waits until you return to the main menu. Enter any server password in the game.</p>" +
                "<p class=\"account-side-note\">You can also browse games through Find a Session or visit hubs through Hub in the game.</p>" +
                Link("/download", "Downloads and updates", "text-link") + "</div>"));
    }

    private static string FilterSelect(string name, string label, IEnumerable<(string Value, string Label)> values, string selected) =>
        $"<label>{H(label)}<select name=\"{H(name)}\">" + string.Concat(values.Select(v => $"<option value=\"{H(v.Value)}\"{(v.Value == selected ? " selected" : "")}>{H(v.Label)}</option>")) + "</select></label>";

    private static string ServerTable(IEnumerable<PublicServer> source, bool join = false) => Table(
        join ? ["Server", "Map / mode", "Players", "Status", "Requirements", "Join"] : ["Server", "Map / mode", "Players", "Status", "Requirements"], source.Select(s =>
    {
        var cells = new List<string>
        {
        H(s.Name) + "<span class=\"table-sub\">" + H(s.Kind == ServerKind.Hub ? "Social hub" : s.Trusted ? "Records match results" : "Community game server") + (s.Region.Length > 0 ? " · " + H(s.Region) : "") + "</span>",
        H(MapName(s.Map)) + "<span class=\"table-sub\">" + H(s.Kind == ServerKind.Hub ? "Social hub" : ModeName(s.Mode)) + "</span>",
        $"<span class=\"numeric\">{s.Players} <span class=\"muted\">/ {s.Maximum}</span></span>",
        Badge(s.Phase switch { SessionPhase.Match => "Playing", SessionPhase.Loading => "Loading", SessionPhase.Results => "Viewing results", _ => "In lobby" }, s.Phase == SessionPhase.Match ? "ok" : ""),
        (s.Password ? Badge("Password needed", "warn") + " " : "") + (s.Accounts ? Badge("Account needed") : Badge("Guests welcome")),
        };
        if (join) cells.Add(s.Maximum > 0 && s.Players >= s.Maximum ? Badge("Full") :
            "<a class=\"button secondary compact\" data-server-join href=\"openscct://join/" + U(s.Id) + "\" aria-label=\"" + H("Join " + s.Name + " in OpenSCCT") + "\">Join ↗</a>");
        return cells.ToArray();
    }), "No games or hubs are listed for this view. Clear any filters or check again later.");

    private IResult Players(HttpContext c)
    {
        string q = Query(c, "q"); int page = PageOf(c);
        (List<PublicPlayerRow> rows, int total) = _store.PublicPlayers(q, "name", page, PageSize);
        string body = PageHeader("COMMUNITY", "Players", "Browse public player profiles, stats, and match history.") +
            "<form method=\"get\" action=\"/players\" class=\"filters\"><label class=\"grow\">Player name<input type=\"search\" name=\"q\" maxlength=\"64\" placeholder=\"Search public player profiles\" value=\"" + H(q) +
            "\"></label><button class=\"button\">Search</button>" + Link("/players", "Clear search", "text-link") + "</form><p class=\"muted\">" + N(total) + " public player" + (total == 1 ? "" : "s") + "</p>" +
            PlayerTable(rows, false, page) + Pager("/players?q=" + U(q), page, (page + 1) * PageSize < total);
        return Show(c, "Players", "players", body);
    }

    private IResult Leaderboards(HttpContext c)
    {
        string sort = Query(c, "sort");
        if (!LeaderboardMetrics.Any(m => m.Key == sort)) sort = "xp";
        int page = PageOf(c);
        (List<PublicPlayerRow> rows, int total) = _store.PublicPlayers(null, sort, page, PageSize);
        string description = LeaderboardMetrics.First(m => m.Key == sort).Description;
        return Show(c, "Leaderboards", "leaderboards", PageHeader("ALL TIME", "Leaderboards", "Public player rankings by lifetime XP, wins, rounds played, kills, objectives, headshot kills and damage dealt.", Link("/players", "Player search", "button secondary compact")) +
            "<form method=\"get\" action=\"/leaderboards\" class=\"filters\">" + FilterSelect("sort", "Rank players by", LeaderboardMetrics.Select(m => (m.Key, m.Label)), sort) +
            "<button class=\"button\">Apply</button><span class=\"field-help\">Public profiles · Lifetime totals</span></form>" +
            "<div class=\"leaderboard-summary\"><p><strong>" + H(LeaderboardMetrics.First(m => m.Key == sort).Label) + "</strong> · " + H(description) + "</p><span>" + N(total) + " eligible player" + (total == 1 ? "" : "s") + "</span></div>" +
            LeaderboardTable(rows, sort, page) + Pager("/leaderboards?sort=" + U(sort), page, (page + 1) * PageSize < total) +
            Section("How these rankings work", "<div class=\"grid-two\"><div class=\"panel\"><h3>Rounds that count</h3><p>Stats come from qualifying rounds reported by approved servers, with a winner and at least one signed-in player on each team. Offline practice does not add to these totals.</p><p class=\"account-side-note\">Win-rate rankings need at least 10 counted rounds. Detailed counters reflect the history recorded for that stat; older rounds may not include every counter.</p>" + Link("/wiki/progression-and-stats", "Progression and stats", "text-link") + "</div><div class=\"panel\"><h3>Profile visibility</h3><p>Named public profiles appear here. Private, hidden and actively banned accounts are excluded. Lifetime XP is retained through prestige.</p><p class=\"account-side-note\">K/D means kills per death. A dash means there is no denominator yet. Ties use XP, wins or rounds played, followed by account order.</p>" + Link("/account/privacy", "Profile privacy", "text-link") + "</div></div>"));
    }

    private static readonly (string Key, string Label, string Description)[] LeaderboardMetrics =
    [
        ("xp", "Total XP", "Lifetime experience, including progress through prestige."),
        ("wins", "Wins", "Victories across both teams."),
        ("win-rate", "Win rate", "Percentage of counted rounds won · minimum 10 rounds."),
        ("rounds", "Rounds played", "Completed rounds included in career stats."),
        ("kills", "Kills", "Career kills from counted rounds."),
        ("spy-wins", "Spy wins", "Victories earned while playing as a spy."),
        ("merc-wins", "Mercenary wins", "Victories earned while playing as a mercenary."),
        ("objectives", "Spy objectives", "Objectives completed by spies."),
        ("headshots", "Mercenary headshot kills", "Recorded mercenary headshot kills."),
        ("damage", "Damage dealt", "Recorded career damage dealt."),
    ];

    private static string LeaderboardTable(IEnumerable<PublicPlayerRow> rows, string sort, int page)
    {
        bool extra = sort is "spy-wins" or "merc-wins" or "objectives" or "headshots" or "damage";
        var columns = new List<(string Key, string Label, Func<PublicPlayerRow, string> Value)>
        {
            ("xp", "Total XP", r => N(r.TotalXp)), ("rounds", "Rounds", r => N(r.Matches)), ("wins", "Wins", r => N(r.Wins)),
            ("win-rate", "Win rate", r => Ratio(r.Wins, r.Matches, true)), ("kills", "Kills", r => N(r.Kills)),
            ("deaths", "Deaths", r => N(r.Deaths)), ("kd", "K/D", r => Ratio(r.Kills, r.Deaths)),
        };
        if (extra) columns.Insert(0, (sort, LeaderboardMetrics.First(m => m.Key == sort).Label, r => N(sort switch
        {
            "spy-wins" => r.SpyWins, "merc-wins" => r.MercWins, "objectives" => r.Objectives, "headshots" => r.Headshots, _ => r.Damage,
        })));
        else
        {
            var selected = columns.First(x => x.Key == sort);
            columns.Remove(selected);
            columns.Insert(0, selected);
        }
        int place = page * PageSize;
        string[] headers = new[] { "Rank", "Player" }.Concat(columns.Select(x => x.Label)).ToArray();
        return "<div class=\"leaderboard-table\">" + Table(headers, rows.Select(r => new[]
        {
            "<span class=\"rank-number\">" + (++place).ToString(CultureInfo.InvariantCulture) + "</span>",
            Link("/players/" + r.Id.ToString(CultureInfo.InvariantCulture), r.Name) + "<span class=\"table-sub\">" + Rank(PlayerRank.FromXp(r.Prestige, r.Xp)) + "</span>",
        }.Concat(columns.Select(x => "<span class=\"numeric\">" + H(x.Value(r)) + "</span>")).ToArray()),
            sort == "win-rate" ? "No public players have completed the 10 counted rounds needed for this ranking yet. Try another statistic." : "No eligible public profiles yet. Play a qualifying round and keep your profile public to appear here.") + "</div>";
    }

    private static string PlayerTable(IEnumerable<PublicPlayerRow> rows, bool ranks, int page)
    {
        int place = page * PageSize;
        return Table(ranks ? ["Rank", "Player", "Level", "Total XP", "Wins", "Kills"] : ["Player", "Level", "Rounds", "Total XP", "Wins", "Kills"], rows.Select(r =>
        {
            string name = Link("/players/" + r.Id.ToString(CultureInfo.InvariantCulture), r.Name);
            string rank = Rank(PlayerRank.FromXp(r.Prestige, r.Xp));
            return ranks ? new[] { "<span class=\"rank-number\">" + (++place).ToString(CultureInfo.InvariantCulture) + "</span>", name, rank, N(r.TotalXp), N(r.Wins), N(r.Kills) }
                : new[] { name, rank, N(r.Matches), N(r.TotalXp), N(r.Wins), N(r.Kills) };
        }), "No public profiles found. If you searched for a name, try a shorter spelling or clear the search.");
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
        body.Append(H(name[..1].ToUpperInvariant())).Append("</div><div><p class=\"eyebrow\">").Append(owner ? "YOUR STATS" : "PLAYER PROFILE").Append(owner ? "</p><h2>" : "</p><h1>").Append(H(name)).Append(owner ? "</h2><p>" : "</h1><p>")
            .Append(Rank(rank)).Append(" · ").Append(N(level.TotalXp)).Append(" total experience points (XP)</p></div>");
        if (owner) body.Append("<div class=\"button-row\">").Append(Link("/account/appearance", "Appearance", "button secondary compact")).Append("</div>");
        body.Append("</div><div class=\"stats-grid\">")
            .Append(Stat("Rounds played", N(stats.Matches), "Rounds included in these stats"))
            .Append(Stat("Wins", N((long)stats.SpyWins + stats.MercWins), Ratio((long)stats.SpyWins + stats.MercWins, stats.Matches, true) + " win rate"))
            .Append(Stat("Kills / deaths", N(stats.Kills) + " / " + N(stats.Deaths), Ratio(stats.Kills, stats.Deaths) + " kills per death"))
            .Append(Stat("Achievements", N(earned.Count) + " / " + N(Achievements.All.Count), N(_store.Unlocked(account.Id).Count) + " cosmetics unlocked"))
            .Append("</div>");
        body.Append(Section("Stats by role and weapon", CareerDetails(progress, level)));
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

    private static string RoleWinRate(RoleRoundStats? rounds) => rounds is null ? "—" : Ratio(rounds.Wins, rounds.Matches, true);

    private static string CareerDetails(AccountProgress progress, ProgressInfo level)
    {
        var groups = new StringBuilder("<div class=\"grid-two\"><div class=\"panel\"><h3>Round performance</h3><div class=\"metric-list\">");
        foreach ((string label, string value) in new[] { ("Spy wins", N(progress.Stats.SpyWins)), ("Mercenary wins", N(progress.Stats.MercWins)),
            ("Spy win rate (since tracking began)", RoleWinRate(progress.Stats.SpyRounds)),
            ("Mercenary win rate (since tracking began)", RoleWinRate(progress.Stats.MercRounds)),
            ("Losses", N(progress.Stats.Losses)), ("Kills per round", Ratio(progress.Stats.Kills, progress.Stats.Matches)),
            ("Best level reached", N(level.BestLevel)), ("Current prestige", N(level.Prestige)) }) groups.Append(Metric(label, value));
        groups.Append("</div></div><div class=\"panel\"><h3>Detailed stats</h3><div class=\"metric-list\">");
        int displayed = 0;
        foreach ((string key, string label) in CareerCounters.Concat(CareerStatCounters.All.Select(c => (c.Key, c.Category + " · " + c.Label))))
            if (progress.Feats.TryGetValue(key, out int value)) { groups.Append(Metric(label, N(value))); displayed++; }
        groups.Append(Metric("Accuracy", Ratio(progress.Feat(CareerStatCounters.ShotsHit), progress.Feat(CareerStatCounters.ShotsFired), true)));
        if (displayed == 0) groups.Append("<p class=\"muted\">Detailed stats appear after you play rounds that qualify for stats and XP.</p>");
        groups.Append("</div></div></div>");
        var weapons = new[] { (Id: "ar", Name: "Assault rifle"), (Id: "smg", Name: "Submachine gun"), (Id: "shotgun", Name: "Shotgun") }
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
        (FeatKeys.MercTerminateKills, "Merc · Choke kills"), (FeatKeys.MercHipfireKills, "Merc · Hip-fire kills"), (FeatKeys.MercChargeHits, "Merc · Charge hits"),
        (FeatKeys.MercBerserkHits, "Merc · Berserk hits"), (FeatKeys.SpyStickyKos, "Sticky camera knockouts"), (FeatKeys.SpySmokeKos, "Smoke knockouts"),
        (FeatKeys.SpyStickyCamUses, "Sticky camera uses"), (FeatKeys.SpyChaffJams, "Mercenaries jammed by chaff"), (FeatKeys.SpyMineKills, "Spy mine kills"),
        (FeatKeys.SpyBulletHits, "Tracking bullet hits"), (FeatKeys.SpyFlashes, "Mercenaries flashed"), (FeatKeys.MercGrenadeKills, "Grenade kills"),
        (FeatKeys.MercMineKills, "Merc mine kills"), (FeatKeys.MercMinesPlaced, "Mines placed"), (FeatKeys.MercMarks, "Tracking marks"), (FeatKeys.MercTaserHits, "Taser hits"),
    ];

    private IResult Matches(HttpContext c)
    {
        int page = PageOf(c);
        List<MatchRow> matches = _store.Matches(page, PageSize, lookAhead: true);
        return Show(c, "Recent matches", "matches", PageHeader("MATCH HISTORY", "Recent matches", "Recorded match results. A round adds to player stats and XP when an approved server reports a winner and at least one signed-in player on each team.", LiveStatus) +
            "<div data-live=\"matches\">" + MatchTable(matches.Take(PageSize)) + Pager("/matches", page, matches.Count > PageSize) + "</div>");
    }

    private static string MatchTable(IEnumerable<MatchRow> matches) => Table(["Round", "Server", "Winner", "Duration", "Player stats"], matches.Select(m => new[]
    {
        Link("/matches/" + m.Id.ToString(CultureInfo.InvariantCulture), MapName(m.Map)) + "<span class=\"table-sub\">" + H(ModeName(m.Mode)) + " · " + Time(m.Time) + "</span>",
        H(m.ServerName), Badge(Winner(m.Winner), m.Winner == 0 ? "spy" : m.Winner == 1 ? "merc" : ""), H(Duration(m.DurationSeconds)),
        Badge(m.Counted ? "Included" : "Not included", m.Counted ? "ok" : ""),
    }), "No rounds have been recorded yet.");

    private IResult Match(HttpContext c, long id)
    {
        if (_store.Match(id) is not { } found) return NotFound(c, "Round unavailable");
        MatchRow m = found.Match;
        long? owner = Current(c)?.Account.Id;
        string body = PageHeader("ROUND REPORT", MapName(m.Map), ModeName(m.Mode) + " · " + m.ServerName, Link("/matches", "All matches", "button secondary compact")) +
            "<div class=\"stats-grid\">" + Stat("Winner", Winner(m.Winner)) + Stat("Duration", Duration(m.DurationSeconds)) +
            Stat("Objectives completed", m.ObjectivesTaken + " / " + m.ObjectivesNeeded) + Stat("Stats and XP", m.Counted ? "Included" : "Not included", AccountStore.Format(m.Time)) + "</div>";
        foreach (int side in found.Rows.Select(r => r.Team).Distinct().Order())
            body += Section(side == 0 ? "Spies" : side == 1 ? "Mercenaries" : "Other participants", Table(["Player", "Score", "Kills", "Deaths"],
                found.Rows.Where(r => r.Team == side).Select(r => new[] { MatchIdentity(r, owner), N(r.Score), N(r.Kills), N(r.Deaths) })));
        body += "<p class=\"inline-notice\">Players with private or hidden profiles appear without their names here. This only affects the website; names in saved game replays stay unchanged.</p>";
        return Show(c, "Round " + id, "matches", body);
    }

    private string MatchIdentity(MatchPlayerRow row, long? owner)
    {
        if (row.Bot) return "Computer player " + Badge("Bot");
        if (row.AccountId is not { } id) return "Guest player";
        bool visible = _store.IsProfilePublic(id);
        if (!visible && id != owner) return "<span class=\"muted\">Private player</span>";
        AccountRow? account = _store.FindById(id);
        if (account?.Name is not { } name) return "Guest player";
        return visible ? Link("/players/" + id.ToString(CultureInfo.InvariantCulture), name) : H(name) + " " + Badge("You · private");
    }

    private static string Rank(PlayerRank rank) => "<span class=\"rank-mark\">" + H(rank.IsMaster ? "Master prestige · Level " + rank.Level : rank.Prestige > 0 ? "Prestige " + rank.Prestige + " · Level " + rank.Level : "Level " + rank.Level) + "</span>";
    private static string Ratio(long numerator, long denominator, bool percentage = false) => denominator <= 0 ? "—" :
        ((double)numerator / denominator * (percentage ? 100 : 1)).ToString(percentage ? "0.0" : "0.00", CultureInfo.InvariantCulture) + (percentage ? "%" : "");
    private static string ModeName(int mode) => mode switch { 5 => "Story", 4 => "Disk Hunt", 6 => "Deathmatch", 7 => "Merc Deathmatch", 1 => "Extraction", 2 => "Sabotage", 3 => "Neutralization", _ => "Custom mode" };
    private static string Winner(int team) => team switch { 0 => "Spies", 1 => "Mercenaries", _ => "No winner" };
    private static string MapName(string map) => CosmeticCatalog.Maps.FirstOrDefault(m => string.Equals(m.Code, CosmeticCatalog.BaseMap(map), StringComparison.OrdinalIgnoreCase)).Name ?? (map.Length > 0 ? map : "Community hub");
}
