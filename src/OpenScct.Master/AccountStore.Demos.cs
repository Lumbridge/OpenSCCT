using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace OpenScct.Master;

/// <summary>Where a demo is: its upload link was handed out, the server confirmed the upload, or its time ran out.</summary>
internal static class DemoState
{
    public const string Pending = "pending";
    public const string Ready = "ready";
    public const string Expired = "expired";
}

/// <summary>One round demo kept in S3.</summary>
internal sealed record DemoRow(
    long Id, long MatchId, string Key, long Size, string Sha256, int DurationSeconds, string GameVersion, string NetVersion,
    string ContentHash, string State, long Created, long Expires);

// Round demos (schema v6, docs/ONLINE.md "Demos").
internal sealed partial class AccountStore
{
    /// <summary>Seconds a demo may stay pending (its upload link lasts 15 minutes) before the sweeper drops it.</summary>
    public const long DemoPendingSeconds = 3600;

    private void MigrateVersion6()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS demos (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                match_id INTEGER NOT NULL REFERENCES matches(id),
                key TEXT NOT NULL UNIQUE,
                size INTEGER NOT NULL,
                sha256 TEXT NOT NULL,
                duration_s INTEGER NOT NULL,
                game_version TEXT NOT NULL,
                net_version TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                state TEXT NOT NULL,
                created INTEGER NOT NULL,
                expires INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS demos_match ON demos(match_id);
            CREATE INDEX IF NOT EXISTS demos_state ON demos(state, created);
            """);
    }

    private const string DemoColumns =
        "id, match_id, key, size, sha256, duration_s, game_version, net_version, content_hash, state, created, expires";

    private static DemoRow ReadDemo(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3), r.GetString(4), r.GetInt32(5), r.GetString(6), r.GetString(7),
        r.GetString(8), r.GetString(9), r.GetInt64(10), r.GetInt64(11));

    /// <summary>A new pending demo of <paramref name="matchId"/>, listed for <paramref name="keepDays"/> once ready; returns its row.</summary>
    public DemoRow CreateDemo(long matchId, string key, long size, string sha256, int durationSeconds, string gameVersion, string netVersion,
        string contentHash, int keepDays)
    {
        lock (_gate)
        {
            long now = Now;
            long expires = now + (keepDays * 86400L);
            Exec("INSERT INTO demos (match_id, key, size, sha256, duration_s, game_version, net_version, content_hash, state, created, expires) " +
                "VALUES ($m, $k, $s, $h, $d, $gv, $nv, $c, $st, $now, $e);",
                ("$m", matchId), ("$k", key), ("$s", size), ("$h", sha256), ("$d", durationSeconds), ("$gv", gameVersion), ("$nv", netVersion),
                ("$c", contentHash), ("$st", DemoState.Pending), ("$now", now), ("$e", expires));
            long id = Convert.ToInt64(Scalar("SELECT last_insert_rowid();"), CultureInfo.InvariantCulture);
            return new DemoRow(id, matchId, key, size, sha256, durationSeconds, gameVersion, netVersion, contentHash, DemoState.Pending, now, expires);
        }
    }

    public DemoRow? Demo(long id)
    {
        lock (_gate)
        {
            return One($"SELECT {DemoColumns} FROM demos WHERE id = $id;", ReadDemo, ("$id", id));
        }
    }

    /// <summary>The match's ready demo, else its newest one, or null.</summary>
    public DemoRow? DemoOfMatch(long matchId)
    {
        lock (_gate)
        {
            return One($"SELECT {DemoColumns} FROM demos WHERE match_id = $m ORDER BY state = $ready DESC, id DESC LIMIT 1;", ReadDemo,
                ("$m", matchId), ("$ready", DemoState.Ready));
        }
    }

    /// <summary>Marks a pending demo ready; false when it is not pending (already confirmed, or swept).</summary>
    public bool MarkDemoReady(long id)
    {
        lock (_gate)
        {
            return Exec("UPDATE demos SET state = $ready WHERE id = $id AND state = $pending AND expires > $now;",
                ("$ready", DemoState.Ready), ("$pending", DemoState.Pending), ("$id", id), ("$now", Now)) > 0;
        }
    }

    /// <summary>Ready, unexpired demos newest first with their rounds and players, one page; and how many there are in all.</summary>
    public (List<(DemoRow Demo, MatchRow Match, List<MatchPlayerRow> Players)> Rows, int Total) ReadyDemos(int page, int pageSize)
    {
        lock (_gate)
        {
            long now = Now;
            int total = Convert.ToInt32(Scalar("SELECT COUNT(*) FROM demos WHERE state = $ready AND expires > $now;",
                ("$ready", DemoState.Ready), ("$now", now)), CultureInfo.InvariantCulture);
            List<DemoRow> demos = Query($"SELECT {DemoColumns} FROM demos WHERE state = $ready AND expires > $now ORDER BY id DESC LIMIT $n OFFSET $o;",
                ReadDemo, ("$ready", DemoState.Ready), ("$now", now), ("$n", pageSize), ("$o", Math.Max(0, page) * pageSize));
            var rows = new List<(DemoRow, MatchRow, List<MatchPlayerRow>)>(demos.Count);
            foreach (DemoRow d in demos)
            {
                if (One($"SELECT {MatchColumns} FROM matches WHERE id = $id;", ReadMatch, ("$id", d.MatchId)) is not { } match)
                {
                    continue;
                }

                List<MatchPlayerRow> players = Query(
                    "SELECT match_id, account_id, name, team, score, kills, deaths, bot FROM match_rows WHERE match_id = $id ORDER BY team, score DESC;",
                    r => new MatchPlayerRow(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4),
                        r.GetInt32(5), r.GetInt32(6), r.GetInt64(7) != 0, new Dictionary<string, int>()),
                    ("$id", d.MatchId));
                rows.Add((d, match, players));
            }

            return (rows, total);
        }
    }

    /// <summary>
    /// Marks demos expired: those past their time (the bucket's lifecycle rule deletes the files) and uploads never
    /// confirmed within <see cref="DemoPendingSeconds"/>. Returns how many changed.
    /// </summary>
    public int ExpireDemos()
    {
        lock (_gate)
        {
            long now = Now;
            return Exec("UPDATE demos SET state = $expired WHERE state != $expired AND (expires <= $now OR (state = $pending AND created < $stale));",
                ("$expired", DemoState.Expired), ("$pending", DemoState.Pending), ("$now", now), ("$stale", now - DemoPendingSeconds));
        }
    }
}
