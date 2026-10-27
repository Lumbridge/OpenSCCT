using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenScct.DiffTest.Model;
using Xunit;

namespace OpenScct.DiffTest.Tests;

public sealed class TraceFileTests
{
    // One frame line as the recorder (re/tools/difftest/record.py) writes it.
    private const string RecorderLines = """
        {"format":"svm.trace/1","scenario":{"id":"movement.synthetic","sha256":"00"},"run":2,"team":"spy"}
        {"f":-1,"t":10.0986,"dt":0.016647033,"wall":0.0,"loc":[-492.0,-2032.0,-1055.0],"vel":[0.0,0.0,0.0],"acc":[0.0,0.0,0.0],"rot":[0,16384,0],"crot":[0,16384,0],"phys":1,"ps":"SPawnAttaque_1Mesh","cs":"s_PlayerWalkingSt0H","crouch":0,"roll":0,"hp":100,"gs":0.0,"ch":87.5,"zmax":0.0,"jn":0,"fwd":0.0,"str":0.0,"npm":0.0,"imove":0,"fast":1,"cwr":0,"pj":0,"in":{},"anim":[[0,"WaitStAlFd0",0.050091773,1.0,1.0,0.9983744]],"late":0,"gap":0,"tries":1}
        {"f":0,"t":10.1153,"dt":0.016655866,"wall":0.01669,"loc":[-492.0,-2031.0835,-1055.0],"vel":[-2.4E-06,54.97,0.0],"acc":[0.0,6000.0,0.0],"rot":[0,81919,0],"crot":[0,16384,0],"phys":1,"ps":"SPawnAttaque_1Mesh","cs":"s_PlayerWalkingSt0H","crouch":0,"roll":0,"hp":100,"gs":423.5,"ch":87.5,"zmax":0.0,"jn":0,"fwd":6600.0,"str":0.0,"npm":1.0,"imove":20,"fast":1,"cwr":0,"pj":0,"in":{"BT_Up":1,"jump":1,"view":[0,16384]},"anim":[[0,"WaitStAlFd0",0.066747636,1.0,1.0,0.9983744],[5,"WalkStAlFd0",0.0,0.8333333,0.066623464]],"late":1,"gap":0,"tries":2,"unstable":1}
        {"end":true,"frames":2,"torn":0,"gaps":0}
        """;

    [Fact]
    public void ReadsTheRecorderLayout()
    {
        TraceRun run = TraceFile.Read(new StringReader(RecorderLines), "synthetic");
        Assert.Equal("movement.synthetic", run.ScenarioId);
        Assert.Equal(2, run.RunIndex);
        Assert.Equal(2, run.Frames.Count);
        Assert.NotNull(run.Footer);
        TraceFrame f = run.Frames[1];
        Assert.Equal(0, f.F);
        Assert.Equal(-2031.0835, f.Loc.Y, 4);
        Assert.Equal(54.97, f.Vel.Y, 4);
        Assert.Equal(81919, f.Rot.Yaw);
        Assert.Equal(16383, f.Rot.YawMasked);
        Assert.Equal("s_PlayerWalkingSt0H", f.ControllerState);
        Assert.Equal(1, f.Input.Button("BT_Up"));
        Assert.True(f.Input.Jump);
        Assert.Equal(new Rot3(0, 16384, 0), f.Input.View);
        Assert.True(f.Late);
        Assert.True(f.Unstable);
        Assert.Equal(2, f.Anim.Count);
        Assert.Equal("WalkStAlFd0", f.Anim[1].Sequence);
        Assert.Null(f.Anim[1].Tween);
        Assert.Equal(0.9983744, run.Frames[0].Anim[0].Tween!.Value, 6);
        Assert.Equal(-1, run.Start.F);
        Assert.Single(run.Played);
    }

    [Fact]
    public void RejectsOtherFormats()
    {
        Assert.Throws<InvalidDataException>(() => TraceFile.Read(new StringReader("{\"format\":\"other/1\"}\n")));
    }

    [Fact]
    public void WriteThenReadKeepsFloat32Values()
    {
        TraceRun run = Synthetic.Walk("movement.synthetic", 1, 30, 423.5, 3300, 20, k => 1.0 / 60.0 + (k % 3 * 1e-5));
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"svm-difftest-{Guid.NewGuid():N}.jsonl.gz");
        try
        {
            TraceFile.Write(path, run.Header, run.Frames);
            TraceRun back = TraceFile.Read(path);
            Assert.Equal(run.Frames.Count, back.Frames.Count);
            foreach ((TraceFrame a, TraceFrame b) in run.Frames.Zip(back.Frames))
            {
                Assert.Equal(a.F, b.F);
                Assert.Equal((float)a.T, (float)b.T);
                Assert.Equal((float)a.Loc.Y, (float)b.Loc.Y);
                Assert.Equal((float)a.Vel.Y, (float)b.Vel.Y);
                Assert.Equal(a.Input.Button("BT_Up"), b.Input.Button("BT_Up"));
                Assert.Equal(a.ControllerState, b.ControllerState);
            }

            Assert.Equal(run.Frames.Count, back.Footer!["frames"]!.GetValue<int>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FrameInputDefaultsToNothingHeld()
    {
        Assert.Equal(0, FrameInput.None.Button("BT_Up"));
        var input = new FrameInput(new Dictionary<string, int> { ["BT_Left"] = 1 }, false, null, true);
        Assert.Equal(1, input.Button("BT_Left"));
        Assert.True(input.FastSpeed);
    }
}
