using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Movement;
using OpenScct.Physics.Collision;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests.Hub;

[Collection(BotMatchCollection.Name)]
public sealed class HubSceneryTraversalTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("CourseZipZL")]
    [InlineData("ZipLineZL")]
    public void ExistingZipLinesRemainUsableWithPhysicalSupports(string id)
    {
        using var run = new PlacedPlayer("Hub", PlaySide.Spy);
        var driver = (PhysicsDriver)run.Server.XLevel.PhysicsDriver!;
        var ge = Enumerable.Range(0, driver.GEs.Count).Select(i => driver.GEs[i]).Single(g => g.Id == id);
        var high = ge.P1; var low = ge.P0;
        var toward = low - high; toward.Z = 0; toward = toward.SafeNormal();
        int yaw = (int)(Math.Atan2(toward.Y, toward.X) * 65536 / (2 * Math.PI));
        run.Place(high + toward * 60 + new FVector(0, 0, -304 + 90), yaw);
        bool riding = false;
        run.Session.Input.Commands.Add(new InputCommand("Jump"));
        run.Session.Input.Up = 280;
        run.Hold(["BT_Up"], .1, () => riding |= run.Client.Physics == EPhysics.PHYS_Linear);
        run.Session.Input.Up = 0;
        run.Hold(["BT_Up"], 6, () => riding |= run.Client.Physics == EPhysics.PHYS_Linear);
        output.WriteLine($"{id} riding={riding} end={run.Client.Location} state={run.ClientController.GetStateName()}");
        Assert.True(riding, "normal jump must catch the zip line");
        Assert.True(((run.Client.Location - high) | toward) > 1400, "ride through the original span");
    }

    [Theory]
    [InlineData("mediumLift", "A")]
    [InlineData("hardLift", "A")]
    [InlineData("hardShuttle", "A")]
    [InlineData("mediumLift", "B")]
    [InlineData("hardLift", "B")]
    [InlineData("hardShuttle", "B")]
    public void NativeLedgeGrabAttachesToTheMovingPlatform(string id, string link)
    {
        using var run = new PlacedPlayer("Hub", PlaySide.Spy, OpenScct.Net.SimLinkProfile.Get(link));
        var driver = (PhysicsDriver)run.Server.XLevel.PhysicsDriver!;
        var ledges = Enumerable.Range(0, driver.GEs.Count).Select(i => driver.GEs[i]).Where(g => g.AttachedTo == id).ToArray();
        Assert.Equal(4, ledges.Length);
        var mover = (Mover)driver.GEs.OwnerOf(ledges[0])!;
        bool grabbed = false;
        foreach (var ge in ledges)
        {
            var delta = ge.P1 - ge.P0; var d = new FVector(delta.X, delta.Y, 0).SafeNormal();
            var outward = new FVector(d.Y, -d.X, 0);
            var mid = (ge.P0 + ge.P1) * .5f;
            var away = mid - mover.Location; away.Z = 0;
            if ((outward | away) < 0) outward = -outward;
            int yaw = (int)(Math.Atan2(-outward.Y, -outward.X) * 65536 / (2 * Math.PI));
            run.Place(mid + outward * 39 + new FVector(0, 0, -90), yaw);
            run.Ticks(60);
            if (run.Client.Physics != EPhysics.PHYS_Ledge) continue;
            grabbed = true; break;
        }
        Assert.True(grabbed, $"{id}: native grab, last state={run.ClientController.GetStateName()} at={run.Client.Location}");
        Assert.Same(mover, run.Server.Base);
        Assert.Equal(mover.BasePos, Assert.IsAssignableFrom<Mover>(run.Client.Base).BasePos);
        var relative = run.Client.Location - run.Client.Base!.Location;
        mover.eventTrigger(run.Server.XLevel.Info, null);
        for (int frame = 0; frame < 360; frame++)
        {
            if (frame % 15 == 0)
            {
                mover.SimOldPos = mover.OldPos;
                mover.SimInterpolate = new FVector(mover.bInterpolating ? mover.PhysAlpha * 100 : 100,
                    Math.Max(.01f, mover.PhysRate) * 100, 256 * mover.PrevKeyNum + mover.KeyNum);
            }
            run.Tick();
        }
        Assert.True((mover.Location - mover.BasePos).Size() > 100, "carrier moved");
        Assert.Equal(EPhysics.PHYS_Ledge, run.Client.Physics);
        Assert.InRange(FVector.Dist(relative, run.Client.Location - run.Client.Base!.Location), 0, 12);
    }

    [Fact]
    public void ImportedLedgesAreSupportedByTheRuntimeCollisionMesh()
    {
        using var run = new PlacedPlayer("Hub", PlaySide.Spy);
        var driver = (PhysicsDriver)run.Server.XLevel.PhysicsDriver!;
        int checkedEdges = 0;
        foreach (int i in Enumerable.Range(0, driver.GEs.Count))
        {
            var ge = driver.GEs[i];
            if (ge.Kind != GEKind.LG || !ge.Id.StartsWith("Scenery", StringComparison.Ordinal)) continue;
            var delta = ge.P1 - ge.P0;
            var d = new FVector(delta.X, delta.Y, 0).SafeNormal(); var n = new FVector(-d.Y, d.X, 0);
            foreach (float t in new[] { .2f, .5f, .8f })
            {
                var p = ge.P0 + (ge.P1 - ge.P0) * t;
                bool supported = false;
                string traces = "";
                foreach (float side in new[] { -1f, 1f })
                {
                    var at = p + n * (side * 2);
                    bool found = driver.Collision.LineCheck(out var hit, null, at - new FVector(0, 0, 30), at + new FVector(0, 0, 30), default,
                        ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry | ETraceFlags.TRACE_Others);
                    if (found && Math.Abs(hit.Location.Z - p.Z) < 10) supported = true;
                    traces += $" side={side} {hit}";
                }
                Assert.True(supported, $"{ge.Id}: floating rim at {p}; {traces}");
            }
            checkedEdges++;
        }
        Assert.True(checkedEdges > 50);
        output.WriteLine($"{checkedEdges} imported ledges have real collision beneath their span");
    }

    [Theory]
    [InlineData("pine-tree-1.glb")]
    [InlineData("pine-tree-2.glb")]
    [InlineData("birch-tree-1.glb")]
    [InlineData("street_lamp_01.glb")]
    public void ClimbableSceneryUsesNativePipeInputs(string asset)
    {
        using var run = new PlacedPlayer("Hub", PlaySide.Spy);
        using var scene = JsonDocument.Parse(File.ReadAllText(Path.Combine(WalkerRig.Maps.FindFolder("Hub")!, "render", "scenery.json")));
        var item = scene.RootElement.GetProperty("instances").EnumerateArray().First(e => e.GetProperty("asset").GetString() == asset);
        string id = item.GetProperty("ges")[0].GetString()!;
        var driver = (PhysicsDriver)run.Server.XLevel.PhysicsDriver!;
        var ge = Enumerable.Range(0, driver.GEs.Count).Select(i => driver.GEs[i]).Single(g => g.Id == id);
        var normal = (ge.Points[2] - ge.P0).SafeNormal();
        int yaw = (int)(Math.Atan2(-normal.Y, -normal.X) * 65536 / (2 * Math.PI));
        run.Place(ge.P0 + normal * 90 + new FVector(0, 0, 90), yaw);
        bool climbing = false;
        float highest = run.Client.Location.Z;
        run.Hold(["BT_Up"], 8, () =>
        {
            climbing |= run.Client.Physics == EPhysics.PHYS_OnPipe;
            highest = Math.Max(highest, run.Client.Location.Z);
        });
        output.WriteLine($"{asset} {id}: climbed={climbing} highest={highest} now={run.Client.Location} state={run.ClientController.GetStateName()}");
        Assert.True(climbing, "native pipe attach: " + asset);
        Assert.True(highest > ge.P1.Z - 140, "reach the authored pipe top without obstruction: " + asset);
        run.Hold(["BT_Down"], 6);
        run.Ticks(90);
        Assert.InRange(FVector.Dist(run.Client.Location, run.Server.Location), 0, 10);
    }
}
