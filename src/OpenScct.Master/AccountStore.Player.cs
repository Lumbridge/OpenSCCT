using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;

namespace OpenScct.Master;

internal sealed record ProfileVisibilityRow(bool Public, bool Hidden);
internal sealed record PlayerWebSessionRow(string Id, long AccountId, string Csrf, long Created, long LastUsed);
internal sealed record PlayerSessionRow(string Id, string Kind, long Created, long LastUsed);
internal sealed record PublicPlayerRow(long Id, string Name, int Prestige, int Xp, long TotalXp, int Matches, int Wins, int Kills, int Deaths);

internal sealed partial class AccountStore
{
    public const long PlayerWebIdle = 12 * 3600;
    public const long PlayerWebMax = 7 * 86400;

    public void MigratePlayerPortal()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS player_visibility (
                account_id INTEGER PRIMARY KEY REFERENCES accounts(id),
                public INTEGER NOT NULL DEFAULT 1,
                hidden INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS player_web_sessions (
                id TEXT PRIMARY KEY,
                token_hash TEXT NOT NULL UNIQUE,
                account_id INTEGER NOT NULL REFERENCES accounts(id),
                csrf TEXT NOT NULL,
                created INTEGER NOT NULL,
                last_used INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS player_web_sessions_account ON player_web_sessions(account_id);
            CREATE TABLE IF NOT EXISTS player_oauth_states (
                state_hash TEXT PRIMARY KEY,
                expires INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS player_game_session_ids (
                token_hash TEXT PRIMARY KEY,
                id TEXT NOT NULL UNIQUE);
            """);
    }

    public ProfileVisibilityRow ProfileVisibility(long accountId)
    {
        lock (_gate)
        {
            return One("SELECT public, hidden FROM player_visibility WHERE account_id = $a;",
                r => new ProfileVisibilityRow(r.GetInt64(0) != 0, r.GetInt64(1) != 0), ("$a", accountId)) ?? new(true, false);
        }
    }

    public void SetProfilePublic(long accountId, bool isPublic)
    {
        lock (_gate)
        {
            Exec("INSERT INTO player_visibility(account_id, public) VALUES($a, $p) " +
                "ON CONFLICT(account_id) DO UPDATE SET public = $p;", ("$a", accountId), ("$p", isPublic ? 1 : 0));
            AuditLocked("profile.visibility", $"account {accountId}: {(isPublic ? "public" : "private")}");
        }
    }

    public void SetProfileHidden(long accountId, bool hidden, string reason)
    {
        lock (_gate)
        {
            Exec("INSERT INTO player_visibility(account_id, hidden) VALUES($a, $h) " +
                "ON CONFLICT(account_id) DO UPDATE SET hidden = $h;", ("$a", accountId), ("$h", hidden ? 1 : 0));
            AuditLocked("profile.moderation", $"account {accountId}: {(hidden ? "hidden" : "restored")}; {reason}");
        }
    }

    public bool IsProfilePublic(long accountId)
    {
        lock (_gate)
        {
            return Convert.ToInt64(Scalar("SELECT COUNT(*) FROM accounts a LEFT JOIN player_visibility v ON v.account_id = a.id " +
                "WHERE a.id = $a AND a.name IS NOT NULL AND a.name <> '' AND COALESCE(v.public, 1) = 1 AND COALESCE(v.hidden, 0) = 0;",
                ("$a", accountId)), CultureInfo.InvariantCulture) != 0;
        }
    }

    /// <summary>Public-only projections: private account fields never leave the store through this query.</summary>
    public (List<PublicPlayerRow> Rows, int Total) PublicPlayers(string? query, string sort, int page, int pageSize)
    {
        string order = sort switch
        {
            "wins" => "wins DESC, total_xp DESC, a.id ASC",
            "kills" => "kills DESC, total_xp DESC, a.id ASC",
            "name" => "a.name_key ASC, a.id ASC",
            _ => "total_xp DESC, wins DESC, a.id ASC",
        };
        string term = (query ?? string.Empty).Trim();
        if (term.Length > 64) term = term[..64];
        string from = "FROM accounts a LEFT JOIN player_visibility v ON v.account_id = a.id " +
            "LEFT JOIN stats s ON s.account_id = a.id LEFT JOIN progression p ON p.account_id = a.id ";
        string where = "WHERE a.name IS NOT NULL AND a.name <> '' AND COALESCE(v.public, 1) = 1 AND COALESCE(v.hidden, 0) = 0 " +
            "AND a.name LIKE $q ESCAPE '\\' ";
        // Search is by public player name only. Leaderboards also exclude active account/Discord bans.
        if (sort != "name")
        {
            where += "AND NOT EXISTS (SELECT 1 FROM bans b WHERE (b.account_id = a.id OR b.discord_id = a.discord_id) " +
                "AND (b.until IS NULL OR b.until > $now)) ";
        }
        string like = "%" + term.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
        lock (_gate)
        {
            int total = Convert.ToInt32(Scalar("SELECT COUNT(*) " + from + where, ("$q", like), ("$now", Now)), CultureInfo.InvariantCulture);
            List<PublicPlayerRow> rows = Query("SELECT a.id, a.name, COALESCE(p.prestige,0), COALESCE(p.xp,0), " +
                "COALESCE(p.total_xp,0) AS total_xp, COALESCE(s.matches,0), COALESCE(s.spy_wins,0) + COALESCE(s.merc_wins,0) AS wins, " +
                "COALESCE(s.kills,0) AS kills, COALESCE(s.deaths,0) " + from + where + "ORDER BY " + order + " LIMIT $n OFFSET $o;",
                r => new PublicPlayerRow(r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt64(4), r.GetInt32(5),
                    r.GetInt32(6), r.GetInt32(7), r.GetInt32(8)),
                ("$q", like), ("$now", Now), ("$n", Math.Clamp(pageSize, 1, 50)), ("$o", Math.Clamp(page, 0, 10_000) * Math.Clamp(pageSize, 1, 50)));
            return (rows, total);
        }
    }

    public string CreatePlayerOAuthState()
    {
        string state = RandomWebToken();
        lock (_gate)
        {
            Exec("DELETE FROM player_oauth_states WHERE expires <= $now;", ("$now", Now));
            Exec("INSERT INTO player_oauth_states(state_hash, expires) VALUES($s, $e);", ("$s", Hash(state)), ("$e", Now + 600));
        }
        return state;
    }

    public bool ConsumePlayerOAuthState(string state)
    {
        lock (_gate)
        {
            return Exec("DELETE FROM player_oauth_states WHERE state_hash = $s AND expires > $now;", ("$s", Hash(state)), ("$now", Now)) == 1;
        }
    }

    public string CreatePlayerWebSession(long accountId)
    {
        string token = RandomWebToken();
        lock (_gate)
        {
            PrunePlayerSessions();
            Exec("INSERT INTO player_web_sessions(id, token_hash, account_id, csrf, created, last_used) VALUES($id,$h,$a,$c,$now,$now);",
                ("$id", RandomWebToken()), ("$h", Hash(token)), ("$a", accountId), ("$c", RandomWebToken()), ("$now", Now));
            AuditLocked("player.signin", $"account {accountId}: website");
        }
        return token;
    }

    public PlayerWebSessionRow? PlayerWebSession(string token)
    {
        if (token.Length is < 20 or > 100) return null;
        lock (_gate)
        {
            string hash = Hash(token);
            PlayerWebSessionRow? session = One("SELECT id, account_id, csrf, created, last_used FROM player_web_sessions WHERE token_hash = $h;",
                r => new PlayerWebSessionRow(r.GetString(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4)), ("$h", hash));
            if (session is null) return null;
            if (Now - session.LastUsed > PlayerWebIdle || Now - session.Created > PlayerWebMax)
            {
                Exec("DELETE FROM player_web_sessions WHERE token_hash = $h;", ("$h", hash));
                return null;
            }
            if (Now - session.LastUsed >= TouchSeconds)
                Exec("UPDATE player_web_sessions SET last_used = $now WHERE token_hash = $h;", ("$now", Now), ("$h", hash));
            return session;
        }
    }

    public void EndPlayerWebSession(string token)
    {
        lock (_gate) Exec("DELETE FROM player_web_sessions WHERE token_hash = $h;", ("$h", Hash(token)));
    }

    private void PrunePlayerSessions()
    {
        Exec("DELETE FROM player_web_sessions WHERE last_used < $idle OR created < $max;", ("$idle", Now - PlayerWebIdle), ("$max", Now - PlayerWebMax));
        Exec("DELETE FROM sessions WHERE last_used < $idle;", ("$idle", Now - SessionIdle));
        Exec("DELETE FROM player_game_session_ids WHERE token_hash NOT IN (SELECT token_hash FROM sessions);");
    }

    /// <summary>Opaque revocation identifiers deliberately differ from the authentication token and its hash.</summary>
    public List<PlayerSessionRow> PlayerSessions(long accountId)
    {
        lock (_gate)
        {
            PrunePlayerSessions();
            foreach (string hash in Query("SELECT token_hash FROM sessions WHERE account_id = $a AND token_hash NOT IN " +
                "(SELECT token_hash FROM player_game_session_ids);", r => r.GetString(0), ("$a", accountId)))
                Exec("INSERT INTO player_game_session_ids(token_hash,id) VALUES($h,$id);", ("$h", hash), ("$id", RandomWebToken()));
            List<PlayerSessionRow> rows = Query("SELECT p.id, s.created, s.last_used FROM sessions s JOIN player_game_session_ids p ON p.token_hash = s.token_hash " +
                "WHERE s.account_id = $a;", r => new PlayerSessionRow(r.GetString(0), "game", r.GetInt64(1), r.GetInt64(2)), ("$a", accountId));
            rows.AddRange(Query("SELECT id, created, last_used FROM player_web_sessions WHERE account_id = $a;",
                r => new PlayerSessionRow(r.GetString(0), "web", r.GetInt64(1), r.GetInt64(2)), ("$a", accountId)));
            return rows.OrderByDescending(r => r.LastUsed).ToList();
        }
    }

    public bool RevokePlayerSession(long accountId, string kind, string id)
    {
        lock (_gate)
        {
            int ended = kind switch
            {
                "web" => Exec("DELETE FROM player_web_sessions WHERE account_id = $a AND id = $id;", ("$a", accountId), ("$id", id)),
                "game" => Exec("DELETE FROM sessions WHERE account_id = $a AND token_hash IN " +
                    "(SELECT token_hash FROM player_game_session_ids WHERE id = $id);", ("$a", accountId), ("$id", id)),
                _ => 0,
            };
            if (ended != 0) AuditLocked("session.revoke", $"account {accountId}: {kind} session revoked");
            return ended != 0;
        }
    }

    public int RevokePlayerSessions(long accountId, string? exceptWebSessionId = null)
    {
        lock (_gate)
        {
            int ended = Exec("DELETE FROM sessions WHERE account_id = $a;", ("$a", accountId));
            ended += Exec("DELETE FROM player_web_sessions WHERE account_id = $a AND ($except IS NULL OR id <> $except);",
                ("$a", accountId), ("$except", exceptWebSessionId));
            AuditLocked("session.revoke-all", $"account {accountId}: {ended} game/website sessions ended");
            return ended;
        }
    }

    private static string RandomWebToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
}
