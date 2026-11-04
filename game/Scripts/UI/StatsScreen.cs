using System;
using System.Collections.Generic;
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
    private Label _player = null!;
    private RankBadge _rank = null!;
    private GridContainer _summary = null!;
    private VBoxContainer _rows = null!;
    private readonly Dictionary<string, Button> _filters = new();
    private Button _refreshButton = null!;
    private Task<AccountInfo>? _refresh;
    private string _category = "Overview";
    private string? _error;

    protected override Vector2 ResponsiveReference => new(1600f, 900f);

    public override void _Ready()
    {
        Name = "Stats";
        BuildFrame("Player stats", 1180f, responsive: true);
        PageCanvas.GetNode<ScrollContainer>("BodyScroll").AddChild(new CareerScroll());
        _service = AccountService.Get(GetTree());
        var profile = CareerPanels.Card(Body, "CareerProfile", "Career record");
        var identity = new HBoxContainer();
        identity.AddThemeConstantOverride("separation", 16);
        profile.AddChild(identity);
        _player = CareerPanels.Text(identity, "PlayerName", "Your career", size: 22);
        _rank = new RankBadge(32, 16) { Name = "Rank", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        identity.AddChild(_rank);
        _status = CareerPanels.Text(profile, "Status", string.Empty, dim: true, size: 14);
        _summary = CareerPanels.Grid(Body, "CareerSummary", 210f, 4);
        _summary.Resized += () => _summary.Columns = _summary.Size.X >= 870f ? 4 : 2;
        var filters = new HFlowContainer { Name = "Categories" };
        filters.AddThemeConstantOverride("h_separation", 8);
        filters.AddThemeConstantOverride("v_separation", 8);
        var group = new ButtonGroup();
        foreach (string category in StatsModel.Categories)
        {
            var button = UiStyle.MenuButton(category, () =>
            {
                _category = category;
                PageCanvas.GetNode<ScrollContainer>("BodyScroll").ScrollVertical = 0;
                Rebuild();
            }, "Filter" + category);
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.SetPressedNoSignal(category == _category);
            button.CustomMinimumSize = new Vector2(0, 34);
            button.AddThemeFontSizeOverride("font_size", 14);
            filters.AddChild(button);
            _filters.Add(category, button);
        }
        Body.AddChild(filters);
        _rows = new VBoxContainer { Name = "StatsPanel" };
        _rows.AddThemeConstantOverride("separation", 10);
        Body.AddChild(_rows);
        CareerPanels.Text(Body, "CountingHelp",
            "Career totals count official rounds played against a signed-in opponent. Totals update after the round is reported.\nCategories can overlap. — means unavailable or no denominator.", dim: true, size: 14);
        PageButton("Back", () => FrontEnd.ShowMainMenu(GetTree()), "Back").GrabFocus();
        PageButton("Account", () => FrontEnd.ShowAccount(GetTree()), "Account");
        PageButton("Match history", () => FrontEnd.ShowMatchHistory(GetTree()), "MatchHistory");
        _refreshButton = PageButton("Refresh", Refresh, "Refresh");
        foreach (Button button in PageButtons) button.CustomMinimumSize = new Vector2(144f, 44f);
        _service.Changed += Rebuild;
        Rebuild();
        Refresh();
    }

    public override void _ExitTree()
    {
        _service.Changed -= Rebuild;
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
        RenderStats(account);
    }

    internal void RenderStats(AccountInfo? account)
    {
        _status.Text = _error ?? (_refresh is not null ? "Refreshing career stats…" : account is null
            ? AccountSettings.Current.SignedIn ? "Stats have not loaded. Use Refresh to try again." : "Sign in on the Account page to view your career stats."
            : "Career totals · Counted official rounds");
        CareerPanels.Clear(_summary);
        CareerPanels.Clear(_rows);
        _player.Text = string.IsNullOrWhiteSpace(account?.Name) ? "Your career" : account.Name;
        _rank.Show(RankBadge.OfAccount(account));
        foreach (var entry in _filters) entry.Value.SetPressedNoSignal(entry.Key == _category);
        _summary.Visible = account is not null;
        if (account is null)
        {
            var empty = CareerPanels.Card(_rows, "EmptyStats", "Your career starts here");
            CareerPanels.Text(empty, "EmptyMessage", AccountSettings.Current.SignedIn
                ? "Your career stats are not available yet. Refresh to try again."
                : "Sign in from Account to track your rounds, combat stats and progression.");
            return;
        }
        var all = StatsModel.Build(account);
        foreach (var metric in new[] { ("Rounds played", "Rounds"), ("Wins", "Wins"), ("Win rate", "Win rate"), ("Kill / death ratio", "K / D") })
        {
            var card = CareerPanels.Card(_summary, "Summary" + metric.Item2.Replace(" ", "").Replace("/", ""), metric.Item2);
            var value = CareerPanels.Text(card, "Value", all.Single(r => r.Label == metric.Item1).Value, size: 26);
            value.Modulate = UiStyle.MenuFocus;
        }
        if (account.Career is null)
            CareerPanels.Text(_rows, "LegacyNotice", "This master does not provide detailed counters yet.", dim: true, size: 14);
        if (_category == "Maps")
            CareerPanels.Text(_rows, "MapsNotice", "Base-map wins across variants. Custom-map wins are not tracked separately.", dim: true, size: 14);
        if (account.Stats.Matches == 0)
            CareerPanels.Text(_rows, "NoRounds", "No counted rounds yet. Your career starts with your first eligible round.", size: 14);
        var grid = CareerPanels.Grid(_rows, "CategoryCards", 380f);
        var rows = all.Where(r => r.Category == _category).ToArray();
        if (_category is "Gadgets" or "Weapons" or "Maps")
        {
            foreach (var group in rows.GroupBy(r => r.Label.Split(" · ")[0]))
                AddGroup(grid, group.Key, group.Select(r => r with { Label = r.Label[(r.Label.IndexOf(" · ", StringComparison.Ordinal) + 3)..] }));
        }
        else if (_category == "Overview")
        {
            AddGroup(grid, "Round record", rows.Take(4));
            AddGroup(grid, "Combat performance", rows.Skip(4));
        }
        else if (_category == "Progression")
        {
            AddGroup(grid, "Unlocks", rows.Where(r => r.Label is "Achievements earned" or "Cosmetics unlocked" or "Titles unlocked"));
            var rank = rows.Where(r => r.Label is not ("Achievements earned" or "Cosmetics unlocked" or "Titles unlocked")).ToArray();
            if (rank.Length > 0) AddGroup(grid, "Rank & experience", rank);
        }
        else AddGroup(grid, _category == "Spy" ? "Spy performance" : "Mercenary performance", rows);
    }

    private static void AddGroup(Container parent, string title, IEnumerable<StatsModel.Row> rows)
    {
        var card = CareerPanels.Card(parent, "StatGroup", title);
        foreach (var row in rows) CareerPanels.StatRow(card, row.Label, row.Value);
    }
}
