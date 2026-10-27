using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Cameras;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Smoke;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>
/// Frame pacing (docs/RENDERING.md): the max frame rate settings (Reloaded's fps_spy and fps_merc, with the monitor and
/// unlimited additions), the engine limiter and vertical sync they set, and what is drawn between ticks at 60, 144,
/// 165 and 240 fps: the scene time (materials, lights, emitters, the HUD's fades) and the spy's following camera.
/// </summary>
public sealed class FramePacingTests
{
    private readonly ITestOutputHelper _output;

    public FramePacingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ---- the settings --------------------------------------------------------------------------------------------

    [Fact]
    public void TheCapFollowsTheMonitorByDefaultWithVerticalSyncOff()
    {
        var s = new GameSettings();
        Assert.Equal(DisplayFrameCap.Monitor, s.FpsSpy);
        Assert.Equal(DisplayFrameCap.Monitor, s.FpsMerc);
        Assert.False(s.VSync);   // Default.ini [D3DDrv.D3DRenderDevice] UseVSync=False
        Assert.Equal(144, DisplayFrameCap.EngineMaxFps(s.FpsSpy, 144.0, s.VSync));
        Assert.Equal(165, DisplayFrameCap.EngineMaxFps(s.FpsMerc, 164.9999, s.VSync));
        Assert.Equal(60, DisplayFrameCap.EngineMaxFps(s.FpsMerc, 59.94, s.VSync));
        Assert.Equal(60, DisplayFrameCap.EngineMaxFps(s.FpsMerc, -1.0, s.VSync));   // the screen gave no rate
    }

    [Theory]
    [InlineData(-7, DisplayFrameCap.Monitor)]
    [InlineData(-1, DisplayFrameCap.Monitor)]
    [InlineData(0, DisplayFrameCap.Unlimited)]
    [InlineData(10, 30)]
    [InlineData(144, 144)]
    [InlineData(999, 240)]
    public void CapsKeepReloadedsRangePlusMonitorAndUnlimited(int saved, int kept)
    {
        Assert.Equal(kept, DisplayFrameCap.Clamp(saved));
        Assert.Equal(kept, GameSettings.ClampFps(saved));
        var s = new GameSettings { FpsSpy = saved };
        s.Clamp();
        Assert.Equal(kept, s.FpsSpy);
    }

    [Theory]
    // cap, screen Hz, vsync, Engine.max_fps, the rate the screen shows
    [InlineData(DisplayFrameCap.Monitor, 144.0, false, 144, 144)]
    [InlineData(DisplayFrameCap.Monitor, 144.0, true, 0, 144)]   // the swap paces: no sleep limiter on top
    [InlineData(DisplayFrameCap.Unlimited, 144.0, false, 0, 0)]
    [InlineData(DisplayFrameCap.Unlimited, 144.0, true, 0, 144)]
    [InlineData(60, 144.0, true, 60, 60)]                       // under the screen's rate the limiter still caps
    [InlineData(240, 144.0, true, 0, 144)]
    [InlineData(240, 144.0, false, 240, 240)]
    [InlineData(165, 165.0, false, 165, 165)]
    [InlineData(DisplayFrameCap.Monitor, 239.76, false, 240, 240)]
    public void TheEngineLimiterAndVerticalSyncFollowTheCap(int cap, double hz, bool vsync, int maxFps, int shown)
    {
        Assert.Equal(maxFps, DisplayFrameCap.EngineMaxFps(cap, hz, vsync));
        Assert.Equal(shown, DisplayFrameCap.ExpectedFps(cap, hz, vsync));
    }

    [Fact]
    public void TheOptionsListNamesMonitorUnlimitedAndTheRates()
    {
        string[] list = DisplayFrameCap.Choices(DisplayFrameCap.Monitor, 143.98);
        Assert.Equal("Monitor (144 Hz)", list[0]);
        Assert.Equal("Unlimited", list[1]);
        Assert.Contains("60", list);
        Assert.Contains("144", list);
        Assert.Contains("165", list);
        Assert.Contains("240", list);
        Assert.Equal("30", list[2]);
        Assert.Contains("97", DisplayFrameCap.Choices(97, 60.0));   // a value saved by hand stays listed
        foreach (string item in list)
        {
            int? cap = DisplayFrameCap.Parse(item);
            Assert.NotNull(cap);
            Assert.Equal(item, DisplayFrameCap.Label(cap.Value, 143.98));
        }

        Assert.Equal(DisplayFrameCap.Unlimited, DisplayFrameCap.Parse("Off"));
        Assert.Null(DisplayFrameCap.Parse("fast"));
    }

    [Fact]
    public void AVersionOneFileWithReloadedsFirstRunCapsFollowsTheMonitor()
    {
        // Version 1 wrote 120 and 120 for every player who never chose a cap.
        GameSettings old = GameSettings.FromJson("""{ "version": 1, "fps_spy": 120, "fps_merc": 120, "vsync": true }""");
        Assert.Equal(DisplayFrameCap.Monitor, old.FpsSpy);
        Assert.Equal(DisplayFrameCap.Monitor, old.FpsMerc);
        Assert.True(old.VSync);   // a saved vertical sync is the player's
        Assert.Equal(GameSettings.Version, old.FileVersion);

        GameSettings chosen = GameSettings.FromJson("""{ "version": 1, "fps_spy": 144, "fps_merc": 120 }""");
        Assert.Equal(144, chosen.FpsSpy);
        Assert.Equal(120, chosen.FpsMerc);

        GameSettings now = GameSettings.FromJson("""{ "version": 2, "fps_spy": 120, "fps_merc": 120 }""");
        Assert.Equal(120, now.FpsSpy);

        GameSettings back = GameSettings.FromJson(new GameSettings { FpsSpy = DisplayFrameCap.Unlimited }.ToJson());
        Assert.Equal(DisplayFrameCap.Unlimited, back.FpsSpy);
        Assert.Equal(DisplayFrameCap.Monitor, back.FpsMerc);
    }

    // ---- what is drawn between ticks -----------------------------------------------------------------------------

    /// <summary>The frame starts of a <paramref name="fps"/> screen (0: uneven frames of 1.6 to 2.6 ms), in microseconds.</summary>
    private static List<long> Frames(int fps, double seconds, long from = 0)
    {
        var frames = new List<long>();
        var random = new Random(7);
        long t = from;
        while (t < from + (long)(seconds * 1_000_000))
        {
            t += fps > 0 ? 1_000_000 / fps : 1600 + random.Next(1000);
            frames.Add(t);
        }

        return frames;
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(165)]
    [InlineData(240)]
    [InlineData(0)]
    public void TheSceneTimeMovesEveryFrameWithTheClock(int fps)
    {
        // The play session's tick schedule and alpha (PlaySession.Update) and Level.TimeSeconds after each tick.
        var drawn = new DrawnTime();
        long ticks = 0;
        var times = new List<double>();
        var tickTimes = new List<double>();
        var levelTimes = new List<double>();
        foreach (long micros in Frames(fps, 2.0))
        {
            long due = micros * Sim.Hz / 1_000_000;
            while (ticks < due)
            {
                ticks++;
                drawn.OnTick(ticks * Sim.DeltaTime);
            }

            float alpha = (float)Math.Clamp((micros * (double)Sim.Hz / 1_000_000.0) - ticks, 0.0, 1.0);
            if (ticks < 3)
            {
                // The first ticks have no tick before them to draw from.
                continue;
            }

            times.Add(micros / 1000.0);
            tickTimes.Add(drawn.At(alpha) * 1000.0);
            levelTimes.Add(drawn.Current * 1000.0);
        }

        MotionStats now = FrameMotion.Motion(times, tickTimes, 0.0);
        MotionStats before = FrameMotion.Motion(times, levelTimes, 0.0);
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{fps} fps: drawn time still in {now.Still}, jitter {now.Jitter:F4}; tick time still in {before.Still} of {before.Frames - 1}, jitter {before.Jitter:F2}"));
        Assert.Equal(0, now.Still);
        Assert.True(now.Jitter < 0.01, $"the drawn scene time should advance with the frame clock (jitter {now.Jitter:F3})");
        if (fps is 0 or > 60)
        {
            // Level.TimeSeconds alone moves only at the 60 Hz ticks.
            Assert.True(before.Still > before.Frames / 3, "the tick time should hold still between ticks above 60 fps");
        }
    }

    [Fact]
    public void ATimeGoingBackStartsTheDrawnTimeAgain()
    {
        var drawn = new DrawnTime();
        drawn.OnTick(10f);
        drawn.OnTick(10f + Sim.DeltaTime);
        Assert.Equal(10f + (Sim.DeltaTime / 2f), drawn.At(0.5f), 4);
        drawn.OnTick(0f);
        Assert.Equal(0f, drawn.At(0.3f));
        Assert.Equal(0f, drawn.At(2f));
    }

    [Fact]
    public void TheMotionMeasuresSeeTickRateSteps()
    {
        // A value that moves only at 60 Hz, drawn at 144 fps: still frames between moving ones.
        List<long> frames = Frames(144, 1.0);
        double[] t = frames.Select(f => f / 1000.0).ToArray();
        double[] stepped = frames.Select(f => Math.Floor(f * 60.0 / 1_000_000)).ToArray();
        double[] smooth = frames.Select(f => f * 60.0 / 1_000_000).ToArray();
        MotionStats s = FrameMotion.Motion(t, stepped, 0.0);
        MotionStats m = FrameMotion.Motion(t, smooth, 0.0);
        Assert.True(s.Steps > 50, $"steps {s.Steps}");
        Assert.True(s.Jitter > 0.5);
        Assert.Equal(0, m.Steps);
        Assert.True(m.Jitter < 0.001);
        FrameTimeStats times = FrameMotion.Times(t);
        Assert.Equal(144.0, times.Fps, 0);
        Assert.Equal(0, times.Slow);
    }

    /// <summary>
    /// The spy's following camera at 144 fps, three mouse counts a frame: the controller turns per tick by two or three
    /// frames of counts, so drawing between ticks alone turns the view 1.5 times faster in some frames than others (a
    /// beat the player sees). The look-ahead draws the turn of the counts in the frame they arrive, as the original's
    /// per-frame controller, and orbits the camera round its target so it keeps the spy where the next tick puts him.
    /// </summary>
    [Theory]
    [InlineData(144, false)]
    [InlineData(144, true)]
    [InlineData(165, true)]
    [InlineData(240, true)]
    public void TheSpysFollowingViewTurnsEvenly(int fps, bool lookAhead)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("SteeE") is null, "SteeE has not been converted into .content/svmap.");
        const float countsPerFrame = 3f;
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings { Map = "SteeE", HostMode = PlayHostMode.Inline, Bots = 0, Side = PlaySide.Spy }, clock);
        var look = new LookAhead();
        float pending = 0f;
        session.BeforeClientTick = (s, _) =>
        {
            s.Input.MouseX += pending;
            pending = 0f;
            look.BeforeTick(s.LocalController, s.Mouse, s.Input.MouseX, s.Input.MouseY, s.ViewportSize);
        };
        session.AfterClientTick = (s, _) => look.AfterTick(s.LocalController, s.View);

        var times = new List<double>();
        var yaws = new List<double>();
        var aims = new List<double>();
        double yaw = 0;
        int? last = null;
        foreach (long micros in Frames(fps, 6.5))
        {
            clock.AdvanceTo(micros);
            bool turning = micros > 5_000_000;
            if (turning)
            {
                pending += countsPerFrame;
            }

            session.Update();
            SPlayerController? pc = session.LocalController;
            if (!turning || pc is null || !session.View.Valid || session.LocalPawn is not { } pawn)
            {
                continue;
            }

            FRotator offset = lookAhead ? look.Offset(pc, session.Mouse, pending, 0f, session.ViewportSize, session.Alpha) : default;
            (FVector location, FRotator rotation, float _) = ViewBlend.Blend(session.PreviousView, session.View, session.Alpha);
            FRotator drawnRotation = new(rotation.Pitch + offset.Pitch, rotation.Yaw + offset.Yaw, rotation.Roll);
            float orbit = lookAhead ? look.OrbitDistance(pc) : 0f;
            if (orbit > 0f)
            {
                location += (rotation.Vector() - drawnRotation.Vector()) * orbit;
            }

            if (micros > 5_300_000)
            {
                yaw += last is { } l ? FRotator.NormalizeAxis(drawnRotation.Yaw - l) : 0;
                times.Add(micros / 1000.0);
                yaws.Add(yaw);

                // Where the spy is in the picture: the yaw from the camera to him against the camera's yaw.
                FVector toPawn = pawn.Location - location;
                aims.Add(FRotator.NormalizeAxis((int)(Math.Atan2(toPawn.Y, toPawn.X) * 32768.0 / Math.PI) - drawnRotation.Yaw));
            }

            last = drawnRotation.Yaw;
        }

        MotionStats m = FrameMotion.Motion(times, yaws, 0.0);
        double aimMean = aims.Average();
        double aimSpread = Math.Sqrt(aims.Average(a => (a - aimMean) * (a - aimMean)));
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{fps} fps look-ahead {lookAhead}: active {look.Active}, orbit {look.OrbitDistance(session.LocalController):F0} uu, yaw speed p90/p10 {m.Spread:F2}, jitter {m.Jitter:F3}, still {m.Still}; spy off the view axis {aimMean:F0} +- {aimSpread:F1} units"));
        Assert.True(m.Moved, "the spy's view should turn");
        if (lookAhead)
        {
            Assert.True(look.Active, "the spy's following view should be the plain mouse model (UpdateCamRotation)");
            Assert.True(look.OrbitDistance(session.LocalController) > 0f);
            Assert.True(m.Spread <= 1.1, $"the drawn turn speed varies by {m.Spread:F2}x");
            Assert.Equal(0, m.Still);
            Assert.True(aimSpread < 60.0, $"the spy should stay put in the picture while the view turns ({aimSpread:F1} units)");
        }
        else
        {
            Assert.True(m.Spread >= 1.4, $"drawing between ticks alone should beat at {fps} fps ({m.Spread:F2})");
        }
    }
}
