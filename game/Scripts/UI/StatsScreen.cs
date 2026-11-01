using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

public partial class StatsScreen : MenuPage
{
    private AccountService _service = null!;
    private Label _status = null!;
    private VBoxContainer _rows = null!;
    private Button _refreshButton = null!;
    private Task<AccountInfo>? _refresh;
    private string _category = "Overview";
    private string? _error;

    public override void _Ready()
    {
        Name = "Stats";
        BuildFrame("Player stats", 900f);
        _service = AccountService.Get(GetTree());
        _status = new Label { Name = "Status", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiStyle.MenuFocus };
        Body.AddChild(_status);
        Body.AddChild(new Label
        {
            Text = "Career totals from counted official rounds. Sign in and play with a signed-in opponent on the other side.\nTotals update after the round is reported. Categories can overlap; — means unavailable or no denominator.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiStyle.Dim,
        });
        var filters = new HFlowContainer { Name = "Categories" };
        var group = new ButtonGroup();
        foreach (string category in StatsModel.Categories)
        {
            var button = UiStyle.MenuButton(category, () => { _category = category; Rebuild(); }, "Filter" + category);
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.SetPressedNoSignal(category == _category);
            button.CustomMinimumSize = new Vector2(120, 40);
            filters.AddChild(button);
        }
        Body.AddChild(filters);
        _rows = Panel(Body, "StatsPanel", null);
        PageButton("Back", () => FrontEnd.ShowMainMenu(GetTree()), "Back").GrabFocus();
        PageButton("Account", () => FrontEnd.ShowAccount(GetTree()), "Account");
        _refreshButton = PageButton("Refresh", Refresh, "Refresh");
        _service.Changed += Rebuild;
        GetViewport().SizeChanged += FitWidth;
        FitWidth();
        Rebuild();
        Refresh();
    }

    private void FitWidth() => PreferredBodyWidth = Math.Min(AvailableBodyWidth, 900f);

    public override void _ExitTree()
    {
        _service.Changed -= Rebuild;
        GetViewport().SizeChanged -= FitWidth;
    }

    private void Refresh()
    {
        if (_refresh is not null || !AccountSettings.Current.SignedIn || !AccountService.HasMaster) return;
        _error = null;
        _refresh = AccountService.Call(c => c.AccountAsync());
        Rebuild();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_refresh is not { IsCompleted: true } task) return;
        _refresh = null;
        if (task.IsCompletedSuccessfully && AccountSettings.Current.SignedIn && task.Result.Id == AccountSettings.Current.AccountId)
            _service.Update(task.Result);
        else if (AccountService.IsSignedOutError(task))
            _service.SignOutLocally();
        else if (!task.IsCompletedSuccessfully)
            _error = "Could not refresh stats: " + AccountService.ErrorOf(task);
        Rebuild();
    }

    private void Rebuild()
    {
        bool signedIn = AccountSettings.Current.SignedIn;
        AccountInfo? account = signedIn ? _service.Account : null;
        _refreshButton.Disabled = !signedIn || !AccountService.HasMaster || _refresh is not null;
        _status.Text = _error ?? (_refresh is not null ? "Refreshing career stats…" : account is null
            ? signedIn ? "Stats have not loaded. Use Refresh to try again." : "Sign in on the Account page to view your career stats."
            : $"{account.Name ?? "Player"} · {StatsModel.Number(account.Stats.Matches)} rounds recorded");
        RenderStats(account);
    }

    internal void RenderStats(AccountInfo? account)
    {
        foreach (Node child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        if (account is null) return;
        if (account.Career is null)
            _rows.AddChild(new Label { Text = "This master does not provide detailed counters yet.", Modulate = UiStyle.Dim });
        if (_category == "Maps")
            _rows.AddChild(new Label { Text = "Base-map wins across variants. Custom-map wins are not tracked separately.", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiStyle.Dim });
        if (account.Stats.Matches == 0)
            _rows.AddChild(new Label { Text = "No counted rounds yet. Your career starts with your first eligible round.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        foreach (StatsModel.Row stat in StatsModel.Build(account).Where(r => r.Category == _category))
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = stat.Label, SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart });
            row.AddChild(new Label { Text = stat.Value, HorizontalAlignment = HorizontalAlignment.Right, Modulate = UiStyle.MenuFocus, CustomMinimumSize = new Vector2(100, 0) });
            _rows.AddChild(row);
        }
    }
}
