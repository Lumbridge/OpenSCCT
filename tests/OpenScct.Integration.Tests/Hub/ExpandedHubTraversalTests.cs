using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Server;
using OpenScct.Server.Hub;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests.Hub;

[Collection(BotMatchCollection.Name)]
public sealed class ExpandedHubTraversalTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("medium", -1)]
    [InlineData("hard", -1)]
    [InlineData("medium", 0)]
    [InlineData("medium", 1)]
    [InlineData("medium", 2)]
    [InlineData("medium", 3)]
    [InlineData("medium", 4)]
    [InlineData("medium", 5)]
    [InlineData("hard", 0)]
    [InlineData("hard", 1)]
    [InlineData("hard", 2)]
    [InlineData("hard", 3)]
    [InlineData("hard", 4)]
    [InlineData("hard", 5)]
    [InlineData("hard", 6)]
    [InlineData("hard", 7)]
    [InlineData("medium", 3, "B")]
    [InlineData("hard", 3, "B")]
    [InlineData("hard", 6, "B")]
    public void SectionIsTraversableWithNormalSpyInputs(string id, int section, string profile = "A")
    {
        string? only = Environment.GetEnvironmentVariable("SVM_HUB_ROUTE_ONLY");
        Assert.SkipWhen(only is not null && only != id + ":" + section, "local route iteration filter");
        string? clips = PlayMapLoader.FindCharsDir(WalkerRig.Maps.FindFolder("Hub")!);
        Assert.SkipWhen(clips is null, "Native course playthroughs need the converted character/root-motion clips.");
        using var run = new PlacedPlayer("Hub", PlaySide.Spy, OpenScct.Net.SimLinkProfile.Get(profile));
        if (!run.ClientController.bFastSpeed) run.Tap("ChangeSpeed");
        string manifest = HubLayout.FindManifest(WalkerRig.Maps.FindFolder("Hub"))!;
        var course = HubLayout.Load(manifest).Courses[id];
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        var definition = json.RootElement.GetProperty("areas").EnumerateArray().Single(a => a.TryGetProperty("courseId", out var value) && value.GetString() == id);
        var steps = definition.GetProperty("playtestRoute").EnumerateArray().Where(s => section < 0 || s.GetProperty("section").GetInt32() == section).ToArray();
        var start = course.Sections[Math.Max(0, section)].Recovery;
        run.Place(start + new FVector(0, 0, 94), course.Sections[Math.Max(0, section)].Yaw);
        var movers = run.Session.Host.World.Level.Actors.OfType<Mover>().Where(m => m.Tag == "HubCourseMover").ToArray();
        foreach (var mover in movers) mover.eventTrigger(run.Session.Host.World.Level.Info, null);
        int total = 0;
        int nextGate = course.Sections[Math.Max(0, section)].FirstGate;
        string label = "start";
        var modes = new HashSet<EPhysics>();
        void Tick()
        {
            if (total % 15 == 0)
                foreach (var mover in movers)
                {
                    mover.SimOldPos = mover.OldPos;
                    mover.SimInterpolate = new FVector(mover.bInterpolating ? mover.PhysAlpha * 100 : 100,
                        Math.Max(.01f, mover.PhysRate) * 100, 256 * mover.PrevKeyNum + mover.KeyNum);
                }
            run.Tick(); total++; modes.Add(run.Client.Physics);
            nextGate = course.AdvanceGates(nextGate, run.Server.Location);
        }
        void Wait(int frames) { for (int f = 0; f < frames; f++) Tick(); }
        void Until(Func<bool> condition, int frames = 1200)
        {
            for (int f = 0; f < frames && !condition(); f++) Tick();
            Assert.True(condition(), $"{id}/{section} {label}: at={run.Client.Location} state={run.ClientController.GetStateName()} modes={string.Join(',', modes)}");
        }
        static FVector Vec(JsonElement j) => new(j[0].GetSingle(), j[1].GetSingle(), j[2].GetSingle());
        static FVector Flat(FVector v) => new(v.X, v.Y, 0);
        void Face(FVector d) => run.ClientController.SetViewRotation(new FRotator(0, (int)(Math.Atan2(d.Y, d.X) * 65536 / (2 * Math.PI)), 0));
        void Stop() { run.Session.Input.Set("BT_Up", false); run.Session.Input.Set("BT_Down", false); run.Session.Input.Set("BT_Left", false); run.Session.Input.Up = 0; }
        void Jump()
        {
            run.Session.Input.Commands.Add(new InputCommand("Jump")); run.Session.Input.Up = 280;
            Tick(); run.Session.Input.Up = 0;
        }
        void Button(string name) { run.Session.Input.Set(name, true); Tick(); run.Session.Input.Set(name, false); }
        void Stand()
        {
            if (run.Client.bIsCrouched) { Button("bCrouch"); Wait(15); }
        }
        void Walk(FVector target, int max = 900, bool settle = true)
        {
            bool arrived = false;
            for (int f = 0; f < max; f++)
            {
                var delta = Flat(target - run.Client.Location);
                if (delta.Size() < 24 && run.Client.Physics == EPhysics.PHYS_Walking && Math.Abs(run.Client.Location.Z - run.Client.CollisionHeight - target.Z) < 28)
                { arrived = true; break; }
                Face(delta - Flat(run.Client.Velocity) * .06f);
                run.Session.Input.Set("BT_Up", true); Tick();
                if (run.Client.Location.Z < target.Z - 900) break;
            }
            Assert.True(arrived, $"{id}/{section} {label}: walk target={target} at={run.Client.Location} state={run.ClientController.GetStateName()} modes={string.Join(',', modes)}");
            if (settle) { Stop(); Wait(8); }
        }
        foreach (var step in steps)
        {
            string kind = step.GetProperty("kind").GetString()!;
            var target = Vec(step.GetProperty("at"));
            label = $"gate {step.GetProperty("gate")} {kind} -> {target}";
            int yaw = step.GetProperty("yaw").GetInt32();
            var forward = new FVector((float)Math.Cos(yaw * 2 * Math.PI / 65536), (float)Math.Sin(yaw * 2 * Math.PI / 65536), 0);
            modes.Clear(); Stand();
            output.WriteLine($"BEGIN {id}/{section} {label} at {run.Client.Location}");
            switch (kind)
            {
                case "walk": Walk(target); break;
                case "jump":
                    Walk(Vec(step.GetProperty("takeoff")), settle: false); Face(forward); Jump();
                    Walk(target); Assert.Contains(EPhysics.PHYS_Falling, modes); break;
                case "ledge":
                    Walk(Vec(step.GetProperty("approach"))); Face(forward);
                    run.Session.Input.Set("BT_Up", true); Wait(12); Jump();
                    Walk(target); Assert.Contains(EPhysics.PHYS_RootMotion, modes); break;
                case "wallkick":
                    Walk(Vec(step.GetProperty("approach"))); Face(forward);
                    run.Session.Input.Set("BT_Up", true); Wait(25);
                    Jump();
                    Walk(target); Assert.Contains(EPhysics.PHYS_RootMotion, modes); break;
                case "pipe":
                case "ladder":
                    Walk(Vec(step.GetProperty("approach"))); Face(forward); Walk(target, 1800);
                    Assert.Contains(kind == "pipe" ? EPhysics.PHYS_OnPipe : EPhysics.PHYS_OnLadder, modes); break;
                case "vault":
                    Walk(Vec(step.GetProperty("approach"))); Face(forward);
                    run.Session.Input.Set("BT_Up", true); Wait(15); Jump(); Walk(target);
                    Assert.Contains(EPhysics.PHYS_RootMotion, modes); break;
                case "roll":
                    Walk(Vec(step.GetProperty("approach")), settle: false); Face(forward); Button("bRoll");
                    bool rolled = run.Client.bIsRolling;
                    for (int f = 0; f < 360 && (Flat(Vec(step.GetProperty("exit")) - run.Client.Location) | forward) > 0; f++)
                    { run.Session.Input.Set("BT_Up", true); Tick(); rolled |= run.Client.bIsRolling; }
                    Stop(); Stand(); Walk(target); Assert.True(rolled, "native roll must execute"); break;
                case "hoh":
                    Walk(Vec(step.GetProperty("approach"))); Face(forward); Wait(15); Jump();
                    Until(() => run.Client.Physics == EPhysics.PHYS_OnPipeH, 180);
                    run.Session.Input.Set("BT_Up", true);
                    Until(() => (Flat(Vec(step.GetProperty("release")) - run.Client.Location) | forward) < 24);
                    Stop(); Button("bCrouch"); Until(() => run.Client.Physics == EPhysics.PHYS_Walking, 180); Stand(); Walk(target); break;
                case "shimmy":
                    Walk(Vec(step.GetProperty("approach"))); Face(new FVector(-forward.Y, forward.X, 0));
                    run.Session.Input.Set("BT_Up", true); Wait(15); Stop(); Wait(10); Jump();
                    Until(() => run.Client.Physics == EPhysics.PHYS_Ledge, 180);
                    run.Session.Input.Set("BT_Left", true);
                    Until(() => (Flat(Vec(step.GetProperty("release")) - run.Client.Location) | forward) < 24, 1800);
                    Stop(); Button("bCrouch"); Until(() => run.Client.Physics == EPhysics.PHYS_Walking, 180); Stand(); Walk(target); break;
                case "fence":
                    Walk(Vec(step.GetProperty("approach"))); Face(forward);
                    run.Session.Input.Set("BT_Up", true);
                    Until(() => (Flat(target - run.Client.Location) | forward) < 315, 600);
                    Stop(); run.Session.Input.Set("BT_Down", true);
                    Until(() => run.Client.Physics == EPhysics.PHYS_Walking, 600);
                    Stop(); Walk(target); Assert.Contains(EPhysics.PHYS_OnVerticalPlane, modes); break;
                case "zip":
                    Walk(Vec(step.GetProperty("approach"))); Face(forward); Jump();
                    run.Session.Input.Set("BT_Up", true);
                    Until(() => run.Client.Physics == EPhysics.PHYS_Linear, 180);
                    Until(() => run.Client.Physics == EPhysics.PHYS_Walking, 900); Stop(); Walk(target); break;
                case "lift":
                case "shuttle":
                    Walk(Vec(step.GetProperty("approach")));
                    var ges = ((OpenScct.Physics.Movement.PhysicsDriver)run.Server.XLevel.PhysicsDriver!).GEs;
                    var attached = Enumerable.Range(0, ges.Count).Select(i => ges[i]).First(g => g.AttachedTo == step.GetProperty("mover").GetString());
                    var mover = (Mover)ges.OwnerOf(attached)!;
                    Until(() => mover.bInterpolating, 2400); Until(() => !mover.bInterpolating && mover.KeyNum == 0, 2400);
                    var clientMover = run.Session.Client.World.Level.Actors.OfType<Mover>().Single(m => m.BasePos == mover.BasePos);
                    Walk(clientMover.Location + new FVector(0, 0, 32)); Assert.IsType<Mover>(run.Client.Base);
                    var rideOffset = run.Client.Location - clientMover.Location;
                    float rideSlip = 0;
                    Until(() =>
                    {
                        rideSlip = Math.Max(rideSlip, (run.Client.Location - clientMover.Location - rideOffset).Size());
                        return !mover.bInterpolating && mover.KeyNum == 1;
                    }, 2400);
                    Assert.InRange(rideSlip, 0, 8f);
                    output.WriteLine($"RIDE {profile} {step.GetProperty("mover")} maximum relative drift {rideSlip:F3}uu");
                    Wait(12);
                    Face(Flat(target - run.Client.Location)); run.Session.Input.Set("BT_Up", true); Jump(); Walk(target); break;
                default: throw new InvalidOperationException(kind);
            }
            output.WriteLine($"PASS {label} {total / 60.0:F1}s");
        }
        string result = $"{id} {(section < 0 ? "complete course" : "section " + (section + 1))}: {steps.Length} route actions, {total / 60.0:F1} seconds of normal input";
        output.WriteLine(result);
        if (section < 0)
        {
            Assert.Equal(course.Gates.Count, nextGate);
            Assert.True(course.Finish.Contains(run.Server.Location));
            var targetTime = definition.GetProperty("cleanRunSeconds");
            Assert.InRange(total / 60.0, targetTime[0].GetDouble(), targetTime[1].GetDouble());
        }
        if (Environment.GetEnvironmentVariable("SVM_HUB_TRAVERSAL_REPORT") is { } report) File.AppendAllText(report, result + "\n");
    }
}
