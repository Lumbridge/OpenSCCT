using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenScct.Net.Cosmetics;
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
          unlock <name|discord id> <all|spy|merc|item id...>         give an account cosmetics without the achievements
          lock <name|discord id> <all|spy|merc|item id...>           take back cosmetics given with unlock
          cosmetics                                                 list every cosmetic item id
          rank <name|discord id> <prestige> <level>                 set an account's prestige (0-10) and level (1-55)
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
        using (AccountStore.ActingAs("cli"))
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
                "unlock" when rest.Length >= 3 => admin.Grant(rest[1], rest[2..], unlock: true),
                "lock" when rest.Length >= 3 => admin.Grant(rest[1], rest[2..], unlock: false),
                "cosmetics" when rest.Length == 1 => admin.Cosmetics(),
                "rank" when rest.Length == 4 => admin.Rank(rest[1], rest[2], rest[3]),
                "audit" when rest.Length <= 2 => admin.Audit(rest.Length == 2 ? rest[1] : null),
                _ => admin.UsageError(),
            };
        }
    }

    private sealed class Session(AccountStore store, MasterOptions options, TextWriter output, TextWriter errors)
    {
        private readonly AdminActions _actions = new(store);

        private int Report(AdminResult result)
        {
            (result.Ok ? output : errors).WriteLine(result.Message);
            return result.Ok ? Ok : result.NotFound ? NotFound : BadUsage;
        }

        public int UsageError()
        {
            errors.WriteLine(Usage);
            return BadUsage;
        }

        /// <summary>The account a name or Discord id names (a Discord id with no account yet gives null).</summary>
        private AccountRow? FindAccount(string who) => _actions.FindAccount(who);

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

            return Report(_actions.Ban(target, days, reason));
        }

        public int Unban(string target) => Report(_actions.Unban(target));

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

        private string Target(BanRow b) => _actions.BanTarget(b);

        public int Rename(string who, string newName) =>
            FindAccount(who) is { } account ? Report(_actions.Rename(account, newName)) : Report(AdminResult.Missing($"No account is called '{who}'."));

        public int ReleaseName(string name) =>
            store.FindByName(name) is { } account ? Report(_actions.ReleaseName(account)) : Report(AdminResult.Missing($"No account is called '{name}'."));

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
            OpenScct.Net.Cosmetics.PlayerRank rank = store.ProgressionOf(a.Id).Rank;
            output.WriteLine($"Rank        prestige {rank.Prestige}, level {rank.Level}");
            output.WriteLine($"Stats       {s.Matches} matches, {s.SpyWins} spy wins, {s.MercWins} merc wins, {s.Losses} losses, " +
                $"{s.Kills} kills, {s.Deaths} deaths");
            output.WriteLine($"Achievements {store.Earned(a.Id).Count} of {Achievements.All.Count}, cosmetics {store.Unlocked(a.Id).Count} of {OpenScct.Net.Cosmetics.CosmeticCatalog.All.Count} ({store.Grants(a.Id).Count} given by unlock)");
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

        public int Grant(string who, string[] what, bool unlock) =>
            FindAccount(who) is { } a ? Report(_actions.Grant(a, what, unlock)) : Report(AdminResult.Missing($"No account is called '{who}'."));

        public int Rank(string who, string prestige, string level) =>
            FindAccount(who) is { } a ? Report(_actions.SetRank(a, prestige, level)) : Report(AdminResult.Missing($"No account is called '{who}'."));

        public int Cosmetics()
        {
            foreach (OpenScct.Net.Cosmetics.CosmeticItem i in OpenScct.Net.Cosmetics.CosmeticCatalog.All)
            {
                output.WriteLine($"{i.Id,-28}  {OpenScct.Net.Cosmetics.CosmeticCatalog.Describe(i)}");
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
                output.WriteLine($"{AccountStore.Format(r.Time)}  {r.Action,-12}  {r.Actor,-14}  {r.Detail}");
            }

            return Ok;
        }

        private static string Until(long? until) => AdminActions.Until(until);
    }
}
