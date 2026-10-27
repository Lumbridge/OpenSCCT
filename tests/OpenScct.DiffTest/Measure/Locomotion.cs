using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using OpenScct.DiffTest.Model;

namespace OpenScct.DiffTest.Measure;

/// <summary>A measured number with its unit, and the decoded value it is compared with when there is one.</summary>
public sealed record Measured(string Name, double Value, string Unit, double? Expected = null)
{
    public override string ToString() => Expected is double e
        ? string.Create(CultureInfo.InvariantCulture, $"{Name} {Value:0.###} {Unit} (decoded {e:0.###})")
        : string.Create(CultureInfo.InvariantCulture, $"{Name} {Value:0.###} {Unit}");
}

/// <summary>
/// Locomotion numbers read off a run: steady speed, time to speed up and to stop, jump apex and air time, gravity
/// and jump impulse, fall height, fall damage and how long a landing holds the player. Frame windows come from the
/// scenario's <c>measure</c> block. Times are LevelInfo.TimeSeconds differences.
/// </summary>
public static class Locomotion
{
    public const int PhysWalking = 1;
    public const int PhysFalling = 2;

    /// <summary>Mean, minimum and maximum horizontal speed over frames [from, to).</summary>
    public static (double Mean, double Min, double Max) PlateauSpeed(TraceRun run, int from, int to)
    {
        var s = run.Frames.Where(f => f.F >= from && f.F < to).Select(f => f.Speed2D).ToList();
        if (s.Count == 0)
        {
            throw new ArgumentException($"no frames in [{from}, {to})");
        }

        return (s.Average(), s.Min(), s.Max());
    }

    /// <summary>
    /// From the tick of frame <paramref name="from"/> (its input applies there) to the first frame whose horizontal
    /// speed reaches <paramref name="fraction"/> of <paramref name="target"/>: seconds and frames.
    /// </summary>
    public static (double Seconds, int Frames) TimeToReach(TraceRun run, int from, double fraction, double target)
    {
        TraceFrame before = run.At(from - 1) ?? throw new ArgumentException($"no frame {from - 1}");
        foreach (TraceFrame f in run.Frames.Where(f => f.F >= from))
        {
            if (f.Speed2D >= (fraction * target) - 1e-6)
            {
                return (f.T - before.T, f.F - from + 1);
            }
        }

        return (double.NaN, -1);
    }

    /// <summary>From the tick of frame <paramref name="from"/> to the first frame at or below fraction x speed.</summary>
    public static (double Seconds, int Frames) TimeToSlow(TraceRun run, int from, double fraction, double speed)
    {
        TraceFrame before = run.At(from - 1) ?? throw new ArgumentException($"no frame {from - 1}");
        foreach (TraceFrame f in run.Frames.Where(f => f.F >= from))
        {
            if (f.Speed2D <= (fraction * speed) + 1e-6)
            {
                return (f.T - before.T, f.F - from + 1);
            }
        }

        return (double.NaN, -1);
    }

    /// <summary>The largest frame-to-frame change of horizontal speed over dt in a window, uu/s^2 (signed).</summary>
    public static double PeakAcceleration(TraceRun run, int from, int to, bool slowing)
    {
        double best = 0;
        TraceFrame? prev = run.At(from - 1);
        foreach (TraceFrame f in run.Frames.Where(f => f.F >= from && f.F < to))
        {
            if (prev is not null && f.Dt > 0)
            {
                double a = (f.Speed2D - prev.Speed2D) / f.Dt;
                best = slowing ? Math.Min(best, a) : Math.Max(best, a);
            }

            prev = f;
        }

        return best;
    }

    /// <summary>Horizontal distance from the location before frame <paramref name="from"/> to frame <paramref name="to"/>.</summary>
    public static double Distance2D(TraceRun run, int from, int to)
    {
        TraceFrame a = run.At(from - 1) ?? throw new ArgumentException($"no frame {from - 1}");
        TraceFrame b = run.Frames.Last(f => f.F <= to);
        return (b.Loc - a.Loc).Length2D;
    }

    /// <summary>An airborne phase from frame <paramref name="from"/> on: take-off frame (first Physics 2) and landing
    /// frame (first Physics 1 after it).</summary>
    public static (TraceFrame TakeOff, TraceFrame? Landing) AirPhase(TraceRun run, int from)
    {
        TraceFrame takeoff = run.Frames.FirstOrDefault(f => f.F >= from && f.Physics == PhysFalling)
            ?? throw new ArgumentException($"no airborne frame from {from}");
        TraceFrame? landing = run.Frames.FirstOrDefault(f => f.F > takeoff.F && f.Physics == PhysWalking);
        return (takeoff, landing);
    }

    public sealed record AirResult(
        double Apex,
        double AirSeconds,
        int AirFrames,
        double Gravity,
        double LaunchVz,
        double HorizontalDistance,
        double AirSpeedMin,
        double AirSpeedMax);

    /// <summary>
    /// The air phase from frame <paramref name="from"/>: apex above the last ground frame, time and frames with
    /// Physics 2, gravity from a least-squares fit of Velocity.Z against time over the inner air frames, launch
    /// speed (the take-off frame's Velocity.Z with that frame's gravity added back: JumpZ for a jump, 0 for a
    /// walk-off), horizontal distance from take-off to landing.
    /// </summary>
    public static AirResult Air(TraceRun run, int from)
    {
        (TraceFrame takeoff, TraceFrame? landing) = AirPhase(run, from);
        TraceFrame ground = run.At(takeoff.F - 1) ?? run.Start;
        var air = run.Frames.Where(f => f.F >= takeoff.F && (landing is null || f.F < landing.F)).ToList();
        double apex = air.Max(f => f.Loc.Z) - ground.Loc.Z;
        double airSeconds = air.Sum(f => f.Dt);
        var inner = air.Skip(1).Take(Math.Max(0, air.Count - 2)).ToList();
        double g = double.NaN;
        if (inner.Count >= 3)
        {
            double mt = inner.Average(f => f.T);
            double mv = inner.Average(f => f.Vel.Z);
            double num = inner.Sum(f => (f.T - mt) * (f.Vel.Z - mv));
            double den = inner.Sum(f => (f.T - mt) * (f.T - mt));
            g = num / den;
        }

        double launch = double.IsNaN(g) ? takeoff.Vel.Z : takeoff.Vel.Z - (g * takeoff.Dt);
        TraceFrame end = landing ?? air[^1];
        return new AirResult(apex, airSeconds, air.Count, g, launch, (end.Loc - ground.Loc).Length2D,
            air.Min(f => f.Speed2D), air.Max(f => f.Speed2D));
    }

    public sealed record FallResult(double Drop, double ZMax, double LandZ, int Damage, double LandingHoldSeconds,
        string? LandingState);

    /// <summary>
    /// A fall from frame <paramref name="from"/>: height fallen as the game counts it (fZMaxFalling at the landing
    /// minus the landing Location.Z), health lost over the run, and how long the landing state lasted.
    /// </summary>
    public static FallResult Fall(TraceRun run, int from)
    {
        (TraceFrame _, TraceFrame? landing) = AirPhase(run, from);
        if (landing is null)
        {
            throw new ArgumentException("the run does not land");
        }

        int damage = run.Start.Health - run.Frames[^1].Health;
        string? state = landing.ControllerState;
        TraceFrame? after = run.Frames.FirstOrDefault(f => f.F > landing.F && f.ControllerState != state);
        double holdSeconds = after is null ? double.NaN : after.T - landing.T;
        return new FallResult(landing.ZMaxFalling - landing.Loc.Z, landing.ZMaxFalling, landing.Loc.Z, damage,
            holdSeconds, state);
    }

    /// <summary>Every measurement the scenario's <c>measure</c> block asks for.</summary>
    public static IReadOnlyList<Measured> MeasureAll(Scenario scenario, TraceRun run)
    {
        var list = new List<Measured>();
        JsonObject m = scenario.Measure;
        double plateau = double.NaN;
        if (m["plateauSpeed"] is JsonObject ps)
        {
            int from = ps["from"]!.GetValue<int>();
            int to = ps["to"]!.GetValue<int>();
            (double mean, double min, double max) = PlateauSpeed(run, from, to);
            plateau = mean;
            list.Add(new Measured("plateauSpeed", mean, "uu/s", ps["expect"]?.GetValue<double>()));
            list.Add(new Measured("plateauSpread", max - min, "uu/s"));
            list.Add(new Measured("groundSpeed", run.At(to - 1)!.GroundSpeed, "uu/s", ps["expect"]?.GetValue<double>()));
        }

        if (m["accelTime"] is JsonObject at && !double.IsNaN(plateau))
        {
            int from = at["from"]!.GetValue<int>();
            double fraction = at["fraction"]!.GetValue<double>();
            (double s, int n) = TimeToReach(run, from, fraction, plateau);
            (double full, int nFull) = TimeToReach(run, from, 0.999, plateau);
            list.Add(new Measured($"accelTime{fraction * 100:0}", s, "s"));
            list.Add(new Measured($"accelFrames{fraction * 100:0}", n, "frames"));
            list.Add(new Measured("accelTimeFull", full, "s"));
            list.Add(new Measured("accelFramesFull", nFull, "frames"));
            list.Add(new Measured("accelPeak", PeakAcceleration(run, from, from + 30, slowing: false), "uu/s^2"));
        }

        if (m["decelTime"] is JsonObject dt && !double.IsNaN(plateau))
        {
            int from = dt["from"]!.GetValue<int>();
            double fraction = dt["fraction"]!.GetValue<double>();
            (double s, int n) = TimeToSlow(run, from, fraction, plateau);
            (double zero, int nZero) = TimeToSlow(run, from, 0.0, plateau);
            list.Add(new Measured($"decelTime{fraction * 100:0}", s, "s"));
            list.Add(new Measured("decelTimeZero", zero, "s"));
            list.Add(new Measured("decelFramesZero", nZero, "frames"));
            list.Add(new Measured("decelPeak", PeakAcceleration(run, from, from + 30, slowing: true), "uu/s^2"));
            list.Add(new Measured("stopSlide", Distance2D(run, from, from + 30), "uu"));
        }

        if (m["stopDistance"] is JsonObject sd)
        {
            list.Add(new Measured("stopDistance", Distance2D(run, sd["from"]!.GetValue<int>(), sd["to"]!.GetValue<int>()), "uu"));
        }

        if (m["jumpApex"] is JsonObject ja)
        {
            AirResult air = Air(run, ja["from"]!.GetValue<int>());
            list.Add(new Measured("jumpApex", air.Apex, "uu", ja["expect"]?.GetValue<double>()));
            list.Add(new Measured("airtime", air.AirSeconds, "s", (m["airtime"] as JsonObject)?["expect"]?.GetValue<double>()));
            list.Add(new Measured("airFrames", air.AirFrames, "frames"));
            list.Add(new Measured("gravity", air.Gravity, "uu/s^2", -1500.0));
            list.Add(new Measured("launchVz", air.LaunchVz, "uu/s"));
            list.Add(new Measured("jumpDistance", air.HorizontalDistance, "uu"));
            list.Add(new Measured("airSpeedMin", air.AirSpeedMin, "uu/s"));
            list.Add(new Measured("airSpeedMax", air.AirSpeedMax, "uu/s"));
        }

        if (m["fall"] is JsonObject fl)
        {
            int from = 0;
            AirResult air = Air(run, from);
            FallResult fall = Fall(run, from);
            list.Add(new Measured("fallDrop", fall.Drop, "uu", fl["expectDrop"]?.GetValue<double>()));
            list.Add(new Measured("fallAirtime", air.AirSeconds, "s"));
            list.Add(new Measured("gravity", air.Gravity, "uu/s^2", -1500.0));
            list.Add(new Measured("fallLandingSpeedZ", run.At(AirPhase(run, from).Landing!.F - 1)!.Vel.Z, "uu/s"));
            list.Add(new Measured("landingHold", fall.LandingHoldSeconds, "s"));
            if (m["fallDamage"] is JsonObject fd)
            {
                list.Add(new Measured("fallDamage", fall.Damage, "hp", fd["expect"]?.GetValue<double>()));
            }
        }

        return list;
    }

    /// <summary>
    /// The spy's or merc's fall damage for a fall of h uu, as the spec decodes it:
    /// <c>int((h - min) / (max - min) * 100)</c> when h exceeds min (spy 600/1500, merc 450/1000).
    /// </summary>
    public static int SpecFallDamage(string team, double h)
    {
        (double min, double max) = team == "merc" ? (450.0, 1000.0) : (600.0, 1500.0);
        return h > min ? (int)((h - min) / (max - min) * 100.0) : 0;
    }
}
