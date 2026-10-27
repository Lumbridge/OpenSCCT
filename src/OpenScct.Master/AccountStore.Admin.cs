using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Microsoft.Data.Sqlite;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

/// <summary>A signed-in admin of the web portal.</summary>
internal sealed record AdminSessionRow(string DiscordId, string Name, string Csrf, long Created, long LastUsed);

/// <summary>One stored round (every report from a trusted server, counted or not).</summary>
internal sealed record MatchRow(
    long Id, long Time, string ServerId, string ServerName, string Map, int Mode, int Winner, int Reason, int ObjectivesTaken,
    int ObjectivesNeeded, int DurationSeconds, bool Counted);

/// <summary>One player's line of a stored round.</summary>
internal sealed record MatchPlayerRow(long MatchId, long? AccountId, string Name, int Team, int Score, int Kills, int Deaths, bool Bot,
    IReadOnlyDictionary<string, int> Feats);

/// <summary>One sample of what the master saw (players online, servers, hubs, relay sessions).</summary>
internal sealed record MetricRow(long Time, int Players, int Servers, int Hubs, int RelaySessions);

/// <summary>A server the master refuses (by server id or public address).</summary>
internal sealed record BlockRow(long Id, string Kind, string Value, string Reason, long Added);

/// <summary>A game sign-in session, as the portal lists it.</summary>
internal sealed record SessionRow(long Created, long LastUsed);

/// <summary>An account in the portal's player list, with its totals.</summary>
internal sealed record AccountListRow(AccountRow Account, int Matches, int Kills, bool Banned);

// The admin portal's tables and queries (schema v4, docs/ONLINE.md "Admin portal").
internal sealed partial class AccountStore
{
    /// <summary>Seconds an admin portal session lasts without use.</summary>
    public const long AdminSessionIdle = 12 * 3600;

    /// <summary>Seconds an admin portal session lasts at most.</summary>
    public const long AdminSessionMax = 7 * 86400;

    /// <summary>Days of samples kept for the history charts.</summary>
    public const int MetricDays = 90;

    private static readonly AsyncLocal<string?> s_actor = new();

    /// <summary>Who the audit lines written now are by ("admin:Ryan", "cli"; "master" when unset).</summary>
    public static string CurrentActor => s_actor.Value ?? "master";

    /// <summary>Writes the audit lines of the calling flow (this request, this command) as <paramref name="actor"/>.</summary>
    public static IDisposable ActingAs(string actor)
    {
        string? before = s_actor.Value;
        s_actor.Value = actor;
        return new Restore(() => s_actor.Value = before);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    private void MigrateVersion4()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS admin_sessions (
                token_hash TEXT PRIMARY KEY,
                discord_id TEXT NOT NULL,
                name TEXT NOT NULL,
                csrf TEXT NOT NULL,
                created INTEGER NOT NULL,
                last_used INTEGER NOT NULL,
                ip TEXT,
                agent TEXT);
            CREATE TABLE IF NOT EXISTS matches (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                time INTEGER NOT NULL,
                server_id TEXT NOT NULL,
                server_name TEXT NOT NULL,
                map TEXT NOT NULL,
                mode INTEGER NOT NULL,
                winner INTEGER NOT NULL,
                reason INTEGER NOT NULL,
                objectives_taken INTEGER NOT NULL,
                objectives_needed INTEGER NOT NULL,
                duration_s INTEGER NOT NULL,
                counted INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS matches_time ON matches(time);
            CREATE TABLE IF NOT EXISTS match_rows (
                match_id INTEGER NOT NULL REFERENCES matches(id),
                account_id INTEGER,
                name TEXT NOT NULL,
                team INTEGER NOT NULL,
                score INTEGER NOT NULL,
                kills INTEGER NOT NULL,
                deaths INTEGER NOT NULL,
                bot INTEGER NOT NULL,
                feats TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS match_rows_match ON match_rows(match_id);
            CREATE INDEX IF NOT EXISTS match_rows_account ON match_rows(account_id);
            CREATE TABLE IF NOT EXISTS metrics (
                time INTEGER PRIMARY KEY,
                players INTEGER NOT NULL,
                servers INTEGER NOT NULL,
                hubs INTEGER NOT NULL,
                relay_sessions INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS blocked_servers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                kind TEXT NOT NULL,
                value TEXT NOT NULL,
                reason TEXT NOT NULL,
                added INTEGER NOT NULL);
            """);
        bool hasActor = Query("PRAGMA table_info(audit);", r => r.GetString(1)).Contains("actor");
        if (!hasActor)
        {
            Exec("ALTER TABLE audit ADD COLUMN actor TEXT;");
        }
    }

    private static AuditRow ReadAudit(SqliteDataReader r) =>
        new(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4));

    // ---- Admin portal sessions ---------------------------------------------------------------------------------

    /// <summary>A new portal session for an admin: the cookie's token (only its hash is kept).</summary>
    public string CreateAdminSession(string discordId, string name, string? ip, string? agent)
    {
        string token = NewToken();
        lock (_gate)
        {
            long now = Now;
            Exec("DELETE FROM admin_sessions WHERE last_used < $idle OR created < $max;",
                ("$idle", now - AdminSessionIdle), ("$max", now - AdminSessionMax));
            Exec("INSERT INTO admin_sessions (token_hash, discord_id, name, csrf, created, last_used, ip, agent) " +
                "VALUES ($h, $d, $n, $c, $now, $now, $ip, $ua);",
                ("$h", Hash(token)), ("$d", discordId), ("$n", name), ("$c", NewToken()), ("$now", now), ("$ip", ip),
                ("$ua", agent is { Length: > 200 } ? agent[..200] : agent));
        }

        return token;
    }

    /// <summary>The portal session of a cookie token, or null (unknown, idle too long or too old). Marks it used.</summary>
    public AdminSessionRow? AdminSession(string token)
    {
        string hash = Hash(token);
        lock (_gate)
        {
            long now = Now;
            AdminSessionRow? row = One("SELECT discord_id, name, csrf, created, last_used FROM admin_sessions WHERE token_hash = $h;",
                r => new AdminSessionRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4)), ("$h", hash));
            if (row is null)
            {
                return null;
            }

            if (now - row.LastUsed > AdminSessionIdle || now - row.Created > AdminSessionMax)
            {
                Exec("DELETE FROM admin_sessions WHERE token_hash = $h;", ("$h", hash));
                return null;
            }

            if (now - row.LastUsed >= TouchSeconds)
            {
                Exec("UPDATE admin_sessions SET last_used = $now WHERE token_hash = $h;", ("$h", hash), ("$now", now));
            }

            return row;
        }
    }

    public void EndAdminSession(string token)
    {
        lock (_gate)
        {
            Exec("DELETE FROM admin_sessions WHERE token_hash = $h;", ("$h", Hash(token)));
        }
    }

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ---- Accounts for the portal -------------------------------------------------------------------------------

    /// <summary>
    /// Accounts matching <paramref name="text"/> (part of the name, a Discord id or an account id; empty: all),
    /// newest-seen first or newest first, one page; and how many match in all.
    /// </summary>
    public (List<AccountListRow> Rows, int Total) SearchAccounts(string? text, bool byCreated, int page, int pageSize)
    {
        string where = "1 = 1";
        var args = new List<(string, object?)>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            string t = text.Trim();
            where = "(a.name LIKE $like ESCAPE '\\' OR a.discord_id = $exact OR CAST(a.id AS TEXT) = $exact)";
            args.Add(("$like", "%" + t.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%"));
            args.Add(("$exact", t));
        }

        lock (_gate)
        {
            int total = Convert.ToInt32(Scalar($"SELECT COUNT(*) FROM accounts a WHERE {where};", [.. args]), CultureInfo.InvariantCulture);
            var pageArgs = new List<(string, object?)>(args) { ("$n", pageSize), ("$o", Math.Max(0, page) * pageSize), ("$now", Now) };
            List<AccountListRow> rows = Query(
                "SELECT a.id, a.discord_id, a.name, a.created, a.last_seen, a.name_changed, COALESCE(s.matches, 0), COALESCE(s.kills, 0), " +
                "EXISTS (SELECT 1 FROM bans b WHERE (b.account_id = a.id OR b.discord_id = a.discord_id) AND (b.until IS NULL OR b.until > $now)) " +
                $"FROM accounts a LEFT JOIN stats s ON s.account_id = a.id WHERE {where} " +
                $"ORDER BY {(byCreated ? "a.created" : "a.last_seen")} DESC, a.id DESC LIMIT $n OFFSET $o;",
                r => new AccountListRow(ReadAccount(r), r.GetInt32(6), r.GetInt32(7), r.GetInt64(8) != 0), [.. pageArgs]);
            return (rows, total);
        }
    }

    /// <summary>Accounts in all, and those made since <paramref name="since"/>.</summary>
    public (int Total, int Since) CountAccounts(long since)
    {
        lock (_gate)
        {
            return (Convert.ToInt32(Scalar("SELECT COUNT(*) FROM accounts;"), CultureInfo.InvariantCulture),
                Convert.ToInt32(Scalar("SELECT COUNT(*) FROM accounts WHERE created >= $s;", ("$s", since)), CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The account's game sign-in sessions, newest use first.</summary>
    public List<SessionRow> Sessions(long accountId)
    {
        lock (_gate)
        {
            return Query("SELECT created, last_used FROM sessions WHERE account_id = $a ORDER BY last_used DESC;",
                r => new SessionRow(r.GetInt64(0), r.GetInt64(1)), ("$a", accountId));
        }
    }

    /// <summary>Signs the account out of the game everywhere; returns how many sessions ended.</summary>
    public int RevokeSessions(long accountId)
    {
        lock (_gate)
        {
            int ended = Exec("DELETE FROM sessions WHERE account_id = $a;", ("$a", accountId));
            AuditLocked("signout", $"account {accountId}: {ended} session(s) ended");
            return ended;
        }
    }

    /// <summary>Bans in force (or with <paramref name="includeEnded"/> every ban), newest first, one page.</summary>
    public List<BanRow> AllBans(bool includeEnded, int page, int pageSize)
    {
        lock (_gate)
        {
            return Query(
                $"SELECT {BanColumns} FROM bans {(includeEnded ? string.Empty : "WHERE until IS NULL OR until > $now ")}" +
                "ORDER BY id DESC LIMIT $n OFFSET $o;",
                ReadBan, ("$now", Now), ("$n", pageSize), ("$o", Math.Max(0, page) * pageSize));
        }
    }

    /// <summary>Every ban of an account (by account or its Discord id), newest first.</summary>
    public List<BanRow> BansOf(AccountRow account)
    {
        lock (_gate)
        {
            return Query($"SELECT {BanColumns} FROM bans WHERE account_id = $a OR discord_id = $d ORDER BY id DESC;", ReadBan,
                ("$a", account.Id), ("$d", account.DiscordId));
        }
    }

    // ---- Matches -----------------------------------------------------------------------------------------------

    private const string MatchColumns =
        "id, time, server_id, server_name, map, mode, winner, reason, objectives_taken, objectives_needed, duration_s, counted";

    private static MatchRow ReadMatch(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7),
        r.GetInt32(8), r.GetInt32(9), r.GetInt32(10), r.GetInt64(11) != 0);

    /// <summary>Keeps a reported round and its players; returns its id.</summary>
    public long RecordMatch(ResultsReport report, string serverName, bool counted)
    {
        lock (_gate)
        {
            return InTransaction(() =>
            {
                Exec("INSERT INTO matches (time, server_id, server_name, map, mode, winner, reason, objectives_taken, objectives_needed, " +
                    "duration_s, counted) VALUES ($t, $s, $sn, $m, $mo, $w, $r, $ot, $on, $d, $c);",
                    ("$t", Now), ("$s", report.ServerId), ("$sn", serverName), ("$m", report.Map), ("$mo", report.Mode),
                    ("$w", report.WinningTeam), ("$r", report.WinningReason), ("$ot", report.ObjectivesTaken),
                    ("$on", report.ObjectivesNeeded), ("$d", Math.Clamp(report.DurationSeconds, 0, 86400)), ("$c", counted ? 1 : 0));
                long id = Convert.ToInt64(Scalar("SELECT last_insert_rowid();"), CultureInfo.InvariantCulture);
                foreach (ResultsReportRow row in report.Rows)
                {
                    string name = row.Name ?? string.Empty;
                    Exec("INSERT INTO match_rows (match_id, account_id, name, team, score, kills, deaths, bot, feats) " +
                        "VALUES ($m, $a, $n, $t, $s, $k, $d, $b, $f);",
                        ("$m", id), ("$a", row.AccountId), ("$n", name.Length > 64 ? name[..64] : name), ("$t", row.Team),
                        ("$s", row.Score), ("$k", row.Kills), ("$d", row.Deaths), ("$b", row.Bot ? 1 : 0),
                        ("$f", JsonSerializer.Serialize(row.Feats ?? new Dictionary<string, int>())));
                }

                return id;
            });
        }
    }

    /// <summary>Stored rounds, newest first (optionally only those an account played in).</summary>
    public List<MatchRow> Matches(int page, int pageSize, long? accountId = null)
    {
        lock (_gate)
        {
            string where = accountId is null ? string.Empty : "WHERE id IN (SELECT match_id FROM match_rows WHERE account_id = $a) ";
            return Query($"SELECT {MatchColumns} FROM matches {where}ORDER BY id DESC LIMIT $n OFFSET $o;", ReadMatch,
                ("$a", accountId), ("$n", pageSize), ("$o", Math.Max(0, page) * pageSize));
        }
    }

    public (MatchRow Match, List<MatchPlayerRow> Rows)? Match(long id)
    {
        lock (_gate)
        {
            MatchRow? match = One($"SELECT {MatchColumns} FROM matches WHERE id = $id;", ReadMatch, ("$id", id));
            if (match is null)
            {
                return null;
            }

            List<MatchPlayerRow> rows = Query(
                "SELECT match_id, account_id, name, team, score, kills, deaths, bot, feats FROM match_rows WHERE match_id = $id " +
                "ORDER BY team, score DESC;",
                r => new MatchPlayerRow(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4),
                    r.GetInt32(5), r.GetInt32(6), r.GetInt64(7) != 0,
                    JsonSerializer.Deserialize<Dictionary<string, int>>(r.GetString(8)) ?? new Dictionary<string, int>()),
                ("$id", id));
            return (match, rows);
        }
    }

    /// <summary>Rounds stored since <paramref name="since"/>.</summary>
    public int CountMatches(long since)
    {
        lock (_gate)
        {
            return Convert.ToInt32(Scalar("SELECT COUNT(*) FROM matches WHERE time >= $s;", ("$s", since)), CultureInfo.InvariantCulture);
        }
    }

    // ---- Metrics -----------------------------------------------------------------------------------------------

    /// <summary>Keeps one sample and drops those older than <see cref="MetricDays"/>.</summary>
    public void AddMetric(int players, int servers, int hubs, int relaySessions)
    {
        lock (_gate)
        {
            long now = Now;
            Exec("INSERT OR REPLACE INTO metrics (time, players, servers, hubs, relay_sessions) VALUES ($t, $p, $s, $h, $r);",
                ("$t", now), ("$p", players), ("$s", servers), ("$h", hubs), ("$r", relaySessions));
            Exec("DELETE FROM metrics WHERE time < $old;", ("$old", now - (MetricDays * 86400L)));
        }
    }

    /// <summary>Samples since <paramref name="from"/>, averaged (players: the peak) per <paramref name="bucket"/> seconds.</summary>
    public List<MetricRow> Metrics(long from, int bucket)
    {
        lock (_gate)
        {
            return Query(
                "SELECT (time / $b) * $b AS t, MAX(players), CAST(ROUND(AVG(servers)) AS INTEGER), CAST(ROUND(AVG(hubs)) AS INTEGER), " +
                "MAX(relay_sessions) FROM metrics WHERE time >= $f GROUP BY t ORDER BY t;",
                r => new MetricRow(r.GetInt64(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4)),
                ("$b", Math.Max(60, bucket)), ("$f", from));
        }
    }

    // ---- Settings ----------------------------------------------------------------------------------------------

    public Dictionary<string, string> Settings()
    {
        lock (_gate)
        {
            var all = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string key, string value) in Query("SELECT key, value FROM settings;", r => (r.GetString(0), r.GetString(1))))
            {
                all[key] = value;
            }

            return all;
        }
    }

    /// <summary>Stores a setting (null removes it, so the config's value applies again).</summary>
    public void SetSetting(string key, string? value)
    {
        lock (_gate)
        {
            if (value is null)
            {
                Exec("DELETE FROM settings WHERE key = $k;", ("$k", key));
            }
            else
            {
                Exec("INSERT INTO settings (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v;", ("$k", key), ("$v", value));
            }

            AuditLocked("setting", $"{key} = {value ?? "(config)"}");
        }
    }

    // ---- Blocked servers ---------------------------------------------------------------------------------------

    public List<BlockRow> Blocks()
    {
        lock (_gate)
        {
            return Query("SELECT id, kind, value, reason, added FROM blocked_servers ORDER BY id DESC;",
                r => new BlockRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4)));
        }
    }

    /// <summary>Refuses a server by id (<paramref name="kind"/> "id") or public address ("ip").</summary>
    public void AddBlock(string kind, string value, string reason)
    {
        lock (_gate)
        {
            Exec("INSERT INTO blocked_servers (kind, value, reason, added) VALUES ($k, $v, $r, $now);",
                ("$k", kind), ("$v", value), ("$r", reason), ("$now", Now));
            AuditLocked("block", $"server {kind} {value}: {reason}");
        }
    }

    public void RemoveBlock(long id)
    {
        lock (_gate)
        {
            Exec("DELETE FROM blocked_servers WHERE id = $id;", ("$id", id));
            AuditLocked("unblock", $"#{id}");
        }
    }

    // ---- Audit for the portal ----------------------------------------------------------------------------------

    /// <summary>Audit rows newest first, optionally only one action or actor (part of it), one page.</summary>
    public List<AuditRow> AuditPage(string? filter, int page, int pageSize)
    {
        lock (_gate)
        {
            string where = string.IsNullOrWhiteSpace(filter) ? string.Empty : "WHERE action LIKE $f OR actor LIKE $f OR detail LIKE $f ";
            return Query($"SELECT id, time, action, detail, COALESCE(actor, '') FROM audit {where}ORDER BY id DESC LIMIT $n OFFSET $o;",
                ReadAudit, ("$f", "%" + filter?.Trim() + "%"), ("$n", pageSize), ("$o", Math.Max(0, page) * pageSize));
        }
    }
}
