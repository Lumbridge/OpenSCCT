using System;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Server.Hub;

/// <summary>
/// The spy assault course: a time trial from the start gate through every checkpoint, in order, to the finish. On a
/// raised course (<see cref="HubLayout.CourseFallZ"/>) a spy who falls into the pit is put back on the deck of the
/// last checkpoint he passed, the clock still running.
/// </summary>
public sealed partial class HubActivities
{
    /// <summary>One run in progress.</summary>
    private sealed class Run
    {
        public double Start;
        public int Next;
        public int Falls;
        public double PutBackAt = double.NegativeInfinity;
        public Pawn? Pawn;
    }

    private void TickCourse(PlayerSlot slot, PlayState state, Pawn? pawn, double now)
    {
        if (pawn is null)
        {
            if (state.Run is not null)
            {
                CancelRun(slot, state, "Run cancelled: you respawned.");
            }

            state.InStart = false;
            return;
        }

        HubLayout l = Layout;
        FVector at = pawn.Location;
        bool inStart = l.CourseStart.Contains(at);
        if (TeamOf(slot) != 0)
        {
            if (inStart && now - state.MercNoticeAt > 10.0)
            {
                state.MercNoticeAt = now;
                Send(slot, HubPlayProtocol.Toast("The movement course is for spies. F2 to play spy."));
            }

            if (state.Run is not null)
            {
                CancelRun(slot, state, "Run cancelled: you changed side.");
            }

            state.InStart = inStart;
            return;
        }

        Run? run = state.Run;
        if (run is not null && !ReferenceEquals(run.Pawn, pawn))
        {
            CancelRun(slot, state, "Run cancelled: you respawned.");
            run = null;
        }

        if (inStart)
        {
            // Back in the start gate: the run is reset, and the clock starts again on the way out.
            if (run is not null && now - run.Start > 0.5)
            {
                CancelRun(slot, state, "Back at the start: the clock starts when you leave the gate.");
            }

            state.InStart = true;
            return;
        }

        if (state.InStart && run is null)
        {
            state.InStart = false;
            state.Run = new Run { Start = now, Pawn = pawn };
            Send(slot, HubPlayProtocol.RunStart(l.Checkpoints.Count));
            return;
        }

        state.InStart = false;
        if (run is null)
        {
            return;
        }

        int ms = (int)Math.Round((now - run.Start) * 1000.0);
        var area = l.CourseArea;
        float m = HubPlayK.CourseLeaveMargin;
        if (at.X < area.X0 - m || at.X > area.X1 + m || at.Y < area.Y0 - m || at.Y > area.Y1 + m)
        {
            CancelRun(slot, state, "Run cancelled: you left the course.");
            return;
        }

        if (now - run.Start > HubPlayK.MaxRunSeconds)
        {
            CancelRun(slot, state, "Run cancelled: out of time.");
            return;
        }

        if (run.Next > 0 && at.Z < l.CourseFallZ && now - run.PutBackAt > HubPlayK.FallGraceSeconds)
        {
            PutBack(slot, run, pawn, now);
            return;
        }

        if (run.Next < l.Checkpoints.Count && l.Checkpoints[run.Next].Contains(at))
        {
            run.Next++;
            Send(slot, HubPlayProtocol.RunCheckpoint(run.Next, l.Checkpoints.Count, ms));
        }

        if (!l.CourseFinish.Contains(at))
        {
            return;
        }

        if (run.Next < l.Checkpoints.Count)
        {
            if (now - state.MissNoticeAt > 3.0)
            {
                state.MissNoticeAt = now;
                Send(slot, HubPlayProtocol.Toast(string.Create(CultureInfo.InvariantCulture,
                    $"Missed checkpoint {run.Next + 1} of {l.Checkpoints.Count}: go back for it.")));
            }

            return;
        }

        state.Run = null;
        FinishRun(slot, ms);
    }

    private void FinishRun(PlayerSlot slot, int ms)
    {
        HubScore? oldTop = Scores.Course.Top(1) is { Count: > 0 } t ? t[0] : null;
        bool best = Scores.Course.Submit(new HubScore(slot.Name, ms, string.Empty, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        int rank = best ? Scores.Course.Rank(slot.Name) : 0;
        Send(slot, HubPlayProtocol.RunFinish(ms, rank, best));
        if (best)
        {
            Scores.Save();
            PublishCourseBoard();
        }

        if (rank == 1 && (oldTop is null || ms < oldTop.Value))
        {
            string text = $"{slot.Name} set a new course record: {HubScores.Time(ms)}!";
            SendNear(Layout.CourseFinish.At, HubPlayK.FanfareRange, HubPlayProtocol.Fanfare(text), slot);
            _hub.Say(text);
        }
    }

    /// <summary>A fall into the pit: the spy is set down on the last passed checkpoint's deck, standing still.</summary>
    private void PutBack(PlayerSlot slot, Run run, Pawn pawn, double now)
    {
        run.Falls++;
        run.PutBackAt = now;
        HubGate gate = Layout.Checkpoints[run.Next - 1];
        pawn.Velocity = FVector.Zero;
        pawn.Acceleration = FVector.Zero;
        pawn.SetLocation(gate.At with { Z = gate.At.Z + pawn.CollisionHeight + HubPlayK.FallReturnHeight });
        Send(slot, HubPlayProtocol.RunFall(run.Falls, run.Next));
    }

    private void CancelRun(PlayerSlot slot, PlayState state, string reason)
    {
        state.Run = null;
        Send(slot, HubPlayProtocol.RunCancel(reason));
    }

    private void PublishCourseBoard() =>
        PublishBoard(CourseBoardId, HubScores.BoardText("ASSAULT COURSE  best times", Scores.Course, static s => HubScores.Time(s.Value)));
}
