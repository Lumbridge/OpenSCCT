using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net.Discovery;
using OpenScct.Net.Scoreboards;
using OpenScctGame.Online;
using OpenScctGame.Play;
using OpenScctGame.Play.Demo;

namespace OpenScctGame.UI;

public partial class DemosScreen
{
    internal IReadOnlyList<ArchivedScoreboard>? PreviewHistory { get; init; }
    public bool StartWithHistory { get; init; }
    private Button _historyTab = null!;
    private bool _historySelected;
    private int _historyPage, _cloudPage = -1;
    private bool _cloudMore;
    private bool _advanceHistoryPage;
    private readonly List<ArchivedScoreboard> _history = new();
    private readonly Dictionary<long, MatchHistoryEntry> _cloudHistory = new();
    private Task<MatchHistoryPage>? _historyRequest;
    private Task<MatchHistoryDetails>? _detailsRequest, _reconcileRequest;
    private RoundScoreboardDialog? _historyDialog;
    private ArchivedScoreboard? _historyDialogEntry;
    private string? _historyError;
    private readonly Queue<string> _reconcile = new();
    private (string Master, long Account, string Token) _historyIdentity;
    private (string Master, long Account, string Token) _requestIdentity, _detailIdentity, _reconcileIdentity;
    private static (string Master, long Account, string Token) HistoryIdentity =>
        (OnlineSettings.Current.MasterUrl, AccountSettings.Current.SignedIn ? AccountSettings.Current.AccountId : 0, AccountSettings.Current.Token);

    private sealed record HistoryItem(ArchivedScoreboard Record, long? CloudId);

    private void ReadHistory()
    {
        _historyIdentity = HistoryIdentity;
        _history.Clear();
        if (AccountSettings.Isolated && PreviewHistory is { } preview) { _history.AddRange(preview); return; }
        _history.AddRange(ScoreboardHistory.Read());
        // A copied v3 recording carries a trustworthy round ID and final board without requiring playback.
        foreach (var (demo, _) in _local)
        {
            if (demo.Index.Scoreboard is not { } board) continue;
            try
            {
                var entry = new ArchivedScoreboard { Scoreboard = board, ImportedReplay = true, ReplayPath = demo.Path };
                var existing = _history.FirstOrDefault(e => e.Scoreboard.RoundId == board.RoundId);
                if (existing is not null) entry = ScoreboardArchive.Merge(existing, entry);
                SaveHistoryEntry(entry);
            }
            catch (Exception error) when (error is System.IO.InvalidDataException or ArgumentException)
            { GD.PushWarning("Could not import replay scoreboard: " + error.Message); }
        }
        _reconcile.Clear();
        if (_historyIdentity.Account > 0)
            foreach (var entry in _history.Where(e => e.Scoreboard.Partial && !e.Scoreboard.Practice &&
                ScoreboardArchive.CanQuery(e, _historyIdentity.Master, _historyIdentity.Account) &&
                Guid.TryParse(e.Scoreboard.RoundId, out _)))
                _reconcile.Enqueue(entry.Scoreboard.RoundId);
    }

    private void SaveHistoryEntry(ArchivedScoreboard entry)
    {
        entry = ScoreboardHistory.Save(entry);
        string key = ScoreboardArchive.Identity(entry);
        int index = _history.FindIndex(e => ScoreboardArchive.Identity(e) == key);
        if (index < 0) _history.Add(entry); else _history[index] = entry;
    }

    private void SelectHistory()
    {
        _historySelected = true;
        _advanceHistoryPage = false;
        _historyPage = 0;
        ReadHistory();
        PageCanvas.GetNode<ScrollContainer>("BodyScroll").ScrollVertical = 0;
        if (_cloudPage < 0) LoadHistoryPage(0);
        Render();
    }

    private void LoadHistoryPage(int page)
    {
        if (_historyRequest is not null || Previewing || AccountSettings.Isolated || !AccountSettings.Current.SignedIn || !AccountService.HasMaster) return;
        if (page == 0) { _cloudHistory.Clear(); _cloudPage = -1; _advanceHistoryPage = false; }
        _requestIdentity = HistoryIdentity;
        _historyError = null;
        _cloudPage = page;
        _historyRequest = AccountService.Call(c => c.MatchHistoryAsync(page, _lifetime.Token));
    }

    private IEnumerable<HistoryItem> HistoryItems()
    {
        var items = new Dictionary<string, HistoryItem>();
        foreach (var entry in _history) items[ScoreboardArchive.Identity(entry)] = new(entry,
            ScoreboardArchive.CanQuery(entry, _historyIdentity.Master, _historyIdentity.Account) ? entry.Scoreboard.MatchId : null);
        foreach (var entry in _cloudHistory.Values)
        {
            var record = new ArchivedScoreboard
            {
                Master = _historyIdentity.Master, AccountId = _historyIdentity.Account,
                Scoreboard = new RoundScoreboard
                {
                    RoundId = entry.RoundId ?? "", MatchId = entry.Id, Master = _historyIdentity.Master,
                    Map = entry.Map, Server = entry.Server, Mode = entry.Mode, StartedUtc = entry.Ended.AddSeconds(-entry.DurationSeconds),
                    EndedUtc = entry.Ended, ElapsedSeconds = entry.DurationSeconds, Winner = entry.Winner, Reason = entry.Reason,
                    ObjectivesTaken = entry.ObjectivesTaken, ObjectivesNeeded = entry.ObjectivesNeeded, Completed = true,
                },
            };
            string key = ScoreboardArchive.Identity(record);
            items[key] = items.TryGetValue(key, out var found) ? found with { CloudId = entry.Id } : new(record, entry.Id);
        }
        var filtered = items.Values.Where(e => Matches(ModeName(e.Record.Scoreboard.Mode), e.Record.Scoreboard.Map,
            MapTitle(e.Record.Scoreboard.Map), e.Record.Scoreboard.Server, string.Join(" ", e.Record.Scoreboard.Rows.Select(r => r.Name))));
        return _order.Selected == 1 ? filtered.OrderBy(e => e.Record.Scoreboard.StartedUtc) : filtered.OrderByDescending(e => e.Record.Scoreboard.StartedUtc);
    }

    private void RenderHistory()
    {
        _localTab.SetPressedNoSignal(false);
        _recentTab.SetPressedNoSignal(false);
        _storage.Visible = false;
        _refreshButton.Disabled = _historyRequest is not null;
        _refreshButton.Text = _historyRequest is not null ? "Refreshing…" : "Refresh";
        _collectionHelp.Text = "Scoreboards stay permanently, even without a demo. Local games and your account history are combined.";
        var entries = HistoryItems().ToArray();
        const int pageSize = 20;
        int pages = Math.Max(1, (entries.Length + pageSize - 1) / pageSize);
        _historyPage = Math.Clamp(_historyPage, 0, pages - 1);
        _pager.Visible = entries.Length > pageSize || _cloudMore;
        _previous.Disabled = _historyPage == 0;
        _next.Disabled = _historyRequest is not null || _historyPage + 1 >= pages && !_cloudMore;
        _pageNumber.Text = $"Page {_historyPage + 1} of {pages}" + (_cloudMore ? " · more online" : "");
        _count.Text = $"{entries.Length} scoreboards" + (_historyRequest is not null ? " · Loading account history…" : _historyIdentity.Account == 0 ? " · Sign in to add account history" : "");
        foreach (var item in entries.Skip(_historyPage * pageSize).Take(pageSize))
        {
            var entry = item.Record;
            var board = entry.Scoreboard;
            var card = Card(_cards, "HistoryMatch");
            Text(card, "Map", MapTitle(board.Map), 22);
            string outcome = board.Partial ? "Incomplete" : board.Winner == 0 ? "Spies win" : board.Winner == 1 ? "Mercenaries win" : "Round stopped";
            Text(card, "Result", outcome + (board.Practice ? " · Practice" : "") + (entry.LeftEarly ? " · Left early" : ""), 15);
            Text(card, "Metadata", $"{board.StartedUtc.ToLocalTime():dd MMM yyyy · HH:mm} · {ModeName(board.Mode)} · {Duration(TimeSpan.FromSeconds(Math.Max(0, board.ElapsedSeconds)))}", 14, dim: true);
            Text(card, "Server", string.IsNullOrWhiteSpace(board.Server) ? "Offline game" : board.Server, 14, dim: true);
            var actions = Actions(card);
            var open = ActionButton(actions, "View scoreboard", () => OpenHistory(item), "ViewScoreboard");
            open.Disabled = _detailsRequest is not null;
            var demo = FindHistoryDemo(board.RoundId);
            if (demo is not null)
            {
                bool playable = demo.Header.ProtocolHash == _protocol;
                var watch = ActionButton(actions, "Watch", () => DemoLibrary.Watch(GetTree(), demo.Path), "Watch");
                watch.Disabled = !playable;
                Text(card, "Replay", playable ? "Replay saved on this computer" : "Replay requires its original game protocol", 13, dim: true);
            }
            else Text(card, "Replay", item.CloudId is null ? "No replay available on this computer" : "Online replay availability shown in match details", 13, dim: true);
        }
        if (_historyError is { } error) ShowError(error);
        if (ScoreboardHistory.LastError is { } archiveError) ShowError(archiveError);
        if (entries.Length == 0) Empty("Your scoreboards will appear here", "Play a round online or offline. Recording is optional; practice and unfinished games are labelled.");
    }

    private Play.Demo.DemoEntry? FindHistoryDemo(string roundId) => string.IsNullOrEmpty(roundId) ? null :
        _local.Select(e => e.Entry).FirstOrDefault(e => e.Header.RoundId == roundId || e.Index.Scoreboard?.RoundId == roundId);

    private void OpenHistory(HistoryItem item)
    {
        // Input can arrive between an account change and this page's next process callback.
        if (_historyIdentity != HistoryIdentity) { ProcessHistory(); return; }
        if (!ScoreboardArchive.VisibleTo(item.Record, _historyIdentity.Master, _historyIdentity.Account)) return;
        if (_detailsRequest is not null) return;
        if (item.CloudId is { } id && AccountSettings.Current.SignedIn && AccountService.HasMaster)
        {
            _detailIdentity = HistoryIdentity;
            _detailsRequest = AccountService.Call(c => c.MatchDetailsAsync(id, _lifetime.Token));
            _detailFallback = item.Record;
            ShowStatus("Loading match details…");
            Render();
        }
        else OpenHistoryRecord(item.Record);
    }

    private ArchivedScoreboard? _detailFallback;

    private void OpenHistoryRecord(ArchivedScoreboard entry, MatchHistoryDetails? details = null)
    {
        var identity = HistoryIdentity;
        if (!ScoreboardArchive.VisibleTo(entry, identity.Master, identity.Account)) return;
        var local = FindHistoryDemo(entry.Scoreboard.RoundId);
        Action? watch = null;
        string replay = "No replay available";
        if (local is not null)
        {
            replay = local.Header.ProtocolHash == _protocol ? "Replay saved on this computer" : "Replay requires its original game protocol";
            if (local.Header.ProtocolHash == _protocol) watch = () => DemoLibrary.Watch(GetTree(), local.Path);
        }
        else if (details?.Demo is { } demo && demo.Expires > DateTimeOffset.UtcNow)
        {
            bool playable = string.Equals(demo.NetVersion, MasterApi.Hex(_protocol), StringComparison.OrdinalIgnoreCase);
            replay = playable ? "Replay available online until " + demo.Expires.ToLocalTime().ToString("dd MMM HH:mm") : "Replay requires its original game protocol";
            if (playable) watch = () =>
            {
                var current = HistoryIdentity;
                if (current.Account == identity.Account &&
                    ScoreboardArchive.NormaliseMaster(current.Master) == ScoreboardArchive.NormaliseMaster(identity.Master))
                    StartDownload(demo, keep: false);
            };
        }
        _historyDialogEntry = entry;
        _historyDialog = RoundScoreboardDialog.Open(this, entry.Scoreboard, entry.LocalParticipantId, watch, replay, details?.Events);
    }

    private ArchivedScoreboard SaveDetails(MatchHistoryDetails details)
    {
        var board = details.Scoreboard ?? LegacyHistoryBoard(details);
        var entry = new ArchivedScoreboard
        {
            Scoreboard = board with { Master = _historyIdentity.Master, MatchId = details.Match.Id, DemoId = details.Demo?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            Master = _historyIdentity.Master, AccountId = _historyIdentity.Account,
        };
        SaveHistoryEntry(entry);
        return _history.First(e => ScoreboardArchive.Identity(e) == ScoreboardArchive.Identity(entry));
    }

    private static RoundScoreboard LegacyHistoryBoard(MatchHistoryDetails details) => new()
    {
        RoundId = details.Match.RoundId ?? "", MatchId = details.Match.Id, Map = details.Match.Map, Server = details.Match.Server,
        Mode = details.Match.Mode, StartedUtc = details.Match.Ended.AddSeconds(-details.Match.DurationSeconds), EndedUtc = details.Match.Ended,
        Completed = true, ElapsedSeconds = details.Match.DurationSeconds, AsOfSeconds = details.Match.DurationSeconds,
        Winner = details.Match.Winner, Reason = details.Match.Reason, ObjectivesTaken = details.Match.ObjectivesTaken, ObjectivesNeeded = details.Match.ObjectivesNeeded,
        Rows = details.Players.Select((r, i) => new ScoreboardPlayer { ParticipantId = i, Name = r.Name ?? "Player", Team = r.Team, Score = r.Score,
            Kills = r.Kills, Deaths = r.Deaths, Bot = r.Bot, StatsVersion = 0, Stats = r.Feats,
            Lives = -2, PingMs = -1, Objectives = r.Feats?.GetValueOrDefault("spy.objectives", -1) ?? -1 }).ToArray(),
    };

    private void ProcessHistory()
    {
        if (_historyIdentity != HistoryIdentity)
        {
            var identity = HistoryIdentity;
            if (_historyDialogEntry is { } shown && !ScoreboardArchive.VisibleTo(shown, identity.Master, identity.Account))
            {
                if (GodotObject.IsInstanceValid(_historyDialog)) { _historyDialog!.Hide(); _historyDialog.QueueFree(); }
                _historyDialog = null;
                _historyDialogEntry = null;
            }
            _historyError = null; _detailFallback = null; _advanceHistoryPage = false;
            _historyPage = 0; _cloudPage = -1; _cloudMore = false; _cloudHistory.Clear(); _reconcile.Clear();
            ReadHistory();
            if (_historySelected) { LoadHistoryPage(0); Render(); }
        }
        if (_historyRequest is { IsCompleted: true } request)
        {
            _historyRequest = null;
            if (_requestIdentity == HistoryIdentity)
            {
                if (request.IsCompletedSuccessfully)
                {
                    foreach (var entry in request.Result.Matches) _cloudHistory[entry.Id] = entry;
                    _cloudMore = request.Result.HasMore;
                    if (_advanceHistoryPage)
                    {
                        if ((_historyPage + 1) * 20 < HistoryItems().Count()) { _historyPage++; _advanceHistoryPage = false; }
                        else if (_cloudMore) LoadHistoryPage(_cloudPage + 1);
                        else _advanceHistoryPage = false;
                    }
                }
                else { _advanceHistoryPage = false; _historyError = "Account history is unavailable. Local scoreboards remain available. " + AccountService.ErrorOf(request); }
                if (_historySelected) Render();
            }
            else { _ = request.Exception; if (_historySelected) LoadHistoryPage(0); }
        }
        if (_detailsRequest is { IsCompleted: true } detail)
        {
            _detailsRequest = null;
            if (_detailIdentity == HistoryIdentity)
            {
                if (detail.IsCompletedSuccessfully) { var entry = SaveDetails(detail.Result); OpenHistoryRecord(entry, detail.Result); ShowStatus(""); }
                else
                {
                    ShowError("Could not retrieve account details. " + AccountService.ErrorOf(detail));
                    if (_detailFallback is { Scoreboard.Rows.Count: > 0 } fallback) OpenHistoryRecord(fallback);
                }
                if (_historySelected) Render();
            }
            else _ = detail.Exception;
            _detailFallback = null;
        }
        if (_reconcileRequest is { IsCompleted: true } reconcile)
        {
            _reconcileRequest = null;
            if (_reconcileIdentity == HistoryIdentity && reconcile.IsCompletedSuccessfully) { SaveDetails(reconcile.Result); if (_historySelected) Render(); }
            else _ = reconcile.Exception;
        }
        if (_historySelected && _reconcileRequest is null && _reconcile.TryDequeue(out string? round))
        {
            _reconcileIdentity = HistoryIdentity;
            _reconcileRequest = AccountService.Call(c => c.MatchDetailsByRoundAsync(round, _lifetime.Token));
        }
    }

    private void PreviousCollectionPage()
    {
        if (_historySelected) { _advanceHistoryPage = false; _historyPage = Math.Max(0, _historyPage - 1); Render(); }
        else LoadRecent(Math.Max(0, (_online?.Page ?? 0) - 1));
    }

    private void NextCollectionPage()
    {
        if (!_historySelected) { LoadRecent((_online?.Page ?? 0) + 1); return; }
        if ((_historyPage + 1) * 20 < HistoryItems().Count()) _historyPage++;
        else if (_cloudMore) { _advanceHistoryPage = true; LoadHistoryPage(_cloudPage + 1); }
        Render();
    }
}
