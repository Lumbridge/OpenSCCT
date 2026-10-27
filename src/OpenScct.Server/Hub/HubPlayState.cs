using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Server.Online;

namespace OpenScct.Server.Hub;

/// <summary>A board as the client has it: where it stands and its lines (title first).</summary>
public sealed record HubBoard(string Id, FVector At, string[] Lines);

/// <summary>A spot the server marked (gate, checkpoint, firing line, jukebox, goal).</summary>
public readonly record struct HubMark(string Kind, string Id, FVector At, float Radius);

/// <summary>A pop-up target as the client draws it.</summary>
public sealed class HubTargetView
{
    public int Id { get; init; }

    public int Owner { get; set; }

    public bool Up { get; set; }

    public FVector Foot { get; set; }

    /// <summary>Client time (s) of the last rise or fall, for the flip animation.</summary>
    public double ChangedAt { get; set; }
}

/// <summary>A player's emote as the client shows it.</summary>
public readonly record struct HubEmoteView(string Emote, double Until);

/// <summary>
/// The client's side of the hub's activities (an OpenSCCT addition): everything the server's activity lines said,
/// kept for the HUD and the world props: the boards, the marks, the player's course run and range round, the
/// targets, the emotes around him, the jukebox's track, the ball, and the notices. Godot-free; times are the
/// caller's clock in seconds (<see cref="Now"/>, set every frame).
/// </summary>
public sealed class HubPlayState
{
    private const int MaxToasts = 4;

    private readonly Dictionary<string, HubBoard> _boards = new(StringComparer.Ordinal);
    private readonly List<HubMark> _marks = new();
    private readonly Dictionary<int, HubTargetView> _targets = new();
    private readonly Dictionary<int, HubEmoteView> _emotes = new();
    private readonly List<(string Text, double Until, bool Big)> _toasts = new();
    private Func<double> _clock = static () => 0.0;

    /// <summary>The client's clock (seconds) the state times its timers and notices by.</summary>
    public Func<double> Clock
    {
        get => _clock;
        set => _clock = value ?? throw new ArgumentNullException(nameof(value));
    }

    public double Now => _clock();

    public IReadOnlyDictionary<string, HubBoard> Boards => _boards;

    public IReadOnlyList<HubMark> Marks => _marks;

    public IReadOnlyDictionary<int, HubTargetView> Targets => _targets;

    // ---- the course run ------------------------------------------------------------------------------------------

    public bool Running { get; private set; }

    /// <summary>Client time the run started (the server's start line arrived).</summary>
    public double RunStartedAt { get; private set; }

    public int Checkpoints { get; private set; }

    public int Passed { get; private set; }

    /// <summary>The last split (ms), or -1.</summary>
    public int LastSplit { get; private set; } = -1;

    /// <summary>The last finished run (ms), or -1; and when it finished (client time).</summary>
    public int LastRun { get; private set; } = -1;

    public double LastRunAt { get; private set; } = double.NegativeInfinity;

    /// <summary>The run's time so far (ms), from the client's clock (the server's time replaces it at the finish).</summary>
    public int RunMs => Running ? (int)Math.Max(0, (Now - RunStartedAt) * 1000.0) : Math.Max(0, LastRun);

    // ---- the range round -----------------------------------------------------------------------------------------

    public bool InRound { get; private set; }

    public int RangePoints { get; private set; }

    public int RangeHits { get; private set; }

    public int RangeShots { get; private set; }

    public int RangeHeads { get; private set; }

    /// <summary>Client time the round ends.</summary>
    public double RoundEndsAt { get; private set; }

    public int LastRangeScore { get; private set; } = -1;

    public double LastRangeAt { get; private set; } = double.NegativeInfinity;

    // ---- social --------------------------------------------------------------------------------------------------

    public int Track { get; private set; } = -1;

    public string TrackTitle { get; private set; } = string.Empty;

    /// <summary>The ball where the server last said, and its velocity (uu/s), at <see cref="BallAt"/> (client time).</summary>
    public FVector BallOrigin { get; private set; }

    public float BallVx { get; private set; }

    public float BallVy { get; private set; }

    public double BallAt { get; private set; }

    public bool HasBall { get; private set; }

    /// <summary>Raised on a fanfare (a new record or a goal near the player): the HUD chimes.</summary>
    public event Action<string>? Fanfare;

    /// <summary>Raised for a small event worth a sound: "cp", "finish", "best", "hit", "head", "start", "end".</summary>
    public event Action<string>? Cue;

    /// <summary>Raised when the jukebox's track changes.</summary>
    public event Action<int>? TrackChanged;

    /// <summary>The ball now: run on from the server's last word with the same friction (no bounces on the client).</summary>
    public FVector BallNow()
    {
        float t = (float)Math.Clamp(Now - BallAt, 0.0, 0.5);
        // The server loses k of the speed per second: over half a second at most, distance = v0 (t - k t^2 / 2).
        float s = t - 0.5f * HubPlayK.BallFriction * t * t;
        return new FVector(BallOrigin.X + BallVx * s, BallOrigin.Y + BallVy * s, BallOrigin.Z);
    }

    /// <summary>The emote <paramref name="playerId"/> shows now, or null.</summary>
    public string? EmoteOf(int playerId) =>
        _emotes.TryGetValue(playerId, out HubEmoteView e) && e.Until > Now ? e.Emote : null;

    /// <summary>The notices still up, newest last, and whether each is a big one (a fanfare or a finish).</summary>
    public void Toasts(List<(string Text, bool Big)> into)
    {
        into.Clear();
        double now = Now;
        _toasts.RemoveAll(t => t.Until <= now);
        foreach (var t in _toasts)
        {
            into.Add((t.Text, t.Big));
        }
    }

    public void Notice(string text, bool big = false, double seconds = 4.0)
    {
        _toasts.Add((text, Now + seconds, big));
        if (_toasts.Count > MaxToasts)
        {
            _toasts.RemoveAt(0);
        }
    }

    /// <summary>The nearest mark of <paramref name="kind"/> to <paramref name="at"/> on the floor, and its distance.</summary>
    public HubMark? Nearest(string kind, FVector at, out float distance)
    {
        HubMark? best = null;
        distance = float.MaxValue;
        foreach (HubMark m in _marks)
        {
            if (m.Kind != kind)
            {
                continue;
            }

            float dx = m.At.X - at.X;
            float dy = m.At.Y - at.Y;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            if (d < distance)
            {
                distance = d;
                best = m;
            }
        }

        return best;
    }

    /// <summary>Reads one session line; false when it is not an activity line.</summary>
    public bool Read(string line)
    {
        if (!HubPlayProtocol.TryParse(line, out string verb, out List<string> f))
        {
            return false;
        }

        double now = Now;
        switch (verb)
        {
            case "board":
            {
                string id = SessionProtocol.Text(f, 0);
                _boards[id] = new HubBoard(id, Vec(f, 1), HubPlayProtocol.BoardLines(SessionProtocol.Text(f, 4)));
                break;
            }

            case "mark":
            {
                var mark = new HubMark(SessionProtocol.Text(f, 0), SessionProtocol.Text(f, 1), Vec(f, 2), HubPlayProtocol.Float(f, 5));
                _marks.RemoveAll(m => m.Id == mark.Id);
                _marks.Add(mark);
                break;
            }

            case "run":
                ReadRun(f, now);
                break;
            case "range":
                ReadRange(f, now);
                break;
            case "target":
            {
                int id = SessionProtocol.Int(f, 0);
                if (!_targets.TryGetValue(id, out HubTargetView? t))
                {
                    t = new HubTargetView { Id = id };
                    _targets[id] = t;
                }

                t.Owner = SessionProtocol.Int(f, 1);
                t.Up = SessionProtocol.Int(f, 2) == 1;
                t.Foot = Vec(f, 3);
                t.ChangedAt = now;
                break;
            }

            case "emote":
            {
                string emote = SessionProtocol.Text(f, 1);
                _emotes[SessionProtocol.Int(f, 0)] = new HubEmoteView(emote, now + HubEmotes.Seconds(emote));
                break;
            }

            case "jukebox":
                Track = SessionProtocol.Int(f, 0);
                TrackTitle = SessionProtocol.Text(f, 1);
                TrackChanged?.Invoke(Track);
                break;
            case "ball":
                BallOrigin = Vec(f, 0);
                BallVx = HubPlayProtocol.Float(f, 3);
                BallVy = HubPlayProtocol.Float(f, 4);
                BallAt = now;
                HasBall = true;
                break;
            case "toast":
                Notice(SessionProtocol.Text(f, 0));
                break;
            case "fanfare":
                Notice(SessionProtocol.Text(f, 0), big: true, seconds: 6.0);
                Fanfare?.Invoke(SessionProtocol.Text(f, 0));
                break;
        }

        return true;
    }

    private void ReadRun(List<string> f, double now)
    {
        switch (SessionProtocol.Text(f, 0))
        {
            case "start":
                Running = true;
                RunStartedAt = now;
                Checkpoints = SessionProtocol.Int(f, 1);
                Passed = 0;
                LastSplit = -1;
                Cue?.Invoke("start");
                break;
            case "cp":
                Passed = SessionProtocol.Int(f, 1);
                Checkpoints = SessionProtocol.Int(f, 2);
                LastSplit = SessionProtocol.Int(f, 3);
                Cue?.Invoke("cp");
                break;
            case "finish":
            {
                Running = false;
                LastRun = SessionProtocol.Int(f, 1);
                LastRunAt = now;
                int rank = SessionProtocol.Int(f, 2);
                bool best = SessionProtocol.Int(f, 3) == 1;
                string place = rank > 0 ? $", #{rank} on the board" : string.Empty;
                Notice($"Finished in {HubScores.Time(LastRun)}{(best ? "  personal best" : string.Empty)}{place}", big: true, seconds: 6.0);
                Cue?.Invoke(best ? "best" : "finish");
                break;
            }

            case "cancel":
                Running = false;
                Notice(SessionProtocol.Text(f, 1));
                break;
        }
    }

    private void ReadRange(List<string> f, double now)
    {
        switch (SessionProtocol.Text(f, 0))
        {
            case "start":
                InRound = true;
                RangePoints = RangeHits = RangeShots = RangeHeads = 0;
                RoundEndsAt = now + SessionProtocol.Int(f, 1);
                Notice("Range round: shoot the targets!", big: true, seconds: 2.5);
                Cue?.Invoke("start");
                break;
            case "score":
            {
                int heads = SessionProtocol.Int(f, 4);
                int hits = SessionProtocol.Int(f, 2);
                if (hits > RangeHits)
                {
                    Cue?.Invoke(heads > RangeHeads ? "head" : "hit");
                }

                RangePoints = SessionProtocol.Int(f, 1);
                RangeHits = hits;
                RangeShots = SessionProtocol.Int(f, 3);
                RangeHeads = heads;
                RoundEndsAt = now + SessionProtocol.Int(f, 5) / 1000.0;
                break;
            }

            case "end":
            {
                InRound = false;
                int score = SessionProtocol.Int(f, 1);
                RangeHits = SessionProtocol.Int(f, 2);
                RangeShots = SessionProtocol.Int(f, 3);
                RangeHeads = SessionProtocol.Int(f, 4);
                if (score < 0)
                {
                    break;
                }

                int rank = SessionProtocol.Int(f, 5);
                bool best = SessionProtocol.Int(f, 6) == 1;
                LastRangeScore = score;
                LastRangeAt = now;
                int accuracy = RangeShots > 0 ? (int)Math.Round(100.0 * RangeHits / RangeShots) : 0;
                string place = rank > 0 ? $", #{rank} on the board" : string.Empty;
                Notice($"Round over: {score} points ({RangeHits}/{RangeShots} hits, {accuracy}%, {RangeHeads} headshots){(best ? "  personal best" : string.Empty)}{place}",
                    big: true, seconds: 7.0);
                Cue?.Invoke(best ? "best" : "end");
                break;
            }
        }
    }

    private static FVector Vec(List<string> f, int at) =>
        new(HubPlayProtocol.Float(f, at), HubPlayProtocol.Float(f, at + 1), HubPlayProtocol.Float(f, at + 2));
}
