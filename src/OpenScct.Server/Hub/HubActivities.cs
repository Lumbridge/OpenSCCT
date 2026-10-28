using System;
using System.Collections.Generic;
using System.Text;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;

namespace OpenScct.Server.Hub;

/// <summary>
/// The hub's things to do while waiting (an OpenSCCT addition, spec/additions/hub.md "Activities"): the spy
/// movement course's time trial, the merc aim range, emotes, the jukebox and the plaza ball, with their score boards.
/// Server-authoritative: the server watches the pawns, keeps the clocks, the targets and the ball, scores and tells
/// each player over <see cref="HubPlayProtocol"/>. One per <see cref="HubServer"/>, ticked after the host.
/// </summary>
public sealed partial class HubActivities
{
    /// <summary>The board ids (the coordinator routes <see cref="PublishBoard"/> to the hub's world signs).</summary>
    public const string CourseBoardId = "CourseBoard";

    public const string RangeBoardId = "RangeBoard";

    public const string BallBoardId = "BallBoard";

    private const int MaxLineBytes = 500;

    private readonly HubServer _hub;
    private readonly Func<long> _micros;
    private readonly Dictionary<PlayerSlot, PlayState> _states = new();
    private readonly Dictionary<string, (FVector At, string Text)> _boards = new(StringComparer.Ordinal);
    private readonly List<string> _marks = new();
    private readonly SimRandom _random;

    /// <summary>One player's activity state.</summary>
    private sealed class PlayState
    {
        public bool InStart;
        public Run? Run;
        public double MercNoticeAt = double.NegativeInfinity;
        public double MissNoticeAt = double.NegativeInfinity;
        public double EmoteAt = double.NegativeInfinity;
        public double EmoteUntil = double.NegativeInfinity;
    }

    internal HubActivities(HubServer hub, HubLayout layout, HubScores scores, Func<long> micros, ulong seed)
    {
        _hub = hub;
        _micros = micros;
        Layout = layout;
        Scores = scores;
        _random = new SimRandom(seed, 0x4855, 1);
        scores.KeepCourseLayout(layout.LayoutVersion);
        _boards[CourseBoardId] = (layout.CourseBoard, string.Empty);
        _boards[RangeBoardId] = (layout.RangeBoard, string.Empty);
        _boards[BallBoardId] = (layout.BallBoard, string.Empty);
        BuildMarks();
        InitBall();
        hub.Host.PlayerJoined += OnJoined;
        hub.Host.PlayerLeft += OnLeft;
        hub.Host.SessionLineReceived += OnLine;
        hub.World.Level.SetService<OpenScct.Game.Combat.IHitZones>(
            new HubRangeHitZones(OpenScct.Game.Combat.CombatServices.HitZones(hub.World.Level.Info), this));
        PublishCourseBoard();
        PublishRangeBoard();
        PublishBallBoard();
    }

    public HubLayout Layout { get; }

    public HubScores Scores { get; }

    /// <summary>The hub's time in seconds (double: a hub runs for days, and the course times are in ms).</summary>
    public double Now => _micros() / 1_000_000.0;

    /// <summary>Raised for each board published (id, text with '\n' between lines): the hook for world signs.</summary>
    public event Action<string, string>? BoardPublished;

    /// <summary>The latest text of a board, or null.</summary>
    public string? BoardText(string id) => _boards.TryGetValue(id, out var b) ? b.Text : null;

    /// <summary>Where a board stands (its area's leaderboard anchor, else the default next to the activity).</summary>
    public FVector BoardAt(string id) => _boards.TryGetValue(id, out var b) ? b.At : Layout.CourseBoard;

    /// <summary>The board's face on the map (its text lies on it), or a billboard for a board with no mounted board.</summary>
    public HubSignFace BoardFace(string id) => id switch
    {
        CourseBoardId => Layout.CourseBoardFace,
        RangeBoardId => Layout.RangeBoardFace,
        _ => HubSignFace.None,
    };

    /// <summary>Every board id.</summary>
    public static IReadOnlyList<string> BoardIds { get; } = [CourseBoardId, RangeBoardId, BallBoardId];

    /// <summary>
    /// Publishes a score board's text (lines separated by '\n'): the one place every leaderboard and score text of the
    /// activities goes through. For now each client draws it in the hub HUD near the board's anchor; the hub's world
    /// signs take it from <see cref="BoardPublished"/>.
    /// </summary>
    public void PublishBoard(string id, string text)
    {
        ArgumentNullException.ThrowIfNull(id);
        text ??= string.Empty;
        FVector at = _boards.TryGetValue(id, out var old) ? old.At : Layout.CourseBoard;
        _boards[id] = (at, text);
        _hub.Host.Broadcast(BoardLine(id, at, text));
        BoardPublished?.Invoke(id, text);
    }

    /// <summary>A board's line, dropping rows from the end until it fits a control message.</summary>
    private static string BoardLine(string id, FVector at, string text)
    {
        string line = HubPlayProtocol.Board(id, at, text);
        while (Encoding.UTF8.GetByteCount(line) > MaxLineBytes && text.LastIndexOf('\n') is var cut and > 0)
        {
            text = text[..cut];
            line = HubPlayProtocol.Board(id, at, text);
        }

        return line;
    }

    /// <summary>One step, after the host's tick.</summary>
    internal void Tick()
    {
        double now = Now;
        foreach (PlayerSlot slot in _hub.Host.Players)
        {
            if (!_states.TryGetValue(slot, out PlayState? state))
            {
                continue;
            }

            Pawn? pawn = LivingPawn(slot);
            TickCourse(slot, state, pawn, now);
            TickEmote(slot, state, pawn, now);
        }

        TickRange(now);
        TickBall(now);
    }

    private void OnJoined(PlayerSlot slot)
    {
        _states[slot] = new PlayState();
        foreach (string mark in _marks)
        {
            Send(slot, mark);
        }

        // Each pad's laptop (the pads are made after the activities, so their marks go out here).
        foreach (HubPad pad in _hub.Pads?.Pads ?? [])
        {
            Send(slot, HubPlayProtocol.Mark("laptop", "Laptop." + pad.Config.Id, pad.Area.Laptop, HubPads.LaptopUseRange));
        }

        foreach (var (id, board) in _boards)
        {
            Send(slot, BoardLine(id, board.At, board.Text));
        }

        Send(slot, HubPlayProtocol.Jukebox(_track, HubJukeboxTracks.Title(_track)));
        Send(slot, BallLine());
        foreach (RangeRound round in _rounds)
        {
            foreach (HubRangeTarget t in round.Targets)
            {
                if (t.Up)
                {
                    Send(slot, TargetLine(t, true));
                }
            }
        }
    }

    private void OnLeft(PlayerSlot slot)
    {
        _states.Remove(slot);
        for (int i = _rounds.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_rounds[i].Owner, slot))
            {
                EndRound(_rounds[i], cancelled: true, reason: null);
            }
        }
    }

    private void OnLine(NetConnection connection, PlayerSlot? slot, string line)
    {
        if (slot is null || !_states.TryGetValue(slot, out PlayState? state) ||
            !HubPlayProtocol.TryParse(line, out string verb, out List<string> fields))
        {
            return;
        }

        switch (verb)
        {
            case "emote":
                Emote(slot, state, OpenScct.Server.Online.SessionProtocol.Text(fields, 0));
                break;
            case "use":
                Use(slot);
                break;
            case "cancel":
                if (state.Run is not null)
                {
                    CancelRun(slot, state, "You stopped the run.");
                }

                foreach (RangeRound round in _rounds)
                {
                    if (ReferenceEquals(round.Owner, slot))
                    {
                        EndRound(round, cancelled: true, reason: "You stopped the round.");
                        break;
                    }
                }

                break;
        }
    }

    /// <summary>U: ready up on a match pad, a pad's laptop, the jukebox when near it, else a range round at the firing line.</summary>
    private void Use(PlayerSlot slot)
    {
        if (LivingPawn(slot) is not { } pawn)
        {
            return;
        }

        // On a match pad the key readies the player up.
        if (_hub.Pads?.ToggleReady(slot) == true)
        {
            return;
        }

        // At a pad's laptop the key opens it (the client shows the pad's settings).
        if (_hub.Pads?.LaptopAt(slot) is { } laptop)
        {
            Send(slot, HubProtocol.Laptop(laptop.Config.Id));
            return;
        }

        if (Flat(pawn.Location - Layout.Jukebox) <= HubPlayK.JukeboxUseRange)
        {
            NextTrack(slot);
            return;
        }

        if (AtFiringLine(pawn.Location))
        {
            StartRound(slot, pawn);
            return;
        }

        Send(slot, HubPlayProtocol.Toast("Nothing to use here. U works on a match pad (ready up), at a pad's laptop (its match), at the range's firing line and at the jukebox."));
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    private static Pawn? LivingPawn(PlayerSlot slot) =>
        slot.Controller?.Pawn is { bDeleteMe: false, Health: > 0 } pawn ? pawn : null;

    private static int TeamOf(PlayerSlot slot) => SGameInfo.TeamIndexOf(slot.Controller);

    private static int ReplicationId(PlayerSlot slot) => slot.Controller?.PlayerReplicationInfo?.PlayerID ?? 0;

    private static float Flat(FVector d) => MathF.Sqrt(d.X * d.X + d.Y * d.Y);

    private static void Send(PlayerSlot slot, string line) => slot.Connection.ControlChannel?.Send(ControlMessage.TextLine(line));

    /// <summary>Sends <paramref name="line"/> to every player whose pawn is within <paramref name="range"/> of <paramref name="at"/>, and to <paramref name="also"/>.</summary>
    private void SendNear(FVector at, float range, string line, PlayerSlot? also = null)
    {
        float r2 = range * range;
        foreach (PlayerSlot p in _hub.Host.Players)
        {
            if (ReferenceEquals(p, also) || LivingPawn(p) is { } pawn && (pawn.Location - at).SizeSquared() <= r2)
            {
                Send(p, line);
            }
        }
    }

    private void BuildMarks()
    {
        HubLayout l = Layout;
        _marks.Add(HubPlayProtocol.Mark("start", l.CourseStart.Id, l.CourseStart.At, l.CourseStart.Radius));
        _marks.Add(HubPlayProtocol.Mark("finish", l.CourseFinish.Id, l.CourseFinish.At, l.CourseFinish.Radius));
        foreach (HubGate cp in l.Checkpoints)
        {
            _marks.Add(HubPlayProtocol.Mark("checkpoint", cp.Id, cp.At, cp.Radius));
        }

        float laneMid = (l.Lane.Y0 + l.Lane.Y1) / 2f;
        _marks.Add(HubPlayProtocol.Mark("firingline", "FiringLine", new FVector(l.FiringLine, laneMid, l.Floor), (l.Lane.Y1 - l.Lane.Y0) / 2f));
        _marks.Add(HubPlayProtocol.Mark("jukebox", "Jukebox", l.Jukebox, HubPlayK.JukeboxRange));
        float plazaMid = (l.Plaza.Y0 + l.Plaza.Y1) / 2f;
        _marks.Add(HubPlayProtocol.Mark("goal", "GoalWest", new FVector(l.Plaza.X0, plazaMid, l.Floor), HubPlayK.GoalHalfWidth));
        _marks.Add(HubPlayProtocol.Mark("goal", "GoalEast", new FVector(l.Plaza.X1, plazaMid, l.Floor), HubPlayK.GoalHalfWidth));
    }
}
