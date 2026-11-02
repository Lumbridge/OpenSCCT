using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

public partial class MatchHistoryScreen : MenuPage
{
    internal object? Preview { get; init; }

    private VBoxContainer _content = null!;
    private Label _status = null!;
    private Button _previous = null!, _next = null!, _refresh = null!;
    private Task<object>? _request;
    private (string Master, string Invite, string Token) _requestedIdentity;
    private (string Master, string Invite, string Token) _displayIdentity;
    private int _page;
    private long? _match;
    private bool _more;
    private static (string, string, string) Identity =>
        (OnlineSettings.Current.MasterUrl, OnlineSettings.Current.InviteKey, AccountSettings.Current.Token);

    protected override Vector2 ResponsiveReference => new(1600f, 900f);

    public override void _Ready()
    {
        Name = "MatchHistory";
        BuildFrame("Match history", 1180f, responsive: true);
        PageCanvas.GetNode<ScrollContainer>("BodyScroll").AddChild(new CareerScroll());
        var header = CareerPanels.Card(Body, "HistoryHeader", "Match archive");
        _status = CareerPanels.Text(header, "Status", "Loading match history…", dim: true, size: 14);
        var links = new HFlowContainer { Name = "CareerLinks" };
        links.AddThemeConstantOverride("h_separation", 8);
        links.AddThemeConstantOverride("v_separation", 8);
        header.AddChild(links);
        CompactButton(links, "Player stats", () => FrontEnd.ShowStats(GetTree()), "StatsPage");
        CompactButton(links, "Account", () => FrontEnd.ShowAccount(GetTree()), "Account");
        _content = new VBoxContainer { Name = "HistoryContent" };
        _content.AddThemeConstantOverride("separation", 10);
        Body.AddChild(_content);
        PageButton("Back", Back, "Back").GrabFocus();
        _previous = PageButton("Previous", () => { _page--; Load(null); }, "Previous");
        _next = PageButton("Next", () => { _page++; Load(null); }, "Next");
        _refresh = PageButton("Refresh", () => Load(_match), "Refresh");
        foreach (Button button in PageButtons) button.CustomMinimumSize = new Vector2(144f, 44f);
        _displayIdentity = Identity;
        Load(null);
    }

    private void Back()
    {
        if (_match is not null) Load(null);
        else FrontEnd.ShowMainMenu(GetTree());
    }

    private void Clear() => CareerPanels.Clear(_content);

    private void Load(long? match)
    {
        if (_request is not null) return;
        _match = match;
        TitleText = match is null ? "Match history" : "Match details";
        Clear();
        PageCanvas.GetNode<ScrollContainer>("BodyScroll").ScrollVertical = 0;
        _previous.Disabled = _next.Disabled = true;
        _previous.Visible = _next.Visible = match is null;
        _refresh.Disabled = true;
        PageButtons[0].GrabFocus();
        if (AccountSettings.Isolated && Preview is { } preview)
        {
            _requestedIdentity = Identity;
            _request = Task.FromResult(preview);
            return;
        }
        if (!AccountSettings.Current.SignedIn)
        {
            _status.Text = "Sign in on the Account page to view your match history.";
            ShowMessage("Your match archive", "Sign in from Account to revisit your recorded matches, scoreboards and timelines.");
            _refresh.Disabled = false;
            return;
        }
        _requestedIdentity = Identity;
        _status.Text = "Loading match history…";
        ShowMessage(match is null ? "Loading matches" : "Loading match details", "Retrieving your recorded matches…");
        _request = match is { } id
            ? AccountService.Call<object>(async c => await c.MatchDetailsAsync(id))
            : AccountService.Call<object>(async c => await c.MatchHistoryAsync(_page));
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_displayIdentity != Identity)
        {
            _displayIdentity = Identity;
            _page = 0;
            Clear();
            _status.Text = "Account or master changed. Loading history…";
            ShowMessage("Refreshing match archive", "Loading records for the current account.");
            if (_request is null) Load(null);
        }
        if (_request is not { IsCompleted: true } task) return;
        _request = null;
        _refresh.Disabled = false;
        if (_requestedIdentity != Identity)
        {
            _ = task.Exception;
            Load(null);
            return;
        }
        Clear();
        if (!task.IsCompletedSuccessfully)
        {
            _status.Text = "History is currently unavailable.";
            ShowMessage("Could not load history", AccountService.ErrorOf(task) + "\nUse Refresh to try again.");
            return;
        }
        if (task.Result is MatchHistoryPage page)
        {
            _more = page.HasMore;
            _previous.Disabled = _page == 0;
            _next.Disabled = !_more;
            _status.Text = page.Matches.Count == 0 ? "No recorded matches yet." : $"Your recorded matches · Page {_page + 1} · Newest first";
            if (page.Matches.Count == 0)
            {
                ShowMessage("No matches to show", _page == 0
                    ? "Your recorded matches will appear here after they are reported."
                    : "There are no matches on this page. Use Previous to return to newer matches.");
                return;
            }
            var matches = CareerPanels.Grid(_content, "Matches", 440f);
            foreach (var entry in page.Matches)
            {
                var card = CareerPanels.Card(matches, "MatchCard" + entry.Id, Winner(entry.Winner));
                CareerPanels.Text(card, "Map", entry.Map, size: 20);
                CareerPanels.Text(card, "When", $"{entry.Ended.ToLocalTime():dd MMM yyyy HH:mm} · {Duration(entry.DurationSeconds)}", dim: true, size: 14);
                CareerPanels.Text(card, "Server", entry.Server, dim: true, size: 14);
                CompactButton(card, "View match", () => Load(entry.Id), "Match" + entry.Id);
            }
        }
        else if (task.Result is MatchHistoryDetails details) ShowDetails(details);
    }

    private void ShowDetails(MatchHistoryDetails details)
    {
        var m = details.Match;
        TitleText = "Match details";
        _previous.Visible = _next.Visible = false;
        _status.Text = $"{m.Ended.ToLocalTime():dd MMM yyyy HH:mm} · {m.Server}";
        var summary = CareerPanels.Grid(_content, "MatchSummary", 380f);
        var result = CareerPanels.Card(summary, "MatchResult", Winner(m.Winner));
        CareerPanels.Text(result, "Map", m.Map, size: 22);
        CareerPanels.Text(result, "Mode", OpenScct.Server.Online.LobbySetup.ModeName((OpenScct.Core.EGameMode)m.Mode), size: 16);
        CareerPanels.Text(result, "Reason", OpenScct.Server.Online.MatchResults.ReasonText(m.Reason), dim: true, size: 14);
        var round = CareerPanels.Card(summary, "RoundRecord", "Round record");
        CareerPanels.StatRow(round, "Duration", Duration(m.DurationSeconds));
        CareerPanels.StatRow(round, "Objectives", $"{m.ObjectivesTaken} / {m.ObjectivesNeeded}");
        CareerPanels.StatRow(round, "Career stats", m.Counted ? "Counted" : "Uncounted");
        var scoreboard = CareerPanels.Card(_content, "Scoreboard", "Players");
        var scores = new GridContainer { Name = "Scores", Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scores.AddThemeConstantOverride("h_separation", 18);
        scores.AddThemeConstantOverride("v_separation", 8);
        scoreboard.AddChild(scores);
        CareerPanels.Text(scores, "PlayerHeading", "Player", dim: true, size: 14);
        foreach (string heading in new[] { "Score", "Kills", "Deaths" }) ScoreCell(scores, heading, dim: true);
        foreach (var player in details.Players.OrderBy(p => p.Team).ThenByDescending(p => p.Score))
        {
            CareerPanels.Text(scores, "Player", $"{(player.Team == 0 ? "Spy" : "Merc")} · {player.Name}{(player.Bot ? " [Bot]" : "")}", size: 16);
            ScoreCell(scores, StatsModel.Number(player.Score));
            ScoreCell(scores, StatsModel.Number(player.Kills));
            ScoreCell(scores, StatsModel.Number(player.Deaths));
        }
        if (details.Players.Count == 0) CareerPanels.Text(scoreboard, "NoPlayers", "No player scores were recorded.", dim: true, size: 14);
        var timeline = CareerPanels.Card(_content, "Timeline", "Match timeline");
        if (details.Events is null) { CareerPanels.Text(timeline, "Unavailable", "Timeline unavailable for this match.", dim: true, size: 14); return; }
        if (details.TimelineTruncated) CareerPanels.Text(timeline, "Truncated", "Some events were omitted because this match reached the recording limit.", dim: true, size: 14);
        if (details.Events.Count == 0) { CareerPanels.Text(timeline, "NoEvents", "No events recorded.", dim: true, size: 14); return; }
        int shown = 0;
        Button more = UiStyle.MenuButton("Show more events", () => { }, "MoreEvents");
        void Append()
        {
            int end = Math.Min(shown + 100, details.Events.Count);
            while (shown < end)
            {
                var e = details.Events[shown++];
                var row = new HBoxContainer { Name = "Event" + shown };
                row.AddThemeConstantOverride("separation", 16);
                timeline.AddChild(row);
                var time = ScoreCell(row, Duration(e.Seconds), dim: true);
                time.CustomMinimumSize = new Vector2(64, 0);
                time.HorizontalAlignment = HorizontalAlignment.Left;
                CareerPanels.Text(row, "EventText", e.Text, size: 14);
            }
            more.Visible = shown < details.Events.Count;
            timeline.MoveChild(more, timeline.GetChildCount() - 1);
        }
        timeline.AddChild(more);
        more.CustomMinimumSize = new Vector2(0, 32);
        more.AddThemeFontSizeOverride("font_size", 14);
        more.Pressed += Append;
        Append();
    }

    private static string Winner(int team) => team == 0 ? "Spies win" : team == 1 ? "Mercenaries win" : "No winner";
    private static string Duration(int seconds) => $"{Math.Max(0, seconds) / 60:00}:{Math.Max(0, seconds) % 60:00}";
    private void ShowMessage(string title, string message)
    {
        var card = CareerPanels.Card(_content, "HistoryState", title);
        CareerPanels.Text(card, "Message", message, dim: true, size: 16);
    }

    private static void CompactButton(Container parent, string caption, Action action, string name)
    {
        var button = UiStyle.MenuButton(caption, action, name);
        button.CustomMinimumSize = new Vector2(0, 32);
        button.AddThemeFontSizeOverride("font_size", 14);
        parent.AddChild(button);
    }

    private static Label ScoreCell(Container parent, string text, bool dim = false)
    {
        var label = new Label
        {
            Text = text, HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsVertical = SizeFlags.ShrinkCenter, Modulate = dim ? UiStyle.Dim : UiStyle.MenuFocus,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", dim ? 14 : 16);
        if (!UiStyle.IsOriginal && UiArt.CaptionFont is { } font) label.AddThemeFontOverride("font", font);
        parent.AddChild(label);
        return label;
    }
}
