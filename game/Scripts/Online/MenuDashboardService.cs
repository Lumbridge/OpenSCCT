using System;
using Godot;
using OpenScct.Net.Discovery;
using OpenScctGame.UI;

namespace OpenScctGame.Online;

/// <summary>
/// Main-thread dashboard snapshots, retained across front-end pages. Requests run in the background and only
/// start while a dashboard/detail page holds a watch. Changing master discards every old response.
/// </summary>
public partial class MenuDashboardService : Node
{
    private const string NodeName = "MenuDashboardService";
    private readonly MenuDashboardRequest<MasterStatus> _status = new(60_000);
    private readonly MenuDashboardRequest<ServerListResponse> _games = new(30_000);
    private readonly MenuDashboardRequest<ServerListResponse> _hubs = new(30_000);
    private readonly MenuDashboardRequest<ChangelogResponse> _changelog = new(600_000);
    private (string Master, string Invite) _identity;
    private int _watchers;
    private AccountService? _accounts;
    private MenuDashboardSnapshot? _preview;

    public event Action? Changed;
    public MenuDashboardFeed<MasterStatus> Status => _preview?.Status ?? _status.View;
    public MenuDashboardFeed<ServerListResponse> Games => _preview?.Games ?? _games.View;
    public MenuDashboardFeed<ServerListResponse> Hubs => _preview?.Hubs ?? _hubs.View;
    public MenuDashboardFeed<ChangelogResponse> Changelog => _preview?.Changelog ?? _changelog.View;
    public string Announcement => Status.Value?.Announcement ?? string.Empty;
    public AccountInfo? Account => _preview is { } preview ? preview.Account : _accounts?.Account;
    public bool AccountStale => _preview is { } preview ? preview.AccountStale : Account is not null && _accounts?.RefreshFailed == true;
    public bool SignedIn => _preview is { } preview ? preview.Account is not null : AccountSettings.Current.SignedIn;

    public static MenuDashboardService Get(SceneTree tree)
    {
        if (tree.Root.GetNodeOrNull<MenuDashboardService>(NodeName) is { } existing) return existing;
        var service = new MenuDashboardService { Name = NodeName };
        tree.Root.AddChild(service);
        return service;
    }

    public override void _Ready()
    {
        _accounts = AccountService.Get(GetTree());
        _accounts.Changed += OnAccountChanged;
        _identity = Identity();
        _Process(0);
    }

    public override void _ExitTree()
    {
        if (_accounts is not null) _accounts.Changed -= OnAccountChanged;
    }

    /// <summary>Keep polling while this page is visible. Dispose the lease when leaving the page.</summary>
    public IDisposable Watch()
    {
        _watchers++;
        _Process(0);
        return new WatchLease(this);
    }

    /// <summary>Smoke-only data injection; never changes account settings or sends requests.</summary>
    public void SetPreview(MenuDashboardSnapshot? snapshot)
    {
        if (!AccountSettings.Isolated) throw new InvalidOperationException("Dashboard previews require isolated settings.");
        _preview = snapshot;
        Changed?.Invoke();
    }

    private void OnAccountChanged() => Changed?.Invoke();
    private static (string Master, string Invite) Identity() =>
        (OnlineSettings.Current.MasterUrl.TrimEnd('/'), OnlineSettings.Current.InviteKey);

    public override void _Process(double delta)
    {
        if (_preview is not null) return;
        bool changed = false;
        if (_identity != Identity())
        {
            _identity = Identity();
            _status.Reset();
            _games.Reset();
            _hubs.Reset();
            _changelog.Reset();
            changed = true;
        }

        if (AccountSettings.Isolated || _identity.Master.Length == 0)
        {
            changed |= _status.Disable();
            changed |= _games.Disable();
            changed |= _hubs.Disable();
            changed |= _changelog.Disable();
        }
        else
        {
            ulong now = Time.GetTicksMsec();
            bool active = _watchers > 0;
            changed |= _status.Pump(now, active, () => AccountService.Call(c => c.StatusAsync(), withToken: false));
            changed |= _games.Pump(now, active, () => AccountService.Call(c => c.ListAsync(kind: ServerKind.Game), withToken: false));
            changed |= _hubs.Pump(now, active, () => AccountService.Call(c => c.ListAsync(kind: ServerKind.Hub), withToken: false));
            changed |= _changelog.Pump(now, active, () => AccountService.Call(c => c.ChangelogAsync(), withToken: false),
                r => r.Unavailable, MenuDashboardModel.MergeChangelog);
            if (active) _accounts?.RefreshAccount();
        }
        if (changed) Changed?.Invoke();
    }

    private sealed class WatchLease(MenuDashboardService owner) : IDisposable
    {
        private MenuDashboardService? _owner = owner;
        public void Dispose()
        {
            if (_owner is { } service) service._watchers = Math.Max(0, service._watchers - 1);
            _owner = null;
        }
    }

}
