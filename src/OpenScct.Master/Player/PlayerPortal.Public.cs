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
        string welcome = settings.GetValueOrDefault("site.welcome", "Find a game, track your stats, and manage your OpenSCCT account.");
        string announcement = settings.GetValueOrDefault("site.announcement", string.Empty);
        var body = new StringBuilder();
        if (announcement.Length > 0) body.Append("<aside class=\"announcement\"><strong>Announcement</strong><p>").Append(H(announcement)).Append("</p></aside>");
        body.Append("<section class=\"hero\"><div><p class=\"eyebrow\">OPENSCCT</p><h1>Spies vs.<br><span>Mercenaries.</span></h1><p class=\"lead\">")
            .Append(H(welcome)).Append("</p><div class=\"button-row\">").Append(Link("/download", "Download OpenSCCT ↗", "button"))
            .Append(Link("/servers", "Find a game →", "button secondary"))
            .Append("</div><p class=\"hero-note\">Free community project · Windows &amp; Linux · Optional Discord sign-in</p></div>")
            .Append($"<div class=\"hero-art\" aria-hidden=\"true\"><div class=\"radar\"><span class=\"radar-label top\">SIGNAL ACQUIRED</span><img class=\"radar-logo\" src=\"{H(AssetUrl("/web/assets/mark.svg"))}\" alt=\"\">")
            .Append("<i class=\"radar-point one\"></i><i class=\"radar-point two\"></i><i class=\"radar-point three\"></i><span class=\"radar-label bottom\">SPY / MERC</span></div><span class=\"art-caption\">").Append(Wordmark).Append("</span></div></section>")
            .Append("<div data-live=\"home-stats\"><div class=\"stats-grid\">")
            .Append(Stat("Players online", N(servers.Sum(s => s.Players)), "In the games and hubs listed here"))
            .Append(Stat("Games and hubs", N(servers.Count), "Currently online"))
            .Append(Stat("Player profiles", N(_store.PublicPlayers(null, "name", 0, 1).Total), "Public profiles you can browse"))
            .Append(Stat("Rounds today", N(_store.CountMatches(_store.Now / 86400 * 86400)), "Results received by the website"))
            .Append("</div></div>");
        body.Append(ImprovementsPreview());
        body.Append(Section("Games and hubs online", "<div data-live=\"home-servers\">" + ServerTable(servers.Take(4)) + "</div>",
            "<div class=\"button-row\">" + LiveStatus + Link("/servers", "View all games and hubs →", "text-link") + "</div>"));
        body.Append(Section("Players and results", "<div class=\"grid-three\">" +
            Feature("01", "Your account", "Check your level and achievements, choose your appearance, and manage your privacy.", "/account", "Open your account →") +
            Feature("02", "Player profiles", "Find a player and see their stats, achievements, and recent matches.", "/players", "Browse players →") +
            Feature("03", "Match results", "See which team won, who played, and how each player scored.", "/matches", "View matches →") + "</div>"));
        return Show(c, "Home", "home", body.ToString());
    }

    private static string Feature(string step, string title, string description, string url, string label) =>
        $"<article class=\"feature-card\"><span class=\"step\">{H(step)}</span><h3>{H(title)}</h3><p>{H(description)}</p>{Link(url, label, "text-link")}</article>";

    private IResult Download(HttpContext c) => Show(c, "Download OpenSCCT", "download",
        PageHeader("GET STARTED", "Download OpenSCCT", "Choose the Windows or Linux download below. You do not need to own the original game.") +
        "<div class=\"grid-two\"><section class=\"panel download-card\"><p class=\"eyebrow\">OPENSCCT FOR WINDOWS</p><h2>Windows launcher</h2>" +
        "<p class=\"lead\">Download and run the launcher. It installs OpenSCCT, downloads the game files, and handles updates.</p>" +
        Link("https://github.com/Lumbridge/OpenSCCT/releases/latest/download/OpenSCCT.Launcher.exe", "Download for Windows ↗", "button") +
        "<span class=\"platform\">Windows 64-bit · Automatic game updates</span><p class=\"account-side-note\">You do not need your own copy of Splinter Cell: Chaos Theory. " +
        "If you already own it, the launcher can use the files from your installation instead.</p>" +
        Link("/changelog", "Read update notes →", "text-link") + "</section>" +
        "<section class=\"panel\"><h2>Install on Windows</h2><ol class=\"download-steps\"><li><strong>Open the launcher</strong><p>Save OpenSCCT.Launcher.exe in a folder such as Downloads, run it, and choose Install.</p></li>" +
        "<li><strong>Download the game files</strong><p>Choose Download game files and wait for the download and preparation to finish.</p></li>" +
        "<li><strong>Start playing</strong><p>Choose PLAY. In the game, use Find a Session to join a match, or Hub to visit a social space. You can also practice offline with bots.</p></li></ol></section></div>" +
        Section("Linux & Steam Deck", "<div class=\"panel download-card\"><p class=\"eyebrow\">EXPERIMENTAL · LINUX · INTEL/AMD 64-BIT</p><h2>Linux and Steam Deck</h2>" +
            "<p class=\"lead\">Download the Flatpak installation file and follow the Linux setup guide. On Steam Deck, setup starts in Desktop Mode. Steam Deck testing is still pending.</p><div class=\"button-row\">" +
            Link("https://github.com/Lumbridge/OpenSCCT/releases/latest/download/OpenSCCT-linux-x86_64.flatpak", "Download for Linux ↗", "button secondary") +
            Link("https://github.com/Lumbridge/OpenSCCT/blob/main/docs/LINUX.md", "Linux setup guide ↗", "text-link") + "</div></div>") +
        Section("Getting started", "<div class=\"grid-three\">" +
            Feature("01", "Playing without an account", "You can play offline and join servers marked Guests welcome from inside the game without an account. Sign in with Discord to reserve a name and save your stats.", "/account", "Set up your account →") +
            Feature("02", "Find people to play with", "See which games have room for more players. Hubs are social spaces where you can meet between matches. Some servers need an account or password.", "/servers", "Browse games and hubs →") +
            Feature("03", "Need help installing?", "Find setup guides, report a problem or suggest an improvement.", "/help", "Get help →") + "</div>"));

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
                "On Linux and Steam Deck, install the latest Flatpak. Your browser may ask permission to open OpenSCCT. Website Join links use your Discord sign-in inside the game; signing in on the website does not sign you into the game.</p>" +
                "<p>If the game is already running, Join uses that window. If you are in a match, it waits until you return to the main menu. Enter any server password in the game.</p>" +
                "<p class=\"account-side-note\">Join links need game version 0.8.58 or later and, on Windows, launcher version 0.8.26 or later. You can also browse games through Find a Session or visit hubs through Hub in the game.</p>" +
                Link("/download", "Download or update OpenSCCT →", "text-link") + "</div>"));
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
        string sort = Query(c, "sort") switch { "wins" => "wins", "kills" => "kills", _ => "xp" };
        int page = PageOf(c);
        (List<PublicPlayerRow> rows, int total) = _store.PublicPlayers(null, sort, page, PageSize);
        return Show(c, "Leaderboards", "leaderboards", PageHeader("ALL TIME", "Leaderboards", "Compare players by total experience points (XP), wins, or kills. These totals come from rounds that qualify for player stats.") +
            "<form method=\"get\" action=\"/leaderboards\" class=\"filters\">" + FilterSelect("sort", "Rank players by", [("xp", "Total XP"), ("wins", "Wins"), ("kills", "Kills")], sort) +
            "<button class=\"button\">Apply</button><span class=\"field-help\">Public profiles · All time</span></form>" + PlayerTable(rows, true, page) +
            Pager("/leaderboards?sort=" + sort, page, (page + 1) * PageSize < total));
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
        if (owner) body.Append("<div class=\"button-row\">").Append(Link("/account/appearance", "Customize appearance →", "button secondary compact")).Append("</div>");
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
        string body = PageHeader("ROUND REPORT", MapName(m.Map), ModeName(m.Mode) + " · " + m.ServerName, Link("/matches", "← All matches", "button secondary compact")) +
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
