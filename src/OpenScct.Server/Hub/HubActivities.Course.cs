using System;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Server.Hub;

public sealed partial class HubActivities
{
    private sealed class Run
    {
        public required HubCourseDefinition Course;
        public double Start;
        public int Next;
        public int Section;
        public int Falls;
        public double PutBackAt = double.NegativeInfinity;
        public Pawn? Pawn;
        public FVector Previous;
    }

    private void TickCourse(PlayerSlot slot, PlayState state, Pawn? pawn, double now)
    {
        if (pawn is null || TeamOf(slot) != 0 || (state.Run is { } old && !ReferenceEquals(old.Pawn, pawn)))
        {
            if (state.Run is not null) CancelRun(slot, state, "Run cancelled: respawn or side change.");
            state.InStart = null;
            return;
        }
        var at = pawn.Location;
        Run? run = state.Run;
        var previous = run?.Previous ?? at;
        if (run is not null) run.Previous = at;
        if (run is not null && now - run.Start > run.Course.MaxSeconds)
        {
            CancelRun(slot, state, "Run cancelled: out of time.");
            return;
        }
        // Returning to the first recovery landing must not reset its running clock.
        if (run is null || now - run.PutBackAt > HubPlayK.FallGraceSeconds)
            foreach (var course in Layout.Courses.Values)
            {
                if (run is not null && run.Course.Id != course.Id) continue;
                if (!course.Start.Contains(at)) continue;
                if (run is not null && run.Course.LegacyRecovery && now - run.Start > 0.5)
                    CancelRun(slot, state, "Back at the start: leave the gate to begin again.");
                if (state.Run is null) state.InStart = course.Id;
                return;
            }
        if (run is null && state.InStart is { } armed)
        {
            state.InStart = null;
            var course = Layout.Courses[armed];
            state.Run = new Run { Course = course, Start = now, Pawn = pawn, Previous = at };
            Send(slot, HubPlayProtocol.RunStart(course.ProgressCount, course.Id));
            return;
        }
        state.InStart = null;
        if (run is null) return;
        var c = run.Course;
        int ms = (int)Math.Round((now - run.Start) * 1000);
        var area = c.Area;
        float margin = HubPlayK.CourseLeaveMargin;
        if (at.X < area.X0 - margin || at.X > area.X1 + margin || at.Y < area.Y0 - margin || at.Y > area.Y1 + margin || now - run.Start > c.MaxSeconds)
        {
            CancelRun(slot, state, "Run cancelled: outside the course or out of time.");
            return;
        }
        bool fell = at.Z < c.Sections[run.Section].FallZ;
        if (c.Sections[run.Section].Failures is { } failures)
            foreach (var volume in failures) fell |= volume.Contains(at);
        foreach (var volume in c.Failures) fell |= volume.Contains(at);
        if (fell && (!c.LegacyRecovery || run.Next > 0) && now - run.PutBackAt > HubPlayK.FallGraceSeconds)
        {
            PutBack(slot, run, now);
            return;
        }
        // Sweep normal motion, but never award gates crossed by an administrative relocation.
        if ((at - previous).SizeSquared() > 512 * 512) previous = at;
        int next = c.AdvanceGates(run.Next, previous, at, pawn.CollisionRadius, pawn.CollisionHeight, out var remaining);
        if (next != run.Next)
        {
            run.Next = next;
            int previousSection = run.Section;
            while (run.Section + 1 < c.Sections.Count && c.Sections[run.Section + 1].FirstGate <= run.Next) run.Section++;
            if (previousSection != run.Section)
                Send(slot, HubPlayProtocol.RunCheckpoint(run.Section, c.ProgressCount, ms));
        }
        if (!c.Finish.Crosses(remaining, at, pawn.CollisionRadius, pawn.CollisionHeight, out _)) return;
        if (run.Next < c.Gates.Count)
        {
            if (now - state.MissNoticeAt > 3)
            {
                state.MissNoticeAt = now;
                Send(slot, HubPlayProtocol.Toast("The route is incomplete. Return to your current section."));
            }
            return;
        }
        state.Run = null;
        var table = Scores.ForCourse(c.Id);
        HubScore? oldTop = table.Top(1) is { Count: > 0 } top ? top[0] : null;
        bool best = table.Submit(new(slot.Name, ms, run.Falls + " falls", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        int rank = best ? table.Rank(slot.Name) : 0;
        Send(slot, HubPlayProtocol.RunFinish(ms, rank, best));
        if (best) { Scores.Save(); PublishCourseBoard(c); Send(slot, HubPlayProtocol.PersonalBest(c.Id, ms)); }
        if (rank == 1 && (oldTop is null || ms < oldTop.Value))
        {
            string text = $"{slot.Name} set a {c.Title} record: {HubScores.Time(ms)}!";
            SendNear(c.Finish.At, HubPlayK.FanfareRange, HubPlayProtocol.Fanfare(text), slot);
            _hub.Say(text);
        }
    }

    private void PutBack(PlayerSlot slot, Run run, double now)
    {
        var section = run.Course.Sections[run.Section];
        bool placed = false;
        // Recovery decks have room for several runners. Keep retries on this deck, never on a later route gate.
        foreach (var offset in new FVector[] { default, new(112, 0, 0), new(-112, 0, 0), new(0, 112, 0), new(0, -112, 0) })
        {
            if (!_hub.TryPlace(slot, section.Recovery + offset, section.Yaw)) continue;
            placed = true;
            break;
        }
        if (!placed) return;
        run.Falls++;
        run.PutBackAt = now;
        run.Previous = run.Pawn!.Location;
        run.Next = section.FirstGate;
        Send(slot, HubPlayProtocol.RunFall(run.Falls, run.Section));
    }

    private void CancelRun(PlayerSlot slot, PlayState state, string reason)
    {
        state.Run = null;
        state.InStart = null;
        Send(slot, HubPlayProtocol.RunCancel(reason));
    }

    private void PublishCourseBoard(HubCourseDefinition course) =>
        PublishBoard(course.BoardId, HubScores.BoardText(course.Title + "  best times", Scores.ForCourse(course.Id), static s => HubScores.Time(s.Value)));
}
