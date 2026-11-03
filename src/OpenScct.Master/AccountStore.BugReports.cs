using System;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace OpenScct.Master;

internal enum StoredBugReportState { Pending, Created, Unknown, Rejected }
internal sealed record StoredBugReport(string Id, string PayloadHash, long Created, StoredBugReportState State, long? IssueNumber);
internal enum BugReportReservationState { Reserved, Existing, Conflict, RateLimited }
internal sealed record BugReportReservation(BugReportReservationState State, StoredBugReport? Report = null);

internal sealed partial class AccountStore
{
    public const int BugReportsPerDay = 3;
    public const long BugReportRetention = 30 * 86400;
    private bool _bugReportsReady;

    // This independent, additive ledger does not alter any existing account tables. Initialise it lazily so
    // existing databases also receive it without changing the game's account schema version.
    private void EnsureBugReports()
    {
        if (_bugReportsReady) return;
        Exec("""
            CREATE TABLE IF NOT EXISTS website_bug_reports (
                id TEXT PRIMARY KEY,
                account_id INTEGER NOT NULL REFERENCES accounts(id),
                payload_hash TEXT NOT NULL,
                created INTEGER NOT NULL,
                state INTEGER NOT NULL,
                issue_number INTEGER);
            CREATE INDEX IF NOT EXISTS website_bug_reports_account_created ON website_bug_reports(account_id, created);
            CREATE INDEX IF NOT EXISTS website_bug_reports_account_payload ON website_bug_reports(account_id, payload_hash);
            """);
        _bugReportsReady = true;
    }

    public StoredBugReport? BugReport(long accountId, string id)
    {
        lock (_gate)
        {
            EnsureBugReports();
            return One("SELECT id, payload_hash, created, state, issue_number FROM website_bug_reports " +
                "WHERE account_id = $a AND id = $id AND created > $cutoff;", ReadBugReport,
                ("$a", accountId), ("$id", id), ("$cutoff", Now - BugReportRetention));
        }
    }

    /// <summary>Minute-by-minute maintenance removes receipts once their 30-day lifetime ends.</summary>
    public void PruneBugReports()
    {
        lock (_gate)
        {
            EnsureBugReports();
            Exec("DELETE FROM website_bug_reports WHERE created <= $cutoff;", ("$cutoff", Now - BugReportRetention));
        }
    }

    public BugReportReservation ReserveBugReport(long accountId, string id, string payloadHash)
    {
        lock (_gate)
        {
            EnsureBugReports();
            return InTransaction(() =>
            {
                long now = Now;
                Exec("DELETE FROM website_bug_reports WHERE created <= $cutoff;", ("$cutoff", now - BugReportRetention));
                var existing = One("SELECT id, payload_hash, created, state, issue_number FROM website_bug_reports " +
                    "WHERE id = $id;", ReadBugReport, ("$id", id));
                if (existing is not null)
                {
                    // A receipt is not authorization: its account owner must match as well as its payload.
                    long owner = Convert.ToInt64(Scalar("SELECT account_id FROM website_bug_reports WHERE id = $id;", ("$id", id)), CultureInfo.InvariantCulture);
                    return owner == accountId && existing.PayloadHash == payloadHash
                        ? new BugReportReservation(BugReportReservationState.Existing, existing)
                        : new BugReportReservation(BugReportReservationState.Conflict);
                }

                // A new form must not duplicate an accepted report or a request whose result was lost.
                existing = One("SELECT id, payload_hash, created, state, issue_number FROM website_bug_reports " +
                    "WHERE account_id = $a AND payload_hash = $p AND state <> $rejected ORDER BY created DESC LIMIT 1;",
                    ReadBugReport, ("$a", accountId), ("$p", payloadHash), ("$rejected", (int)StoredBugReportState.Rejected));
                if (existing is not null) return new(BugReportReservationState.Existing, existing);

                int daily = Convert.ToInt32(Scalar("SELECT COUNT(*) FROM website_bug_reports WHERE account_id = $a AND created > $since;",
                    ("$a", accountId), ("$since", now - 86400)), CultureInfo.InvariantCulture);
                long latest = Convert.ToInt64(Scalar("SELECT COALESCE(MAX(created),0) FROM website_bug_reports WHERE account_id = $a;",
                    ("$a", accountId)), CultureInfo.InvariantCulture);
                if (daily >= BugReportsPerDay || (latest > 0 && now - latest < 60))
                    return new(BugReportReservationState.RateLimited);

                Exec("INSERT INTO website_bug_reports(id, account_id, payload_hash, created, state) VALUES($id,$a,$p,$now,$state);",
                    ("$id", id), ("$a", accountId), ("$p", payloadHash), ("$now", now), ("$state", (int)StoredBugReportState.Pending));
                return new(BugReportReservationState.Reserved, new(id, payloadHash, now, StoredBugReportState.Pending, null));
            });
        }
    }

    public void FinishBugReport(long accountId, string id, StoredBugReportState state, long? issueNumber = null)
    {
        lock (_gate)
        {
            EnsureBugReports();
            Exec("UPDATE website_bug_reports SET state = $state, issue_number = $issue " +
                "WHERE account_id = $a AND id = $id AND state = $pending;",
                ("$state", (int)state), ("$issue", issueNumber), ("$a", accountId), ("$id", id), ("$pending", (int)StoredBugReportState.Pending));
        }
    }

    private static StoredBugReport ReadBugReport(SqliteDataReader row) =>
        new(row.GetString(0), row.GetString(1), row.GetInt64(2), (StoredBugReportState)row.GetInt32(3), row.IsDBNull(4) ? null : row.GetInt64(4));
}
