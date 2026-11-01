using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using OpenScct.Server.Online;
using OpenScctGame.Hub;
using OpenScctGame.Options;
using OpenScctGame.Play;

namespace OpenScctGame.Online;

public partial class AccountService
{
    private sealed record AppearanceSnapshot(AccountInfo Account, AccountAppearance? Appearance);

    private bool? _appearanceSupported;
    private long _appearanceAccount;
    private string _appearanceMaster = string.Empty;
    private AccountAppearance? _appearanceBaseline;
    private AccountAppearance? _appearanceRemote;
    private AccountAppearance? _observedAppearance;
    private AccountAppearance? _appearanceSending;
    private Task<AppearanceSnapshot>? _appearancePull;
    private Task<AppearanceUpdateResult>? _appearancePush;
    private ulong _appearanceNextPoll;
    private ulong _appearancePushAt;
    private bool _appearanceDirty;
    private Action? _appearanceBeforeJoin;
    private Node? _appearanceJoinScene;
    private ConfirmationDialog? _appearanceDialog;

    public string AppearanceStatus { get; private set; } = string.Empty;

    public bool AppearanceConflict { get; private set; }

    public bool AppearanceManaged => _appearanceSupported == true;

    public event Action? AppearanceChanged;

    private static AccountAppearance LocalAppearance() => new()
    {
        Spy = SettingsStore.Current.CosmeticsSpy.Order(StringComparer.Ordinal).ToArray(),
        Merc = SettingsStore.Current.CosmeticsMerc.Order(StringComparer.Ordinal).ToArray(),
        Title = SettingsStore.Current.Title,
    };

    private static bool SameAppearance(AccountAppearance a, AccountAppearance b) => a.Title == b.Title &&
        a.Spy.Order(StringComparer.Ordinal).SequenceEqual(b.Spy.Order(StringComparer.Ordinal)) &&
        a.Merc.Order(StringComparer.Ordinal).SequenceEqual(b.Merc.Order(StringComparer.Ordinal));

    private bool SafeForAppearance()
    {
        SceneTree tree = GetTree();
        OnlineClient? online = tree.Root.GetNodeOrNull<OnlineService>(OnlineService.NodeName)?.Client;
        if (online?.State is OnlineClientState.Loading or OnlineClientState.Match) return false;
        // Hubs already support live appearance changes. Offline rounds and replay scenes stay untouched.
        return tree.CurrentScene is not PlayRoot play || play.Session?.IsStopped == true ||
            tree.Root.GetNodeOrNull<HubService>(HubService.NodeName)?.Client is not null;
    }

    private void ConfigureAppearance(AccountInfo account)
    {
        string master = OnlineSettings.Current.MasterUrl.TrimEnd('/');
        if (_appearanceAccount != account.Id || _appearanceMaster != master)
        {
            Action? ready = _appearanceBeforeJoin;
            Node? scene = _appearanceJoinScene;
            ResetAppearance();
            _appearanceBeforeJoin = ready;
            _appearanceJoinScene = scene;
            _appearanceAccount = account.Id;
            _appearanceMaster = master;
            AccountSettings saved = AccountSettings.Current;
            if (saved.AppearanceAccountId == account.Id && saved.AppearanceMaster == master)
            {
                try
                {
                    _appearanceBaseline = JsonSerializer.Deserialize<AccountAppearance>(saved.AppearanceBaseline, MasterApi.Json);
                    _appearanceDirty = saved.AppearanceDirty;
                }
                catch (JsonException) { _appearanceBaseline = null; }
            }

            _observedAppearance = LocalAppearance();
        }

        _appearanceSupported = account.AppearanceSync;
        if (!_appearanceSupported.Value)
        {
            AppearanceStatus = "This master uses appearance sync at sign-in.";
        }
    }

    private void ResetAppearance()
    {
        _appearanceSupported = null;
        _appearanceAccount = 0;
        _appearanceMaster = string.Empty;
        _appearanceBaseline = null;
        _appearanceRemote = null;
        _appearancePull = null;
        _appearancePush = null;
        _appearanceSending = null;
        _appearanceBeforeJoin = null;
        _appearanceJoinScene = null;
        _appearanceDirty = false;
        _appearanceNextPoll = 0;
        _appearancePushAt = 0;
        AppearanceConflict = false;
        AppearanceStatus = string.Empty;
        _appearanceDialog?.QueueFree();
        _appearanceDialog = null;
    }

    private void PersistAppearance()
    {
        if (!CanSync || _appearanceAccount == 0) return;
        AccountSettings saved = AccountSettings.Current;
        saved.AppearanceAccountId = _appearanceAccount;
        saved.AppearanceMaster = _appearanceMaster;
        saved.AppearanceBaseline = _appearanceBaseline is null ? string.Empty : JsonSerializer.Serialize(_appearanceBaseline, MasterApi.Json);
        saved.AppearanceDirty = _appearanceDirty;
        saved.Save();
    }

    private void ObserveAppearanceChange()
    {
        AccountAppearance current = LocalAppearance();
        bool changed = _observedAppearance is not null && !SameAppearance(_observedAppearance, current);
        _observedAppearance = current;
        if (!changed || _applying || AccountSettings.Isolated || !AccountSettings.Current.SignedIn) return;
        // A known account's pending edits also survive a temporarily unreachable master at startup.
        if (_appearanceSupported == false) return;
        _appearanceDirty = true;
        _appearancePushAt = Time.GetTicksMsec() + PushDelayMs;
        AppearanceStatus = AppearanceConflict ? "Your choices are kept locally. Resolve the appearance conflict to sync them." : "Appearance changes waiting to sync.";
        PersistAppearance();
        AppearanceChanged?.Invoke();
    }

    private void PullAppearance()
    {
        if (!CanSync || _appearancePull is not null || _appearancePush is not null) return;
        _appearancePull = Call(async client =>
        {
            AccountInfo info = await client.AccountAsync().ConfigureAwait(false);
            AccountAppearance? appearance = info.AppearanceSync ? await client.AppearanceAsync().ConfigureAwait(false) : null;
            return new AppearanceSnapshot(info, appearance);
        });
        _appearanceNextPoll = Time.GetTicksMsec() + 30_000;
    }

    /// <summary>Refreshes before a new connection without blocking the main thread or making a stale write.</summary>
    public void PrepareAppearance(Action ready)
    {
        ArgumentNullException.ThrowIfNull(ready);
        if (!CanSync || _appearanceSupported == false)
        {
            ready();
            return;
        }

        _appearanceBeforeJoin = ready;
        _appearanceJoinScene = GetTree().CurrentScene;
        _appearancePushAt = 0;
        AppearanceStatus = "Checking appearance before joining…";
        if (AppearanceConflict) ShowAppearanceConflict();
        else PullAppearance();
    }

    private void FinishAppearancePreparation()
    {
        if (AppearanceConflict || _appearancePull is not null || _appearancePush is not null || _appearanceBeforeJoin is not { } ready) return;
        _appearanceBeforeJoin = null;
        Node? scene = _appearanceJoinScene;
        _appearanceJoinScene = null;
        if (scene is not null && (!GodotObject.IsInstanceValid(scene) || GetTree().CurrentScene != scene)) return;
        ready();
    }

    private void ProcessAppearance()
    {
        if (!CanSync || !SafeForAppearance())
        {
            _appearanceDialog?.Hide();
            return;
        }
        ulong now = Time.GetTicksMsec();
        if (_appearancePull is { IsCompleted: true } pull)
        {
            _appearancePull = null;
            if (pull.IsCompletedSuccessfully)
            {
                Update(pull.Result.Account);
                if (pull.Result.Appearance is { } remote) ReceiveAppearance(remote);
                else FinishAppearancePreparation();
            }
            else
            {
                AppearanceFailure(pull);
                FinishAppearancePreparation();
            }
        }

        if (_appearancePush is { IsCompleted: true } push)
        {
            _appearancePush = null;
            if (push.IsCompletedSuccessfully)
            {
                AppearanceUpdateResult result = push.Result;
                _appearanceRemote = result.Appearance;
                if (result.Success)
                {
                    _appearanceBaseline = result.Appearance;
                    // A second choice made while the first was being sent must not be replaced by its response.
                    _appearanceDirty = _appearanceSending is not null && !SameAppearance(LocalAppearance(), _appearanceSending);
                    if (!_appearanceDirty) ApplyAppearance(result.Appearance);
                    else PersistAppearance();
                    AppearanceStatus = _appearanceDirty ? "Saving your latest appearance…" : "Appearance synced. Titles are shown to others from your next join.";
                }
                else if (result.Conflict)
                {
                    ConflictAppearance(result.Appearance);
                }
                else
                {
                    // Keep the rejected draft; do not repeatedly submit it or discard it silently.
                    AppearanceConflict = true;
                    AppearanceStatus = result.Error ?? "The appearance could not be saved. Reload or adjust your choices.";
                    PersistAppearance();
                    ShowAppearanceConflict();
                }

                _appearanceSending = null;
                AppearanceChanged?.Invoke();
                if (!_appearanceDirty || AppearanceConflict) FinishAppearancePreparation();
            }
            else
            {
                _appearanceSending = null;
                AppearanceFailure(push);
                FinishAppearancePreparation();
            }
        }

        if (_appearanceSupported == false) return;
        if (_appearanceDirty && !AppearanceConflict && _appearanceBaseline is not null && _appearancePull is null && _appearancePush is null && now >= _appearancePushAt)
        {
            PushAppearance();
        }
        else if (_appearancePull is null && _appearancePush is null && now >= _appearanceNextPoll)
        {
            PullAppearance();
        }
    }

    private void ReceiveAppearance(AccountAppearance remote)
    {
        _appearanceRemote = remote;
        AccountAppearance local = LocalAppearance();
        if (_appearanceDirty && !SameAppearance(local, remote))
        {
            if (_appearanceBaseline is null || remote.Revision != _appearanceBaseline.Revision)
            {
                ConflictAppearance(remote);
                return;
            }

            PushAppearance();
            return;
        }

        AppearanceStatus = "Appearance is up to date. Website changes sync here while you are in menus or a lobby.";
        ApplyAppearance(remote);
        FinishAppearancePreparation();
    }

    private void PushAppearance()
    {
        if (_appearanceBaseline is null || _appearancePush is not null || !SafeForAppearance()) return;
        AccountAppearance selection = LocalAppearance();
        _appearanceSending = selection;
        var request = new AppearanceUpdateRequest
        {
            ExpectedRevision = _appearanceBaseline.Revision, Spy = selection.Spy, Merc = selection.Merc, Title = selection.Title,
        };
        _appearancePush = Call(c => c.UpdateAppearanceAsync(request));
        _appearancePushAt = Time.GetTicksMsec() + 30_000;
        AppearanceStatus = "Saving appearance…";
        AppearanceChanged?.Invoke();
    }

    private void ApplyAppearance(AccountAppearance remote)
    {
        _appearanceBaseline = remote;
        _appearanceRemote = remote;
        _appearanceDirty = false;
        AppearanceConflict = false;
        bool changed = !SameAppearance(LocalAppearance(), remote);
        if (changed)
        {
            _applying = true;
            try
            {
                GameSettings settings = SettingsStore.Current;
                settings.CosmeticsSpy = remote.Spy.ToArray();
                settings.CosmeticsMerc = remote.Merc.ToArray();
                settings.Title = remote.Title;
                SettingsStore.Save(settings);
            }
            finally { _applying = false; }

            OnlineClient? online = GetTree().Root.GetNodeOrNull<OnlineService>(OnlineService.NodeName)?.Client;
            if (online?.State == OnlineClientState.Lobby)
            {
                online.SetLook(SettingsStore.Current.SharedLook(CosmeticSide.Spy).ToHex(), SettingsStore.Current.SharedLook(CosmeticSide.Merc).ToHex());
            }
        }

        _observedAppearance = LocalAppearance();
        PersistAppearance();
        if (changed) Changed?.Invoke();
        AppearanceChanged?.Invoke();
    }

    private void ConflictAppearance(AccountAppearance remote)
    {
        bool newConflict = !AppearanceConflict || _appearanceRemote?.Revision != remote.Revision;
        _appearanceRemote = remote;
        AppearanceConflict = true;
        AppearanceStatus = "Appearance changed on the website or another device. Reload it or reapply your local choices.";
        PersistAppearance();
        AppearanceChanged?.Invoke();
        if (newConflict) ShowAppearanceConflict();
    }

    private void AppearanceFailure(Task task)
    {
        if (IsSignedOutError(task))
        {
            SignOutLocally();
            return;
        }

        AppearanceStatus = "Appearance sync unavailable: " + ErrorOf(task) + " Your local choices are kept; retrying in 30 seconds.";
        _appearanceNextPoll = Time.GetTicksMsec() + 30_000;
        _appearancePushAt = _appearanceNextPoll;
        PersistAppearance();
        AppearanceChanged?.Invoke();
    }

    public void ReloadAppearance()
    {
        if (!SafeForAppearance() || _appearanceRemote is not { } remote) return;
        AppearanceStatus = "Loaded the account's current appearance.";
        ApplyAppearance(remote);
        _appearanceDialog?.Hide();
        FinishAppearancePreparation();
    }

    public void ReapplyAppearance()
    {
        if (!SafeForAppearance() || _appearanceRemote is not { } remote) return;
        _appearanceBaseline = remote;
        AppearanceConflict = false;
        _appearanceDirty = true;
        PersistAppearance();
        _appearanceDialog?.Hide();
        PushAppearance();
    }

    private void ShowAppearanceConflict()
    {
        if (!SafeForAppearance() || _appearanceDialog is { Visible: true }) return;
        if (_appearanceDialog is null)
        {
            _appearanceDialog = new ConfirmationDialog
            {
                Title = "Appearance changed elsewhere", OkButtonText = "Reload account appearance", CancelButtonText = "Keep editing locally",
                DialogText = "The account appearance changed while you were editing. Reload the account selection, or reapply your local choices. Your controls and other settings are unaffected.",
                MinSize = new Vector2I(560, 160),
            };
            AddChild(_appearanceDialog);
            _appearanceDialog.Confirmed += ReloadAppearance;
            _appearanceDialog.AddButton("Reapply my choices", false, "reapply");
            _appearanceDialog.CustomAction += action => { if (action == "reapply") ReapplyAppearance(); };
            _appearanceDialog.Canceled += () => _appearanceBeforeJoin = null;
        }

        _appearanceDialog.PopupCentered();
    }
}
