using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

/// <summary>
/// <c>svm-master admin ...</c>: bans, names and lookups on the account database, run on the VPS next to the
/// running master. The master reads bans fresh on every ticket, heartbeat and sign-in, so changes apply at once.
/// Exit codes: 0 done, 1 not found, 2 usage.
/// </summary>
internal static class AdminCommands
{
    public const string Usage = """
        Usage: svm-master admin <command> [args] [--config <file>]
          ban <name|discord id|ip|cidr> [--days N] [--reason text]   ban an account (and its Discord id) or an address
          unban <name|discord id|ip|cidr|#ban id>                   end the matching bans
          bans                                                      list the bans in force
          rename <name> <new name>                                  rename an account (no cooldown)
          release-name <name>                                       take a name away; the account picks a new one
          whois <name|discord id>                                   an account: ids, dates, stats, bans, old names
          audit [N]                                                 the last N audit entries (default 20)
        """;

    private const int Ok = 0;
    private const int NotFound = 1;
    private const int BadUsage = 2;

    public static int Run(IReadOnlyList<string> args, MasterOptions options, TextWriter output, TextWriter errors)
    {
        if (args.Count == 0 || args[0] is "help" or "--help" or "-h")
        {
            output.WriteLine(Usage);
            return args.Count == 0 ? BadUsage : Ok;
        }

        AccountStore store;
        try
        {
            store = AccountStore.Open(options.ResolvePath(options.Database));
        }
        catch (InvalidOperationException e)
        {
            errors.WriteLine(e.Message);
            return 3;
        }

        using (store)
        {
            var admin = new Session(store, options, output, errors);
            string[] rest = [.. args];
            return args[0] switch
            {
                "ban" => admin.Ban(rest[1..]),
                "unban" when rest.Length == 2 => admin.Unban(rest[1]),
                "bans" when rest.Length == 1 => admin.Bans(),
                "rename" when rest.Length == 3 => admin.Rename(rest[1], rest[2]),
                "release-name" when rest.Length == 2 => admin.ReleaseName(rest[1]),
                "whois" when rest.Length == 2 => admin.Whois(rest[1]),
                "audit" when rest.Length <= 2 => admin.Audit(rest.Length == 2 ? rest[1] : null),
                _ => admin.UsageError(),
            };
        }
    }

    private sealed class Session(AccountStore store, MasterOptions options, TextWriter output, TextWriter errors)
    {
        public int UsageError()
        {
            errors.WriteLine(Usage);
            return BadUsage;
        }

        /// <summary>The account a name or Discord id names (a Discord id with no account yet gives null).</summary>
        private AccountRow? FindAccount(string who) =>
            MasterOptions.IsSnowflake(who) ? store.FindByDiscord(who) : store.FindByName(who);

        public int Ban(string[] args)
        {
            if (args.Length == 0)
            {
                return UsageError();
            }

            string target = args[0];
            double? days = null;
            string reason = string.Empty;
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--days" when i + 1 < args.Length &&
                        double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0:
                        days = d;
                        i++;
                        break;
                    case "--reason" when i + 1 < args.Length:
                        // The rest of the line, so an unquoted reason works too.
                        reason = string.Join(' ', args[(i + 1)..]);
                        i = args.Length;
                        break;
                    default:
                        return UsageError();
                }
            }

            long? until = days is { } n ? store.Now + (long)(n * 86400) : null;
            if (AccountStore.NormalizeIp(target) is { } ip)
            {
                long id = store.AddBan(null, null, ip, reason, until);
                output.WriteLine($"Ban #{id}: address {ip}, {Until(until)}.");
                return Ok;
            }

            AccountRow? account = FindAccount(target);
            if (account is null && !MasterOptions.IsSnowflake(target))
            {
                errors.WriteLine($"No account is called '{target}'.");
                return NotFound;
            }

            string discord = account?.DiscordId ?? target;
            long banId = store.AddBan(account?.Id, discord, null, reason, until);
            output.WriteLine(account is null
                ? $"Ban #{banId}: Discord id {discord} (no account yet), {Until(until)}."
                : $"Ban #{banId}: account {account.Id} ({account.Name ?? "no name"}, Discord {discord}), {Until(until)}.");
            return Ok;
        }

        public int Unban(string target)
        {
            int ended;
            if (target.StartsWith('#') && long.TryParse(target.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out long banId))
            {
                ended = store.EndBans(banId, null, null, null);
            }
            else if (AccountStore.NormalizeIp(target) is { } ip)
            {
                ended = store.EndBans(null, null, null, ip);
            }
            else if (FindAccount(target) is { } account)
            {
                ended = store.EndBans(null, account.Id, account.DiscordId, null);
            }
            else if (MasterOptions.IsSnowflake(target))
            {
                ended = store.EndBans(null, null, target, null);
            }
            else if (long.TryParse(target, NumberStyles.None, CultureInfo.InvariantCulture, out long plainId))
            {
                ended = store.EndBans(plainId, null, null, null);
            }
            else
            {
                errors.WriteLine($"No account is called '{target}'.");
                return NotFound;
            }

            if (ended == 0)
            {
                errors.WriteLine($"No ban in force matches '{target}'.");
                return NotFound;
            }

            output.WriteLine($"Ended {ended} ban(s).");
            return Ok;
        }

        public int Bans()
        {
            List<BanRow> bans = store.ActiveBans();
            if (bans.Count == 0)
            {
                output.WriteLine("No bans in force.");
                return Ok;
            }

            foreach (BanRow b in bans)
            {
                output.WriteLine($"#{b.Id}  {Target(b)}  {Until(b.Until)}  added {AccountStore.Format(b.Added)}  " +
                    (string.IsNullOrWhiteSpace(b.Reason) ? "(no reason)" : b.Reason));
            }

            return Ok;
        }

        private string Target(BanRow b)
        {
            if (b.Ip is not null)
            {
                return "address " + b.Ip;
            }

            if (b.AccountId is { } id)
            {
                string name = store.FindById(id)?.Name ?? "no name";
                return $"account {id} ({name}, Discord {b.DiscordId ?? "-"})";
            }

            return "Discord " + (b.DiscordId ?? "-");
        }

        public int Rename(string who, string newName)
        {
            if (FindAccount(who) is not { } account)
            {
                errors.WriteLine($"No account is called '{who}'.");
                return NotFound;
            }

            if (PlayerNames.ClaimProblem(newName) is { } problem)
            {
                errors.WriteLine(problem);
                return BadUsage;
            }

            switch (store.ClaimName(account.Id, newName, ignoreCooldown: true, out _))
            {
                case NameClaim.Taken:
                    errors.WriteLine($"'{newName}' is taken (or held for its last owner; release-name frees it).");
                    return BadUsage;
                case NameClaim.NoAccount:
                    errors.WriteLine("The account is gone.");
                    return NotFound;
                default:
                    output.WriteLine($"Account {account.Id}: {account.Name ?? "(no name)"} -> {newName}.");
                    return Ok;
            }
        }

        public int ReleaseName(string name)
        {
            AccountRow? account = store.FindByName(name);
            if (account is null || !store.ReleaseName(account.Id))
            {
                errors.WriteLine($"No account is called '{name}'.");
                return NotFound;
            }

            output.WriteLine($"Account {account.Id} no longer has the name {account.Name}; it picks a new one at its next sign-in.");
            return Ok;
        }

        public int Whois(string who)
        {
            if (FindAccount(who) is not { } a)
            {
                errors.WriteLine($"No account is called '{who}'.");
                return NotFound;
            }

            AccountStats s = store.Stats(a.Id);
            output.WriteLine($"Account     {a.Id}{(options.IsAdmin(a.DiscordId) ? "  (admin)" : string.Empty)}");
            output.WriteLine($"Discord id  {a.DiscordId}");
            output.WriteLine($"Name        {a.Name ?? "(none)"}" + (a.NameChanged is { } c ? $"  (set {AccountStore.Format(c)})" : string.Empty));
            output.WriteLine($"Created     {AccountStore.Format(a.Created)}");
            output.WriteLine($"Last seen   {AccountStore.Format(a.LastSeen)}");
            output.WriteLine($"Stats       {s.Matches} matches, {s.SpyWins} spy wins, {s.MercWins} merc wins, {s.Losses} losses, " +
                $"{s.Kills} kills, {s.Deaths} deaths");
            output.WriteLine($"Achievements {store.Earned(a.Id).Count} of {Achievements.All.Count}, cosmetics {Achievements.Unlocked(store.Earned(a.Id).Keys).Count} of {OpenScct.Net.Cosmetics.CosmeticCatalog.All.Count}");
            BanRow? ban = store.ActiveBan(a.Id, a.DiscordId, null);
            output.WriteLine(ban is null ? "Bans        none in force" : $"Bans        #{ban.Id}: {ban.Describe()}");
            List<NameHistoryRow> history = store.NameHistory(a.Id);
            if (history.Count == 0)
            {
                output.WriteLine("Old names   none");
            }

            foreach (NameHistoryRow h in history)
            {
                output.WriteLine($"Old name    {h.Name}  (until {AccountStore.Format(h.Changed)}, held to {AccountStore.Format(h.Until)})");
            }

            return Ok;
        }

        public int Audit(string? countText)
        {
            int count = 20;
            if (countText is not null && (!int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1))
            {
                return UsageError();
            }

            List<AuditRow> rows = store.AuditLog(Math.Min(count, 10000));
            if (rows.Count == 0)
            {
                output.WriteLine("The audit log is empty.");
            }

            foreach (AuditRow r in rows)
            {
                output.WriteLine($"{AccountStore.Format(r.Time)}  {r.Action,-12}  {r.Detail}");
            }

            return Ok;
        }

        private static string Until(long? until) => until is { } u ? "until " + AccountStore.Format(u) : "permanent";
    }
}
