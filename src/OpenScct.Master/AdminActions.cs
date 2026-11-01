using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

/// <summary>What an admin action did: success, and the line to show (CLI output or the portal's notice).</summary>
internal readonly record struct AdminResult(bool Ok, string Message, bool NotFound = false)
{
    public static AdminResult Done(string message) => new(true, message);

    public static AdminResult Missing(string message) => new(false, message, NotFound: true);

    public static AdminResult Refused(string message) => new(false, message);
}

/// <summary>
/// The admin actions shared by the command line (<see cref="AdminCommands"/>) and the web portal: finding accounts,
/// bans, names and cosmetics. Callers set who acts with <see cref="AccountStore.ActingAs"/> for the audit log.
/// </summary>
internal sealed class AdminActions(AccountStore store)
{
    /// <summary>The account a name, a Discord id or "#id" (account id) names, or null.</summary>
    public AccountRow? FindAccount(string who)
    {
        who = who.Trim();
        if (who.StartsWith('#') && long.TryParse(who.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out long id))
        {
            return store.FindById(id);
        }

        return MasterOptions.IsSnowflake(who) ? store.FindByDiscord(who) : store.FindByName(who);
    }

    /// <summary>Bans an address or CIDR range, an account (and its Discord id) or a Discord id with no account yet.</summary>
    public AdminResult Ban(string target, double? days, string reason)
    {
        if (days is { } duration && (!double.IsFinite(duration) || duration <= 0 || duration > 36500))
        {
            return AdminResult.Refused("A temporary ban must be a positive duration of at most 36,500 days; omit the duration only for a permanent ban.");
        }

        long? until = days is > 0 and { } n ? store.Now + (long)(n * 86400) : null;
        if (AccountStore.NormalizeIp(target) is { } ip)
        {
            long id = store.AddBan(null, null, ip, reason, until);
            return AdminResult.Done($"Ban #{id}: address {ip}, {Until(until)}.");
        }

        AccountRow? account = FindAccount(target);
        if (account is null && !MasterOptions.IsSnowflake(target))
        {
            return AdminResult.Missing($"No account is called '{target}'.");
        }

        string discord = account?.DiscordId ?? target;
        long banId = store.AddBan(account?.Id, discord, null, reason, until);
        return AdminResult.Done(account is null
            ? $"Ban #{banId}: Discord id {discord} (no account yet), {Until(until)}."
            : $"Ban #{banId}: account {account.Id} ({account.Name ?? "no name"}, Discord {discord}), {Until(until)}.");
    }

    /// <summary>Ends the bans in force on a "#ban id", an address, an account or a Discord id.</summary>
    public AdminResult Unban(string target)
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
            return AdminResult.Missing($"No account is called '{target}'.");
        }

        return ended == 0 ? AdminResult.Missing($"No ban in force matches '{target}'.") : AdminResult.Done($"Ended {ended} ban(s).");
    }

    /// <summary>What a ban is on, for lists.</summary>
    public string BanTarget(BanRow b)
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

    /// <summary>Gives an account a name at once (no cooldown).</summary>
    public AdminResult Rename(AccountRow account, string newName)
    {
        if (PlayerNames.ClaimProblem(newName) is { } problem)
        {
            return AdminResult.Refused(problem);
        }

        return store.ClaimName(account.Id, newName, ignoreCooldown: true, out _) switch
        {
            NameClaim.Taken => AdminResult.Refused($"'{newName}' is taken (or held for its last owner; releasing it frees it)."),
            NameClaim.NoAccount => AdminResult.Missing("The account is gone."),
            _ => AdminResult.Done($"Account {account.Id}: {account.Name ?? "(no name)"} -> {newName}."),
        };
    }

    /// <summary>Takes an account's name away; it picks a new one at its next sign-in.</summary>
    public AdminResult ReleaseName(AccountRow account) =>
        store.ReleaseName(account.Id)
            ? AdminResult.Done($"Account {account.Id} no longer has the name {account.Name}; it picks a new one at its next sign-in.")
            : AdminResult.Missing("The account has no name to release.");

    /// <summary>The cosmetics "all", "spy", "merc" or item ids name, or the word that matched nothing.</summary>
    public static (List<string> Items, string? Unknown) Cosmetics(IEnumerable<string> what)
    {
        var items = new List<string>();
        foreach (string w in what)
        {
            IEnumerable<CosmeticItem> matched = w.Trim().ToLowerInvariant() switch
            {
                "all" => CosmeticCatalog.All,
                "spy" => CosmeticCatalog.All.Where(i => i.Side == CosmeticSide.Spy),
                "merc" => CosmeticCatalog.All.Where(i => i.Side == CosmeticSide.Merc),
                _ => CosmeticCatalog.Find(w.Trim()) is { } one ? [one] : [],
            };
            var ids = matched.Select(i => i.Id).ToList();
            if (ids.Count == 0)
            {
                return (items, w);
            }

            items.AddRange(ids.Where(id => !items.Contains(id)));
        }

        return (items, null);
    }

    /// <summary>Unlocks (or takes back) cosmetics for an account without the achievements.</summary>
    public AdminResult Grant(AccountRow account, IEnumerable<string> what, bool unlock)
    {
        (List<string> items, string? unknown) = Cosmetics(what);
        if (unknown is not null)
        {
            return AdminResult.Missing($"No cosmetic is called '{unknown}'.");
        }

        int changed = store.SetGrants(account.Id, items, unlock);
        return AdminResult.Done($"{(unlock ? "Unlocked" : "Took back")} {changed} cosmetic(s) for {account.Name ?? account.DiscordId}; " +
            $"it has {store.Unlocked(account.Id).Count} of {CosmeticCatalog.All.Count}. " +
            "The game sees the change on its next account refresh (opening the Armoury or Account page).");
    }

    /// <summary>Sets an account's prestige and level (the XP becomes the start of that level).</summary>
    public AdminResult SetRank(AccountRow account, string prestigeText, string levelText)
    {
        if (!int.TryParse(prestigeText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int prestige) ||
            !int.TryParse(levelText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int level) ||
            prestige > ProgressionK.MaxPrestige || level < 1 || level > ProgressionK.MaxLevel)
        {
            return AdminResult.Refused($"Prestige is 0 to {ProgressionK.MaxPrestige} and level 1 to {ProgressionK.MaxLevel}.");
        }

        store.SetRank(account.Id, prestige, level);
        return AdminResult.Done($"{account.Name ?? account.DiscordId} is now prestige {prestige}, level {level}. " +
            "Servers see it at the player's next join.");
    }

    public static string Until(long? until) => until is { } u ? "until " + AccountStore.Format(u) : "permanent";
}
