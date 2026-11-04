using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Net.Scoreboards;
using OpenScct.Server;
using OpenScctGame.Online;

namespace OpenScctGame.Play;

/// <summary>Saves only snapshots received by the playing client, including for a local listen host.</summary>
public sealed class ScoreboardCapture : IDisposable
{
    private readonly ClientSession _client;
    private readonly string _master = OnlineSettings.Current.MasterUrl;
    private readonly long _account = AccountSettings.Current.SignedIn ? AccountSettings.Current.AccountId : 0;
    private RoundScoreboard? _last;
    private int _lastLocal = -1;
    private bool _disposed;

    public ScoreboardCapture(ClientSession client)
    {
        _client = client;
        client.ScoreboardReceived += Receive;
        if (client.Scoreboard is { } board) Receive(board);
    }

    private void Receive(RoundScoreboard board)
    {
        if (_last is { EndedUtc: null } previous && previous.RoundId != board.RoundId) Save(previous, _lastLocal, left: true);
        _last = board;
        _lastLocal = _client.ScoreboardLocalParticipantId;
        if (board.EndedUtc is not null) Save(board, _lastLocal, left: false);
    }

    private void Save(RoundScoreboard board, int local, bool left)
    {
        if (board.RoundId.Length == 0 || board.Rows.Count == 0) return;
        if (!board.Rows.Any(r => r.ParticipantId == local)) return; // Pure spectators have not played this round.
        if (left) board = board with
        {
            Partial = true, EndedUtc = DateTimeOffset.UtcNow,
            Rows = board.Rows.Select(r => r.ParticipantId == local ? r with { LeftEarly = true } : r).ToArray(),
        };
        ScoreboardHistory.Save(new ArchivedScoreboard
        {
            Scoreboard = board, LocalParticipantId = local, Master = board.Master ?? "",
            AccountId = board.Master is { } master && ScoreboardArchive.NormaliseMaster(master) == ScoreboardArchive.NormaliseMaster(_master) ? _account : 0,
            PlayedLocally = true, LeftEarly = left,
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.ScoreboardReceived -= Receive;
        if (_last is { EndedUtc: null } board) Save(board, _lastLocal, left: true);
    }
}

/// <summary>Godot path and error reporting around the independently testable archive.</summary>
public static class ScoreboardHistory
{
    private static ScoreboardArchive? _store;
    public static ScoreboardArchive Store => _store ??= new(ProjectSettings.GlobalizePath("user://scoreboards"));
    public static string? LastError { get; private set; }
    public static ArchivedScoreboard? LastCompleted { get; private set; }

    public static ArchivedScoreboard Save(ArchivedScoreboard entry)
    {
        if (entry.Scoreboard.Completed && entry.PlayedLocally) LastCompleted = entry;
        if (AccountSettings.Isolated) return entry;
        try { var saved = Store.Save(entry); LastError = null; return saved; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            LastError = "Could not save the scoreboard: " + e.Message;
            GD.PushWarning(LastError);
            return entry;
        }
    }

    public static IReadOnlyList<ArchivedScoreboard> Read()
    {
        if (AccountSettings.Isolated) return [];
        try
        {
            return Store.ReadAll(error => GD.PushWarning("Skipped scoreboard: " + error))
                .Where(e => ScoreboardArchive.VisibleTo(e, OnlineSettings.Current.MasterUrl,
                    AccountSettings.Current.SignedIn ? AccountSettings.Current.AccountId : 0)).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { LastError = "Could not read scoreboard history: " + e.Message; return []; }
    }
}
