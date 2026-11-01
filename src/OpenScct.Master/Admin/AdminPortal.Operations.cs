using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using static OpenScct.Master.Admin.AdminHtml;

namespace OpenScct.Master.Admin;

internal sealed partial class AdminPortal
{
    private static string Query(HttpContext context, string key, int max = 100)
    {
        string value = context.Request.Query[key].ToString().Trim();
        return value.Length > max ? value[..max] : value;
    }

    private static bool ValidVersion(string value)
    {
        string[] parts = value.Split('.');
        return parts.Length is >= 1 and <= 3 && parts.All(part =>
            int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }

    private IResult Search(HttpContext context, AdminSessionRow admin)
    {
        string query = Query(context, "q");
        string form = "<form action=\"/admin/search\" method=\"get\" class=\"bar\">" +
            Input("q", "Player name, account id, server, map or match id", query, maxLength: 100) + Button("Search") + "</form>";
        if (query.Length == 0)
        {
            return Page(context, "Search", string.Empty, admin.Name, form + "<p class=\"muted\">Find players, live servers and recorded matches in one place.</p>");
        }

        (List<AccountListRow> accounts, int total) = _store.SearchAccounts(query, false, 0, 20);
        string players = Table(["Player", "Account", "Discord", "Status"], accounts.Select(r => new[]
        {
            Link($"/admin/players/{r.Account.Id}", r.Account.Name ?? "Unnamed account"), N(r.Account.Id), H(r.Account.DiscordId),
            r.Banned ? Badge("banned", "bad") : Badge("active", "ok"),
        }), "No matching players.");
        List<ServerView> servers = Snapshot().Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            s.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || s.Map.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        return Page(context, "Search results", string.Empty, admin.Name, form +
            Section($"Players ({total})", players + Link("/admin/players?q=" + U(query), "View all matching players")) +
            Section("Live servers", ServerTable(servers)) + Section("Recorded matches", MatchTable(_store.SearchAdminMatches(query.TrimStart('#'), string.Empty, 0, 20))));
    }

    private IResult Commands(HttpContext context, AdminSessionRow admin)
    {
        string server = Query(context, "server");
        string state = Query(context, "state");
        bool pending = state == "pending";
        if (state is not ("pending" or "expired" or "interrupted" or "response-received")) state = string.Empty;
        int page = PageOf(context);
        string form = "<form method=\"get\" action=\"/admin/commands\" class=\"bar\">" +
            Input("server", "Server id (optional)", server) + Select("state", [("", "All states"), ("pending", "Awaiting dispatch or response"),
                ("response-received", "Response received"), ("expired", "Expired"), ("interrupted", "Interrupted")], state) + Button("Filter") + "</form>";
        List<AdminCommandRow> commands = _store.AdminCommands(server.Length > 0 ? server : null, PageSize, pending, page,
            pending ? string.Empty : state, lookAhead: true);
        return Page(context, "Command history", "/admin/commands", admin.Name, form +
            "<p class=\"muted\">A received response records what the server printed; it does not imply success. Interrupted commands are never replayed.</p>" +
            Section("Commands", CommandTable(commands.Take(PageSize)) + Pager("/admin/commands?server=" + U(server) + "&state=" + U(state), page, commands.Count > PageSize)), refresh: true);
    }

    private static string CommandTable(IEnumerable<AdminCommandRow> rows) => Table(["Queued", "Server", "Operator", "Command", "State", "Response"], rows.Select(r => new[]
    {
        H(Time(r.Created)), Link("/admin/servers/" + U(r.ServerId), r.ServerName), H(r.Actor), "<code>" + H(r.Line) + "</code>",
        Badge(r.State, r.State is "expired" or "interrupted" ? "warn" : string.Empty),
        "<pre>" + H(r.Output.Length > 0 ? r.Output : r.State == "response-received" ? "Response received (no output)." :
            "Awaiting " + (r.State == "queued" ? "dispatch" : "response")) + "</pre>",
    }), "No commands recorded.");

    private string OperationsIssues(IReadOnlyList<ServerView> servers)
    {
        var rows = new List<string[]>();
        foreach (ServerView server in servers)
        {
            if (server.SinceHeartbeat > _options.HeartbeatSeconds * 2)
            {
                rows.Add([Badge("heartbeat", "warn"), Link("/admin/servers/" + U(server.Id), server.Name), H("Last received " + Duration(server.SinceHeartbeat) + " ago")]);
            }

            if (!string.IsNullOrEmpty(_options.LatestVersion) && !string.Equals(server.Version, _options.LatestVersion, StringComparison.Ordinal))
            {
                rows.Add([Badge("version", "warn"), Link("/admin/servers/" + U(server.Id), server.Name), H(server.Version + " · advertised latest " + _options.LatestVersion)]);
            }
        }

        foreach (AdminCommandRow command in _store.AdminCommands(limit: 20, pendingOnly: true))
        {
            rows.Add([Badge(command.State), Link("/admin/commands?server=" + U(command.ServerId), command.ServerName), H(command.Line)]);
        }

        return Table(["Attention", "Target", "Detail"], rows, "All clear. No stale heartbeats, version mismatches or pending commands.");
    }

    private static string BanDurationFields(string unit = "days", string initial = "7") =>
        "<label class=\"field\"><span>Duration</span>" + Select("duration", [("temporary", "Temporary"), ("permanent", "Permanent")], "temporary") + "</label>" +
        "<label class=\"field\"><span>" + H(unit) + " (temporary only)</span>" + Input(unit, unit, initial, "number") + "</label>";

    private static bool TryBanDuration(IFormCollection form, string unit, out double? amount, out string error)
    {
        amount = null;
        error = string.Empty;
        if (Field(form, "duration") == "permanent")
        {
            return true;
        }

        if (Field(form, "duration") != "temporary" ||
            !double.TryParse(Field(form, unit), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value) ||
            !double.IsFinite(value) || value <= 0 || value > (unit == "days" ? 36500 : 52560000) ||
            (unit == "minutes" && value != Math.Truncate(value)))
        {
            error = "Choose Permanent explicitly, or enter a positive temporary duration in " + unit + ".";
            return false;
        }

        amount = value;
        return true;
    }

    private AdminResult BanFromForm(string target, IFormCollection form)
    {
        if (!TryBanDuration(form, "days", out double? days, out string error))
        {
            return AdminResult.Refused(error);
        }

        string reason = Field(form, "reason");
        if (reason.Length == 0)
        {
            return AdminResult.Refused("Enter a reason for the ban.");
        }

        return _actions.Ban(target, days, reason);
    }

    private List<BanRow> BanAuditSnapshot(string target, bool banId)
    {
        List<BanRow> active = _store.ActiveBans();
        if (banId && target.StartsWith('#') && long.TryParse(target.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out long id))
        {
            return active.Where(b => b.Id == id).ToList();
        }

        if (AccountStore.NormalizeIp(target) is { } ip) return active.Where(b => b.Ip == ip).ToList();
        if (_actions.FindAccount(target) is { } account) return active.Where(b => b.AccountId == account.Id || b.DiscordId == account.DiscordId).ToList();
        return active.Where(b => b.DiscordId == target).ToList();
    }

    private object PlayerAuditSnapshot(AccountRow account) => new
    {
        Account = account.Id, Name = _store.FindById(account.Id)?.Name,
        Progress = _store.ProgressInfo(account.Id), Grants = _store.Grants(account.Id),
        Visibility = _store.ProfileVisibility(account.Id),
        Bans = _store.BansOf(account), Sessions = _store.PlayerSessions(account.Id).Count,
    };

    private string PlayerSessions(HttpContext context, long accountId)
    {
        string self = $"/admin/players/{accountId}";
        return Table(["Session", "Signed in", "Last active", ""], _store.PlayerSessions(accountId).Select(s => new[]
        {
            H(s.Kind == "web" ? "Player website" : "Game"), H(Time(s.Created)), H(Ago(s.LastUsed, _store.Now)),
            Form(context, self, $"<input type=\"hidden\" name=\"session\" value=\"{H(s.Id)}\"><input type=\"hidden\" name=\"kind\" value=\"{H(s.Kind)}\">" +
                Button("Revoke", "do", "revoke"), "Sign this session out?"),
        }), "No active player sessions.");
    }

    private static long? ParseDate(string date, bool end = false) =>
        DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime value)
            ? new DateTimeOffset(end ? value.AddDays(1) : value).ToUnixTimeSeconds() : null;

    private static string DetailedAuditTable(IEnumerable<AuditDetailRow> rows) => Table(["When", "Operator", "Action", "Target / details"], rows.Select(r => new[]
    {
        H(Time(r.Time)), H(r.Actor), H(r.Action), H(r.Detail) + (r.Before.Length + r.After.Length == 0 ? string.Empty :
            "<details class=\"audit-diff\"><summary>Before / after</summary><strong>Before</strong><pre>" + H(r.Before) + "</pre><strong>After</strong><pre>" + H(r.After) + "</pre></details>"),
    }), "No matching audit events.");
}
