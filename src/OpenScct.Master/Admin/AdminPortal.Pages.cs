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
        body.Append(Tiles(
            ("Players online", N(inGames + inHubs), $"{inGames} in games, {inHubs} in hubs"),
            ("Servers", N(servers.Count(s => s.Kind == ServerKind.Game)),
                $"{servers.Count(s => s.Kind == ServerKind.Hub)} hub(s), {servers.Count(s => !s.Listed)} unlisted"),
            ("Accounts", N(accounts), $"{newToday} new today"),
            ("Rounds today", N(_store.CountMatches(now - 86400)), null),
            ("Relay", N(_registry.RelaySessionCount) + " sessions", $"{N(relayed)} packets relayed"),
            ("Introductions", N(introductions), "NAT punches since start")));
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
        string q = c.Request.Query["q"].ToString();
        bool byCreated = c.Request.Query["sort"] == "created";
        int page = PageOf(c);
        (List<AccountListRow> rows, int total) = _store.SearchAccounts(q, byCreated, page, PageSize);
        HashSet<long> online = OnlineAccounts();
        long now = _store.Now;
        string search = "<form method=\"get\" action=\"/admin/players\" class=\"bar\">" + Input("q", "Name, Discord id or account id", q) +
            Select("sort", [("seen", "Last seen"), ("created", "Newest accounts")], byCreated ? "created" : "seen") +
            Button("Search") + "</form>";
        string table = Table(["Account", "Name", "Discord", "Last seen", "Created", "Matches", "Kills", ""],
            rows.Select(r => new[]
            {
                "#" + r.Account.Id.ToString(CultureInfo.InvariantCulture), Link($"/admin/players/{r.Account.Id}", r.Account.Name ?? "(no name)"),
                H(r.Account.DiscordId), H(Ago(r.Account.LastSeen, now)), H(Time(r.Account.Created)), N(r.Matches), N(r.Kills),
                (online.Contains(r.Account.Id) ? Badge("online", "ok") : string.Empty) + (r.Banned ? Badge("banned", "bad") : string.Empty) +
                    (_options.IsAdmin(r.Account.DiscordId) ? Badge("admin") : string.Empty),
            }));
        string basePath = "/admin/players?q=" + U(q) + "&sort=" + (byCreated ? "created" : "seen");
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
        var body = new StringBuilder();
        string where = string.Join(", ", Snapshot().Where(s => s.Players.Any(p => p.AccountId == id))
            .Select(s => Link($"/admin/servers/{s.Id}", s.Name)));
        body.Append(Tiles(
            ("Name", account.Name ?? "(none)", "Discord " + account.DiscordId),
            ("Last seen", Ago(account.LastSeen, now), "since " + Time(account.Created)),
            ("Matches", N(info.Stats.Matches), $"{info.Stats.SpyWins} spy wins, {info.Stats.MercWins} merc wins, {info.Stats.Losses} losses"),
            ("Kills", N(info.Stats.Kills), $"{info.Stats.Deaths} deaths"),
            ("Achievements", $"{info.Achievements.Count(a => a.Earned is not null)} / {info.Achievements.Count}", null),
            ("Cosmetics", $"{info.Unlocked.Count} / {CosmeticCatalog.All.Count}", $"{_store.Grants(id).Count} given by admins")));
        if (where.Length > 0)
        {
            body.Append("<p class=\"notice\">Playing now on ").Append(where).Append(".</p>");
        }

        string self = $"/admin/players/{id}";
        body.Append(Section("Actions", "<div class=\"actions\">" +
            Form(c, self, Input("name", "New name", account.Name ?? string.Empty, maxLength: 16) + Button("Rename", "do", "rename")) +
            Form(c, self, Button("Release name", "do", "release"), "Take this account's name away? It picks a new one at its next sign-in.") +
            Form(c, self, Input("days", "Days (empty: permanent)", type: "number") + Input("reason", "Reason") + Button("Ban", "do", "ban", "danger"),
                "Ban this account (and its Discord id)?") +
            Form(c, self, Button("Unban", "do", "unban")) +
            Form(c, self, Button("Kick from server", "do", "kick"), "Kick this player from the server they are on?") +
            Form(c, self, Button("Sign out everywhere", "do", "signout"), "End every game sign-in of this account?") +
            Form(c, self, Select("what", CosmeticChoices()) + Button("Unlock", "do", "unlock") + Button("Take back", "do", "lock")) +
            "</div>"));

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
        body.Append(Section("Game sign-ins", Table(["Signed in", "Last used"], _store.Sessions(id).Select(s => new[]
        {
            H(Time(s.Created)), H(Ago(s.LastUsed, now)),
        }), "Not signed in anywhere.")));
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

        AdminResult result = Field(form, "do") switch
        {
            "rename" => _actions.Rename(account, Field(form, "name", 32)),
            "release" => _actions.ReleaseName(account),
            "ban" => _actions.Ban("#" + id.ToString(CultureInfo.InvariantCulture), Days(Field(form, "days")), Field(form, "reason")),
            "unban" => _actions.Unban(account.DiscordId),
            "kick" => KickAccount(id, "Kicked by an admin."),
            "signout" => AdminResult.Done($"Ended {_store.RevokeSessions(id)} game sign-in(s)."),
            "unlock" => _actions.Grant(account, [Field(form, "what")], unlock: true),
            "lock" => _actions.Grant(account, [Field(form, "what")], unlock: false),
            _ => AdminResult.Refused("Unknown action."),
        };
        return Player(c, admin, id, result.Message, !result.Ok);
    }

    private static double? Days(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0 ? d : null;

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

    private IResult Bans(HttpContext c, AdminSessionRow admin, string? notice, bool bad)
    {
        bool all = c.Request.Query["all"] == "1";
        int page = PageOf(c);
        List<BanRow> bans = _store.AllBans(all, page, PageSize + 1);
        long now = _store.Now;
        string add = Form(c, "/admin/bans",
            Input("target", "Name, #account, Discord id, IP or CIDR", required: true) + Input("days", "Days (empty: permanent)", type: "number") +
            Input("reason", "Reason") + Button("Ban", "do", "ban", "danger"), "Add this ban?", "bar");
        string filter = all ? Link("/admin/bans", "Only bans in force") : Link("/admin/bans?all=1", "Show ended bans too");
        string table = Table(["#", "On", "Reason", "Until", "Added", ""], bans.Take(PageSize).Select(b => new[]
        {
            "#" + b.Id.ToString(CultureInfo.InvariantCulture),
            b.AccountId is { } a ? Link($"/admin/players/{a}", _actions.BanTarget(b)) : H(_actions.BanTarget(b)),
            H(b.Reason), H(b.Until is { } u ? (u <= now ? "ended " : string.Empty) + Time(u) : "permanent"), H(Time(b.Added)),
            b.Until is { } end && end <= now ? string.Empty
                : Form(c, "/admin/bans", $"<input type=\"hidden\" name=\"target\" value=\"#{b.Id}\">" + Button("End", "do", "unban"), "End this ban?", "inline"),
        }), "No bans.");
        return Page(c, "Bans", "/admin/bans", admin.Name, add + $"<p>{filter}</p>" + table +
            Pager(all ? "/admin/bans?all=1" : "/admin/bans", page, bans.Count > PageSize), notice, bad);
    }

    private IResult BanAction(HttpContext c, AdminSessionRow admin, IFormCollection form)
    {
        string target = Field(form, "target", 64);
        AdminResult result = Field(form, "do") == "unban"
            ? _actions.Unban(target)
            : target.Length == 0 ? AdminResult.Refused("Say who to ban.") : _actions.Ban(target, Days(Field(form, "days")), Field(form, "reason"));
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
        List<MatchRow> rows = _store.Matches(page, PageSize + 1);
        return Page(c, "Matches", "/admin/matches", admin.Name,
            "<p class=\"muted\">Every round a trusted server reported. Counted rounds (a winner and a signed-in player on each side) " +
            "add to stats and achievements.</p>" + MatchTable(rows.Take(PageSize)) + Pager("/admin/matches", page, rows.Count > PageSize));
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
            }));
        return Page(c, $"Round #{id}", "/admin/matches", admin.Name, body);
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
            Section("Master", form) + Section("Announcement", announce) +
            Section("Blocked servers", blocks + Form(c, "/admin/servers/blocks",
                Select("kind", [("ip", "Address"), ("id", "Server id")]) + Input("value", "Address or server id", required: true) +
                Input("reason", "Reason") + Button("Block", "do", "block", "danger"), "Refuse this server's heartbeats?", "bar")) +
            Section("Admins (from the config file's Admins list)", admins), notice, bad);
    }

    private IResult SettingsAction(HttpContext c, AdminSessionRow admin, IFormCollection form)
    {
        switch (Field(form, "do"))
        {
            case "save":
                foreach (string key in new[] { "motd", "latestVersion", "minVersion" })
                {
                    string value = Field(form, key, 300);
                    if (key != "motd" && value.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d+(\.\d+){0,3}$"))
                    {
                        return Settings(c, admin, $"'{value}' is not a version (like 0.6.3).", true);
                    }

                    _store.SetSetting(key, value);
                }

                MasterSettings.Apply(_store, _options);
                return Settings(c, admin, "Saved.", false);
            case "reset":
                foreach (string key in new[] { "motd", "latestVersion", "minVersion" })
                {
                    _store.SetSetting(key, null);
                }

                MasterSettings.Apply(_store, _options);
                return Settings(c, admin, "The config file's values apply again (after a restart, if they were changed here before).", false);
            case "announce":
                string text = Field(form, "text", 150);
                int sent = text.Length == 0 ? 0 : QueueForAll("say " + text);
                _store.Audit("announce", $"{sent} server(s): {text}");
                return Settings(c, admin, $"Sent to {sent} trusted server(s).", sent == 0);
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
        string q = c.Request.Query["q"].ToString();
        int page = PageOf(c);
        List<AuditRow> rows = _store.AuditPage(q, page, PageSize + 1);
        string search = "<form method=\"get\" action=\"/admin/audit\" class=\"bar\">" + Input("q", "Filter: action, who or text", q) + Button("Filter") + "</form>";
        return Page(c, "Audit log", "/admin/audit", admin.Name, search + AuditTable(rows.Take(PageSize)) +
            Pager("/admin/audit?q=" + U(q), page, rows.Count > PageSize));
    }
}
