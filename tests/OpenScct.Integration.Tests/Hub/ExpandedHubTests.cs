using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScct.Server.Hub;
using Xunit;

namespace OpenScct.Integration.Tests.Hub;

[Collection(BotMatchCollection.Name)]
public sealed class ExpandedHubTests(ITestOutputHelper output)
{
    private sealed class UnusedLauncher : IHubMatchLauncher
    {
        public IHubMatch Start(HubMatchRequest request) => throw new InvalidOperationException("No complete match lineup in this test.");
    }

    private sealed class Rig : IDisposable
    {
        private readonly VirtualClock _clock = new();
        private long _frame;
        private readonly SimLinkNetwork _network;
        private readonly MapCatalog _maps;
        public HubServer Server { get; }
        public List<HubClient> Clients { get; } = [];
        public List<double> Ticks { get; } = [];
        public List<string> Departures { get; } = [];
        public HubLayout Layout => Server.Activities!.Layout;
        public Rig(string profile = "A", MapCatalog? maps = null)
        {
            _maps = maps ?? WalkerRig.Maps;
            _network = new(_clock, 88, SimLinkProfile.Get(profile));
            Server = new(_network.Bind("server"), new HubSettings
            {
                Maps = _maps,
                Classes = WalkerRig.Classes,
                Voice = false,
                MatchLauncher = new UnusedLauncher(),
                Config = HubConfig.Default with { Pads = HubConfig.Default.Pads.Select((p, i) => i == 0 ? p with { Map = MapCatalog.TestBox } : p).ToArray() },
            });
            Server.Host.PlayerLeft += slot => Departures.Add(slot.Name + ": " + slot.Connection.CloseReason);
        }
        public HubClient Join(int team = 0)
        {
            string name = "Visitor" + Clients.Count;
            var client = new HubClient(_network.Bind(name), "server", _clock.NowMicroseconds,
                new HubClientSettings { Maps = _maps, Classes = WalkerRig.Classes, PlayerName = name, Team = team });
            Clients.Add(client);
            client.Play.Clock = () => _clock.NowMicroseconds / 1_000_000.0;
            return client;
        }
        public void Step(double seconds)
        {
            int frames = (int)Math.Ceiling(seconds * Sim.Hz);
            for (int i = 0; i < frames; i++)
            {
                _clock.AdvanceTo(VirtualClock.TickToMicroseconds(++_frame, Sim.Hz));
                var watch = Stopwatch.StartNew();
                Server.Tick(_clock.NowMicroseconds);
                Ticks.Add(watch.Elapsed.TotalMilliseconds);
                foreach (var client in Clients) client.Tick(_clock.NowMicroseconds);
            }
        }
        public void AdvanceIdleTime(int seconds)
        {
            // Five-second transport heartbeats advance the activity clock without simulating hours of render frames.
            for (int i = 0; i < seconds; i += 5)
            {
                _frame += 5 * Sim.Hz;
                _clock.AdvanceTo(VirtualClock.TickToMicroseconds(_frame, Sim.Hz));
                Server.Tick(_clock.NowMicroseconds);
                foreach (var client in Clients) client.Tick(_clock.NowMicroseconds);
            }
            Step(0.2);
        }
        public PlayerSlot Slot(HubClient client) => Server.Players.Single(p => p.Slot.Name == "Visitor" + Clients.IndexOf(client)).Slot;
        public SBasePawn Pawn(HubClient client) => (SBasePawn)Slot(client).Controller!.Pawn!;
        // Administrative placement tests progression, independently of the normal-input traversal tests.
        public void At(HubClient client, FVector deck)
        {
            var pawn = Pawn(client);
            Assert.True(pawn.SetLocation(deck + new FVector(0, 0, 90)), "placement " + deck);
            pawn.Velocity = pawn.Acceleration = default;
            Step(0.08);
        }
        public HubCourseDefinition Start(HubClient client, string id)
        {
            var course = Layout.Courses[id];
            At(client, course.Start.At);
            At(client, course.Start.At + new FVector(0, course.Start.Radius + 60, 0));
            Step(0.4);
            Assert.True(client.Play.Running);
            Assert.Equal(id, client.Play.CourseId);
            return course;
        }
        public void Dispose()
        {
            foreach (var client in Clients) client.Dispose();
            Server.Dispose();
        }
    }

    [Fact]
    public void UseAtTravelStationDoesNotOpenTravelAndStillReadiesMatchQueue()
    {
        using var rig = new Rig();
        var client = rig.Join(); rig.Step(6);
        int travelMenus = 0;
        client.Play.TravelMenuOpened += () => travelMenus++;
        var station = rig.Layout.TeleportDestinations["plaza"];
        rig.At(client, station.Arrivals[0]);
        client.Use(); rig.Step(0.5);
        Assert.Equal(0, travelMenus);
        var pad = rig.Server.Pads!.Find("Pad1")!;
        rig.At(client, pad.Area.Centre); rig.Step(1);
        var entry = Assert.Single(pad.Queue, e => ReferenceEquals(e.Player.Slot, rig.Slot(client)));
        Assert.False(entry.Ready);
        client.Use(); rig.Step(0.5);
        Assert.True(entry.Ready);
        client.Use(); rig.Step(0.5);
        Assert.False(entry.Ready);
        Assert.Equal(0, travelMenus);
    }

    [Fact]
    public void ThreeCoursesHaveIndependentScoresAndLegacyScoresMigrateOnlyForEquivalentRoutes()
    {
        using var rig = new Rig();
        Assert.Equal(new[] { "easy", "hard", "medium" }, rig.Layout.Courses.Keys.Order());
        Assert.Equal(8, rig.Layout.TeleportDestinations.Count);
        Assert.Equal(6, rig.Layout.Courses["medium"].Sections.Count);
        Assert.Equal(8, rig.Layout.Courses["hard"].Sections.Count);
        Assert.Equal(2700, rig.Layout.Courses["medium"].MaxSeconds);
        Assert.Equal(5400, rig.Layout.Courses["hard"].MaxSeconds);
        string file = Path.Combine(Path.GetTempPath(), "hub-scores-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(file, """{"courseLayout":3,"course":[{"name":"Veteran","value":45000,"detail":"","when":1}]}""");
            var scores = HubScores.Open(file);
            scores.KeepCourses(rig.Layout.Courses.Values.Select(c => c.Id == "easy" ? c with { Revision = 3 } : c));
            Assert.Equal(45000, scores.Course.Best("Veteran")!.Value);
            scores.KeepCourses(rig.Layout.Courses.Values);
            Assert.Empty(scores.Course.Bests); // full perimeter GEs deliberately advance Easy to revision 4
            scores.Course.Submit(new("Veteran", 44000, "0 falls", 2));
            scores.ForCourse("medium").Submit(new("Climber", 600000, "0 falls", 2));
            scores.ForCourse("hard").Submit(new("Climber", 1200000, "0 falls", 3));
            scores.Save();
            scores = HubScores.Open(file);
            scores.KeepCourses(rig.Layout.Courses.Values.Select(c => c.Id == "medium" ? c with { Revision = c.Revision + 1 } : c));
            Assert.Empty(scores.ForCourse("medium").Bests);
            Assert.Single(scores.ForCourse("hard").Bests);
            Assert.Single(scores.Course.Bests);
            Assert.Null(scores.Error);
        }
        finally { File.Delete(file); File.Delete(file + ".tmp"); }
    }

    [Theory]
    [InlineData("medium")]
    [InlineData("hard")]
    public void FinishAcceptsOuterCircleAfterLastObstacleWithoutHiddenRunoutGates(string id)
    {
        using var rig = new Rig();
        var client = rig.Join(); rig.Step(5);
        var course = rig.Start(client, id);
        using var json = JsonDocument.Parse(File.ReadAllText(HubLayout.FindManifest(WalkerRig.Maps.FindFolder("Hub"))!));
        var definition = json.RootElement.GetProperty("areas").EnumerateArray().Single(a => a.TryGetProperty("courseId", out var value) && value.GetString() == id);
        int lastObstacle = definition.GetProperty("playtestRoute").EnumerateArray().Last(s => s.GetProperty("kind").GetString() != "walk").GetProperty("gate").GetInt32();
        // Reaching the finish with a missing obstacle must still fail.
        rig.At(client, course.Finish.At);
        Assert.True(client.Play.Running);
        for (int i = 0; i <= lastObstacle; i++) rig.At(client, course.Gates[i].At);
        // The whole visible finish circle counts, including outside the old 140uu hidden gate.
        rig.At(client, course.Finish.At + new FVector(170, 0, 0));
        rig.Step(.3);
        Assert.False(client.Play.Running);
        Assert.Single(rig.Server.Activities!.Scores.ForCourse(id).Bests);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    public void CourseMoversStaySmoothAcrossNetworkRefreshes(string profile)
    {
        using var rig = new Rig(profile);
        var client = rig.Join(); rig.Step(8);
        var movers = client.Session.World.Level.Actors.OfType<Mover>().Where(m => m.Tag == "HubCourseMover").ToArray();
        Assert.Equal(3, movers.Length);
        float backward = 0, largestStep = 0, largestError = 0;
        int samples = 0;
        for (int frame = 0; frame < 30 * Sim.Hz; frame++)
        {
            var before = movers.Select(m => (m.Location, m.PrevKeyNum, m.KeyNum, m.bInterpolating)).ToArray();
            rig.Step(1.0 / Sim.Hz);
            for (int i = 0; i < movers.Length; i++)
            {
                var mover = movers[i];
                var old = before[i];
                if (!old.bInterpolating || !mover.bInterpolating || old.PrevKeyNum != mover.PrevKeyNum || old.KeyNum != mover.KeyNum) continue;
                var direction = (mover.KeyPos[mover.KeyNum] - mover.KeyPos[mover.PrevKeyNum]).SafeNormal();
                var distance = (mover.Location - old.Location) | direction;
                backward = Math.Max(backward, -distance);
                largestStep = Math.Max(largestStep, distance);
                var server = rig.Server.World.Level.Actors.OfType<Mover>().Single(m => m.BasePos == mover.BasePos);
                largestError = Math.Max(largestError, (mover.Location - server.Location).Size());
                samples++;
            }
        }
        output.WriteLine($"{profile}: {samples} samples, backward={backward:F3}, step={largestStep:F3}, error={largestError:F3}");
        Assert.True(samples > 1000);
        Assert.InRange(backward, 0, .01f);
        // Fast lifts travel 3.05uu/tick; allow gradual correction, never a position snap.
        Assert.InRange(largestStep, 0, 5f);
        Assert.InRange(largestError, 0, 100f);
    }

    [Fact]
    public void IndependentRunsRequireOrderedGatesAndRecoveryRestoresTheSection()
    {
        using var rig = new Rig();
        var a = rig.Join(); var b = rig.Join(); rig.Step(5);
        var medium = rig.Start(a, "medium");
        var hard = rig.Start(b, "hard");
        // The revised start is a raised solid apron. Fall beside it, on the
        // exposed ground inside the section's failure volume, not inside it.
        var fall = new FVector(medium.Start.At.X + 640, medium.Start.At.Y, 0);
        rig.At(a, fall);
        Assert.Equal(1, a.Play.Falls);
        Assert.True(a.Play.Running);
        Assert.Equal(0, a.Play.Passed);
        int before = a.Play.RunMs;
        rig.Step(2);
        Assert.True(a.Play.RunMs > before + 1500);
        Assert.Equal("hard", b.Play.CourseId);
        Assert.Equal(0, b.Play.Falls);
        int boundary = medium.Sections[1].FirstGate;
        rig.At(a, medium.Gates[boundary - 1].At); // Out of order cannot earn the section.
        Assert.Equal(0, a.Play.Passed);
        for (int i = 0; i < boundary; i++) rig.At(a, medium.Gates[i].At);
        Assert.Equal(1, a.Play.Passed);
        for (int i = boundary; i < boundary + 4; i++) rig.At(a, medium.Gates[i].At);
        rig.At(a, fall);
        Assert.Equal(2, a.Play.Falls);
        Assert.Equal(1, a.Play.Passed);
        Assert.True((rig.Pawn(a).Location - medium.Sections[1].Recovery).Size() < 240);
        // The four partially completed gates were discarded by recovery.
        for (int i = boundary + 4; i < medium.Gates.Count; i++) rig.At(a, medium.Gates[i].At);
        rig.At(a, medium.Finish.At);
        Assert.True(a.Play.Running);
        Assert.Empty(rig.Server.Activities!.Scores.ForCourse("medium").Bests);
        for (int i = boundary; i < medium.Gates.Count; i++) rig.At(a, medium.Gates[i].At);
        rig.At(a, medium.Finish.At);
        Assert.False(a.Play.Running);
        Assert.Single(rig.Server.Activities.Scores.ForCourse("medium").Bests);
        Assert.True(b.Play.Running);
        Assert.Empty(rig.Server.Activities.Scores.ForCourse("hard").Bests);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void TravelHandlesCrowdsCooldownInvalidIdsAndClearsMovement(string profile)
    {
        using var rig = new Rig(profile);
        var a = rig.Join(); var b = rig.Join(1); rig.Step(8);
        Assert.Equal(8, a.Play.Destinations.Count);
        rig.Start(a, "medium");
        var movement = rig.Server.Host.Movement.EndpointOf(rig.Slot(a).Controller!)!.Sequenced!;
        uint epoch = movement.CommandEpoch;
        a.Travel("missing"); rig.Step(1);
        Assert.True(a.Play.Running);
        Assert.Equal(epoch, movement.CommandEpoch);
        var pawn = rig.Pawn(a);
        pawn.Velocity = new FVector(400, 300, 200);
        pawn.bIsRolling = true;
        pawn.bLyingOnGround = true;
        pawn.SetBase(rig.Server.World.Level.Actors.OfType<Mover>().First(m => m.Tag == "HubCourseMover"));
        var physics = (OpenScct.Physics.Movement.PhysicsDriver)pawn.XLevel.PhysicsDriver!;
        physics.GetState(pawn).TraversalBase = pawn.Base;
        a.Travel("garden"); b.Travel("garden"); rig.Step(1);
        Assert.False(a.Play.Running);
        Assert.True(movement.CommandEpoch > epoch); // Queued input from before placement belongs to the previous epoch.
        Assert.False(pawn.bIsRolling);
        Assert.False(pawn.bLyingOnGround);
        Assert.IsNotType<Mover>(pawn.Base);
        Assert.Null(physics.GetState(pawn).TraversalBase);
        Assert.True(pawn.Velocity.Size() < 1);
        Assert.True((pawn.Location - rig.Pawn(b).Location).Size() >= 90);
        Assert.InRange((pawn.Location - rig.Layout.TeleportDestinations["garden"].Station).Size(), 0, 900);
        foreach (var traveller in new[] { a, b })
        {
            var controller = (SPlayerController)traveller.LocalPlayer!;
            Assert.Equal(rig.Layout.TeleportDestinations["garden"].Yaw, controller.Rotation.Yaw);
            Assert.Equal(controller.Rotation, controller.LocalMyDesiredRotation);
        }
        var garden = pawn.Location;
        a.Travel("hard"); rig.Step(0.5);
        Assert.InRange((pawn.Location - garden).Size(), 0, 20);
        rig.Step(2); a.Travel("hard"); rig.Step(2);
        Assert.InRange((pawn.Location - rig.Layout.TeleportDestinations["hard"].Station).Size(), 0, 900);
        Assert.False(a.Play.Running);
        Assert.InRange((a.LocalPlayer!.Pawn!.Location - pawn.Location).Size(), 0, 40);
        rig.Step(2);
        var pad = rig.Server.Pads!.Find("Pad1")!;
        rig.At(a, pad.Area.Centre);
        rig.Step(1);
        Assert.Contains(pad.Queue, e => ReferenceEquals(e.Player.Slot, rig.Slot(a)));
        rig.Server.Pads.ToggleReady(rig.Slot(a));
        a.Travel("lounge"); rig.Step(2);
        Assert.DoesNotContain(pad.Queue, e => ReferenceEquals(e.Player.Slot, rig.Slot(a)));
        Assert.Null(rig.Server.Players.Single(p => ReferenceEquals(p.Slot, rig.Slot(a))).Pad);
        // A late join must see the three continuously cycling movers at the server's phase.
        var late = rig.Join(); rig.Step(6);
        var movers = rig.Server.World.Level.Actors.OfType<Mover>().Where(m => m.Tag.ToString() == "HubCourseMover").ToList();
        Assert.Equal(3, movers.Count);
        foreach (var mover in movers)
        {
            var seen = late.Session.World.Level.Actors.OfType<Mover>().Single(m => m.BasePos == mover.BasePos);
            Assert.InRange((seen.Location - mover.Location).Size(), 0, 200);
            var driver = (OpenScct.Physics.Movement.PhysicsDriver)seen.XLevel.PhysicsDriver!;
            var ledges = Enumerable.Range(0, driver.GEs.Count).Select(i => driver.GEs[i]).Where(g => ReferenceEquals(driver.GEs.OwnerOf(g), seen)).ToArray();
            Assert.Equal(4, ledges.Length);
            Assert.All(ledges, g => Assert.InRange(Math.Abs(g.P0.Z - seen.Location.Z - 32), 0, .01));
        }
    }

    [Fact]
    public void LongAttemptsContinueAndCancelOnLimitSideChangeRespawnOrRequest()
    {
        using var rig = new Rig();
        var a = rig.Join(); rig.Step(5);
        var medium = rig.Start(a, "medium");
        rig.At(a, medium.Gates[0].At);
        rig.AdvanceIdleTime(1200);
        Assert.True(a.Play.Running);
        Assert.InRange(a.Play.RunMs, 1200000, 1210000);
        rig.AdvanceIdleTime(1505);
        Assert.False(a.Play.Running);
        var hard = rig.Start(a, "hard");
        rig.At(a, hard.Gates[0].At);
        rig.AdvanceIdleTime(3600);
        Assert.True(a.Play.Running);
        rig.AdvanceIdleTime(1810);
        Assert.False(a.Play.Running);
        rig.Start(a, "medium"); a.RequestSide(1); rig.Step(3);
        Assert.False(a.Play.Running);
        a.RequestSide(0); rig.Step(3);
        rig.Start(a, "hard"); a.Respawn(); rig.Step(3);
        Assert.False(a.Play.Running);
        rig.Start(a, "medium"); a.CancelActivity(); rig.Step(1);
        Assert.False(a.Play.Running);
    }

    [Fact]
    public void LongTopTenBoardsReachCurrentAndLateJoiningClientsWithoutDroppingRows()
    {
        using var rig = new Rig();
        var first = rig.Join(); rig.Step(5);
        var table = rig.Server.Activities!.Scores.ForCourse("hard");
        for (int i = 0; i < 10; i++) table.Submit(new HubScore("LongPlayerName" + i + "界界界界", 900000 + i, "12 falls", i));
        string expected = HubScores.BoardText("Hard - Frozen Observatory  best times", table, s => HubScores.Time(s.Value));
        rig.Server.Activities.PublishBoard("CourseBoard.hard", expected);
        rig.Step(2);
        Assert.Equal(expected, string.Join('\n', first.Play.Boards["CourseBoard.hard"].Lines));
        var late = rig.Join(); rig.Step(5);
        Assert.Equal(expected, string.Join('\n', late.Play.Boards["CourseBoard.hard"].Lines));
        Assert.Equal(11, late.Play.Boards["CourseBoard.hard"].Lines.Length);
    }

    [Fact]
    public void BlockedDestinationDoesNotCancelAnAttempt()
    {
        using var rig = new Rig();
        var runner = rig.Join();
        var blockers = Enumerable.Range(0, 20).Select(_ => rig.Join()).ToArray();
        rig.Step(8);
        var destination = rig.Layout.TeleportDestinations["garden"];
        for (int i = 0; i < blockers.Length; i++) rig.At(blockers[i], destination.Arrivals[i]);
        rig.Start(runner, "medium");
        runner.Travel("garden"); rig.Step(1);
        Assert.True(runner.Play.Running);
        Assert.Equal("medium", runner.Play.CourseId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FiftyPlayersCanTravelToSeparateDistricts(bool baseline)
    {
        string? baselineRoot = Environment.GetEnvironmentVariable("SVM_HUB_BASELINE_ROOT");
        Assert.SkipWhen(baseline && baselineRoot is null, "Set SVM_HUB_BASELINE_ROOT to a layout-v3 fixture root for the comparison.");
        long before = GC.GetTotalMemory(true);
        var loading = Stopwatch.StartNew();
        MapCatalog? maps = baseline ? new MapCatalog(new[] { baselineRoot! }.Concat(WalkerRig.Maps.Roots).ToArray()) : null;
        using var rig = new Rig(maps: maps);
        Assert.Equal(baseline ? 1 : 3, rig.Layout.Courses.Count);
        void Report(string text)
        {
            text = (baseline ? "baseline " : "expanded ") + text;
            output.WriteLine(text);
            if (Environment.GetEnvironmentVariable("SVM_HUB_SCALE_REPORT") is { } report) File.AppendAllText(report, text + "\n");
        }
        Report($"Hub server load {loading.Elapsed.TotalMilliseconds:F0} ms");
        for (int i = 0; i < 50; i++) { rig.Join(i % 2); if (i % 10 == 9) rig.Step(1); }
        rig.Step(8);
        rig.Ticks.Clear(); rig.Step(8);
        double[] crowded = rig.Ticks.Order().ToArray();
        Report($"50 concentrated visitors: tick p50 {crowded[crowded.Length / 2]:F2} ms, p99 {crowded[(int)(crowded.Length * .99)]:F2} ms; managed memory delta {(GC.GetTotalMemory(false) - before) / 1048576:F0} MiB (server plus 50 clients)");
        Assert.True(rig.Server.Players.Count == 50, string.Join("; ", rig.Departures));
        if (baseline) return;
        string[] destinations = rig.Layout.TeleportDestinations.Keys.ToArray();
        foreach (var client in rig.Clients) client.Travel(destinations[rig.Clients.IndexOf(client) % destinations.Length]);
        rig.Step(4);
        Assert.Equal(50, rig.Server.Players.Count);
        foreach (var client in rig.Clients)
        {
            var destination = rig.Layout.TeleportDestinations[destinations[rig.Clients.IndexOf(client) % destinations.Length]];
            Assert.InRange((rig.Pawn(client).Location - destination.Station).Size(), 0, 1000);
        }
        rig.Ticks.Clear(); rig.Step(8);
        double[] ticks = rig.Ticks.Order().ToArray();
        Report($"50 spread visitors: tick p50 {ticks[ticks.Length / 2]:F2} ms, p99 {ticks[(int)(ticks.Length * .99)]:F2} ms; managed memory delta {(GC.GetTotalMemory(false) - before) / 1048576:F0} MiB (server plus 50 clients)");
        Assert.False(rig.Server.Game.bGameEnded);
    }
}
