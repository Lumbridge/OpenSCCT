using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Server.Hub;

/// <summary>The hub's emotes, by name, and what each shows over the player's head.</summary>
public static class HubEmotes
{
    /// <summary>The emotes in menu order (the emote menu's keys 1 to 8).</summary>
    public static readonly IReadOnlyList<string> All = ["wave", "salute", "cheer", "dance", "sit", "point", "laugh", "clap"];

    /// <summary>The words under a player's name while he emotes.</summary>
    public static string Caption(string emote) => emote switch
    {
        "wave" => "waves",
        "salute" => "salutes",
        "cheer" => "cheers!",
        "dance" => "dances",
        "sit" => "sits down",
        "point" => "points over there",
        "laugh" => "laughs",
        "clap" => "claps",
        _ => emote,
    };

    /// <summary>How long the caption stays up (seconds).</summary>
    public static float Seconds(string emote) => emote is "dance" or "sit" ? 8f : 4f;

    public static bool IsEmote(string name)
    {
        foreach (string e in All)
        {
            if (e == name)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>The jukebox's tracks: our own procedural pieces (game/Scripts/Hub/HubAudio.cs plays them), so no licence applies.</summary>
public static class HubJukeboxTracks
{
    public static readonly IReadOnlyList<string> Titles = ["Plaza Drift", "Night Shift", "Rooftop Run", "Static Lounge"];

    public static string Title(int track) => Titles[((track % Titles.Count) + Titles.Count) % Titles.Count];
}

/// <summary>Emotes, the jukebox and the plaza ball.</summary>
public sealed partial class HubActivities
{
    private int _track;
    private double _trackChangedAt = double.NegativeInfinity;
    private FVector _ball;
    private float _ballVx;
    private float _ballVy;
    private double _ballSentAt;
    private double _kickAt = double.NegativeInfinity;
    private double _goalResetAt = -1;
    private bool _ballMoving;
    private PlayerSlot? _lastKicker;

    /// <summary>The ball's place now (diagnostics).</summary>
    public FVector Ball => _ball;

    public int Track => _track;

    private void Emote(PlayerSlot slot, PlayState state, string emote)
    {
        double now = Now;
        if (!HubEmotes.IsEmote(emote) || LivingPawn(slot) is not { } pawn || now - state.EmoteAt < HubPlayK.EmoteCooldown)
        {
            return;
        }

        state.EmoteAt = now;
        state.EmoteUntil = now + HubEmotes.Seconds(emote);
        SendNear(pawn.Location, HubPlayK.EmoteRange, HubPlayProtocol.Emote(ReplicationId(slot), emote), slot);
    }

    /// <summary>Ends a player's emote for everyone near when he moves off or loses his pawn, so the pose stops as he goes.</summary>
    private void TickEmote(PlayerSlot slot, PlayState state, Pawn? pawn, double now)
    {
        if (now >= state.EmoteUntil)
        {
            return;
        }

        FVector v = pawn?.Velocity ?? FVector.Zero;
        bool moved = pawn is null || (v.X * v.X) + (v.Y * v.Y) > HubPlayK.EmoteStopSpeed * HubPlayK.EmoteStopSpeed ||
                     MathF.Abs(v.Z) > HubPlayK.EmoteStopSpeed;
        if (!moved || now - state.EmoteAt < 0.25)
        {
            return;
        }

        state.EmoteUntil = double.NegativeInfinity;
        if (pawn is not null)
        {
            SendNear(pawn.Location, HubPlayK.EmoteRange, HubPlayProtocol.EmoteStop(ReplicationId(slot)), slot);
        }
    }

    private void NextTrack(PlayerSlot slot)
    {
        double now = Now;
        if (now - _trackChangedAt < HubPlayK.JukeboxCooldown)
        {
            Send(slot, HubPlayProtocol.Toast("The jukebox is still changing the record."));
            return;
        }

        _trackChangedAt = now;
        _track = (_track + 1) % HubJukeboxTracks.Titles.Count;
        _hub.Host.Broadcast(HubPlayProtocol.Jukebox(_track, HubJukeboxTracks.Title(_track)));
        SendNear(Layout.Jukebox, HubPlayK.JukeboxRange, HubPlayProtocol.Toast($"{slot.Name} put on \"{HubJukeboxTracks.Title(_track)}\"."));
    }

    // ---- the plaza ball ------------------------------------------------------------------------------------------

    private FVector BallHome
    {
        get
        {
            var p = Layout.Plaza;
            return new FVector((p.X0 + p.X1) / 2f, p.Y0 + (Layout.Fountain.Y0 - p.Y0) / 2f, Layout.Floor + HubPlayK.BallRadius);
        }
    }

    private void InitBall()
    {
        _ball = BallHome;
        _ballVx = 0f;
        _ballVy = 0f;
    }

    private string BallLine() => HubPlayProtocol.Ball(_ball, _ballVx, _ballVy);

    private void TickBall(double now)
    {
        const float dt = 1f / Sim.Hz;
        if (_goalResetAt >= 0 && now >= _goalResetAt)
        {
            _goalResetAt = -1;
            InitBall();
            _hub.Host.Broadcast(BallLine());
        }

        bool kicked = _goalResetAt < 0 && Kick(now);
        float speed = MathF.Sqrt(_ballVx * _ballVx + _ballVy * _ballVy);
        if (speed > 0f)
        {
            _ball = new FVector(_ball.X + _ballVx * dt, _ball.Y + _ballVy * dt, _ball.Z);
            float keep = MathF.Max(0f, 1f - HubPlayK.BallFriction * dt);
            _ballVx *= keep;
            _ballVy *= keep;
            if (speed * keep < HubPlayK.BallStopSpeed)
            {
                _ballVx = 0f;
                _ballVy = 0f;
            }

            Bounce(now);
        }

        bool moving = _ballVx != 0f || _ballVy != 0f;
        if (kicked || _ballMoving && !moving)
        {
            // A kick and the stop go to everyone; the updates in between only to the players near the ball.
            _ballSentAt = now;
            _hub.Host.Broadcast(BallLine());
        }
        else if (moving && now - _ballSentAt >= HubPlayK.BallSendInterval)
        {
            _ballSentAt = now;
            SendNear(_ball, HubPlayK.BallSendRange, BallLine());
        }

        _ballMoving = moving;
    }

    /// <summary>A pawn walking into the ball kicks it away from him, at his speed and more.</summary>
    private bool Kick(double now)
    {
        if (now - _kickAt < 0.15)
        {
            return false;
        }

        foreach (PlayerSlot slot in _hub.Host.Players)
        {
            if (LivingPawn(slot) is not { } pawn)
            {
                continue;
            }

            float dx = _ball.X - pawn.Location.X;
            float dy = _ball.Y - pawn.Location.Y;
            float reach = pawn.CollisionRadius + HubPlayK.BallRadius;
            float d2 = dx * dx + dy * dy;
            if (d2 > reach * reach || MathF.Abs(pawn.Location.Z - pawn.CollisionHeight - (_ball.Z - HubPlayK.BallRadius)) > 64f)
            {
                continue;
            }

            float d = MathF.Max(1f, MathF.Sqrt(d2));
            float nx = dx / d;
            float ny = dy / d;
            float pawnSpeed = MathF.Sqrt(pawn.Velocity.X * pawn.Velocity.X + pawn.Velocity.Y * pawn.Velocity.Y);
            float kick = Math.Clamp(pawnSpeed * HubPlayK.BallKickScale, HubPlayK.BallMinKick, HubPlayK.BallMaxSpeed);
            _ballVx = nx * kick;
            _ballVy = ny * kick;
            _ball = new FVector(pawn.Location.X + nx * (reach + 1f), pawn.Location.Y + ny * (reach + 1f), _ball.Z);
            _kickAt = now;
            _lastKicker = slot;
            return true;
        }

        return false;
    }

    /// <summary>Off the plaza's edges and the fountain; through a goal mouth it is a goal.</summary>
    private void Bounce(double now)
    {
        var p = Layout.Plaza;
        float r = HubPlayK.BallRadius;
        float mid = (p.Y0 + p.Y1) / 2f;
        bool inMouth = MathF.Abs(_ball.Y - mid) <= HubPlayK.GoalHalfWidth;
        if (_ball.X < p.X0 + r || _ball.X > p.X1 - r)
        {
            if (inMouth && _goalResetAt < 0)
            {
                Goal(west: _ball.X < p.X0 + r, now);
                return;
            }

            _ball = _ball with { X = Math.Clamp(_ball.X, p.X0 + r, p.X1 - r) };
            _ballVx = -_ballVx * HubPlayK.BallBounce;
        }

        if (_ball.Y < p.Y0 + r || _ball.Y > p.Y1 - r)
        {
            _ball = _ball with { Y = Math.Clamp(_ball.Y, p.Y0 + r, p.Y1 - r) };
            _ballVy = -_ballVy * HubPlayK.BallBounce;
        }

        var f = Layout.Fountain;
        if (_ball.X > f.X0 - r && _ball.X < f.X1 + r && _ball.Y > f.Y0 - r && _ball.Y < f.Y1 + r)
        {
            // Out along the nearest side.
            float left = _ball.X - (f.X0 - r);
            float right = f.X1 + r - _ball.X;
            float down = _ball.Y - (f.Y0 - r);
            float up = f.Y1 + r - _ball.Y;
            float least = MathF.Min(MathF.Min(left, right), MathF.Min(down, up));
            if (least == left || least == right)
            {
                _ball = _ball with { X = least == left ? f.X0 - r : f.X1 + r };
                _ballVx = -_ballVx * HubPlayK.BallBounce;
            }
            else
            {
                _ball = _ball with { Y = least == down ? f.Y0 - r : f.Y1 + r };
                _ballVy = -_ballVy * HubPlayK.BallBounce;
            }
        }
    }

    private void Goal(bool west, double now)
    {
        _ballVx = 0f;
        _ballVy = 0f;
        _goalResetAt = now + HubPlayK.GoalResetSeconds;
        _hub.Host.Broadcast(BallLine());
        string side = west ? "west" : "east";
        if (_lastKicker is { } kicker && _states.ContainsKey(kicker))
        {
            int goals = (Scores.Goals.Best(kicker.Name)?.Value ?? 0) + 1;
            Scores.Goals.Submit(new HubScore(kicker.Name, goals, string.Empty, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            Scores.Save();
            PublishBallBoard();
            SendNear(_ball, HubPlayK.FanfareRange, HubPlayProtocol.Fanfare(string.Create(CultureInfo.InvariantCulture,
                $"GOAL! {kicker.Name} scores in the {side} goal ({goals} in all).")));
        }
        else
        {
            SendNear(_ball, HubPlayK.FanfareRange, HubPlayProtocol.Toast($"The ball rolled into the {side} goal."));
        }
    }

    private void PublishBallBoard() =>
        PublishBoard(BallBoardId, HubScores.BoardText("PLAZA BALL  most goals", Scores.Goals,
            static s => s.Value.ToString(CultureInfo.InvariantCulture)));
}
