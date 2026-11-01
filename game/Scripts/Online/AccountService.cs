using System;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net.Discovery;
using OpenScctGame.Controls;
using OpenScctGame.Options;

namespace OpenScctGame.Online;

/// <summary>
/// The player's account on our master, for the whole run: one node under the scene tree's root (made by the main
/// menu) that refreshes the account once at start-up, keeps the last <see cref="AccountInfo"/> for the Account page,
/// and syncs the player's settings with the account (<see cref="ProfileSync"/>): it takes the account's copy after a
/// sign-in and once at start-up, and sends this PC's copy 3 s after the settings or a key layout were saved. Master
/// calls run in the background; their answers are taken in <see cref="_Process"/>, on the main thread. A 401 from
/// the master signs the player out on this PC. Smoke runs never touch accounts.
/// </summary>
public partial class AccountService : Node
{
    public const string NodeName = "AccountService";

    /// <summary>How long after a save the settings are sent (more saves in that time send once).</summary>
    private const ulong PushDelayMs = 3000;

    private Task<AccountInfo>? _refresh;
    private Task<ProfileData?>? _pull;
    private Task<ProfileData>? _push;
    private ulong _pushAt;
    private bool _pushAgain;
    private bool _applying;
    private (string Master, string Token, long Account) _syncIdentity;

    private static (string Master, string Token, long Account) SyncIdentity =>
        (OnlineSettings.Current.MasterUrl.TrimEnd('/'), AccountSettings.Current.Token, AccountSettings.Current.AccountId);

    /// <summary>The account as the master last described it (null until a refresh or sign-in this run).</summary>
    public AccountInfo? Account { get; private set; }

    /// <summary>Raised when the sign-in or the account changes (the main menu's line, the Account page).</summary>
    public event Action? Changed;

    /// <summary>The service of <paramref name="tree"/>, made on first use.</summary>
    public static AccountService Get(SceneTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (tree.Root.GetNodeOrNull<AccountService>(NodeName) is { } existing)
        {
            return existing;
        }

        var service = new AccountService { Name = NodeName };
        tree.Root.AddChild(service);
        return service;
    }

    /// <summary>The master is set (accounts live there).</summary>
    public static bool HasMaster => OnlineSettings.Current.MasterUrl.Length > 0;

    /// <summary>
    /// A client for the master with the invite key and, when signed in, the session token; null when no master is set
    /// or its address is not usable. The caller disposes it.
    /// </summary>
    public static MasterClient? MakeClient(bool withToken = true)
    {
        OnlineSettings s = OnlineSettings.Current;
        if (s.MasterUrl.Length == 0)
        {
            return null;
        }

        try
        {
            var client = new MasterClient(s.MasterUrl, s.InviteKey.Length > 0 ? s.InviteKey : null);
            AccountSettings account = AccountSettings.Current;
            if (withToken && account.SignedIn)
            {
                client.SessionToken = account.Token;
            }

            return client;
        }
        catch (ArgumentException e)
        {
            GD.PushWarning("Account: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// Runs one master call in the background with a client made now (on the main thread) and disposed after. The
    /// task fails with a <see cref="MasterException"/> when no master is set.
    /// </summary>
    public static Task<T> Call<T>(Func<MasterClient, Task<T>> call, bool withToken = true)
    {
        ArgumentNullException.ThrowIfNull(call);
        MasterClient? client = MakeClient(withToken);
        if (client is null)
        {
            return Task.FromException<T>(new MasterException("Set a master server first (Find a Session)."));
        }

        return Task.Run(async () =>
        {
            using (client)
            {
                return await call(client).ConfigureAwait(false);
            }
        });
    }

    /// <summary>The message of a finished task's failure, for the player.</summary>
    public static string ErrorOf(Task task, string fallback = "The master server did not answer.")
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.Exception?.InnerException?.Message ?? fallback;
    }

    /// <summary>True when a finished task failed because the master no longer knows the sign-in.</summary>
    public static bool IsSignedOutError(Task task) =>
        task is { IsFaulted: true } && task.Exception?.InnerException is MasterException { StatusCode: 401 } &&
        AccountSettings.Current.SignedIn;

    public override void _Ready()
    {
        _syncIdentity = SyncIdentity;
        _observedAppearance = LocalAppearance();
        SettingsStore.Saved += OnSettingsSaved;
        InputBindings.UserFileSaved += OnLocalChange;
        AccountSettings account = AccountSettings.Current;
        if (account.SignedIn && HasMaster)
        {
            // Load an unsent revision baseline before network requests, including when starting offline.
            ConfigureAppearance(new AccountInfo { Id = account.AccountId, AppearanceSync = true });
            _refresh = Call(c => c.AccountAsync());
        }
    }

    public override void _ExitTree()
    {
        SettingsStore.Saved -= OnSettingsSaved;
        InputBindings.UserFileSaved -= OnLocalChange;
    }

    /// <summary>A finished sign-in (the Account page): keeps the token and the account, then takes the account's settings.</summary>
    public void SignedIn(string token, AccountInfo account)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        ArgumentNullException.ThrowIfNull(account);
        AccountSettings s = AccountSettings.Current;
        s.Token = token;
        s.Remember(account);
        s.Save();
        // A pending response for a previous sign-in cannot overwrite this account's settings.
        _refresh = null;
        _pull = null;
        _push = null;
        _pushAt = 0;
        _pushAgain = false;
        ResetAppearance();
        _syncIdentity = SyncIdentity;
        Account = account;
        ConfigureAppearance(account);
        Changed?.Invoke();
        Pull();
    }

    /// <summary>A newer description of the account (a claimed name, the Account page's refresh).</summary>
    public void Update(AccountInfo account)
    {
        ArgumentNullException.ThrowIfNull(account);
        AccountSettings s = AccountSettings.Current;
        s.Remember(account);
        s.Save();
        _syncIdentity = SyncIdentity;
        Account = account;
        ConfigureAppearance(account);
        Changed?.Invoke();
    }

    /// <summary>Forgets the sign-in on this PC (after the master's sign-out, or a 401).</summary>
    public void SignOutLocally()
    {
        AccountSettings.Current.SignOutLocally();
        Account = null;
        _refresh = null;
        _pull = null;
        _push = null;
        _pushAt = 0;
        _pushAgain = false;
        ResetAppearance();
        Changed?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (_syncIdentity != SyncIdentity)
        {
            // Master settings can change while requests are in flight. Drop their responses and revision
            // baselines before processing results or sending anything against the newly selected master.
            _syncIdentity = SyncIdentity;
            _refresh = null;
            _pull = null;
            _push = null;
            _pushAt = 0;
            _pushAgain = false;
            Account = null;
            ResetAppearance();
            if (CanSync)
            {
                ConfigureAppearance(new AccountInfo { Id = AccountSettings.Current.AccountId, AppearanceSync = true });
                _refresh = Call(c => c.AccountAsync());
            }

            Changed?.Invoke();
        }

        if (_refresh is { IsCompleted: true } refresh)
        {
            _refresh = null;
            if (refresh.IsCompletedSuccessfully)
            {
                Update(refresh.Result);
                Pull();
            }
            else if (IsSignedOutError(refresh))
            {
                GD.Print("SVM_ACCOUNT the master no longer knows this sign-in; signed out.");
                SignOutLocally();
            }
            else
            {
                GD.Print("SVM_ACCOUNT refresh failed: " + ErrorOf(refresh));
            }
        }

        if (_pull is { IsCompleted: true } pull)
        {
            _pull = null;
            if (pull.IsCompletedSuccessfully)
            {
                Pulled(pull.Result);
            }
            else if (IsSignedOutError(pull))
            {
                SignOutLocally();
            }
            else
            {
                GD.Print("SVM_ACCOUNT profile pull failed: " + ErrorOf(pull));
            }
        }

        if (_push is { IsCompleted: true } push)
        {
            _push = null;
            if (push.IsCompletedSuccessfully)
            {
                Pushed(push.Result);
            }
            else if (IsSignedOutError(push))
            {
                SignOutLocally();
            }
            else
            {
                GD.Print("SVM_ACCOUNT profile push failed: " + ErrorOf(push));
            }
        }

        if (_pushAt != 0 && _push is null && _pull is null && Time.GetTicksMsec() >= _pushAt)
        {
            _pushAt = 0;
            Push();
        }
        else if (_pushAgain && _push is null && _pushAt == 0)
        {
            _pushAgain = false;
            Push();
        }

        ProcessAppearance();
    }

    private static bool CanSync => !AccountSettings.Isolated && AccountSettings.Current.SignedIn && HasMaster;

    private void OnSettingsSaved(GameSettings settings)
    {
        ObserveAppearanceChange();
        OnLocalChange();
    }

    /// <summary>The settings or a key layout changed on this PC: note when, and send them a little later.</summary>
    private void OnLocalChange()
    {
        if (_applying || AccountSettings.Isolated)
        {
            return;
        }

        AccountSettings s = AccountSettings.Current;
        s.ProfileUpdated = DateTimeOffset.UtcNow;
        s.Save();
        if (CanSync)
        {
            _pushAt = Time.GetTicksMsec() + PushDelayMs;
        }
    }

    /// <summary>Asks for the account's settings (after a sign-in, and once at start-up).</summary>
    private void Pull()
    {
        if (CanSync && _pull is null)
        {
            _pull = Call(c => c.GetProfileAsync());
        }
    }

    private void Pulled(ProfileData? remote)
    {
        AccountSettings s = AccountSettings.Current;
        if (remote is null)
        {
            GD.Print("SVM_ACCOUNT the account has no settings yet; sending this PC's.");
            Push();
            return;
        }

        if (s.SyncedAccountId != s.AccountId || s.ProfileUpdated is not { } local || remote.Updated > local)
        {
            Take(remote);
        }
        else if (local > remote.Updated)
        {
            Push();
        }
    }

    /// <summary>Sends this PC's settings now (a send already on its way sends again when it is back).</summary>
    private void Push()
    {
        if (!CanSync)
        {
            return;
        }

        if (_push is not null)
        {
            _pushAgain = true;
            return;
        }

        string data = ProfileSync.Build(_appearanceSupported == true);
        if (System.Text.Encoding.UTF8.GetByteCount(data) > MasterApi.MaxProfileBytes)
        {
            GD.PushWarning("Profile sync: the settings are larger than the master keeps; not sent.");
            return;
        }

        AccountSettings s = AccountSettings.Current;
        s.ProfileUpdated ??= DateTimeOffset.UtcNow;
        var profile = new ProfileData { Data = data, Updated = s.ProfileUpdated.Value };
        _push = Call(c => c.PutProfileAsync(profile));
    }

    private void Pushed(ProfileData stored)
    {
        AccountSettings s = AccountSettings.Current;
        if (s.ProfileUpdated is { } local && stored.Updated > local)
        {
            // The master had a newer copy from another PC and kept it.
            Take(stored);
            return;
        }

        s.SyncedAccountId = s.AccountId;
        s.Save();
        GD.Print("SVM_ACCOUNT settings sent to the account.");
    }

    /// <summary>Puts the account's settings on this PC without sending them back.</summary>
    private void Take(ProfileData remote)
    {
        _applying = true;
        bool applied;
        try
        {
            applied = ProfileSync.Apply(remote.Data, _appearanceSupported == true);
        }
        finally
        {
            _applying = false;
        }

        if (!applied)
        {
            return;
        }

        AccountSettings s = AccountSettings.Current;
        s.SyncedAccountId = s.AccountId;
        s.ProfileUpdated = remote.Updated;
        s.Save();
        _pushAt = 0;
        GD.Print("SVM_ACCOUNT took the account's settings.");
        Changed?.Invoke();
    }
}
