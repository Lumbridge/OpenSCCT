using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using OpenScct.Core.Engine;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

/// <summary>One account row. Times are Unix seconds.</summary>
internal sealed record AccountRow(long Id, string DiscordId, string? Name, long Created, long LastSeen, long? NameChanged);

/// <summary>One ban: of an account, a Discord id, or an address or CIDR range. A null <see cref="Until"/> is forever.</summary>
internal sealed record BanRow(long Id, long? AccountId, string? DiscordId, string? Ip, string Reason, long? Until, long Added)
{
    /// <summary>The reason and how long, for the player and the admin.</summary>
    public string Describe()
    {
        string reason = string.IsNullOrWhiteSpace(Reason) ? "no reason given" : Reason;
        return Until is { } until
            ? $"{reason} (until {AccountStore.Format(until)})"
            : $"{reason} (permanent)";
    }
}

/// <summary>A name an account had, held from others until <see cref="Until"/>.</summary>
internal sealed record NameHistoryRow(string Name, long Changed, long Until);

internal sealed record AuditRow(long Id, long Time, string Action, string Detail, string Actor = "");

/// <summary>How a name claim went.</summary>
internal enum NameClaim
{
    Done,
    Taken,
    TooSoon,
    NoAccount,
}

/// <summary>
/// The master's accounts in SQLite (docs/ONLINE.md, "Accounts"): Discord-linked accounts and their claimed names,
/// sign-in sessions (only a SHA-256 of each token is kept), bans, stats, achievements, synced settings and an audit
/// log. One connection behind a lock; WAL lets the admin command read and write while the master runs.
/// </summary>
internal sealed partial class AccountStore : IDisposable
{
    public const int SchemaVersion = 6;

    /// <summary>Seconds between renames.</summary>
    public const long RenameCooldown = 30 * 86400;

    /// <summary>Seconds an old name stays held for its last owner.</summary>
    public const long NameHold = 14 * 86400;

    /// <summary>Seconds a sign-in session lasts without use.</summary>
    public const long SessionIdle = 180 * 86400;

    /// <summary>Seconds between last-used updates of one session.</summary>
    private const long TouchSeconds = 60;

    private readonly SqliteConnection _db;
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private SqliteTransaction? _tx;

    private AccountStore(SqliteConnection db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public long Now => _time.GetUtcNow().ToUnixTimeSeconds();

    /// <summary>Opens (or makes) the database at <paramref name="path"/> and brings its tables up to date.</summary>
    public static AccountStore Open(string path, TimeProvider? time = null)
    {
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 10 };
        var db = new SqliteConnection(cs.ToString());
        var store = new AccountStore(db, time ?? TimeProvider.System);
        try
        {
            db.Open();
            store.Exec("PRAGMA journal_mode=WAL;");
            store.Exec("PRAGMA busy_timeout=5000;");
            store.Exec("PRAGMA foreign_keys=ON;");
            store.Migrate();
            return store;
        }
        catch (SqliteException e)
        {
            store.Dispose();
            throw new InvalidOperationException($"The account database '{path}' could not be opened: {e.Message}", e);
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    private void Migrate()
    {
        Exec("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);");
        long version = Convert.ToInt64(Scalar("SELECT COALESCE(MAX(version), 0) FROM schema_version;"), CultureInfo.InvariantCulture);
        if (version > SchemaVersion)
        {
            throw new InvalidOperationException($"The account database is version {version}; this master knows up to {SchemaVersion}. Update svm-master.");
        }

        if (version == SchemaVersion)
        {
            return;
        }

        if (version < 1)
        {
            CreateVersion1();
        }

        // Version 2: feat totals and base-map wins for the achievements. Version 3: cosmetics an admin unlocked.
        // Version 4: the admin portal (MigrateVersion4). Version 5: levels and prestige (MigrateVersion5).
        // Version 6: round demos (MigrateVersion6).
        InTransaction(() =>
        {
            Exec("""
                CREATE TABLE IF NOT EXISTS feat_totals (
                    account_id INTEGER NOT NULL REFERENCES accounts(id),
                    feat TEXT NOT NULL,
                    total INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (account_id, feat));
                CREATE TABLE IF NOT EXISTS map_wins (
                    account_id INTEGER NOT NULL REFERENCES accounts(id),
                    map TEXT NOT NULL,
                    team INTEGER NOT NULL,
                    wins INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (account_id, map, team));
                CREATE TABLE IF NOT EXISTS cosmetic_grants (
                    account_id INTEGER NOT NULL REFERENCES accounts(id),
                    item TEXT NOT NULL,
                    granted INTEGER NOT NULL,
                    PRIMARY KEY (account_id, item));
                """);
            MigrateVersion4();
            MigrateVersion5();
            MigrateVersion6();
            Exec("DELETE FROM schema_version; INSERT INTO schema_version (version) VALUES ($v);", ("$v", SchemaVersion));
            return 0;
        });
    }

    private void CreateVersion1()
    {
        InTransaction(() =>
        {
            Exec("""
                CREATE TABLE accounts (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    discord_id TEXT NOT NULL UNIQUE,
                    name TEXT,
                    name_key TEXT UNIQUE,
                    created INTEGER NOT NULL,
                    last_seen INTEGER NOT NULL,
                    name_changed INTEGER);
                CREATE TABLE sessions (
                    token_hash TEXT PRIMARY KEY,
                    account_id INTEGER NOT NULL REFERENCES accounts(id),
                    created INTEGER NOT NULL,
                    last_used INTEGER NOT NULL);
                CREATE INDEX sessions_account ON sessions(account_id);
                CREATE TABLE name_history (
                    account_id INTEGER NOT NULL REFERENCES accounts(id),
                    name TEXT NOT NULL,
                    name_key TEXT NOT NULL,
                    changed INTEGER NOT NULL,
                    until INTEGER NOT NULL);
                CREATE INDEX name_history_key ON name_history(name_key);
                CREATE INDEX name_history_account ON name_history(account_id);
                CREATE TABLE bans (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    account_id INTEGER,
                    discord_id TEXT,
                    ip TEXT,
                    reason TEXT NOT NULL,
                    until INTEGER,
                    added INTEGER NOT NULL);
                CREATE TABLE stats (
                    account_id INTEGER PRIMARY KEY REFERENCES accounts(id),
                    matches INTEGER NOT NULL DEFAULT 0,
                    spy_wins INTEGER NOT NULL DEFAULT 0,
                    merc_wins INTEGER NOT NULL DEFAULT 0,
                    losses INTEGER NOT NULL DEFAULT 0,
                    kills INTEGER NOT NULL DEFAULT 0,
                    deaths INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE achievements (
                    account_id INTEGER NOT NULL REFERENCES accounts(id),
                    achievement_id TEXT NOT NULL,
                    earned INTEGER NOT NULL,
                    server TEXT NOT NULL,
                    PRIMARY KEY (account_id, achievement_id));
                CREATE TABLE profiles (
                    account_id INTEGER PRIMARY KEY REFERENCES accounts(id),
                    data TEXT NOT NULL,
                    updated_ms INTEGER NOT NULL);
                CREATE TABLE audit (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    time INTEGER NOT NULL,
                    action TEXT NOT NULL,
                    detail TEXT NOT NULL);
                """);
            Exec("DELETE FROM schema_version; INSERT INTO schema_version (version) VALUES (1);");
            return 0;
        });
    }

    // ---- Accounts --------------------------------------------------------------------------------------------

    private const string AccountColumns = "id, discord_id, name, created, last_seen, name_changed";

    private static AccountRow ReadAccount(SqliteDataReader r) =>
        new(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetInt64(3), r.GetInt64(4), r.IsDBNull(5) ? null : r.GetInt64(5));

    /// <summary>The account linked to <paramref name="discordId"/>, made on its first sign-in.</summary>
    public AccountRow GetOrCreate(string discordId)
    {
        lock (_gate)
        {
            long now = Now;
            if (Exec("INSERT OR IGNORE INTO accounts (discord_id, created, last_seen) VALUES ($d, $now, $now);", ("$d", discordId), ("$now", now)) > 0)
            {
                AuditLocked("account.create", "discord " + discordId);
            }

            return FindByDiscord(discordId) ?? throw new InvalidOperationException("The account was not stored.");
        }
    }

    public AccountRow? FindById(long id)
    {
        lock (_gate)
        {
            return One($"SELECT {AccountColumns} FROM accounts WHERE id = $id;", ReadAccount, ("$id", id));
        }
    }

    public AccountRow? FindByDiscord(string discordId)
    {
        lock (_gate)
        {
            return One($"SELECT {AccountColumns} FROM accounts WHERE discord_id = $d;", ReadAccount, ("$d", discordId));
        }
    }

    /// <summary>The account whose claimed name folds to the same key as <paramref name="name"/>.</summary>
    public AccountRow? FindByName(string name)
    {
        lock (_gate)
        {
            return One($"SELECT {AccountColumns} FROM accounts WHERE name_key = $k;", ReadAccount, ("$k", PlayerNames.Key(name)));
        }
    }

    // ---- Sessions --------------------------------------------------------------------------------------------

    /// <summary>A new sign-in session for <paramref name="accountId"/>: the token the game keeps (only its hash is stored).</summary>
    public string CreateSession(long accountId)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        string token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        lock (_gate)
        {
            long now = Now;
            Exec("INSERT INTO sessions (token_hash, account_id, created, last_used) VALUES ($h, $a, $now, $now);",
                ("$h", Hash(token)), ("$a", accountId), ("$now", now));
            Exec("UPDATE accounts SET last_seen = $now WHERE id = $a;", ("$a", accountId), ("$now", now));
        }

        return token;
    }

    /// <summary>The account a session token belongs to, or null (unknown or idle too long). Marks it used.</summary>
    public AccountRow? Authenticate(string token)
    {
        string hash = Hash(token);
        lock (_gate)
        {
            long now = Now;
            (AccountRow Account, long LastUsed)? found = One(
                "SELECT a.id, a.discord_id, a.name, a.created, a.last_seen, a.name_changed, s.last_used FROM sessions s " +
                "JOIN accounts a ON a.id = s.account_id WHERE s.token_hash = $h;",
                r => ((AccountRow Account, long LastUsed)?)(ReadAccount(r), r.GetInt64(6)), ("$h", hash));
            if (found is not { } f)
            {
                return null;
            }

            if (now - f.LastUsed > SessionIdle)
            {
                Exec("DELETE FROM sessions WHERE token_hash = $h;", ("$h", hash));
                return null;
            }

            if (now - f.LastUsed >= TouchSeconds)
            {
                Exec("UPDATE sessions SET last_used = $now WHERE token_hash = $h;", ("$h", hash), ("$now", now));
                Exec("UPDATE accounts SET last_seen = $now WHERE id = $a;", ("$a", f.Account.Id), ("$now", now));
            }

            return f.Account;
        }
    }

    public bool DeleteSession(string token)
    {
        lock (_gate)
        {
            return Exec("DELETE FROM sessions WHERE token_hash = $h;", ("$h", Hash(token))) > 0;
        }
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // ---- Names -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Gives <paramref name="name"/> (already checked with <see cref="PlayerNames.ClaimProblem"/>) to the account.
    /// The first claim is free; renames wait <see cref="RenameCooldown"/> unless <paramref name="ignoreCooldown"/>,
    /// and the old name is held for its owner for <see cref="NameHold"/>. <paramref name="retryAt"/> is when a
    /// <see cref="NameClaim.TooSoon"/> rename becomes allowed.
    /// </summary>
    public NameClaim ClaimName(long accountId, string name, bool ignoreCooldown, out long retryAt)
    {
        retryAt = 0;
        string key = PlayerNames.Key(name);
        lock (_gate)
        {
            long now = Now;
            if (One($"SELECT {AccountColumns} FROM accounts WHERE id = $id;", ReadAccount, ("$id", accountId)) is not { } account)
            {
                return NameClaim.NoAccount;
            }

            string? oldKey = account.Name is null ? null : PlayerNames.Key(account.Name);
            if (oldKey == key)
            {
                // The same name in another case or spelling of separators: no rename, no hold.
                if (account.Name != name)
                {
                    Exec("UPDATE accounts SET name = $n WHERE id = $id;", ("$n", name), ("$id", accountId));
                    AuditLocked("name.recase", $"account {accountId}: {account.Name} -> {name}");
                }

                return NameClaim.Done;
            }

            if (account.Name is not null && !ignoreCooldown && account.NameChanged is { } changed && changed + RenameCooldown > now)
            {
                retryAt = changed + RenameCooldown;
                return NameClaim.TooSoon;
            }

            bool held = Convert.ToInt64(Scalar(
                "SELECT (SELECT COUNT(*) FROM accounts WHERE name_key = $k AND id <> $id) + " +
                "(SELECT COUNT(*) FROM name_history WHERE name_key = $k AND account_id <> $id AND until > $now);",
                ("$k", key), ("$id", accountId), ("$now", now)), CultureInfo.InvariantCulture) > 0;
            if (held)
            {
                return NameClaim.Taken;
            }

            InTransaction(() =>
            {
                if (account.Name is not null)
                {
                    Exec("INSERT INTO name_history (account_id, name, name_key, changed, until) VALUES ($id, $n, $k, $now, $until);",
                        ("$id", accountId), ("$n", account.Name), ("$k", oldKey), ("$now", now), ("$until", now + NameHold));
                }

                Exec("UPDATE accounts SET name = $n, name_key = $k, name_changed = $now WHERE id = $id;",
                    ("$n", name), ("$k", key), ("$now", now), ("$id", accountId));
                AuditLocked(account.Name is null ? "name.claim" : "name.change",
                    $"account {accountId}: {account.Name ?? "(none)"} -> {name}" + (ignoreCooldown ? " (admin)" : string.Empty));
                return 0;
            });
            return NameClaim.Done;
        }
    }

    /// <summary>Takes the account's name away (it picks a new one) and frees the name at once.</summary>
    public bool ReleaseName(long accountId)
    {
        lock (_gate)
        {
            if (One($"SELECT {AccountColumns} FROM accounts WHERE id = $id;", ReadAccount, ("$id", accountId)) is not { Name: { } name })
            {
                return false;
            }

            InTransaction(() =>
            {
                string key = PlayerNames.Key(name);
                Exec("UPDATE accounts SET name = NULL, name_key = NULL WHERE id = $id;", ("$id", accountId));
                Exec("UPDATE name_history SET until = $now WHERE name_key = $k AND until > $now;", ("$k", key), ("$now", Now));
                AuditLocked("name.release", $"account {accountId}: {name}");
                return 0;
            });
            return true;
        }
    }

    public List<NameHistoryRow> NameHistory(long accountId)
    {
        lock (_gate)
        {
            return Query("SELECT name, changed, until FROM name_history WHERE account_id = $id ORDER BY changed;",
                r => new NameHistoryRow(r.GetString(0), r.GetInt64(1), r.GetInt64(2)), ("$id", accountId));
        }
    }

    // ---- Bans ------------------------------------------------------------------------------------------------

    private const string BanColumns = "id, account_id, discord_id, ip, reason, until, added";

    private static BanRow ReadBan(SqliteDataReader r) => new(
        r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
        r.GetString(4), r.IsDBNull(5) ? null : r.GetInt64(5), r.GetInt64(6));

    /// <summary>Every ban in force now (read fresh each time, so the admin command takes effect at once).</summary>
    public List<BanRow> ActiveBans()
    {
        lock (_gate)
        {
            return Query($"SELECT {BanColumns} FROM bans WHERE until IS NULL OR until > $now ORDER BY id;", ReadBan, ("$now", Now));
        }
    }

    /// <summary>The ban in force on the account, the Discord id or the address, or null.</summary>
    public BanRow? ActiveBan(long? accountId, string? discordId, IPAddress? ip)
    {
        foreach (BanRow b in ActiveBans())
        {
            if ((accountId is not null && b.AccountId == accountId) ||
                (discordId is not null && string.Equals(b.DiscordId, discordId, StringComparison.Ordinal)) ||
                (ip is not null && b.Ip is not null && IpMatches(b.Ip, ip)))
            {
                return b;
            }
        }

        return null;
    }

    /// <summary>The accounts of <paramref name="accountIds"/> that are banned now (by account or Discord id).</summary>
    public List<long> BannedAmong(IReadOnlyList<long> accountIds)
    {
        var banned = new List<long>();
        if (accountIds.Count == 0)
        {
            return banned;
        }

        List<BanRow> bans = ActiveBans();
        if (bans.Count == 0)
        {
            return banned;
        }

        var byAccount = new HashSet<long>();
        var byDiscord = new HashSet<string>(StringComparer.Ordinal);
        foreach (BanRow b in bans)
        {
            if (b.AccountId is { } a)
            {
                byAccount.Add(a);
            }

            if (b.DiscordId is { } d)
            {
                byDiscord.Add(d);
            }
        }

        foreach (long id in accountIds)
        {
            if (byAccount.Contains(id) || (byDiscord.Count > 0 && FindById(id) is { } a && byDiscord.Contains(a.DiscordId)))
            {
                if (!banned.Contains(id))
                {
                    banned.Add(id);
                }
            }
        }

        return banned;
    }

    public long AddBan(long? accountId, string? discordId, string? ip, string reason, long? until)
    {
        lock (_gate)
        {
            return InTransaction(() =>
            {
                Exec("INSERT INTO bans (account_id, discord_id, ip, reason, until, added) VALUES ($a, $d, $ip, $r, $u, $now);",
                    ("$a", accountId), ("$d", discordId), ("$ip", ip), ("$r", reason), ("$u", until), ("$now", Now));
                long id = Convert.ToInt64(Scalar("SELECT last_insert_rowid();"), CultureInfo.InvariantCulture);
                AuditLocked("ban", $"#{id} account {accountId?.ToString(CultureInfo.InvariantCulture) ?? "-"}, discord {discordId ?? "-"}, " +
                    $"ip {ip ?? "-"}, until {(until is { } u ? Format(u) : "forever")}: {reason}");
                return id;
            });
        }
    }

    /// <summary>Ends the bans in force that match (by ban id, account, Discord id or exact address text).</summary>
    public int EndBans(long? banId, long? accountId, string? discordId, string? ip)
    {
        lock (_gate)
        {
            long now = Now;
            int ended = Exec(
                "UPDATE bans SET until = $now WHERE (until IS NULL OR until > $now) AND " +
                "(id = $b OR account_id = $a OR discord_id = $d OR ip = $ip);",
                ("$now", now), ("$b", banId), ("$a", accountId), ("$d", discordId), ("$ip", ip));
            if (ended > 0)
            {
                AuditLocked("unban", $"{ended} ban(s): id {banId?.ToString(CultureInfo.InvariantCulture) ?? "-"}, " +
                    $"account {accountId?.ToString(CultureInfo.InvariantCulture) ?? "-"}, discord {discordId ?? "-"}, ip {ip ?? "-"}");
            }

            return ended;
        }
    }

    /// <summary>An address or CIDR range in the form bans keep it, or null when the text is neither.</summary>
    public static string? NormalizeIp(string text)
    {
        if (IPAddress.TryParse(text, out IPAddress? ip) && !text.Contains('/', StringComparison.Ordinal))
        {
            return (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
        }

        return IPNetwork.TryParse(text, out IPNetwork network) ? network.ToString() : null;
    }

    private static bool IpMatches(string rule, IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (rule.Contains('/', StringComparison.Ordinal))
        {
            return IPNetwork.TryParse(rule, out IPNetwork network) &&
                network.BaseAddress.AddressFamily == ip.AddressFamily && network.Contains(ip);
        }

        return IPAddress.TryParse(rule, out IPAddress? one) && (one.IsIPv4MappedToIPv6 ? one.MapToIPv4() : one).Equals(ip);
    }

    // ---- Stats and achievements ------------------------------------------------------------------------------

    public AccountStats Stats(long accountId)
    {
        lock (_gate)
        {
            return StatsLocked(accountId);
        }
    }

    private AccountStats StatsLocked(long accountId) =>
        One("SELECT matches, spy_wins, merc_wins, losses, kills, deaths FROM stats WHERE account_id = $a;",
            r => new AccountStats
            {
                Matches = r.GetInt32(0), SpyWins = r.GetInt32(1), MercWins = r.GetInt32(2), Losses = r.GetInt32(3), Kills = r.GetInt32(4),
                Deaths = r.GetInt32(5),
            }, ("$a", accountId)) ?? new AccountStats();

    /// <summary>The achievements the account has, by id, with when (Unix seconds).</summary>
    public Dictionary<string, long> Earned(long accountId)
    {
        lock (_gate)
        {
            var earned = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach ((string id, long at) in Query("SELECT achievement_id, earned FROM achievements WHERE account_id = $a;",
                r => (r.GetString(0), r.GetInt64(1)), ("$a", accountId)))
            {
                earned[id] = at;
            }

            return earned;
        }
    }

    /// <summary>The account's totals for the achievements (stats, feats, base-map wins, earned achievements).</summary>
    public AccountProgress Progress(long accountId)
    {
        lock (_gate)
        {
            return ProgressLocked(accountId);
        }
    }

    private AccountProgress ProgressLocked(long accountId)
    {
        var feats = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string feat, int total) in Query("SELECT feat, total FROM feat_totals WHERE account_id = $a;",
            r => (r.GetString(0), r.GetInt32(1)), ("$a", accountId)))
        {
            feats[feat] = total;
        }

        var maps = new Dictionary<(string, int), int>();
        foreach ((string map, int team, int wins) in Query("SELECT map, team, wins FROM map_wins WHERE account_id = $a;",
            r => (r.GetString(0), r.GetInt32(1), r.GetInt32(2)), ("$a", accountId)))
        {
            maps[(map, team)] = wins;
        }

        var earned = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in Query("SELECT achievement_id FROM achievements WHERE account_id = $a;", r => r.GetString(0), ("$a", accountId)))
        {
            earned.Add(id);
        }

        return new AccountProgress { Stats = StatsLocked(accountId), Feats = feats, MapWins = maps, Earned = earned };
    }

    /// <summary>The cosmetics an admin unlocked for the account (beside what its achievements unlock).</summary>
    public List<string> Grants(long accountId)
    {
        lock (_gate)
        {
            return Query("SELECT item FROM cosmetic_grants WHERE account_id = $a;", r => r.GetString(0), ("$a", accountId));
        }
    }

    /// <summary>Everything the account may wear: its achievements' rewards and the admin's grants.</summary>
    public IReadOnlyList<string> Unlocked(long accountId)
    {
        var ids = new List<string>(Achievements.Unlocked(Earned(accountId).Keys));
        foreach (string item in Grants(accountId))
        {
            if (!ids.Contains(item, StringComparer.Ordinal) && CosmeticCatalog.Find(item) is not null)
            {
                ids.Add(item);
            }
        }

        var progress = ProgressionOf(accountId);
        foreach (var pistol in OpenScct.Core.Engine.Sidearms.All)
        {
            if (progress.BestLevel >= pistol.Level + 5) ids.Add($"merc.{pistol.Id}.night_ops");
            if (progress.BestLevel >= pistol.Level + 10) ids.Add($"merc.{pistol.Id}.cryo");
        }
        return ids.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Unlocks (or with <paramref name="unlock"/> false, takes back) cosmetics; returns how many rows changed.</summary>
    public int SetGrants(long accountId, IEnumerable<string> items, bool unlock)
    {
        lock (_gate)
        {
            long now = Now;
            var list = items.ToList();
            return InTransaction(() =>
            {
                int changed = 0;
                foreach (string item in list)
                {
                    changed += unlock
                        ? Exec("INSERT OR IGNORE INTO cosmetic_grants (account_id, item, granted) VALUES ($a, $i, $now);",
                            ("$a", accountId), ("$i", item), ("$now", now))
                        : Exec("DELETE FROM cosmetic_grants WHERE account_id = $a AND item = $i;", ("$a", accountId), ("$i", item));
                }

                AuditLocked(unlock ? "unlock" : "lock", $"account {accountId}: {list.Count} cosmetic(s), {changed} changed");
                return changed;
            });
        }
    }

    /// <summary>The most one round may add to one feat.</summary>
    public const int MaxFeatPerRound = 200;

    /// <summary>
    /// Counts a finished round (already checked as countable) for every signed-in player in it and grants the
    /// achievements it earned. Returns how many accounts were counted.
    /// </summary>
    public int ApplyResults(ResultsReport report, string serverName)
    {
        lock (_gate)
        {
            long now = Now;
            return InTransaction(() =>
            {
                var seen = new HashSet<long>();
                int counted = 0;
                foreach (ResultsReportRow row in report.Rows)
                {
                    if (row.Bot || row.AccountId is not { } id || !seen.Add(id) ||
                        One("SELECT id FROM accounts WHERE id = $a;", r => (long?)r.GetInt64(0), ("$a", id)) is null)
                    {
                        continue;
                    }

                    bool won = row.Team == report.WinningTeam;
                    Exec("INSERT OR IGNORE INTO stats (account_id) VALUES ($a);", ("$a", id));
                    Exec("UPDATE stats SET matches = matches + 1, spy_wins = spy_wins + $sw, merc_wins = merc_wins + $mw, " +
                        "losses = losses + $l, kills = kills + $k, deaths = deaths + $d WHERE account_id = $a;",
                        ("$a", id), ("$sw", won && row.Team == 0 ? 1 : 0), ("$mw", won && row.Team == 1 ? 1 : 0), ("$l", won ? 0 : 1),
                        ("$k", Math.Clamp(row.Kills, 0, 1000)), ("$d", Math.Clamp(row.Deaths, 0, 1000)));
                    foreach ((string feat, int value) in row.Feats ?? new Dictionary<string, int>())
                    {
                        if (Array.IndexOf(FeatKeys.All, feat) >= 0 && value > 0)
                        {
                            Exec("INSERT INTO feat_totals (account_id, feat, total) VALUES ($a, $f, $v) " +
                                "ON CONFLICT(account_id, feat) DO UPDATE SET total = total + $v;",
                                ("$a", id), ("$f", feat), ("$v", Math.Min(value, MaxFeatPerRound)));
                        }
                    }

                    if (won && CosmeticCatalog.BaseMap(report.Map) is { } baseMap)
                    {
                        Exec("INSERT INTO map_wins (account_id, map, team, wins) VALUES ($a, $m, $t, 1) " +
                            "ON CONFLICT(account_id, map, team) DO UPDATE SET wins = wins + 1;",
                            ("$a", id), ("$m", baseMap), ("$t", row.Team));
                    }

                    AccountProgress progress = ProgressLocked(id);
                    var newlyEarned = new List<Achievement>();
                    foreach (Achievement a in Achievements.All)
                    {
                        if (!progress.Earned.Contains(a.Id) && a.Earned(progress, row, report))
                        {
                            Exec("INSERT OR IGNORE INTO achievements (account_id, achievement_id, earned, server) VALUES ($a, $id, $now, $s);",
                                ("$a", id), ("$id", a.Id), ("$now", now), ("$s", report.ServerId));
                            progress.Earned.Add(a.Id);
                            newlyEarned.Add(a);
                        }
                    }

                    AwardLocked(id, row, report, newlyEarned, progress.Earned, now);

                    counted++;
                }

                AuditLocked("results", $"server {serverName} ({report.ServerId}), map {report.Map}, mode {report.Mode}, " +
                    $"winner {report.WinningTeam}, objectives {report.ObjectivesTaken}/{report.ObjectivesNeeded}, {counted} account(s)");
                return counted;
            });
        }
    }

    // ---- Profiles --------------------------------------------------------------------------------------------

    public ProfileData? Profile(long accountId)
    {
        lock (_gate)
        {
            return ProfileLocked(accountId);
        }
    }

    private ProfileData? ProfileLocked(long accountId) =>
        One("SELECT data, updated_ms FROM profiles WHERE account_id = $a;",
            r => new ProfileData { Data = r.GetString(0), Updated = DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(1)) }, ("$a", accountId));

    /// <summary>Keeps <paramref name="profile"/> when it is newer than the stored one; returns the one kept.</summary>
    public ProfileData PutProfile(long accountId, ProfileData profile)
    {
        lock (_gate)
        {
            // A clock ahead of the master's must not pin the record forever.
            long updated = Math.Min(profile.Updated.ToUnixTimeMilliseconds(), _time.GetUtcNow().ToUnixTimeMilliseconds());
            ProfileData? stored = ProfileLocked(accountId);
            if (stored is not null && stored.Updated.ToUnixTimeMilliseconds() >= updated)
            {
                return stored;
            }

            Exec("INSERT INTO profiles (account_id, data, updated_ms) VALUES ($a, $d, $u) " +
                "ON CONFLICT (account_id) DO UPDATE SET data = excluded.data, updated_ms = excluded.updated_ms;",
                ("$a", accountId), ("$d", profile.Data), ("$u", updated));
            return new ProfileData { Data = profile.Data, Updated = DateTimeOffset.FromUnixTimeMilliseconds(updated) };
        }
    }

    // ---- Audit -----------------------------------------------------------------------------------------------

    public void Audit(string action, string detail)
    {
        lock (_gate)
        {
            AuditLocked(action, detail);
        }
    }

    private void AuditLocked(string action, string detail) =>
        Exec("INSERT INTO audit (time, action, detail, actor) VALUES ($t, $a, $d, $who);",
            ("$t", Now), ("$a", action), ("$d", detail), ("$who", CurrentActor));

    /// <summary>The newest <paramref name="count"/> audit rows, oldest first.</summary>
    public List<AuditRow> AuditLog(int count)
    {
        lock (_gate)
        {
            List<AuditRow> rows = Query("SELECT id, time, action, detail, COALESCE(actor, '') FROM audit ORDER BY id DESC LIMIT $n;",
                ReadAudit, ("$n", count));
            rows.Reverse();
            return rows;
        }
    }

    /// <summary>A Unix time as the admin command and ban messages show it.</summary>
    public static string Format(long unixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        _db.Dispose();
    }

    // ---- SQL helpers (callers hold the lock) -----------------------------------------------------------------

    private T InTransaction<T>(Func<T> body)
    {
        if (_tx is not null)
        {
            return body();
        }

        _tx = _db.BeginTransaction();
        try
        {
            T result = body();
            _tx.Commit();
            return result;
        }
        catch
        {
            _tx.Rollback();
            throw;
        }
        finally
        {
            _tx.Dispose();
            _tx = null;
        }
    }

    private SqliteCommand Command(string sql, (string Name, object? Value)[] args)
    {
        SqliteCommand command = _db.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _tx;
        foreach ((string name, object? value) in args)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    private int Exec(string sql, params (string Name, object? Value)[] args)
    {
        using SqliteCommand command = Command(sql, args);
        return command.ExecuteNonQuery();
    }

    private object? Scalar(string sql, params (string Name, object? Value)[] args)
    {
        using SqliteCommand command = Command(sql, args);
        return command.ExecuteScalar();
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> read, params (string Name, object? Value)[] args)
    {
        using SqliteCommand command = Command(sql, args);
        using SqliteDataReader reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    private T? One<T>(string sql, Func<SqliteDataReader, T> read, params (string Name, object? Value)[] args)
    {
        List<T> rows = Query(sql, read, args);
        return rows.Count > 0 ? rows[0] : default;
    }
}
