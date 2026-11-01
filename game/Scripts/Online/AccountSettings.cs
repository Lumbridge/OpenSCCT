using System;
using System.Globalization;
using Godot;

namespace OpenScctGame.Online;

/// <summary>
/// The player's sign-in on our master (Discord, through the master), kept in <c>user://account.cfg</c>: the session
/// token the master gave, the account's id, claimed name and admin flag as last seen, and the profile sync's
/// bookkeeping (when this PC's settings last changed, and which account this PC last synced with). Smoke runs
/// (<see cref="OpenScctGame.Options.SettingsStore.Isolated"/>) are always signed out and never write the file.
/// </summary>
public sealed class AccountSettings
{
    public const string FilePath = "user://account.cfg";

    private static AccountSettings? _current;

    /// <summary>The master's session token (sent as <c>Authorization: Bearer</c>), or empty when signed out.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>The account's id on the master (0 when signed out).</summary>
    public long AccountId { get; set; }

    /// <summary>The claimed name, or empty until the player picks one.</summary>
    public string Name { get; set; } = string.Empty;

    public bool Admin { get; set; }

    /// <summary>When this PC's synced settings (settings and key layouts) last changed, or null when never.</summary>
    public DateTimeOffset? ProfileUpdated { get; set; }

    /// <summary>The account this PC last synced its settings with (0: none yet).</summary>
    public long SyncedAccountId { get; set; }

    /// <summary>Appearance baseline and unsent choices survive restarts and temporary loss of connectivity.</summary>
    public long AppearanceAccountId { get; set; }

    public string AppearanceMaster { get; set; } = string.Empty;

    public string AppearanceBaseline { get; set; } = string.Empty;

    public bool AppearanceDirty { get; set; }

    /// <summary>Smoke runs: always signed out, nothing written.</summary>
    public static bool Isolated => OpenScctGame.Options.SettingsStore.Isolated;

    /// <summary>True while the player is signed in (never in smoke runs).</summary>
    public bool SignedIn => !Isolated && Token.Length > 0;

    /// <summary>The claimed name when signed in and claimed, else null.</summary>
    public string? AccountName => SignedIn && Name.Length > 0 ? Name : null;

    public static AccountSettings Current => _current ??= Load();

    public static AccountSettings Load(string path = FilePath)
    {
        var s = new AccountSettings();
        var file = new ConfigFile();
        if (Isolated || file.Load(path) != Error.Ok)
        {
            return s;
        }

        s.Token = file.GetValue("account", "token", s.Token).AsString();
        s.AccountId = file.GetValue("account", "id", s.AccountId).AsInt64();
        s.Name = file.GetValue("account", "name", s.Name).AsString();
        s.Admin = file.GetValue("account", "admin", s.Admin).AsBool();
        string updated = file.GetValue("profile", "updated", string.Empty).AsString();
        s.ProfileUpdated = DateTimeOffset.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset when)
            ? when
            : null;
        s.SyncedAccountId = file.GetValue("profile", "synced_account", s.SyncedAccountId).AsInt64();
        s.AppearanceAccountId = file.GetValue("appearance", "account", 0L).AsInt64();
        s.AppearanceMaster = file.GetValue("appearance", "master", string.Empty).AsString();
        s.AppearanceBaseline = file.GetValue("appearance", "baseline", string.Empty).AsString();
        s.AppearanceDirty = file.GetValue("appearance", "dirty", false).AsBool();
        return s;
    }

    public Error Save(string path = FilePath)
    {
        if (Isolated)
        {
            return Error.Ok;
        }

        var file = new ConfigFile();
        file.SetValue("account", "token", Token);
        file.SetValue("account", "id", AccountId);
        file.SetValue("account", "name", Name);
        file.SetValue("account", "admin", Admin);
        file.SetValue("profile", "updated", ProfileUpdated?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
        file.SetValue("profile", "synced_account", SyncedAccountId);
        file.SetValue("appearance", "account", AppearanceAccountId);
        file.SetValue("appearance", "master", AppearanceMaster);
        file.SetValue("appearance", "baseline", AppearanceBaseline);
        file.SetValue("appearance", "dirty", AppearanceDirty);
        return file.Save(path);
    }

    /// <summary>Keeps what the master says about the account (id, name, admin).</summary>
    public void Remember(OpenScct.Net.Discovery.AccountInfo account)
    {
        ArgumentNullException.ThrowIfNull(account);
        AccountId = account.Id;
        Name = account.Name ?? string.Empty;
        Admin = account.Admin;
    }

    /// <summary>Forgets the sign-in on this PC (the profile bookkeeping stays).</summary>
    public void SignOutLocally()
    {
        Token = string.Empty;
        AccountId = 0;
        Name = string.Empty;
        Admin = false;
        Save();
    }
}
