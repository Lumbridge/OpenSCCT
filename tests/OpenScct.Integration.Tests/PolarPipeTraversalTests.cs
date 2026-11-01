using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>Polar Base's spy-spawn pipes GE19, GE20 and GE110, through the actual session's bound-key input.</summary>
public sealed class PolarPipeTraversalTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(-8277f, 4120f, 250f, 0, false, false)]
    [InlineData(-7403f, 4120f, 250f, 32768, false, false)]
    [InlineData(-7928f, 3563f, 250f, 16384, false, false)]
    [InlineData(-8190f, 4120f, 1246f, 32768, true, false)]
    [InlineData(-7490f, 4120f, 1242f, 0, true, false)]
    [InlineData(-7928f, 3650f, 1242f, 49152, true, false)]
    [InlineData(-8190f, 4120f, 1246f, 32768, true, true)]
    [InlineData(-7490f, 4120f, 1242f, 0, true, true)]
    [InlineData(-7928f, 3650f, 1242f, 49152, true, true)]
    [InlineData(-8190f, 4110f, 1246f, 32768, true, false)]
    [InlineData(-8190f, 4130f, 1246f, 32768, true, false)]
    [InlineData(-7490f, 4110f, 1242f, 0, true, false)]
    [InlineData(-7490f, 4130f, 1242f, 0, true, false)]
    [InlineData(-7938f, 3650f, 1242f, 49152, true, false)]
    [InlineData(-7918f, 3650f, 1242f, 49152, true, false)]
    public void SpyClimbsAndDescendsSpawnPipesWithoutASidewaysAttachSnap(float x, float y, float z, int yaw, bool down, bool crouched)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("PolaE") is null, "PolaE content is not installed.");
        using var run = new PlacedPlayer("PolaE", PlaySide.Spy);
        // The original top-entry native requires walking input (< 4000); a fast run deliberately walks off.
        if (down && run.ClientController.bFastSpeed)
        {
            run.Tap("ChangeSpeed");
        }

        run.Place(new FVector(x, y, z), yaw);
        if (crouched)
        {
            run.Tap("bCrouch");
        }

        output.WriteLine($"placed {run.Client.Location} state {run.ClientController.GetStateName()}");
        var states = new List<string>();
        float? topEntrySideError = null;
        run.Hold(["BT_Up"], 12, () =>
        {
            string s = run.ClientController.GetStateName().ToString();
            if (down && s == "s_OnPipe")
            {
                if (topEntrySideError is null)
                {
                    FVector delta = run.Client.Location - run.Client.ZipP1;
                    FVector n = run.Client.m_geoNormal;
                    topEntrySideError = MathF.Abs(delta.X * -n.Y + delta.Y * n.X);
                }

                run.Session.Input.Set("BT_Up", false);
                run.Session.Input.Set("BT_Down", true);
            }

            if (!down && s == "s_TopPipe")
            {
                run.Session.Input.Set("BT_Up", false);
            }

            if (states.Count == 0 || states[^1] != s)
            {
                states.Add(s);
                output.WriteLine($"{s} {run.Client.Location} phys {run.Client.Physics} zip {run.Client.ZipP1} / {run.Client.ZipP2}");
            }
        });
        output.WriteLine($"end {run.Client.Location} state {run.ClientController.GetStateName()}, server {run.Server.Location}");
        Assert.Contains(down ? "s_downTopPipe" : "s_TopPipe", states);
        if (down)
        {
            Assert.InRange(topEntrySideError ?? float.PositiveInfinity, 0f, 8f);
        }

        Assert.InRange(run.Client.Location.Z, down ? 245f : 1238f, down ? 253f : 1256f);
        Assert.InRange(FVector.Dist(run.Client.Location, run.Server.Location), 0f, 8f);
    }
}
