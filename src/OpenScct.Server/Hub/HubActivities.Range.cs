using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;

namespace OpenScct.Server.Hub;

/// <summary>The merc aim range: timed rounds of pop-up targets downrange, hit by the real rifle trace.</summary>
public sealed partial class HubActivities
{
    private readonly List<RangeRound> _rounds = new();
    private int _nextTargetId = 1;

    /// <summary>One round in progress: its shooter, clock, tally and targets.</summary>
    internal sealed class RangeRound
    {
        public required PlayerSlot Owner;
        public required int OwnerId;
        public double Start;
        public double End;
        public double NextRise;
        public double ScoreSentAt;
        public int Points;
        public int Hits;
        public int Shots;
        public int Heads;
        public readonly List<HubRangeTarget> Targets = new();
    }

    /// <summary>Rounds in progress (the hit zones read their targets).</summary>
    internal IReadOnlyList<RangeRound> Rounds => _rounds;

    private bool AtFiringLine(FVector at)
    {
        HubLayout l = Layout;
        return at.X >= l.FiringLine - HubPlayK.FiringLineSlack && at.X <= l.RangeArea.X1 + 64f &&
               at.Y >= l.RangeArea.Y0 && at.Y <= l.RangeArea.Y1;
    }

    private void StartRound(PlayerSlot slot, Pawn pawn)
    {
        if (TeamOf(slot) != 1)
        {
            Send(slot, HubPlayProtocol.Toast("The aim range is for mercs. F3 to play merc."));
            return;
        }

        foreach (RangeRound r in _rounds)
        {
            if (ReferenceEquals(r.Owner, slot))
            {
                Send(slot, HubPlayProtocol.Toast("Your round is already running."));
                return;
            }
        }

        if (_rounds.Count >= HubPlayK.MaxRounds)
        {
            Send(slot, HubPlayProtocol.Toast("The range is full. Try again in a moment."));
            return;
        }

        double now = Now;
        _rounds.Add(new RangeRound
        {
            Owner = slot,
            OwnerId = ReplicationId(slot),
            Start = now,
            End = now + HubPlayK.RoundSeconds,
            NextRise = now + 0.5,
            ScoreSentAt = now,
        });
        Send(slot, HubPlayProtocol.RangeStart((int)HubPlayK.RoundSeconds));
    }

    private void TickRange(double now)
    {
        for (int i = _rounds.Count - 1; i >= 0; i--)
        {
            RangeRound round = _rounds[i];
            Pawn? pawn = LivingPawn(round.Owner);
            var area = Layout.RangeArea;
            float m = HubPlayK.RangeLeaveMargin;
            if (pawn is null || TeamOf(round.Owner) != 1)
            {
                EndRound(round, cancelled: true, reason: "Round cancelled.");
                continue;
            }

            FVector at = pawn.Location;
            if (at.X < area.X0 - m || at.X > area.X1 + m || at.Y < area.Y0 - m || at.Y > area.Y1 + m)
            {
                EndRound(round, cancelled: true, reason: "Round cancelled: you left the range.");
                continue;
            }

            if (now >= round.End)
            {
                EndRound(round, cancelled: false, reason: null);
                continue;
            }

            int up = 0;
            foreach (HubRangeTarget t in round.Targets)
            {
                if (t.Up && now >= t.DownAt)
                {
                    Lower(t, now);
                }

                up += t.Up ? 1 : 0;
            }

            if (up < HubPlayK.TargetsUp && now >= round.NextRise)
            {
                Raise(round, now);
                round.NextRise = now + HubPlayK.TargetGap;
            }

            if (now - round.ScoreSentAt >= HubPlayK.RangeScoreInterval)
            {
                SendScore(round, now);
            }
        }
    }

    private void Raise(RangeRound round, double now)
    {
        HubLayout l = Layout;
        float x0 = l.Backstop + 256f;
        float x1 = Math.Max(x0, l.FiringLine - 512f);
        float y0 = l.Lane.Y0 + 64f;
        float y1 = Math.Max(y0, l.Lane.Y1 - 64f);
        HubRangeTarget? target = null;
        foreach (HubRangeTarget t in round.Targets)
        {
            if (!t.Up)
            {
                target = t;
                break;
            }
        }

        if (target is null)
        {
            target = new HubRangeTarget(_nextTargetId++, round);
            round.Targets.Add(target);
        }

        target.Foot = new FVector(_random.RandRange(x0, x1), _random.RandRange(y0, y1), l.Floor);
        target.Up = true;
        target.DownAt = now + HubPlayK.TargetUpMin + _random.FRand() * (HubPlayK.TargetUpMax - HubPlayK.TargetUpMin);
        _hub.Host.Broadcast(TargetLine(target, true));
    }

    private void Lower(HubRangeTarget t, double now)
    {
        t.Up = false;
        t.Round.NextRise = Math.Max(t.Round.NextRise, now + HubPlayK.TargetGap);
        _hub.Host.Broadcast(TargetLine(t, false));
    }

    private static string TargetLine(HubRangeTarget t, bool up) => HubPlayProtocol.Target(t.Id, t.Round.OwnerId, up, t.Foot);

    private void SendScore(RangeRound round, double now)
    {
        round.ScoreSentAt = now;
        int left = (int)Math.Max(0, Math.Round((round.End - now) * 1000.0));
        Send(round.Owner, HubPlayProtocol.RangeScore(round.Points, round.Hits, round.Shots, round.Heads, left));
    }

    private void EndRound(RangeRound round, bool cancelled, string? reason)
    {
        _rounds.Remove(round);
        double now = Now;
        foreach (HubRangeTarget t in round.Targets)
        {
            if (t.Up)
            {
                Lower(t, now);
            }
        }

        if (cancelled)
        {
            if (reason is not null)
            {
                Send(round.Owner, HubPlayProtocol.RangeEnd(-1, round.Hits, round.Shots, round.Heads, 0, false));
                Send(round.Owner, HubPlayProtocol.Toast(reason));
            }

            return;
        }

        int score = RoundScore(round.Points, round.Hits, round.Shots);
        int accuracy = round.Shots > 0 ? (int)Math.Round(100.0 * round.Hits / round.Shots) : 0;
        string detail = string.Create(CultureInfo.InvariantCulture, $"{accuracy}% {round.Heads}HS");
        HubScore? oldTop = Scores.Range.Top(1) is { Count: > 0 } top ? top[0] : null;
        bool best = score > 0 && Scores.Range.Submit(new HubScore(round.Owner.Name, score, detail, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        int rank = best ? Scores.Range.Rank(round.Owner.Name) : 0;
        Send(round.Owner, HubPlayProtocol.RangeEnd(score, round.Hits, round.Shots, round.Heads, rank, best));
        if (best)
        {
            Scores.Save();
            PublishRangeBoard();
        }

        if (rank == 1 && (oldTop is null || score > oldTop.Value))
        {
            string text = $"{round.Owner.Name} set a new aim range record: {score.ToString(CultureInfo.InvariantCulture)} points!";
            SendNear(Layout.RangeBoard, HubPlayK.FanfareRange, HubPlayProtocol.Fanfare(text), round.Owner);
            _hub.Say(text);
        }
    }

    /// <summary>A round's score: its hit points plus the accuracy bonus (the share of shots that hit times <see cref="HubPlayK.AccuracyBonus"/>).</summary>
    public static int RoundScore(int points, int hits, int shots) =>
        points + (shots > 0 ? (int)Math.Round(HubPlayK.AccuracyBonus * (double)Math.Min(hits, shots) / shots) : 0);

    /// <summary>A rifle shot by <paramref name="shooter"/> (the hit zones' trace with real fire): counts for his round.</summary>
    internal void OnShot(Pawn shooter)
    {
        foreach (RangeRound r in _rounds)
        {
            if (ReferenceEquals(r.Owner.Controller?.Pawn, shooter))
            {
                r.Shots++;
                return;
            }
        }
    }

    /// <summary>A bullet on <paramref name="target"/>: scores for the target's round when its own shooter fired it.</summary>
    internal void OnTargetHit(HubRangeTarget target, Pawn? shooter, bool head)
    {
        RangeRound round = target.Round;
        if (!target.Up || shooter is null || !_rounds.Contains(round) || !ReferenceEquals(round.Owner.Controller?.Pawn, shooter) ||
            !PlayRules.Of(shooter).RangeTargetsTakeHits)
        {
            return;
        }

        double now = Now;
        round.Hits++;
        round.Heads += head ? 1 : 0;
        int points = head ? HubPlayK.HeadPoints : HubPlayK.BodyPoints;
        round.Points += points;
        Lower(target, now);
        SendScore(round, now);
        Send(round.Owner, HubPlayProtocol.Toast(head ? "Headshot +" + points.ToString(CultureInfo.InvariantCulture) : "Hit +" + points.ToString(CultureInfo.InvariantCulture)));
    }

    private void PublishRangeBoard() =>
        PublishBoard(RangeBoardId, HubScores.BoardText("AIM RANGE  best rounds", Scores.Range,
            static s => s.Value.ToString(CultureInfo.InvariantCulture)));
}

/// <summary>
/// A pop-up target of the aim range: never spawned in the level (nothing replicates it; clients draw it from the
/// target lines), only the actor the rifle's trace reports as hit, so the rifle calls its <see cref="TakeDamage"/>
/// as for any other non-pawn actor. Two boxes facing the firing line: the body and, over it, the head.
/// </summary>
public sealed class HubRangeTarget : Actor
{
    internal HubRangeTarget(int id, HubActivities.RangeRound round)
    {
        Id = id;
        Round = round;
        RemoteRole = ENetRole.ROLE_None;
    }

    public int Id { get; }

    internal HubActivities.RangeRound Round { get; }

    internal HubActivities? Activities { get; set; }

    /// <summary>The middle of the target's foot on the floor.</summary>
    public FVector Foot { get; internal set; }

    public bool Up { get; internal set; }

    internal double DownAt { get; set; }

    /// <summary>The pill tags this target answers with: the head and the body (our own numbers; pawns use 1 to 15).</summary>
    public const int HeadTag = 101;

    public const int BodyTag = 102;

    /// <summary>The body's box (min, max), its broad side towards +X (the firing line).</summary>
    public (FVector Min, FVector Max) Body => (
        Foot + new FVector(-HubPlayK.TargetHalfDepth, -HubPlayK.TargetHalfWidth, 0f),
        Foot + new FVector(HubPlayK.TargetHalfDepth, HubPlayK.TargetHalfWidth, HubPlayK.TargetBodyHeight));

    public (FVector Min, FVector Max) Head
    {
        get
        {
            float z0 = HubPlayK.TargetBodyHeight + HubPlayK.TargetHeadGap;
            return (Foot + new FVector(-HubPlayK.TargetHalfDepth, -HubPlayK.TargetHeadHalfWidth, z0),
                Foot + new FVector(HubPlayK.TargetHalfDepth, HubPlayK.TargetHeadHalfWidth, z0 + HubPlayK.TargetHeadHeight));
        }
    }

    protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0)
    {
        Activities?.OnTargetHit(this, EventInstigator, PillTag == HeadTag);
    }
}

/// <summary>
/// The hub server's hit zones: the level's own trace (<see cref="TraceHitZones"/>), then the aim range's raised
/// targets, the nearer hit winning. Rifle shots (real fire from a merc rifle) are counted for the shooter's round.
/// </summary>
internal sealed class HubRangeHitZones(IHitZones inner, HubActivities activities) : IHitZones
{
    public BoneTraceResult TraceBone(Actor Shooter, FVector TraceEnd, FVector TraceStart, bool bRealFire)
    {
        BoneTraceResult result = inner.TraceBone(Shooter, TraceEnd, TraceStart, bRealFire);
        if (activities.Rounds.Count == 0)
        {
            return result;
        }

        if (bRealFire && Shooter is SWeaponDefense { Instigator: { } shooter })
        {
            activities.OnShot(shooter);
        }

        FVector d = TraceEnd - TraceStart;
        float length = d.Size();
        if (length <= 0f)
        {
            return result;
        }

        float best = result.bHit ? (result.HitLocation - TraceStart).Size() / length : 1f;
        HubRangeTarget? hitTarget = null;
        bool head = false;
        foreach (HubActivities.RangeRound round in activities.Rounds)
        {
            foreach (HubRangeTarget t in round.Targets)
            {
                if (!t.Up)
                {
                    continue;
                }

                if (Slab(TraceStart, d, t.Head, out float th) && th < best)
                {
                    best = th;
                    hitTarget = t;
                    head = true;
                }

                if (Slab(TraceStart, d, t.Body, out float tb) && tb < best)
                {
                    best = tb;
                    hitTarget = t;
                    head = false;
                }
            }
        }

        if (hitTarget is null)
        {
            return result;
        }

        hitTarget.Activities = activities;
        return new BoneTraceResult
        {
            Actor = hitTarget,
            bHit = true,
            PillTag = head ? HubRangeTarget.HeadTag : HubRangeTarget.BodyTag,
            HitLocation = TraceStart + d * best,
            HitNormal = new FVector(d.X > 0f ? -1f : 1f, 0f, 0f),
        };
    }

    /// <summary>The ray from <paramref name="o"/> along <paramref name="d"/> (0 to 1) against a box: the entry parameter.</summary>
    private static bool Slab(FVector o, FVector d, (FVector Min, FVector Max) box, out float t)
    {
        float t0 = 0f;
        float t1 = 1f;
        t = 0f;
        for (int axis = 0; axis < 3; axis++)
        {
            float oa = axis == 0 ? o.X : axis == 1 ? o.Y : o.Z;
            float da = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            float lo = axis == 0 ? box.Min.X : axis == 1 ? box.Min.Y : box.Min.Z;
            float hi = axis == 0 ? box.Max.X : axis == 1 ? box.Max.Y : box.Max.Z;
            if (MathF.Abs(da) < 1e-6f)
            {
                if (oa < lo || oa > hi)
                {
                    return false;
                }

                continue;
            }

            float a = (lo - oa) / da;
            float b = (hi - oa) / da;
            if (a > b)
            {
                (a, b) = (b, a);
            }

            t0 = MathF.Max(t0, a);
            t1 = MathF.Min(t1, b);
            if (t0 > t1)
            {
                return false;
            }
        }

        t = t0;
        return true;
    }
}
