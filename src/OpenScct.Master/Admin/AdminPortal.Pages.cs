using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using static OpenScct.Master.Admin.AdminHtml;

namespace OpenScct.Master.Admin;

// The portal's pages other than the servers (AdminPortal.Servers.cs).
internal sealed partial class AdminPortal
{
    private const int PageSize = 50;

    public static string ModeName(int mode) => mode switch
    {
        5 => "Story",
        4 => "Disk Hunt",
        6 => "Deathmatch",
        7 => "Merc Deathmatch",
        1 => "Extraction",
        2 => "Sabotage",
        3 => "Neutralization",
        _ => "-",
    };

    private static string Winner(int team) => team switch { 0 => "Spies", 1 => "Mercs", _ => "None" };

    private static string ReasonText(int reason) => reason switch
    {
        0 => "Objectives / kill limit",
        1 => "Time out",
        2 => "Team eliminated",
        _ => "Stopped",
    };

    private static int PageOf(HttpContext c) =>
        int.TryParse(c.Request.Query["page"], NumberStyles.None, CultureInfo.InvariantCulture, out int p) ? Math.Clamp(p, 0, 100000) : 0;

    // ---- Dashboard ---------------------------------------------------------------------------------------------

    private IResult Dashboard(HttpContext c, AdminSessionRow admin)
    {
        long now = _store.Now;
        List<ServerView> servers = Snapshot();
        int inGames = servers.Where(s => s.Kind == ServerKind.Game).Sum(s => s.People);
        int inHubs = servers.Where(s => s.Kind == ServerKind.Hub).Sum(s => s.People);
        (int accounts, int newToday) = _store.CountAccounts(now - 86400);
        (long relayed, long introductions) = _udpStats();
        var body = new StringBuilder();
        body.Append("<div class=\"dashboard-metrics\">").Append(Tiles(
            ("Players online", N(inGames + inHubs), $"{inGames} in games, {inHubs} in hubs"),
            ("Servers", N(servers.Count(s => s.Kind == ServerKind.Game)),
                $"{servers.Count(s => s.Kind == ServerKind.Hub)} hub(s), {servers.Count(s => !s.Listed)} unlisted"),
            ("Accounts", N(accounts), $"{newToday} new in 24 hours"),
            ("Rounds in 24 hours", N(_store.CountMatches(now - 86400)), null),
            ("Relay", N(_registry.RelaySessionCount) + " sessions", $"{N(relayed)} packets relayed"),
            ("Introductions", N(introductions), "NAT punches since start"))).Append("</div>");
        body.Append(Section("Needs attention", OperationsIssues(servers)));
        List<MetricRow> day = _store.Metrics(now - 86400, 600);
        body.Append(Section("Players online, last 24 hours", Chart(
            [("Players", day.Select(m => (m.Time, m.Players)).ToList()), ("Servers", day.Select(m => (m.Time, m.Servers)).ToList())],
            now - 86400, now, t => DateTimeOffset.FromUnixTimeSeconds(t).ToString("HH:mm", CultureInfo.InvariantCulture))));
        body.Append(Section("Servers now", ServerTable(servers.Take(10))));
        body.Append(Section("Latest rounds", MatchTable(_store.Matches(0, 8))));
        body.Append(Section("Latest admin and account events", AuditTable(_store.AuditPage(null, 0, 10))));
        return Page(c, "Dashboard", "/admin", admin.Name, body.ToString(), refresh: true);
    }

    // ---- Players -----------------------------------------------------------------------------------------------

    private IResult Players(HttpContext c, AdminSessionRow admin)
    {
        string q = Query(c, "q");
        bool byCreated = c.Request.Query["sort"] == "created";
        int page = PageOf(c);
        string status = Query(c, "status");
        HashSet<long> online = OnlineAccounts();
        (List<AccountListRow> rows, int total) = _store.SearchAccounts(q, byCreated, page, PageSize, status, online, _options.Admins);
        Dictionary<long, PlayerRank> ranks = _store.Ranks(rows.Select(r => r.Account.Id));
        long now = _store.Now;
        string search = "<form method=\"get\" action=\"/admin/players\" class=\"bar\">" + Input("q", "Name, Discord id or account id", q) +
            Select("sort", [("seen", "Last seen"), ("created", "Newest accounts")], byCreated ? "created" : "seen") +
            Select("status", [("", "All players"), ("online", "Online now"), ("banned", "Banned"), ("private", "Private / hidden"), ("admin", "Admins")], status) +
            Button("Search") + "</form>";
        string table = Table(["Account", "Name", "Rank", "Discord", "Last seen", "Created", "Matches", "Kills", ""],
            rows.Select(r => new[]
            {
                "#" + r.Account.Id.ToString(CultureInfo.InvariantCulture), Link($"/admin/players/{r.Account.Id}", r.Account.Name ?? "(no name)"),
                H(RankText(ranks.GetValueOrDefault(r.Account.Id))), H(r.Account.DiscordId), H(Ago(r.Account.LastSeen, now)), H(Time(r.Account.Created)), N(r.Matches), N(r.Kills),
                (online.Contains(r.Account.Id) ? Badge("online", "ok") : string.Empty) + (r.Banned ? Badge("banned", "bad") : string.Empty) +
                    (_options.IsAdmin(r.Account.DiscordId) ? Badge("admin") : string.Empty),
            }));
        string basePath = "/admin/players?q=" + U(q) + "&sort=" + (byCreated ? "created" : "seen") + "&status=" + U(status);
        return Page(c, "Players", "/admin/players", admin.Name,
            search + $"<p class=\"muted\">{N(total)} account(s).</p>" + table + Pager(basePath, page, (page + 1) * PageSize < total));
    }

    private IResult Player(HttpContext c, AdminSessionRow admin, long id, string? notice, bool bad)
    {
        if (_store.FindById(id) is not { } account)
        {
            return Page(c, "No such player", "/admin/players", admin.Name, "<p>That account does not exist.</p>", status: 404);
        }

        long now = _store.Now;
        AccountInfo info = AccountInfo(account);
        ProgressRow progress = _store.ProgressionOf(id);
        var body = new StringBuilder();
        string where = string.Join(", ", Snapshot().Where(s => s.Players.Any(p => p.AccountId == id))
            .Select(s => Link($"/admin/servers/{s.Id}", s.Name)));
        body.Append(Tiles(
            ("Name", account.Name ?? "(none)", "Discord " + account.DiscordId),
            ("Last seen", Ago(account.LastSeen, now), "since " + Time(account.Created)),
            ("Matches", N(info.Stats.Matches), $"{info.Stats.SpyWins} spy wins, {info.Stats.MercWins} merc wins, {info.Stats.Losses} losses"),
            ("Kills", N(info.Stats.Kills), $"{info.Stats.Deaths} deaths"),
            ("Achievements", $"{info.Achievements.Count(a => a.Earned is not null)} / {info.Achievements.Count}", null),
            ("Cosmetics", $"{info.Unlocked.Count} / {CosmeticCatalog.All.Count}", $"{_store.Grants(id).Count} given by admins"),
            ("Rank", RankText(progress.Rank), $"{N(progress.TotalXp)} XP in all, best level {progress.BestLevel}, " +
                $"{_store.Titles(id).Count} / {TitleCatalog.All.Count} titles"),
            ("Last round XP", progress.LastAward is { } award ? "+" + N(award.Total) : "-",
                progress.LastAward is { } la ? $"score {la.Score}, finish {la.Finish}, win {la.Win}, objectives {la.Objectives}, " +
                    $"achievements {la.Achievements} ({la.Map})" : null)));
        if (where.Length > 0)
        {
            body.Append("<p class=\"notice\">Playing now on ").Append(where).Append(".</p>");
        }

        string self = $"/admin/players/{id}";
        ProfileVisibilityRow visibility = _store.ProfileVisibility(id);
        body.Append("<nav class=\"tabs\" aria-label=\"Player sections\">")
            .Append(Link("#moderation", "Moderation")).Append(Link("#progression", "Progression"))
            .Append(Link("#recent-rounds", "Matches")).Append(Link("#sessions", "Sessions"))
            .Append(Link("#audit-history", "Audit")).Append("</nav>");
        body.Append(Section("Moderation", "<p class=\"muted\">Target: <strong>" + H(account.Name ?? "Unnamed account") + "</strong> · account #" + N(id) +
            " · " + (visibility.Hidden ? "profile hidden by moderation" : visibility.Public ? "public profile" : "private profile") + "</p><div class=\"actions\">" +
            Form(c, self, "<h3>Player name</h3>" + Input("name", "New name", account.Name ?? string.Empty, maxLength: 16) + Button("Rename", "do", "rename")) +
            Form(c, self, "<h3>Release player name</h3>" + Button("Release name", "do", "release", "danger"), "Release the name of " + (account.Name ?? "this account") + "? They will choose a name on their next sign-in.") +
            Form(c, self, "<h3>Ban account</h3>" + BanDurationFields() + Input("reason", "Reason", required: true) + Button("Ban account", "do", "ban", "danger"),
                "Ban " + (account.Name ?? "account #" + id) + " and its Discord identity?") +
            Form(c, self, "<h3>End account bans</h3>" + Button("Unban", "do", "unban"), "End the active account bans?") +
            Form(c, self, "<h3>Remove from live servers</h3>" + Input("reason", "Kick reason") + Button("Kick player", "do", "kick"), "Kick " + (account.Name ?? "this player") + " from the trusted servers they are on?") +
            Form(c, self, "<h3>Public profile moderation</h3>" + Input("reason", "Moderation reason", required: true) +
                Button(visibility.Hidden ? "Restore visibility" : "Hide public profile", "do", visibility.Hidden ? "show-profile" : "hide-profile", "danger"),
                visibility.Hidden ? "Restore this profile's own privacy choice?" : "Hide this player from the public website, including scoreboards and leaderboards?") +
            "</div>"));
        body.Append(Section("Progression", "<div class=\"actions\">" +
            Form(c, self, "<h3>Cosmetic grants</h3>" + Select("what", CosmeticChoices()) + Input("reason", "Reason", required: true) +
                Button("Unlock", "do", "unlock") + Button("Take back", "do", "lock", "danger"), "Change this player's cosmetic grants?") +
            Form(c, self, "<h3>Set rank</h3><label class=\"field\"><span>Prestige (0–10)</span>" +
                Input("prestige", "Prestige (0–10)", progress.Prestige.ToString(CultureInfo.InvariantCulture), type: "number") + "</label>" +
                "<label class=\"field\"><span>Rank (level 1–55)</span>" +
                Input("level", "Rank (level 1–55)", progress.Rank.Level.ToString(CultureInfo.InvariantCulture), type: "number") + "</label>" +
                "<label class=\"field\"><span>Reason</span>" + Input("reason", "Reason", required: true) + "</label>" +
                Button("Set rank", "do", "rank"), "Set this account's prestige and level?") + "</div>"));

        body.Append(Section("Bans", Table(["#", "Reason", "Until", "Added"], _store.BansOf(account).Select(b => new[]
        {
            "#" + b.Id.ToString(CultureInfo.InvariantCulture), H(b.Reason), H(b.Until is { } u ? (u <= now ? "ended " : string.Empty) + Time(u) : "permanent"),
            H(Time(b.Added)),
        }), "No bans.")));
        body.Append(Section("Recent rounds", MatchTable(_store.Matches(0, 15, id))));
        body.Append(Section("Achievements", Table(["Achievement", "Group", "Progress", "Unlocks"], info.Achievements.Select(a => new[]
        {
            (a.Earned is not null ? Badge("earned", "ok") + " " : string.Empty) + H(a.Title), H(a.Category),
            H(a.Earned is { } e ? e.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : $"{a.Current} / {a.Target}"),
            H(CosmeticCatalog.DescribeAll(a.Rewards)),
        }))));
        body.Append(Section("Sessions", PlayerSessions(c, id) + Form(c, self, Button("Sign out everywhere", "do", "signout", "danger"),
            "End every game and player website session for this account?", "bar")));
        body.Append(Section("Audit history", DetailedAuditTable(_store.DetailedAudit(string.Empty, string.Empty, string.Empty, "account:" + id,
            null, null, 0, 20)) + Link("/admin/audit?target=" + U("account:" + id), "View full audit history")));
        body.Append(Section("Old names", Table(["Name", "Until", "Held until"], _store.NameHistory(id).Select(h => new[]
        {
            H(h.Name), H(Time(h.Changed)), H(Time(h.Until)),
        }), "None.")));
        return Page(c, account.Name ?? $"Account #{id}", "/admin/players", admin.Name, body.ToString(), notice, bad);
    }

    private static IEnumerable<(string, string)> CosmeticChoices() =>
        new[] { ("all", "Every cosmetic"), ("spy", "All spy cosmetics"), ("merc", "All merc cosmetics") }
            .Concat(CosmeticCatalog.All.Select(i => (i.Id, CosmeticCatalog.Describe(i))));

    private IResult PlayerAction(HttpContext c, AdminSessionRow admin, long id, IFormCollection form)
    {
        if (_store.FindById(id) is not { } account)
        {
            return Player(c, admin, id, null, false);
        }

        string action = Field(form, "do");
        string reason = Field(form, "reason");
        if (action is "ban" or "unlock" or "lock" or "rank" or "hide-profile" or "show-profile" && reason.Length == 0)
        {
            return Player(c, admin, id, "Enter a reason for this change.", true);
        }

        object before = PlayerAuditSnapshot(account);
        AdminResult result;
        if (action is "hide-profile" or "show-profile")
        {
            _store.SetProfileHidden(id, action == "hide-profile", reason);
            result = AdminResult.Done(action == "hide-profile" ? "The public profile is hidden." : "The player's privacy choice applies again.");
        }
        else
        {
            result = action switch
            {
                "rename" => _actions.Rename(account, Field(form, "name", 32)),
                "release" => _actions.ReleaseName(account),
                "ban" => BanFromForm("#" + id.ToString(CultureInfo.InvariantCulture), form),
                "unban" => _actions.Unban(account.DiscordId),
                "kick" => KickAccount(id, reason.Length > 0 ? reason : "Kicked by an admin."),
                "signout" => AdminResult.Done($"Ended {_store.RevokePlayerSessions(id)} game and player website session(s)."),
                "revoke" => _store.RevokePlayerSession(id, Field(form, "kind"), Field(form, "session"))
                    ? AdminResult.Done("Session revoked.") : AdminResult.Missing("That session has already ended."),
                "unlock" => _actions.Grant(account, [Field(form, "what")], unlock: true),
                "lock" => _actions.Grant(account, [Field(form, "what")], unlock: false),
                "rank" => _actions.SetRank(account, Field(form, "prestige"), Field(form, "level")),
                _ => AdminResult.Refused("Unknown action."),
            };
        }

        if (result.Ok)
        {
            _store.AuditChange("player." + action, "account:" + id, reason, before, PlayerAuditSnapshot(account));
        }
        return Player(c, admin, id, result.Message, !result.Ok);
    }

    private static string RankText(PlayerRank rank) =>
        rank.IsNone ? "-" : rank.IsMaster ? $"Master, level {rank.Level}" : rank.Prestige > 0 ? $"P{rank.Prestige}, level {rank.Level}" : $"Level {rank.Level}";

    private AccountInfo AccountInfo(AccountRow account)
    {
        Dictionary<string, long> earned = _store.Earned(account.Id);
        AccountProgress progress = _store.Progress(account.Id);
        return new AccountInfo
        {
            Id = account.Id, Name = account.Name, Stats = _store.Stats(account.Id), Unlocked = _store.Unlocked(account.Id),
            Achievements = Achievements.All.Select(a =>
            {
                bool done = earned.TryGetValue(a.Id, out long at);
                (int current, int target) = a.Goal(progress);
                return new AchievementInfo
                {
                    Id = a.Id, Title = a.Title, Description = a.Description, Category = a.Category.ToString(), Rewards = a.Rewards,
                    Earned = done ? DateTimeOffset.FromUnixTimeSeconds(at) : null, Current = done ? target : Math.Min(current, target),
                    Target = target,
                };
            }).ToList(),
        };
    }

    // ---- Bans --------------------------------------------------------------------------------------------------

    private IResult Bans(HttpContext c, AdminSessionRow admin, string? notice, bool bad, string? preview = null)
    {
        bool all = c.Request.Query["all"] == "1";
        int page = PageOf(c);
        List<BanRow> bans = _store.AllBans(all, page, PageSize, lookAhead: true);
        long now = _store.Now;
        string add = Form(c, "/admin/bans",
            Input("target", "Name, #account, Discord id, IP or CIDR", required: true) + BanDurationFields() +
            Input("reason", "Reason", required: true) + Button("Preview ban", "do", "preview"), cls: "bar");
        string filter = all ? Link("/admin/bans", "Only bans in force") : Link("/admin/bans?all=1", "Show ended bans too");
        string table = Table(["#", "On", "Reason", "Until", "Added", ""], bans.Take(PageSize).Select(b => new[]
        {
            "#" + b.Id.ToString(CultureInfo.InvariantCulture),
            b.AccountId is { } a ? Link($"/admin/players/{a}", _actions.BanTarget(b)) : H(_actions.BanTarget(b)),
            H(b.Reason), H(b.Until is { } u ? (u <= now ? "ended " : string.Empty) + Time(u) : "permanent"), H(Time(b.Added)),
            b.Until is { } end && end <= now ? string.Empty
                : Form(c, "/admin/bans", $"<input type=\"hidden\" name=\"target\" value=\"#{b.Id}\">" + Button("End", "do", "unban"), "End this ban?", "inline"),
        }), "No bans.");
        return Page(c, "Bans", "/admin/bans", admin.Name, (preview ?? string.Empty) + Section("New ban", add) + $"<p>{filter}</p>" + table +
            Pager(all ? "/admin/bans?all=1" : "/admin/bans", page, bans.Count > PageSize), notice, bad);
    }

    private IResult BanAction(HttpContext c, AdminSessionRow admin, IFormCollection form)
    {
        string target = Field(form, "target", 64);
        string action = Field(form, "do");
        if (action == "preview")
        {
            if (!TryBanDuration(form, "days", out double? days, out string error))
            {
                return Bans(c, admin, error, true);
            }
            string reason = Field(form, "reason");
            if (reason.Length == 0) return Bans(c, admin, "Enter a reason for the ban.", true);
            AccountRow? account = _actions.FindAccount(target);
            string? ip = AccountStore.NormalizeIp(target);
            if (account is null && ip is null && !MasterOptions.IsSnowflake(target)) return Bans(c, admin, "No matching account or valid address.", true);
            string canonical = account is not null ? "#" + account.Id : ip ?? target;
            string description = account is not null ? $"Account #{account.Id}: {account.Name ?? "unnamed"} (Discord {account.DiscordId})" : ip is not null ? "Address / range: " + ip : "Discord identity: " + target;
            string hidden = string.Concat(new[] { ("target", canonical), ("duration", days is null ? "permanent" : "temporary"),
                ("days", days?.ToString(CultureInfo.InvariantCulture) ?? ""), ("reason", reason) }.Select(f => $"<input type=\"hidden\" name=\"{H(f.Item1)}\" value=\"{H(f.Item2)}\">"));
            string preview = Section("Review ban", "<p><strong>" + H(description) + "</strong></p><p>" + H(days is null ? "Permanent ban" : $"Temporary: {days} days") +
                "</p><p>Reason: " + H(reason) + "</p>" + Form(c, "/admin/bans", hidden + Button("Confirm ban", "do", "ban", "danger"), "Apply this ban to the reviewed target?"));
            return Bans(c, admin, null, false, preview);
        }

        List<BanRow> before = BanAuditSnapshot(target, action == "unban");
        AdminResult result = action == "unban" ? _actions.Unban(target) : action == "ban" ? BanFromForm(target, form) : AdminResult.Refused("Unknown action.");
        if (result.Ok)
        {
            AccountRow? account = action == "unban"
                ? before.FirstOrDefault()?.AccountId is { } accountId ? _store.FindById(accountId) : null
                : _actions.FindAccount(target);
            _store.AuditChange("moderation." + action, account is not null ? "account:" + account.Id : "ban:" + target,
                Field(form, "reason"), before, BanAuditSnapshot(target, action == "unban"));
        }
        return Bans(c, admin, result.Message, !result.Ok);
    }

    // ---- Matches -----------------------------------------------------------------------------------------------

    private string MatchTable(IEnumerable<MatchRow> matches) => Table(["When", "Server", "Map", "Mode", "Winner", "Length", ""],
        matches.Select(m => new[]
        {
            Link($"/admin/matches/{m.Id}", Time(m.Time)), H(m.ServerName), H(m.Map), H(ModeName(m.Mode)),
            H(Winner(m.Winner) + " (" + ReasonText(m.Reason) + ")"), H(m.DurationSeconds > 0 ? Duration(m.DurationSeconds) : "-"),
            m.Counted ? Badge("counted", "ok") : Badge("not counted"),
        }), "No rounds yet.");

    private IResult Matches(HttpContext c, AdminSessionRow admin)
    {
        int page = PageOf(c);
        string query = Query(c, "q");
        string counted = Query(c, "counted") is "yes" or "no" ? Query(c, "counted") : string.Empty;
        List<MatchRow> rows = _store.SearchAdminMatches(query, counted, page, PageSize, lookAhead: true);
        string filter = "<form method=\"get\" action=\"/admin/matches\" class=\"bar\">" + Input("q", "Map, server or match id", query) +
            Select("counted", [("", "All rounds"), ("yes", "Counted"), ("no", "Not counted")], counted) + Button("Filter") + "</form>";
        return Page(c, "Matches", "/admin/matches", admin.Name,
            "<p class=\"muted\">Counted rounds contribute to player stats and achievements. All reported rounds remain available for investigation.</p>" + filter +
            MatchTable(rows.Take(PageSize)) + Pager("/admin/matches?q=" + U(query) + "&counted=" + U(counted), page, rows.Count > PageSize));
    }

    private IResult Match(HttpContext c, AdminSessionRow admin, long id)
    {
        if (_store.Match(id) is not { } found)
        {
            return Page(c, "No such round", "/admin/matches", admin.Name, "<p>That round is not stored.</p>", status: 404);
        }

        MatchRow m = found.Match;
        string body = Tiles(
            ("Map", m.Map, ModeName(m.Mode)), ("Winner", Winner(m.Winner), ReasonText(m.Reason)),
            ("Objectives", $"{m.ObjectivesTaken} / {m.ObjectivesNeeded}", null),
            ("Length", m.DurationSeconds > 0 ? Duration(m.DurationSeconds) : "-", Time(m.Time)), ("Server", m.ServerName, m.ServerId)) +
            Table(["Player", "Side", "Score", "Kills", "Deaths", "Feats"], found.Rows.Select(r => new[]
            {
                (r.AccountId is { } a ? Link($"/admin/players/{a}", r.Name.Length > 0 ? r.Name : "#" + a) : H(r.Name.Length > 0 ? r.Name : "guest")) +
                    (r.Bot ? " " + Badge("bot") : string.Empty),
                H(r.Team == 0 ? "Spy" : r.Team == 1 ? "Merc" : "-"), N(r.Score), N(r.Kills), N(r.Deaths),
                H(string.Join(", ", r.Feats.Select(f => $"{f.Key} {f.Value}"))),
            })) + DemoSection(id);
        return Page(c, $"Round #{id}", "/admin/matches", admin.Name, body);
    }

    /// <summary>The round's demo, with a download link while it is kept.</summary>
    private string DemoSection(long matchId)
    {
        if (_store.DemoOfMatch(matchId) is not { } d)
        {
            return _demos is null ? string.Empty : "<h2>Demo</h2><p class=\"muted\">No demo was uploaded for this round.</p>";
        }

        bool kept = d.State == DemoState.Ready && d.Expires > _store.Now;
        string state = d.State switch
        {
            DemoState.Ready when kept => Badge("ready", "ok"),
            DemoState.Pending => Badge("uploading"),
            _ => Badge("expired"),
        };
        string download = kept && _demos is not null ? Link($"/admin/matches/{matchId}/demo", "Download") : "-";
        return "<h2>Demo</h2>" + Table(["Demo", "State", "Size", "Length", "Versions", "Uploaded", "Kept until", ""], [new[]
        {
            H("#" + d.Id), state, H((d.Size / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB"),
            H(d.DurationSeconds > 0 ? Duration(d.DurationSeconds) : "-"),
            H($"game {d.GameVersion}, net {d.NetVersion}, content {d.ContentHash}"), H(Time(d.Created)), H(Time(d.Expires)), download,
        }]);
    }

    /// <summary>Sends the admin to a fresh short-lived download link of the round's demo.</summary>
    private IResult MatchDemo(HttpContext c, AdminSessionRow admin, long matchId)
    {
        if (_demos is null || _store.DemoOfMatch(matchId) is not { State: DemoState.Ready } d || d.Expires <= _store.Now)
        {
            return Page(c, "No demo", "/admin/matches", admin.Name, "<p>This round has no demo kept.</p>", status: 404);
        }

        return Results.Redirect(_demos.DownloadUrl(d.Key).Url);
    }

    // ---- History -----------------------------------------------------------------------------------------------

    private IResult History(HttpContext c, AdminSessionRow admin)
    {
        string range = c.Request.Query["range"].ToString() is "week" or "month" ? c.Request.Query["range"].ToString() : "day";
        long now = _store.Now;
        (long span, int bucket, string format) = range switch
        {
            "week" => (7 * 86400L, 3600, "ddd HH:mm"),
            "month" => (30 * 86400L, 6 * 3600, "dd MMM"),
            _ => (86400L, 600, "HH:mm"),
        };
        List<MetricRow> rows = _store.Metrics(now - span, bucket);
        string tabs = "<p class=\"tabs\">" + string.Join(' ', new[] { "day", "week", "month" }.Select(r =>
            r == range ? $"<strong>{r}</strong>" : Link("/admin/history?range=" + r, r))) + "</p>";
        string chart = Chart(
            [
                ("Players (peak)", rows.Select(m => (m.Time, m.Players)).ToList()),
                ("Game servers", rows.Select(m => (m.Time, m.Servers)).ToList()),
                ("Relay sessions (peak)", rows.Select(m => (m.Time, m.RelaySessions)).ToList()),
            ],
            now - span, now, t => DateTimeOffset.FromUnixTimeSeconds(t).ToString(format, CultureInfo.InvariantCulture));
        List<MetricRow> daily = _store.Metrics(now - (30 * 86400L), 86400);
        string peaks = Table(["Day", "Peak players", "Servers (average)"], daily.AsEnumerable().Reverse().Select(m => new[]
        {
            H(DateTimeOffset.FromUnixTimeSeconds(m.Time).ToString("ddd dd MMM", CultureInfo.InvariantCulture)), N(m.Players), N(m.Servers),
        }), "No samples yet; the master takes one a minute.");
        return Page(c, "History", "/admin/history", admin.Name, tabs + chart + Section("Peak players per day (UTC)", peaks));
    }

    // ---- Settings ----------------------------------------------------------------------------------------------

    private IResult Settings(HttpContext c, AdminSessionRow admin, string? notice, bool bad)
    {
        Dictionary<string, string> stored = _store.Settings();
        string Row(string key, string label, string value, string help) =>
            $"<label><span>{H(label)}</span>{Input(key, string.Empty, value, maxLength: 300)}<small>{H(help)}" +
            (stored.ContainsKey(key) ? " (set here)" : " (from the config file)") + "</small></label>";
        string form = Form(c, "/admin/settings",
            Row("motd", "Message of the day", _options.Motd ?? string.Empty, "Shown in the game's server browser.") +
            Row("latestVersion", "Latest version", _options.LatestVersion ?? string.Empty, "Hosts older than this are told to update.") +
            Row("minVersion", "Minimum version", _options.MinVersion ?? string.Empty, "Hosts older than this are refused.") +
            Button("Save", "do", "save") + " " + Button("Use the config file's values", "do", "reset"), cls: "stack");
        string site = Form(c, "/admin/settings",
            "<label>Welcome text<textarea name=\"site.welcome\" maxlength=\"300\">" + H(stored.GetValueOrDefault("site.welcome", "Welcome to OpenSCCT. Find your next match.")) + "</textarea></label>" +
            "<label>Public announcement<textarea name=\"site.announcement\" maxlength=\"1000\">" + H(stored.GetValueOrDefault("site.announcement", string.Empty)) + "</textarea></label>" +
            "<small>Plain text only. Leave the announcement empty to remove it.</small><details open><summary>Preview</summary><div id=\"site-preview\" class=\"site-preview\"></div></details>" +
            Button("Publish website text", "do", "site"), "Publish this text to the public player hub?", "stack");
        string announce = Form(c, "/admin/settings",
            Input("text", "Message for every player on the master's own servers", maxLength: 150, required: true) + Button("Announce", "do", "announce"),
            "Send this message to every trusted server?", "bar");
        string admins = Table(["Discord id", "Account"], _options.Admins.Select(d => new[]
        {
            H(d), _store.FindByDiscord(d) is { } a ? Link($"/admin/players/{a.Id}", a.Name ?? "#" + a.Id) : "(never signed in)",
        }), "No admins.");
        string blocks = Table(["#", "Kind", "Value", "Reason", "Added", ""], _store.Blocks().Select(b => new[]
        {
            "#" + b.Id.ToString(CultureInfo.InvariantCulture), H(b.Kind), H(b.Value), H(b.Reason), H(Time(b.Added)),
            Form(c, "/admin/servers/blocks", $"<input type=\"hidden\" name=\"id\" value=\"{b.Id}\">" + Button("Remove", "do", "unblock"), cls: "inline"),
        }), "No servers blocked.");
        return Page(c, "Settings", "/admin/settings", admin.Name,
            Section("Master", form) + Section("Public website", site) + Section("In-game announcement", announce) +
            Section("Blocked servers", blocks + Form(c, "/admin/servers/blocks",
                Select("kind", [("ip", "Address"), ("id", "Server id")]) + Input("value", "Address or server id", required: true) +
                Input("reason", "Reason", required: true) + Button("Block", "do", "block", "danger"), "Refuse this server's heartbeats?", "bar")) +
            Section("Admins (from the config file's Admins list)", admins), notice, bad);
    }

    private IResult SettingsAction(HttpContext c, AdminSessionRow admin, IFormCollection form)
    {
        switch (Field(form, "do"))
        {
            case "save":
                var values = new Dictionary<string, string?>();
                foreach (string key in new[] { "motd", "latestVersion", "minVersion" })
                {
                    string value = Field(form, key, 300);
                    if (key != "motd" && value.Length > 0 && !ValidVersion(value))
                    {
                        return Settings(c, admin, $"'{value}' is not a version (like 0.8.52). Nothing was saved.", true);
                    }
                    values[key] = value;
                }
                if (!string.IsNullOrEmpty(values["latestVersion"]) && !string.IsNullOrEmpty(values["minVersion"]) &&
                    BuildInfo.CompareVersions(values["minVersion"], values["latestVersion"]) > 0)
                {
                    return Settings(c, admin, "The minimum version cannot be newer than the latest version. Nothing was saved.", true);
                }
                _store.SetSettingsAtomic(values);
                MasterSettings.Apply(_store, _options);
                return Settings(c, admin, "Settings saved.", false);
            case "reset":
                _store.SetSettingsAtomic(new Dictionary<string, string?> { ["motd"] = null, ["latestVersion"] = null, ["minVersion"] = null });
                MasterSettings.Apply(_store, _options);
                return Settings(c, admin, "The startup config values apply again.", false);
            case "site":
                // Text areas retain newlines; rendering always HTML-encodes these values.
                string welcome = form["site.welcome"].ToString().Trim();
                string announcement = form["site.announcement"].ToString().Trim();
                if (welcome.Length > 300 || announcement.Length > 1000)
                {
                    return Settings(c, admin, "Welcome text is limited to 300 characters; announcements to 1,000. Nothing was saved.", true);
                }
                _store.SetSettingsAtomic(new Dictionary<string, string?> { ["site.welcome"] = welcome, ["site.announcement"] = announcement });
                return Settings(c, admin, "The public website text is published.", false);
            case "announce":
                string text = Field(form, "text", 150);
                int sent = text.Length == 0 ? 0 : QueueForAll("say " + text);
                _store.AuditChange("announce", "servers:trusted", string.Empty, null, new { Servers = sent, Text = text });
                return Settings(c, admin, $"Queued for {sent} trusted server(s). Track responses in Command history.", sent == 0);
            default:
                return Settings(c, admin, "Unknown action.", true);
        }
    }

    // ---- Audit -------------------------------------------------------------------------------------------------

    private static string AuditTable(IEnumerable<AuditRow> rows) => Table(["When", "Who", "Action", "Detail"], rows.Select(r => new[]
    {
        H(Time(r.Time)), H(r.Actor.Length > 0 ? r.Actor : "-"), H(r.Action), H(r.Detail),
    }), "The audit log is empty.");

    private IResult Audit(HttpContext c, AdminSessionRow admin)
    {
        string q = Query(c, "q");
        string actor = Query(c, "actor");
        string action = Query(c, "action");
        string target = Query(c, "target");
        string from = Query(c, "from");
        string until = Query(c, "until");
        int page = PageOf(c);
        List<AuditDetailRow> rows = _store.DetailedAudit(q, actor, action, target, ParseDate(from), ParseDate(until, end: true), page, PageSize, lookAhead: true);
        string search = "<form method=\"get\" action=\"/admin/audit\" class=\"bar\">" +
            Input("q", "Search detail", q) + Input("actor", "Operator", actor) + Input("action", "Action", action) +
            Input("target", "Target (account:123)", target) + "<label>From " + Input("from", "From date", from, "date") + "</label>" +
            "<label>Through " + Input("until", "Through date", until, "date") + "</label>" + Button("Filter") + "</form>";
        string path = "/admin/audit?q=" + U(q) + "&actor=" + U(actor) + "&action=" + U(action) + "&target=" + U(target) + "&from=" + U(from) + "&until=" + U(until);
        return Page(c, "Audit log", "/admin/audit", admin.Name, search + DetailedAuditTable(rows.Take(PageSize)) +
            Pager(path, page, rows.Count > PageSize));
    }
}
