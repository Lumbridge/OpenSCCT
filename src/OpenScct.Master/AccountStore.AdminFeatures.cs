using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace OpenScct.Master;

internal sealed record AdminCommandRow(long Id, string ServerId, string ServerName, string Actor, string Line,
    string State, long Created, long Updated, string Output);

internal sealed record AuditDetailRow(long Id, long Time, string Action, string Detail, string Actor,
    string Target, string Reason, string Before, string After);

internal sealed partial class AccountStore
{
    private void MigrateAdminFeatures()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS admin_commands (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                server_id TEXT NOT NULL,
                server_name TEXT NOT NULL,
                actor TEXT NOT NULL,
                line TEXT NOT NULL,
                state TEXT NOT NULL,
                created INTEGER NOT NULL,
                updated INTEGER NOT NULL,
                output TEXT NOT NULL DEFAULT '');
            CREATE INDEX IF NOT EXISTS admin_commands_server ON admin_commands(server_id, id);
            CREATE INDEX IF NOT EXISTS admin_commands_state ON admin_commands(state, updated);
            CREATE TABLE IF NOT EXISTS audit_metadata (
                audit_id INTEGER PRIMARY KEY REFERENCES audit(id),
                target TEXT NOT NULL,
                reason TEXT NOT NULL,
                before_json TEXT NOT NULL,
                after_json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS audit_metadata_target ON audit_metadata(target, audit_id);
            """);
    }

    public void AuditChange(string action, string target, string reason, object? before, object? after)
    {
        lock (_gate)
        {
            InTransaction(() =>
            {
                AuditLocked(action, target + (reason.Length > 0 ? ": " + reason : string.Empty));
                long id = Convert.ToInt64(Scalar("SELECT last_insert_rowid();"), CultureInfo.InvariantCulture);
                Exec("INSERT INTO audit_metadata (audit_id, target, reason, before_json, after_json) VALUES ($id, $t, $r, $b, $a);",
                    ("$id", id), ("$t", target), ("$r", reason), ("$b", JsonSerializer.Serialize(before)), ("$a", JsonSerializer.Serialize(after)));
                return 0;
            });
        }
    }

    public List<AuditDetailRow> DetailedAudit(string query, string actor, string action, string target, long? from, long? until,
        int page, int size, bool lookAhead = false)
    {
        lock (_gate)
        {
            return Query("""
                SELECT a.id, a.time, a.action, a.detail, COALESCE(a.actor,''), COALESCE(m.target,''),
                    COALESCE(m.reason,''), COALESCE(m.before_json,''), COALESCE(m.after_json,'')
                FROM audit a LEFT JOIN audit_metadata m ON m.audit_id = a.id
                WHERE ($q = '' OR a.detail LIKE $like OR a.action LIKE $like OR a.actor LIKE $like)
                  AND ($actor = '' OR a.actor LIKE $actorLike)
                  AND ($action = '' OR a.action LIKE $actionLike)
                  AND ($target = '' OR m.target = $target)
                  AND ($from IS NULL OR a.time >= $from) AND ($until IS NULL OR a.time < $until)
                ORDER BY a.id DESC LIMIT $n OFFSET $o;
                """, r => new AuditDetailRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4),
                    r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8)),
                ("$q", query), ("$like", "%" + query + "%"), ("$actor", actor), ("$actorLike", "%" + actor + "%"),
                ("$action", action), ("$actionLike", "%" + action + "%"), ("$target", target), ("$from", from), ("$until", until),
                ("$n", Math.Clamp(size, 1, 100) + (lookAhead ? 1 : 0)), ("$o", Math.Clamp(page, 0, 100000) * Math.Clamp(size, 1, 100)));
        }
    }

    /// <summary>All validated settings take effect together, with one structured audit entry.</summary>
    public void SetSettingsAtomic(IReadOnlyDictionary<string, string?> values)
    {
        lock (_gate)
        {
            InTransaction(() =>
            {
                Dictionary<string, string> before = Settings();
                foreach ((string key, string? value) in values)
                {
                    if (value is null)
                    {
                        Exec("DELETE FROM settings WHERE key = $k;", ("$k", key));
                    }
                    else
                    {
                        Exec("INSERT INTO settings (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v;",
                            ("$k", key), ("$v", value));
                    }
                }

                AuditChange("settings.update", "settings", string.Empty, before, Settings());
                return 0;
            });
        }
    }

    public List<MatchRow> SearchAdminMatches(string query, string counted, int page, int pageSize, bool lookAhead = false)
    {
        lock (_gate)
        {
            return Query($"""
                SELECT {MatchColumns} FROM matches
                WHERE ($q = '' OR map LIKE $like OR server_name LIKE $like OR CAST(id AS TEXT) = $q)
                  AND ($counted = '' OR counted = $c)
                ORDER BY id DESC LIMIT $n OFFSET $o;
                """, ReadMatch, ("$q", query), ("$like", "%" + query + "%"), ("$counted", counted),
                ("$c", counted == "yes" ? 1 : 0), ("$n", pageSize + (lookAhead ? 1 : 0)), ("$o", Math.Max(0, page) * pageSize));
        }
    }

    public long AddAdminCommand(string serverId, string serverName, string line)
    {
        lock (_gate)
        {
            Exec("INSERT INTO admin_commands (server_id, server_name, actor, line, state, created, updated) " +
                "VALUES ($s, $n, $a, $l, 'queued', $t, $t);", ("$s", serverId), ("$n", serverName),
                ("$a", CurrentActor), ("$l", line), ("$t", Now));
            return Convert.ToInt64(Scalar("SELECT last_insert_rowid();"), CultureInfo.InvariantCulture);
        }
    }

    public bool DispatchAdminCommand(string serverId, long id)
    {
        lock (_gate)
        {
            return Exec("UPDATE admin_commands SET state = 'dispatched', updated = $t WHERE id = $id AND server_id = $s AND state = 'queued';",
                ("$t", Now), ("$id", id), ("$s", serverId)) == 1;
        }
    }

    public bool CompleteAdminCommand(string serverId, long id, string output)
    {
        lock (_gate)
        {
            return Exec("UPDATE admin_commands SET state = 'response-received', output = $o, updated = $t " +
                "WHERE id = $id AND server_id = $s AND state = 'dispatched';", ("$o", output), ("$t", Now), ("$id", id), ("$s", serverId)) != 0;
        }
    }

    public void InterruptAdminCommands()
    {
        lock (_gate)
        {
            Exec("UPDATE admin_commands SET state = 'interrupted', updated = $t, output = 'Master restarted; command was not replayed.' " +
                "WHERE state IN ('queued', 'dispatched');", ("$t", Now));
        }
    }

    public void ExpireAdminCommands()
    {
        lock (_gate)
        {
            Exec("UPDATE admin_commands SET state = 'expired', updated = $t, " +
                "output = CASE WHEN state = 'queued' THEN 'Not dispatched before the deadline.' ELSE 'No response received; execution is unknown.' END " +
                "WHERE state IN ('queued', 'dispatched') AND updated < $cut;", ("$t", Now), ("$cut", Now - 300));
        }
    }

    public List<AdminCommandRow> AdminCommands(string? serverId = null, int limit = 50, bool pendingOnly = false,
        int page = 0, string state = "", bool lookAhead = false)
    {
        lock (_gate)
        {
            return Query("SELECT id, server_id, server_name, actor, line, state, created, updated, output FROM admin_commands " +
                "WHERE ($s IS NULL OR server_id = $s) AND ($pending = 0 OR state IN ('queued','dispatched')) " +
                "AND ($state = '' OR state = $state) ORDER BY id DESC LIMIT $n OFFSET $offset;",
                r => new AdminCommandRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                    r.GetString(5), r.GetInt64(6), r.GetInt64(7), r.GetString(8)),
                ("$s", serverId), ("$pending", pendingOnly ? 1 : 0), ("$state", state),
                ("$n", Math.Clamp(limit, 1, 200) + (lookAhead ? 1 : 0)), ("$offset", Math.Clamp(page, 0, 100000) * Math.Clamp(limit, 1, 200)));
        }
    }
}
