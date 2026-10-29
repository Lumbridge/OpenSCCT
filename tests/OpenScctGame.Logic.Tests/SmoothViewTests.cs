using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenScct.Server;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScctGame.Cameras;
using OpenScctGame.Play.Session;
using OpenScctGame.Views;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>
/// The player's view when the screen runs faster than the 60 Hz simulation (docs/RENDERING.md, drawing between
/// ticks): the camera, a zoom's FOV and the view model's one-shots move every frame, and a mouse turn is drawn in the
/// frame it arrives in, as the original (which runs its controller once per rendered frame) draws it.
/// </summary>
public sealed class SmoothViewTests
{
    private const int ScreenHz = 144;

    private readonly ITestOutputHelper _output;

    public SmoothViewTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>The frame times of a <paramref name="hz"/> screen over <paramref name="seconds"/>, in microseconds.</summary>
    private static IEnumerable<long> Frames(int hz, double seconds)
    {
        int n = (int)(seconds * hz);
        for (int f = 1; f <= n; f++)
        {
            yield return (long)f * 1_000_000 / hz;
        }
    }

    /// <summary>The tick index and alpha of a frame time, as PlaySession.Update computes them.</summary>
    private static (long Tick, float Alpha) TickAt(long micros)
    {
        double exact = micros * (double)Sim.Hz / 1_000_000.0;
        long tick = (long)Math.Floor(exact);
        return (tick, (float)(exact - tick));
    }

    [Fact]
    public void ScopeZoomFovChangesEveryFrameAt144Hz()
    {
        // spec/equipment/merc-scope.md rule 4, zoom 30 -> 20, per 60 Hz tick: 0.4 s, the FOV held 0.25 s, then
        // target + (previous - target) x time x 5 down to the target.
        var fov = new List<float> { 30f };
        float time = 0.4f + Sim.DeltaTime;
        for (int t = 1; t <= 40; t++)
        {
            float value = fov[^1];
            if (time > 0f)
            {
                time -= Sim.DeltaTime;
                if (time < 0f)
                {
                    value = 20f;
                }
                else if (time < 0.15f)
                {
                    value = 20f + ((30f - 20f) * time * 5f);
                }
            }

            fov.Add(value);
        }

        var drawn = new List<float>();
        var stepped = new List<float>();
        foreach (long micros in Frames(ScreenHz, 0.6))
        {
            (long tick, float alpha) = TickAt(micros);
            if (tick < 1 || tick >= fov.Count)
            {
                continue;
            }

            var a = new ViewSample(tick - 1, default, default, fov[(int)tick - 1], true);
            var b = new ViewSample(tick, default, default, fov[(int)tick], true);
            drawn.Add(ViewBlend.Blend(a, b, alpha).Fov);
            stepped.Add(b.Fov);
        }

        int Stalls(List<float> v)
        {
            int first = v.FindIndex(x => x < 30f), last = v.FindIndex(x => x <= 20f);
            int stalls = 0;
            for (int i = first + 1; i <= last; i++)
            {
                stalls += v[i] == v[i - 1] ? 1 : 0;
            }

            return stalls;
        }

        // The last tick's FOV holds for two or three frames of a 144 Hz screen; the drawn FOV moves in every frame.
        Assert.True(Stalls(stepped) > 5, $"the tick-rate FOV should hold still between ticks ({Stalls(stepped)})");
        Assert.Equal(0, Stalls(drawn));
    }

    [Fact]
    public void ScopeRaiseClipAdvancesEveryFrameAt144Hz()
    {
        // FirstSnipStBg2 at rate 1.6: 0.292 s, counted down once a tick by the simulation.
        var sequence = new OpenScct.Core.Name("FirstSnipStBg2");
        const float full = 7f / 15f / 1.6f - (1f / 15f / 1.6f);
        var remaining = new List<float> { full };
        while (remaining[^1] > 0f)
        {
            remaining.Add(Math.Max(0f, remaining[^1] - Sim.DeltaTime));
        }

        var drawn = new List<float>();
        var raw = new List<float>();
        foreach (long micros in Frames(ScreenHz, 0.5))
        {
            (long tick, float alpha) = TickAt(micros);
            if (tick < 1 || tick >= remaining.Count)
            {
                continue;
            }

            raw.Add(remaining[(int)tick]);
            drawn.Add(OneShotClock.Remaining(true, sequence, remaining[(int)tick - 1], sequence, remaining[(int)tick], full, alpha));
        }

        int Stalls(List<float> v) => v.Zip(v.Skip(1)).Count(p => p.First == p.Second && p.First > 0f);
        Assert.True(Stalls(raw) > 10, "the tick-rate clip time should hold still between ticks");
        Assert.Equal(0, Stalls(drawn));
        for (int i = 1; i < drawn.Count; i++)
        {
            Assert.True(drawn[i] <= drawn[i - 1], "the drawn clip never runs backwards");
        }
    }

    /// <summary>
    /// The cam views' turn the look-ahead draws ahead (s_UseObject.UpdateRotation): a tick turns only past the dead
    /// zone (150 on aTurn), by int(50 x sensitivity x dt x FOV / 110 x aTurn), within the cam's yaw window; during a
    /// steady turn the part of a tick's counts under the dead zone is drawn too.
    /// </summary>
    [Fact]
    public void CamTurnFollowsTheDeadZoneAndTheYawWindow()
    {
        var world = new World(1, OpenScct.Core.Engine.ENetMode.NM_Standalone);
        SPlayerController pc = world.Level.SpawnActor(new SPlayerController(), default, default)!;
        pc.fSensibiliteSouris = 4f;
        pc.DesiredFOV = 110f;
        var cam = new SCamNetwork { iYaw = 0, iPitch = 0 };
        float dt = Sim.DeltaTime;

        Assert.Equal(default, LookAhead.CamTurn(pc, cam, 120f, 0f, dt));
        Assert.Equal(new FRotator(0, (int)(50f * 4f * dt * 120f), 0), LookAhead.CamTurn(pc, cam, 120f, 0f, dt, turning: true));
        Assert.Equal(new FRotator(0, (int)(50f * 4f * dt * 300f), 0), LookAhead.CamTurn(pc, cam, 300f, 0f, dt));

        // A window of 2000 either side of yaw 0 stops the turn at its end.
        cam.YawConstraint = new FVector(0f, -2000f, 2000f);
        cam.iYaw = 1900;
        Assert.Equal(new FRotator(0, 100, 0), LookAhead.CamTurn(pc, cam, 1000f, 0f, dt));
    }

    [Fact]
    public void NewClipIsDrawnFromItsStart()
    {
        var a = new OpenScct.Core.Name("FirstShooStFd2");
        var b = new OpenScct.Core.Name("FirstSnipStBg2");
        Assert.Equal(0.3f, OneShotClock.Remaining(true, a, 0.1f, b, 0.28f, 0.3f, 0f), 4);
        Assert.Equal(0.28f, OneShotClock.Remaining(true, a, 0.1f, b, 0.28f, 0.3f, 1f), 4);
        Assert.Equal(0.28f, OneShotClock.Remaining(false, a, 0.1f, b, 0.28f, 0.3f, 0f), 4);
    }

    /// <summary>
    /// The merc turns with a steady mouse on a 144 Hz screen through the play session (listen server inline, the
    /// client's controller, the calc view). The drawn yaw is compared with the yaw the counts delivered so far make:
    /// the simulation's yaw after its last tick plus the counts not yet simulated times the decoded turn per count
    /// (UpdateRotationAttaquant_Mouse: int(aTurn x sensitivity x dt x 50), aTurn = 1280 x 6 x counts / 640, profile
    /// sensitivity 40 / 10: 40 rotation units a count at 60 Hz). Drawn between the last two ticks the view is one to
    /// two ticks late; with the look-ahead it is within the integer truncation of the turn.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MouseTurnIsDrawnInTheFrameItArrives(bool lookAhead)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("SteeE") is null, "SteeE has not been converted into .content/svmap.");
        const float countsPerFrame = 3f;
        const float unitsPerCount = 40f;
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings { Map = "SteeE", HostMode = PlayHostMode.Inline, Bots = 0, Side = PlaySide.Merc }, clock);
        var look = new LookAhead();
        float pending = 0f;
        session.BeforeClientTick = (s, _) =>
        {
            s.Input.MouseX += pending;
            pending = 0f;
            look.BeforeTick(s.LocalController, s.Mouse, s.Input.MouseX, s.Input.MouseY);
        };
        session.AfterClientTick = (s, _) => look.AfterTick(s.LocalController, s.View);

        var lags = new List<float>();
        var deltas = new List<int>();
        int? lastDrawn = null;
        foreach (long micros in Frames(ScreenHz, 7.0))
        {
            clock.AdvanceTo(micros);
            bool turning = micros > 5_000_000;
            SPlayerController? pc = session.LocalController;
            if (turning)
            {
                pending += countsPerFrame;
            }

            session.Update();
            pc = session.LocalController;
            if (!turning || pc is null || !session.View.Valid)
            {
                continue;
            }

            FRotator offset = lookAhead ? look.Offset(pc, session.Mouse, pending, 0f, session.Alpha) : default;
            int drawn = ViewBlend.Blend(session.PreviousView, session.View, session.Alpha).Rotation.Yaw + offset.Yaw;
            float wanted = session.View.Rotation.Yaw + (pending * unitsPerCount);
            if (micros > 5_300_000)
            {
                lags.Add(FRotator.NormalizeAxis((int)wanted - drawn));
                if (lastDrawn is { } last)
                {
                    deltas.Add(FRotator.NormalizeAxis(drawn - last));
                }
            }

            lastDrawn = drawn;
        }

        Assert.NotEmpty(lags);
        float worst = lags.Max(Math.Abs);
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"look-ahead {lookAhead}: active {look.Active}, lag mean {lags.Average():F1} worst {worst:F1} units, frames without a turn {deltas.Count(d => d == 0)} of {deltas.Count}"));
        if (lookAhead)
        {
            Assert.True(look.Active, "the merc's walking view should be the plain mouse model");
            Assert.True(worst <= 2f, $"the drawn view lags the delivered mouse counts by up to {worst} rotation units");
            Assert.DoesNotContain(0, deltas);
        }
        else
        {
            // The old drawing: a whole tick of turn (3 counts x 2.4 frames x 40) behind, or more.
            Assert.True(lags.Average() > 200f, $"drawing between ticks alone should lag a tick or more (mean {lags.Average():F1})");
        }
    }
}
