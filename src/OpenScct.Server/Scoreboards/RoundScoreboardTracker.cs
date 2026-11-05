using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Net.Scoreboards;

namespace OpenScct.Server;

/// <summary>Private reporting identity. Never included in the public scoreboard transport.</summary>
public sealed record ScoreboardIdentity(string Key, int SessionId, long? AccountId = null, string? PlayerKey = null,
    bool Bot = false, int Rank = 0, int PingMs = 0);

/// <summary>Host-thread-owned round ledger and disclosure queue, used by online and offline hosts alike.</summary>
public sealed class RoundScoreboardTracker(ServerHost host)
{
    public const long DelayMicroseconds = 30_000_000;
    private const long SampleMicroseconds = 1_000_000;
    private const long RefreshMicroseconds = 30_000_000;
    private readonly Dictionary<PlayerReplicationInfo, Participant> _byPri = new();
    private readonly Dictionary<string, Participant> _byIdentity = new(StringComparer.Ordinal);
    private readonly List<Participant> _players = new();
    private readonly Queue<(long Time, Dictionary<int, IReadOnlyDictionary<string, int>> Stats)> _samples = new();
    private readonly Dictionary<NetConnection, long> _sent = new();
    private Dictionary<int, IReadOnlyDictionary<string, int>>? _released;
    private double? _asOf;
    private long _start = -1, _now, _nextSample, _nextRelease, _sequence;
    private DateTimeOffset _startedUtc;

    private sealed class Participant
    {
        public required int Id;
        public required PlayerReplicationInfo Pri;
        public required ScoreboardIdentity Identity;
        public ScoreboardPlayer Row = new();
        public RecordedLoadout? Loadout;
        public bool Left;
    }

    public string RoundId { get; } = Guid.NewGuid().ToString("N");
    public string ServerName { get; set; } = "Offline";
    public string? Master { get; set; }
    public TimeProvider WallClock { get; set; } = TimeProvider.System;
    public bool Enabled { get; set; } = true;
    public Func<PlayerReplicationInfo, ScoreboardIdentity?>? IdentityOf { get; set; }
    public RoundScoreboard? Current { get; private set; }
    public RoundScoreboard? Final { get; private set; }
    public event Action<RoundScoreboard>? Finalized;
    public event Action<string>? Warning;
    public string? LastError { get; private set; }

    public ScoreboardIdentity? Identity(int participantId) => _players.FirstOrDefault(p => p.Id == participantId)?.Identity;
    public RecordedLoadout? Loadout(int participantId) => _players.FirstOrDefault(p => p.Id == participantId)?.Loadout;
    public int ParticipantId(PlayerReplicationInfo? pri) => pri is not null && _byPri.TryGetValue(pri, out var p) ? p.Id : -1;

    public void Tick(long now)
    {
        _now = now;
        if (!Enabled || host.World.Level.Info.Game is not SGameInfo game) return;
        if (_start < 0)
        {
            if (game.GameReplicationInfo?.bMatchHasBegun != true) return;
            _start = now; _nextSample = now; _startedUtc = WallClock.GetUtcNow();
            _nextRelease = now + DelayMicroseconds;
        }
        if (Final is not null) { SendUpdates(); return; }
        Discover();
        // The original end-state transition can precede replication of its winning side/reason.
        if (game.bGameEnded && game.GameReplicationInfo is { WinningTeam: not 255 }) { Finish(completed: true); return; }
        if (now < _nextSample) return;
        _nextSample = now + SampleMicroseconds;
        CaptureRows();
        _samples.Enqueue((now, _players.ToDictionary(p => p.Id, p => p.Row.Stats!)));
        if (now >= _nextRelease)
        {
            while (_samples.TryPeek(out var sample) && sample.Time <= now - DelayMicroseconds)
            {
                _samples.Dequeue(); _released = sample.Stats; _asOf = (sample.Time - _start) / 1_000_000d;
            }
            _nextRelease = now + RefreshMicroseconds;
        }
        Current = Build(game, final: false, completed: false);
        SendUpdates();
    }

    private void Discover()
    {
        foreach (var pri in host.World.Level.AllActors<PlayerReplicationInfo>())
        {
            if (pri.bOnlySpectator || pri.bDeleteMe || pri.Team?.TeamIndex is not (0 or 1)) continue;
            if (_byPri.TryGetValue(pri, out var known))
            {
                if (!known.Left && IdentityOf?.Invoke(pri) is { } identity) known.Identity = identity;
                continue;
            }
            ScoreboardIdentity data = IdentityOf?.Invoke(pri) ?? new("pri:" + pri.PlayerID, pri.PlayerID,
                Bot: !host.Players.Any(p => ReferenceEquals(p.Controller?.PlayerReplicationInfo, pri)), Rank: pri.SvmRank, PingMs: pri.Ping);
            if (_byIdentity.TryGetValue(data.Key, out var existing))
            {
                _byPri.Remove(existing.Pri); existing.Pri = pri; existing.Identity = data; existing.Left = false;
                _byPri[pri] = existing;
            }
            else if (_players.Count < 256)
            {
                var player = new Participant { Id = _players.Count + 1, Pri = pri, Identity = data };
                _players.Add(player); _byPri[pri] = player; _byIdentity[data.Key] = player;
            }
        }
        foreach (var p in _players)
            if (!p.Left && (p.Pri.bDeleteMe || p.Pri.bOnlySpectator)) { Capture(p); p.Left = true; }
    }

    public void Depart(PlayerReplicationInfo? pri)
    {
        if (_start < 0 || Final is not null || pri is null) return;
        Discover();
        if (_byPri.TryGetValue(pri, out var p)) { Capture(p); p.Left = true; p.Row = p.Row with { LeftEarly = true }; }
    }

    private void CaptureRows()
    {
        foreach (var p in _players)
        {
            if (!p.Left) Capture(p);
            // Already-applied poison retains its instigator and can still credit this stats object after logout.
            // Keep the departed player's public fields frozen; a reconnect replaces Pri with the restored one.
            else p.Row = p.Row with { Stats = RoundStatCounters.Read(p.Pri) };
        }
    }

    private static void Capture(Participant p)
    {
        var pri = p.Pri;
        p.Row = new ScoreboardPlayer
        {
            ParticipantId = p.Id, Name = pri.PlayerName, Team = pri.Team?.TeamIndex ?? 255,
            Score = (int)pri.Score, Kills = pri.Kills, Deaths = pri.Deaths,
            Lives = pri.NumLives >= OpenScct.Game.GameFlow.K.UnlimitedLives ? -1 : pri.NumLives,
            Objectives = pri.iNbObjectivesTaken, Rank = p.Identity.Rank, PingMs = p.Identity.PingMs,
            Bot = p.Identity.Bot, LeftEarly = p.Left, Stats = RoundStatCounters.Read(pri),
        };
        if (pri.Stats is { } stats) p.Loadout = new RecordedLoadout { Weapon = stats.Weapon, Gadgets = stats.Gadgets }.Snapshot();
    }

    public RoundScoreboard? Finish(bool completed = false)
    {
        if (Final is not null) return Final;
        if (_start < 0 || host.World.Level.Info.Game is not SGameInfo game) return null;
        Discover(); CaptureRows();
        Final = Current = Build(game, final: true, completed);
        _samples.Clear(); _released = null;
        host.Recorder?.SetScoreboard(Final);
        SendUpdates();
        Finalized?.Invoke(Final);
        return Final;
    }

    private RoundScoreboard Build(SGameInfo game, bool final, bool completed)
    {
        int needed = 0, taken = 0;
        int mode = (int)(game.MatchInfo?.GameMode ?? EGameMode.GM_Undefined);
        foreach (SMission mission in host.World.Level.AllActors<SMission>())
        {
            if ((int)mission.GameMode != mode) continue;
            needed = mission.MinimumObjectives; taken += mission.Objectives.Count(o => o is { ObjectiveTaken: true });
        }
        double elapsed = Math.Max(0, (_now - _start) / 1_000_000d);
        return new RoundScoreboard
        {
            RoundId = RoundId, Sequence = ++_sequence, Map = host.Map.Name, Server = ServerName, Mode = mode,
            StartedUtc = _startedUtc, EndedUtc = final ? WallClock.GetUtcNow() : null, ElapsedSeconds = elapsed,
            AsOfSeconds = final ? elapsed : _asOf, Completed = completed, Partial = final && !completed,
            NextCombatUpdateSeconds = final ? null : (_nextRelease - _start) / 1_000_000d,
            Practice = OpenScct.Game.Modes.PracticeRules.IsOn(game), Master = Master,
            Winner = final ? game.GameReplicationInfo?.WinningTeam ?? 255 : 255,
            Reason = final ? game.GameReplicationInfo?.WinningReason ?? 255 : 255,
            ObjectivesTaken = taken, ObjectivesNeeded = needed,
            Rows = _players.Select(p => p.Row with { LeftEarly = p.Left,
                Stats = final ? p.Row.Stats : _released?.GetValueOrDefault(p.Id) }).ToArray(),
        };
    }

    public void AttachMatchId(long id)
    {
        if (Final is null || Final.MatchId == id) return;
        Current = Final = Final with { MatchId = id, Sequence = ++_sequence };
        host.Recorder?.SetScoreboard(Final);
        SendUpdates();
    }

    private void SendUpdates()
    {
        if (Current is not { } board) return;
        foreach (var slot in host.Players)
        {
            if (!slot.Connection.bJoined || _sent.GetValueOrDefault(slot.Connection, -1) == board.Sequence) continue;
            if (!Send(slot.Connection, board, ParticipantId(slot.Controller?.PlayerReplicationInfo)))
            {
                const string error = "Scoreboard exceeds the bounded transfer limit.";
                if (LastError != error) { LastError = error; Warning?.Invoke("scoreboard not sent: " + error); }
            }
            _sent[slot.Connection] = board.Sequence;
        }
        foreach (var connection in _sent.Keys.Where(c => !host.Players.Any(p => ReferenceEquals(p.Connection, c))).ToArray()) _sent.Remove(connection);
    }

    public static bool Send(NetConnection connection, RoundScoreboard board, int localParticipantId, bool historical = false)
    {
        IReadOnlyList<string> lines;
        try { lines = ScoreboardWire.Encode(board, localParticipantId, historical); }
        catch (InvalidDataException) { return false; }
        foreach (string line in lines)
            connection.ControlChannel?.Send(ControlMessage.TextLine(line));
        return true;
    }
}
