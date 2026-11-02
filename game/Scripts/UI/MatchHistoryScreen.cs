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

    public override void _Ready()
    {
        BuildFrame("Match history", 1000f, responsive: true);
        _status = Line(Body, "Loading match history…");
        _content = new VBoxContainer();
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

    private void Clear()
    {
        foreach (Node child in _content.GetChildren()) { _content.RemoveChild(child); child.QueueFree(); }
    }

    private void Load(long? match)
    {
        if (_request is not null) return;
        _match = match;
        Clear();
        _previous.Disabled = _next.Disabled = true;
        _refresh.Disabled = true;
        if (AccountSettings.Isolated && Preview is { } preview)
        {
            _requestedIdentity = Identity;
            _request = Task.FromResult(preview);
            return;
        }
        if (!AccountSettings.Current.SignedIn)
        {
            _status.Text = "Sign in on the Account page to view your match history.";
            _refresh.Disabled = false;
            return;
        }
        _requestedIdentity = Identity;
        _status.Text = "Loading match history…";
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
        if (!task.IsCompletedSuccessfully)
        {
            _status.Text = "History unavailable: " + AccountService.ErrorOf(task);
            return;
        }
        if (task.Result is MatchHistoryPage page)
        {
            _more = page.HasMore;
            _previous.Disabled = _page == 0;
            _next.Disabled = !_more;
            _status.Text = page.Matches.Count == 0 ? "No recorded matches yet." : $"Your recorded matches · Page {_page + 1} · Newest first";
            foreach (var entry in page.Matches)
            {
                var button = UiStyle.MenuButton($"{entry.Ended.ToLocalTime():dd MMM yyyy HH:mm} · {entry.Map} · {Winner(entry.Winner)} · {Duration(entry.DurationSeconds)}",
                    () => Load(entry.Id), "Match" + entry.Id);
                button.ClipText = true;
                button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                button.CustomMinimumSize = new Vector2(0, 44);
                button.TooltipText = entry.Server;
                _content.AddChild(button);
            }
        }
        else if (task.Result is MatchHistoryDetails details) ShowDetails(details);
    }

    private void ShowDetails(MatchHistoryDetails details)
    {
        var m = details.Match;
        _status.Text = $"{m.Map} · {m.Ended.ToLocalTime():dd MMM yyyy HH:mm} · {Winner(m.Winner)}";
        Line(_content, $"{m.Server} · {OpenScct.Server.Online.LobbySetup.ModeName((OpenScct.Core.EGameMode)m.Mode)} · {Duration(m.DurationSeconds)} · Objectives {m.ObjectivesTaken}/{m.ObjectivesNeeded} · {(m.Counted ? "Counted" : "Uncounted")}");
        Line(_content, OpenScct.Server.Online.MatchResults.ReasonText(m.Reason));
        var scoreboard = Panel(_content, "Scoreboard", "Players · Score / Kills / Deaths");
        foreach (var player in details.Players.OrderBy(p => p.Team).ThenByDescending(p => p.Score))
            Line(scoreboard, $"{(player.Team == 0 ? "Spy" : "Merc")} · {player.Name}{(player.Bot ? " [Bot]" : "")} · {player.Score} / {player.Kills} / {player.Deaths}");
        var timeline = Panel(_content, "Timeline", "Match timeline");
        if (details.Events is null) { Line(timeline, "Timeline unavailable for this match."); return; }
        if (details.TimelineTruncated) Line(timeline, "Some events were omitted because this match reached the recording limit.");
        if (details.Events.Count == 0) { Line(timeline, "No events recorded."); return; }
        int shown = 0;
        Button more = UiStyle.MenuButton("Show more events", () => { }, "MoreEvents");
        void Append()
        {
            int end = Math.Min(shown + 100, details.Events.Count);
            while (shown < end)
            {
                var e = details.Events[shown++];
                Line(timeline, $"{Duration(e.Seconds)}  ·  {e.Text}");
            }
            more.Visible = shown < details.Events.Count;
            timeline.MoveChild(more, timeline.GetChildCount() - 1);
        }
        timeline.AddChild(more);
        more.Pressed += Append;
        Append();
    }

    private static string Winner(int team) => team == 0 ? "Spies win" : team == 1 ? "Mercenaries win" : "No winner";
    private static string Duration(int seconds) => $"{Math.Max(0, seconds) / 60:00}:{Math.Max(0, seconds) % 60:00}";
    private static Label Line(Container parent, string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        parent.AddChild(label);
        return label;
    }
}
